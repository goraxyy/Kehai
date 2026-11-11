using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Eval;
using NUnit.Framework;
using UnityEngine;

// 0.4.0, phase 2 (build plan §7): the persona, the options, habits and mood, the trained mind's
// inputs and reward, and the Survivor.
public class Karen040PlayersTests
{
    // ---- C2.1 the persona ------------------------------------------------------------------------

    // The four scripted profiles, built from their personas, have exactly today's reflexes: same
    // tables, same seeded jitter. So they play as they do today, choice for choice.
    [Test]
    public void Persona_NamedPoints_GiveTodaysReflexesExactly()
    {
        foreach (PlayerProfile profile in System.Enum.GetValues(typeof(PlayerProfile)))
            foreach (int seed in new[] { 1, 1031, 31000 + 7, 99999 })
            {
                BotTraits today = BotTraits.For(profile, seed);
                BotTraits fromPersona = Persona.Of(profile).ToTraits(seed);
                CollectionAssert.AreEqual(today.Susceptibility, fromPersona.Susceptibility, $"{profile} seed {seed}");
                foreach (var f in typeof(BotTraits).GetFields().Where(f => f.FieldType != typeof(float[])))
                    Assert.AreEqual(f.GetValue(today), f.GetValue(fromPersona), $"{profile} seed {seed}: {f.Name}");
                Assert.AreEqual(profile, Persona.Of(profile).NearestProfile());
            }
    }

    [Test]
    public void Persona_TheFourProfilesAreFourPointsOfTheSpace()
    {
        Persona reckless = Persona.Of(PlayerProfile.Reckless);
        Assert.AreEqual(1f, reckless[Trait.Pace]);
        Assert.AreEqual(0.1f, reckless[Trait.Caution], 1e-6f);
        Assert.Greater(reckless[Trait.FearOfChase], 0.5f, "only being chased gets to it");
        Assert.Less(new[] { Trait.FearOfDark, Trait.FearOfSounds, Trait.FearOfHer }.Max(t => reckless[t]), 0.2f);
        Assert.AreEqual(14, Persona.Count);
        Assert.AreEqual(14, Persona.Names.Length);
    }

    [Test]
    public void Persona_AnyOther_MapsSmoothlyAndStaysInRange()
    {
        var random = new System.Random(3);
        for (int i = 0; i < 200; i++)
        {
            Persona p = Persona.Draw(random, PersonaRanges.Everything);
            BotTraits t = p.ToTraits(i);
            Assert.IsTrue(t.Susceptibility.All(s => s >= 0f && s <= 2.2f), p.ToString());
            Assert.That(t.Tau, Is.InRange(4f, 41f));
            Assert.That(t.KeepClear, Is.InRange(4f, 9f));
        }
        // More cautious keeps further away and turns back sooner.
        Persona bold = new Persona("bold", Enumerable.Repeat(0.5f, 14).ToArray()) { [Trait.Caution] = 0.1f };
        Persona wary = bold.Clone();
        wary[Trait.Caution] = 0.9f;
        Assert.Greater(wary.ToTraits(1).KeepClear, bold.ToTraits(1).KeepClear);
        Assert.Greater(wary.ToTraits(1).TurnBackAt, bold.ToTraits(1).TurnBackAt);
    }

    [Test]
    public void Persona_ParsesFromText()
    {
        Assert.IsTrue(Persona.TryParse("reckless", out Persona p) && p.Scripted == PlayerProfile.Reckless);
        Assert.IsTrue(Persona.TryParse("survivor", out p) && p.IsSurvivor);
        string text = string.Join(",", Enumerable.Range(0, 14).Select(i => (i / 14f).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.IsTrue(Persona.TryParse(text, out p));
        Assert.AreEqual(13 / 14f, p[Trait.FearOfChase], 1e-5f);
        Assert.IsFalse(Persona.TryParse("0.1,0.2", out _));
    }

    // ---- C2.2 options ---------------------------------------------------------------------------

    static Dictionary<string, object> Obs(bool open = true, bool lights = true, int queue = 0, int spills = 0, bool bag = false, int chores = 0)
    {
        List<object> L(int n, System.Func<int, Dictionary<string, object>> f) => Enumerable.Range(0, n).Select(i => (object)f(i)).ToList();
        return new Dictionary<string, object>
        {
            ["store_open"] = open,
            ["time_remaining"] = 100f,
            ["you"] = new Dictionary<string, object> { ["energy"] = 0.8f, ["holding"] = bag ? "trash bag" : null, ["inventory"] = new List<string>(), ["at"] = new[] { 0f, 0f } },
            ["checkout_queue"] = L(queue, i => new Dictionary<string, object> { ["id"] = "c" + i, ["waited_s"] = 5f, ["walk_m"] = 10f }),
            ["customers_asking"] = new List<object>(),
            ["shelves_to_restock"] = new List<object>(),
            ["spills"] = L(spills, i => new Dictionary<string, object> { ["id"] = "s" + i, ["walk_m"] = 4f + i }),
            ["bins"] = new List<object>(),
            ["karen_chores"] = L(chores, i => new Dictionary<string, object> { ["id"] = "f" + i, ["kind"] = "fog", ["walk_m"] = 12f }),
            ["power"] = new Dictionary<string, object> { ["lights_on"] = lights },
            ["karen"] = new Dictionary<string, object> { ["visible"] = false, ["heard"] = false },
            ["heard"] = new List<object>()
        };
    }

    [Test]
    public void Options_MasksFollowTheObservation()
    {
        bool[] m = EmployeeOptions.MaskWhat(Obs());
        Assert.IsFalse(m[(int)EmployeeWhat.ServeTill]);
        Assert.IsFalse(m[(int)EmployeeWhat.ClockOut], "the doors are open");
        Assert.IsFalse(m[(int)EmployeeWhat.AnswerChore]);
        Assert.IsTrue(m[(int)EmployeeWhat.Wait] && m[(int)EmployeeWhat.Hide] && m[(int)EmployeeWhat.BackOff]);
        m = EmployeeOptions.MaskWhat(Obs(open: false, lights: false, queue: 2, spills: 1, bag: true));
        Assert.IsTrue(m[(int)EmployeeWhat.ServeTill] && m[(int)EmployeeWhat.Mop] && m[(int)EmployeeWhat.BagOut]);
        Assert.IsTrue(m[(int)EmployeeWhat.ClockOut]);
        Assert.IsTrue(m[(int)EmployeeWhat.AnswerChore], "lights out: the breakers are her chore");
        Assert.AreEqual(14, EmployeeOptions.Whats);
        Assert.AreEqual(3, EmployeeOptions.Hows);
        Assert.AreEqual(3, EmployeeOptions.Whiches);
    }

    // The heuristic picks among the same options: every action it can take has its option.
    [Test]
    public void Options_EveryScriptedActionIsNamed()
    {
        var cases = new (string verb, string target, EmployeeWhat what)[]
        {
            ("serve", "c1", EmployeeWhat.ServeTill), ("help", "a1", EmployeeWhat.HelpShopper), ("restock", "bay_1", EmployeeWhat.Restock),
            ("pick_up", "crate", EmployeeWhat.Restock), ("mop", "s1", EmployeeWhat.Mop), ("pick_up", "mop", EmployeeWhat.Mop),
            ("bag_trash", "bin_1", EmployeeWhat.Bins), ("dispose", null, EmployeeWhat.BagOut), ("flip_breaker", "b1", EmployeeWhat.AnswerChore),
            ("reset_fog", "f", EmployeeWhat.AnswerChore), ("restart_clock", "c", EmployeeWhat.AnswerChore), ("drink_coffee", null, EmployeeWhat.Coffee),
            ("clock_out", null, EmployeeWhat.ClockOut), ("wait", null, EmployeeWhat.Wait), ("move_to", "checkout_2", EmployeeWhat.Wait)
        };
        foreach (var (verb, target, what) in cases)
            Assert.AreEqual(what, EmployeeOptions.WhatOf(new EnvAction { verb = verb, target = target }, false), verb + " " + target);
        Assert.AreEqual(EmployeeWhat.BagOut, EmployeeOptions.WhatOf(new EnvAction { verb = "drop" }, holdingBag: true));
        Assert.AreEqual(EmployeeHow.Hurry, EmployeeOptions.HowOf(new EnvAction { sprint = true }));
        Assert.AreEqual(EmployeeHow.Sneak, EmployeeOptions.HowOf(new EnvAction { crouch = true }));
    }

    // Logging a choice draws nothing from the player's dice: the heuristic's next choice is the same.
    [Test]
    public void Options_TheLogFormatIsStable()
    {
        var c = new EmployeeChoice { What = EmployeeWhat.Mop, How = EmployeeHow.Hurry, Which = EmployeeWhich.Nearest, Verb = "mop", Target = "s1", Chance = 0.5f, By = "heuristic" };
        Assert.AreEqual("Mop|Hurry|Nearest|mop|s1|0.5|heuristic", EmployeeOptions.Describe(c));
        string players = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/Eval/Scripts/SimulatedPlayers.cs"));
        string tag = System.Text.RegularExpressions.Regex.Match(players, @"EmployeeChoice Tag\(.*?\};", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
        Assert.IsNotEmpty(tag);
        StringAssert.DoesNotContain("random", tag, "naming the heuristic's choice draws no dice");
    }

    // ---- C2.3 habits and mood ---------------------------------------------------------------------

    [Test]
    public void Memory_HabitsMoodsAndGrudges()
    {
        var m = new BotMemory();
        m.BeginShift(1);
        m.Hid("Aisle 12/A");
        m.Hid("Aisle 12/A");
        m.Hid("Stockroom");
        Assert.AreEqual("Aisle 12/A", m.UsualHide());
        Assert.AreEqual(2f / 3f, m.HideHabit(), 1e-5f);
        m.DidJob("Mop");
        m.DidJob("Restock");
        Assert.AreEqual("Restock", m.UsualNext("Mop"));
        m.EndShift(true, "Freezers");
        Assert.AreEqual(0.5f, m.Confidence, 1e-5f, "careful after dying");
        m.BeginShift(2);
        Assert.IsTrue(m.Holds("Freezers", 2));
        CollectionAssert.AreEqual(new[] { "Mop", "Restock" }, m.LastOrder);
        m.EndShift(false, null);
        m.BeginShift(3);
        m.EndShift(false, null);
        m.BeginShift(4);
        m.EndShift(false, null);
        Assert.Greater(m.Confidence, 0.8f, "bolder again over quiet shifts");
        m.BeginShift(5);
        Assert.IsFalse(m.Holds("Freezers", 2), "the grudge fades");
    }

    // ---- C2.4 the trained mind's inputs and reward --------------------------------------------------

    [Test]
    public void EmployeeObservation_HasItsSizeAndNoNaN()
    {
        float[] v = EmployeeObservation.Build(Obs(queue: 1, spills: 2, chores: 1), 0.4f, new BotMemory(), Persona.Of(PlayerProfile.Skittish), EmployeeWhat.Mop, 10f);
        Assert.AreEqual(EmployeeObservation.Size, v.Length);
        Assert.That(EmployeeObservation.Size, Is.InRange(60, 80), "about 70 numbers (§7.3)");
        Assert.IsTrue(v.All(x => !float.IsNaN(x) && x >= -1f && x <= 1f));
        // An empty observation doesn't throw.
        Assert.AreEqual(EmployeeObservation.Size, EmployeeObservation.Build(new Dictionary<string, object>(), 0f, null, null, null, 0f).Length);
    }

    [Test]
    public void EmployeeReward_PersonaSetsTheWeights_AndItIsBounded()
    {
        var diligent = Persona.Of(PlayerProfile.Efficient);
        var lazy = diligent.Clone();
        lazy.Scripted = null;
        lazy[Trait.Diligence] = 0f;
        var done = new EmployeeOutcome { Jobs = 3 };
        Assert.Greater(EmployeeReward.Pay(diligent.Weights, done), EmployeeReward.Pay(lazy.Weights, done));
        var caught = new EmployeeOutcome { Caught = true };
        var wary = lazy.Clone();
        wary[Trait.Caution] = 1f;
        Assert.Less(EmployeeReward.Pay(wary.Weights, caught), EmployeeReward.Pay(lazy.Weights, caught), "caution makes a catch cost more");
        var random = new System.Random(1);
        for (int i = 0; i < 100; i++)
        {
            EmployeeRewardWeights w = Persona.Draw(random, PersonaRanges.Everything).Weights;
            var worst = new EmployeeOutcome { Jobs = 0, CustomersLost = 20, FearSeconds = 300f, SprintSeconds = 300f, Caught = true, TimedOut = true };
            var best = new EmployeeOutcome { Jobs = 40, UsedHabit = true, LookedOnce = true, ClockedOut = true };
            Assert.LessOrEqual(Mathf.Abs(EmployeeReward.Pay(w, worst)), EmployeeReward.Bound(w) + 1e-4f);
            Assert.LessOrEqual(Mathf.Abs(EmployeeReward.Pay(w, best)), EmployeeReward.Bound(w) + 1e-4f);
            Assert.Less(EmployeeReward.Bound(w), 5f, "a decision's reward stays small next to PPO's clip");
        }
    }

    [Test]
    public void EmployeeMind_DiceAreLoadedByConsistency()
    {
        float[] logits = { 2f, 1f, 0f };
        var dice = new System.Random(9);
        int TopShare(float consistency)
        {
            int top = 0;
            for (int i = 0; i < 2000; i++) if (EmployeeMinds.Draw(logits, null, consistency, dice, out _) == 0) top++;
            return top;
        }
        Assert.Greater(TopShare(1f), TopShare(0f) + 300, "a consistent persona takes its top choice far more often");
        Assert.AreEqual(1, EmployeeMinds.Draw(logits, new[] { false, true, false }, 0.5f, dice, out float chance), "masked options are never drawn");
        Assert.AreEqual(1f, chance, 1e-6f);
        Assert.IsNull(EmployeeMinds.Create("heuristic"));
        Assert.IsNull(GeneratedEmployeeMind.TryCreate(out string why), "nothing is trained yet");
        StringAssert.Contains("no trained EmployeeBrain", why);
    }

    // ---- C2.8 the Survivor -------------------------------------------------------------------------

    // It wins only by clocking out: nothing else pays, and a shift that times out is a loss.
    [Test]
    public void Survivor_WinsOnlyByClockingOut()
    {
        Persona s = Persona.Survivor;
        Assert.AreEqual(1f, s[Trait.Caution]);
        Assert.AreEqual(1f, s[Trait.Attention]);
        EmployeeRewardWeights w = s.Weights;
        Assert.Greater(EmployeeReward.Pay(w, new EmployeeOutcome { ClockedOut = true }), 0f);
        Assert.AreEqual(0f, EmployeeReward.Pay(w, new EmployeeOutcome { Jobs = 30, FearSeconds = 0f, UsedHabit = true, LookedOnce = true }), "jobs alone pay nothing");
        Assert.Less(EmployeeReward.Pay(w, new EmployeeOutcome { TimedOut = true }), 0f, "surviving to the time-out isn't a win");
        Assert.Less(EmployeeReward.Pay(w, new EmployeeOutcome { Caught = true }), 0f);
        Assert.IsFalse(EvalSplits.InCarefulCorner(s), "pace 0.5: not a careful-corner persona, so Vital may spar with it");
    }
}
