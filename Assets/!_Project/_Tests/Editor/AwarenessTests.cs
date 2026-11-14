using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Karen 0.2.0, phase 4: how fast she sees you.
//
// The walk-by set times her own eye (KarenBody.ConfigureEye: 150°, floor 0.04, 0.5 s grace)
// through scripted passes: you walk round her at each pass's distance and angle, and the
// meter is stepped at 60 frames a second from the eye's own score. No rays: the passes are in
// the open, or as much of you as the pass says shows past cover.
//
// 0.4.1, the owner's calls: twice the radius (72 m, half strength at 32 m: was 36 and 16),
// twice as quick to notice (gain 3.3: was 1.65), and cover judged eye to eye. So 0.2.0's
// targets are halved: walking 10 m away in a lit aisle, seen within 0.35 s (was 0.7); 15 m
// and off-centre, within 0.75 s (was 1.5). And 0.2.0's third target, "crouched in the dim,
// never", is replaced by the eye-to-eye rule: with no line to your eyes, never; with one, she
// does see you, slowly.
public class AwarenessTests
{
    const float Frame = 1f / 60f;
    const float Lit = 0.9f;                          // a lit aisle, between two ceiling panels (1 right under one)
    const float Dim = 0.3f;
    const float Walking = 0.55f, Crouching = 0.2f;   // PlayerPresence.MotionSalience

    public struct Pass
    {
        public string Name;
        public float Distance, Angle, Light, Salience, Exposure;
        public override string ToString() => Name;
    }

    static Pass Walk(string name, float distance, float angle) =>
        new Pass { Name = name, Distance = distance, Angle = angle, Light = Lit, Salience = Walking, Exposure = 1f };

    // The six cases of the design notes (§3), and the second target.
    static readonly Pass TenCentre = Walk("walking, 10 m, dead centre, lit", 10f, 0f);
    static readonly Pass FifteenOff = Walk("walking, 15 m, 45° off-centre, lit", 15f, 45f);
    // Crouched in the dim with only your eyes over cover (SightSensor.ExposureOf: a half), and
    // the same with no line to your eyes at all.
    static readonly Pass Crouched = new Pass
    {
        Name = "crouched, only your eyes showing, 10 m, 45° off, dim",
        Distance = 10f, Angle = 45f, Light = Dim, Salience = Crouching, Exposure = SightSensor.ExposureOf(true, false, false),
    };
    static readonly Pass Hidden = new Pass
    {
        Name = "crouched, no line to your eyes, 10 m, 45° off, dim",
        Distance = 10f, Angle = 45f, Light = Dim, Salience = Crouching, Exposure = SightSensor.ExposureOf(false, true, true),
    };

    public const float TenTarget = 0.35f, FifteenTarget = 0.75f;   // seconds: half of 0.2.0's 0.7 and 1.5

    static readonly Pass[] WalkBy =
    {
        TenCentre,
        Walk("walking, 10 m, 45° off-centre, lit", 10f, 45f),
        Walk("walking, 10 m, 70° off-centre, lit", 10f, 70f),
        FifteenOff,
        Walk("walking, 25 m, dead centre, lit", 25f, 0f),
        Walk("walking, 36 m, dead centre, lit", 36f, 0f),
        Walk("walking, 72 m, dead centre, lit", 72f, 0f),
        Crouched,
        Hidden,
    };

    GameObject go;
    SightSensor eye;
    KarenConfig config;

    [SetUp]
    public void BuildHerEye()
    {
        config = new KarenConfig();
        go = new GameObject("her eye");
        eye = go.AddComponent<SightSensor>();
        KarenBody.ConfigureEye(eye, config);
    }

    [TearDown]
    public void Clear() => Object.DestroyImmediate(go);

    // Seconds from first being visible to "seen" (awareness 0.5), with her belief expecting
    // you `expected` (0–1) where you are. Infinity if not within `limit`.
    static float SecondsToSeen(SightSensor eye, Pass p, float expected = 0f, float limit = 30f)
    {
        eye.ResetAwareness();
        int frames = Mathf.RoundToInt(limit / Frame);
        for (int i = 1; i <= frames; i++)
        {
            eye.Integrate(eye.Detect(p.Angle, p.Distance, p.Light, p.Salience, p.Exposure), Frame, expected);
            if (eye.Awareness >= eye.seeAt) return i * Frame;
        }
        return float.PositiveInfinity;
    }

    // "She starts moving at you": her decisions read the meter through her appraisal
    // (KarenConfig.appraiseHz), and a sighting that can start a hunt cuts her plan at once
    // (KarenBrain.Interrupt); the hunt's run starts within a frame or two. At worst, then,
    // this long after "seen".
    static float MovingAfterSeen(KarenConfig c) => 1f / c.appraiseHz + 2f * Frame;

    // ---- the walk-by set -----------------------------------------------------------------

    [Test]
    public void TheWalkBySet_HitsTheOwnersTargets()
    {
        float moving = MovingAfterSeen(config);
        foreach (Pass p in WalkBy)
        {
            float seen = SecondsToSeen(eye, p), expected = SecondsToSeen(eye, p, 1f);
            TestContext.WriteLine($"{p.Name}: score {eye.Detect(p.Angle, p.Distance, p.Light, p.Salience, p.Exposure):0.000}, " +
                                  $"seen {Show(seen)}, moving at you by {Show(seen + moving)}; where she expects you {Show(expected)}");
        }

        Assert.LessOrEqual(SecondsToSeen(eye, TenCentre), TenTarget, TenCentre.Name);
        Assert.LessOrEqual(SecondsToSeen(eye, FifteenOff), FifteenTarget, FifteenOff.Name);
        Assert.IsTrue(float.IsInfinity(SecondsToSeen(eye, Hidden)), Hidden.Name + ": seen");
    }

    static string Show(float seconds) => float.IsInfinity(seconds) ? "never" : $"{seconds:0.00} s";

    [Test]
    public void WithNoLineToYourEyes_YouStayUnseen_EvenWhereSheExpectsYou()
    {
        // Eye to eye (0.4.1): chest and feet in plain view count for nothing without it.
        Assert.AreEqual(0f, eye.Detect(Hidden.Angle, Hidden.Distance, Hidden.Light, Hidden.Salience, Hidden.Exposure));
        Assert.IsTrue(float.IsInfinity(SecondsToSeen(eye, Hidden, 1f)));

        // Your eyes over the cover, and she does see you: slower than in the open.
        float peeking = SecondsToSeen(eye, Crouched);
        Pass open = Crouched;
        open.Exposure = 1f;
        Assert.IsFalse(float.IsInfinity(peeking), "a line to your eyes should be seen");
        Assert.Greater(peeking, SecondsToSeen(eye, open), "only your eyes showing should take longer than all of you");
    }

    [Test]
    public void Exposure_IsNothingWithoutTheEyes_AndHalfWithThemAlone()
    {
        Assert.AreEqual(0f, SightSensor.ExposureOf(false, true, true));
        Assert.AreEqual(0f, SightSensor.ExposureOf(false, false, false));
        Assert.AreEqual(0.5f, SightSensor.ExposureOf(true, false, false));
        Assert.AreEqual(0.75f, SightSensor.ExposureOf(true, true, false));
        Assert.AreEqual(1f, SightSensor.ExposureOf(true, true, true));
    }

    [Test]
    public void WalkingOrStandingInTheOpen_IsSeenAcrossHerWholeRange()
    {
        foreach (Pass p in WalkBy.Where(p => p.Salience == Walking))
            Assert.IsFalse(float.IsInfinity(SecondsToSeen(eye, p)), p.Name);

        Pass standing = Walk("standing, 72 m, dead centre, lit", 72f, 0f);
        standing.Salience = 0.05f;
        Assert.IsFalse(float.IsInfinity(SecondsToSeen(eye, standing)), standing.Name);
    }

    [Test]
    public void SheNoticesYouTwiceAsFast_AndFromTwiceAsFar_As020()
    {
        // The owner's calls (0.4.1), against 0.2.0's eye: 36 m, half strength 16 m, gain 1.65.
        Assert.AreEqual(72f, config.sightRange);
        Assert.AreEqual(32f, config.sightHalfStrength);
        Assert.AreEqual(3.3f, config.sightGain, 1e-4f);

        // Twice the gain alone halves the time to "seen", pass by pass.
        foreach (Pass p in WalkBy.Where(p => p.Salience == Walking))
        {
            float now = SecondsToSeen(eye, p);
            eye.gain = config.sightGain * 0.5f;
            float atHalfGain = SecondsToSeen(eye, p);
            eye.gain = config.sightGain;
            Assert.AreEqual(atHalfGain / 2f, now, 2f * Frame, p.Name);
        }

        // Twice the radius: she sees as well at twice the distance as 0.2.0's eye did.
        var old = new GameObject("0.2.0's eye").AddComponent<SightSensor>();
        try
        {
            KarenBody.ConfigureEye(old, config);
            old.range = 36f;
            old.halfStrengthAt = 16f;
            foreach (float d in new[] { 5f, 10f, 18f, 36f })
                Assert.AreEqual(old.Visibility(20f, d, Lit), eye.Visibility(20f, d * 2f, Lit), 1e-5f, $"{d} m then, {d * 2f} m now");
            Assert.AreEqual(0f, old.Visibility(0f, 40f, Lit), "0.2.0's eye stopped at 36 m");
            Assert.Greater(eye.Visibility(0f, 70f, Lit), 0f, "hers reaches 72 m");
        }
        finally { Object.DestroyImmediate(old.gameObject); }
    }

    // ---- D and E -------------------------------------------------------------------------

    [Test]
    public void WhereSheExpectsYou_SheSeesYouTwiceAsFast()
    {
        foreach (Pass p in WalkBy.Where(p => p.Salience == Walking))
        {
            float plain = SecondsToSeen(eye, p), expected = SecondsToSeen(eye, p, 1f);
            Assert.AreEqual(plain / 2f, expected, 2f * Frame, p.Name);
            Assert.AreEqual(plain, SecondsToSeen(eye, p, 0f), 1e-4f, "no expectation, no boost");
        }
    }

    [Test]
    public void AShelfEnd_DoesNotResetHer()
    {
        // Seen for a moment, behind a shelf end for under the grace: the meter holds.
        for (int i = 0; i < 18; i++) eye.Integrate(0.4f, Frame, 0f);
        float before = eye.Awareness;
        Assert.Greater(before, 0.1f);
        for (int i = 0; i < 27; i++) eye.Integrate(0f, Frame, 0f);    // 0.45 s
        Assert.AreEqual(before, eye.Awareness, 1e-5f, "drained inside the grace");

        // Past it, it drains as before.
        for (int i = 0; i < 30; i++) eye.Integrate(0f, Frame, 0f);
        Assert.Less(eye.Awareness, before - 0.1f, "didn't drain after the grace");

        // Seen again, the grace starts over.
        eye.Integrate(0.4f, Frame, 0f);
        float again = eye.Awareness;
        for (int i = 0; i < 27; i++) eye.Integrate(0f, Frame, 0f);
        Assert.AreEqual(again, eye.Awareness, 1e-5f);
    }

    [Test]
    public void TheCamerasAndShoppers_KeepTheirOwnEyes()
    {
        var other = new GameObject("a camera").AddComponent<SightSensor>();
        try
        {
            Assert.AreEqual(8f, other.halfStrengthAt);
            Assert.AreEqual(0.02f, other.scoreFloor);
            Assert.AreEqual(0f, other.grace);
            Assert.AreEqual(0f, other.expectedBoost);
            Assert.AreEqual(18f, other.range);

            // Hers works at half strength four times as far away (twice, until 0.4.1).
            Assert.AreEqual(0.5f, eye.Falloff(32f), 1e-5f);
            Assert.AreEqual(0.5f, other.Falloff(8f), 1e-5f);
        }
        finally { Object.DestroyImmediate(other.gameObject); }
    }
}

// The parts of phase 4's eyes that need the store: her expectation, read off the belief
// grid, and the "nothing here" sweep over the cells inside her cone.
public class AwarenessInTheStoreTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    StoreMap map;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        map = StoreMap.Rebuild();
        Physics.SyncTransforms();
    }

    List<int> Hers() => Enumerable.Range(0, map.CellCount).Where(map.IsKarens).ToList();

    [Test]
    public void Expectation_IsFullWhereSheJustSawYou_AndNoneFarAway()
    {
        var grid = new BeliefGrid(map);
        List<int> hers = Hers();
        Vector3 at = map.CellPosition[hers[hers.Count / 2]];
        grid.Apply(new Observation(SenseChannel.Sight, at, 0.95f, 1.5f, "test sighting"));

        Assert.AreEqual(1f, grid.Expectation(at), 1e-4f, "where she just saw you");
        int far = hers.OrderByDescending(c => (map.CellPosition[c] - at).sqrMagnitude).First();
        Assert.AreEqual(0f, grid.Expectation(map.CellPosition[far]), 1e-4f, "the far end of her store");
        Assert.AreEqual(0f, grid.Expectation(map.CellPosition[grid.OutsideCell]), "outside: she never hunts you there");

        // An even spread expects you nowhere in particular.
        var even = new BeliefGrid(map);
        float most = hers.Where((c, i) => i % 50 == 0).Max(c => even.Expectation(map.CellPosition[c]));
        TestContext.WriteLine($"most expectation anywhere at the start of a shift: {most:0.00}");
        Assert.Less(most, 1f, "the shift's prior shouldn't already expect you everywhere");
    }

    // At spots all over her store, facing four ways: how many ticks between two looks at the
    // same cell in view. Before 0.2.0 the sweep went round every cell within 18 m, 70 a
    // tick, and skipped those outside her 120°; now it goes round the cells inside her 150°
    // cone: 36 m and 120 a tick in 0.2.0, 72 m and 240 a tick since 0.4.1.
    [Test]
    public void TheSweep_ChecksEachCellInViewAboutAsOftenAsBefore_AndItsCostIsMeasured()
    {
        var config = new KarenConfig();
        var go = new GameObject("her eye");
        var eye = go.AddComponent<SightSensor>();
        KarenBody.ConfigureEye(eye, config);
        var sensorium = new Sensorium(map, new EnergyBelief())
        {
            UseHearing = false, UseTraces = false, UseTestimony = false, UseInfrastructure = false,
            SweepCellsPerTick = config.sweepCellsPerTick,
        };
        try
        {
            List<int> hers = Hers();
            var cone = new List<int>();
            var circle = new List<int>();
            double before = 0, after = 0, ms = 0;
            int spots = 0, rays = 0, maxRays = 0;
            var watch = new Stopwatch();
            for (int k = 0; k < hers.Count; k += Mathf.Max(1, hers.Count / 40))
            {
                Vector3 floor = map.CellPosition[hers[k]];
                for (int turn = 0; turn < 4; turn++)
                {
                    go.transform.SetPositionAndRotation(floor + Vector3.up * 1.85f, Quaternion.Euler(0f, 90f * turn, 0f));
                    map.CellsWithin(eye.Eye, 18f, circle);
                    Sensorium.ConeCells(map, eye, cone);
                    if (circle.Count == 0 || cone.Count == 0) continue;
                    before += System.Math.Max(1.0, circle.Count / 70.0);
                    after += System.Math.Max(1.0, cone.Count / (double)config.sweepCellsPerTick);

                    watch.Restart();
                    sensorium.Collect(floor, eye, go.transform);
                    watch.Stop();
                    ms += watch.Elapsed.TotalMilliseconds;
                    rays += sensorium.SweepRays;
                    maxRays = Mathf.Max(maxRays, sensorium.SweepRays);
                    spots++;
                }
            }
            Assert.Greater(spots, 50);
            before /= spots;
            after /= spots;
            TestContext.WriteLine($"{spots} spots: a cell in view is looked at every {before:0.0} ticks before, {after:0.0} now; " +
                                  $"a sweep casts {rays / (float)spots:0} rays (at most {maxRays}) in {ms / spots:0.000} ms");
            Assert.LessOrEqual(after, before * 1.25, "cells in view are checked less often than before");
            Assert.LessOrEqual(maxRays, config.sweepCellsPerTick, "the sweep cast more rays than its budget");
            // The budget per cell checked is 0.2.0's (5 ms for 120 cells); since 0.4.1 a tick checks
            // twice the cells, for a cone twice as long, so a sweep may cost twice as much.
            Assert.Less(ms / spots, 5.0 * config.sweepCellsPerTick / 120.0, "a sweep costs too much for the cells it checks");
        }
        finally
        {
            sensorium.Dispose();
            Object.DestroyImmediate(go);
        }
    }
}
