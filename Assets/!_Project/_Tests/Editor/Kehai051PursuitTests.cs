using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// 0.5.1, item 13: one 60 s pursuit/search window from her last confirmed sighting, and item 14's
// stun gate where it meets the hunt.
public class Kehai051PursuitTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const float Window = 60f;
    readonly List<GameObject> objects = new List<GameObject>();
    readonly List<KarenDirector> directors = new List<KarenDirector>();

    [SetUp]
    public void Setup()
    {
        if (!Application.isPlaying) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [TearDown]
    public void Cleanup()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
        foreach (KarenDirector d in directors) d.Dispose();   // its panic index listens to static events
        directors.Clear();
    }

    GameObject New(string name)
    {
        var go = new GameObject(name);
        objects.Add(go);
        return go;
    }

    static object Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void SetField(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);

    // ---- the window ------------------------------------------------------------------------

    [Test]
    public void TheWindowRunsFromTheLastConfirmedSighting_AndOnlyASightingRenewsIt()
    {
        var p = new PostSightPursuit();
        p.Start(100f, 100.5f, Window);
        Assert.AreEqual(160f, p.Deadline, 1e-4f, "60 s from the sighting that started the hunt, not from the plan");
        Assert.IsTrue(p.Active(159.9f));

        p.Heard(new Vector3(4f, 0f, 2f), 150f);
        p.Heard(new Vector3(5f, 0f, 2f), 158f);
        Assert.AreEqual(160f, p.Deadline, 1e-4f, "noises steer the search, they never move the deadline");
        Assert.IsFalse(p.Active(160f), "repeated noises can't keep a hunt alive past its window");

        p.ObserveSight(158f, 158.1f, Window);
        Assert.AreEqual(218f, p.Deadline, 1e-4f, "a new sighting near the deadline renews it");
        Assert.IsEmpty(p.Noises, "a new sighting starts the search afresh");

        p.ObserveSight(157f, 159f, Window);
        p.ObserveSight(159f, 160f, Window);
        Assert.AreEqual(218f, p.Deadline, 1e-4f, "an old or stale sighting renews nothing");
    }

    [Test]
    public void AnEndedHuntCantBeRestartedByItsOwnSighting_AndAShiftResetClearsAll()
    {
        var p = new PostSightPursuit();
        p.Start(10f, 10f, Window);
        p.BeginSearch();
        p.Searched(Vector3.zero);
        p.Ending("test");
        Assert.AreEqual("test", p.EndWhy);
        p.Clear();
        Assert.IsFalse(p.Active(11f));
        Assert.IsFalse(p.Searching);
        p.Start(10f, 20f, Window);
        Assert.IsFalse(p.Active(21f), "the sighting that started the last hunt can't start another");
        p.Start(20f, 20f, Window);
        Assert.IsTrue(p.Active(21f), "a newer sighting can");

        p.Reset();
        Assert.IsFalse(p.Active(21f));
        Assert.IsFalse(p.WasSearched(Vector3.zero));
        p.Start(10f, 10f, Window);
        Assert.IsTrue(p.Active(11f), "a new shift forgets which sightings were used");
    }

    // ---- the run and the search --------------------------------------------------------------

    [TestCase(true, 0f, 0.2f, false, false, false, 0f, 30f, Pursue.Next.Run, TestName = "Pursuit: seeing you, she runs")]
    [TestCase(false, 3f, 8f, false, false, false, 0f, 50f, Pursue.Next.Run, TestName = "Pursuit: lost, not there yet, she runs on")]
    [TestCase(false, 2f, 1f, false, false, false, 0f, 50f, Pursue.Next.Search, TestName = "Pursuit: an empty destination starts a search, not the end")]
    [TestCase(false, 2f, 6f, true, false, false, 0f, 50f, Pursue.Next.Search, TestName = "Pursuit: as close as she can get counts as getting there")]
    [TestCase(false, 20f, 9f, false, true, false, 0f, 30f, Pursue.Next.Search, TestName = "Pursuit: searching goes on inside the window")]
    [TestCase(false, 61f, 9f, false, true, false, 0f, 0f, Pursue.Next.End, TestName = "Pursuit: the window over, the hunt ends")]
    [TestCase(true, 0f, 9f, false, true, false, 0f, 0f, Pursue.Next.Run, TestName = "Pursuit: seen again at the deadline, she runs")]
    [TestCase(true, 0f, 3f, false, false, true, 0f, 50f, Pursue.Next.End, TestName = "Pursuit: a recovery still ends it")]
    [TestCase(true, 0f, 3f, false, true, false, 0.9f, 50f, Pursue.Next.End, TestName = "Pursuit: you went outside, she can't follow")]
    public void TheHuntRunsThenSearches_AndEndsOnlyByTheRules(bool sees, float lostFor, float toGoal, bool cantGetCloser, bool searching,
                                                             bool recovery, float outside, float left, Pursue.Next expected)
    {
        Assert.AreEqual(expected, Pursue.Rule(sees, lostFor, toGoal, cantGetCloser, searching, recovery, outside, left, Window, out string why), why);
        Assert.IsFalse(string.IsNullOrEmpty(why), "every outcome says why");
    }

    [Test]
    public void TheSearch_FollowsNoisesThenGuessesThenTheSightingThenHerBelief()
    {
        var p = new PostSightPursuit();
        p.Start(100f, 100f, Window);
        Vector3 seen = new Vector3(0f, 0f, 0f), peak = new Vector3(30f, 0f, 30f);
        var guesses = new List<(string, Vector3, float)>
        {
            ("to the time clock", new Vector3(20f, 0f, 0f), 0.6f),
            ("turned back", new Vector3(-20f, 0f, 0f), 0.3f),
            ("unlikely", new Vector3(0f, 0f, 40f), 0.01f),
        };

        p.Heard(new Vector3(10f, 0f, 10f), 101f);
        p.Heard(new Vector3(12f, 0f, -8f), 104f);
        PursuitSearch.Spot s = PursuitSearch.Choose(p, guesses, seen, peak, 105f);
        Assert.AreEqual(new Vector3(12f, 0f, -8f), s.At, "the newest noise first");
        Assert.IsTrue(s.Fresh);
        p.Searched(s.At);
        Assert.AreEqual(new Vector3(10f, 0f, 10f), PursuitSearch.Choose(p, guesses, seen, peak, 105f).At, "then the older one");
        p.Searched(new Vector3(10f, 0f, 10f));

        Assert.AreEqual(new Vector3(20f, 0f, 0f), PursuitSearch.Choose(p, guesses, seen, peak, 105f).At, "then her likeliest guess");
        p.Searched(new Vector3(21f, 0f, 1f));   // within 3 m counts
        Assert.AreEqual(new Vector3(-20f, 0f, 0f), PursuitSearch.Choose(p, guesses, seen, peak, 105f).At, "then the next guess");
        p.Searched(new Vector3(-20f, 0f, 0f));
        Assert.AreEqual(seen, PursuitSearch.Choose(p, guesses, seen, peak, 105f).At, "a guess she doubts is skipped; then where she saw you");
        p.Searched(seen);
        Assert.AreEqual(peak, PursuitSearch.Choose(p, guesses, seen, peak, 105f).At, "and last her belief, which always has somewhere");
        Assert.AreEqual(peak, PursuitSearch.Choose(p, guesses, seen, peak, 200f).At, "old noises stop steering");
    }

    [Test]
    public void ThePursuitAndItsSearch_ReadNothingButHerSenses()
    {
        string root = Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts");
        foreach (string file in new[] { "Core/PostSightPursuit.cs", "Decision/PursuitSearch.cs" })
        {
            string code = File.ReadAllText(Path.Combine(root, file));
            StringAssert.DoesNotMatch(@"\b(PlayerPresence|PlayerMotor|CharacterController|Camera\.main|TruePlayerPosition|transform)\b", code, file);
        }
    }

    // ---- the brain: the stun gate and the hunt's clock ----------------------------------------

    KarenBrain Brain(out KarenStun stun)
    {
        var go = New("Karen");
        var brain = go.AddComponent<KarenBrain>();
        var bodyGo = New("KAREN_Body");
        var body = bodyGo.AddComponent<KarenBody>();
        body.GetComponent<NavMeshAgent>().enabled = false;
        var sight = New("Gaze").AddComponent<SightSensor>();
        typeof(KarenBody).GetProperty("Sight").SetValue(body, sight);
        stun = bodyGo.AddComponent<KarenStun>();
        typeof(KarenBody).GetProperty("Stun").SetValue(body, stun);
        stun.GetType().GetMethod("Awake", Private).Invoke(stun, null);
        typeof(KarenBrain).GetProperty("Body").SetValue(brain, body);
        var ledger = new KarenLedger(brain.config);
        var director = new KarenDirector(brain.config, ledger);
        directors.Add(director);
        typeof(KarenBrain).GetProperty("Director").SetValue(brain, director);
        return brain;
    }

    Item Thrown()
    {
        var go = New("thrown item");
        go.AddComponent<BoxCollider>();
        go.AddComponent<Rigidbody>().useGravity = false;
        var item = go.AddComponent<Item>();
        item.GetType().GetMethod("Awake", Private).Invoke(item, null);
        item.lastAuthor = NoiseAuthor.Player;
        return item;
    }

    [Test]
    public void AStun_StopsThePlanButNotTheHunt_SpendsItsWindow_AndWakesToDecide()
    {
        KarenBrain brain = Brain(out KarenStun stun);
        float now = Time.time;
        brain.Pursuit.Start(now - 10f, now, Window);
        SetField(brain, "chasing", true);
        float deadline = brain.Pursuit.Deadline;
        SetField(brain, "decideNow", false);

        Assert.IsTrue(stun.TryImpact(Thrown(), 9f, 9f));
        Assert.IsTrue((bool)Call(brain, "StunGate"), "stunned: the gate holds every action");
        Assert.IsTrue(brain.IsChasing, "a stun doesn't end the hunt (no second recovery, no new start)");
        Assert.AreEqual(deadline, brain.Pursuit.Deadline, "a stun doesn't extend the window: it spends it");
        Assert.IsNull(brain.CurrentPlan);

        stun.ResetState();
        Assert.IsFalse((bool)Call(brain, "StunGate"));
        Assert.IsTrue(Field<bool>(brain, "decideNow"), "on waking she decides again from what she senses now");
    }

    [Test]
    public void WithTheWindowOverAndNothingInSight_TheHuntEnds_AndSaysWhy()
    {
        KarenBrain brain = Brain(out _);
        float now = Time.time;
        brain.Pursuit.Start(now - 30f, now, Window);
        SetField(brain, "chasing", true);
        Call(brain, "KeepHuntClock");
        Assert.IsTrue(brain.IsChasing, "30 s in, the hunt goes on");

        brain.Pursuit.Start(now - 61f, now, Window);
        Call(brain, "KeepHuntClock");
        Assert.IsFalse(brain.IsChasing, "61 s after the last sighting the hunt is over");
        Assert.IsFalse(brain.Pursuit.Active(now));
        Assert.IsTrue(Field<bool>(brain, "decideNow"));
        bool said = false;
        for (int i = 0; i < brain.Log.Count; i++) said |= brain.Log[i].Text.Contains("the hunt ends") && brain.Log[i].Text.Contains("since she last saw you");
        Assert.IsTrue(said, "the thought log says why the hunt ended");
    }

    // The gate comes before everything she does, on both paths (with and without belief).
    [Test]
    public void TheStunGate_SitsAboveEveryAction_InTheBrainTick()
    {
        string code = File.ReadAllText(Path.Combine(Application.dataPath, "!_Project/_Game/AI/Scripts/Core/KarenBrain.cs"));
        int update = code.IndexOf("        void Update()");
        int end = code.IndexOf("        void TickDirector(", update);
        string tick = code.Substring(update, end - update);
        int gate = tick.IndexOf("bool stunned = StunGate();");
        Assert.Greater(gate, 0);
        StringAssert.Contains("if (!stunned && !PursueWitnessedHide()) SimpleGuard(dt);", tick);
        int held = tick.IndexOf("if (stunned) { ExpireClosures(); return; }");
        Assert.Greater(held, gate);
        foreach (string act in new[] { "Dash.TickLurk(Ctx)", "if (PursueWitnessedHide())", "Decide(forced)", "RunPlan(dt)" })
            Assert.Greater(tick.IndexOf(act), held, act + " runs only past the gate");
    }
}
