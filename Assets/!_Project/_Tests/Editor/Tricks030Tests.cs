using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.3.0, the returning tricks' own rules (build plan phases 2–4). The end-to-end checks
// in a real shift are TricksEndToEndTests (C5.5).
public class Tricks030Tests
{
    GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("Tricks030Tests");

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        TacticLibrary.ResetShift();
    }

    KarenContext WithShift(float seconds, out ShiftManager shift)
    {
        var tasks = root.AddComponent<TaskManager>();
        shift = root.AddComponent<ShiftManager>();
        shift.taskManager = tasks;
        tasks.shiftManager = shift;
        shift.shiftDurationSeconds = seconds;
        shift.StartShift();
        return new KarenContext { Config = new KarenConfig { seed = 7 }, Rng = new KarenRng(7), Shift = shift, Tasks = tasks };
    }

    // ---- the overtime (C2.4) ---------------------------------------------------------------

    [Test]
    public void TheOvertime_WaitsForTheShiftsLastTwoMinutes()
    {
        Tactic overtime = TacticLibrary.Get("overtime");
        KarenContext c = WithShift(300f, out _);
        Assert.IsFalse(overtime.Available(c, out string why));
        Assert.AreEqual("not the shift's last two minutes", why);
    }

    [Test]
    public void TheOvertime_InTheLastTwoMinutes_GoesLookingForTheClock()
    {
        Tactic overtime = TacticLibrary.Get("overtime");
        KarenContext c = WithShift(100f, out _);
        Assert.IsFalse(overtime.Available(c, out string why), "this test has no map, so no clock");
        Assert.AreEqual("no time clock", why, "every other gate passed");
    }

    [Test]
    public void TheOvertime_OnceAShift_AndNotWhileTheClocksStopped()
    {
        Tactic overtime = TacticLibrary.Get("overtime");
        KarenContext c = WithShift(100f, out ShiftManager shift);
        shift.Freeze();
        Assert.IsFalse(overtime.Available(c, out string why));
        Assert.AreEqual("the clock is already stopped", why);
        Assert.IsTrue(overtime.EffectRunning(c), "running while the clock stands");

        shift.Unfreeze();
        Assert.IsFalse(overtime.EffectRunning(c));
        overtime.UsesThisShift = 1;
        Assert.IsFalse(overtime.Available(c, out why));
        Assert.AreEqual("once a shift", why);
    }

    // The old version refused your clock-out and added two minutes. That's gone.
    [Test]
    public void TheOldOvertime_IsGone()
    {
        Assert.IsNull(typeof(ShiftManager).GetField("ClockOutGuard"));
        Assert.IsNull(typeof(ShiftManager).GetMethod("AddOvertime"));
        Assert.IsNull(typeof(KarenBrain).GetMethod("ArmOvertime"));
        StringAssert.Contains("restart the time clock", TacticLibrary.Get("overtime").Chore);
    }

    // ---- Camera bolt-on (C3.5) -------------------------------------------------------------

    static List<CameraPlacement.HeatCell> HeatAt(params Vector3[] at) =>
        at.Select(p => new CameraPlacement.HeatCell(p, 1f)).ToList();

    [Test]
    public void ACamera_SeesTheHeatInItsCone_AndNotBehindIt()
    {
        var eye = new Vector3(0f, 1.2f, 0f);
        var heat = HeatAt(new Vector3(0f, 1.2f, 8f), new Vector3(0f, 1.2f, -8f));
        float ahead = CameraPlacement.Coverage(eye, Vector3.forward, heat, null);
        float behind = CameraPlacement.Coverage(eye, Vector3.back, heat, null);
        Assert.Greater(ahead, 0f);
        Assert.AreEqual(ahead, behind, 1e-5f, "one spot each way, the same distance");
        Assert.AreEqual(0f, CameraPlacement.Coverage(eye, Vector3.right, heat, null), 1e-5f, "neither in a 70° cone to the side");
    }

    [Test]
    public void ACamera_OnlySeesWhatItHasALineTo_AndWithinItsRange()
    {
        var eye = new Vector3(0f, 1.2f, 0f);
        var heat = HeatAt(new Vector3(0f, 1.2f, 6f), new Vector3(0f, 1.2f, 30f), new Vector3(0f, 1.2f, 1f));
        float all = CameraPlacement.Coverage(eye, Vector3.forward, heat, null);
        float blocked = CameraPlacement.Coverage(eye, Vector3.forward, heat, new[] { false, true, true });
        Assert.Greater(all, 0f, "6 m away, in view");
        Assert.AreEqual(0f, blocked, 1e-5f, "the 6 m spot behind a shelf; 30 m is out of range, 1 m too close");
    }

    [Test]
    public void TheEightTurnsAndThreeTilts_PointWhereTheySay()
    {
        Assert.AreEqual(8, CameraPlacement.Yaws.Length);
        CollectionAssert.AreEqual(new[] { 0f, -10f, -20f }, CameraPlacement.Pitches);
        Vector3 straight = CameraPlacement.Forward(Vector3.forward, 0f, 0f);
        Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, straight), 1e-3f);
        Vector3 down = CameraPlacement.Forward(Vector3.forward, 0f, -20f);
        Assert.Less(down.y, 0f, "a negative tilt looks down");
        Assert.AreEqual(20f, Vector3.Angle(Vector3.forward, down), 1e-2f);
        Vector3 turned = CameraPlacement.Forward(Vector3.forward, 70f, 0f);
        Assert.AreEqual(70f, Vector3.Angle(Vector3.forward, turned), 1e-2f);
        Assert.Greater(turned.x, 0f);
    }

    [Test]
    public void ASlotHerCameraStandsIn_CantBeStocked_AndIsntAGap()
    {
        var bayGo = new GameObject("bay");
        bayGo.transform.SetParent(root.transform);
        var bay = bayGo.AddComponent<ShelfUnit>();
        var slot = new ShelfSlot(bay, bayGo.transform, Vector3.zero, false, new Vector2(0.25f, 0.25f), 0.2f);
        bay.SetSlots(new List<ShelfSlot> { slot });
        Assert.IsFalse(bay.IsFull, "an empty slot is a gap");

        var camera = new object();
        Assert.IsTrue(slot.Block(camera));
        Assert.IsTrue(bay.IsFull, "the camera's slot isn't a gap to restock");
        Assert.IsFalse(slot.Fill(), "nothing can be stocked over the camera");
        Assert.IsFalse(slot.Block(new object()), "one camera a slot");

        slot.Unblock(new object());
        Assert.IsTrue(slot.IsBlocked, "only the camera that holds it can let it go");
        slot.Unblock(camera);
        Assert.IsFalse(slot.IsBlocked);
        Assert.IsFalse(bay.IsFull, "with the camera gone, the gap is back");
    }

    [Test]
    public void SheKeepsAtMostTwoCamerasUp()
    {
        Assert.AreEqual(2, KarenWorld.MaxCameras);
    }

    // ---- Tool theft (C3.4) -----------------------------------------------------------------

    [Test]
    public void ToolTheft_OnlyTakesTheWcMop()
    {
        Assert.AreEqual("WC", ToolTheftTactic.Rack);
        var staffGo = new GameObject("staff room mop rack");
        staffGo.transform.SetParent(root.transform);
        var mopGo = new GameObject("staff room mop");
        mopGo.transform.SetParent(root.transform);
        var mop = mopGo.AddComponent<Item>();
        mop.type = ItemType.Mop;
        var staff = staffGo.AddComponent<ToolSnapPoint>();
        staff.tool = mop;
        Assert.IsNull(ToolTheftTactic.WcRack(), "the staff room's rack isn't hers to take from");
        staff.place = "WC";
        Assert.AreSame(staff, ToolTheftTactic.WcRack());
    }

    // ---- speed: a bay's stand point, worked out once ----------------------------------------

    [Test]
    public void ABaysStandPoint_IsWorkedOutOnce_AndAgainWhenTheBayMoves()
    {
        var bay = new GameObject("Bay").AddComponent<ShelfUnit>();
        bay.transform.SetParent(root.transform, false);
        var point = new GameObject("ShelfPoint").transform;
        point.SetParent(bay.transform, false);
        point.localPosition = new Vector3(0f, 0f, -1f);
        TacticHelpers.ForgetStands();

        Vector3 first = TacticHelpers.StandIn(bay);
        Assert.AreEqual(TacticHelpers.FindStand(bay), first, "the same answer as working it out");

        point.localPosition = new Vector3(0f, 0f, -3f);
        Assert.AreEqual(first, TacticHelpers.StandIn(bay), "kept while the bay stays put");

        bay.transform.position += Vector3.right * 5f;
        Assert.AreEqual(TacticHelpers.FindStand(bay), TacticHelpers.StandIn(bay), "worked out again once it moves");

        TacticHelpers.ForgetStands();
    }

    // ---- finish what she starts (the owner, 8 November) -------------------------------------

    [Test]
    public void AHandsOnTrickUnderWay_IsKept_UnlessAHuntWins()
    {
        Tactic spill = TacticLibrary.Get("spill");
        Assert.IsTrue(spill.IsHandsOn);
        foreach (ActionId next in new[] { ActionId.Roam, ActionId.Search, ActionId.Disrupt, ActionId.Flush, ActionId.LieInWait })
            Assert.IsTrue(KarenBrain.FinishesFirst(spill, 5f, next), $"dropped for {next}");
        Assert.IsFalse(KarenBrain.FinishesFirst(spill, 5f, ActionId.Hunt), "a hunt wins");
        Assert.IsTrue(KarenBrain.IgnoresNoise(spill, 5f), "a noise doesn't make her start over");
    }

    [Test]
    public void FinishingFirst_IsOnlyForHandsOnTricks_AndOnlyForAMinute()
    {
        Tactic spill = TacticLibrary.Get("spill"), fog = TacticLibrary.Get("fog");
        Assert.IsFalse(fog.IsHandsOn);
        Assert.IsFalse(KarenBrain.FinishesFirst(fog, 5f, ActionId.Roam), "the building's tricks land at once: nothing to finish");
        Assert.IsFalse(KarenBrain.IgnoresNoise(fog, 5f));
        Assert.IsFalse(KarenBrain.FinishesFirst(spill, KarenBrain.CommitSeconds + 1f, ActionId.Roam), "a plan that can't finish doesn't hold her");
        Assert.IsFalse(KarenBrain.FinishesFirst(null, 5f, ActionId.Roam));
    }

    // Choose(commit: false) only looks; Commit is what moves her onto a goal (Choose itself needs
    // a whole Karen, so the look runs in the eval's shifts).
    [Test]
    public void Commit_MovesHerOntoAGoal()
    {
        var c = new KarenContext { Config = new KarenConfig { rung = KarenRung.E_Ledger, seed = 7 }, Rng = new KarenRng(7) };
        var actions = new ActionSet();
        actions.Reset();
        Assert.AreEqual(ActionId.Roam, actions.Current);
        actions.Commit(ActionId.Hunt, c);
        Assert.AreEqual(ActionId.Hunt, actions.Current);
        Assert.AreEqual(c.Now, actions.CommittedAt);
    }
}
