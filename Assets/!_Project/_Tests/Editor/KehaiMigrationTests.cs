using System.IO;
using System.Linq;
using Kehai;
using NUnit.Framework;

// The save data follows the game's renames. The first launch as Kehai copies what the game
// kept as Karoshi; a launch after she became Karen again copies her files and settings from
// Aiko's names. The right files, under the right names, only once, never touching the old.
public class KehaiMigrationTests
{
    string root, before, now;

    [SetUp]
    public void MakeFolders()
    {
        root = Path.Combine(Path.GetTempPath(), "kehai_migration_" + System.Guid.NewGuid().ToString("N"));
        before = Path.Combine(root, "Karoshi");
        now = Path.Combine(root, "Kehai");
        Directory.CreateDirectory(before);
    }

    [TearDown]
    public void RemoveFolders()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    void Write(string relative, string text)
    {
        string path = Path.Combine(before, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    void OldSave()
    {
        Write("karen_ledger.json", "{\"version\":1,\"shiftsWorked\":3}");
        Write("karen_logs/shift_01.jsonl", "{\"kind\":\"PLAN\"}");
        Write("karoshi_eval/ablation_20260925.jsonl", "{\"rung\":\"F\",\"karen_catches\":2,\"karen_top_tactics\":\"spill fog\"}\n{\"karen_catches\":0}");
        Write("karoshi_eval/eval_ledger.json", "{\"version\":1}");
        Write("shift_records/shift_01_20260926_054221.json", "{\"shift\":1}");
        Write("Unity/cache.bin", "Unity's own");
        Write("TestResults.xml", "<test-run/>");
    }

    [Test]
    public void CopiesEachKindUnderItsNewName()
    {
        OldSave();
        Assert.AreEqual(4, KehaiMigration.CopyData(before, now));

        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":3}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
        Assert.IsTrue(File.Exists(Path.Combine(now, "karen_logs", "shift_01.jsonl")));
        Assert.IsTrue(File.Exists(Path.Combine(now, "kehai_eval", "eval_ledger.json")));
        Assert.IsTrue(File.Exists(Path.Combine(now, "shift_records", "shift_01_20260926_054221.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(now, "Unity")), "Unity's own cache isn't the game's data");
        Assert.IsFalse(File.Exists(Path.Combine(now, "TestResults.xml")));

        string eval = File.ReadAllText(Path.Combine(now, "kehai_eval", "ablation_20260925.jsonl"));
        Assert.AreEqual("{\"rung\":\"F\",\"karen_catches\":2,\"karen_top_tactics\":\"spill fog\"}\n{\"karen_catches\":0}", eval);
    }

    [Test]
    public void LeavesTheOldFolderAsItWas()
    {
        OldSave();
        string[] Listing() => Directory.GetFiles(before, "*", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        string[] was = Listing();
        string evalWas = File.ReadAllText(Path.Combine(before, "karoshi_eval", "ablation_20260925.jsonl"));

        KehaiMigration.CopyData(before, now);

        CollectionAssert.AreEqual(was, Listing());
        Assert.AreEqual(evalWas, File.ReadAllText(Path.Combine(before, "karoshi_eval", "ablation_20260925.jsonl")));
    }

    [Test]
    public void DoesNothingWhenTheNewFolderAlreadyHasData()
    {
        OldSave();
        Directory.CreateDirectory(Path.Combine(now, "shift_records"));
        File.WriteAllText(Path.Combine(now, "shift_records", "shift_02.json"), "{}");

        Assert.AreEqual(0, KehaiMigration.CopyData(before, now));
        Assert.IsFalse(File.Exists(Path.Combine(now, "karen_ledger.json")));
    }

    [Test]
    public void AnEmptyFolderIsNotData()
    {
        OldSave();
        Directory.CreateDirectory(Path.Combine(now, "kehai_eval"));   // e.g. "Open Eval Folder" ran first

        Assert.IsFalse(KehaiMigration.HasData(now));
        Assert.AreEqual(4, KehaiMigration.CopyData(before, now));
    }

    [Test]
    public void RunsOnlyOnce()
    {
        OldSave();
        KehaiMigration.CopyData(before, now);
        File.WriteAllText(Path.Combine(now, "karen_ledger.json"), "{\"version\":1,\"shiftsWorked\":4}");

        Assert.AreEqual(0, KehaiMigration.CopyData(before, now));
        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":4}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
    }

    [Test]
    public void NoOldFolderOrTheSameFolderIsANoOp()
    {
        Assert.AreEqual(0, KehaiMigration.CopyData(Path.Combine(root, "nothing here"), now));
        OldSave();
        Assert.AreEqual(0, KehaiMigration.CopyData(before, before));
    }

    [Test]
    public void TakesDataTheRenamedGameWroteBeforeTheProductNameChanged()
    {
        Write("aiko_ledger.json", "{\"version\":1,\"shiftsWorked\":5}");
        Write("karen_ledger.json", "{\"version\":1,\"shiftsWorked\":3}");

        KehaiMigration.CopyData(before, now);
        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":5}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
    }

    // ---- Aiko → Karen, in Kehai's own folder ----------------------------------------------------

    void WriteNow(string relative, string text)
    {
        string path = Path.Combine(now, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    [Test]
    public void AikosFiles_AreCopiedToKarensNames_AndKeptAsTheyWere()
    {
        WriteNow("aiko_ledger.json", "{\"version\":1,\"shiftsWorked\":7}");
        WriteNow("aiko_logs/shift_07.jsonl", "{\"kind\":\"PLAN\"}");

        Assert.AreEqual(2, KehaiMigration.CopyAikoData(now));
        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":7}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
        Assert.IsTrue(File.Exists(Path.Combine(now, "karen_logs", "shift_07.jsonl")));
        Assert.IsTrue(File.Exists(Path.Combine(now, "aiko_ledger.json")), "copied, not moved");
        Assert.IsTrue(File.Exists(Path.Combine(now, "aiko_logs", "shift_07.jsonl")), "copied, not moved");
    }

    [Test]
    public void AikosFiles_NeverOverwriteKarens()
    {
        WriteNow("aiko_ledger.json", "{\"version\":1,\"shiftsWorked\":7}");
        WriteNow("karen_ledger.json", "{\"version\":1,\"shiftsWorked\":8}");

        Assert.AreEqual(0, KehaiMigration.CopyAikoData(now));
        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":8}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
    }

    // A career played as Aiko is newer than the one in the Karoshi folder, and is the one kept.
    [Test]
    public void AnAikoCareer_IsKept_OverAnOlderKaroshiOne()
    {
        OldSave();   // a Karoshi career of 3 shifts
        WriteNow("aiko_ledger.json", "{\"version\":1,\"shiftsWorked\":7}");

        KehaiMigration.MigrateData(before, now);
        Assert.AreEqual("{\"version\":1,\"shiftsWorked\":7}", File.ReadAllText(Path.Combine(now, "karen_ledger.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(now, "shift_records")), "nothing came across from Karoshi");
    }

    [Test]
    public void EvalRunsFromEitherName_ReadAsKarens()
    {
        Assert.AreEqual("{\"karen_catches\":2}", KehaiMigration.RenameEvalKeys("{\"aiko_catches\":2}"));
        Assert.AreEqual("{\"karen_catches\":2}", KehaiMigration.RenameEvalKeys("{\"karen_catches\":2}"));
    }

    // Every setting she had under Aiko's names goes to the name the game reads now.
    [Test]
    public void AikosSettings_GoToTheKeysTheGameReads()
    {
        var keys = KehaiMigration.AikoPrefKeys().ToDictionary(k => k.aiko, k => k.now);
        Assert.AreEqual("Kehai.Volume." + SoundKind.Karen, keys["Kehai.Volume.Aiko"]);
        Assert.AreEqual(Kehai.Blink.BlinkTracker.ConsentKey, keys["aiko.blink.consent"]);
        Assert.AreEqual("karen.blink.cal.vision.open", keys["aiko.blink.cal.vision.open"]);
        Assert.AreEqual("karen.blink.cal.vision.closed", keys["aiko.blink.cal.vision.closed"]);
        Assert.AreEqual("Kehai.KarenCone", keys["Kehai.AikoCone"]);
        foreach (var k in KehaiMigration.AikoPrefKeys())
            StringAssert.DoesNotContain("aiko", k.now.ToLowerInvariant());
    }

    // The shape `plutil -convert xml1` prints for the game's old PlayerPrefs.
    const string OldPlist = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
	<key>Karoshi.KarenCone</key>
	<integer>0</integer>
	<key>Karoshi.MouseSensitivity</key>
	<real>1.5</real>
	<key>Karoshi.Volume.Karen</key>
	<real>0.75</real>
	<key>UnityGraphicsQuality</key>
	<integer>0</integer>
	<key>karen.blink.cal.vision.closed</key>
	<real>0.28540000319480896</real>
	<key>karen.blink.consent</key>
	<integer>1</integer>
	<key>unity.cloud_userid</key>
	<string>76534852abee34262b3f3bd908b3fe06</string>
</dict>
</plist>";

    [Test]
    public void PrefsKeepTheirTypesUnderTheNewNames()
    {
        var prefs = KehaiMigration.RenamePrefs(KehaiMigration.ParsePlist(OldPlist)).ToDictionary(p => p.Key);

        CollectionAssert.AreEquivalent(new[] { "Kehai.KarenCone", "Kehai.MouseSensitivity", "Kehai.Volume.Karen", "karen.blink.cal.vision.closed", "karen.blink.consent" }, prefs.Keys);
        Assert.AreEqual(KehaiMigration.PrefKind.Int, prefs["Kehai.KarenCone"].Kind);
        Assert.AreEqual(0, prefs["Kehai.KarenCone"].Int);
        Assert.AreEqual(KehaiMigration.PrefKind.Float, prefs["Kehai.Volume.Karen"].Kind);
        Assert.AreEqual(0.75f, prefs["Kehai.Volume.Karen"].Float);
        Assert.AreEqual(0.2854f, prefs["karen.blink.cal.vision.closed"].Float, 1e-6f);
        Assert.AreEqual(1, prefs["karen.blink.consent"].Int);
    }

    [Test]
    public void AnUnreadablePlistMeansNoPrefs()
    {
        Assert.IsEmpty(KehaiMigration.ParsePlist(""));
        Assert.IsEmpty(KehaiMigration.ParsePlist("<plist version=\"1.0\"><array/></plist>"));
    }
}
