using System.Collections.Generic;
using System.Linq;
using Kehai;
using Kehai.Eval;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// The belief grid's motion models (Belief/MotionModel.cs, LearnedMotion.cs) on synthetic maps,
// so none of this needs the store scene:
//   - the shipped hand step is bit for bit the old Predict, through a long mixed sequence;
//   - every model keeps Σb = 1 and moves mass only along edges;
//   - a grid with headings reads like one without when its headings are uniform;
//   - the generated weights have their shapes, and the C# kernels equal PyTorch's (to 1e-5) on
//     the fixed state tools/belief wrote into MotionWeights.g.cs — the C# parity gate;
//   - a shadow filter is the filter without the Director's hint, exactly.
public class MotionModelTests
{
    // 20 × 14, with shelving.
    static StoreMap Store()
    {
        const int cols = 20, rows = 14;
        var open = new bool[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                open[r * cols + c] = !((c % 4 == 2 && r > 1 && r < rows - 2) || (r == 6 && c > 12 && c < 17));
        return StoreMap.Synthetic(cols, rows, open);
    }

    static void Layers(StoreMap map, float[] desire, float[] habit)
    {
        float[] d = map.DistancesFrom(new List<int> { 3, map.CellCount / 2, map.CellCount - 5 });
        System.Array.Copy(d, desire, d.Length);
        for (int c = 0; c < map.CellCount; c++)
        {
            habit[c] = 0.5f + 0.5f * Mathf.Sin(c * 0.37f);
            if (c % 23 == 0) desire[c] = float.PositiveInfinity;
        }
    }

    static Observation Sight(Vector3 at, float conf) => new Observation(SenseChannel.Sight, at, conf, Mathf.Lerp(3f, 0.8f, conf), "test sight");

    // ---- m0 is the code that shipped -----------------------------------------------------------

    [Test]
    public void HandMotion_MatchesTheOldPredictBitForBit()
    {
        StoreMap map = Store();
        var now = new BeliefGrid(map);
        var old = new LegacyBeliefGrid(map);
        Layers(map, now.DesireDistance, now.HabitPrior);
        Layers(map, old.DesireDistance, old.HabitPrior);
        for (int c = 0; c < map.CellCount; c += 7) { now.Danger[c] = 0.3f; old.Danger[c] = 0.3f; }
        now.ResetToPrior();
        old.ResetToPrior();
        now.ResetTo(map.CellPosition[10], 3f);
        old.ResetTo(map.CellPosition[10], 3f);
        Same(now, old, "after the reset");

        var swept = new List<int>();
        var probability = new List<float>();
        for (int i = 0; i < 300; i++)
        {
            Vector3 at = map.CellPosition[(i * 37) % map.CellCount];
            if (i % 7 == 0) { now.Apply(Sight(at, 0.4f + 0.05f * (i % 10))); old.Apply(Sight(at, 0.4f + 0.05f * (i % 10))); }
            if (i % 11 == 0)
            {
                var noise = new Observation(SenseChannel.Hearing, at + Vector3.right * 0.4f, 0.6f, 2.5f, "test noise");
                now.Apply(noise);
                old.Apply(noise);
            }
            if (i % 13 == 0)
            {
                var told = new Observation(SenseChannel.Testimony, at, 0.7f, 5f, "test testimony");
                now.Apply(told);
                old.Apply(told);
            }
            if (i % 5 == 0)
            {
                map.CellsWithin(map.CellPosition[(i * 13) % map.CellCount], 6f, swept);
                probability.Clear();
                for (int k = 0; k < swept.Count; k++) probability.Add(0.2f + 0.7f * ((k * 7 + i) % 10) / 10f);
                now.ApplySweep(swept, probability);
                old.ApplySweep(swept, probability);
            }
            if (i % 17 == 0) { now.Bias(map.CellRegion[(i * 5) % map.CellCount], 0.05f); old.Bias(map.CellRegion[(i * 5) % map.CellCount], 0.05f); }
            float dt = 0.1f + 0.0333f * (i % 3), speed = i % 40 < 20 ? 3.1f : 2f;
            now.Predict(dt, speed);
            old.Predict(dt, speed);
            Same(now, old, "at step " + i);
        }
        now.ApplySweep(Enumerable.Range(0, map.CellCount).ToList(), Enumerable.Repeat(1f, map.CellCount).ToList());
        old.ApplySweep(Enumerable.Range(0, map.CellCount).ToList(), Enumerable.Repeat(1f, map.CellCount).ToList());
        Same(now, old, "after everything was ruled out");
        Assert.AreEqual(old.Collapses, now.Collapses);
        Assert.Greater(now.Collapses, 0, "the last sweep should have collapsed belief to the prior");
    }

    static void Same(BeliefGrid now, LegacyBeliefGrid old, string when)
    {
        for (int c = 0; c < now.CellCount; c++)
            if (now[c] != old[c])
                Assert.Fail($"{when}: cell {c} is {now[c]:R}, the old grid had {old[c]:R}");
        Assert.AreEqual(old.PeakCell, now.PeakCell, when);
        Assert.AreEqual(old.Entropy, now.Entropy, when);
    }

    // ---- every model ----------------------------------------------------------------------------

    static MotionFeatures Features(StoreMap map)
    {
        var f = new MotionFeatures();
        f.FillSynthetic(map);
        return f;
    }

    [Test]
    public void EveryModel_KeepsMassAndMovesOnlyAlongEdges()
    {
        StoreMap map = Store();
        MotionFeatures f = Features(map);
        foreach (string id in MotionModels.Ladder)
        {
            IMotionModel model = MotionModels.Create(id, f);
            var grid = new BeliefGrid(map, model) { DesireDistance = f.Desire, HabitPrior = f.Habit };
            int hc = model.Headings;
            foreach (int cell in new[] { 0, map.CellCount / 3, map.CellCount - 1 })
                for (int h = 0; h < hc; h++)
                {
                    var from = new float[map.CellCount * hc];
                    var to = new float[from.Length];
                    from[cell * hc + h] = 1f;
                    model.Transition(grid, from, to, 0.1333f, 3.1f);
                    Assert.AreEqual(1f, to.Sum(), 1e-5f, $"{id}: mass from cell {cell} heading {h}");
                    var allowed = new HashSet<int> { cell };
                    for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++) allowed.Add(map.EdgeTo[e]);
                    for (int s = 0; s < to.Length; s++)
                    {
                        Assert.GreaterOrEqual(to[s], 0f, $"{id}: negative mass");
                        if (to[s] > 0f) Assert.IsTrue(allowed.Contains(s / hc), $"{id}: mass jumped from {cell} to {s / hc}");
                    }
                }

            // Through the whole filter: a sighting, then a while unobserved, still sums to one.
            grid.Apply(Sight(map.CellPosition[5], 0.9f));
            for (int i = 0; i < 50; i++) grid.Predict(0.1333f, 3.1f);
            float sum = 0f;
            for (int c = 0; c < grid.CellCount; c++) sum += grid[c];
            Assert.AreEqual(1f, sum, 1e-4f, id);
            Assert.AreEqual(1f, grid.RegionMass.Sum(), 1e-4f, id);
        }
    }

    // ---- headings ---------------------------------------------------------------------------------

    // A heading model that moves exactly like m0 and spreads every cell's mass evenly over the
    // nine headings: its grid must read like m0's.
    sealed class UniformHeadings : IMotionModel
    {
        readonly HandMotion hand = new HandMotion();
        float[] cells, moved;
        public string Id => "uniform";
        public int Headings => 9;
        public float Leak => hand.Leak;
        public float[] HeadingPrior { get; } = Enumerable.Repeat(1f / 9f, 9).ToArray();

        public void Transition(BeliefGrid grid, float[] from, float[] to, float dt, float speed)
        {
            int n = grid.CellCount;
            if (cells == null || cells.Length != n) { cells = new float[n]; moved = new float[n]; }
            for (int c = 0; c < n; c++) { float m = 0f; for (int h = 0; h < 9; h++) m += from[c * 9 + h]; cells[c] = m; }
            hand.Transition(grid, cells, moved, dt, speed);
            for (int c = 0; c < n; c++) for (int h = 0; h < 9; h++) to[c * 9 + h] = moved[c] / 9f;
        }
    }

    [Test]
    public void Headings_ReadLikeNoHeadingsWhenUniform()
    {
        StoreMap map = Store();
        var plain = new BeliefGrid(map);
        var headed = new BeliefGrid(map, new UniformHeadings());
        Assert.AreEqual(9, headed.Headings);
        Layers(map, plain.DesireDistance, plain.HabitPrior);
        headed.DesireDistance = plain.DesireDistance;
        headed.HabitPrior = plain.HabitPrior;
        plain.ResetTo(map.CellPosition[40], 3f);
        headed.ResetTo(map.CellPosition[40], 3f);

        var swept = new List<int>();
        for (int i = 0; i < 120; i++)
        {
            Vector3 at = map.CellPosition[(i * 29) % map.CellCount];
            if (i % 6 == 0) { plain.Apply(Sight(at, 0.6f)); headed.Apply(Sight(at, 0.6f)); }
            if (i % 9 == 0)
            {
                var noise = new Observation(SenseChannel.Hearing, at, 0.5f, 3f, "test noise");
                plain.Apply(noise);
                headed.Apply(noise);
            }
            if (i % 4 == 0)
            {
                map.CellsWithin(at, 5f, swept);
                var p = swept.Select(_ => 0.6f).ToList();
                plain.ApplySweep(swept, p);
                headed.ApplySweep(swept, p);
            }
            if (i % 15 == 0) { plain.Bias(1, 0.1f); headed.Bias(1, 0.1f); }
            plain.Predict(0.1333f, 3.1f);
            headed.Predict(0.1333f, 3.1f);
            for (int c = 0; c < map.CellCount; c++)
                Assert.AreEqual(plain[c], headed[c], 1e-6f + 1e-4f * plain[c], $"cell {c} at step {i}");
        }
        float headingSum = 0f;
        for (int h = 0; h < 9; h++) headingSum += headed.HeadingMass(h);
        Assert.AreEqual(1f, headingSum, 1e-4f);
    }

    // ---- the generated weights ------------------------------------------------------------------------

    [Test]
    public void GeneratedWeights_HaveTheirShapes()
    {
        foreach (LearnedMotion.Kind kind in System.Enum.GetValues(typeof(LearnedMotion.Kind)))
        {
            float[] pack = MotionWeights.Pack(kind);
            Assert.AreEqual(LearnedMotion.PackSize(kind), pack.Length, kind.ToString());
            Assert.IsTrue(pack.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), kind + " has a NaN or ∞");
        }
        HandMotion.Gains g = MotionWeights.M1Gains;
        Assert.That(g.MovingProbability, Is.InRange(0f, 1f));
        Assert.That(g.DangerAvoidance, Is.InRange(0f, 1f));
        Assert.Greater(g.Uncertainty, 0f);
        Assert.Greater(g.DangerRadius, 0f);
        CollectionAssert.Contains(MotionModels.Ladder, MotionWeights.Best);
        Assert.IsNotNull(MotionModels.Create(MotionWeights.Best, new MotionFeatures()));
    }

    // The C# parity gate: one kernel step on the fixed synthetic state, against PyTorch.
    [Test]
    public void LearnedKernels_MatchPyTorch()
    {
        StoreMap map = StoreMap.Synthetic(MotionWeights.FixtureColumns, MotionWeights.FixtureRows, MotionWeights.FixtureOpenCells);
        MotionFeatures f = Features(map);
        foreach (string id in MotionModels.Ladder)
        {
            IMotionModel model = MotionModels.Create(id, f);
            var grid = new BeliefGrid(map, model) { DesireDistance = f.Desire, HabitPrior = f.Habit };
            float[] from = (model.Headings == 1 ? MotionWeights.FixtureFromCells : MotionWeights.FixtureFromHeadings).ToArray();
            var to = new float[from.Length];
            model.Transition(grid, from, to, MotionWeights.FixtureDt, MotionWeights.FixtureSpeed);
            float[] expected = MotionWeights.FixtureExpected(id);
            Assert.AreEqual(expected.Length, to.Length, id);
            float worst = 0f;
            for (int s = 0; s < to.Length; s++) worst = Mathf.Max(worst, Mathf.Abs(to[s] - expected[s]));
            Assert.LessOrEqual(worst, 1e-5f, $"{id}: the C# kernel is {worst:E2} from PyTorch's");
        }
    }

    // ---- shadows ------------------------------------------------------------------------------------

    [Test]
    public void Shadow_IsThePrimaryWithoutTheHint()
    {
        StoreMap map = Store();
        MotionFeatures f = Features(map);
        var primary = new BeliefGrid(map);
        var reference = new BeliefGrid(map);
        Layers(map, primary.DesireDistance, primary.HabitPrior);
        System.Array.Copy(primary.DesireDistance, reference.DesireDistance, map.CellCount);
        System.Array.Copy(primary.HabitPrior, reference.HabitPrior, map.CellCount);
        BeliefShadows shadows = BeliefShadows.Beside(primary, f, new[] { "m0", "m1", "m4" });
        try
        {
            primary.ResetTo(map.CellPosition[7], 3f);
            reference.ResetTo(map.CellPosition[7], 3f);
            var swept = new List<int>();
            for (int i = 0; i < 150; i++)
            {
                Vector3 at = map.CellPosition[(i * 41) % map.CellCount];
                if (i % 8 == 0) { primary.Apply(Sight(at, 0.7f)); reference.Apply(Sight(at, 0.7f)); }
                if (i % 3 == 0)
                {
                    map.CellsWithin(at, 4f, swept);
                    var p = swept.Select(_ => 0.5f).ToList();
                    primary.ApplySweep(swept, p);
                    reference.ApplySweep(swept, p);
                }
                if (i % 5 == 0) primary.Bias(map.CellRegion[(i * 3) % map.CellCount], 0.1f);   // the hint: hers only
                primary.Predict(0.1333f, 3.1f);
                reference.Predict(0.1333f, 3.1f);
            }
            BeliefGrid m0 = shadows.Grid("m0");
            bool differs = false;
            for (int c = 0; c < map.CellCount; c++)
            {
                Assert.AreEqual(reference[c], m0[c], $"cell {c}: the m0 shadow isn't the filter without the hint");
                differs |= primary[c] != reference[c];
            }
            Assert.IsTrue(differs, "the hint should have changed her own grid");
            Assert.AreEqual(1f, shadows.Grid("m4").RegionMass.Sum(), 1e-4f);
            Assert.AreEqual(3, shadows.Ids.Count);
        }
        finally { shadows.Detach(); }
    }
}

// The belief grid as it was before the motion models (git c8c4600, Belief/BeliefGrid.cs), cut
// to what HandMotion_MatchesTheOldPredictBitForBit runs. Kept only as that test's reference.
sealed class LegacyBeliefGrid
{
    readonly StoreMap map;
    readonly float[] b, scratch, prior, regionMass;
    float[] distanceScratch;
    readonly bool[] stamp;
    readonly List<int> nearby = new List<int>();

    public float[] DesireDistance, HabitPrior, Danger;
    public int CellCount => b.Length;
    public float this[int cell] => b[cell];
    public int PeakCell { get; private set; }
    public int PeakRegion { get; private set; }
    public float Entropy { get; private set; }
    public int Collapses { get; private set; }

    public float Floor = 1e-4f, MovingProbability = 0.65f, DesireGain = 0.9f, HabitGain = 1.2f, DangerAvoidance = 0.6f, Uncertainty = 0.015f;

    public LegacyBeliefGrid(StoreMap map)
    {
        this.map = map;
        b = new float[map.CellCount];
        scratch = new float[map.CellCount];
        prior = new float[map.CellCount];
        stamp = new bool[map.CellCount];
        regionMass = new float[map.Regions.Count];
        DesireDistance = new float[map.CellCount];
        HabitPrior = new float[map.CellCount];
        Danger = new float[map.CellCount];
        for (int i = 0; i < DesireDistance.Length; i++) DesireDistance[i] = float.PositiveInfinity;
        ResetToPrior();
    }

    public void RebuildPrior()
    {
        float sum = 0f;
        for (int c = 0; c < prior.Length; c++)
        {
            Region region = map.Regions[map.CellRegion[c]];
            float w = StoreMap.IsOutdoors(region.Area) ? 0.25f : 1f;
            if (!float.IsInfinity(DesireDistance[c])) w *= 1f + 1.5f * Mathf.Exp(-DesireDistance[c] / 8f);
            w *= 1f + HabitGain * HabitPrior[c];
            prior[c] = w;
            sum += w;
        }
        for (int c = 0; c < prior.Length; c++) prior[c] /= sum;
    }

    public void ResetToPrior()
    {
        RebuildPrior();
        System.Array.Copy(prior, b, b.Length);
        Refresh();
    }

    public void ResetTo(Vector3 position, float sigma)
    {
        System.Array.Copy(prior, b, b.Length);
        Multiply(position, sigma, 1f);
        Normalise();
    }

    public void Predict(float dt, float speed)
    {
        float mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * MovingProbability;
        System.Array.Clear(scratch, 0, scratch.Length);
        for (int c = 0; c < b.Length; c++)
        {
            float mass = b[c];
            if (mass <= 0f) continue;
            int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
            if (start == end) { scratch[c] += mass; continue; }
            float leaving = mass * mobility;
            scratch[c] += mass - leaving;
            float total = 0f;
            float here = DesireDistance[c];
            for (int e = start; e < end; e++) total += Weight(here, map.EdgeTo[e]);
            for (int e = start; e < end; e++) scratch[map.EdgeTo[e]] += leaving * Weight(here, map.EdgeTo[e]) / total;
        }
        float u = Mathf.Clamp01(Uncertainty * dt);
        for (int c = 0; c < b.Length; c++) b[c] = (1f - u) * scratch[c] + u * prior[c];
        Refresh();
    }

    float Weight(float hereDesire, int n)
    {
        float w = 1f;
        float there = DesireDistance[n];
        if (!float.IsInfinity(hereDesire) && !float.IsInfinity(there))
            w *= 1f + DesireGain * 0.5f * Mathf.Clamp((hereDesire - there) / StoreMap.CellSize, -1f, 1f);
        w *= 1f + HabitGain * HabitPrior[n];
        w *= 1f - DangerAvoidance * Mathf.Clamp01(Danger[n]);
        return Mathf.Max(0.02f, w);
    }

    public bool Apply(in Observation o)
    {
        if (o.IsNegative) return false;
        switch (o.Channel)
        {
            case SenseChannel.Hearing: MultiplyAlongFloor(o.Position, o.Sigma, o.Confidence); break;
            case SenseChannel.Trace:
                Vector3 centre = o.Position;
                if (o.Heading.sqrMagnitude > 0.01f) centre += o.Heading.normalized * Mathf.Min(6f, 1.5f + (Time.time - o.Timestamp) * 1.2f);
                MultiplyAlongFloor(centre, o.Sigma, o.Confidence);
                break;
            default: Multiply(o.Position, o.Sigma, o.Confidence); break;
        }
        return Normalise();
    }

    public bool ApplySweep(List<int> cells, List<float> detectProbability)
    {
        for (int i = 0; i < cells.Count; i++) b[cells[i]] *= 1f - Mathf.Clamp01(detectProbability[i]);
        return Normalise();
    }

    void Multiply(Vector3 position, float sigma, float confidence)
    {
        map.CellsWithin(position, sigma * 3f, nearby);
        for (int i = 0; i < nearby.Count; i++) stamp[nearby[i]] = true;
        float floor = Mathf.Pow(Floor, Mathf.Clamp01(confidence));
        float inv = 1f / (2f * sigma * sigma);
        for (int c = 0; c < b.Length; c++)
        {
            if (!stamp[c]) { b[c] *= floor; continue; }
            Vector3 d = map.CellPosition[c] - position;
            d.y = 0f;
            b[c] *= floor + Mathf.Exp(-d.sqrMagnitude * inv);
        }
        for (int i = 0; i < nearby.Count; i++) stamp[nearby[i]] = false;
    }

    void MultiplyAlongFloor(Vector3 position, float sigma, float confidence)
    {
        int source = map.CellAt(position);
        if (source < 0) { Multiply(position, sigma, confidence); return; }
        distanceScratch = map.Distances(source, sigma * 3f, distanceScratch);
        float floor = Mathf.Pow(Floor, Mathf.Clamp01(confidence));
        float inv = 1f / (2f * sigma * sigma);
        for (int c = 0; c < b.Length; c++)
        {
            float d = distanceScratch[c];
            b[c] *= float.IsInfinity(d) ? floor : floor + Mathf.Exp(-d * d * inv);
        }
    }

    public void Bias(int region, float amount)
    {
        if (region < 0 || amount <= 0f) return;
        Region r = map.Regions[region];
        if (r.Cells.Count == 0) return;
        amount = Mathf.Clamp01(amount);
        for (int c = 0; c < b.Length; c++) b[c] *= 1f - amount;
        float share = amount / r.Cells.Count;
        foreach (int c in r.Cells) b[c] += share;
        Normalise();
    }

    bool Normalise()
    {
        float sum = 0f;
        for (int c = 0; c < b.Length; c++) sum += b[c];
        if (sum < 1e-12f || float.IsNaN(sum))
        {
            Collapses++;
            System.Array.Copy(prior, b, b.Length);
            Refresh();
            return true;
        }
        float inv = 1f / sum;
        for (int c = 0; c < b.Length; c++) b[c] *= inv;
        Refresh();
        return false;
    }

    void Refresh()
    {
        System.Array.Clear(regionMass, 0, regionMass.Length);
        float peak = -1f;
        for (int c = 0; c < b.Length; c++)
        {
            regionMass[map.CellRegion[c]] += b[c];
            if (b[c] > peak) { peak = b[c]; PeakCell = c; }
        }
        float best = -1f, entropy = 0f;
        for (int r = 0; r < regionMass.Length; r++)
        {
            float m = regionMass[r];
            if (m > best) { best = m; PeakRegion = r; }
            if (m > 1e-9f) entropy -= m * Mathf.Log(m);
        }
        Entropy = entropy;
    }
}
