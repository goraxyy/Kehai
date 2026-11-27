using System.Collections.Generic;
using System.IO;
using Kehai.Eval;
using NUnit.Framework;

// The ablation's jsonl (EpisodeLog in AblationRunner.cs): a shift that never started is never
// written, and neither is a second row for a (rung, player, seed, shift) already there. On
// 2026-10-15 a failed clock-in between shifts logged 9 such copies in a 36-shift run.
public class EpisodeLogTests
{
    string path;
    readonly List<EpisodeMetrics> begun = new List<EpisodeMetrics>();

    [SetUp]
    public void SetUp() => path = Path.Combine(Path.GetTempPath(), $"episode_log_test_{System.Guid.NewGuid():N}.jsonl");

    [TearDown]
    public void TearDown()
    {
        foreach (EpisodeMetrics m in begun) m.Unsubscribe();
        begun.Clear();
        if (File.Exists(path)) File.Delete(path);
    }

    EpisodeMetrics Started(string rung, int shift, int seed = 1000, string agent = "efficient")
    {
        var m = new EpisodeMetrics();
        m.Begin(seed, shift, agent, rung);
        begun.Add(m);
        return m;
    }

    string[] Lines() => File.Exists(path) ? File.ReadAllLines(path) : new string[0];

    [Test]
    public void AShiftThatNeverStarted_IsNotWritten()
    {
        var log = new EpisodeLog(path);
        var ghost = new EpisodeMetrics { Seed = 1000, Shift = 4, Agent = "efficient", Rung = "A" };

        Assert.IsFalse(ghost.Started);
        Assert.IsFalse(log.TryAppend(ghost, out string refusal));
        StringAssert.Contains("never started", refusal);
        Assert.AreEqual(0, Lines().Length);
        Assert.AreEqual(0, log.Count);
    }

    [Test]
    public void AStartedShift_IsWrittenAsOneLine()
    {
        var log = new EpisodeLog(path);
        Assert.IsTrue(log.TryAppend(Started("C", 4), out string refusal), refusal);

        string[] lines = Lines();
        Assert.AreEqual(1, lines.Length);
        StringAssert.Contains("\"shift\":4", lines[0]);
        StringAssert.Contains("\"rung\":\"C\"", lines[0]);
    }

    [Test]
    public void ACopyOfARecordedShift_IsNotWritten_ButTheNextShiftAndOtherRungsAre()
    {
        var log = new EpisodeLog(path);
        Assert.IsTrue(log.TryAppend(Started("A", 4), out _));

        // What the old runner logged after a failed clock-in: the same shift number again.
        Assert.IsFalse(log.TryAppend(Started("A", 4), out string refusal));
        StringAssert.Contains("already recorded", refusal);

        Assert.IsTrue(log.TryAppend(Started("A", 5), out _), "the career's next shift");
        Assert.IsTrue(log.TryAppend(Started("B", 4), out _), "the same shift at another rung is its pair, not a copy");
        Assert.IsTrue(log.TryAppend(Started("A", 4, seed: 1010, agent: "skittish"), out _), "another career");
        Assert.AreEqual(4, Lines().Length);
        Assert.AreEqual(4, log.Count);
    }

    // C5.1: a stopped run resumed keeps its finished careers' rows once, drops a half-finished
    // career's rows (that career is played again from its start), and never writes a kept row twice.
    [Test]
    public void AResumedRun_KeepsFinishedCareersOnce_AndDropsAHalfFinishedOne()
    {
        var log = new EpisodeLog(path);
        Assert.IsTrue(log.TryAppend(Started("E", 3, seed: 1000), out _));
        Assert.IsTrue(log.TryAppend(Started("E", 4, seed: 1000), out _));
        File.AppendAllText(AblationResume.CareersPath(path), AblationResume.Key(1000, "E") + "\n");
        Assert.IsTrue(log.TryAppend(Started("E", 3, seed: 1100), out _), "the next career, stopped after one shift");
        Assert.AreEqual(3, Lines().Length);

        var finished = AblationResume.Finished(path);
        CollectionAssert.AreEquivalent(new[] { "1000|E" }, finished);
        var resumed = new EpisodeLog(path);
        foreach (string key in AblationResume.KeepFinished(path, finished)) resumed.Remember(key);
        Assert.AreEqual(2, Lines().Length, "the half-finished career's row is gone");
        Assert.AreEqual(2, resumed.Count);

        Assert.IsFalse(resumed.TryAppend(Started("E", 4, seed: 1000), out string refusal), "a kept row is never written twice");
        StringAssert.Contains("already recorded", refusal);
        Assert.IsTrue(resumed.TryAppend(Started("E", 3, seed: 1100), out _), "the half-finished career, played again");
        Assert.AreEqual(3, Lines().Length);
        File.Delete(AblationResume.CareersPath(path));
    }

    // The owner's call (26 November): after a catch an ablation career moves on to the next shift.
    // The shift she caught you in is counted as done only when it wasn't completed and is still
    // the career's newest; a completed shift, or a career just placed, is left as it is.
    [Test]
    public void AfterACatch_TheCareerMovesOnToTheNextShift()
    {
        Assert.IsTrue(KehaiEnv.CaughtShiftToSkip(lastCompleted: false, shiftNumber: 3, completedShifts: 2), "caught in shift 3");
        Assert.IsFalse(KehaiEnv.CaughtShiftToSkip(lastCompleted: true, shiftNumber: 3, completedShifts: 3), "shift 3 was completed");
        Assert.IsFalse(KehaiEnv.CaughtShiftToSkip(lastCompleted: false, shiftNumber: 2, completedShifts: 2), "a career placed at shift 3, not played yet");
    }

    [Test]
    public void ANewRun_HasNoFinishedCareers()
    {
        CollectionAssert.IsEmpty(AblationResume.Finished(path));
        CollectionAssert.IsEmpty(AblationResume.KeepFinished(path, new HashSet<string>()));
    }
}
