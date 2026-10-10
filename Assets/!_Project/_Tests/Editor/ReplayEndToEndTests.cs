using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using Kehai.Eval;
using Kehai.Replay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

// The 3D replay, end to end: record a few seconds of a bot shift in the real store, open the
// recording as a replay, and check the store is being played back rather than played: the game
// switched off, everyone where the recording says, every camera somewhere sensible, a picture
// on screen and her mind on its own layer. Then leave, and the store is a game again. Slow
// (it loads the store twice), so it deletes what it recorded.
public class ReplayEndToEndTests
{
    const float RecordSeconds = 12f;

    sealed class Run
    {
        public string Path;
    }

    [UnityTest, Timeout(900000)]
    public IEnumerator ABotShift_PlaysBackAsAReplay()
    {
        string scene = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
        if (string.IsNullOrEmpty(scene) || !File.Exists(scene)) Assert.Ignore("the store scene isn't in this project");
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        yield return new EnterPlayMode();

        // Play mode reloaded the scripts: nothing a lambda captures is declared above the yield.
        string folder = ShiftRecorder.Folder;
        var before = new HashSet<string>(Directory.Exists(folder) ? Directory.GetFiles(folder) : new string[0]);
        var run = new Run();

        KehaiEnv env = KehaiEnv.Ensure();
        var config = new EnvConfig
        {
            seed = 777, rung = "F", shiftSeconds = 90f, fps = 15, render = false, freshLedger = true,
            startShift = 3, agent = "efficient", syntheticBlinks = true
        };
        env.StartCoroutine(PlayOneShift(env, config));
        float deadline = Time.realtimeSinceStartup + 600f;
        while (run.Path == null && Time.realtimeSinceStartup < deadline)
        {
            ReplayRecorder rec = ReplayRecorder.Instance;
            if (rec != null && rec.Recording && rec.LastTickTime >= RecordSeconds) run.Path = rec.EndRecording();
            yield return null;
        }

        try
        {
            Assert.IsNotNull(run.Path, "no shift was recorded in time");

            // The bot stops; the store reloads as the replay.
            env.StopAllCoroutines();
            Object.Destroy(env.gameObject);
            if (ShiftRecorder.Instance != null) ShiftRecorder.Instance.enabled = false;
            Time.timeScale = 1f;
            Time.captureFramerate = 0;
            yield return null;
            ReplayMode.Open(run.Path);

            deadline = Time.realtimeSinceStartup + 180f;
            while ((ReplayPlayer.Instance == null || !ReplayPlayer.Instance.Ready) && ReplayPlayer.Instance?.Error == null && Time.realtimeSinceStartup < deadline)
                yield return null;
            ReplayPlayer p = ReplayPlayer.Instance;
            Assert.IsNotNull(p, "the replay didn't open");
            Assert.IsNull(p.Error, "the replay failed: " + p.Error);
            Assert.IsTrue(p.Ready);
            p.Playing = false;
            yield return null;

            // Played back, not played.
            Assert.IsNull(Object.FindAnyObjectByType<KarenBrain>(), "Karen's brain isn't running in a replay");
            Assert.IsFalse(Object.FindAnyObjectByType<ShiftManager>().enabled, "the shift isn't running");
            Assert.AreEqual(SimulationMode.Script, Physics.simulationMode, "nothing simulates");
            Assert.IsFalse(Object.FindObjectsByType<NavMeshAgent>().Any(a => a.enabled), "nobody walks by themselves");
            Assert.IsFalse(Object.FindObjectsByType<CustomerSpawner>().Any(s => s.enabled), "nobody new comes in");

            ReplayStage stage = p.Stage;
            int shelves = stage.Actors.Count(a => a.FromScene && a.Entity.Kind == KrecKind.ShelfUnit);
            Assert.Greater(shelves, 100, "the store's own shelf units are driven: " + stage.Summary);
            Assert.GreaterOrEqual(stage.KarenId, 0, "Karen has a puppet");
            Assert.GreaterOrEqual(stage.PlayerId, 0, "you have a body");
            Debug.Log("Replay end to end: " + stage.Summary);

            // Everyone where the recording says, forwards and backwards.
            float worst = 0f;
            int checkedPoses = 0;
            foreach (float at in new[] { 1f, 9f, 4f, 11f, 0.5f })
            {
                float t = stage.Start + at;
                p.Seek(t);
                foreach (ReplayStage.Actor a in stage.Actors)
                {
                    if (a.Entity.Kind != KrecKind.Karen && a.Entity.Kind != KrecKind.Customer && a.Entity.Kind != KrecKind.ShelfUnit) continue;
                    if (!p.Data.TryPose(a.Entity.Id, t, out EntitySample pose) || !pose.Visible) continue;
                    Assert.IsTrue(a.Root.activeInHierarchy, $"{a.Entity.Kind} {a.Entity.Id} is shown at {at} s");
                    worst = Mathf.Max(worst, Vector3.Distance(a.Transform.position, pose.Position));
                    checkedPoses++;
                }
                if (p.Data.TryPose(stage.PlayerId, t, out EntitySample me))
                {
                    Vector3 body = stage.Body.transform.position;
                    worst = Mathf.Max(worst, Vector2.Distance(new Vector2(body.x, body.z), new Vector2(me.Position.x, me.Position.z)));
                    Assert.AreEqual(stage.FloorY, body.y, 0.3f, "your body stands on the floor");
                    checkedPoses++;
                }
            }
            Assert.Greater(checkedPoses, 5);
            Assert.Less(worst, 0.01f, $"everyone within a centimetre of the recording ({checkedPoses} poses)");

            // Every camera somewhere sensible, near what it follows.
            float mid = stage.Start + RecordSeconds * 0.5f;
            p.Data.TryPose(stage.KarenId, mid, out EntitySample her);
            foreach (ShotPreset preset in new[] { ShotPreset.Pov, ShotPreset.Cctv, ShotPreset.Chase, ShotPreset.Orbit, ShotPreset.TopDown })
            {
                p.Cameras.Use(preset);
                p.ApplyAt(mid, 1f / 60f, false);
                Vector3 c = stage.Camera.transform.position;
                Assert.IsFalse(float.IsNaN(c.x) || float.IsInfinity(c.x), preset + " is somewhere");
                if (preset != ShotPreset.Pov) Assert.Less(Vector3.Distance(c, her.Position), 40f, preset + " is near her");
            }
            if (Application.isBatchMode) Assert.AreEqual(0f, AudioListener.volume, "an unattended run is never heard");

            // Where she is in the picture, as a shot's track records it: in frame for the
            // cameras that follow her.
            foreach (ShotPreset preset in new[] { ShotPreset.Chase, ShotPreset.Orbit, ShotPreset.TopDown })
            {
                p.Cameras.Subject = KrecKind.Karen;
                p.Cameras.Use(preset);
                p.ApplyAt(mid, 1f / 60f, false);
                double[] seen = ShotTrack.Project(stage.Camera, her.Position + Vector3.up * ShotTrack.MiddleHeight(true));
                Assert.IsNotNull(seen, preset + " has her in the picture");
                Assert.That(seen[0], Is.InRange(0.05, 0.95), preset + " across");
                Assert.That(seen[1], Is.InRange(0.05, 0.95), preset + " down");
                Assert.Greater(seen[2], 0.005, preset + " shows her bigger than a dot");
            }
            p.Data.TryCamera(mid, out CameraSample eyes);
            p.Cameras.Use(ShotPreset.Pov);
            p.ApplyAt(mid, 1f / 60f, false);
            Assert.Less(Vector3.Distance(stage.Camera.transform.position, eyes.Position), 0.01f, "your eyes are where they were");

            // A path from three keyframes plays back through them.
            var path = new ShotPath();
            foreach (float at in new[] { 2f, 5f, 8f }) { p.Cameras.Use(ShotPreset.Orbit); p.ApplyAt(stage.Start + at, 0f, false); path.Add(p.Cameras.KeyNow(stage.Start + at)); }
            p.Cameras.Path = path;
            p.Cameras.Use(ShotPreset.Path);
            p.ApplyAt(stage.Start + 5f, 0f, false);
            Assert.Less(Vector3.Distance(stage.Camera.transform.position, path.Keys[1].position), 0.01f, "the path goes through its keys");

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                // A picture of the store, and her mind alone with a transparent background.
                p.Cameras.Use(ShotPreset.Chase);
                p.Mind.Shown = MindLayer.None;
                p.ApplyAt(mid, 0f, false);
                Color32[] store = Render(stage.Camera, ~0, false);
                Assert.Greater(store.Select(c => c.r / 16 * 256 + c.g / 16 * 16 + c.b / 16).Distinct().Count(), 40, "the store is drawn");

                p.Mind.Shown = MindLayer.All;
                p.ApplyAt(mid, 0f, false);
                Color32[] mind = Render(stage.Camera, 1 << ReplayLook.MindLayer, true);
                Assert.Greater(mind.Count(c => c.a > 0), 100, "her mind is drawn");
                Assert.Greater(mind.Count(c => c.a == 0), mind.Length / 10, "and nothing else");
            }

            float[] sound = ReplaySound.Mix(p.Data, stage.Start, stage.Start + RecordSeconds, t => new Pose(stage.Camera.transform.position, stage.Camera.transform.rotation));
            Assert.Greater(sound.Count(s => Mathf.Abs(s) > 0.01f), 1000, "the shift made some noise");

            // Leaving: the store is a game again.
            ReplayMode.Leave();
            deadline = Time.realtimeSinceStartup + 120f;
            while (KarenBrain.Instance == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(KarenBrain.Instance, "back to the game");
            Assert.IsNull(ReplayPlayer.Instance);
            Assert.AreNotEqual(SimulationMode.Script, Physics.simulationMode, "physics runs again");
        }
        finally
        {
            if (Directory.Exists(folder))
                foreach (string file in Directory.GetFiles(folder))
                    if (!before.Contains(file)) File.Delete(file);
        }

        yield return new ExitPlayMode();
    }

    static Color32[] Render(Camera source, int mask, bool transparent)
    {
        var go = new GameObject("Test camera");
        Camera cam = go.AddComponent<Camera>();
        cam.CopyFrom(source);
        cam.enabled = false;
        cam.cullingMask = mask;
        if (transparent)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
        }
        var rt = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var tex = new Texture2D(320, 180, TextureFormat.RGBA32, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
        RenderTexture.active = null;
        Color32[] pixels = tex.GetPixels32();
        cam.targetTexture = null;
        Object.Destroy(go);
        Object.Destroy(rt);
        Object.Destroy(tex);
        return pixels;
    }

    static IEnumerator PlayOneShift(KehaiEnv env, EnvConfig config)
    {
        yield return env.ResetEpisode(config);
        var bot = new SimulatedPlayer(env, PlayerProfile.Efficient, 99);
        yield return bot.PlayShift();
    }
}
