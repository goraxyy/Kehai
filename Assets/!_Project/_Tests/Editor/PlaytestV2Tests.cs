using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai;
using Kehai.Playtest;
using NUnit.Framework;
using UnityEngine;

// Playtest v2's game side (C2.2, C2.3, C2.5–C2.7, C2.10), without a running store: the consent,
// the funnel's queue, what a send's answer means, the zip's headers and sidecars, the summary,
// the new-build check and what the thanks page says. The service's side is tools/playtest/worker.
public class PlaytestV2Tests
{
    const string ConsentKey = "Kehai.Playtest.Consent", VersionKey = "Kehai.Playtest.ConsentVersion";
    string temp;
    int consentBefore;
    string versionBefore;

    [SetUp]
    public void SetUp()
    {
        temp = Path.Combine(Path.GetTempPath(), "kehai-playtest-v2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        PlaytestPaths.RootOverride = temp;
        consentBefore = PlayerPrefs.GetInt(ConsentKey, 0);
        versionBefore = PlayerPrefs.GetString(VersionKey, null);
        PlaytestEvents.Answered(false);   // nothing left in memory from another test
    }

    [TearDown]
    public void TearDown()
    {
        PlaytestPaths.RootOverride = null;
        PlayerPrefs.SetInt(ConsentKey, consentBefore);
        if (versionBefore == null) PlayerPrefs.DeleteKey(VersionKey); else PlayerPrefs.SetString(VersionKey, versionBefore);
        PlayerPrefs.Save();
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
    }

    [Test]
    public void ATesterWhoAgreedToV1_IsAskedAgain_AndSendsNothingUntilThen()
    {
        PlayerPrefs.SetInt(ConsentKey, 1);
        PlayerPrefs.DeleteKey(VersionKey);      // v1 stored no version
        Assert.IsFalse(TesterState.ConsentAsked, "v1's answer doesn't cover training");
        Assert.IsFalse(TesterState.Sends);
        Assert.IsNull(TesterState.Agreed);
        TesterState.SetConsent(true);
        Assert.IsTrue(TesterState.Sends);
        Assert.AreEqual(PlaytestConsent.Version, TesterState.ConsentVersion);
        TesterState.SetConsent(false);
        Assert.AreEqual(false, TesterState.Agreed);
    }

    [Test]
    public void TheFunnel_WaitsForTheConsent_KeepsItOnYes_AndSendsNothingOnNo()
    {
        PlaytestEvents.Record("launch", null, "T07", "s1", "round1", "0.5.2");
        Assert.AreEqual(1, PlaytestEvents.Buffered);
        Assert.IsFalse(File.Exists(PlaytestEvents.QueuePath), "nothing is written before the answer");
        PlaytestEvents.Answered(false);
        Assert.AreEqual(0, PlaytestEvents.Buffered);
        PlaytestEvents.Record("end", false, "T07", "s1", "round1", "0.5.2");
        Assert.IsFalse(File.Exists(PlaytestEvents.QueuePath), "a tester who said no sends none");

        PlaytestEvents.Record("launch", null, "T07", "s2", "round1", "0.5.2");
        PlaytestEvents.Answered(true);
        PlaytestEvents.Record("consent", true, "T07", "s2", "round1", "0.5.2");
        PlaytestEvents.Record("not-a-kind", true, "T07", "s2", "round1", "0.5.2");
        List<string> queued = PlaytestEvents.Queued();
        CollectionAssert.AreEqual(new[] { "launch", "consent" }, queued.Select(l => MiniJson.ParseObject(l).GetString("kind")).ToArray());
        var body = MiniJson.ParseObject(PlaytestEvents.Body(queued));
        Assert.AreEqual(2, ((List<object>)body["events"]).Count);
        StringAssert.IsMatch("^[0-9a-f]{32}$", MiniJson.ParseObject(queued[0]).GetString("id"));

        PlaytestEvents.Drop(1);
        Assert.AreEqual(1, PlaytestEvents.Queued().Count, "the first was received");
        PlaytestEvents.Drop(1);
        Assert.IsFalse(File.Exists(PlaytestEvents.QueuePath));
    }

    [Test]
    public void ASendsAnswer_SaysWhatBecomesOfTheZip()
    {
        Assert.AreEqual(SendResult.Sent, SendAnswer.Of(201, false));
        Assert.AreEqual(SendResult.Sent, SendAnswer.Of(200, false), "a retry the service already had");
        Assert.AreEqual(SendResult.Gone, SendAnswer.Of(410, false), "the tester asked to be deleted");
        foreach (long kept in new long[] { 400, 409, 413 }) Assert.AreEqual(SendResult.Rejected, SendAnswer.Of(kept, false), kept.ToString());
        foreach (long later in new long[] { 401, 429, 500, 503 }) Assert.AreEqual(SendResult.Queued, SendAnswer.Of(later, false), later.ToString());
        Assert.AreEqual(SendResult.Queued, SendAnswer.Of(0, true), "offline or a timeout");
    }

    [Test]
    public void AZipGoesUpWithTheConsentItWasPackedUnder_AndAnOldOneWithNone()
    {
        Dictionary<string, string> old = PlaytestUploader.HeadersFor(null, "key-0123456789abcdef");
        Assert.IsFalse(old.ContainsKey("X-Kehai-Consent"), "an older build's zip: the service counts it as v1");
        var meta = new OutboxMeta { consent = PlaytestConsent.Version, training = true, build = "0.5.2-beta.1 85f67f4" };
        Dictionary<string, string> v2 = PlaytestUploader.HeadersFor(meta, "key-0123456789abcdef");
        Assert.AreEqual("v2", v2["X-Kehai-Consent"]);
        Assert.AreEqual("1", v2["X-Kehai-Training"]);
        Assert.AreEqual("0.5.2-beta.1 85f67f4", v2["X-Kehai-Build"]);
        Assert.AreEqual(PlaytestSummary.Schema.ToString(), v2["X-Kehai-Schema"]);
    }

    [Test]
    public void ARejectedZip_IsKeptHereWithItsSidecars_ASentOneLeaves_AndADeletedOneGoes()
    {
        string Zip(string name)
        {
            Directory.CreateDirectory(PlaytestPaths.Outbox);
            string zip = Path.Combine(PlaytestPaths.Outbox, name);
            File.WriteAllText(zip, "PK");
            new OutboxMeta { consent = "v2", training = true }.Write(zip);
            File.WriteAllText(OutboxMeta.SummaryPathFor(zip), "{}");
            return zip;
        }
        string rejected = Zip("round1_T07_20261127_100000.zip"), sent = Zip("round1_T07_20261127_110000.zip"), gone = Zip("round1_T07_20261127_120000.zip");
        PlaytestUploader.Settle(rejected, SendResult.Rejected);
        PlaytestUploader.Settle(sent, SendResult.Sent);
        PlaytestUploader.Settle(gone, SendResult.Gone);
        Assert.AreEqual(0, PlaytestUploader.Waiting().Length);
        Assert.AreEqual(0, Directory.GetFiles(PlaytestPaths.Outbox).Length, "no sidecar is left behind");
        CollectionAssert.AreEquivalent(
            new[] { "round1_T07_20261127_100000.zip", "round1_T07_20261127_100000.zip.json", "round1_T07_20261127_100000.summary.json" },
            Directory.GetFiles(PlaytestUploader.RejectedFolder).Select(Path.GetFileName).ToArray());
        Assert.AreEqual(1, PlaytestUploader.Kept().Length);
        Assert.AreEqual("v2", OutboxMeta.Read(Path.Combine(PlaytestUploader.RejectedFolder, "round1_T07_20261127_100000.zip")).consent);
    }

    [Test]
    public void TheSummary_HasWhatTheServiceReads()
    {
        var summary = new PlaytestSummary
        {
            Round = "round1", Tester = "A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1A1", Launch = "20261127_101500-" + new string('a', 32),
            Build = "0.5.2", Computer = "MacBookAir", Fps = 58.24f, Minutes = 21.53f, Ended = "finished", Training = true,
        };
        summary.Answers["karen"] = "Scary";
        summary.Answers["more"] = "Yes";
        summary.Answers["a1"] = "Find the mop";
        summary.Answers["nickname"] = "Rin";
        summary.Moment(2, 133.54f, "stuck on the shelf");
        summary.Shifts.Add(new Dictionary<string, object> { ["n"] = 1, ["seconds"] = 420f, ["caught"] = 0 });
        Dictionary<string, object> json = MiniJson.ParseObject(summary.ToJson());
        Assert.AreEqual("round1", json.GetString("round"));
        Assert.AreEqual(PlaytestSummary.Schema, (int)json.GetNumber("schema"));
        Dictionary<string, object> consent = json.GetObject("consent");
        Assert.AreEqual("v2", consent.GetString("version"));
        Assert.IsTrue(consent.GetBool("training"));
        Dictionary<string, object> answers = json.GetObject("answers");
        var taps = (List<object>)answers["taps"];
        CollectionAssert.AreEqual(new object[] { "Scary", null, null, "Yes" }, taps, "the four taps in order; unanswered ones are null");
        Assert.AreEqual("Find the mop", answers.GetString("a1"));
        Assert.AreEqual("", answers.GetString("a2"));
        Assert.AreEqual("Rin", answers.GetString("nickname"));
        var moment = (Dictionary<string, object>)((List<object>)json["moments"])[0];
        Assert.AreEqual(2, (int)moment.GetNumber("shift"));
        Assert.AreEqual(133.5, moment.GetNumber("replay_s"), 1e-6);
        Assert.AreEqual(58.2, json.GetNumber("fps"), 1e-6);
        Assert.AreEqual(1, ((List<object>)json["shifts"]).Count);
    }

    [Test]
    public void ANewerBuild_IsNewerByItsVersion_AndLinksToThisPlatformsDownload()
    {
        Assert.IsTrue(PlaytestUpdate.IsNewer("0.5.3", "0.5.2"));
        Assert.IsTrue(PlaytestUpdate.IsNewer("0.5.2", "0.5.2-beta.4 85f67f4"), "a release beats its pre-releases");
        Assert.IsTrue(PlaytestUpdate.IsNewer("0.5.2-beta.10", "0.5.2-beta.9"));
        Assert.IsTrue(PlaytestUpdate.IsNewer("0.6.0", "0.5.12"));
        Assert.IsFalse(PlaytestUpdate.IsNewer("0.5.2", "0.5.2"));
        Assert.IsFalse(PlaytestUpdate.IsNewer("0.5.1", "0.5.2"));
        Assert.IsFalse(PlaytestUpdate.IsNewer("0.5.2-beta.4", "0.5.2"));
        Assert.IsFalse(PlaytestUpdate.IsNewer("0.5.3", ""), "a build that doesn't know its version says nothing");

        PlaytestUpdate.Saw("{\"round\":\"round1\",\"version\":\"0.5.2\"}", "0.5.2", "https://up.example.dev", RuntimePlatform.OSXPlayer);
        Assert.IsNull(PlaytestUpdate.Latest, "the same build");
        PlaytestUpdate.Saw("{\"round\":\"round2\",\"version\":\"0.5.3\"}", "0.5.2", "https://up.example.dev/", RuntimePlatform.WindowsPlayer);
        Assert.AreEqual("0.5.3", PlaytestUpdate.Latest);
        Assert.AreEqual("https://up.example.dev/download/windows", PlaytestUpdate.DownloadUrl);
        PlaytestUpdate.Saw("not json", "0.5.2", "https://up.example.dev", RuntimePlatform.OSXPlayer);
        Assert.AreEqual("0.5.3", PlaytestUpdate.Latest, "a bad answer changes nothing");
    }

    [Test]
    public void TheThanksPage_SaysOnlyWhatHappened()
    {
        StringAssert.Contains("Nothing was sent", PlaytestScreens.Outcome(agreed: false, uploads: true, received: false, queued: 0, kept: 0));
        StringAssert.Contains("doesn't send", PlaytestScreens.Outcome(true, uploads: false, received: false, queued: 1, kept: 0));
        StringAssert.Contains("was received", PlaytestScreens.Outcome(true, true, received: true, queued: 0, kept: 0));
        StringAssert.Contains("isn't sent yet", PlaytestScreens.Outcome(true, true, received: true, queued: 1, kept: 0), "one sent, one still waiting");
        StringAssert.Contains("isn't sent yet", PlaytestScreens.Outcome(true, true, received: false, queued: 1, kept: 0), "offline");
        StringAssert.Contains("kept on this computer", PlaytestScreens.Outcome(true, true, received: true, queued: 0, kept: 1), "too big");
    }

    [Test]
    public void TheEndPage_AsksQ2sThreeQuestionsAndQ3sNickname_AndTheConsentSaysTraining()
    {
        Assert.AreEqual(3, PlaytestScreens.TypedQuestions.Length);
        StringAssert.Contains("confusing", PlaytestScreens.TypedQuestions[0]);
        StringAssert.Contains(GameNames.Antagonist, PlaytestScreens.TypedQuestions[1]);
        StringAssert.Contains("change first", PlaytestScreens.TypedQuestions[2]);
        StringAssert.Contains("train " + GameNames.Antagonist, PlaytestConsent.TrainingLine);
    }
}
