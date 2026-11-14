using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// 0.4.0, phase 5 (build plan §5): the seam, the inputs, the rewards, Vital's gates, the brain files.
public class Karen040BrainsTests
{
    static KarenContext Context(KarenConfig config)
    {
        var ledger = new KarenLedger(config);
        StoreMap map = StoreMap.Synthetic(6, 5, null);
        return new KarenContext { Config = config, Rng = new KarenRng(7), Ledger = ledger, Director = new KarenDirector(config, ledger), Map = map, Belief = new BeliefGrid(map) };
    }

    // ---- C5.1 the seam -------------------------------------------------------------------------

    [Test]
    public void Pairs_AreNamedAndKnown()
    {
        List<string> pairs = BrainSeam.AllPairs();
        Assert.That(pairs.Count, Is.InRange(20, 80), "about 30 pairs (D1), with every action a trick serves");
        Assert.AreEqual(pairs.Count, pairs.Distinct().Count());
        Assert.IsTrue(pairs.All(BrainSeam.KnownOutput));
        Assert.IsFalse(BrainSeam.KnownOutput("hunt:juggling"));
        StringAssert.Contains("\"inputs_version\":" + BrainObservation.Version, BrainSeam.SpecJson());
    }

    // The mask never lets through what the gates refuse: every legal pair's action is permitted
    // and its trick passes the planner's own gates and preconditions.
    [Test]
    public void Mask_NeverLetsThroughWhatTheGatesRefuse([Values(KarenBrainKind.Basic, KarenBrainKind.Vital)] KarenBrainKind kind)
    {
        var config = new KarenConfig { brain = kind, rung = KarenRung.E_Ledger };
        KarenContext c = Context(config);
        foreach (BrainSeam.Pair p in BrainSeam.Legal(c))
        {
            Assert.Greater(BrainSeam.Permission(p.Action, c), 0f, p.Key);
            Assert.IsTrue(HtnPlanner.Gated(p.Tactic, c, out string why), $"{p.Key}: {why}");
            Assert.IsTrue(p.Tactic.Actions.Contains(p.Action), p.Key);
            Assert.IsFalse(p.Tactic.Draft, p.Key);
        }
    }

    [Test]
    public void SheIsAsked_OnlyAtThePlannersMoments()
    {
        Assert.IsTrue(BrainSeam.Asks(forced: true, hasPlan: true, sinceCommit: 0f, minCommit: 4f), "an interrupt, or a plan ended");
        Assert.IsTrue(BrainSeam.Asks(false, hasPlan: false, 0f, 4f), "no plan");
        Assert.IsTrue(BrainSeam.Asks(false, true, 4f, 4f), "the minimum commit time has passed");
        Assert.IsFalse(BrainSeam.Asks(false, true, 1f, 4f), "otherwise she keeps what she's doing");
    }

    [Test]
    public void ThePlanner_NeverAsksABrain()
    {
        Assert.IsNull(BrainPolicies.Create(new KarenConfig(), out _), "-karen-brain planner: today's game");
        Assert.IsNull(BrainPolicies.Create(new KarenConfig { brain = KarenBrainKind.Basic }, out string why), "nothing trained yet");
        StringAssert.Contains("KarenBrainBasic", why, "and it says why");
        string brain = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts/Core/KarenBrain.cs"));
        StringAssert.Contains("if (BrainPolicy == null || !config.Features.Goals) return false;", brain, "with no brain the planner's own path runs, unchanged");
    }

    [Test]
    public void Draw_UsesItsOwnDice_AndGivesTheChance()
    {
        var dice = new System.Random(1);
        int i = BrainSeam.Draw(new[] { 0f, 1f, 0f }, dice, out float chance);
        Assert.AreEqual(1, i);
        Assert.AreEqual(1f, chance);
        Assert.AreEqual(-1, BrainSeam.Draw(null, dice, out _));
        int top = 0;
        for (int k = 0; k < 1000; k++) if (BrainSeam.Draw(new[] { 0.7f, 0.3f }, dice, out _) == 0) top++;
        Assert.That(top, Is.InRange(620, 780), "she doesn't always take the top one");
    }

    // ---- C5.2 her inputs, version 2 --------------------------------------------------------------

    [Test]
    public void Inputs_Version2_HaveTheirSize_AndTheLedgerOnlyForBasic()
    {
        var config = new KarenConfig { brain = KarenBrainKind.Basic };
        KarenContext c = Context(config);
        Tactic blackout = TacticLibrary.Get("blackout");
        for (int k = 0; k < 4; k++) c.Ledger.RecordEvidence(blackout, -0.3f);
        float[] basic = BrainObservation.Build(c, readLedger: true);
        float[] vital = BrainObservation.Build(c, readLedger: false);
        Assert.AreEqual(BrainObservation.Size, basic.Length);
        Assert.AreEqual(2, BrainObservation.Version);
        int ledgerStart = BrainObservation.Size - (2 * TacticLibrary.All.Count + 2 * BrainObservation.Kinds.Length + 3 + BrainObservation.Answers.Length);
        Assert.IsTrue(vital.Skip(ledgerStart).All(x => x == 0f), "Vital reads no Ledger: zeros");
        Assert.IsTrue(basic.Skip(ledgerStart).Any(x => x != 0f), "Basic reads yours");
        Assert.IsTrue(basic.All(x => !float.IsNaN(x) && x >= -1f && x <= 1f));
    }

    [Test]
    public void Inputs_TheFourDeadOnesAreGone()
    {
        string src = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts/Decision/Policy/BrainObservation.cs"));
        string code = string.Join("\n", src.Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        foreach (string dead in new[] { "lookBehindRate", "timeToFlee", "coffeePerShift", "warnings" })
            StringAssert.DoesNotContain(dead, code, dead + " was never written; version 2 drops it");
    }

    // The "moved player" test (§5.6): move the player where she can't sense it, and her inputs
    // don't change. With a real, current player, so it isn't vacuous.
    [Test]
    public void MovedPlayer_DoesNotChangeHerInputs([Values(KarenBrainKind.Basic, KarenBrainKind.Vital)] KarenBrainKind kind)
    {
        var player = new GameObject("player (test)") { tag = "Player" };
        try
        {
            PlayerPresence presence = player.AddComponent<PlayerPresence>();
            typeof(PlayerPresence).GetProperty("Current").SetValue(null, presence);
            var config = new KarenConfig { brain = kind, mirror = MirrorMode.K2 };
            KarenContext c = Context(config);
            c.Belief.ResetTo(c.Map.CellPosition[3], 2f);
            player.transform.position = new Vector3(1f, 0f, 1f);
            float[] before = BrainObservation.Build(c, kind == KarenBrainKind.Basic);
            player.transform.position = new Vector3(8f, 0f, 6f);
            Assert.AreEqual(8f, PlayerPresence.Current.Position.x, 1e-5f, "the player really moved");
            float[] after = BrainObservation.Build(c, kind == KarenBrainKind.Basic);
            CollectionAssert.AreEqual(before, after);
        }
        finally
        {
            typeof(PlayerPresence).GetProperty("Current").SetValue(null, null);
            Object.DestroyImmediate(player);
        }
    }

    // ---- C5.3 the rewards, and their bounds --------------------------------------------------------

    [Test]
    public void Vital_IsPaidForCatching_SoonerIsBetter()
    {
        Assert.AreEqual(0.8f, BrainReward.VitalCatch(60f, 300f), 1e-5f, "the plan's example: caught at 60 s of 300");
        Assert.AreEqual(0.1f, BrainReward.VitalCatch(270f, 300f), 1e-5f);
        var pay = new BrainPay(KarenBrainKind.Vital);
        pay.Pacing(0.9f, 0.1f, 5f, 300f);
        Assert.AreEqual(0f, pay.Total, "your fear: nothing");
        pay.EndShift(caught: false, stuck: false);
        Assert.AreEqual(-1f, pay.Total, "no catch: −1, however the shift ends");
        pay.Reset();
        pay.Caught(60f, 300f, KarenDirector.Phase.Build);
        pay.Violations(2);
        pay.EndShift(caught: true, stuck: false);
        Assert.AreEqual(0.8f - 0.2f, pay.Total, 1e-5f);
        Assert.That(pay.Total, Is.InRange(-1f - 0.1f * 100, 1f));
    }

    [Test]
    public void Basic_IsPaidForPacing_TheDial_Sameness_AndStuck()
    {
        var pay = new BrainPay(KarenBrainKind.Basic);
        // The plan's example: fear 0.12 from the target on average all shift, a catch in Crunch,
        // Spill used five times: −0.12 + 0.15 − 0.04 = −0.01.
        for (int s = 0; s < 300; s++) pay.Pacing(0.52f, 0.4f, 1f, 300f);
        pay.Caught(200f, 300f, KarenDirector.Phase.Crunch);
        for (int use = 0; use < 5; use++) pay.Used(use);
        pay.EndShift(caught: true, stuck: false);
        Assert.AreEqual(-0.01f, pay.Total, 1e-4f);
        Assert.AreEqual(0f, BrainReward.BasicCatch(KarenDirector.Phase.Build, 0.15f), "a catch outside a tense phase pays nothing");
        var stuck = new BrainPay(KarenBrainKind.Basic);
        stuck.EndShift(false, stuck: true);
        Assert.AreEqual(-0.5f, stuck.Total);
        // Bounds: pacing over a whole shift lies in [−1, 0].
        var worst = new BrainPay(KarenBrainKind.Basic);
        for (int s = 0; s < 300; s++) worst.Pacing(1f, 0f, 1f, 300f);
        Assert.AreEqual(-1f, worst.Total, 1e-4f);
        Assert.AreEqual(0.15f, new KarenConfig().catchDial, 1e-6f, "the dial starts at +0.15");
        float take = pay.Take();
        Assert.AreEqual(0f, pay.Take(), "what's handed over is handed over once");
        Assert.AreEqual(-0.01f, take, 1e-4f);
    }

    // ---- C5.4 Vital's gates, and the hunter ------------------------------------------------------

    [Test]
    public void PacingOff_ForVitalAndTheHunter_FairnessStays()
    {
        Assert.IsFalse(new KarenConfig().PacingOff, "today's game: pacing on");
        Assert.IsFalse(new KarenConfig { brain = KarenBrainKind.Basic }.PacingOff, "Basic: pacing on (D4)");
        Assert.IsTrue(new KarenConfig { brain = KarenBrainKind.Vital }.PacingOff);
        Assert.IsTrue(new KarenConfig { hunter = true }.PacingOff, "the hunter: the planner with the same freedom");

        var vital = new KarenConfig { brain = KarenBrainKind.Vital };
        KarenContext c = Context(vital);
        Assert.IsFalse(HtnPlanner.Shielded(c), "the Director's gates are off");
        Assert.AreEqual(1f, c.Director.FairPermission(ActionId.Roam, c));
        // Never a hunt with you outside: a fairness rule, on for Vital too.
        StoreMap map = StoreMap.Synthetic(4, 4, null, Enumerable.Range(0, 16).Select(g => g / 4 == 3).ToArray());
        var grid = new BeliefGrid(map);
        grid.ResetTo(map.CellPosition[map.CellCount - 1], 0.5f);
        c.Belief = grid;
        c.Map = map;
        Assume.That(grid.OutsideMass, Is.GreaterThan(0.5f));
        Assert.AreEqual(0f, c.Director.FairPermission(ActionId.Hunt, c), "she never chases you outside the store");
        StringAssert.Contains("if (config.PacingOff) return;", File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts/Core/KarenDirector.cs")),
                              "the Director's hint toward your area is off");
    }

    // ---- C5.7 a brain file for another build is refused, with a clear message ---------------------

    static BrainNet Net(int inputsVersion, int inputs, string[] outputs)
    {
        var card = new BrainCard { Name = "KarenBrainBasic", Version = "t", InputsVersion = inputsVersion, OutputNames = new[] { outputs } };
        return new BrainNet(card, new float[inputs], Enumerable.Repeat(1f, inputs).ToArray(), new int[0], new float[0][], new float[0][],
                            new[] { outputs.Length }, new[] { new float[outputs.Length * inputs] }, new[] { new float[outputs.Length] });
    }

    [Test]
    public void BrainFiles_AnOldVersionOrUnknownTricks_AreRefused()
    {
        string[] good = BrainSeam.AllPairs().Take(4).ToArray();
        Assert.IsTrue(BrainSeam.Accepts(Net(BrainObservation.Version, BrainObservation.Size, good), out string why), why);
        Assert.IsFalse(BrainSeam.Accepts(Net(1, BrainObservation.Size, good), out why));
        StringAssert.Contains("inputs version 1", why);
        StringAssert.Contains("Retrain", why);
        Assert.IsFalse(BrainSeam.Accepts(Net(BrainObservation.Version, BrainObservation.Size, new[] { "hunt:juggling", "flush:yodel" }), out why));
        StringAssert.Contains("none of its outputs", why);
        Assert.IsTrue(BrainSeam.Accepts(Net(BrainObservation.Version, BrainObservation.Size, good.Concat(new[] { "hunt:juggling" }).ToArray()), out why),
                      "one removed trick is ignored, not fatal");
        StringAssert.Contains("ignored", why);
        // Chances are tied to names: an unknown output gets nothing.
        BrainNet net = Net(BrainObservation.Version, BrainObservation.Size, good);
        var legal = new List<BrainSeam.Pair>();
        foreach (string key in good)
        {
            string[] parts = key.Split(':');
            Tactic t = TacticLibrary.Get(parts[1]);
            legal.Add(new BrainSeam.Pair { Action = t.Actions.First(a => a.ToString().ToLowerInvariant() == parts[0]), Tactic = t });
        }
        float[] p = BrainSeam.Chances(net, new float[BrainObservation.Size], legal);
        Assert.AreEqual(1f, p.Sum(), 1e-5f);
    }
}
