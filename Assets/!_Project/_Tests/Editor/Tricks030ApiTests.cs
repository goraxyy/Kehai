using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.3.0, the APIs the tricks and the second builder build on (build plan C1.6): the
// stopped clock, the fog, the PA's power, the new jobs, the new sounds.
public class Tricks030ApiTests
{
    GameObject root;

    [SetUp]
    public void SetUp() => root = new GameObject("Tricks030ApiTests");

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        FogState.Reset();
        FogState.RoomOf = p => Kehai.Store.StoreMap.AreaAt(p);
        FogState.Clock = () => Time.time;
    }

    (ShiftManager shift, TaskManager tasks) Shift()
    {
        var tasks = root.AddComponent<TaskManager>();
        var shift = root.AddComponent<ShiftManager>();
        shift.taskManager = tasks;
        tasks.shiftManager = shift;
        return (shift, tasks);
    }

    // ---- the overtime's clock ------------------------------------------------------------

    [Test]
    public void TheClock_FreezesOnlyDuringAShift()
    {
        var (shift, _) = Shift();
        Assert.IsFalse(shift.Freeze(), "no shift, nothing to stop");
        shift.StartShift();
        Assert.IsTrue(shift.Freeze());
        Assert.IsTrue(shift.IsFrozen);
        Assert.IsFalse(shift.Freeze(), "already stopped");
    }

    [Test]
    public void Freezing_SaysSo_AndUnfreezingStartsItAgain()
    {
        var (shift, _) = Shift();
        int changes = 0;
        shift.FreezeChanged += () => changes++;
        shift.StartShift();
        float left = shift.TimeRemaining;
        shift.Freeze();
        Assert.AreEqual(1, changes);
        Assert.IsTrue(shift.Unfreeze());
        Assert.IsFalse(shift.IsFrozen);
        Assert.AreEqual(2, changes);
        Assert.AreEqual(left, shift.TimeRemaining, 1e-4f, "it starts again from where it stopped");
        Assert.IsFalse(shift.Unfreeze());
    }

    [Test]
    public void AShiftThatEnds_TakesTheStopWithIt()
    {
        var (shift, _) = Shift();
        shift.StartShift();
        shift.Freeze();
        shift.EndShift();
        Assert.IsFalse(shift.IsFrozen);
        shift.StartShift();
        Assert.IsFalse(shift.IsFrozen);
    }

    [Test]
    public void AStoppedClock_IsAJob_AndKeepsYouFromClockingOut()
    {
        var (shift, tasks) = Shift();
        shift.StartShift();
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.RestartClock));
        bool allDoneBefore = tasks.AllComplete;

        shift.Freeze();
        TaskManager.ShiftTask job = tasks.Tasks.Single(t => t.Kind == TaskManager.TaskKind.RestartClock);
        Assert.AreEqual("Restart the time clock", job.Label);
        Assert.IsFalse(job.IsComplete);
        Assert.IsFalse(tasks.AllComplete);
        Assert.IsFalse(shift.CanClockOut);

        shift.Unfreeze();
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.RestartClock));
        Assert.AreEqual(allDoneBefore, tasks.AllComplete);
    }

    // ---- a job's place --------------------------------------------------------------------

    [Test]
    public void AJobsPlace_ShowsWhileItsOpen()
    {
        var (shift, tasks) = Shift();
        shift.StartShift();
        tasks.Hint(TaskManager.TaskKind.Stock, "Aisle 4");
        Assert.AreEqual("Aisle 4", tasks.HintFor(TaskManager.TaskKind.Stock));
        tasks.Hint(TaskManager.TaskKind.Stock, null);
        Assert.AreEqual(string.Empty, tasks.HintFor(TaskManager.TaskKind.Stock));
    }

    // ---- the mop she hid ------------------------------------------------------------------

    [Test]
    public void AToolSheTook_IsAJob_UntilItsBack()
    {
        var (shift, tasks) = Shift();
        shift.StartShift();
        var mopGo = new GameObject("WC mop");
        mopGo.transform.SetParent(root.transform);
        var mop = mopGo.AddComponent<Item>();
        mop.type = ItemType.Mop;
        var rackGo = new GameObject("WC mop rack");
        rackGo.transform.SetParent(root.transform);
        var rack = rackGo.AddComponent<ToolSnapPoint>();
        rack.tool = mop;
        rack.place = "WC";

        rack.MarkTaken(true);
        Assert.AreEqual(1, ToolSnapPoint.TakenCount);
        TaskManager.ShiftTask job = tasks.Tasks.Single(t => t.Kind == TaskManager.TaskKind.ReturnTool);
        Assert.AreEqual("Return the WC mop", job.Label);
        Assert.IsFalse(tasks.AllComplete);

        mopGo.transform.position = new Vector3(20f, 0f, 0f);
        Assert.AreEqual("She moved it. Find it.", rack.GetPrompt());

        rack.MarkTaken(false);
        Assert.AreEqual(0, ToolSnapPoint.TakenCount);
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.ReturnTool));
    }

    [Test]
    public void ARackThatGoes_LeavesNoJobBehind()
    {
        var rackGo = new GameObject("rack");
        var rack = rackGo.AddComponent<ToolSnapPoint>();
        rack.MarkTaken(true);
        Object.DestroyImmediate(rackGo);
        Assert.AreEqual(0, ToolSnapPoint.TakenCount);
    }

    // ---- fog ------------------------------------------------------------------------------

    float now;

    void FakeFog()
    {
        now = 0f;
        FogState.Clock = () => now;
        FogState.RoomOf = p => p.x < 0f ? "Staff room" : "Sales floor";
    }

    [Test]
    public void Fog_RollsIn_ToTheVisibility()
    {
        FakeFog();
        var at = new Vector3(5f, 0f, 0f);
        FogState.Fill("Sales floor", 2f, 8f);
        Assert.IsTrue(FogState.IsFogged("Sales floor"));
        Assert.AreEqual(FogState.Open, FogState.VisibilityAt(at), 1e-3f, "not yet");
        now = 4f;
        Assert.AreEqual(Mathf.Lerp(FogState.Open, 2f, 0.5f), FogState.VisibilityAt(at), 1e-3f);
        now = 8f;
        Assert.AreEqual(2f, FogState.VisibilityAt(at), 1e-3f);
        Assert.AreEqual(FogState.Open, FogState.VisibilityAt(new Vector3(-5f, 0f, 0f)), 1e-3f, "the staff room is clear");
    }

    [Test]
    public void Fog_LimitsEveryEye_AtEitherEnd()
    {
        FakeFog();
        FogState.Fill("Sales floor", 2f, 0.01f);
        now = 1f;
        var inFog = new Vector3(5f, 0f, 0f);
        var outside = new Vector3(-5f, 0f, 0f);
        Assert.AreEqual(2f, FogState.SightLimit(outside, inFog), 1e-3f, "looking into it");
        Assert.AreEqual(2f, FogState.SightLimit(inFog, outside), 1e-3f, "looking out of it");
        Assert.AreEqual(FogState.Open, FogState.SightLimit(outside, outside), 1e-3f);
    }

    [Test]
    public void Fog_Thins_WhenTheBoxIsReset_AndSaysSo()
    {
        FakeFog();
        string filled = null, cleared = null;
        System.Action<string> onFill = r => filled = r, onClear = r => cleared = r;
        FogState.Filled += onFill;
        FogState.Cleared += onClear;
        try
        {
            FogState.Fill("Sales floor", 2f, 0.01f);
            Assert.AreEqual("Sales floor", filled);
            now = 10f;
            FogState.Clear("Sales floor", 3f);
            Assert.AreEqual("Sales floor", cleared);
            Assert.IsFalse(FogState.IsFogged("Sales floor"));
            now = 11.5f;
            Assert.AreEqual(Mathf.Lerp(FogState.Open, 2f, 0.5f), FogState.VisibilityAt(new Vector3(5f, 0f, 0f)), 1e-3f);
            now = 13.1f;
            Assert.AreEqual(FogState.Open, FogState.VisibilityAt(new Vector3(5f, 0f, 0f)), 1e-3f);
            Assert.IsFalse(FogState.Any);
        }
        finally
        {
            FogState.Filled -= onFill;
            FogState.Cleared -= onClear;
        }
    }

    [Test]
    public void TheThickness_IsAdjustable()
    {
        FakeFog();
        FogState.Fill("Sales floor", 5f, 0.01f);
        now = 1f;
        Assert.AreEqual(5f, FogState.VisibilityAt(new Vector3(5f, 0f, 0f)), 1e-3f);
        FogState.Fill("Sales floor", 1f, 0.01f);
        Assert.AreEqual(1f, FogState.VisibilityAt(new Vector3(5f, 0f, 0f)), 1e-3f);
    }

    // Fog blinds her too (the owner's answer, 7 Nov): her eye, a camera's, Kenji's.
    [Test]
    public void InFog_AnEyeSeesOnlyAsFarAsTheVisibility_AndAllTheWayOnceItClears()
    {
        FakeFog();
        var eyeGo = new GameObject("eye");
        eyeGo.transform.SetParent(root.transform);
        eyeGo.transform.position = new Vector3(5f, 1.5f, 0f);   // the sales floor, looking along +z
        var eye = eyeGo.AddComponent<SightSensor>();
        eye.range = 18f;
        eye.fieldOfView = 120f;

        var near = new Vector3(5f, 1.5f, 1.5f);
        var far = new Vector3(5f, 1.5f, 8f);
        Assert.IsTrue(eye.CanSee(far), "clear: 8 m is in range");

        FogState.Fill("Sales floor", 2f, 0.01f);
        now = 1f;
        Assert.AreEqual(2f, eye.ReachTo(far), 1e-3f);
        Assert.IsTrue(eye.CanSee(near), "1.5 m, inside the visibility");
        Assert.IsFalse(eye.CanSee(far), "8 m, lost in the fog");

        FogState.Clear("Sales floor", 0.01f);
        now = 2f;
        Assert.IsTrue(eye.CanSee(far));
    }

    [Test]
    public void TheFridges_AreFoundByName()
    {
        foreach (string n in new[] { "RetailChiller_03", "Storage_Chiller", "Staff_Fridge_01", "Prop_Fridge_02", "Cold_Freezer" })
            Assert.IsTrue(KarenWorld.IsFridge(n), n);
        foreach (string n in new[] { "Label_Retail_Chiller_1_8", "Shelf_Unit", "Breaker_Box" })
            Assert.IsFalse(KarenWorld.IsFridge(n), n);
    }

    [Test]
    public void FogsRooms_AreTheOnesWithFridges()
    {
        CollectionAssert.AreEquivalent(new[] { "Sales floor", "Cold storage", "Staff room" }, KarenWorld.FogRooms);
        Assert.AreEqual(2f, new KarenConfig().fogVisibility, 1e-4f, "2 m by default");
    }

    [Test]
    public void TheStoresRooms_AreTheMapsAreas()
    {
        Assert.AreEqual("Staff room", Kehai.Store.StoreMap.AreaAt(new Vector3(15f, 0f, -140f)));
        Assert.AreEqual("Cold storage", Kehai.Store.StoreMap.AreaAt(new Vector3(60f, 0f, -180f)));
        Assert.AreEqual("Sales floor", Kehai.Store.StoreMap.AreaAt(new Vector3(50f, 0f, -150f)));
    }

    // ---- the PA's power ---------------------------------------------------------------------

    [Test]
    public void APaSwitchedOff_SaysNothing_UntilTheShiftsEnd()
    {
        var pa = root.AddComponent<PaSystem>();
        bool? last = null;
        pa.PowerChanged += on => last = on;
        Assert.IsTrue(pa.Powered);

        pa.SetPowered(false);
        Assert.IsFalse(pa.Powered);
        Assert.AreEqual(false, last);
        PaAnnouncement a = pa.Announce("Cleanup in aisle four.");
        Assert.IsTrue(a.Jammed);
        Assert.IsTrue(a.Done);

        pa.Clear();   // the shift's end
        Assert.IsTrue(pa.Powered);
        Assert.AreEqual(true, last);
    }

    // ---- the new sounds -------------------------------------------------------------------------

    [Test]
    public void TheCameraBeep_AndTheClocksSounds_Exist()
    {
        AudioClip beep = ProceduralAudio.CameraBeep();
        Assert.IsNotNull(beep);
        Assert.Less(beep.length, 0.5f, "a pip, once a second");
        Assert.IsNotNull(ProceduralAudio.ClockStop());
        Assert.IsNotNull(ProceduralAudio.ClockStart());
    }
}
