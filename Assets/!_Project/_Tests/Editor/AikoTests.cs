using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kehai;
using Kehai.Aiko;
using Kehai.Eval;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// The rules in Aiko.md that can be checked without playing: that Aiko is blind to the
// player except through her senses, that every tactic is telegraphed and leaves the player
// something to do, that learning is reversible, that the pacing gates hold, and that the
// harness's JSON survives a round trip.
//
// Run from Window → General → Test Runner (EditMode), or headless:
//   Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
public class AikoRuleTests
{
    static string AiRoot => Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts");

    // ---- §2: she does not know where you are ------------------------------------------------

    // The Director is allowed the truth (pacing, the bounded search bias, the replay's
    // "where they really were"); the body, the belief and the decision layer are not.
    // (The audio listener rides on the player's camera, so it counts too.)
    static readonly Regex Omniscience = new Regex(@"\b(PlayerPresence|PlayerMotor|FindGameObjectWithTag|Camera\.main|TruePlayerPosition|CharacterController|AudioListener|ListenerPosition)\b");

    static readonly string[] AllowedLines =
    {
        "PlayerPosition = AikoDirector.TruePlayerPosition",   // the replay record, labelled debug-only
        "if (other is CharacterController) Touched?.Invoke(other);",   // touch is a sense: being caught is contact
        "hit.collider.GetComponent<CharacterController>() == null) continue;",   // vantage: a body in the way isn't a wall
    };

    [Test]
    public void BodyBeliefAndDecisions_NeverReadThePlayer()
    {
        var files = new List<string> { Path.Combine(AiRoot, "Core/AikoBody.cs"), Path.Combine(AiRoot, "Core/AikoBrain.cs") };
        files.AddRange(Directory.GetFiles(Path.Combine(AiRoot, "Belief"), "*.cs", SearchOption.AllDirectories));
        files.AddRange(Directory.GetFiles(Path.Combine(AiRoot, "Decision"), "*.cs", SearchOption.AllDirectories));

        var leaks = new List<string>();
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split(new[] { "//" }, System.StringSplitOptions.None)[0];
                if (!Omniscience.IsMatch(code)) continue;
                if (AllowedLines.Any(a => code.Contains(a))) continue;
                leaks.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.IsEmpty(leaks, GameNames.Antagonist + " read the player directly:\n" + string.Join("\n", leaks));
    }

    // ---- her speed: fast, but a sprint always gets away -------------------------------------

    [Test]
    public void Aiko_NeverOutrunsASprintingEmployee()
    {
        var motor = new GameObject("player").AddComponent<PlayerMotor>();
        try
        {
            var config = new AikoConfig();
            foreach (float pace in new[] { config.sneakSpeed, config.walkSpeed, config.hurrySpeed, config.runSpeed })
                Assert.Less(pace, motor.sprintSpeed, "a default pace is faster than the default sprint");
            Assert.Greater(config.hurrySpeed, motor.walkSpeed, "hurrying, she should outpace a walking employee");

            // A scene with a slower sprint pulls her paces down with it.
            AikoBootstrap.KeepBelowSprint(config, 5f);
            foreach (float pace in new[] { config.sneakSpeed, config.walkSpeed, config.hurrySpeed, config.runSpeed })
                Assert.Less(pace, 5f);
        }
        finally { Object.DestroyImmediate(motor.gameObject); }
    }

    // ---- §9: every threat is telegraphed and answerable --------------------------------------

    [Test]
    public void EveryTactic_HasATellOfAtLeast800ms_AndAChore()
    {
        Assert.That(TacticLibrary.All.Count, Is.GreaterThanOrEqualTo(30));
        foreach (Tactic t in TacticLibrary.All)
        {
            if (t.Tier > 0) Assert.GreaterOrEqual(t.TellLead, 0.8f, $"{t.Id}: tell {t.TellLead}s is under the 0.8 s floor");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Chore), $"{t.Id} leaves the player nothing to do");
            Assert.IsFalse(string.IsNullOrWhiteSpace(t.Attacks), $"{t.Id} doesn't say what it attacks");
            Assert.That(t.Tier, Is.InRange(0, 4), t.Id);
            Assert.GreaterOrEqual(t.Cooldown, 0f, t.Id);
        }
    }

    [Test]
    public void TacticIds_AreUnique_AndResolvable()
    {
        var ids = TacticLibrary.All.Select(t => t.Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), "duplicate tactic ids");
        foreach (string id in ids) Assert.AreSame(TacticLibrary.All.First(t => t.Id == id), TacticLibrary.Get(id));
    }

    [Test]
    public void EveryTier_IsPopulated()
    {
        for (int tier = 0; tier <= 4; tier++)
            Assert.IsTrue(TacticLibrary.All.Any(t => t.Tier == tier), $"no tactic at tier {tier}");
    }

    // ---- §7: the bandit learns, habituates, and forgets -----------------------------------------

    static AikoContext Context(AikoRung rung, out AikoConfig config)
    {
        config = new AikoConfig { rung = rung, seed = 7 };
        return new AikoContext { Config = config, Rng = new AikoRng(7) };
    }

    [Test]
    public void Bandit_LearnsFromReward()
    {
        AikoContext c = Context(AikoRung.D_Bandit, out AikoConfig config);
        var ledger = new AikoLedger(config);
        Tactic t = TacticLibrary.All.First(x => x.Tier == 1);

        for (int i = 0; i < 12; i++) ledger.Reward(t.Id, -0.3f);
        Assert.Less(ledger.ExpectedPanicDelta(t, c), -0.2f, "Q̂ didn't move toward the observed deltas");

        // Below rung D, the prior stands regardless of what was observed.
        AikoContext cc = Context(AikoRung.C_BeliefGrid, out _);
        Assert.AreEqual(t.PanicPrior, ledger.ExpectedPanicDelta(t, cc), 1e-5f);
    }

    [Test]
    public void Bandit_HabituatesToWhatItJustUsed()
    {
        AikoContext c = Context(AikoRung.D_Bandit, out AikoConfig config);
        var ledger = new AikoLedger(config);
        Tactic t = TacticLibrary.All.First(x => x.Tier == 2);
        ledger.Reward(t.Id, 0.2f);

        ledger.MarkUsed(t, c.Now - 10000f);
        float rested = ledger.BanditScore(t, c, out _);
        ledger.MarkUsed(t, c.Now);
        float fresh = ledger.BanditScore(t, c, out _);
        Assert.Greater(rested - fresh, config.habituationLambda * 0.9f, "using a tactic didn't make it less attractive");
    }

    [Test]
    public void Bandit_ExploresArmsItHasNotTried()
    {
        AikoContext c = Context(AikoRung.D_Bandit, out AikoConfig config);
        var ledger = new AikoLedger(config);
        var tier2 = TacticLibrary.All.Where(x => x.Tier == 2).Take(2).ToArray();
        Tactic tried = tier2[0], untried = tier2[1];
        for (int i = 0; i < 20; i++) ledger.Reward(tried.Id, untried.PanicPrior);

        ledger.BanditScore(tried, c, out string whyTried);
        ledger.BanditScore(untried, c, out string whyUntried);
        float bonusTried = float.Parse(Regex.Match(whyTried, @"ucb\+([0-9.]+)").Groups[1].Value);
        float bonusUntried = float.Parse(Regex.Match(whyUntried, @"ucb\+([0-9.]+)").Groups[1].Value);
        Assert.Greater(bonusUntried, bonusTried);
    }

    [Test]
    public void Ledger_ForgetsWithinTwoShifts()
    {
        Context(AikoRung.E_Ledger, out AikoConfig config);
        // A persistent ledger, but in a temp file: a test must never touch the player's own.
        config.ledgerPath = Path.Combine(Path.GetTempPath(), "aiko_ledger_test_" + System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var ledger = new AikoLedger(config) { Persistent = true };
            Assert.AreNotEqual(AikoLedger.DefaultPath, ledger.SavePath);
            Tactic t = TacticLibrary.All.First(x => x.Tier == 3);
            ledger.BeginShift(1);
            for (int i = 0; i < 10; i++) ledger.Reward(t.Id, t.PanicPrior + 0.5f);
            ledger.Data.routes.Add(new RouteStat { destination = "spill", path = "a|b|c", count = 1f, lastShift = 1 });
            ArmStat arm = ledger.Arms.First(a => a.id == t.Id);
            float n0 = arm.n, gap0 = arm.q - t.PanicPrior;
            ledger.EndShift(300f, 0.5f, true);

            // Two shifts in which the player does something else.
            for (int shift = 2; shift <= 3; shift++)
            {
                ledger.BeginShift(shift);
                ledger.EndShift(300f, 0.5f, true);
            }

            Assert.IsTrue(ledger.Data.routes.All(r => r.count < 0.4f), "a route unused for two shifts still dominates");
            Assert.Less(arm.n, n0 * 0.6f, "the bandit's confidence didn't fade");
            Assert.Less(arm.q - t.PanicPrior, gap0 * 0.7f, "what it learnt didn't drift back toward the prior");
            Assert.IsTrue(File.Exists(config.ledgerPath), "the ledger wasn't saved where it was told to be");
        }
        finally
        {
            if (File.Exists(config.ledgerPath)) File.Delete(config.ledgerPath);
        }
    }

    // ---- §9: the pacing gates -------------------------------------------------------------------

    [Test]
    public void Director_GatesBigTacticsOffShiftAndWhileSettling()
    {
        AikoContext c = Context(AikoRung.F_Blink, out AikoConfig config);
        var director = new AikoDirector(config, new AikoLedger(config));
        Assert.IsTrue(director.PermitsTier(0, c), "movement is always allowed");
        Assert.IsFalse(director.PermitsTier(1, c), "tactics before the shift starts");

        director.BeginShift(3, 300f);
        Assert.AreEqual(AikoDirector.Phase.Settle, director.CurrentPhase);
        Assert.IsTrue(director.PermitsTier(1, c));
        for (int tier = 2; tier <= 4; tier++) Assert.IsFalse(director.PermitsTier(tier, c), $"tier {tier} while settling");
    }

    // ---- §9.8: seeded determinism --------------------------------------------------------------

    [Test]
    public void Rng_IsDeterministicPerSeed()
    {
        var a = new AikoRng(1234);
        var b = new AikoRng(1234);
        var d = new AikoRng(4321);
        var sa = Enumerable.Range(0, 50).Select(_ => a.Value).ToArray();
        var sb = Enumerable.Range(0, 50).Select(_ => b.Value).ToArray();
        var sd = Enumerable.Range(0, 50).Select(_ => d.Value).ToArray();
        CollectionAssert.AreEqual(sa, sb);
        CollectionAssert.AreNotEqual(sa, sd);
    }

    // ---- the thought log and the harness speak JSON --------------------------------------------

    [Test]
    public void ThoughtRecord_SerialisesToParsableJson()
    {
        var record = new ThoughtRecord
        {
            T = 12.5f, Shift = 3, Kind = "GOAL", Text = "GOAL     sweep \"aisle 2\" — confidence 0.41",
            Options = new List<ThoughtOption> { new ThoughtOption { Name = "sweep", Utility = 0.62f, Why = "stale 40s" } },
            Chose = "sweep", Because = "entropy high", Panic = 0.3f, Target = 0.45f
        };
        var parsed = MiniJson.ParseObject(MiniJson.Serialize(record));
        Assert.IsNotNull(parsed);
        Assert.AreEqual("GOAL", parsed.GetString("kind"));
        Assert.AreEqual(3, (int)parsed.GetNumber("shift"));
        StringAssert.Contains("\"aisle 2\"", parsed.GetString("text"));
    }

    [Test]
    public void MiniJson_RoundTripsNestedValues()
    {
        var value = new Dictionary<string, object>
        {
            ["a"] = 1.5f, ["b"] = new List<object> { 1, "two", true, null, new Dictionary<string, object> { ["c"] = "é\n\"q\"" } },
            ["d"] = new List<string> { "x", "y" }, ["e"] = false
        };
        var back = MiniJson.ParseObject(MiniJson.Serialize(value));
        Assert.AreEqual(1.5, back.GetNumber("a"), 1e-6);
        var list = (List<object>)back["b"];
        Assert.AreEqual(5, list.Count);
        Assert.AreEqual("two", list[1]);
        Assert.AreEqual(true, list[2]);
        Assert.IsNull(list[3]);
        Assert.AreEqual("é\n\"q\"", ((Dictionary<string, object>)list[4]).GetString("c"));
        Assert.AreEqual(false, back.GetBool("e", true));
    }

    [Test]
    public void EnvConfigAndAction_ParseFromTheWireFormat()
    {
        var config = EnvConfig.From(MiniJson.ParseObject("{\"seed\":9,\"rung\":\"C\",\"shift_seconds\":120,\"fps\":500,\"render\":false,\"ledger_path\":\"/tmp/x.json\"}"));
        Assert.AreEqual(9, config.seed);
        Assert.AreEqual("C", config.rung);
        Assert.AreEqual(120f, config.shiftSeconds);
        Assert.AreEqual(120, config.fps, "fps is clamped");
        Assert.IsFalse(config.render);
        Assert.AreEqual("/tmp/x.json", config.ledgerPath);

        EnvAction a = EnvAction.From(MiniJson.ParseObject("{\"verb\":\"mop\",\"target\":\"spill_2\",\"sprint\":true}"));
        Assert.AreEqual("mop", a.verb);
        Assert.AreEqual("spill_2", a.target);
        Assert.IsTrue(a.sprint);
        Assert.IsTrue(a.accept, "help defaults to accepting");
    }

    [Test]
    public void Rungs_ParseFromLetters()
    {
        Assert.IsTrue(AikoBootstrap.TryParseRung("a", out AikoRung r) && r == AikoRung.A_RandomPatrol);
        Assert.IsTrue(AikoBootstrap.TryParseRung("F", out r) && r == AikoRung.F_Blink);
        Assert.IsFalse(new RungFeatures(AikoRung.C_BeliefGrid).Bandit);
        Assert.IsTrue(new RungFeatures(AikoRung.D_Bandit).Bandit);
        Assert.IsFalse(new RungFeatures(AikoRung.D_Bandit).Persistent);
        Assert.IsTrue(new RungFeatures(AikoRung.E_Ledger).Persistent);
        Assert.IsTrue(new RungFeatures(AikoRung.F_Blink).Blink);
    }

    // ---- the blink sidecar's packets --------------------------------------------------------------

    [Test]
    public void UdpBlinkSource_ReadsTheSidecarPacketFormat()
    {
        const int port = 5099;
        using (var source = new Kehai.Blink.UdpBlinkSource(port))
        using (var udp = new System.Net.Sockets.UdpClient())
        {
            // Exactly what tools/blink/blink_server.py sends.
            byte[] packet = System.Text.Encoding.UTF8.GetBytes(
                "{\"seq\": 1, \"closed\": 0.93, \"conf\": 0.88, \"src\": \"ear\", \"capture\": 1000.0, \"sent\": 1000.016, \"fps\": 30.0}");
            udp.Send(packet, packet.Length, "127.0.0.1", port);

            // Live as soon as packets arrive, before anything reads one: the tracker only
            // switches to the webcam once it's live, so waiting for a read would wait forever.
            for (int i = 0; i < 200 && source.Packets == 0; i++) System.Threading.Thread.Sleep(5);
            Assert.IsTrue(source.IsLive, "the webcam doesn't count as live until something reads it");

            Kehai.Blink.BlinkSample sample = default;
            bool got = false;
            for (int i = 0; i < 200 && !got; i++)
            {
                System.Threading.Thread.Sleep(5);
                got = source.TryRead(out sample);
            }
            Assert.IsTrue(got, "no packet arrived on the loopback port");
            Assert.AreEqual(0.93f, sample.Closed, 1e-3f);
            Assert.AreEqual(0.88f, sample.Confidence, 1e-3f);
            Assert.AreEqual("ear", sample.Source);
            Assert.AreEqual(16f, source.MeasuredLatencyMs, 1f, "capture→sent latency not measured");
            Assert.AreEqual(30f, source.SidecarFps, 1e-3f);
        }
    }

    // ---- the eval's failure taxonomy -------------------------------------------------------------

    [Test]
    public void FailureTaxonomy_LabelsTheObviousCases()
    {
        var m = new EpisodeMetrics { TimedOut = true, ClockedOut = false, SpuriousClockOuts = 2, MinEnergy = 0f, SimSeconds = 300f };
        var labels = FailureTaxonomy.Classify(m);
        CollectionAssert.Contains(labels, FailureTaxonomy.ClockBlindness);
        CollectionAssert.Contains(labels, FailureTaxonomy.SpuriousCompletion);
        CollectionAssert.Contains(labels, FailureTaxonomy.ResourceMismanagement);

        var clean = new EpisodeMetrics { ClockedOut = true, SimSeconds = 300f };
        CollectionAssert.IsEmpty(FailureTaxonomy.Classify(clean));
    }
}

// Tests that need the store itself: the map and the belief grid over it.
public class StoreMapAndBeliefTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    StoreMap map;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        map = StoreMap.Rebuild();
    }

    [Test]
    public void Map_HasTheStoreRoomsDoorsAndChokepoints()
    {
        Assert.Greater(map.CellCount, 1000);
        var rooms = map.Regions.Select(r => r.Room).Distinct().ToList();
        Assert.GreaterOrEqual(rooms.Count, 4, "rooms: " + string.Join(", ", rooms));
        Assert.IsTrue(map.Regions.Any(r => r.IsDoor), "no doorways");
        Assert.IsTrue(map.Regions.Any(r => r.IsChokepoint), "no chokepoints");
        Assert.IsTrue(map.FindLandmark(LandmarkKind.TimeClock).HasValue, "no time clock");
    }

    [Test]
    public void Map_EveryRegionIsReachableFromTheTimeClock()
    {
        int start = map.CellAt(map.FindLandmark(LandmarkKind.TimeClock).Value.Position, 4f);
        float[] d = map.Distances(start, float.MaxValue, null);
        var unreachable = map.Regions.Where(r => r.Cells.All(c => float.IsInfinity(d[c]))).Select(r => r.Name).ToList();
        Assert.IsEmpty(unreachable, string.Join(", ", unreachable));
    }

    [Test]
    public void Belief_CollapsesOnASightingAndSpreadsAgain()
    {
        var grid = new BeliefGrid(map);
        float h0 = grid.Entropy;
        Vector3 at = map.CellPosition[map.CellCount / 2];

        grid.Apply(new Observation(SenseChannel.Sight, at, 0.95f, 1.5f, "test sighting"));
        Assert.Less(grid.Entropy, h0 * 0.6f, "a confident sighting barely moved belief");
        Assert.AreEqual(map.RegionAt(at), grid.PeakRegion);
        float collapsed = grid.Entropy;

        for (int i = 0; i < 40; i++) grid.Predict(0.25f, 2.5f);
        Assert.Greater(grid.Entropy, collapsed, "belief didn't spread while unobserved");
    }

    [Test]
    public void Belief_ASweepRemovesMassWhereSheLooked()
    {
        var grid = new BeliefGrid(map);
        int region = map.Regions.First(r => !r.IsDoor && r.Cells.Count >= 6).Id;
        float before = grid.RegionMass[region];
        var cells = map.Regions[region].Cells.ToList();
        grid.ApplySweep(cells, cells.Select(_ => 0.9f).ToList());
        Assert.Less(grid.RegionMass[region], before * 0.2f);
        Assert.AreEqual(1f, grid.RegionMass.Sum(), 1e-3f, "belief no longer sums to one");
    }

    [Test]
    public void Belief_BiasMovesNoMoreThanItIsAllowed()
    {
        var grid = new BeliefGrid(map);
        int region = map.Regions.First(r => !r.IsDoor && r.Cells.Count >= 6).Id;
        float before = grid.RegionMass[region];
        grid.Bias(region, 0.15f);
        Assert.LessOrEqual(grid.RegionMass[region] - before, 0.15f + 1e-4f);
        Assert.Greater(grid.RegionMass[region], before);
    }
}
