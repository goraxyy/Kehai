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
using UnityEngine.TestTools;

// The replay recorder, end to end: record ten seconds of a bot shift in the real store, read
// the file back, and every position the player, Karen and the customers really had at each
// tick comes back within a centimetre. Slow (it loads the store and plays a shift), so it
// cleans up the shift record it leaves behind.
public class ReplayRoundTripTests
{
    const float RecordSeconds = 10f;
    const float Tolerance = 0.01f;

    [UnityTest, Timeout(900000)]
    public IEnumerator TenSecondsOfABotShift_ComeBackWithinACentimetre()
    {
        string scene = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
        if (string.IsNullOrEmpty(scene) || !File.Exists(scene)) Assert.Ignore("the store scene isn't in this project");
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        yield return new EnterPlayMode();

        // Everything from here on lives in play mode. Entering it reloaded the scripts and the
        // test resumed after the yield, so nothing a lambda captures may be declared above it:
        // the truth is kept by an object instead.
        string folder = ShiftRecorder.Folder;
        var before = new HashSet<string>(Directory.Exists(folder) ? Directory.GetFiles(folder) : new string[0]);
        var truth = new Truth();
        string path = null;

        KehaiEnv env = KehaiEnv.Ensure();
        var config = new EnvConfig
        {
            seed = 4242, rung = "F", shiftSeconds = 90f, fps = 15, render = false, freshLedger = true,
            startShift = 3, agent = "efficient", syntheticBlinks = true
        };
        env.StartCoroutine(PlayOneShift(env, config));

        float deadline = Time.realtimeSinceStartup + 600f;
        while (path == null && Time.realtimeSinceStartup < deadline)
        {
            ReplayRecorder rec = ReplayRecorder.Instance;
            if (rec != null) truth.Watch(rec);
            if (rec != null && rec.Recording && rec.LastTickTime >= RecordSeconds) path = rec.EndRecording();
            yield return null;
        }

        try
        {
            Assert.IsNotNull(path, "no replay was recorded in time");
            ReplayData data = KrecReader.Load(path);
            Assert.IsTrue(data.Complete);
            Assert.GreaterOrEqual(data.End, RecordSeconds);
            List<(float t, int id, Vector3 position, Quaternion rotation)> poses = truth.Poses;
            Assert.IsTrue(poses.Any(s => data.Entities.TryGetValue(s.id, out ReplayEntity e) && e.Kind == KrecKind.Player), "the player was recorded");
            Assert.IsTrue(poses.Any(s => data.Entities.TryGetValue(s.id, out ReplayEntity e) && e.Kind == KrecKind.Karen), "Karen was recorded");
            Assert.Greater(poses.Count, 100);

            float worst = 0f, worstAngle = 0f;
            int checkedPoses = 0;
            foreach (var s in poses)
            {
                if (s.t > data.End) continue;
                Assert.IsTrue(data.TryPose(s.id, s.t, out EntitySample pose), $"entity {s.id} missing at {s.t:0.00}s");
                worst = Mathf.Max(worst, Vector3.Distance(pose.Position, s.position));
                worstAngle = Mathf.Max(worstAngle, Quaternion.Angle(pose.Rotation, s.rotation));
                checkedPoses++;
            }
            Assert.Less(worst, Tolerance, $"worst position error {worst * 1000f:0.0} mm over {checkedPoses} poses");
            Assert.Less(worstAngle, 0.1f, $"worst rotation error {worstAngle:0.000}°");

            Assert.Greater(data.Camera.Count, 50, "the player's view was recorded");
            Assert.Greater(data.Belief.Count, 5, "her belief map was recorded");
            float perTen = new FileInfo(path).Length / Mathf.Max(1f, data.End) * 600f / (1024f * 1024f);
            Assert.Less(perTen, 20f, $"≈{perTen:0.0} MB per 10 minutes");
            Debug.Log($"Replay round trip: {checkedPoses} poses, worst {worst * 1000f:0.00} mm / {worstAngle:0.000}°, " +
                      $"{data.Entities.Count} entities, {data.Camera.Count} views, {data.Belief.Count} belief frames, ≈{perTen:0.00} MB per 10 min");
        }
        finally
        {
            // Close the shift record too, then remove everything this test wrote.
            if (ShiftRecorder.Instance != null) ShiftRecorder.Instance.enabled = false;
            if (Directory.Exists(folder))
                foreach (string file in Directory.GetFiles(folder))
                    if (!before.Contains(file)) File.Delete(file);
        }

        yield return new ExitPlayMode();
    }

    // Where the player, Karen and every customer really were at each tick the recorder wrote.
    sealed class Truth
    {
        public readonly List<(float t, int id, Vector3 position, Quaternion rotation)> Poses = new List<(float, int, Vector3, Quaternion)>();
        ReplayRecorder watching;

        public void Watch(ReplayRecorder recorder)
        {
            if (recorder == watching) return;
            watching = recorder;     // a new one after the store reloaded for the episode
            Poses.Clear();
            recorder.Ticked += OnTick;
        }

        void OnTick(float t)
        {
            foreach (ReplayRecorder.Tracked e in watching.Entities)
                if ((e.Kind == KrecKind.Player || e.Kind == KrecKind.Karen || e.Kind == KrecKind.Customer) && e.Transform != null)
                    Poses.Add((t, e.Id, e.Transform.position, e.Transform.rotation));
        }
    }

    static IEnumerator PlayOneShift(KehaiEnv env, EnvConfig config)
    {
        yield return env.ResetEpisode(config);
        var bot = new SimulatedPlayer(env, PlayerProfile.Efficient, 99);
        yield return bot.PlayShift();
    }
}
