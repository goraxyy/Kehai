using System.IO;
using Kehai.Karen;
using NUnit.Framework;

public class Karen050LearningTests
{
    string folder;
    KarenConfig config;

    [SetUp] public void SetUp()
    {
        folder = Path.Combine(Path.GetTempPath(), "kehai-ledger-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        config = new KarenConfig { ledgerPath = Path.Combine(folder, "ledger.json") };
    }
    [TearDown] public void TearDown() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    static EscapeRecord Escape(int shift, string layout) => new EscapeRecord {
        shift = shift, layout = layout, from = "clock", path = new[] { "left" },
        karen = new float[] { 0, 0 }, topThree = new[] { "left" }, t = 2f, end = "shift_end"
    };

    [Test] public void Ledger_TruthIsInvisibleUntilEndAndEndIsIdempotent()
    {
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.BeginShift(1);
        var row = Escape(1, ledger.Data.layout);
        ledger.QueueEscapesAtShiftEnd(new[] { row });
        row.path[0] = "mutated";
        Assert.AreEqual(.5f, ledger.EscapeChance("clock", "left", .5f));
        ledger.EndShift(10, 1, false);
        Assert.AreEqual(2f / 3f, ledger.EscapeChance("clock", "left", .5f), 1e-5);
        ledger.EndShift(10, 1, false);
        Assert.AreEqual(1f, ledger.Data.escapes[0].count);
        var reloaded = new KarenLedger(config) { Persistent = true };
        reloaded.Load(); reloaded.BeginShift(2);
        Assert.AreEqual(2f / 3f, reloaded.EscapeChance("clock", "left", .5f), 1e-5);
        reloaded.EndShift(10, 1, false);
        Assert.AreEqual(.7f, reloaded.Data.escapes[0].count, 1e-5);
    }

    [Test] public void Ledger_LegacySpatialMemoryBelongsOnlyToToday()
    {
        File.WriteAllText(config.ledgerPath, "{\"version\":1,\"regions\":[{\"key\":\"1_2_0\",\"dwell\":12}],\"routes\":[{\"destination\":\"clock\",\"path\":\"1_2_0|clock\",\"count\":2}]}");
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load();
        Assert.AreEqual(2, ledger.Data.version);
        Assert.AreEqual("today", ledger.Data.layout);
        Assert.IsEmpty(ledger.Data.escapes);
        ledger.SelectLayout("labyrinth");
        Assert.IsEmpty(ledger.Data.regions);
        Assert.IsEmpty(ledger.Data.routes);
        ledger.SelectLayout("today");
        Assert.AreEqual("1_2_0", ledger.Data.regions[0].key);
        Assert.AreEqual(12f, ledger.Data.regions[0].dwell);
    }

    [Test] public void Ledger_LegacyDoorOrdinalsAreDroppedOnceWithoutErasingSafeHabits()
    {
        var old = new LedgerData { version = 1, doorKeySchema = 0, sprintFraction = .6f };
        old.regions.Add(new RegionStat { key = "door_2", dwell = 30 });
        old.regions.Add(new RegionStat { key = "1_2_0", dwell = 12 });
        old.routes.Add(new RouteStat { path = "1_2_0|door_2|2_2_0", count = 3 });
        old.routes.Add(new RouteStat { path = "1_2_0|2_2_0", count = 2 });
        old.places.Add(new PlaceStat { trick = "door_lock", key = "door_2:left", n = 4 });
        File.WriteAllText(config.ledgerPath, UnityEngine.JsonUtility.ToJson(old));
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load();
        Assert.AreEqual(1, ledger.Data.doorKeySchema);
        Assert.AreEqual(1, ledger.Data.regions.Count);
        Assert.AreEqual("1_2_0", ledger.Data.regions[0].key);
        Assert.AreEqual(1, ledger.Data.routes.Count);
        Assert.IsEmpty(ledger.Data.places);
        Assert.AreEqual(.6f, ledger.Data.sprintFraction);
        Assert.That(ledger.Data.migrations[0], Does.Contain("legacy door_N"));
        ledger.Data.escapes.Add(new EscapeStat { layout = "today", from = "door_2", next = "1_2_0", count = 1 });
        ledger.Save();
        ledger.Load();
        Assert.AreEqual(1, ledger.Data.escapes.Count, "new sorted door counts must survive reload");
        Assert.AreEqual(1, ledger.Data.migrations.Count);
    }

    [Test] public void Ledger_FutureVersionIsNotOverwritten()
    {
        string future = "{\"version\":99,\"futureData\":42}";
        File.WriteAllText(config.ledgerPath, future);
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.BeginShift(1); ledger.EndShift(10, 1, false);
        Assert.AreEqual(future, File.ReadAllText(config.ledgerPath));
    }
    [Test] public void Ledger_LegacyMigrationKeepsExactOriginalAndDoesNotOverwriteBackup()
    {
        string original = "{\"version\":1,\"regions\":[{\"key\":\"door_2\",\"dwell\":30}]}";
        File.WriteAllText(config.ledgerPath, original);
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.Save();
        Assert.AreEqual(original, File.ReadAllText(config.ledgerPath + ".pre-0.5.0.json"));
        Assert.IsEmpty(ledger.Data.regions);
        ledger.Load(); ledger.Save();
        Assert.AreEqual(original, File.ReadAllText(config.ledgerPath + ".pre-0.5.0.json"));
        Assert.AreEqual(1, Directory.GetFiles(folder, "*.pre-0.5.0*.json").Length);
    }

    [Test] public void Ledger_UnreadableSaveCannotBeClobberedAtShiftEnd()
    {
        const string broken = "{this is not a ledger";
        File.WriteAllText(config.ledgerPath, broken);
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.BeginShift(1); ledger.EndShift(10, 1, false);
        Assert.AreEqual(broken, File.ReadAllText(config.ledgerPath));
    }

    [Test] public void Ledger_UnvisitedLayoutsFadeAndRestoreTheirOwnSpatialMemory()
    {
        var data = new LedgerData { doorKeySchema = 1 };
        data.regions.Add(new RegionStat { key = "safe", dwell = 10 });
        data.routes.Add(new RouteStat { path = "safe|other", count = 5 });
        data.places.Add(new PlaceStat { trick = "door_lock", key = "safe", n = 5 });
        File.WriteAllText(config.ledgerPath, UnityEngine.JsonUtility.ToJson(data));
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.BeginShift(1);
        ledger.SelectLayout("labyrinth");
        Assert.IsEmpty(ledger.Data.regions);
        ledger.EndShift(10, 1, false);
        ledger.Load(); ledger.SelectLayout("today");
        Assert.AreEqual(7f, ledger.Data.regions[0].dwell, 1e-5);
        Assert.AreEqual(3f, ledger.Data.routes[0].count, 1e-5);
        Assert.AreEqual(3f, ledger.Data.places[0].n, 1e-5);
        ledger.SelectLayout("labyrinth");
        Assert.IsEmpty(ledger.Data.regions);
    }

    [Test] public void Ledger_OnlyCurrentShiftLayoutEscapesCommit()
    {
        var ledger = new KarenLedger(config) { Persistent = true };
        ledger.Load(); ledger.BeginShift(2);
        ledger.QueueEscapesAtShiftEnd(new[] { Escape(1, ledger.Data.layout), Escape(2, "other"), Escape(2, ledger.Data.layout) });
        Assert.IsEmpty(ledger.Data.escapes);
        ledger.EndShift(10, 1, false);
        Assert.AreEqual(1, ledger.Data.escapes.Count);
        Assert.AreEqual(1f, ledger.Data.escapes[0].count);
    }

}
