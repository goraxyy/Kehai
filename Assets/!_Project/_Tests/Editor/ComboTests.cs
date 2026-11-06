using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.2.0, phase 4: the combo room and the learner that fills it.
//
// The planted check (C4.6) is a simulation, not the store: a ten-minute shift is a decision
// every 10 s between Blackout, Ambush, Bin tamper and doing nothing (Patrol), each gated by
// its own cooldown. The rewards are planted: Ambush pays a lot while the lights are out
// (60 s after a Blackout) and little otherwise; the rest pay a little, with noise. The
// choosing and the learning are the game's own: HtnPlanner.Rank, KarenLedger.BanditScore,
// ChainedOn, Settle and EndShift, on a ledger that persists between shifts as on rung E.
// It flattens her two-level choice (an action, then its trick) into one, so it tests the
// learner, not the actions' utilities.
//
// Written down before the first run: over 20 seeds, (1) Blackout → Ambush is found within 8
// shifts in every one; (2) where no chain pays more than its parts, none is found in any;
// (3) over shifts 4–8 the learner that sees her last trick earns more than the same learner
// without it, paired by seed, by more than two standard errors.
public class ComboTests
{
    const int Seeds = 20, Shifts = 8;
    const float Step = 10f, ShiftLength = 600f, LightsOutFor = 60f, Noise = 0.05f;

    struct World
    {
        public float Blackout, BinTamper, Ambush, AmbushInTheDark;
    }

    static readonly World Planted = new World { Blackout = 0.10f, BinTamper = 0.08f, Ambush = 0.02f, AmbushInTheDark = 0.30f };
    static readonly World NoCombo = new World { Blackout = 0.10f, BinTamper = 0.08f, Ambush = 0.08f, AmbushInTheDark = 0.08f };

    readonly List<string> ledgers = new List<string>();

    [TearDown]
    public void Clear()
    {
        TacticLibrary.ResetShift();
        foreach (string path in ledgers) if (File.Exists(path)) File.Delete(path);
        ledgers.Clear();
    }

    KarenConfig Config(bool combo)
    {
        string path = Path.Combine(Path.GetTempPath(), $"kehai_combo_{System.Guid.NewGuid():N}.json");
        ledgers.Add(path);
        return new KarenConfig { rung = KarenRung.E_Ledger, comboContext = combo, ledgerPath = path };
    }

    sealed class Career
    {
        public int FoundOn = -1;                          // the shift Blackout → Ambush was found
        public readonly List<float> Earned = new List<float>();   // reward per shift
        public KarenLedger Ledger;
    }

    // One career in the planted world. Everything she chooses goes through the game's code.
    static Career Run(KarenConfig config, World world, int seed)
    {
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load();
        var c = new KarenContext { Ledger = ledger, Config = config, Rng = new KarenRng(seed) };
        var noise = new System.Random(seed * 31 + 7);
        var career = new Career { Ledger = ledger };

        Tactic blackout = TacticLibrary.Get("blackout"), ambush = TacticLibrary.Get("ambush");
        Tactic[] menu = { blackout, ambush, TacticLibrary.Get("bin_tamper"), TacticLibrary.Get("patrol") };

        for (int shift = 1; shift <= Shifts; shift++)
        {
            ledger.BeginShift(shift);
            TacticLibrary.ResetShift();
            string last = null;
            float lastAt = -999f, lightsBackAt = -1f, earned = 0f;

            for (float t = 0f; t < ShiftLength; t += Step)
            {
                float now = shift * 10000f + t;
                c.NowOverride = now;
                c.Last = config.comboContext && last != null
                    ? new LastTrick { Id = last, Ago = now - lastAt, Running = last == blackout.Id && now < lightsBackAt }
                    : default;

                List<Tactic> candidates = menu.Where(x => !x.OnCooldown(now)).ToList();
                Tactic pick = HtnPlanner.Rank(candidates, c)[0].t;
                pick.LastUsed = now;
                ledger.MarkUsed(pick, now);
                if (!pick.Learnable) continue;

                string after = ledger.ChainedOn(pick, c);
                bool dark = now < lightsBackAt;
                float pays = pick == blackout ? world.Blackout : pick == ambush ? (dark ? world.AmbushInTheDark : world.Ambush) : world.BinTamper;
                float delta = pays + Noise * Gaussian(noise);
                earned += delta;
                if (pick == blackout) lightsBackAt = now + LightsOutFor;

                ChainStat found = ledger.Settle(pick.Id, after, delta);
                if (found != null && found.first == blackout.Id && found.then == ambush.Id && career.FoundOn < 0) career.FoundOn = shift;
                last = pick.Id;
                lastAt = now;
            }
            career.Earned.Add(earned);
            ledger.EndShift(ShiftLength, 0.5f, true);
        }
        return career;
    }

    static float Gaussian(System.Random r)
    {
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
    }

    // ---- the planted check (C4.6) ------------------------------------------------------------

    [Test]
    public void ThePlantedChain_BlackoutThenAmbush_IsFoundWithinEightShifts()
    {
        var foundOn = new List<int>();
        for (int seed = 1; seed <= Seeds; seed++)
        {
            Career career = Run(Config(true), Planted, seed);
            foundOn.Add(career.FoundOn);
            ChainStat chain = career.Ledger.Room.Find("blackout", "ambush");
            if (seed <= 3 && chain != null)
                TestContext.WriteLine($"seed {seed}: found on shift {career.FoundOn}; after {Shifts} shifts it pays {chain.q:+0.00} over {chain.n:0.#} uses, against {career.Ledger.Alone("ambush"):+0.00} alone");
        }
        TestContext.WriteLine("found on shift: " + string.Join(", ", foundOn));
        Assert.IsTrue(foundOn.All(s => s >= 1 && s <= Shifts), "not found within " + Shifts + " shifts: " + string.Join(", ", foundOn));
    }

    [Test]
    public void WhereNoChainPaysMoreThanItsParts_NoneIsFound()
    {
        var falseFinds = new List<string>();
        for (int seed = 1; seed <= Seeds; seed++)
            falseFinds.AddRange(Run(Config(true), NoCombo, seed).Ledger.Room.Found.Select(x => $"seed {seed}: {x.Name}"));
        Assert.IsEmpty(falseFinds, string.Join("\n", falseFinds));
    }

    [Test]
    public void SeeingHerLastTrick_BeatsTheSameLearnerWithout_PairedBySeed()
    {
        var gains = new List<double>();
        for (int seed = 1; seed <= Seeds; seed++)
        {
            Career with = Run(Config(true), Planted, seed), without = Run(Config(false), Planted, seed);
            gains.Add(with.Earned.Skip(3).Average() - without.Earned.Skip(3).Average());
            Assert.AreEqual(0, without.Ledger.Room.Count, "the learner without her last trick kept chains");
        }
        double mean = gains.Average();
        double se = System.Math.Sqrt(gains.Sum(g => (g - mean) * (g - mean)) / (gains.Count - 1) / gains.Count);
        TestContext.WriteLine($"shifts 4–{Shifts}: {mean:+0.000;-0.000} reward a shift with her last trick, SE {se:0.000}, {Seeds} seeds");
        Assert.Greater(mean, 2.0 * se, $"gain {mean:0.000} isn't two standard errors ({se:0.000}) above zero");
    }

    // ---- the room ----------------------------------------------------------------------------

    [Test]
    public void TheRoom_StartsEmpty_RemembersBetweenShifts_AndForgets()
    {
        KarenConfig config = Config(true);
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load();
        Assert.AreEqual(0, ledger.Room.Count, "the room starts empty");

        ledger.BeginShift(1);
        ChainStat found = null;
        for (int i = 0; i < 3; i++) found = ledger.Settle("ambush", "blackout", 0.5f) ?? found;   // Ambush, after Blackout
        Assert.IsNotNull(found, "three uses that pay 0.5 against Ambush's 0.3 alone weren't found");
        Assert.AreEqual("blackout → ambush", found.Name);
        Assert.AreEqual(1, found.found);
        Assert.AreEqual(0.5f, found.q, 1e-4f);
        Assert.AreEqual(0.3f, ledger.Alone("ambush"), 1e-4f, "a chained use went to Ambush's own arm");
        ledger.EndShift(600f, 0.5f, true);    // saves

        var again = new KarenLedger(config) { Persistent = true };
        again.Load();
        ChainStat kept = again.Room.Find("blackout", "ambush");
        Assert.IsNotNull(kept, "the room didn't survive a save");
        Assert.AreEqual(1, kept.found);

        // Unreinforced, it fades and is forgotten (rule 7).
        for (int shift = 2; shift < 12; shift++)
        {
            again.BeginShift(shift);
            again.EndShift(600f, 0.5f, true);
        }
        Assert.IsNull(again.Room.Find("blackout", "ambush"), "a chain nobody used for ten shifts is still remembered");
    }

    [Test]
    public void ItsLastTrick_IsInPlayForTwentySeconds_OrWhileItsEffectRuns()
    {
        Assert.IsTrue(new LastTrick { Id = "blackout", Ago = 19f }.Active);
        Assert.IsFalse(new LastTrick { Id = "blackout", Ago = 21f }.Active);
        Assert.IsTrue(new LastTrick { Id = "blackout", Ago = 90f, Running = true }.Active, "the lights are still out");
        Assert.IsFalse(default(LastTrick).Active);

        var ledger = new KarenLedger(Config(true));
        var c = new KarenContext { Ledger = ledger, Config = new KarenConfig(), Rng = new KarenRng(1) };
        c.Last = new LastTrick { Id = "blackout", Ago = 5f };
        Assert.AreEqual("blackout", ledger.ChainedOn(TacticLibrary.Get("ambush"), c));
        Assert.IsNull(ledger.ChainedOn(TacticLibrary.Get("blackout"), c), "a trick doesn't chain on itself");
        Assert.IsNull(ledger.ChainedOn(TacticLibrary.Get("patrol"), c), "walking about isn't a trick");

        var off = new KarenLedger(Config(false));
        Assert.IsNull(off.ChainedOn(TacticLibrary.Get("ambush"), c), "the learner without her last trick saw it");
    }

    [Test]
    public void TheRoom_ChoosesNothing_TheGateStillDoes()
    {
        // A chain she's sure of reorders what the gate passed, and never adds to it.
        KarenConfig config = Config(true);
        var ledger = new KarenLedger(config);
        var c = new KarenContext { Ledger = ledger, Config = config, Rng = new KarenRng(1), NowOverride = 1000f };
        for (int i = 0; i < 6; i++) ledger.Settle("ambush", "blackout", 0.9f);
        c.Last = new LastTrick { Id = "blackout", Ago = 5f };

        Tactic ambush = TacticLibrary.Get("ambush"), bin = TacticLibrary.Get("bin_tamper");
        Assert.AreEqual(ambush, HtnPlanner.Rank(new[] { bin, ambush }, c)[0].t, "the chain she's sure of goes first");

        // Ambush on cooldown, or over the budget: the gate leaves it out, and the room can't put it back.
        var ranked = HtnPlanner.Rank(new[] { bin }, c);
        Assert.AreEqual(1, ranked.Count);
        Assert.AreEqual(bin, ranked[0].t);

        // And its goal looks better to her for it: the action's expected panic reads the chain too.
        float inContext = ledger.ExpectedPanicDelta(ambush, c);
        c.Last = default;
        Assert.Greater(inContext, ledger.ExpectedPanicDelta(ambush, c) + 0.2f);
    }
}
