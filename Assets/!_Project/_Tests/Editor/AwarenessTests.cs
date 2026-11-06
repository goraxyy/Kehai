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
// The walk-by set times her own eye (KarenBody.ConfigureEye: 150°, 36 m, half strength at
// 16 m, gain 1.65, floor 0.04, 0.5 s grace) through scripted passes: you walk round her at
// each pass's distance and angle, and the meter is stepped at 60 frames a second from the
// eye's own score. No rays: the passes are in the open, or as much of you as the pass says
// shows past cover. The targets are the owner's (build plan §2): walking 10 m away in a lit
// aisle, seen within 0.7 s; 15 m and off-centre, within 1.5 s; crouched in the dim, never.
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
    static readonly Pass Crouched = new Pass
    {
        Name = "crouched, a third of you showing, 10 m, 45° off, dim",
        Distance = 10f, Angle = 45f, Light = Dim, Salience = Crouching, Exposure = 1f / 3f,
    };

    static readonly Pass[] WalkBy =
    {
        TenCentre,
        Walk("walking, 10 m, 45° off-centre, lit", 10f, 45f),
        Walk("walking, 10 m, 70° off-centre, lit", 10f, 70f),
        FifteenOff,
        Walk("walking, 25 m, dead centre, lit", 25f, 0f),
        Walk("walking, 36 m, dead centre, lit", 36f, 0f),
        Crouched,
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

        Assert.LessOrEqual(SecondsToSeen(eye, TenCentre), 0.7f, TenCentre.Name);
        Assert.LessOrEqual(SecondsToSeen(eye, FifteenOff), 1.5f, FifteenOff.Name);
        Assert.IsTrue(float.IsInfinity(SecondsToSeen(eye, Crouched)), Crouched.Name + ": seen");
    }

    static string Show(float seconds) => float.IsInfinity(seconds) ? "never" : $"{seconds:0.00} s";

    [Test]
    public void CrouchedInTheDim_StaysUnseen_EvenWhereSheExpectsYou()
    {
        // The floor is on the score, so more gain never finds the best hiding.
        Assert.Less(eye.Detect(Crouched.Angle, Crouched.Distance, Crouched.Light, Crouched.Salience, Crouched.Exposure), config.sightFloor);
        Assert.IsTrue(float.IsInfinity(SecondsToSeen(eye, Crouched, 1f)));

        // Show a little more of yourself, and she does.
        Pass more = Crouched;
        more.Exposure = 2f / 3f;
        Assert.IsFalse(float.IsInfinity(SecondsToSeen(eye, more)), "two thirds showing should be seen");
    }

    [Test]
    public void WalkingOrStandingInTheOpen_IsSeenAcrossHerWholeRange()
    {
        foreach (Pass p in WalkBy.Where(p => p.Salience == Walking))
            Assert.IsFalse(float.IsInfinity(SecondsToSeen(eye, p)), p.Name);

        Pass standing = Walk("standing, 36 m, dead centre, lit", 36f, 0f);
        standing.Salience = 0.05f;
        Assert.IsFalse(float.IsInfinity(SecondsToSeen(eye, standing)), standing.Name);
    }

    [Test]
    public void TheGain_IsAsLowAsStillHitsTheTargets()
    {
        // Option A was set last and low, because more gain weakens hiding everywhere: a tenth
        // less and a target is missed.
        eye.gain = config.sightGain * 0.9f;
        bool missed = SecondsToSeen(eye, TenCentre) > 0.7f || SecondsToSeen(eye, FifteenOff) > 1.5f;
        Assert.IsTrue(missed, $"gain {eye.gain:0.00} still hits both targets: lower {nameof(KarenConfig.sightGain)}");
        Assert.Less(config.sightGain, 2.2f, "with the wider cone the gain went down from 2.2, not up");
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

            // Hers works at half strength twice as far away.
            Assert.AreEqual(0.5f, eye.Falloff(16f), 1e-5f);
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
    // tick, and skipped those outside her 120°; now it goes round the cells inside her 150°,
    // 36 m cone, 120 a tick.
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
            Assert.Less(ms / spots, 5.0, "a sweep costs too much");
        }
        finally
        {
            sensorium.Dispose();
            Object.DestroyImmediate(go);
        }
    }
}
