using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Rung F (tools/rl/README.md): the pieces of the trained tactic chooser that can be checked
// without the ML-Agents package — the rung itself, the shield, the planner's ordering, and
// what the policy sees and is paid. The agent and the trainer are checked by the smoke run.
public class KarenPolicyTests
{
    static KarenContext Context(KarenRung rung, bool shield = true)
    {
        var config = new KarenConfig { rung = rung, seed = 7, shield = shield };
        var ledger = new KarenLedger(config);
        return new KarenContext { Config = config, Rng = new KarenRng(7), Ledger = ledger, Director = new KarenDirector(config, ledger) };
    }

    [Test]
    public void RungF_ParsesAndIsEWithThePolicy()
    {
        Assert.IsTrue(KarenBootstrap.TryParseRung("f", out KarenRung r) && r == KarenRung.F_Policy);
        Assert.IsTrue(KarenBootstrap.TryParseRung("5", out r) && r == KarenRung.F_Policy);
        Assert.IsFalse(KarenBootstrap.TryParseRung("G", out _));

        var e = new RungFeatures(KarenRung.E_Ledger);
        var f = new RungFeatures(KarenRung.F_Policy);
        Assert.IsFalse(e.Policy);
        Assert.IsTrue(f.Policy);
        // Everything else as E: the arms still learn (goal utility, fallbacks, the teacher).
        Assert.AreEqual(e.Belief, f.Belief);
        Assert.AreEqual(e.Goals, f.Goals);
        Assert.AreEqual(e.Bandit, f.Bandit);
        Assert.AreEqual(e.Persistent, f.Persistent);
    }

    [Test]
    public void ShieldSwitch_ParsesFromTheCommandLine()
    {
        Assert.IsFalse(KarenBootstrap.ParseSwitch("off", true));
        Assert.IsTrue(KarenBootstrap.ParseSwitch(" ON ", false));
        Assert.IsTrue(KarenBootstrap.ParseSwitch("maybe", true), "an unreadable value keeps the default");
    }

    // ---- the shield ---------------------------------------------------------------------------

    [Test]
    public void Shield_OnlyRungFCanDropIt()
    {
        Assert.IsTrue(HtnPlanner.Shielded(Context(KarenRung.E_Ledger, shield: false)), "the shield switch belongs to rung F alone");
        Assert.IsTrue(HtnPlanner.Shielded(Context(KarenRung.F_Policy, shield: true)));
        Assert.IsFalse(HtnPlanner.Shielded(Context(KarenRung.F_Policy, shield: false)));
    }

    [Test]
    public void Shield_GatesTierCooldownAndBudget_ButNeverTheCareerTable()
    {
        Tactic big = TacticLibrary.All.First(t => t.Tier >= 2 && t.IntroducedShift <= 1);
        Tactic later = TacticLibrary.All.First(t => t.IntroducedShift > 1);

        foreach (bool shield in new[] { true, false })
        {
            KarenContext c = Context(KarenRung.F_Policy, shield);
            c.Director.BeginShift(1, 300f);                // settling in: nothing above tier 1
            c.A = new Appraisal { Tension = 0f };          // and no budget

            Assert.AreEqual(!shield, HtnPlanner.Gated(big, c, out string why), $"shield {shield}: {why}");
            Assert.IsFalse(HtnPlanner.Gated(later, c, out why), $"shield {shield}: a tactic from a later shift got through");
        }

        // Cooldown, on its own: tier 1, affordable, just used.
        Tactic cheap = TacticLibrary.All.First(t => t.Tier == 1 && t.IntroducedShift <= 1 && t.Cooldown > 0f && t.TensionCost <= 0.5f);
        float lastUsed = cheap.LastUsed;
        try
        {
            foreach (bool shield in new[] { true, false })
            {
                KarenContext c = Context(KarenRung.F_Policy, shield);
                c.Director.BeginShift(1, 300f);
                c.A = new Appraisal { Tension = 1f };
                cheap.LastUsed = c.Now;
                Assert.AreEqual(!shield, HtnPlanner.Gated(cheap, c, out string why), $"shield {shield}: {why}");
            }
        }
        finally { cheap.LastUsed = lastUsed; }
    }

    [Test]
    public void PacingCheck_CatchesBigTacticsWhileSettlingIn()
    {
        KarenContext c = Context(KarenRung.F_Policy);
        c.Director.BeginShift(1, 300f);
        Tactic small = TacticLibrary.All.First(t => t.Tier == 1);
        Tactic big = TacticLibrary.All.First(t => t.Tier == 3);

        int before = FairnessGuard.Violations;
        Assert.IsTrue(FairnessGuard.CheckPacing(small, c.Director));
        Assert.AreEqual(before, FairnessGuard.Violations);
        Assert.IsFalse(FairnessGuard.CheckPacing(big, c.Director));
        Assert.AreEqual(before + 1, FairnessGuard.Violations);
    }

    // ---- the planner's order ------------------------------------------------------------------

    [Test]
    public void PolicyFirst_PutsThePickFirstAndKeepsTheRestInTheBanditsOrder()
    {
        var tactics = TacticLibrary.All.Where(t => t.Learnable).Take(4).ToList();
        var scored = new List<(Tactic t, float s, string why)>
        {
            (tactics[0], 0.9f, "a"), (tactics[1], 0.7f, "b"), (tactics[2], 0.5f, "c"), (tactics[3], 0.3f, "d")
        };
        HtnPlanner.PolicyFirst(scored, tactics[2], "test");

        CollectionAssert.AreEqual(new[] { tactics[2], tactics[0], tactics[1], tactics[3] }, scored.Select(x => x.t).ToArray());
        Assert.AreEqual(0.5f, scored[0].s, "the pick keeps the bandit's score, so the log stays comparable");
        StringAssert.StartsWith("policy: test", scored[0].why);

        HtnPlanner.PolicyFirst(scored, TacticLibrary.All.First(t => !tactics.Contains(t)), "nowhere");
        Assert.AreEqual(tactics[2], scored[0].t, "a pick that isn't a candidate changes nothing");
    }

    // ---- what it sees -------------------------------------------------------------------------

    [Test]
    public void Mask_OpensExactlyTheCandidates()
    {
        var ids = TacticLibrary.All.Select(TacticLibrary.IndexOf).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(0, TacticLibrary.All.Count).ToList(), ids, "every tactic needs its own action number");

        var allowed = new[] { TacticLibrary.All[2], TacticLibrary.All[5] };
        bool[] mask = PolicyMask.Build(allowed);
        Assert.AreEqual(PolicyMask.Size, mask.Length);
        Assert.AreEqual(2, mask.Count(m => m));
        Assert.IsTrue(mask[2] && mask[5]);
    }

    [Test]
    public void Observation_IsFixedLengthFiniteAndInRange()
    {
        KarenContext c = Context(KarenRung.F_Policy);
        c.Director.BeginShift(3, 300f);
        c.A = new Appraisal { Confidence = 2f, Pressure = -5f, Panic = float.NaN, Staleness = 1e9f, Tension = 0.4f };

        float[] v = PolicyObservation.Build(GoalId.Sweep, c);
        Assert.AreEqual(PolicyObservation.Size, v.Length);
        Assert.IsTrue(v.All(x => !float.IsNaN(x) && !float.IsInfinity(x)), "NaN or infinity reached the network");
        Assert.IsTrue(v.All(x => x >= -1f && x <= 1f), "a value outside [-1, 1]");
        CollectionAssert.AreEqual(v, PolicyObservation.Build(GoalId.Sweep, c), "the same state gave a different observation");
        CollectionAssert.AreNotEqual(v, PolicyObservation.Build(GoalId.Stalk, c), "the goal isn't in the observation");
    }

    // ---- what it's paid -----------------------------------------------------------------------

    [Test]
    public void Reward_IsZeroOnTheSetpointAndMinusOneForAShiftAtTheWorst()
    {
        Assert.AreEqual(0f, PolicyReward.Pacing(0.4f, 0.4f, 0.2f, 150f), 1e-6f);
        float worst = 0f;
        for (int i = 0; i < 750; i++) worst += PolicyReward.Pacing(1f, 0f, 0.2f, 150f);
        Assert.AreEqual(-1f, worst, 1e-3f);
        Assert.Less(PolicyReward.ViolationPenalty, 0f);
    }
}

// The belief picture needs the store's map.
public class BeliefRasterTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    StoreMap map;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        map = StoreMap.Rebuild();
    }

    [Test]
    public void Raster_ShowsWhereSheThinksYouAreAndWhereSheIs()
    {
        var grid = new BeliefGrid(map);
        Vector3 sighting = map.CellPosition[map.CellCount / 3];
        Vector3 karen = map.CellPosition[2 * map.CellCount / 3];
        grid.Apply(new Observation(SenseChannel.Sight, sighting, 0.95f, 1.5f, "test sighting"));

        var raster = new BeliefRaster();
        var picture = new float[BeliefRaster.Length];
        raster.Write(map, grid, karen, picture);

        int n = BeliefRaster.Size * BeliefRaster.Size;
        float[] belief = picture.Take(n).ToArray();
        float[] her = picture.Skip(n).ToArray();
        Assert.AreEqual(1f, belief.Max(), 1e-5f, "the likeliest pixel isn't scaled to 1");
        // The sighting's bump is about a pixel wide, so its peak may land in a neighbour.
        int seen = BeliefRaster.Pixel(map.Bounds, sighting), peak = System.Array.IndexOf(belief, belief.Max());
        int s = BeliefRaster.Size;
        Assert.LessOrEqual(Mathf.Max(Mathf.Abs(seen / s - peak / s), Mathf.Abs(seen % s - peak % s)), 1, "the peak isn't where she saw you");
        Assert.AreEqual(1, her.Count(x => x > 0f));
        Assert.AreEqual(1f, her[BeliefRaster.Pixel(map.Bounds, karen)]);

        // A rebuilt map (as after a maze mutation) is picked up, not read through stale indices.
        StoreMap rebuilt = StoreMap.Rebuild();
        raster.Write(rebuilt, new BeliefGrid(rebuilt), karen, picture);
        Assert.AreEqual(1f, picture.Take(n).Max(), 1e-5f);
        map = rebuilt;
    }
}
