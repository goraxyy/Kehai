using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Kehai;
using Kehai.Playtest;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The playtest mode's moving parts that don't need a running store: the build's config, tester
// codes, the session log, what a session packs and where it's sent, and PLAYTEST.md's questions.
public class PlaytestTests
{
    string temp;

    [SetUp]
    public void MakeTemp()
    {
        temp = Path.Combine(Path.GetTempPath(), "kehai-playtest-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
    }

    [TearDown]
    public void RemoveTemp()
    {
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
    }

    [Test]
    public void Config_NeedsARound_AndTidiesItsAddress()
    {
        PlaytestConfig c = PlaytestConfig.Parse("{\"round\":\" round1 \",\"uploadUrl\":\"https://up.example.dev/\",\"suggestFinishAfter\":0}");
        Assert.IsNotNull(c);
        Assert.AreEqual("round1", c.round);
        Assert.AreEqual("https://up.example.dev", c.uploadUrl);
        Assert.AreEqual(3, c.suggestFinishAfter);
        Assert.IsTrue(c.Uploads);
        Assert.IsNull(PlaytestConfig.Parse("{\"uploadUrl\":\"https://up.example.dev\"}"), "no round, no playtest");
        Assert.IsNull(PlaytestConfig.Parse("not json"));
        Assert.IsFalse(PlaytestConfig.Parse("{\"round\":\"r\"}").Uploads, "no address keeps sessions on the computer");
    }

    [Test]
    public void FormLink_CarriesTheTesterCode()
    {
        var c = new PlaytestConfig { round = "r", formUrl = "https://forms.example/viewform?entry.1={code}" };
        Assert.AreEqual("https://forms.example/viewform?entry.1=T07", c.FormFor("T07"));
        Assert.IsNull(new PlaytestConfig { round = "r" }.FormFor("T07"));
    }

    [Test]
    public void TesterCodes_AreShortLettersDigitsAndDashes()
    {
        Assert.AreEqual("T07", TesterCode.Normalize(" t07 "));
        Assert.AreEqual("LAF-3", TesterCode.Normalize("laf-3"));
        foreach (string bad in new[] { null, "", "T", "T 07", "T_07", "TOOLONGFORACODE123", "Ä1" })
            Assert.IsNull(TesterCode.Normalize(bad), bad ?? "null");
    }

    [Test]
    public void SessionLog_WritesOneParsableLinePerNote()
    {
        string path = Path.Combine(temp, "s", PlaytestPaths.LogName);
        using (var log = new SessionLog(path))
        {
            log.Write(0f, "start", new Dictionary<string, object> { ["utc"] = "2026-10-04T10:15:00.0000000Z", ["code"] = "T07" });
            log.Write(1.234f, "key", new Dictionary<string, object> { ["key"] = "E", ["target"] = "Mop \"wet\"" });
        }
        string[] lines = File.ReadAllLines(path);
        Assert.AreEqual(2, lines.Length);
        Dictionary<string, object> key = MiniJson.ParseObject(lines[1]);
        Assert.AreEqual("key", key.GetString("k"));
        Assert.AreEqual(1.23, key.GetNumber("t"), 0.001);
        Assert.AreEqual("Mop \"wet\"", key.GetString("target"));
        Assert.AreEqual(new DateTime(2026, 10, 4, 10, 15, 0, DateTimeKind.Utc), SessionLog.StartedUtc(path));
    }

    [Test]
    public void APackage_HoldsTheSession_NotOldShiftsOrReports()
    {
        DateTime started = DateTime.UtcNow.AddMinutes(-10);
        string session = Dir("sessions/20261004_101500"), shifts = Dir("shift_records"), thoughts = Dir("karen_logs");
        File.WriteAllText(Path.Combine(session, PlaytestPaths.LogName), "{}\n");
        string[] fresh = { "shift_01_x.json", "shift_01_x.krec", "shift_01_x.markers.json", "interlude_01_x.krec", "shift_02_y.krec.part", "shift_01_x.html", "notes.txt" };
        foreach (string f in fresh) File.WriteAllText(Path.Combine(shifts, f), f);
        string old = Path.Combine(shifts, "shift_09_old.krec");
        File.WriteAllText(old, "old");
        File.SetLastWriteTimeUtc(old, started.AddHours(-2));
        File.WriteAllText(Path.Combine(thoughts, "thoughts_shift01.jsonl"), "{}");
        string ledger = Path.Combine(temp, "karen_ledger.json");
        File.WriteAllText(ledger, "{}");
        string playerLog = Path.Combine(temp, "Player.log");
        File.WriteAllText(playerLog, "log");

        var files = PlaytestPackage.Collect(session, started, shifts, thoughts, ledger, playerLog, previousLogToo: false);
        CollectionAssert.AreEquivalent(new[]
        {
            "session.jsonl", "shifts/shift_01_x.json", "shifts/shift_01_x.krec", "shifts/shift_01_x.markers.json",
            "shifts/interlude_01_x.krec", "shifts/shift_02_y.krec.part", "karen_logs/thoughts_shift01.jsonl", "karen_ledger.json", "Player.log",
        }, files.Select(f => f.entry).ToArray());

        string zip = PlaytestPackage.Pack(Path.Combine(temp, "outbox", PlaytestPackage.ZipName("round1", "T07", "20261004_101500")), files);
        Assert.AreEqual("round1_T07_20261004_101500.zip", Path.GetFileName(zip));
        using var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read);
        Assert.AreEqual(files.Count, archive.Entries.Count);
    }

    [Test]
    public void Uploads_GoToRoundTesterAndLaunch()
    {
        Assert.AreEqual("round-2_T-7_20261004_101500.zip", PlaytestPackage.ZipName("round 2", "T_7", "20261004_101500"));
        Assert.AreEqual("https://up.example.dev/sessions/round1/T07/20261004_101500.zip",
            PlaytestUploader.UrlFor("https://up.example.dev/", "/x/outbox/round1_T07_20261004_101500.zip"));
        Assert.IsNull(PlaytestUploader.UrlFor("https://up.example.dev", "/x/outbox/stray.zip"));
    }

    [Test]
    public void PlaytestMd_HasTheQuestionsTheGameAsks()
    {
        string md = File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs", "production", "PLAYTEST.md"));
        foreach ((string id, string text, string[] options) q in PlaytestScreens.Questions)
        {
            StringAssert.Contains(q.text, md, "PLAYTEST.md is missing a question");
            foreach (string option in q.options) StringAssert.Contains(option, md, $"PLAYTEST.md is missing an answer to \"{q.text}\"");
        }
        StringAssert.Contains(PlaytestScreens.FreeQuestion, md);
    }

    [Test]
    public void PlaytestBuilds_ReadTheirSettings_AndGoInTheirOwnFolder()
    {
        string env = Path.Combine(temp, ".env");
        File.WriteAllText(env, "# comment\nKEHAI_PLAYTEST_ROUND=round2\nKEHAI_PLAYTEST_UPLOAD_URL=\"https://up.example.dev/\"\n\nBROKEN LINE\n");
        Dictionary<string, string> values = KehaiBuild.ReadEnvFile(env);
        Assert.AreEqual("round2", values["KEHAI_PLAYTEST_ROUND"]);
        Assert.AreEqual("https://up.example.dev/", values["KEHAI_PLAYTEST_UPLOAD_URL"]);
        Assert.AreEqual(2, values.Count);

        string name = PlayerSettings.productName;
        StringAssert.EndsWith(Path.Combine("Builds", "playtest-round1", $"{name}-playtest-round1-macOS", name + ".app"),
            KehaiBuild.DefaultPath(BuildTarget.StandaloneOSX, "round1"));
        StringAssert.EndsWith(Path.Combine($"{name}.app", "Contents", "Resources", "Data", "StreamingAssets"),
            KehaiBuild.StreamingAssetsOf(BuildTarget.StandaloneOSX, "/b/" + name + ".app"));
        StringAssert.EndsWith(Path.Combine($"{name}_Data", "StreamingAssets"),
            KehaiBuild.StreamingAssetsOf(BuildTarget.StandaloneWindows64, "/b/" + name + ".exe"));
    }

    string Dir(string relative)
    {
        string path = Path.Combine(temp, relative);
        Directory.CreateDirectory(path);
        return path;
    }
}
