using System.IO;
using Kehai.Karen;
using Kehai.Replay;
using NUnit.Framework;
using UnityEngine;

// The replay player's parts that don't need the store: the camera path, the shot settings,
// where a body lands in a shot's picture, and the sound of a rendered shot.
public class ReplayPlayerTests
{
    static ShotPath ThreeKeys()
    {
        var p = new ShotPath();
        p.Add(2f, new Vector3(0, 2, 0), Quaternion.Euler(10, 0, 0), 60f, 4f);
        p.Add(5f, new Vector3(6, 3, 2), Quaternion.Euler(20, 90, 0), 45f, 6f);
        p.Add(9f, new Vector3(6, 2, 10), Quaternion.Euler(5, 180, 0), 70f, 3f);
        return p;
    }

    [Test]
    public void ShotPath_GoesThroughItsKeys_AndHoldsStillBeyondThem()
    {
        ShotPath p = ThreeKeys();
        foreach (ShotPath.Key k in p.Keys)
        {
            Assert.IsTrue(p.Evaluate(k.t, out ShotPath.Key at));
            Assert.Less(Vector3.Distance(at.position, k.position), 1e-3f, $"position at {k.t}");
            Assert.Less(Quaternion.Angle(at.rotation, k.rotation), 0.05f, $"rotation at {k.t}");
            Assert.AreEqual(k.fov, at.fov, 1e-3f);
        }
        p.Evaluate(0f, out ShotPath.Key before);
        p.Evaluate(20f, out ShotPath.Key after);
        Assert.AreEqual(p.Keys[0].position, before.position);
        Assert.AreEqual(p.Keys[2].position, after.position);
        Assert.IsFalse(new ShotPath().Evaluate(1f, out _), "an empty path has nowhere to be");
    }

    [Test]
    public void ShotPath_IsSmooth()
    {
        ShotPath p = ThreeKeys();
        float worstMove = 0f, worstTurn = 0f, worstAccel = 0f;
        p.Evaluate(p.Start, out ShotPath.Key last);
        Vector3 lastStep = Vector3.zero;
        const float dt = 1f / 60f;
        for (float t = p.Start + dt; t <= p.End; t += dt)
        {
            p.Evaluate(t, out ShotPath.Key now);
            Vector3 step = now.position - last.position;
            worstMove = Mathf.Max(worstMove, step.magnitude);
            worstAccel = Mathf.Max(worstAccel, (step - lastStep).magnitude);
            worstTurn = Mathf.Max(worstTurn, Quaternion.Angle(now.rotation, last.rotation));
            last = now;
            lastStep = step;
        }
        Assert.Less(worstMove, 0.1f, "no jumps: under 10 cm a frame at 60 fps");
        Assert.Less(worstTurn, 1.5f, "no snaps: under 1.5° a frame");
        Assert.Less(worstAccel, 0.004f, "no kinks through the keys");
    }

    [Test]
    public void ShotPath_SavesAndLoads_AndAKeyAtTheSameTimeReplacesTheOldOne()
    {
        ShotPath p = ThreeKeys();
        p.Add(5.02f, new Vector3(1, 1, 1), Quaternion.identity, 50f, 2f);
        Assert.AreEqual(3, p.Keys.Count);
        Assert.AreEqual(new Vector3(1, 1, 1), p.Keys[1].position);

        string file = Path.Combine(Path.GetTempPath(), "kehai_path_test.path.json");
        try
        {
            p.Krec = "shift_03_x.krec";
            p.Save(file);
            ShotPath back = ShotPath.Load(file);
            Assert.AreEqual("shift_03_x.krec", back.Krec);
            Assert.AreEqual(3, back.Keys.Count);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(p.Keys[i].t, back.Keys[i].t, 1e-4f);
                Assert.Less(Vector3.Distance(p.Keys[i].position, back.Keys[i].position), 1e-4f);
                Assert.Less(Quaternion.Angle(p.Keys[i].rotation, back.Keys[i].rotation), 0.01f);
            }
            Assert.IsTrue(back.RemoveLast());
            Assert.AreEqual(2, back.Keys.Count);
        }
        finally
        {
            File.Delete(file);
        }
        Assert.AreEqual("/r/shift_01.path.json", ShotPath.PathFor("/r/shift_01.krec").Replace('\\', '/'));
    }

    [Test]
    public void TheShotSettings_ReadAsTheRenderTakesThem()
    {
        Assert.AreEqual(MindLayer.Belief | MindLayer.Cone, MindLayers.Parse("belief,cone"));
        Assert.AreEqual(MindLayer.All, MindLayers.Parse("all"));
        Assert.AreEqual(MindLayer.None, MindLayers.Parse("none"));
        Assert.AreEqual(MindLayer.None, MindLayers.Parse(""));
        Assert.AreEqual(MindLayer.Sound | MindLayer.Thoughts, MindLayers.Parse("rings, thoughts"));
        Assert.Throws<System.ArgumentException>(() => MindLayers.Parse("belief,colour"));
        Assert.AreEqual("belief,cone", MindLayers.Describe(MindLayer.Belief | MindLayer.Cone));

        Assert.IsTrue(ReplayCameras.TryParse("topdown", out ShotPreset top));
        Assert.AreEqual(ShotPreset.TopDown, top);
        Assert.IsTrue(ReplayCameras.TryParse("CCTV", out ShotPreset cctv));
        Assert.AreEqual(ShotPreset.Cctv, cctv);
        Assert.IsFalse(ReplayCameras.TryParse("drone", out _));
    }

    [Test]
    public void AShotTrack_PutsABodyWhereTheCameraSeesIt()
    {
        var go = new GameObject("Track camera");
        try
        {
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.aspect = 9f / 16f;
            cam.nearClipPlane = 0.1f;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            double[] ahead = ShotTrack.Project(cam, new Vector3(0f, 0f, 5f));
            Assert.IsNotNull(ahead);
            Assert.AreEqual(0.5, ahead[0], 1e-3, "straight ahead is the middle, across");
            Assert.AreEqual(0.5, ahead[1], 1e-3, "and down");
            double[] above = ShotTrack.Project(cam, new Vector3(0f, 1f, 5f));
            Assert.Less(above[1], 0.5, "higher up is nearer the top (y runs down)");
            double[] right = ShotTrack.Project(cam, new Vector3(0.5f, 0f, 5f));
            Assert.Greater(right[0], 0.5, "to the right is further across");
            double[] far = ShotTrack.Project(cam, new Vector3(0f, 0f, 20f));
            Assert.Less(far[2], ahead[2], "further away looks smaller");
            Assert.IsNull(ShotTrack.Project(cam, new Vector3(0f, 0f, -5f)), "behind the camera");
            Assert.IsNull(ShotTrack.Project(cam, new Vector3(30f, 0f, 5f)), "outside the frame");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AShotTrack_SamplesTenTimesASecond_AtAnyFrameRate()
    {
        foreach (int fps in new[] { 24, 25, 30, 60 })
        {
            int samples = 0;
            for (int frame = 0; frame < fps * 3; frame++)
                if (ShotTrack.Due(frame, fps, samples)) samples++;
            Assert.AreEqual(3 * ShotTrack.Hz, samples, $"{fps} fps");
        }
        Assert.IsFalse(ShotTrack.Due(-1, 30, 0), "not during the warm-up");
    }

    [Test]
    public void TheSoundOfAShot_IsHeardWhereItHappened()
    {
        var data = new ReplayData();
        // A crash three metres to the listener's left at 1 s, and another 40 m away at 1.5 s.
        data.Events.Add(new ReplayEvent { T = 1f, Type = KrecEvent.Noise, Kind = (int)NoiseKind.DroppedItem, Position = new Vector3(-3f, 1.6f, 0f), Value = 0.8f });
        data.Events.Add(new ReplayEvent { T = 1.5f, Type = KrecEvent.Noise, Kind = (int)NoiseKind.DroppedItem, Position = new Vector3(40f, 1.6f, 0f), Value = 0.8f });
        var ear = new Pose(new Vector3(0f, 1.6f, 0f), Quaternion.identity);   // facing +z: -x is on the left

        float[] mix = ReplaySound.Mix(data, 0f, 2f, _ => ear);
        Assert.AreEqual(2 * 2 * ReplaySound.MixRate, mix.Length, "two seconds of stereo");

        float Peak(float from, float to, int channel)
        {
            float peak = 0f;
            for (int i = (int)(from * ReplaySound.MixRate); i < (int)(to * ReplaySound.MixRate); i++) peak = Mathf.Max(peak, Mathf.Abs(mix[i * 2 + channel]));
            return peak;
        }
        Assert.AreEqual(0f, Peak(0f, 0.99f, 0) + Peak(0f, 0.99f, 1), 1e-6f, "silent before it happened");
        Assert.Greater(Peak(1f, 1.3f, 0), 0.1f, "heard when it happened");
        Assert.Greater(Peak(1f, 1.3f, 0), Peak(1f, 1.3f, 1) * 2f, "on the left");
        Assert.AreEqual(0f, Peak(1.5f, 2f, 0) + Peak(1.5f, 2f, 1), 1e-6f, "40 m away is too far to hear");

        string file = Path.Combine(Path.GetTempPath(), "kehai_mix_test.wav");
        try
        {
            ReplaySound.WriteWav(file, mix);
            byte[] wav = File.ReadAllBytes(file);
            Assert.AreEqual(44 + mix.Length * 2, wav.Length);
            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual(ReplaySound.MixRate, System.BitConverter.ToInt32(wav, 24));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
