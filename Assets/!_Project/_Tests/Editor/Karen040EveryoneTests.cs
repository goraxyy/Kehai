using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// 0.4.0, phase 3 (build plan §6): the Ledger read by the trained parts (everyone, and you), the
// rules that keep them apart, and the model pack.
public class Karen040EveryoneTests
{
    static KarenLedger Ledger() => new KarenLedger(new KarenConfig { ledgerPath = Path.Combine(Path.GetTempPath(), "kehai_040_ledger.json") });

    // ---- C3.1 the blend ---------------------------------------------------------------------------

    [Test]
    public void Blend_IsTheFormulaOfTheplan()
    {
        // her guess = (n × yours + 2 × everyone's) ÷ (n + 2)
        Assert.AreEqual(0.12f, KarenLedger.Blend(0f, 0.9f, 0.12f), 1e-6f, "nothing seen: everyone's");
        Assert.AreEqual((3 * 0.01f + 2 * 0.12f) / 5f, KarenLedger.Blend(3f, 0.01f, 0.12f), 1e-6f);
    }

    // A trick's result moves her guess for the other tricks of its kind, and less than its own.
    [Test]
    public void Blend_ATrickMovesItsKind_AndItselfMost()
    {
        Tactic blackout = TacticLibrary.Get("blackout"), fog = TacticLibrary.Get("fog");
        Assume.That(blackout != null && fog != null, "two tricks that attack sight");
        Assert.AreEqual(KarenLedger.KindOf(blackout), KarenLedger.KindOf(fog));
        LedgerStart everyone = LedgerStart.Hand();
        KarenLedger ledger = Ledger();
        float blackoutBefore = ledger.Reading(blackout, everyone).Guess, fogBefore = ledger.Reading(fog, everyone).Guess;
        Assert.AreEqual(everyone.Trick("blackout"), blackoutBefore, 1e-6f);

        for (int i = 0; i < 3; i++) ledger.RecordEvidence(blackout, -0.3f);   // blackouts that barely moved you
        float blackoutAfter = ledger.Reading(blackout, everyone).Guess, fogAfter = ledger.Reading(fog, everyone).Guess;
        Assert.Less(fogAfter, fogBefore, "the other sight trick's guess drops too");
        Assert.Greater(blackoutBefore - blackoutAfter, fogBefore - fogAfter, "and less than its own");
        LedgerReading r = ledger.Reading(blackout, everyone);
        Assert.AreEqual(3f, r.N, 1e-6f);
        Assert.AreEqual(-0.3f, r.Yours, 1e-6f);
        Assert.AreEqual(0.6f, r.YourShare, 1e-6f);
        StringAssert.Contains("over 3", r.ToString());
    }

    [Test]
    public void Blend_AnswersAndHabitsAreReadTheSameWay()
    {
        KarenLedger ledger = Ledger();
        LedgerStart everyone = LedgerStart.Hand();
        everyone.SetAnswer("fog_reset", 60f);
        Assert.AreEqual(60f, ledger.AnswerReading("fog_reset", everyone).Guess, 1e-5f);
        ledger.RecordCounterplay("fog_reset", 20f);
        ledger.RecordCounterplay("fog_reset", 20f);
        Assert.AreEqual(KarenLedger.Blend(2f, 20f, 60f), ledger.AnswerReading("fog_reset", everyone).Guess, 1e-4f);
    }

    [Test]
    public void Evidence_FadesEachShift_AndRemovedTricksDropOut()
    {
        KarenLedger ledger = Ledger();
        Tactic blackout = TacticLibrary.Get("blackout");
        ledger.RecordEvidence(blackout, 0.2f);
        ledger.EndShift(100f, 1f, true);
        EvidenceStat e = ledger.TrickEvidence("blackout");
        Assert.AreEqual(0.8f, e.n, 1e-5f, "rule 7: it fades");
        Assert.AreEqual(0.2f, e.Mean, 1e-5f, "its mean stays");
        ledger.Data.trickEvidence.Add(new EvidenceStat { key = "a_trick_long_gone", n = 4, sum = 1 });
        ledger.Persistent = true;
        ledger.Save();
        KarenLedger again = Ledger();
        again.Persistent = true;
        again.Load();
        Assert.IsNull(again.TrickEvidence("a_trick_long_gone"));
        Assert.IsNotNull(again.TrickEvidence("blackout"));
        again.Wipe();
    }

    // ---- C3.6 the thought log and the shift report say both -----------------------------------------

    [Test]
    public void ThoughtLog_SaysEveryoneAndYou()
    {
        var r = new LedgerReading { Everyone = 0.12f, Yours = 0.01f, N = 3f, Guess = KarenLedger.Blend(3f, 0.01f, 0.12f) };
        Assert.AreEqual("PICK     flush:blackout 41% (everyone +0.12, you +0.01 over 3 → +0.05)", KarenBrain.PickLine("flush:blackout", 0.41f, r));
        Assert.AreEqual("everyone +0.12 → +0.12", new LedgerReading { Everyone = 0.12f, Guess = 0.12f }.ToString(), "nothing seen of you yet");
        Assert.AreEqual(r.ToString(), PerformanceReview.EveryoneAndYou(r));
        string brain = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts/Core/KarenBrain.cs"));
        StringAssert.Contains("Note(\"PICK\", PickLine(", brain, "every pick goes in the thought log");
    }

    // ---- C3.5 the separation tests (§6.4) -----------------------------------------------------------

    // Rule 3: your Ledger holds only your evidence, as plain counts and averages.
    [Test]
    public void Separation_ALedgerHoldsOnlyItsPlayersEvidence()
    {
        KarenLedger ledger = Ledger();
        Tactic blackout = TacticLibrary.Get("blackout");
        LedgerStart everyone = LedgerStart.Hand();
        everyone.SetTrick("blackout", -1, 0.5f);
        ledger.RecordEvidence(blackout, 0.01f);
        ledger.RecordEvidence(blackout, 0.03f);
        Assert.AreEqual(0.02f, ledger.TrickEvidence("blackout").Mean, 1e-6f, "her record of you isn't blended");
        ledger.Reading(blackout, everyone);
        Assert.AreEqual(0.02f, ledger.TrickEvidence("blackout").Mean, 1e-6f, "reading it doesn't write everyone into it");
        string json = JsonUtility.ToJson(ledger.Data);
        StringAssert.DoesNotContain("0.5", json, "no 'everyone' number is stored in your Ledger");
    }

    // Rule 7: wiping the Ledger gives you everyone's Karen again.
    [Test]
    public void Separation_WipingGivesEveryone()
    {
        KarenLedger ledger = Ledger();
        LedgerStart everyone = LedgerStart.Hand();
        Tactic blackout = TacticLibrary.Get("blackout");
        for (int i = 0; i < 5; i++) ledger.RecordEvidence(blackout, -0.4f);
        Assert.AreNotEqual(everyone.Trick("blackout"), ledger.Reading(blackout, everyone).Guess);
        ledger.Wipe();
        foreach (Tactic t in TacticLibrary.All)
            Assert.AreEqual(everyone.Trick(t.Id), ledger.Reading(t, everyone).Guess, 1e-6f, t.Id);
    }

    // Rule 2: the general model is read-only while you play. Nothing in the game writes it:
    // the starting Ledger's setters are used only where it's built, and no game code writes a pack
    // or a generated file.
    [Test]
    public void Separation_TheGameNeverWritesTheGeneralModel()
    {
        string game = Path.Combine(Application.dataPath, "!_Project/_Game");
        var writes = new Regex(@"\.(SetTrick|SetKind|SetAnswer)\(|\b(everyone|start|LedgerStart\.Current)\.(Hides|Routes)\.Add");
        var files = new Regex(@"File\.(Write|Append|Copy|Move|Delete)\w*\([^)]*(kpack|ModelPack\.Folder|Generated|\.g\.cs)");
        var leaks = new List<string>();
        foreach (string file in Directory.GetFiles(game, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            bool builder = name == "LedgerStart.cs" || name == "ModelPack.cs" || name.EndsWith(".g.cs");
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].Split(new[] { "//" }, System.StringSplitOptions.None)[0];
                if ((!builder && writes.IsMatch(code)) || files.IsMatch(code)) leaks.Add($"{name}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.IsEmpty(leaks, "the game wrote the general model:\n" + string.Join("\n", leaks));
    }

    // ---- C3.7 the model pack: §11's three checks (the gate's is tools/learn/tests/test_pack.py) ------

    static RSAParameters testKey;

    static string Pack(int version, int inputsVersion, string parts)
    {
        string body = "{\"format\":\"kehai-model-pack\",\"pack_version\":" + version + ",\"inputs_version\":" + inputsVersion +
                      ",\"created\":\"2026-11-11\",\"parts\":[" + parts + "]}";
        return body + "\n" + ModelPack.Sign(body, testKey) + "\n";
    }

    [SetUp]
    public void TrustATestKey()
    {
        using (RSA rsa = RSA.Create())
        {
            rsa.KeySize = 2048;
            testKey = rsa.ExportParameters(true);
            ModelPack.TrustedKeyOverride = new RSAParameters { Modulus = testKey.Modulus, Exponent = testKey.Exponent };
        }
    }

    [TearDown]
    public void Untrust()
    {
        ModelPack.TrustedKeyOverride = null;
        ModelPack.Use(null);
    }

    // 1. A pack that's identical gives the build's own numbers, so the same seeds play the same shift.
    [Test]
    public void Pack_IdenticalToTheBuild_GivesTheSameNumbers()
    {
        LedgerStart build = LedgerStart.Hand();
        Assert.IsTrue(ModelPack.TryParse(Pack(1, BrainObservation.Version, build.ToPartJson()), "test", out ModelPack p, out string why), why);
        ModelPack.Use(p);
        LedgerStart fromPack = LedgerStart.Current;
        Assert.AreNotSame(build, fromPack);
        foreach (Tactic t in TacticLibrary.All)
            Assert.AreEqual(build.Trick(t.Id), fromPack.Trick(t.Id), 0f, t.Id);
        foreach (string kind in build.KindKeys)
            Assert.AreEqual(build.Kind(kind), fromPack.Kind(kind), 0f, kind);
        KarenLedger ledger = Ledger();
        Tactic blackout = TacticLibrary.Get("blackout");
        ledger.RecordEvidence(blackout, 0.05f);
        Assert.AreEqual(ledger.Reading(blackout, build).Guess, ledger.Reading(blackout, fromPack).Guess, 0f, "she reads you the same");
        ModelPack.Use(null);
        Assert.AreEqual("hand", LedgerStart.Current.Version, "no pack: the build's own");
    }

    // 2. A tampered pack, an old inputs' version and an unknown trick name are each refused.
    [Test]
    public void Pack_TamperedOldOrUnknown_IsRefused()
    {
        string good = Pack(2, BrainObservation.Version, LedgerStart.Hand().ToPartJson());
        Assert.IsTrue(ModelPack.TryParse(good, "t", out _, out string why), why);

        string tampered = good.Replace("\"pack_version\":2", "\"pack_version\":9");
        Assert.IsFalse(ModelPack.TryParse(tampered, "t", out _, out why));
        StringAssert.Contains("signature", why);

        Assert.IsFalse(ModelPack.TryParse(Pack(2, BrainObservation.Version - 1, LedgerStart.Hand().ToPartJson()), "t", out _, out why));
        StringAssert.Contains("inputs version", why);

        string unknown = "{\"kind\":\"start\",\"name\":\"LedgerStart\",\"card\":{},\"tricks\":{\"juggling\":[0.2]}}";
        Assert.IsFalse(ModelPack.TryParse(Pack(2, BrainObservation.Version, unknown), "t", out _, out why));
        StringAssert.Contains("juggling", why);

        string brain = "{\"kind\":\"brain\",\"name\":\"KarenBrainBasic\",\"card\":{\"inputs_version\":" + BrainObservation.Version +
                       ",\"outputs\":[[\"hunt:juggling\"]]},\"mean\":[0],\"std\":[1],\"hidden\":[],\"heads\":[{\"out\":1,\"w\":[0],\"b\":[0]}]}";
        Assert.IsFalse(ModelPack.TryParse(Pack(2, BrainObservation.Version, brain), "t", out _, out why));
        StringAssert.Contains("hunt:juggling", why);

        ModelPack.TrustedKeyOverride = null;
        if (PackKeys.Public == null)
        {
            Assert.IsFalse(ModelPack.TryParse(good, "t", out _, out why), "a build with no key trusts no pack");
            StringAssert.Contains("no pack key", why);
        }
    }

    // A pack signed by tools/learn/pack.py (pure-Python RSA) verifies here, and not once changed.
    [Test]
    public void Pack_SignedInPython_VerifiesInTheGame()
    {
        ModelPack.TrustedKeyOverride = new RSAParameters
        {
            Modulus = System.Convert.FromBase64String(PackFixture.Modulus),
            Exponent = System.Convert.FromBase64String(PackFixture.Exponent)
        };
        Assert.IsTrue(ModelPack.TryParse(PackFixture.Pack, "fixture", out ModelPack p, out string why), why);
        Assert.AreEqual(5, p.PackVersion);
        Assert.AreEqual(0.11f, p.Start.Kind("sight"), 1e-6f);
        Assert.AreEqual(33f, p.Start.AnswerSeconds("fog_reset"), 1e-6f);
        Assert.AreEqual(0.25f, p.Start.SprintShare, 1e-6f);
        Assert.IsFalse(ModelPack.TryParse(PackFixture.Pack.Replace("0.11", "0.12"), "fixture", out _, out _));
    }

    [Test]
    public void Pack_TheFallbackOrder()
    {
        ModelPack.Select(Path.Combine(Path.GetTempPath(), "no_such_pack.kpack"));
        Assert.IsNull(ModelPack.Current, "a pack that fails: the build's own numbers");
        string file = Path.Combine(Path.GetTempPath(), "kehai_test.kpack");
        File.WriteAllText(file, Pack(4, BrainObservation.Version, LedgerStart.Hand().ToPartJson()));
        ModelPack.Select(file);
        Assert.IsNotNull(ModelPack.Current);
        Assert.AreEqual(4, ModelPack.Current.PackVersion);
        File.Delete(file);
    }
}
