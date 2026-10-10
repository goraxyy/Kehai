using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Replay;
using NUnit.Framework;
using UnityEngine;

// The .krec format on its own: numbers and rotations survive the trip, a writer's file reads
// back as written, an entity that held still doesn't drift, and a file cut short still loads.
public class KrecTests
{
    string path;

    [SetUp]
    public void TempFile() => path = Path.Combine(Path.GetTempPath(), "krec_test_" + System.Guid.NewGuid().ToString("N") + ".krec");

    [TearDown]
    public void RemoveFile()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    [Test]
    public void VarintsRoundTrip()
    {
        var values = new[] { 0, 1, -1, 63, -64, 64, 300, -300, 70000, -70000, int.MaxValue, int.MinValue };
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) foreach (int v in values) Krec.WriteVarint(w, v);
        ms.Position = 0;
        using (var r = new BinaryReader(ms)) foreach (int v in values) Assert.AreEqual(v, Krec.ReadVarint(r));
        Assert.Less(Varint(47), 2, "a walking step fits in a byte");
    }

    static long Varint(int v)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) Krec.WriteVarint(w, v);
        return ms.ToArray().Length;
    }

    [Test]
    public void RotationsSurviveCompression()
    {
        var rng = new System.Random(7);
        for (int i = 0; i < 2000; i++)
        {
            Quaternion q = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
            Assert.Less(Quaternion.Angle(q, Krec.Expand(Krec.Compress(q))), 0.02f);
        }
        Assert.Less(Quaternion.Angle(Quaternion.identity, Krec.Expand(Krec.Compress(new Quaternion(0f, 0f, 0f, 0f)))), 0.02f, "a zero quaternion becomes identity");
    }

    [Test]
    public void AWrittenFileReadsBackAsWritten()
    {
        var header = new KrecHeader { Stem = "shift_03_test", Shift = 3, Started = "2026-09-30 10:00", Scene = "Assets/store.unity", Seed = 1234, Rung = "F",
                                      BeliefOrigin = new Vector2(-10f, -200f), BeliefCols = 3, BeliefRows = 2, BeliefMask = new byte[] { 1, 1, 0, 1, 1, 1 } };
        header.MazeMoves.Add((7, new Vector3(1f, 0f, 2f), new Vector3(4f, 0f, 2f)));
        header.Slots.Add((new Vector3(10f, 1.2f, -50f), true, "miso_paste"));
        header.Lights.Add((new Vector3(12f, 4f, -60f), false));

        Quaternion turned = Quaternion.Euler(0f, 90f, 0f);
        using (var w = new KrecWriter(path))
        {
            w.Header(header);
            w.Tick(0f);
            w.Spawn(1, KrecKind.Player, "Player", "you", new Vector3(40.1234f, 0f, -150.5678f), Quaternion.identity, 2, true);
            w.Spawn(2, KrecKind.Item, "miso_paste", "Miso", new Vector3(10f, 1.2f, -50f), Quaternion.identity, KrecState.ItemLoose, true);
            w.Camera(0f, new Vector3(40f, 1.6f, -150f), Quaternion.identity, 70f, 0f, 0);
            w.Belief(0f, new Vector2(41f, -151f), 0.6f, new byte[] { 0, 10, 0, 255, 3, 0 });

            w.Tick(1f / 30f);
            Assert.IsTrue(w.Pose(1, new Vector3(40.17f, 0f, -150.5678f), turned, 2, true));
            Assert.IsFalse(w.Pose(2, new Vector3(10f, 1.2f, -50f), Quaternion.identity, KrecState.ItemLoose, true), "nothing changed, nothing written");
            w.Camera(1f / 30f, new Vector3(40f, 1.6f, -150f), Quaternion.identity, 70f, 1f, 2);
            w.Noise(1f / 30f, 2, 0, new Vector3(40f, 0f, -150f), 0.8f);
            w.Tell(1f / 30f, 5, new Vector3(20f, 3f, -100f), 1.5f);
            w.Text(1f / 30f, KrecEvent.PaSpeech, "This is a formal conversation.");
            w.Circuits(1f / 30f, 1 | 2 | 8);
            w.Thought(1f / 30f, "PLAN", "PLAN     fog → because");
            w.Story(1f / 30f, 3, "Karen heard running.");
            w.Slot(1f / 30f, 0, false, "miso_paste");
            w.Lights(1f / 30f, new List<(int, bool)> { (0, true) });

            w.Tick(2f / 30f);
            w.Pose(2, new Vector3(10f, 0.05f, -50.3f), Quaternion.identity, KrecState.ItemInHand, false);
            w.Despawn(1);
            w.Belief(0.5f, new Vector2(42f, -150f), 0.7f, new byte[] { 0, 12, 0, 250, 3, 1 });
            w.End(2f / 30f, true);
        }

        ReplayData d = KrecReader.Load(path);
        Assert.IsTrue(d.Complete);
        Assert.IsTrue(d.ClockedOut);
        Assert.AreEqual("shift_03_test", d.Header.Stem);
        Assert.AreEqual(1234, d.Header.Seed);
        Assert.AreEqual("F", d.Header.Rung);
        Assert.AreEqual(3, d.Header.BeliefCols);
        CollectionAssert.AreEqual(new byte[] { 1, 1, 0, 1, 1, 1 }, d.Header.BeliefMask);
        Assert.AreEqual(7, d.Header.MazeMoves[0].bay);
        Assert.AreEqual(4f, d.Header.MazeMoves[0].to.x, 1e-3f);
        Assert.AreEqual("miso_paste", d.Header.Slots[0].productId);
        Assert.IsFalse(d.Header.Lights[0].on);
        CollectionAssert.AreEqual(new[] { 0f, 1f / 30f, 2f / 30f }, d.Ticks);

        Assert.IsTrue(d.TryPose(1, 1f / 30f, out EntitySample you));
        Assert.AreEqual(40.17f, you.Position.x, 0.0006f);
        Assert.AreEqual(-150.5678f, you.Position.z, 0.0006f);
        Assert.Less(Quaternion.Angle(turned, you.Rotation), 0.02f);
        Assert.IsFalse(d.TryPose(1, 2f / 30f, out _), "despawned");

        Assert.IsTrue(d.TryPose(2, 2f / 30f, out EntitySample item));
        Assert.AreEqual(KrecState.ItemInHand, item.State);
        Assert.IsFalse(item.Visible);
        Assert.AreEqual(0.05f, item.Position.y, 0.0006f);
        Assert.AreEqual(KrecKind.Item, d.Entities[2].Kind);
        Assert.AreEqual("Miso", d.Entities[2].Label);

        Assert.AreEqual(2, d.Camera.Count);
        Assert.AreEqual(1f, d.Camera[1].Eyelids, 1e-3f);
        Assert.AreEqual(2, d.Camera[1].Held);
        Assert.AreEqual(70f, d.Camera[1].Fov, 0.01f);

        CollectionAssert.AreEqual(
            new[] { KrecEvent.Noise, KrecEvent.Tell, KrecEvent.PaSpeech, KrecEvent.Circuits, KrecEvent.Thought, KrecEvent.Story, KrecEvent.Slot, KrecEvent.Lights },
            d.Events.Select(e => e.Type).ToArray());
        Assert.AreEqual("This is a formal conversation.", d.Events[2].Text);
        Assert.AreEqual(1 | 2 | 8, d.Events[3].Kind);
        Assert.AreEqual("PLAN", d.Events[4].Extra);
        Assert.AreEqual(1.5f, d.Events[1].Value, 1e-6f);
        Assert.AreEqual((0, true), d.Events[7].Lights[0]);

        Assert.AreEqual(2, d.Belief.Count);
        CollectionAssert.AreEqual(new byte[] { 0, 12, 0, 250, 3, 1 }, d.Belief[1].Grid, "the second grid is XOR'd against the first");
        Assert.AreEqual(0.7f, d.Belief[1].Confidence, 1e-4f);
    }

    [Test]
    public void SomethingThatHeldStillDoesntDrift()
    {
        using (var w = new KrecWriter(path))
        {
            w.Header(new KrecHeader());
            for (int tick = 0; tick <= 10; tick++)
            {
                float t = tick / 30f;
                w.Tick(t);
                if (tick == 0) w.Spawn(1, KrecKind.Customer, "Customer", "c", Vector3.zero, Quaternion.identity, 0, true);
                else w.Pose(1, tick < 10 ? Vector3.zero : new Vector3(1f, 0f, 0f), Quaternion.identity, 0, true);
            }
            w.End(10f / 30f, false);
        }
        ReplayData d = KrecReader.Load(path);
        Assert.AreEqual(2, d.Entities[1].Samples.Count, "only the start and the move were written");
        d.TryPose(1, 5f / 30f, out EntitySample middle);
        Assert.AreEqual(0f, middle.Position.x, 1e-4f, "still there five ticks later, not halfway");
        d.TryPose(1, 9.5f / 30f, out EntitySample moving);
        Assert.AreEqual(0.5f, moving.Position.x, 1e-3f, "moving only in the last tick");
    }

    [Test]
    public void AFileCutShortStillLoads()
    {
        using (var w = new KrecWriter(path))
        {
            w.Header(new KrecHeader { Stem = "cut" });
            for (int tick = 0; tick < 200; tick++)
            {
                w.Tick(tick / 30f);
                if (tick == 0) w.Spawn(1, KrecKind.Player, "Player", "you", Vector3.zero, Quaternion.identity, 0, true);
                else w.Pose(1, new Vector3(tick * 0.05f, 0f, 0f), Quaternion.identity, 0, true);
            }
        }   // no End: the game quit
        ReplayData whole = KrecReader.Load(path);
        Assert.IsFalse(whole.Complete);
        Assert.AreEqual(200, whole.Ticks.Count);

        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes.Take(bytes.Length * 2 / 3).ToArray());
        ReplayData cut = null;
        Assert.DoesNotThrow(() => cut = KrecReader.Load(path));
        Assert.AreEqual("cut", cut.Header.Stem);
    }

    // Thirty things walking about for a minute, the view at 60 Hz and her map at 2 Hz: the
    // recording has to stay well under 20 MB per ten minutes.
    [Test]
    public void TenMinutesStayUnderTwentyMegabytes()
    {
        var rng = new System.Random(3);
        using (var w = new KrecWriter(path))
        {
            w.Header(new KrecHeader { BeliefCols = 50, BeliefRows = 50, BeliefMask = new byte[2500] });
            var grid = new byte[2500];
            for (int tick = 0; tick < 60 * 30; tick++)
            {
                float t = tick / 30f;
                w.Tick(t);
                for (int id = 1; id <= 30; id++)
                {
                    var p = new Vector3(id * 3f + Mathf.Sin(t * 0.3f + id) * 20f, 0f, Mathf.Cos(t * 0.2f + id) * 20f);
                    Quaternion q = Quaternion.Euler(0f, t * 30f + id * 10f, 0f);
                    if (tick == 0) w.Spawn(id, KrecKind.Customer, "Customer", "c", p, q, 0, true);
                    else w.Pose(id, p, q, (tick / 90) % 3, true);
                }
                w.Camera(t, new Vector3(Mathf.Sin(t) * 10f, 1.6f, t), Quaternion.Euler(Mathf.Sin(t) * 10f, t * 20f, 0f), 70f, 0f, 0);
                w.Camera(t + 1f / 60f, new Vector3(Mathf.Sin(t + 0.016f) * 10f, 1.6f, t + 0.016f), Quaternion.Euler(Mathf.Sin(t) * 10f, t * 20f + 0.3f, 0f), 70f, 0f, 0);
                if (tick % 15 == 0)
                {
                    for (int i = 0; i < grid.Length; i++) grid[i] = (byte)Mathf.Clamp(128 + (int)(Mathf.Sin(i * 0.05f + t * 0.1f) * 100f) + rng.Next(-2, 3), 0, 255);
                    w.Belief(t, Vector2.zero, 0.5f, grid);
                }
            }
            w.End(60f, true);
        }
        float perTenMinutes = new FileInfo(path).Length * 10f / (1024f * 1024f);
        Assert.Less(perTenMinutes, 20f, $"≈{perTenMinutes:0.0} MB per 10 minutes");
    }

    [Test]
    public void WithoutAModelTheBodyIsACapsule()
    {
        PlayerBodySlot slot = PlayerBodySlot.Create();
        try
        {
            if (!slot.IsPlaceholder) Assert.Ignore("the owner's player model is in this project");
            Assert.IsNotNull(slot.Body);
            Assert.IsNull(slot.Body.GetComponent<Collider>(), "the replay body mustn't collide with anything");
            slot.Show(new Vector3(5f, 0f, 7f), 90f, 1, false, 0.1f);
            Assert.AreEqual(PlayerBodySlot.Clip.CrouchWalk, slot.Playing);
            Assert.Less(slot.Body.transform.localScale.y, 0.9f, "lower while crouching");
            Assert.AreEqual(new Vector3(5f, 0f, 7f), slot.transform.position);
        }
        finally
        {
            Object.DestroyImmediate(slot.gameObject);
        }
    }

    [Test]
    public void ClipsAreFoundByName()
    {
        Assert.AreEqual(PlayerBodySlot.Clip.CrouchWalk, PlayerBodySlot.Classify("Armature|Crouch_Walk"));
        Assert.AreEqual(PlayerBodySlot.Clip.Walk, PlayerBodySlot.Classify("walk_loop"));
        Assert.AreEqual(PlayerBodySlot.Clip.Run, PlayerBodySlot.Classify("Sprint"));
        Assert.AreEqual(PlayerBodySlot.Clip.Carry, PlayerBodySlot.Classify("carry_walk"));
        Assert.AreEqual(PlayerBodySlot.Clip.Idle, PlayerBodySlot.Classify("Idle"));
        Assert.AreEqual(PlayerBodySlot.Clip.Carry, PlayerBodySlot.ClipFor(2, true));
        Assert.AreEqual(PlayerBodySlot.Clip.Run, PlayerBodySlot.ClipFor(3, true));
    }
}
