using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;

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
    public void TheLiveTricks_AreTheFourThePlanKeeps()
    {
        var live = TacticLibrary.Live.Where(t => !t.OwnMove).Select(t => t.Id).OrderBy(id => id).ToList();
        CollectionAssert.AreEqual(new[] { "ambush", "bin_tamper", "blackout", "poll_witnesses" }, live);
    }

    // Roam, Check, Search, Hunt and Back off move on their own; Lie in wait, Flush and Disrupt
    // have a live trick; Corner, Loom and Lure have neither until their drafts come back.
    [Test]
    public void EveryAction_HasItsOwnMoveOrALiveTrick_ButCornerLoomAndLure()
    {
        foreach (ActionId a in new[] { ActionId.Roam, ActionId.Check, ActionId.Search, ActionId.Hunt, ActionId.BackOff })
            Assert.IsTrue(TacticLibrary.For(a).Any(t => t.OwnMove && !t.Draft), $"{a.Name()} has no own move");
        var dormant = AllActions.Where(a => !TacticLibrary.For(a).Any(t => !t.Draft)).ToList();
        CollectionAssert.AreEquivalent(new[] { ActionId.Corner, ActionId.Loom, ActionId.Lure }, dormant);
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

    [Test]
    public void Ambush_ComesOnShiftThree()
    {
        Assert.AreEqual(3, TacticLibrary.Get("ambush").IntroducedShift);
    }

    // ---- phase 2: Hunt -----------------------------------------------------------------------

    [Test]
    public void Hunt_CostsAndCoolsLikeTheChaseDid()
    {
        Tactic hunt = TacticLibrary.Get("hunt");
        Assert.AreEqual(0.6f, hunt.TensionCost, 1e-4f);
        Assert.AreEqual(90f, hunt.Cooldown, 1e-4f);
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
        Assert.AreEqual(1f, c.Director.ActionPermission(ActionId.Hunt, c), "off shift and outside nothing, the Director doesn't stop a hunt");
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

    [Test]
    public void TheRenamedTricks_HaveTheirNewNames()
    {
        Assert.AreEqual("Block door", TacticLibrary.Get("door_lock").Title);
        Assert.AreEqual("Peace offering", TacticLibrary.Get("favour").Title);
    }
}
