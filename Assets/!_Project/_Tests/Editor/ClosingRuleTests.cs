using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Rule 10 (0.3.0, the owner's call of 8 November): once the doors close, Karen makes no new
// chores, so the shift stays finishable. The ablation found shifts that couldn't end: after
// closing she kept adding spills, sweeps and bins faster than they could be cleared, and the
// clock-out waits for every one.
public class ClosingRuleTests
{
    GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("ClosingRuleTests");

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        TacticLibrary.ResetShift();
    }

    static readonly string[] ChoreTricks =
    {
        "shelf_sweep", "spill", "bin_tamper", "tool_theft", "overtime", "fog", "camera_bolt_on",
        "phantom_chime", "door_lock", "understudy", "blackout",
    };

    KarenContext Context(KarenRung rung, out ShiftManager shift, bool shield = true)
    {
        var tasks = root.AddComponent<TaskManager>();
        shift = root.AddComponent<ShiftManager>();
        shift.taskManager = tasks;
        tasks.shiftManager = shift;
        shift.shiftDurationSeconds = 300f;
        shift.StartShift();
        var config = new KarenConfig { rung = rung, seed = 7, shield = shield };
        var ledger = new KarenLedger(config);
        var director = new KarenDirector(config, ledger);
        director.BeginShift(1, 300f);
        return new KarenContext
        {
            Config = config, Rng = new KarenRng(7), Ledger = ledger, Director = director,
            Shift = shift, Tasks = tasks, A = new Appraisal { Tension = 1f },
        };
    }

    // A trick made for the test, so the plan's own check is tested whatever the library holds.
    sealed class Probe : Tactic
    {
        readonly bool chore;
        public Probe(bool chore) => this.chore = chore;
        public override string Id => chore ? "probe_chore" : "probe_no_chore";
        public override string Title => "Closing probe";
        public override ActionId[] Actions => new[] { ActionId.Disrupt };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;
        public override float Cooldown => 0f;
        public override float PanicPrior => 0f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => chore ? "fix it" : "none";
        public override string Attacks => "work";
        public override bool LeavesChore => chore;
        public override void Plan(KarenContext c, PlanBuilder plan) { }
    }

    static PlanTree Tree(Tactic t, params Primitive[] steps) =>
        new PlanTree("probe", ActionId.Disrupt, t, steps.ToList(), _ => false);

    [Test]
    public void TheTricksThatLeaveAChore_AreTheseEleven()
    {
        var live = TacticLibrary.Live.Where(t => !t.Draft && t.LeavesChore).Select(t => t.Id).OrderBy(x => x);
        CollectionAssert.AreEqual(ChoreTricks.OrderBy(x => x), live);
        foreach (string id in new[] { "hunt", "cut", "ambush", "throw_oil", "throw_flour", "throw_tin", "poll_witnesses" })
            Assert.IsFalse(TacticLibrary.Get(id).LeavesChore, $"{id} leaves no chore: she may still do it after closing");
    }

    [Test]
    public void TheDoorsClose_WhenTheCountdownDoes()
    {
        Context(KarenRung.E_Ledger, out ShiftManager shift);
        Assert.IsFalse(shift.DoorsClosed);
        Assert.IsTrue(shift.CustomersAllowed);
        shift.CloseDoors();
        Assert.IsTrue(shift.DoorsClosed);
        Assert.IsFalse(shift.CustomersAllowed);
        Assert.AreEqual(0f, shift.TimeRemaining);
        shift.EndShift();
        Assert.IsFalse(shift.DoorsClosed, "between shifts the doors aren't 'closed', the shift is over");
    }

    [Test]
    public void AfterClosing_NoChoreTrickGetsThroughTheGate_AndTheRestStillCan()
    {
        KarenContext c = Context(KarenRung.E_Ledger, out ShiftManager shift);
        foreach (string id in ChoreTricks)
        {
            HtnPlanner.Gated(TacticLibrary.Get(id), c, out string why);
            Assert.AreNotEqual(FairnessGuard.ClosedWhy, why, $"{id}: the doors are still open");
        }

        shift.CloseDoors();
        foreach (string id in ChoreTricks)
        {
            Assert.IsFalse(HtnPlanner.Gated(TacticLibrary.Get(id), c, out string why), $"{id} got through after closing");
            Assert.AreEqual(FairnessGuard.ClosedWhy, why);
        }
        foreach (string id in new[] { "hunt", "ambush", "throw_oil" })
        {
            HtnPlanner.Gated(TacticLibrary.Get(id), c, out string why);
            Assert.AreNotEqual(FairnessGuard.ClosedWhy, why, $"{id} isn't a chore");
        }
    }

    [Test]
    public void TheRuleHolds_EvenWithRungFsShieldOff()
    {
        KarenContext c = Context(KarenRung.F_Policy, out ShiftManager shift, shield: false);
        shift.CloseDoors();
        Assert.IsFalse(HtnPlanner.Gated(TacticLibrary.Get("spill"), c, out string why));
        Assert.AreEqual(FairnessGuard.ClosedWhy, why, "not a pacing gate: the shield doesn't lift it");
    }

    [Test]
    public void AChoreTrickThatHasntLanded_StopsWhenTheDoorsClose()
    {
        KarenContext c = Context(KarenRung.E_Ledger, out ShiftManager shift);
        bool landed = false, arrived = false;
        PlanTree plan = Tree(new Probe(chore: true),
            new Kehai.Karen.WaitUntil(_ => arrived, 30f, "walk to the bay"),
            new Effect("sweep the bay", _ => landed = true));

        Assert.AreEqual(Status.Running, plan.Tick(c, 0.1f));
        shift.CloseDoors();
        arrived = true;
        Assert.AreEqual(Status.Failure, plan.Tick(c, 0.1f));
        Assert.IsTrue(plan.StoppedAtClosing);
        Assert.IsFalse(landed, "no new chore after closing");
    }

    [Test]
    public void AChoreTrickThatHasLanded_FinishesWhatItStarted()
    {
        // Tool theft takes the mop, then carries it off: stopping halfway would leave it in her hands.
        KarenContext c = Context(KarenRung.E_Ledger, out ShiftManager shift);
        bool dropped = false, carried = false;
        PlanTree plan = Tree(new Probe(chore: true),
            new Effect("take the mop", _ => { }),
            new Kehai.Karen.WaitUntil(_ => carried, 30f, "carry it off"),
            new Effect("leave it", _ => dropped = true));

        Assert.AreEqual(Status.Running, plan.Tick(c, 0.1f));
        Assert.IsTrue(plan.Plan.Landed);
        shift.CloseDoors();
        carried = true;
        Assert.AreEqual(Status.Success, plan.Tick(c, 0.1f));
        Assert.IsFalse(plan.StoppedAtClosing);
        Assert.IsTrue(dropped);
    }

    [Test]
    public void ATrickWithNoChore_CarriesOnAfterClosing()
    {
        KarenContext c = Context(KarenRung.E_Ledger, out ShiftManager shift);
        bool done = false, ready = false;
        PlanTree plan = Tree(new Probe(chore: false),
            new Kehai.Karen.WaitUntil(_ => ready, 30f, "wait on your way"),
            new Effect("step out", _ => done = true));

        Assert.AreEqual(Status.Running, plan.Tick(c, 0.1f));
        shift.CloseDoors();
        ready = true;
        Assert.AreEqual(Status.Success, plan.Tick(c, 0.1f));
        Assert.IsTrue(done);
    }
}
