using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kehai;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// The clip finder: markers close together become one moment, a moment scores its markers'
// weights (more with a chase or a catch), its rolls stay inside the shift, and each rule
// that writes a marker fires when it should and not when it shouldn't.
public class ClipMarkerTests
{
    static ClipMarker M(string id, float t, float end = float.NaN) =>
        new ClipMarker { Id = id, T = t, End = float.IsNaN(end) ? t : end, Value = float.NaN, Text = id };

    static List<ClipMoment> Build(IEnumerable<ClipMarker> markers, float length = 600f, IEnumerable<Vector2> chases = null, IEnumerable<ShiftEvent> events = null) =>
        ClipMoments.Build(markers.ToList(), (chases ?? new Vector2[0]).ToList(), (events ?? new ShiftEvent[0]).ToList(), length);

    // ---- the table ---------------------------------------------------------------------------

    [Test]
    public void TheTableHasEveryMarkerWithItsWeight()
    {
        var expected = new Dictionary<string, int>
        {
            { "blink_move", 10 }, { "catch", 10 }, { "near_miss", 9 }, { "escape", 8 }, { "found_blind", 8 },
            { "blackout", 8 }, { "possessed", 8 }, { "learned", 7 }, { "undone_work", 7 }, { "pa_call", 6 },
            { "prop_trick", 6 }, { "clock_refused", 6 }, { "loud_mistake", 6 }, { "tell_then_trick", 5 },
            { "shift_review", 5 }, { "karen_stuck", 4 }, { "customer_chaos", 3 }
        };
        foreach (var pair in expected)
        {
            Assert.IsNotNull(ClipMarkers.Get(pair.Key), pair.Key);
            Assert.AreEqual(pair.Value, ClipMarkers.Get(pair.Key).Weight, pair.Key);
        }
        Assert.IsTrue(ClipMarkers.Get("manual_good").AlwaysKept);
        Assert.IsTrue(ClipMarkers.Get("manual_bug").AlwaysKept);
        CollectionAssert.Contains(ClipMarkers.Get("manual_bug").Tags, "bug");
    }

    // ---- merging -------------------------------------------------------------------------------

    [Test]
    public void MarkersCloserThanTheGapBecomeOneMoment()
    {
        var moments = Build(new[] { M("near_miss", 10f), M("found_blind", 16f), M("learned", 30f) });
        Assert.AreEqual(2, moments.Count);
        ClipMoment first = moments.Single(m => m.Markers.Count == 2);
        CollectionAssert.AreEquivalent(new[] { "near_miss", "found_blind" }, first.Markers.Select(m => m.Id).ToArray());
        Assert.AreEqual("learned", moments.Single(m => m.Markers.Count == 1).Markers[0].Id);
    }

    [Test]
    public void ALongMarkerIsMeasuredFromItsEnd()
    {
        var moments = Build(new[] { M("blackout", 10f, 25f), M("pa_call", 31f) });
        Assert.AreEqual(1, moments.Count, "31 s is 6 s after the lights came back");
        Assert.AreEqual(10f - ClipMarkers.Get("blackout").PreRoll, moments[0].Start, 1e-4f);
        Assert.AreEqual(31f + ClipMarkers.Get("pa_call").PostRoll, moments[0].End, 1e-4f);
    }

    [Test]
    public void AVeryLongMarkerStretchesItsMomentOnlySoFar()
    {
        var moments = Build(new[] { M("blackout", 10f, 300f), M("near_miss", 200f) });
        Assert.AreEqual(2, moments.Count, "a five-minute blackout doesn't swallow the shift");
        ClipMoment dark = moments.Single(m => m.Markers[0].Id == "blackout");
        Assert.AreEqual(10f + ClipMarkers.MaxSpan + ClipMarkers.Get("blackout").PostRoll, dark.End, 1e-4f);
        Assert.AreEqual(300f, dark.Markers[0].End, "the marker still says how long it lasted");
    }

    [Test]
    public void AMomentNeverRunsLongerThanAClip()
    {
        var busy = Enumerable.Range(0, 41).Select(i => M("pa_call", i * 5f)).ToList();
        var moments = Build(busy, length: 400f);
        Assert.Greater(moments.Count, 1);
        Assert.IsTrue(moments.All(m => m.End - m.Start <= ClipMarkers.MaxMoment + 1e-4f), "a moment ran long");
        Assert.AreEqual(41, moments.Sum(m => m.Markers.Count), "every marker lands in a moment");
    }

    // ---- scoring -------------------------------------------------------------------------------

    [Test]
    public void AMomentScoresItsWeights_AndAChaseOrCatchMultipliesThem()
    {
        Assert.AreEqual(17f, Build(new[] { M("near_miss", 100f), M("found_blind", 104f) })[0].Score, 1e-4f);

        var chased = Build(new[] { M("near_miss", 100f), M("found_blind", 104f) }, chases: new[] { new Vector2(98f, 101f) })[0];
        Assert.AreEqual(17f * ClipMarkers.ChaseBoost, chased.Score, 1e-4f);
        Assert.IsTrue(chased.Chase);
        CollectionAssert.Contains(chased.Tags, "chase");

        Assert.AreEqual(10f * ClipMarkers.ChaseBoost, Build(new[] { M("catch", 200f) })[0].Score, 1e-4f);
        Assert.IsFalse(Build(new[] { M("near_miss", 100f) }, chases: new[] { new Vector2(300f, 320f) })[0].Chase, "a chase elsewhere doesn't count");
    }

    [Test]
    public void TheRollsStayInsideTheShift()
    {
        var early = Build(new[] { M("catch", 2f) }, length: 100f)[0];
        Assert.AreEqual(0f, early.Start);
        Assert.AreEqual(2f + ClipMarkers.Get("catch").PostRoll, early.End, 1e-4f);

        var late = Build(new[] { M("blink_move", 99f), M("shift_review", 100f) }, length: 100f)[0];
        Assert.AreEqual(100f, late.End);
        Assert.AreEqual(99f - ClipMarkers.Get("blink_move").PreRoll, late.Start, 1e-4f);
    }

    [Test]
    public void BestFirst_WeakMomentsDropped_ManualOnesAlwaysKept()
    {
        var moments = Build(new[] { M("customer_chaos", 10f), M("manual_bug", 50f), M("near_miss", 100f), M("catch", 200f) });
        CollectionAssert.AreEqual(new[] { "catch", "near_miss", "manual_bug" }, moments.Select(m => m.Markers[0].Id).ToArray());
        Assert.IsTrue(moments[2].Kept);
        Assert.IsTrue(moments.All(m => m.Score >= ClipMarkers.MinScore || m.Kept));
    }

    [Test]
    public void SubjectsAndTagsComeFromTheMarkers()
    {
        var m = Build(new[] { M("possessed", 20f), M("pa_call", 24f) })[0];
        CollectionAssert.AreEqual(new[] { "karen", "customer" }, m.Subjects);
        CollectionAssert.IsSubsetOf(new[] { "possession", "trick", "pa", "voice" }, m.Tags);
    }

    [Test]
    public void TheCaptionSeedIsTheNarratorLinesInside()
    {
        var events = new[]
        {
            new ShiftEvent { T = 5f, Kind = nameof(StoryKind.Plan), Text = "before the moment" },
            new ShiftEvent { T = 11f, Kind = nameof(StoryKind.Plan), Text = GameNames.Antagonist + " is emptying a shelf." },
            new ShiftEvent { T = 12f, Kind = "sound", Text = "footsteps" },
            new ShiftEvent { T = 12.5f, Kind = "job", Text = "You mopped a spill." },
            new ShiftEvent { T = 13f, Kind = nameof(StoryKind.Guess), Text = "a guess" },
            new ShiftEvent { T = 13.5f, Kind = nameof(StoryKind.Guess), Text = "a guess" },
            new ShiftEvent { T = 14f, Kind = nameof(StoryKind.Warning), Text = "Warning: a shelf rattles." },
            new ShiftEvent { T = 40f, Kind = nameof(StoryKind.Chase), Text = "after the moment" },
        };
        var m = Build(new[] { M("near_miss", 12f) }, events: events)[0];
        CollectionAssert.AreEqual(new[] { GameNames.Antagonist + " is emptying a shelf.", "a guess", "Warning: a shelf rattles." }, m.CaptionSeed);
    }

    // ---- the file --------------------------------------------------------------------------------

    [Test]
    public void TheMarkersFileParses_BestMomentFirst()
    {
        var r = new ShiftRecording { ShiftNumber = 3, StartedAt = "2026-09-30 10:00", Length = 300f, ClockedOut = true, KarenRung = "F" };
        r.Markers.Add(M("near_miss", 40f));
        r.Markers.Add(new ClipMarker { Id = "blackout", T = 100f, End = 130f, Value = 30f, Text = "The lights were out for \"30\" seconds." });
        r.Markers.Add(M("catch", 200f));
        List<ClipMoment> moments = ClipMoments.Build(r);
        r.Moments = moments;

        Dictionary<string, object> file = MiniJson.ParseObject(ClipMoments.FileJson(r, "shift_03_20260930_100000", moments));
        Assert.IsNotNull(file, "the markers file doesn't parse");
        Assert.AreEqual(GameNames.Game, file.GetString("game"));
        Assert.AreEqual("shift_03_20260930_100000", file.GetString("stem"));
        Assert.AreEqual(3, ((IList)file["markers"]).Count);
        var list = (IList)file["moments"];
        Assert.AreEqual(3, list.Count);
        var best = (Dictionary<string, object>)list[0];
        Assert.AreEqual(1d, best.GetNumber("rank"));
        Assert.AreEqual(15d, best.GetNumber("score"), 1e-6);   // the catch

        Dictionary<string, object> report = MiniJson.ParseObject(r.ToJson(null, null));
        Assert.IsNotNull(report, "the report data doesn't parse with markers in it");
        Assert.AreEqual(3, ((IList)report["moments"]).Count);
    }

    // ---- the rules ---------------------------------------------------------------------------------

    static ShiftFrame Frame(float t, Vector2 player, Vector2 karen, bool sees = false, bool chasing = false,
                            KarenBody.Mood mood = KarenBody.Mood.Calm, Vector2? guess = null, float confidence = 0f) =>
        new ShiftFrame
        {
            T = t, Player = player, KarenPresent = true, Karen = karen, KarenSees = sees, Chasing = chasing,
            KarenMood = (byte)mood, Guess = guess ?? new Vector2(999f, 999f), GuessConfidence = confidence
        };

    static (ClipWatch watch, List<ClipMarker> markers, List<Vector2> chases) Watch()
    {
        var markers = new List<ClipMarker>();
        var chases = new List<Vector2>();
        return (new ClipWatch(markers, chases), markers, chases);
    }

    static string[] Ids(List<ClipMarker> markers) => markers.Select(m => m.Id).ToArray();

    [Test]
    public void AChaseThatEndsWithoutACatchIsAnEscape()
    {
        var (w, markers, chases) = Watch();
        for (float t = 0f; t <= 30f; t += 0.1f)
            w.Frame(Frame(t, Vector2.zero, new Vector2(20f, 0f), chasing: t >= 5f && t < 25f));
        CollectionAssert.AreEqual(new[] { "escape" }, Ids(markers));
        Assert.AreEqual(25f, markers[0].End, 0.11f);
        Assert.AreEqual(25f - ClipMarkers.EscapeLead, markers[0].T, 0.11f);
        Assert.AreEqual(1, chases.Count);
    }

    [Test]
    public void ACaughtChaseIsACatch_NotAnEscape()
    {
        var (w, markers, _) = Watch();
        for (float t = 0f; t <= 30f; t += 0.1f)
        {
            if (Mathf.Abs(t - 25f) < 0.05f) w.Record(t, "CAUGHT", null, "CAUGHT written warning #1", Vector2.zero);
            w.Frame(Frame(t, Vector2.zero, new Vector2(1f, 0f), sees: true, chasing: t >= 5f && t < 25f));
        }
        CollectionAssert.AreEqual(new[] { "catch" }, Ids(markers));
    }

    [Test]
    public void ANearMissIsCloseAndUnseen()
    {
        var (w, markers, _) = Watch();
        w.Frame(Frame(10f, Vector2.zero, new Vector2(2.5f, 0f)));
        w.Frame(Frame(11f, Vector2.zero, new Vector2(2f, 0f)));                  // still cooling down
        w.Frame(Frame(40f, Vector2.zero, new Vector2(1.5f, 0f), sees: true));    // she saw you: not a miss
        w.Frame(Frame(60f, Vector2.zero, new Vector2(4f, 0f)));                  // too far
        CollectionAssert.AreEqual(new[] { "near_miss" }, Ids(markers));
        Assert.AreEqual(10f, markers[0].T);
    }

    [Test]
    public void FoundBlindIsAConfidentGuessWithoutSight()
    {
        var (w, markers, _) = Watch();
        w.Frame(Frame(10f, Vector2.zero, new Vector2(30f, 0f), guess: new Vector2(1.5f, 0f), confidence: 0.3f));
        w.Frame(Frame(20f, Vector2.zero, new Vector2(30f, 0f), guess: new Vector2(5f, 0f), confidence: 0.9f));
        w.Frame(Frame(30f, Vector2.zero, new Vector2(30f, 0f), guess: new Vector2(1.5f, 0f), confidence: 0.6f));
        CollectionAssert.AreEqual(new[] { "found_blind" }, Ids(markers));
        Assert.AreEqual(30f, markers[0].T);
    }

    [Test]
    public void HuntingInPlaceIsStuck_HuntingOnTheMoveIsNot()
    {
        var (w, markers, _) = Watch();
        for (float t = 0f; t <= 7f; t += 0.1f) w.Frame(Frame(t, Vector2.zero, new Vector2(20f, 0f), mood: KarenBody.Mood.Hunt));
        CollectionAssert.AreEqual(new[] { "karen_stuck" }, Ids(markers));

        var (moving, none, _) = Watch();
        for (float t = 0f; t <= 7f; t += 0.1f) moving.Frame(Frame(t, Vector2.zero, new Vector2(20f + t, 0f), mood: KarenBody.Mood.Hunt));
        Assert.IsEmpty(none);
    }

    [Test]
    public void PossessionAndALostGuideAreMarked()
    {
        var (w, markers, _) = Watch();
        ShiftFrame a = Frame(10f, Vector2.zero, new Vector2(30f, 0f));
        a.Customers.Add(new PersonState { Id = 4, State = CustomerMark.Following });
        a.Customers.Add(new PersonState { Id = 5, State = CustomerMark.Shopping });
        ShiftFrame b = Frame(11f, Vector2.zero, new Vector2(30f, 0f));
        b.Customers.Add(new PersonState { Id = 4, State = CustomerMark.LostTheGuide });
        b.Customers.Add(new PersonState { Id = 5, State = CustomerMark.Possessed });
        b.Customers.Add(new PersonState { Id = -7, State = CustomerMark.Fake });
        w.Frame(a);
        w.Frame(b);
        w.Frame(b);
        CollectionAssert.AreEquivalent(new[] { "customer_chaos", "possessed" }, Ids(markers));
    }

    [Test]
    public void EmptyingAShelfYouJustFilledIsUndoneWork()
    {
        var (w, markers, _) = Watch();
        w.Restocked(100f, 7);
        w.Swept(140f, 7, Vector2.zero);
        w.Restocked(100f, 8);
        w.Swept(170f, 8, Vector2.zero);     // 70 s later: too long ago to feel personal
        w.Swept(180f, 9, Vector2.zero);     // a shelf you never touched
        CollectionAssert.AreEqual(new[] { "undone_work" }, Ids(markers));
        Assert.AreEqual(40f, markers[0].Value, 1e-4f);
    }

    [Test]
    public void ASpillWhereYouJustMoppedIsUndoneWork()
    {
        var (w, markers, _) = Watch();
        w.Mopped(50f, new Vector2(10f, 10f));
        w.Record(80f, "EFFECT", null, "[01:20] EFFECT   kick over a bucket at Aisle 3", new Vector2(12f, 10f));
        w.Mopped(100f, new Vector2(10f, 10f));
        w.Record(130f, "EFFECT", null, "[02:10] EFFECT   kick over a bucket at the Stockroom", new Vector2(60f, 10f));   // far away
        CollectionAssert.AreEqual(new[] { "undone_work" }, Ids(markers));
        Assert.AreEqual(80f, markers[0].T);
    }

    [Test]
    public void AWarningFollowedByItsTrickIsMarkedAsOne()
    {
        var (w, markers, _) = Watch();
        w.Story(50f, StoryKind.Warning, "Warning: crates scrape across the floor.", Vector2.zero, true);
        w.Record(55f, "EFFECT", null, "[00:55] EFFECT   crate wall at Aisle 2", Vector2.zero);
        w.Story(100f, StoryKind.Warning, "Warning: a light flickers.", Vector2.zero, true);
        w.Record(111f, "EFFECT", null, "[01:51] EFFECT   kill the light over Aisle 4", Vector2.zero);   // too late
        w.Story(200f, StoryKind.Warning, "Warning: the speakers chime.", Vector2.zero, true);
        w.Record(203f, "EFFECT", null, "[03:23] EFFECT   PA: hello (chime 3.0s before)", Vector2.zero);   // PA lines aren't tricks
        CollectionAssert.AreEqual(new[] { "tell_then_trick" }, Ids(markers));
        Assert.AreEqual(50f, markers[0].T);
        Assert.AreEqual(55f, markers[0].End);
    }

    [Test]
    public void ALoudNoiseSheHeardAndCameToIsALoudMistake()
    {
        var (w, markers, _) = Watch();
        w.Frame(Frame(9f, Vector2.zero, new Vector2(20f, 0f)));
        w.Noise(10f, NoiseKind.Sprint, NoiseAuthor.Player, Vector2.zero);
        w.Story(11.5f, StoryKind.Heard, "Karen heard running.", Vector2.zero, true);
        w.Frame(Frame(12f, Vector2.zero, new Vector2(18.5f, 0f)));
        CollectionAssert.AreEqual(new[] { "loud_mistake" }, Ids(markers));
        Assert.AreEqual(10f, markers[0].T);

        var (quiet, none, _) = Watch();
        quiet.Frame(Frame(9f, Vector2.zero, new Vector2(20f, 0f)));
        quiet.Noise(10f, NoiseKind.Sprint, NoiseAuthor.Customer, Vector2.zero);       // not you
        quiet.Story(11f, StoryKind.Heard, "heard", Vector2.zero, true);
        quiet.Noise(20f, NoiseKind.DroppedItem, NoiseAuthor.Player, Vector2.zero);
        quiet.Story(25f, StoryKind.Heard, "heard", Vector2.zero, true);               // too late
        quiet.Noise(40f, NoiseKind.Sprint, NoiseAuthor.Player, Vector2.zero);
        quiet.Story(41f, StoryKind.Heard, "heard", Vector2.zero, true);
        for (float t = 41f; t < 47f; t += 0.5f) quiet.Frame(Frame(t, Vector2.zero, new Vector2(20f, 0f)));   // she never came
        Assert.IsEmpty(none);
    }

    [Test]
    public void APropTrickLastsUntilItHappens()
    {
        var (w, markers, _) = Watch();
        w.Record(30f, "PLAN", "crate_wall", "PLAN crate_wall", Vector2.zero);
        w.Record(40f, "EFFECT", null, "[00:40] EFFECT   crate wall at Aisle 2", Vector2.zero);
        w.Record(60f, "PLAN", "patrol", "PLAN patrol", Vector2.zero);
        CollectionAssert.AreEqual(new[] { "prop_trick" }, Ids(markers));
        Assert.AreEqual(30f, markers[0].T);
        Assert.AreEqual(40f, markers[0].End);
    }

    [Test]
    public void ABlackoutLastsUntilTheLightsAreBack()
    {
        var (w, markers, _) = Watch();
        w.Lights(10f, false);
        w.Lights(40f, true);
        w.Lights(250f, false);
        w.Finish(300f, true, Vector2.zero);
        CollectionAssert.AreEqual(new[] { "blackout", "blackout", "shift_review" }, Ids(markers));
        Assert.AreEqual(30f, markers[0].Value, 1e-4f);
        Assert.AreEqual(300f, markers[1].End);
        Assert.AreEqual(300f, markers[2].T);
    }

    [Test]
    public void TheSmallerRules()
    {
        var (w, markers, _) = Watch();
        w.Story(5f, StoryKind.Blink, "You blinked — and Karen moved.", Vector2.zero, true);
        w.Story(6f, StoryKind.Blink, "You blinked — and Karen moved.", Vector2.zero, true);    // same blink burst
        w.Story(20f, StoryKind.Learned, "Karen noticed that the fog rattled you.", Vector2.zero, false);
        w.Story(25f, StoryKind.Learned, "Karen noticed that the spill didn't bother you.", Vector2.zero, false);   // cooling down
        w.Pa(30f, "Thank you for your flexibility.");
        w.Pa(35f, "Five.");   // a countdown is one call
        w.PunchRefused(40f, Vector2.zero);
        w.Record(41f, "EFFECT", null, "[00:41] EFFECT   clock-out refused; +2:00 of shift", Vector2.zero);   // the same refusal
        w.CustomerGaveUp(50f, Vector2.zero);
        w.Manual(60f, false, Vector2.zero);
        w.Manual(61f, true, Vector2.zero);
        w.Finish(100f, false, Vector2.zero);   // didn't clock out: no review
        CollectionAssert.AreEqual(new[] { "blink_move", "learned", "pa_call", "clock_refused", "customer_chaos", "manual_good", "manual_bug" }, Ids(markers));
    }
}
