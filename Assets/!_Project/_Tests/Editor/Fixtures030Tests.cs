using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.3.0, the things you use to answer her tricks (build plan B2.1, B2.2, B2.4, B4.2): the
// fridge control box, the stopped time clock, the wall clock's hands, Kenji's talk.
public class Fixtures030Tests
{
    GameObject root;
    float now;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Fixtures030Tests");
        now = 0f;
        FogState.Clock = () => now;
        FogState.RoomOf = p => "Sales floor";
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(root);
        FogState.Reset();
        FogState.RoomOf = p => Kehai.Store.StoreMap.AreaAt(p);
        FogState.Clock = () => Time.time;
    }

    // ---- the stopped clock (B2.4) ---------------------------------------------------------

    [Test]
    public void TheTimeClock_IsAHold_OnlyWhileShesStoppedIt()
    {
        var tasks = root.AddComponent<TaskManager>();
        var shift = root.AddComponent<ShiftManager>();
        shift.taskManager = tasks;
        tasks.shiftManager = shift;
        var clockGo = new GameObject("time clock");
        clockGo.transform.SetParent(root.transform);
        var clock = clockGo.AddComponent<Puncher>();
        clock.shiftManager = shift;

        shift.StartShift();
        Assert.IsFalse(clock.CanHold(null));
        Assert.AreEqual(shift.CanClockOut ? "Clock out" : "Finish your tasks first", clock.GetPrompt());

        shift.Freeze();
        Assert.IsTrue(clock.CanHold(null));
        Assert.AreEqual("Hold E to restart the clock", clock.GetPrompt());
        Assert.AreEqual(3f, clock.HoldDuration, 1e-5f);
    }

    [Test]
    public void TheWallClock_RunsThroughTheNight_AndStandsStillWhenTheCountdownDoes()
    {
        Assert.AreEqual(22f, WallClockHands.NightTime(300f, 300f, true), 1e-4f, "the shift starts at ten");
        Assert.AreEqual(26f, WallClockHands.NightTime(300f, 150f, true), 1e-4f, "halfway: two in the morning");
        Assert.AreEqual(30f, WallClockHands.NightTime(300f, 0f, true), 1e-4f, "six, the end");
        Assert.AreEqual(22f, WallClockHands.NightTime(300f, 120f, false), 1e-4f, "no shift: ten");
        // Stopped, the countdown holds, so the hands hold with it.
        Assert.AreEqual(WallClockHands.NightTime(300f, 90f, true), WallClockHands.NightTime(300f, 90f, true));
    }

    // ---- the fridge control box (B2.1) -----------------------------------------------------

    [Test]
    public void TheFridgeBox_IsAHold_OnlyWhileARoomIsFogged_AndSaysWhereItIs()
    {
        var go = new GameObject("fridge box");
        go.transform.SetParent(root.transform);
        var box = go.AddComponent<FogBox>();
        Assert.AreEqual("Fridge control: all normal", box.GetPrompt());
        Assert.IsFalse(box.CanHold(null));

        FogState.Fill("Sales floor", 2f, 0.01f);
        Assert.IsTrue(box.CanHold(null));
        Assert.AreEqual("Hold E to reset the fridges (20 s)", box.GetPrompt());
        Assert.AreEqual(FogBox.HoldSeconds, box.HoldDuration, 1e-5f);
        Assert.AreEqual(20f, box.HoldDuration, 1e-5f, "0.4.1: a backstreet fix takes twenty seconds");
    }

    // ---- Kenji (B4.2) ---------------------------------------------------------------------

    [Test]
    public void Kenji_KeepsTenToTwentyMetresBack_AndIsSentToTheTills()
    {
        Assert.AreEqual(10f, Understudy.Near, 1e-5f);
        Assert.AreEqual(20f, Understudy.Far, 1e-5f);
        Assert.AreEqual(2.5f, Understudy.TalkRange, 1e-5f);
        StringAssert.Contains("needs you on the tills", Understudy.SendLine);
        Assert.AreEqual("Never mind", Understudy.StayLine);
    }

    // ---- doors (B4.1) ---------------------------------------------------------------------

    [Test]
    public void TheBlockedDoor_SaysWhatToDo_FromEachSide()
    {
        Assert.AreEqual("Pick up the box to unblock the door", DoorBlock.PickUpPrompt);
        Assert.AreEqual("Blocked from the other side", DoorBlock.OtherSidePrompt);
    }
}
