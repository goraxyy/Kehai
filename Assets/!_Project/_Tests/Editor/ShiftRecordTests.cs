using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kehai;
using Kehai.Aiko;
using NUnit.Framework;
using UnityEngine;

// The shift record: the file the report page reads must parse whatever the names and
// sentences contain, the analysis must count what happened, and the page must keep the
// data inside its script tag. Plus the house rule that Aiko's name is never abbreviated.
public class ShiftRecordTests
{
    static ShiftRecording Sample()
    {
        var r = new ShiftRecording
        {
            ShiftNumber = 3, PlayerName = "Employee \"#0417\"", AikoRung = "F_Blink",
            StartedAt = "2026-09-26 05:38", Length = 12f, ClockedOut = true
        };
        for (int i = 0; i <= 60; i++)
        {
            var f = new ShiftFrame
            {
                T = i * 0.2f, Player = new Vector2(40f + i * 0.1f, -150f), PlayerYaw = 90f, Energy = 1f - i / 100f,
                AikoPresent = true, Aiko = new Vector2(60f - i * 0.2f, -150f), AikoSees = i > 30,
                Guess = new Vector2(45f, -150f), GuessConfidence = i == 10 ? float.NaN : 0.4f
            };
            f.Customers.Add(new PersonState { Id = 1, At = new Vector2(50f, -140f), State = i < 30 ? CustomerMark.Asking : CustomerMark.Following, Bay = 7 });
            f.Customers.Add(new PersonState { Id = 2, At = new Vector2(70f, -125f), State = CustomerMark.Queueing, Wait = i * 0.2f, Bay = -1 });
            f.Spills.Add(new Vector2(55f, -160f));
            f.EmptyBays.Add(7);
            f.Bins.Add(new Vector4(38f, -124f, 3, 5));
            f.Props.Add(new Vector4((float)PropKind.Fog, 50f, -150f, 3f));
            r.Frames.Add(f);
        }
        var at = new Vector2(50f, -140f);
        r.Events.Add(new ShiftEvent { T = 1f, Kind = "customer", Who = "asked", At = at, HasPlace = true, Text = "A customer asked where the \"Soft Drinks\" are — Aisle 1." });
        r.Events.Add(new ShiftEvent { T = 2f, Kind = "job", Who = "gave directions", Text = "You walked a customer to Aisle 1." });
        r.Events.Add(new ShiftEvent { T = 3f, Kind = "sound", Who = "you", At = at, HasPlace = true, Radius = 10.5f, Text = "running" });
        r.Events.Add(new ShiftEvent { T = 6f, Kind = nameof(StoryKind.Seen), Who = GameNames.Antagonist, At = at, HasPlace = true, Text = GameNames.Antagonist + " spotted you in Aisle 1." });
        r.Events.Add(new ShiftEvent { T = 7f, Kind = nameof(StoryKind.Store), Who = GameNames.Antagonist, Text = GameNames.Antagonist + " over the speakers: \"This is a formal conversation.\"" });
        r.Events.Add(new ShiftEvent { T = 8f, Kind = nameof(StoryKind.Plan), Who = GameNames.Antagonist, Text = GameNames.Antagonist + " is emptying a shelf you've already filled — the Bakery." });
        r.Events.Add(new ShiftEvent { T = 9f, Kind = nameof(StoryKind.Chase), Who = GameNames.Antagonist, Text = GameNames.Antagonist + " is chasing you!" });
        r.Events.Add(new ShiftEvent { T = 10f, Kind = nameof(StoryKind.Chase), Who = GameNames.Antagonist, Text = GameNames.Antagonist + " caught you." });
        r.Events.Add(new ShiftEvent { T = 11f, Kind = nameof(StoryKind.Warning), Who = GameNames.Antagonist, Text = "a line\nwith a tab\tand a backslash \\ and </script>" });
        r.CustomerWants[1] = "Cola \"Zero\"";
        return r;
    }

    [Test]
    public void RecordingJson_ParsesWhateverTheTextContains()
    {
        ShiftRecording r = Sample();
        string json = r.ToJson(null, ShiftAnalysis.Of(r));
        Dictionary<string, object> d = MiniJson.ParseObject(json);
        Assert.IsNotNull(d, "the recording isn't valid JSON:\n" + json.Substring(0, Mathf.Min(400, json.Length)));
        Assert.AreEqual("Employee \"#0417\"", d["player"]);
        Assert.AreEqual(61, ((List<object>)d["frames"]).Count);
        Assert.AreEqual(r.Events.Count, ((List<object>)d["events"]).Count);
        Assert.AreEqual("Cola \"Zero\"", ((Dictionary<string, object>)d["wants"])["1"]);
        var last = (Dictionary<string, object>)((List<object>)d["events"]).Last();
        Assert.AreEqual(r.Events.Last().Text, last["s"]);
        var findings = (List<object>)((Dictionary<string, object>)d["analysis"])["findings"];
        Assert.IsNotEmpty(findings);
    }

    [Test]
    public void Analysis_CountsWhatHappened()
    {
        ShiftAnalysis a = ShiftAnalysis.Of(Sample());
        Assert.AreEqual(1, a.Spotted);
        Assert.AreEqual(1, a.Chases);
        Assert.AreEqual(1, a.Catches);
        Assert.AreEqual(1, a.Warnings);
        Assert.AreEqual(1, a.AskedForHelp);
        Assert.AreEqual(1, a.DirectionsGiven);
        Assert.That(a.Tricks.ContainsKey("emptying a shelf you've already filled"));
        Assert.Greater(a.LongestTillWait, 10f);
        Assert.Greater(a.SeenSeconds, 5f);
        Assert.Less(a.ClosestDistance, 5f);
        Assert.That(a.Findings.Any(f => f.Contains("spotted you 1 time")), string.Join("\n", a.Findings));
    }

    [Test]
    public void ReportPage_KeepsTheDataInsideItsScriptTag()
    {
        ShiftRecording r = Sample();
        string html = ShiftReportHtml.Build(r.ToJson(null, ShiftAnalysis.Of(r)), 3);
        Assert.AreEqual(2, Regex.Matches(html, "</script>").Count, "a sentence closed the page's script early");
        Match m = Regex.Match(html, "<script id='data' type='application/json'>(.*?)</script>", RegexOptions.Singleline);
        Assert.IsTrue(m.Success);
        Assert.IsNotNull(MiniJson.ParseObject(m.Groups[1].Value), "the page's embedded data doesn't parse");
        Assert.IsFalse(html.Contains("__DATA__") || html.Contains("__TITLE__") || html.Contains("__ANTAGONIST__"));
    }

    // "Aiko", never AIKO or A.I.K.O. — in the game's text and in the docs.
    [Test]
    public void AikosName_IsNeverAbbreviated()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        var files = Directory.GetFiles(Path.Combine(Application.dataPath, "!_Project"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(root, "*.md", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "tools"), "*.*", SearchOption.AllDirectories)
                .Where(p => (p.EndsWith(".md") || p.EndsWith(".py") || p.EndsWith(".swift"))
                         && !p.Contains(".venv") && !p.Contains("site-packages")));
        var shouting = new Regex(@"\bAIKO\b|A\.I\.K\.O");
        var found = new List<string>();
        foreach (string file in files)
        {
            if (file.EndsWith(nameof(ShiftRecordTests) + ".cs")) continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (shouting.IsMatch(lines[i])) found.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.IsEmpty(found, string.Join("\n", found));
    }
}
