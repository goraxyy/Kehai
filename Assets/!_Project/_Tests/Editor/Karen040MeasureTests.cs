using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kehai;
using Kehai.Eval;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// 0.4.0, phase 1 (build plan §12): learning records, catch causes, -karen-set, the seeds and who is
// held out, and what the player could sense of her.
public class Karen040MeasureTests
{
    static string AiRoot => Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts");

    // ---- C1.1 learning records -------------------------------------------------------------------

    static LearningRecord Record(float now, string pick = "check:look") => new LearningRecord
    {
        Shift = 3, Seed = 42, Player = "Employee #1", Rung = "E_Ledger", T = now, Now = now, Goal = "Check",
        InputsVersion = PolicyObservation.Version, Inputs = new[] { 0.1f, 0.2f }, Legal = new[] { "check:look", "check:peek" },
        Pick = pick, Chance = 1f, Panic = 0.3f
    };

    [Test]
    public void LearningRecords_OneLinePerDecision_WithItsWindows()
    {
        string path = Path.Combine(Path.GetTempPath(), "kehai_learning_test.jsonl");
        if (File.Exists(path)) File.Delete(path);
        var rec = new LearningRecorder { Enabled = true, FilePath = path };
        rec.BeginShift();
        rec.Add(Record(0f));
        rec.Add(Record(5f, "check:peek"));
        rec.Tick(10f, 0.5f);     // the first's 10 s window
        rec.HintFired(12f);      // inside both records' 20 s
        rec.Tick(20f, 0.6f);     // the first's 20 s window
        rec.Caught(28f);
        rec.Tick(30f, 0.6f);     // the first is settled and written
        rec.EndShift();          // the second is written with what's known

        string[] lines = File.ReadAllLines(path);
        File.Delete(path);
        Assert.AreEqual(2, lines.Length, "one line per decision");
        Dictionary<string, object> first = MiniJson.ParseObject(lines[0]);
        Assert.AreEqual("check:look", first.GetString("pick"));
        Assert.AreEqual(0.2, first.GetNumber("d_panic_10"), 1e-4);
        Assert.AreEqual(0.3, first.GetNumber("d_panic_20"), 1e-4);
        Assert.IsTrue(first.GetBool("caught_30"));
        Assert.IsTrue(first.GetBool("hint"), "the Director's hint fired inside the window");
        Assert.AreEqual(2, ((List<object>)first["legal"]).Count);
        Assert.AreEqual(PolicyObservation.Version, (int)first.GetNumber("inputs_version"));
        Dictionary<string, object> second = MiniJson.ParseObject(lines[1]);
        Assert.IsTrue(second.GetBool("caught_30"));
        Assert.AreEqual(0.3, second.GetNumber("d_panic_10"), 1e-4);
    }

    [Test]
    public void LearningRecords_Disabled_WriteNothing()
    {
        var rec = new LearningRecorder { Enabled = false };
        rec.Add(Record(0f));
        rec.Tick(40f, 0f);
        rec.EndShift();
        Assert.AreEqual(0, rec.Written.Count);
    }

    // Her inputs on record are the brain's own (PolicyObservation, under Decision/), and the
    // record's file sits under the source scan: only what she may know.
    [Test]
    public void LearningRecords_InputsAreOnlyWhatSheMayKnow()
    {
        string brain = File.ReadAllText(Path.Combine(AiRoot, "Core/KarenBrain.cs"));
        string record = Regex.Match(brain, @"void RecordDecision\(.*?\n        }", RegexOptions.Singleline).Value;
        Assert.IsNotEmpty(record);
        StringAssert.Contains("BrainObservation.Build(", record, "the record's inputs are the brain's observation (version 2, under Decision/)");
        StringAssert.DoesNotContain("TruePlayerPosition", record);
        StringAssert.DoesNotContain("PlayerPresence", record);
        Assert.IsTrue(Karen040Files.UnderTheRule(AiRoot).Any(f => f.EndsWith("LearningRecord.cs")), "the source scan covers the record");
    }

    // ---- C1.2 catch causes, -karen-set, time to catch ------------------------------------------

    [Test]
    public void KarenSet_SetsAnyField_AndRefusesTypos()
    {
        var config = new KarenConfig();
        Assert.IsTrue(KarenConfigSet.Apply(config, "huntMemorySeconds=12.5", out _));
        Assert.AreEqual(12.5f, config.huntMemorySeconds, 1e-5f);
        Assert.IsTrue(KarenConfigSet.Apply(config, "shield=off", out _));
        Assert.IsFalse(config.shield);
        Assert.IsTrue(KarenConfigSet.Apply(config, "brain=vital", out _));
        Assert.AreEqual(KarenBrainKind.Vital, config.brain);
        Assert.IsTrue(KarenConfigSet.Apply(config, "rung=C", out _), "a rung by its letter");
        Assert.AreEqual(KarenRung.C_BeliefGrid, config.rung);
        Assert.IsFalse(KarenConfigSet.Apply(config, "huntMemorySecs=3", out string why));
        StringAssert.Contains("no field", why);
        Assert.IsFalse(KarenConfigSet.Apply(config, "shield=perhaps", out _));
        Assert.IsTrue(config.shield == false, "a refused value changes nothing");
    }

    [Test]
    public void TheNewSwitches_Parse()
    {
        Assert.IsTrue(KarenBootstrap.TryParseBrain("Vital", out KarenBrainKind b) && b == KarenBrainKind.Vital);
        Assert.IsTrue(KarenBootstrap.TryParseBrain("f", out b) && b == KarenBrainKind.Basic, "rung F now means Basic");
        Assert.IsTrue(KarenBootstrap.TryParseBrain("planner", out b) && b == KarenBrainKind.Planner);
        Assert.IsFalse(KarenBootstrap.TryParseBrain("chess", out _));
        Assert.IsTrue(KarenBootstrap.TryParseMirror("K2", out MirrorMode m) && m == MirrorMode.K2);
        var c = new KarenConfig();
        Assert.AreEqual(KarenBrainKind.Planner, c.brain, "today's game by default");
        Assert.AreEqual(MirrorMode.K0, c.mirror);
        Assert.AreEqual("s0", c.senses);
        Assert.AreEqual("p0", c.motion);
        Assert.IsFalse(c.hunter);
    }

    [Test]
    public void KaplanMeier_CountsShiftsWithNoCatchAsNotYet()
    {
        var samples = new[] { (30f, true), (60f, true), (150f, false), (150f, false) };
        var curve = KaplanMeier.Curve(samples);
        Assert.AreEqual(2, curve.Count);
        Assert.AreEqual(0.75f, curve[0].survival, 1e-5f);
        Assert.AreEqual(0.5f, curve[1].survival, 1e-5f);
        Assert.AreEqual(60f, KaplanMeier.Median(samples), 1e-5f);
        Assert.IsNaN(KaplanMeier.Median(new[] { (30f, true), (150f, false), (150f, false) }));
    }

    [Test]
    public void CatchCause_TheClockCampGuardCountsWithin10m()
    {
        Assert.IsTrue(new CatchCause { ClockDistance = 9.9f }.AtClock);
        Assert.IsFalse(new CatchCause { ClockDistance = 10.1f }.AtClock);
        Assert.IsFalse(new CatchCause { ClockDistance = -1f }.AtClock, "no clock, no camp");
        Dictionary<string, object> d = new CatchCause { Action = "Hunt", Tactic = "chase", SeenWithin3s = true, ClockDistance = 4f }.ToDictionary();
        Assert.AreEqual("Hunt", d["action"]);
        Assert.AreEqual(true, d["seen_3s"]);
    }

    // ---- C1.5 seeds and who is held out ----------------------------------------------------------

    [Test]
    public void Splits_SeedRangesDoNotOverlap()
    {
        Assert.AreEqual(Split.Training, EvalSplits.Of(EvalSplits.SeedFor(Split.Training, 0)));
        Assert.AreEqual(Split.Training, EvalSplits.Of(EvalSplits.SeedFor(Split.Training, EvalSplits.Span - 1)));
        Assert.AreEqual(Split.Validation, EvalSplits.Of(20000));
        Assert.AreEqual(Split.Test, EvalSplits.Of(30000));
        Assert.AreEqual(Split.Legacy, EvalSplits.Of(1000), "the old ablations' seeds are their own");
        Assert.Throws<System.ArgumentOutOfRangeException>(() => EvalSplits.SeedFor(Split.Training, EvalSplits.Span), "training can't run into validation");
    }

    [Test]
    public void Splits_TrainingNeverDrawsAHeldOutSeedOrPersona()
    {
        var random = new System.Random(5);
        for (int i = 0; i < 5000; i++)
        {
            Persona p = EvalSplits.DrawForKaren(random, PersonaRanges.Everything);
            Assert.IsFalse(EvalSplits.InCarefulCorner(p), "drew a careful-corner persona for Karen's training: " + p);
            int seed = EvalSplits.SeedFor(Split.Training, random.Next(EvalSplits.Span));
            Assert.IsTrue(EvalSplits.KarenMayTrainOn(seed, p));
        }
        Persona cautious = Persona.Of(PlayerProfile.Cautious);
        Assert.IsTrue(EvalSplits.InCarefulCorner(cautious), "the cautious profile lives in the careful corner");
        Assert.IsFalse(EvalSplits.KarenMayTrainOn(10001, cautious));
        Assert.IsFalse(EvalSplits.PlayersMayTrainOn(10001, cautious), "the cautious profile is held out of everything");
        Persona corner = EvalSplits.DrawCorner(random);
        Assert.IsTrue(EvalSplits.InCarefulCorner(corner));
        Assert.IsFalse(EvalSplits.KarenMayTrainOn(10001, corner));
        Assert.IsTrue(EvalSplits.PlayersMayTrainOn(10001, corner), "the bots' own network trains on the corner");
        Assert.IsFalse(EvalSplits.KarenMayTrainOn(30001, Persona.Of(PlayerProfile.Efficient)), "never a test seed");
        Assert.IsFalse(EvalSplits.KarenMayTrainOn(20001, Persona.Of(PlayerProfile.Efficient)), "validation seeds aren't training's");
        Assert.IsTrue(EvalSplits.KarenMayTrainOn(20001, Persona.Of(PlayerProfile.Efficient), Split.Validation));
        Assert.Throws<System.InvalidOperationException>(() => EvalSplits.AssertKarenTraining(10001, cautious));
        Assert.DoesNotThrow(() => EvalSplits.AssertKarenTraining(10001, Persona.Of(PlayerProfile.Reckless)));
    }

    // ---- C1.6 what you could sense of her ---------------------------------------------------------

    [Test]
    public void PlayerSenses_FromABot_AreItsOwnObservation()
    {
        PlayerSenses.FromBot(true, false);
        Assert.IsTrue(PlayerSenses.BotPlaying);
        PlayerSenses.Sense(null, null, out bool saw, out bool heard);
        Assert.IsTrue(saw);
        Assert.IsFalse(heard);
    }

    [Test]
    public void PlayerSenses_ARecordedSampleCarriesBothFlags()
    {
        var rec = new ShiftRecording { ShiftNumber = 1 };
        rec.Frames.Add(new ShiftFrame { T = 0f, PlayerSawKaren = true, PlayerHeardKaren = false });
        rec.Frames.Add(new ShiftFrame { T = 0.2f, PlayerSawKaren = false, PlayerHeardKaren = true });
        var d = MiniJson.ParseObject(rec.ToJson(null, ShiftAnalysis.Of(rec)));
        var frames = (List<object>)d["frames"];
        foreach (object f in frames)
        {
            var row = (List<object>)f;
            Assert.AreEqual(27, row.Count, "18 numbers, 7 lists, then saw and heard");
        }
        Assert.AreEqual(1.0, System.Convert.ToDouble(((List<object>)frames[0])[25]));
        Assert.AreEqual(1.0, System.Convert.ToDouble(((List<object>)frames[1])[26]));
    }

    // She doesn't know what you saw: nothing under Belief/, Perception/ or Decision/ reads it.
    [Test]
    public void PlayerSenses_AreNeverReadByHerMind()
    {
        var reads = new Regex(@"\b(PlayerSenses|PlayerSawKaren|PlayerHeardKaren)\b");
        var leaks = new List<string>();
        foreach (string dir in new[] { "Belief", "Perception", "Decision" })
            foreach (string file in Directory.GetFiles(Path.Combine(AiRoot, dir), "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (reads.IsMatch(lines[i].Split(new[] { "//" }, System.StringSplitOptions.None)[0]))
                        leaks.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        Assert.IsEmpty(leaks, "her mind read what you sensed of her:\n" + string.Join("\n", leaks));
    }
}
