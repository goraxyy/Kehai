using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.2.0, phase 1: goals became actions, tricks are the ways to do them, and the drafts
// are off in play. These pin the shape the build plan settled on, so a change to it is a
// change on purpose.
public class KarenActionsTests
{
    static IEnumerable<ActionId> AllActions => System.Enum.GetValues(typeof(ActionId)).Cast<ActionId>();

    [Test]
    public void NoAction_SharesANameWithATrick()
    {
        var actions = AllActions.Select(a => a.Name().ToLowerInvariant()).ToList();
        foreach (Tactic t in TacticLibrary.All.Where(t => !t.OwnMove))
            CollectionAssert.DoesNotContain(actions, t.Title.ToLowerInvariant(), $"the trick {t.Id} is named like an action");
    }

    [Test]
    public void ADraft_IsNeverACandidate()
    {
        var c = new KarenContext { Config = new KarenConfig { rung = KarenRung.E_Ledger, seed = 7 }, Rng = new KarenRng(7) };
        var drafts = TacticLibrary.All.Where(t => t.Draft).ToList();
        Assert.IsNotEmpty(drafts);
        foreach (Tactic t in drafts)
        {
            Assert.IsFalse(HtnPlanner.Gated(t, c, out string why), $"{t.Id} passed the gate");
            Assert.AreEqual("draft", why, t.Id);
        }
    }

    [Test]
    public void TheLiveTricks_AreTheOnesThePlanKeeps()
    {
        var live = TacticLibrary.Live.Where(t => !t.OwnMove).Select(t => t.Id).OrderBy(id => id).ToList();
        CollectionAssert.AreEqual(new[]
        {
            "ambush", "bin_tamper", "blackout", "camera_bolt_on", "camera_break", "camera_watch", "cut", "door_lock", "fog", "overtime", "phantom_chime",
            "poll_witnesses", "shelf_sweep", "spill", "throw_flour", "throw_oil", "throw_tin", "tool_theft", "understudy",
        }, live);
    }

    // 0.3.0: the five the owner didn't ask about stay drafts (build plan D9); nothing else does.
    [Test]
    public void TheDrafts_AreTheFiveLeft()
    {
        var drafts = TacticLibrary.All.Where(t => t.Draft).Select(t => t.Id).OrderBy(id => id).ToArray();
        CollectionAssert.AreEqual(new[] { "crate_wall", "favour", "mimicry", "shelf_relocation", "stalk" }, drafts);
    }

    // Roam, Check, Search, Hunt and Back off move on their own; Lie in wait, Flush, Disrupt and
    // Corner have a live trick; Loom and Lure have neither until their drafts come back.
    [Test]
    public void EveryAction_HasItsOwnMoveOrALiveTrick_ButLoomAndLure()
    {
        foreach (ActionId a in new[] { ActionId.Roam, ActionId.Check, ActionId.Search, ActionId.Hunt, ActionId.BackOff })
            Assert.IsTrue(TacticLibrary.For(a).Any(t => t.OwnMove && !t.Draft), $"{a.Name()} has no own move");
        var dormant = AllActions.Where(a => !TacticLibrary.For(a).Any(t => !t.Draft)).ToList();
        CollectionAssert.AreEquivalent(new[] { ActionId.Loom, ActionId.Lure }, dormant);
    }

    [Test]
    public void TheChaseFollowAndFunnel_AreGone_AndHuntRunsOnItsOwn()
    {
        foreach (string id in new[] { "chase", "follow", "funnel" })
            Assert.IsNull(TacticLibrary.Get(id), $"{id} is still in the library");
        Tactic hunt = TacticLibrary.Get("hunt");
        Assert.IsNotNull(hunt);
        Assert.IsTrue(hunt.OwnMove);
        CollectionAssert.AreEqual(new[] { ActionId.Hunt }, hunt.Actions);
    }

    // ---- 0.3.0: nine tricks removed for good (build plan C1.1) -------------------------------

    static readonly string[] RemovedIn030 =
    {
        "mirror_black", "task_falsification", "pa_decoy", "pa_announce_position", "pa_task_readback",
        "pa_countdown", "pa_footsteps", "silence", "witness",
    };

    [Test]
    public void TheNineRemovedIn030_AreGone_AndTheLibraryHolds29()
    {
        foreach (string id in RemovedIn030)
            Assert.IsNull(TacticLibrary.Get(id), $"{id} is still in the library");
        Assert.AreEqual(29, TacticLibrary.All.Count, "27 after 0.3.0, and 0.4.1's Camera watch and Camera break");
    }

    // An old save that learned about the removed tricks still loads; it just forgets them.
    [Test]
    public void ALedgerThatKnewTheRemovedTricks_StillLoads_AndDropsThem()
    {
        string path = Path.Combine(Path.GetTempPath(), "kehai_removed_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var old = new LedgerData();
            foreach (string id in RemovedIn030) old.arms.Add(new ArmStat { id = id, q = 0.5f, n = 3f });
            old.arms.Add(new ArmStat { id = "blackout", q = 0.4f, n = 2f });
            old.chains.Add(new ChainStat { first = "silence", then = "ambush", q = 0.3f, n = 4f });
            old.chains.Add(new ChainStat { first = "blackout", then = "ambush", q = 0.3f, n = 4f });
            File.WriteAllText(path, JsonUtility.ToJson(old));

            var ledger = new KarenLedger(new KarenConfig { ledgerPath = path }) { Persistent = true };
            ledger.Load();
            ledger.Save();

            LedgerData now = JsonUtility.FromJson<LedgerData>(File.ReadAllText(path));
            CollectionAssert.AreEqual(new[] { "blackout" }, now.arms.Select(a => a.id).ToArray());
            CollectionAssert.AreEqual(new[] { "blackout" }, now.chains.Select(c => c.first).ToArray());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Test]
    public void Ambush_ComesOnShiftThree()
    {
        Assert.AreEqual(3, TacticLibrary.Get("ambush").IntroducedShift);
    }

    // The owner, 8 November: every trick from shift 1 while they're tested. The career table
    // stays in the code (each trick's IntroducedShift), behind KarenConfig.careerTable.
    [Test]
    public void EveryTrick_ComesOnShiftOne_UntilTheCareerTableIsBack()
    {
        var config = new KarenConfig { rung = KarenRung.E_Ledger, seed = 7 };
        Assert.IsFalse(config.careerTable, "the lock is off by default");
        var ledger = new KarenLedger(config);
        var director = new KarenDirector(config, ledger);
        director.BeginShift(1, 300f);
        var c = new KarenContext { Config = config, Rng = new KarenRng(7), Ledger = ledger, Director = director, A = new Appraisal { Tension = 1f } };
        Assert.AreEqual(1, c.ShiftNumber);

        var later = TacticLibrary.Live.Where(t => !t.Draft && t.IntroducedShift > 1).ToList();
        Assert.IsNotEmpty(later);
        foreach (Tactic t in later)
        {
            HtnPlanner.Gated(t, c, out string why);
            Assert.AreNotEqual($"introduced on shift {t.IntroducedShift}", why, $"{t.Id} is still locked on shift 1");
        }

        config.careerTable = true;
        foreach (Tactic t in later)
        {
            Assert.IsFalse(HtnPlanner.Gated(t, c, out string why), $"{t.Id} got through on shift 1 with the table on");
            Assert.AreEqual($"introduced on shift {t.IntroducedShift}", why);
        }
    }

    // ---- phase 2: Hunt -----------------------------------------------------------------------

    // The pacing is the Hunt action's, whichever of its moves or tricks runs (phase 3).
    [Test]
    public void Hunt_CostsAndCoolsAsAnAction_NotPerTrick()
    {
        var config = new KarenConfig();
        Assert.AreEqual(0.6f, config.huntTensionCost, 1e-4f);
        Assert.AreEqual(90f, config.huntCooldownSeconds, 1e-4f);
        Assert.AreEqual(0f, TacticLibrary.Get("hunt").TensionCost, "the own move costs nothing more");
        Assert.AreEqual(0f, TacticLibrary.Get("cut").TensionCost, "Cut costs nothing more");
    }

    [TestCase(false, false, false, 120f, 0.7f, 0f, true, TestName = "Rested and paid for: a hunt may start")]
    [TestCase(false, false, false, 30f, 0.7f, 0f, false, TestName = "Thirty seconds after the last: cooling down")]
    [TestCase(false, false, false, 120f, 0.4f, 0f, false, TestName = "Too little tension to pay for it")]
    [TestCase(true, false, false, 30f, 0.1f, 0f, true, TestName = "A hunt already running goes on")]
    [TestCase(true, true, false, 120f, 0.7f, 0f, false, TestName = "Never in a recovery")]
    [TestCase(false, false, true, 120f, 0.7f, 0f, false, TestName = "Never while you settle in")]
    [TestCase(true, false, false, 120f, 0.7f, 0.9f, false, TestName = "Never with you outside")]
    public void Hunt_IsPacedByTheRules(bool hunting, bool recovery, bool settling, float sinceLast, float tension, float outside, bool expected)
    {
        Assert.AreEqual(expected, KarenDirector.HuntAllowed(hunting, recovery, settling, sinceLast, tension, outside, new KarenConfig(), out string why), why);
    }

    static KarenContext DirectedContext()
    {
        var config = new KarenConfig { rung = KarenRung.E_Ledger, seed = 7 };
        var ledger = new KarenLedger(config);
        return new KarenContext { Config = config, Rng = new KarenRng(7), Ledger = ledger, Director = new KarenDirector(config, ledger) };
    }

    [Test]
    public void Hunt_IsHeldBack_WhileYouSettleIn_AndInARecovery()
    {
        KarenContext c = DirectedContext();
        c.Director.BeginShift(1, 300f);
        Assert.AreEqual(0f, c.Director.ActionPermission(ActionId.Hunt, c), "a hunt while you settle in");
        c.Director.StartRecovery(45f, c, "test");
        Assert.AreEqual(0f, c.Director.ActionPermission(ActionId.Hunt, c), "a hunt in a recovery");
    }

    [TestCase(true, 0f, 0.2f, 0f, false, 0f, false, TestName = "Seeing you, she keeps going")]
    [TestCase(false, 3f, 8f, 3f, false, 0f, false, TestName = "Lost, but not there yet, she keeps going")]
    [TestCase(false, 2f, 1f, 2f, false, 0f, true, TestName = "There, and you're not: hand over")]
    [TestCase(false, 7f, 6f, 7f, false, 0f, true, TestName = "Six seconds with nothing new: hand over")]
    [TestCase(false, 7f, 6f, 1f, false, 0f, false, TestName = "Lost, but a fresh clue: she keeps going")]
    [TestCase(true, 0f, 3f, 0f, true, 0f, true, TestName = "A recovery: hand over")]
    [TestCase(true, 0f, 3f, 0f, false, 0.9f, true, TestName = "You went outside: hand over")]
    public void Hunt_HandsOver_ByTheRules(bool sees, float lostFor, float toGoal, float staleness, bool recovery, float outside, bool expected)
    {
        Assert.AreEqual(expected, Pursue.HandOver(sees, lostFor, toGoal, staleness, recovery, outside, 6f, out string why), why);
    }

    // ---- phase 3: Throw ----------------------------------------------------------------------

    // An item alone never makes a catch certain: sprinting on after its effect, you're still
    // SafeGap metres ahead of her, from the closest she may throw it.
    [Test]
    public void NoThrow_MakesACatchCertain()
    {
        var config = new KarenConfig();
        var motor = new UnityEngine.GameObject("player").AddComponent<PlayerMotor>();
        try
        {
            foreach (ThrownItem item in System.Enum.GetValues(typeof(ThrownItem)))
            {
                float gap = ThrownItems.GapAfter(item, ThrownItems.MinRange(item), config.runSpeed, motor.sprintSpeed);
                Assert.GreaterOrEqual(gap, ThrownItems.SafeGap, $"{item} from {ThrownItems.MinRange(item)} m leaves you {gap:0.0} m ahead");
                Assert.Less(ThrownItems.MinRange(item), ThrownItems.MaxRange(item), item.ToString());
            }
            Assert.GreaterOrEqual(ThrownItems.MinRange(ThrownItem.Tin), 15f, "a stun only from 15 m or more");
        }
        finally { UnityEngine.Object.DestroyImmediate(motor.gameObject); }
    }

    [Test]
    public void Effects_SlowAndStunYou_AndWearOff()
    {
        var go = new UnityEngine.GameObject("player");
        try
        {
            var fx = go.AddComponent<PlayerEffects>();
            Assert.AreEqual(1f, fx.SpeedFactor);
            fx.Slow(0.25f, 2f);
            Assert.AreEqual(0.75f, fx.SpeedFactor, 1e-4f);
            fx.Stun(1f);
            Assert.AreEqual(0f, fx.SpeedFactor, "stunned, you don't move");
            fx.Clear();
            Assert.AreEqual(1f, fx.SpeedFactor);
            Assert.IsFalse(fx.Blurred || fx.Slowed || fx.Stunned);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void TheRenamedTricks_HaveTheirNewNames()
    {
        Assert.AreEqual("Block door", TacticLibrary.Get("door_lock").Title);
        Assert.AreEqual("Peace offering", TacticLibrary.Get("favour").Title);
    }
}
