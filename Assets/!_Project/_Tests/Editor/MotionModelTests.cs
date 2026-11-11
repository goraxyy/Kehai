using System.Collections.Generic;
using System.Linq;
using Kehai;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// 0.4.0, phase 4 (build plan §8): the predict step behind IMotionModel (today's, bit for bit), the
// destinations and P1–P3, her senses' table, the mirror map, and the parity with the Python replay.
public class MotionModelTests
{
    // A store of 9 × 7 cells with two shelves, and (optionally) an outside strip along one side.
    static StoreMap Store(bool withOutside)
    {
        int cols = 9, rows = withOutside ? 8 : 7;
        var open = new bool[cols * rows];
        var outside = new bool[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                open[r * cols + c] = !(r is 2 or 4 && c >= 2 && c <= 6);
                outside[r * cols + c] = withOutside && r == rows - 1;
            }
        return StoreMap.Synthetic(cols, rows, open, withOutside ? outside : null);
    }

    static void Layers(BeliefGrid g, int seed)
    {
        var rnd = new System.Random(seed);
        for (int c = 0; c < g.CellCount; c++)
        {
            g.DesireDistance[c] = rnd.NextDouble() < 0.1 ? float.PositiveInfinity : (float)(rnd.NextDouble() * 20.0);
            g.HabitPrior[c] = rnd.NextDouble() < 0.3 ? (float)rnd.NextDouble() : 0f;
            g.Danger[c] = rnd.NextDouble() < 0.2 ? (float)rnd.NextDouble() : 0f;
        }
    }

    // ---- C4.1 today's step behind the interface, bit for bit ------------------------------------

    // The predict step's inner loop as it was before 0.4.0 (BeliefGrid.Predict, 1fada06), frozen
    // here: HandMotion.Today must give exactly these numbers.
    static void FrozenSpread(BeliefGrid g, float[] b, float[] scratch, float[] inflow, float dt, float speed)
    {
        StoreMap map = g.Map;
        float mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * g.MovingProbability;
        for (int c = 0; c < b.Length; c++)
        {
            if (!g.IsHers(c)) continue;
            float mass = b[c];
            if (mass <= 0f) continue;
            int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
            if (start == end) { scratch[c] += mass; continue; }
            float leaving = mass * mobility;
            scratch[c] += mass - leaving;
            float total = 0f;
            float here = g.DesireDistance[c];
            for (int e = start; e < end; e++) total += FrozenWeight(g, here, map.EdgeTo[e]);
            for (int e = start; e < end; e++)
            {
                int n = map.EdgeTo[e];
                float share = leaving * FrozenWeight(g, here, n) / total;
                if (g.IsHers(n)) scratch[n] += share;
                else inflow[g.ExitIndexOf(c)] += share;
            }
        }
    }

    static float FrozenWeight(BeliefGrid g, float hereDesire, int n)
    {
        float w = 1f;
        float there = g.DesireDistance[n];
        if (!float.IsInfinity(hereDesire) && !float.IsInfinity(there))
            w *= 1f + g.DesireGain * 0.5f * Mathf.Clamp((hereDesire - there) / StoreMap.CellSize, -1f, 1f);
        w *= 1f + g.HabitGain * g.HabitPrior[n];
        w *= 1f - g.DangerAvoidance * Mathf.Clamp01(g.Danger[n]);
        return Mathf.Max(0.02f, w);
    }

    sealed class Frozen : IMotionModel
    {
        public string Id => "frozen";
        public void Spread(BeliefGrid grid, float[] from, float[] to, float[] outflow, float dt, float speed) => FrozenSpread(grid, from, to, outflow, dt, speed);
    }

    [Test]
    public void TodaysStep_BehindTheInterface_IsBitForBit([Values(false, true)] bool withOutside)
    {
        StoreMap map = Store(withOutside);
        var today = new BeliefGrid(map);
        var frozen = new BeliefGrid(map) { Motion = new Frozen() };
        Assert.AreSame(HandMotion.Today, today.Motion, "today's step by default");
        foreach (BeliefGrid g in new[] { today, frozen }) { Layers(g, 3); g.ResetToPrior(); }
        var rnd = new System.Random(8);
        for (int step = 0; step < 200; step++)
        {
            if (step % 7 == 0)
            {
                int cell = rnd.Next(map.CellCount);
                var o = new Observation(step % 14 == 0 ? SenseChannel.Hearing : SenseChannel.Sight, map.CellPosition[cell], 0.4f + 0.5f * (float)rnd.NextDouble(), 1f + 3f * (float)rnd.NextDouble(), "test");
                today.Apply(o);
                frozen.Apply(o);
            }
            float dt = 0.1f, speed = 2f + 3f * (float)rnd.NextDouble();
            today.Predict(dt, speed);
            frozen.Predict(dt, speed);
            float[] a = today.Snapshot(), b = frozen.Snapshot();
            for (int c = 0; c < a.Length; c++)
                Assert.AreEqual(b[c], a[c], 0f, $"step {step}, cell {c}: the same belief, number for number");
        }
    }

    [Test]
    public void TheLadder_ParsesAndP0IsToday()
    {
        Assert.AreSame(HandMotion.Today, MotionModels.Create("p0", null));
        Assert.AreSame(HandMotion.Today, MotionModels.Create("hand", null));
        Assert.AreEqual("p1", MotionModels.Create("p1", null).Id);
        Assert.IsInstanceOf<DestinationMotion>(MotionModels.Create("p2", new MotionFeatures()));
        Assert.IsTrue(((DestinationMotion)MotionModels.Create("p3", new MotionFeatures())).ReadsLedger);
        Assert.IsNull(MotionModels.Create("m4", null), "PR #68's M2–M4 are dropped");
    }

    // ---- C4.4 every model keeps mass on walkable edges, adding up to 1 ---------------------------

    static MotionFeatures Features(StoreMap map, BeliefGrid g)
    {
        var f = new MotionFeatures { SheIsThought = map.CellPosition[30], LastSeenAt = map.CellPosition[2], LastSeenHeading = Vector3.right, TimeLeft01 = 0.5f };
        var places = new List<Destination>
        {
            new Destination { Kind = DestinationKind.Job, Name = "a spill", Cell = 5, Position = map.CellPosition[5], Waited = 10f, AnswerSeconds = -1f },
            new Destination { Kind = DestinationKind.Clock, Name = "the time clock", Cell = 40, Position = map.CellPosition[40], AnswerSeconds = -1f },
            new Destination { Kind = DestinationKind.Stay, Name = "staying here", Cell = -1, AnswerSeconds = -1f },
            new Destination { Kind = DestinationKind.TurnBack, Name = "turned back", Cell = -1, AnswerSeconds = -1f },
        };
        f.Compute(map, g, places);
        return f;
    }

    [Test]
    public void EveryModel_MovesMassOnlyAlongWalkableEdges_AndKeepsItAll([Values("p0", "p1", "p2", "p3")] string id)
    {
        StoreMap map = Store(false);
        var g = new BeliefGrid(map);
        MotionFeatures f = Features(map, g);
        g.Motion = MotionModels.Create(id, f);
        Layers(g, 4);
        var from = new float[map.CellCount];
        from[20] = 1f;
        var to = new float[map.CellCount];
        g.Motion.Spread(g, from, to, null, 0.1f, 4f);
        Assert.AreEqual(1f, to.Sum(), 1e-5f, "nothing made or lost");
        var reach = new HashSet<int> { 20 };
        for (int e = map.EdgeStart[20]; e < map.EdgeStart[21]; e++) reach.Add(map.EdgeTo[e]);
        for (int c = 0; c < to.Length; c++)
            if (!reach.Contains(c)) Assert.AreEqual(0f, to[c], $"{id}: mass jumped to cell {c}, not a neighbour");
    }

    [Test]
    public void Destinations_FlowTowardTheLikelyPlace()
    {
        StoreMap map = Store(false);
        var g = new BeliefGrid(map);
        MotionFeatures f = Features(map, g);
        int cell = 20;
        float toJob = map.Distances(5)[cell];
        int downhill = -1;
        float[] d = map.Distances(5);
        for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
            if (d[map.EdgeTo[e]] < toJob && f.EdgeRate[e] > 0f) downhill = map.EdgeTo[e];
        Assert.GreaterOrEqual(downhill, 0, "some mass heads down the way to the job");
        for (int c = 0; c < map.CellCount; c++)
        {
            float sum = 0f;
            for (int k = 0; k < f.Count; k++) sum += f.Chance(c, k);
            Assert.AreEqual(1f, sum, 1e-4f, "the chances from every cell add up");
        }
    }

    // ---- C4.2 the C# filter and the Python replay agree (the fixture is Python's) -------------------

    [Test]
    public void ThePythonReplay_AndTheCSharpFilter_Agree()
    {
        StoreMap map = StoreMap.Synthetic(BeliefParityFixture.Columns, BeliefParityFixture.Rows, BeliefParityFixture.Open);
        var g = new BeliefGrid(map);
        int n = map.CellCount;
        var features = new MotionFeatures { SheIsThought = new Vector3(7.5f, 0f, 7.5f), LastSeenAt = new Vector3(3f, 0f, 2f), LastSeenHeading = Vector3.right, TimeLeft01 = 0.5f, StoreOpen = true };
        int step = 0;
        void Check(string what)
        {
            float[] b = g.Snapshot();
            for (int c = 0; c < n; c++)
                Assert.AreEqual(BeliefParityFixture.Expected[step * n + c], b[c], 1e-5f, $"step {step} ({what}), cell {c}");
            step++;
        }
        float[] desire = map.DistancesFrom(new List<int> { 5, 40 });
        System.Array.Copy(desire, g.DesireDistance, n);
        Check("desire");
        g.ResetToPrior(); Check("prior");
        g.Apply(new Observation(SenseChannel.Sight, new Vector3(3f, 0f, 2f), 0.8f, 1.2f, "s")); Check("sight");
        g.Predict(0.1f, 4f); Check("predict");
        g.Predict(0.1f, 4f); Check("predict");
        g.Apply(new Observation(SenseChannel.Hearing, map.CellPosition[30], 0.5f, 2f, "h")); Check("hearing");
        g.Predict(0.1f, 3f); Check("predict");
        g.ApplySweep(new List<int> { 0, 1, 2, 3 }, new List<float> { 0.6f, 0.6f, 0.3f, 0.2f }); Check("sweep");
        g.Predict(0.1f, 3f); Check("predict");
        g.Bias(3, 0.05f); Check("bias");
        g.Predict(0.1f, 4f); Check("predict");
        g.Motion = MotionModels.Create("p1", features); Check("p1");
        features.Compute(map, g, new List<Destination> { new Destination { Kind = DestinationKind.Stay, Cell = -1, AnswerSeconds = -1f } });
        features.WriteDanger(g.Danger); Check("danger");
        g.Predict(0.1f, 4f); Check("p1 predict");
        g.Predict(0.1f, 4f); Check("p1 predict");
        g.Motion = MotionModels.Create("p2", features); Check("p2");
        features.Compute(map, g, new List<Destination>
        {
            new Destination { Kind = DestinationKind.Job, Cell = 5, Position = map.CellPosition[5], Waited = 10f, AnswerSeconds = -1f },
            new Destination { Kind = DestinationKind.Clock, Cell = 40, Position = map.CellPosition[40], AnswerSeconds = -1f },
            new Destination { Kind = DestinationKind.Stay, Cell = -1, AnswerSeconds = -1f },
        });
        Check("places");
        g.Predict(0.1f, 4f); Check("p2 predict");
        g.Predict(0.1f, 4f); Check("p2 predict");
        g.Predict(0.1f, 4f); Check("p2 predict");
        Assert.AreEqual(BeliefParityFixture.Steps, step);
    }

    // ---- C4.7 her senses' table: S0 is today's numbers ---------------------------------------------

    [Test]
    public void Senses_S0IsTodaysNumbers()
    {
        SenseModel s = SenseModel.For("s0");
        for (float a = 0f; a <= 1f; a += 0.05f) Assert.AreEqual(Mathf.Lerp(3f, 0.8f, a), s.SightSigma(a), 0f);
        for (float d = 0f; d < 20f; d += 0.7f)
        {
            Assert.AreEqual(Mathf.Sqrt(1f - d / 25f) * 0.9f, s.HearingConfidence(d, 25f), 0f);
            Assert.AreEqual(1f + d * 0.12f, s.HearingSigma(d, d * 0.5f, true), 0f, "S0 ignores walls and the dark");
        }
        Assert.AreEqual(2.5f + 12f * 0.25f, s.TestimonySigma(12f), 0f);
        s.Trace(TraceKind.WetFootprint, 0.5f, 3f, out float conf, out float sigma);
        Assert.AreEqual(0.55f * 0.5f, conf, 0f);
        Assert.AreEqual(3f, sigma, 0f);
        s.Trace(TraceKind.DroppedItem, 1f, 10f, out conf, out sigma);
        Assert.AreEqual(5f + 10f * 0.1f, sigma, 0f);
        Assert.AreEqual(0.85f, s.SweepScale, 0f);
        SenseModel s2 = SenseModel.For("s2");
        s2.WallGain = 0.5f;
        Assert.Greater(s2.HearingSigma(10f, 5f, false), s.HearingSigma(10f, 5f, false), "S2: a noise through a wall gets a wider circle");
        Assert.IsFalse(SenseModel.For("s1").Trained, "nothing is fitted yet: S1 falls back to today's numbers, and says so");
    }

    // ---- C4.8 the mirror map --------------------------------------------------------------------------

    [Test]
    public void Mirror_K0IsWhereSheIs_K1AndK2RememberAndSpread()
    {
        StoreMap map = Store(false);
        var k0 = new MirrorBelief(map, MirrorMode.K0);
        var k1 = new MirrorBelief(map, MirrorMode.K1);
        var k2 = new MirrorBelief(map, MirrorMode.K2);
        Vector3 start = map.CellPosition[2], now = map.CellPosition[40];
        foreach (var m in new[] { k0, k1, k2 }) { m.Reset(start, 0f); m.SawEachOther(start, 3f, 0f); }
        Assert.AreEqual(now, k0.Thought(now), "K0: you always know where she is (today)");
        Assert.AreEqual(start, k1.Thought(now), "K1: where you last sensed her");
        for (int i = 0; i < 100; i++) { k1.Predict(0.1f); k2.Predict(0.1f); }
        Assert.AreEqual(start, k1.Thought(now), "K1 doesn't spread");
        Assert.Less(k2.Mass(map.CellAt(start)), 0.9f, "K2 spreads while you can't sense her");
        Assert.AreEqual(1f, Enumerable.Range(0, map.CellCount).Sum(c => k2.Mass(c)), 1e-4f);
        Assert.AreEqual(10f, k2.Stale(10f), 1e-5f);
        Assert.AreEqual(0f, k0.Stale(10f));
    }

    // A false cue (her phantom chime) moves your picture of her to where it sounded.
    [Test]
    public void Mirror_AFalseCueMovesYourPicture()
    {
        StoreMap map = Store(false);
        var g = new BeliefGrid(map);
        g.ResetTo(map.CellPosition[20], 1f);
        var k2 = new MirrorBelief(map, MirrorMode.K2);
        k2.Reset(map.CellPosition[2], 0f);
        k2.MadeNoise(map.CellPosition[22], 30f, g, 5f, falseCue: true);
        Assert.Less(Vector3.Distance(k2.LastSensedAt, map.CellPosition[22]), 0.01f);
    }

    // ---- the "moved player" test (§5.6): her model inputs don't change when you move unseen ---------

    [Test]
    public void MovedPlayer_DoesNotChangeTheMirrorOrTheDestinations()
    {
        var player = new GameObject("player (test)") { tag = "Player" };
        try
        {
            PlayerPresence presence = player.AddComponent<PlayerPresence>();
            typeof(PlayerPresence).GetProperty("Current").SetValue(null, presence);
            Assert.AreSame(presence, PlayerPresence.Current, "a real, current player: the test isn't vacuous");

            StoreMap map = Store(false);
            (float[] mirror, float[] rates, Vector3 thought) Inputs()
            {
                var g = new BeliefGrid(map);
                g.ResetTo(map.CellPosition[20], 2f);
                var k2 = new MirrorBelief(map, MirrorMode.K2);
                k2.Reset(map.CellPosition[2], 0f);
                k2.SawEachOther(map.CellPosition[2], 4f, 1f);
                k2.MadeNoise(map.CellPosition[30], 15f, g, 2f);
                for (int i = 0; i < 20; i++) k2.Predict(0.1f);
                MotionFeatures f = Features(map, g);
                return (Enumerable.Range(0, map.CellCount).Select(k2.Mass).ToArray(), (float[])f.EdgeRate.Clone(), k2.Thought(map.CellPosition[2]));
            }

            player.transform.position = map.CellPosition[3];
            var before = Inputs();
            player.transform.position = map.CellPosition[45] + new Vector3(0.3f, 0f, 0.2f);
            Assert.AreNotEqual(map.CellPosition[3], PlayerPresence.Current.Position, "the player really moved");
            var after = Inputs();
            CollectionAssert.AreEqual(before.mirror, after.mirror, "the mirror map");
            CollectionAssert.AreEqual(before.rates, after.rates, "the destinations' flows");
            Assert.AreEqual(before.thought, after.thought);
        }
        finally
        {
            typeof(PlayerPresence).GetProperty("Current").SetValue(null, null);
            Object.DestroyImmediate(player);
        }
    }

    // ---- C4.5 her route guesses on the same model; C4.6 tricks on your way --------------------------

    [Test]
    public void RouteGuesses_TakeTheModelsChances_WhenGiven()
    {
        StoreMap map = Store(false);
        var p = new RoutePredictor(map);
        var goals = new List<(string, Vector3)> { ("a spill", map.CellPosition[5]), ("the time clock", map.CellPosition[40]) };
        p.Build(map.CellPosition[20], Vector3.zero, 1f, goals, (name, at, kind) => name == "the time clock" ? 0.8f : kind == GuessKind.Goal ? 0.1f : 0.05f);
        Assert.AreEqual("the time clock", p.Best.Name, "the model's chance decides");
        Assert.AreEqual(1f, p.Guesses.Sum(g => g.Weight), 1e-5f);
        var today = new RoutePredictor(map);
        today.Build(map.CellPosition[20], Vector3.zero, 1f, goals);
        Assert.AreEqual(0.1f / today.Guesses.Sum(g => g.Prior), today.Guesses.First(g => g.Kind == GuessKind.Hiding).Weight, 1e-6f, "no model: today's priors");
    }

    [Test]
    public void TricksOnYourWay_OnlyWithTheDestinationModel()
    {
        StoreMap map = Store(false);
        var config = new KarenConfig();
        var c = new KarenContext { Map = map, Belief = new BeliefGrid(map), Config = config };
        Assert.AreEqual(0f, TacticHelpers.OnGuessedRoutes(c, map.Regions[0]), "today's step: today's placements");
        var bet = new TrickBet { Trick = "spill", At = Vector3.zero, Radius = 4f, Horizon = 60f };
        Assert.IsFalse(bet.Settled);
    }
}
