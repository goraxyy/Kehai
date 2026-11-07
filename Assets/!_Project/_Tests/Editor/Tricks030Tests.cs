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
}
