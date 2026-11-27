using NUnit.Framework;

// This fixture also runs unchanged under tools/build/check_phase67.py, without Unity.
public sealed class EditionCareerGuideTests
{
    static FirstShiftGuideState Working()
    {
        var s = new FirstShiftGuideState();
        s.Observe(GuideSignal.ClockedIn); s.Tick(0f);
        s.Observe(GuideSignal.TaskListOpened); s.Tick(0f);
        Assert.AreEqual(GuideStep.Waiting, s.Step);
        return s;
    }
    [Test] public void AnEditorRunCanPlayAsAnotherEdition()
    {
        Assert.AreEqual(KehaiEdition.Training, EditionRules.FromCommandLine(new[] { "Unity", "-kehai-edition", "training" }));
        Assert.AreEqual(KehaiEdition.Gameplay, EditionRules.FromCommandLine(new[] { "-kehai-edition", "Gameplay" }));
        Assert.IsNull(EditionRules.FromCommandLine(new[] { "Unity", "-batchmode" }));
        Assert.IsNull(EditionRules.FromCommandLine(new[] { "-kehai-edition", "nightly" }));
        Assert.IsNull(EditionRules.FromCommandLine(new[] { "-kehai-edition" }));
    }

    [Test] public void EditionNamesAreExplicit()
    {
        Assert.IsTrue(EditionRules.TryParse(" Training ", out var e)); Assert.AreEqual(KehaiEdition.Training, e);
        Assert.IsTrue(EditionRules.TryParse("TESTING", out e)); Assert.AreEqual(KehaiEdition.Testing, e);
        Assert.IsTrue(EditionRules.TryParse("gameplay", out e)); Assert.AreEqual(KehaiEdition.Gameplay, e);
        Assert.IsFalse(EditionRules.TryParse("train", out e)); Assert.IsFalse(EditionRules.TryParse(null, out e));
    }
    [Test] public void GameplayHasThreeKarenNights()
    {
        Assert.IsFalse(EditionRules.KarenAppears(KehaiEdition.Gameplay, 0));
        Assert.IsFalse(EditionRules.KarenAppears(KehaiEdition.Gameplay, 1));
        for (int night = 2; night <= 4; night++) Assert.IsTrue(EditionRules.KarenAppears(KehaiEdition.Gameplay, night));
        Assert.IsTrue(EditionRules.KarenAppears(KehaiEdition.Testing, 1));
        Assert.IsTrue(EditionRules.KarenAppears(KehaiEdition.Training, 1));
    }
    [Test] public void BenchmarkSimulationUsesGameplayPresenceInTrainingAndTesting()
    {
        foreach (var edition in new[] { KehaiEdition.Training, KehaiEdition.Testing })
        {
            Assert.IsTrue(EditionRules.KarenAppears(edition, 1));
            Assert.IsFalse(EditionRules.KarenAppears(edition, 1, gameplaySimulation: true));
            for (int shift = 2; shift <= 4; shift++)
                Assert.IsTrue(EditionRules.KarenAppears(edition, shift, gameplaySimulation: true));
        }
    }
    [Test] public void EditionCapabilitiesKeepTrainingInvisibleAndGameplayPrivate()
    {
        Assert.IsFalse(EditionRules.ShowUI(KehaiEdition.Training));
        Assert.IsTrue(EditionRules.ShowUI(KehaiEdition.Testing));
        Assert.IsTrue(EditionRules.ShowUI(KehaiEdition.Gameplay));
        Assert.IsFalse(EditionRules.AllowDebugTools(KehaiEdition.Training));
        Assert.IsTrue(EditionRules.AllowDebugTools(KehaiEdition.Testing));
        Assert.IsFalse(EditionRules.AllowDebugTools(KehaiEdition.Gameplay));
        Assert.IsTrue(EditionRules.AllowEvaluation(KehaiEdition.Training));
        Assert.IsFalse(EditionRules.AllowEvaluation(KehaiEdition.Gameplay));
        Assert.IsFalse(EditionRules.RecordReplays(KehaiEdition.Training));
        Assert.IsFalse(EditionRules.RecordShiftData(KehaiEdition.Gameplay));
        Assert.IsTrue(EditionRules.RecordShiftData(KehaiEdition.Training));
    }
    [Test] public void VersionAndDefineStampAreEditionSpecific()
    {
        Assert.AreEqual("0.5.0", EditionRules.VersionLabel("0.5.0", KehaiEdition.Gameplay));
        Assert.AreEqual("0.5.0-training", EditionRules.VersionLabel("0.5.0", KehaiEdition.Training));
        Assert.AreEqual("0.5.0-testing", EditionRules.VersionLabel("0.5.0", KehaiEdition.Testing));
        Assert.AreEqual("KEHAI_TRAINING", EditionRules.Define(KehaiEdition.Training));
        Assert.AreEqual("KEHAI_GAMEPLAY", EditionRules.Define(KehaiEdition.Gameplay));
    }
    [Test] public void FourSuccessfulShiftsFinishOnce()
    {
        var c = new CareerProgress();
        for (int night = 1; night <= 4; night++)
        {
            Assert.AreEqual(night, c.UpcomingShift); Assert.IsTrue(c.TryStart());
            Assert.AreEqual(night, c.ShiftNumber); Assert.IsFalse(c.TryStart());
            Assert.IsTrue(c.End(true)); Assert.IsFalse(c.End(true));
            Assert.AreEqual(night, c.CompletedShifts);
        }
        Assert.IsTrue(c.Complete); Assert.IsFalse(c.TryStart());
    }
    [Test] public void FailedFourthNightRemainsRetryable()
    {
        var c = new CareerProgress(); c.Restore(3); c.TryStart(); c.End(false);
        Assert.AreEqual(3, c.CompletedShifts); Assert.IsFalse(c.Complete);
        Assert.AreEqual(4, c.UpcomingShift); Assert.IsTrue(c.TryStart());
        c.End(true); Assert.IsTrue(c.Complete);
    }
    [Test] public void CareerRestoreCannotRenumberAnActiveShift()
    {
        var c = new CareerProgress(); c.Restore(-5); Assert.AreEqual(0, c.CompletedShifts);
        c.Restore(2); c.TryStart(); c.Restore(0);
        Assert.AreEqual(3, c.ShiftNumber); Assert.AreEqual(2, c.CompletedShifts);
    }
    [Test] public void GuideEligibilityExcludesTrainingBotsBatchLaterNightsAndReplay()
    {
        Assert.IsTrue(FirstShiftGuideState.Eligible(KehaiEdition.Gameplay, 1, false, false, false));
        Assert.IsTrue(FirstShiftGuideState.Eligible(KehaiEdition.Testing, 1, false, false, false));
        Assert.IsFalse(FirstShiftGuideState.Eligible(KehaiEdition.Training, 1, false, false, false));
        Assert.IsFalse(FirstShiftGuideState.Eligible(KehaiEdition.Testing, 2, false, false, false));
        Assert.IsFalse(FirstShiftGuideState.Eligible(KehaiEdition.Testing, 1, true, false, false));
        Assert.IsFalse(FirstShiftGuideState.Eligible(KehaiEdition.Testing, 1, false, true, false));
        Assert.IsFalse(FirstShiftGuideState.Eligible(KehaiEdition.Testing, 1, false, false, true));
    }
    [Test] public void ArrivalIsSequentialAndAutomaticDoorsHaveNoUsePrompt()
    {
        var s = new FirstShiftGuideState(); Assert.AreEqual("", FirstShiftGuideState.Prompt(s.Step));
        s.Observe(GuideSignal.EnteredStore); s.Tick(0f); Assert.AreEqual(GuideStep.Lobby, s.Step);
        s.Observe(GuideSignal.SawStaffDoor); s.Tick(0f); Assert.AreEqual(GuideStep.Door, s.Step);
        Assert.AreEqual("E to open doors", FirstShiftGuideState.Prompt(s.Step));
        s.Observe(GuideSignal.OpenedDoor); s.Tick(0f); Assert.AreEqual(GuideStep.ClockIn, s.Step);
        s.Observe(GuideSignal.ClockedIn); s.Tick(0f); Assert.AreEqual(GuideStep.TaskList, s.Step);
    }
    [Test] public void APlayerWhoAlreadyClockedInCannotGetStuckAtTheDoor()
    {
        var s = new FirstShiftGuideState(); s.Observe(GuideSignal.ClockedIn); s.Tick(0f);
        Assert.AreEqual(GuideStep.TaskList, s.Step);
    }
    [Test] public void ThoughtsLastFourSecondsAndRepeatOnlyOnceAtThirty()
    {
        var s = new FirstShiftGuideState(); Assert.IsTrue(s.ThoughtVisible);
        s.Tick(4f); Assert.IsFalse(s.ThoughtVisible);
        s.Tick(26f); Assert.IsTrue(s.ThoughtVisible);
        s.Tick(4f); Assert.IsFalse(s.ThoughtVisible);
        s.Tick(26f); Assert.IsFalse(s.ThoughtVisible);
    }
    [Test] public void CompletedActionsAreRememberedWhileAnotherLessonRuns()
    {
        var s = Working(); s.Observe(GuideSignal.NeedsStock); s.Tick(0f);
        Assert.AreEqual(GuideStep.Crate, s.Step);
        s.Observe(GuideSignal.Stocked); s.Tick(0f); Assert.AreEqual(GuideStep.Stock, s.Step);
        s.Tick(0f); Assert.AreEqual(GuideStep.OneDown, s.Step);
        s.Tick(4f); s.Tick(0f); Assert.AreEqual(GuideStep.Waiting, s.Step);
    }
    [Test] public void JobsCanArriveInEitherOrderButNeverOverlap()
    {
        var s = Working(); s.Observe(GuideSignal.TillWaiting); s.Tick(0f);
        Assert.AreEqual(GuideStep.Till, s.Step);
        s.Observe(GuideSignal.NeedsStock); s.Tick(1f); Assert.AreEqual(GuideStep.Till, s.Step);
        s.Observe(GuideSignal.Served); s.Tick(0f); s.Tick(0f); Assert.AreEqual(GuideStep.Crate, s.Step);
    }
    [Test] public void MopReturnTimesOutAfterSixtySeconds()
    {
        var s = Working(); s.Observe(GuideSignal.NeedsMop); s.Tick(0f);
        s.Observe(GuideSignal.MopHeld); s.Tick(0f);
        s.Observe(GuideSignal.Cleaned); s.Tick(0f); Assert.AreEqual(GuideStep.MopRack, s.Step);
        s.Tick(59f); Assert.AreEqual(GuideStep.MopRack, s.Step);
        s.Tick(1f); Assert.AreEqual(GuideStep.Waiting, s.Step);
    }
    [Test] public void MissingOptionalJobsDoNotBlockClosing()
    {
        var s = Working(); s.Observe(GuideSignal.NeedsStock); s.Tick(0f);
        s.Observe(GuideSignal.ClosingTime); s.Tick(0f); Assert.AreEqual(GuideStep.Closing, s.Step);
        s.Observe(GuideSignal.ClosingSaid); s.Tick(0f); Assert.AreEqual(GuideStep.ClockOut, s.Step);
        s.Observe(GuideSignal.ClockedOut); s.Tick(0f); Assert.AreEqual(GuideStep.Done, s.Step);
        s.Tick(4f); Assert.IsFalse(s.Running);
    }
    [Test] public void EarlyClockOutDoesNotLeaveTheGuideWaitingForClosingTime()
    {
        var s = Working(); s.Observe(GuideSignal.ClockedOut); s.Tick(0f);
        Assert.AreEqual(GuideStep.Done, s.Step); s.Tick(4f); Assert.IsFalse(s.Running);
    }
    [Test] public void SkipIsTerminalAndDoesNotOfferHelpReplay()
    {
        var s = Working(); s.Skip(); s.Observe(GuideSignal.ClosingTime); s.Tick(100f);
        Assert.IsTrue(s.Skipped); Assert.IsFalse(s.Running); Assert.IsFalse(s.ThoughtVisible);
        Assert.AreEqual(GuideStep.Finished, s.Step);
    }
    [Test] public void PowerLessonFinishesOnRestoration()
    {
        var s = Working(); s.Observe(GuideSignal.PowerLost); s.Tick(0f); Assert.AreEqual(GuideStep.Power, s.Step);
        Assert.AreEqual("Hold E to reset the breakers (20 s)", FirstShiftGuideState.Prompt(s.Step));
        s.Observe(GuideSignal.PowerRestored); s.Tick(0f); Assert.AreEqual(GuideStep.Waiting, s.Step);
    }
}
