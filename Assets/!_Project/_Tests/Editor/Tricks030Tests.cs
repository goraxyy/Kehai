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
}
