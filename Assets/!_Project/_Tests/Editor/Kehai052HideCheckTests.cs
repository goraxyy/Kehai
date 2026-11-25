using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.2, Phase 9: the hide checks (fridge I8, locker, kneel I9) and the swinging door.
//
// The pick comes only from what she sensed: with her belief fixed, moving you from one chiller to
// the other never changes which she checks, or her random stream. With no reason, no roll at all.
public class Kehai052HideCheckInTheStoreTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    StoreMap map;
    HidingPlace[] chillers;
    PlayerPresence player;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        HidingPlaceAuthoring.Apply(EditorSceneManager.GetActiveScene());
        map = StoreMap.Rebuild();
        Physics.SyncTransforms();
        chillers = HidingPlace.All
            .Where(p => p.Usable && !p.requiresCrouch && StoreMap.AreaAt(HideCheck.Centre(p)) == "Cold storage")
            .OrderBy(p => HideCheck.Centre(p).x).ThenBy(p => HideCheck.Centre(p).z).ToArray();
        // The store's own employee is spawned in play; a stand-in body, as the 0.5.1 hiding tests use.
        var person = new GameObject("hide check employee");
        var controller = person.AddComponent<CharacterController>();
        controller.radius = 0.4f;
        controller.height = 1.8f;
        controller.center = Vector3.up * 0.9f;
        player = person.AddComponent<PlayerPresence>();
        typeof(PlayerPresence).GetMethod("Awake", Private)?.Invoke(player, null);
    }

    [OneTimeTearDown]
    public void RemoveStandIn()
    {
        if (player != null) Object.DestroyImmediate(player.gameObject);
    }

    KarenContext Context(BeliefGrid belief, int seed) =>
        new KarenContext { Config = new KarenConfig { seed = seed }, Rng = new KarenRng(seed), Map = map, Belief = belief, NowOverride = 100f };

    void PutYouIn(HidingPlace place)
    {
        Vector3 floor = HideCheck.Centre(place);
        floor.y = place.transform.TransformPoint(place.interior.min).y;
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = floor + Vector3.up * 0.05f;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
    }

    BeliefGrid ClankBeside(HidingPlace place)
    {
        var belief = new BeliefGrid(map);
        List<int> beside = HideCheck.CellsBeside(map, place);
        Assert.IsNotEmpty(beside, place.name + " has her map's cells beside it");
        Vector3 at = beside.Select(c => map.CellPosition[c]).Aggregate(Vector3.zero, (a, b) => a + b) / beside.Count;
        belief.Apply(new Observation(SenseChannel.Hearing, at, 0.9f, 1.2f, "test: a clank beside " + place.name));
        return belief;
    }

    [Test]
    public void ThePick_NeverChanges_WhenYouMoveBetweenChillers()
    {
        Assert.GreaterOrEqual(chillers.Length, 2, "two cold-room places you can hide in");
        Assert.IsNotNull(player, "the store's employee");
        HidingPlace heard = chillers[0], other = chillers[1];
        BeliefGrid belief = ClankBeside(heard);
        Assert.Greater(HideCheck.Reason(Context(belief, 1), heard), HideCheck.MinReason, "the clank gives her a reason");

        var picks = new List<string>();
        var next = new List<float>();
        foreach (HidingPlace youIn in new[] { heard, other, heard })
        {
            PutYouIn(youIn);
            KarenContext c = Context(belief, 11);
            HidingPlace pick = HideCheck.Pick(c, HideCheck.Centre(heard), HideCheck.Around, null);
            picks.Add(pick != null ? pick.name : "none");
            next.Add(c.Rng.Value);
        }
        TestContext.WriteLine("picks: " + string.Join(", ", picks));
        Assert.That(picks.Distinct().Count(), Is.EqualTo(1), "where you are never moves her pick");
        Assert.That(next.Distinct().Count(), Is.EqualTo(1), "nor her random stream");
    }

    [Test]
    public void NoReason_NoRoll()
    {
        Assert.GreaterOrEqual(chillers.Length, 1);
        var belief = new BeliefGrid(map);
        // She has looked: nothing beside any chiller.
        var cells = chillers.SelectMany(p => HideCheck.CellsBeside(map, p)).Distinct().ToList();
        belief.ApplySweep(cells, cells.Select(_ => 1f).ToList());
        KarenContext c = Context(belief, 5);
        foreach (HidingPlace p in chillers) Assert.Less(HideCheck.Reason(c, p), HideCheck.MinReason, p.name);
        Assert.IsNull(HideCheck.Pick(c, HideCheck.Centre(chillers[0]), 50f, null), "nothing to suspect");
        Assert.AreEqual(new KarenRng(5).Value, c.Rng.Value, "and no roll: the shift's random stream is untouched");
    }

    [Test]
    public void APlaceSheJustChecked_IsLeftAlone()
    {
        Assert.GreaterOrEqual(chillers.Length, 1);
        KarenContext c = Context(ClankBeside(chillers[0]), 3);
        c.HideChecked[chillers[0]] = c.Now - 10f;
        Assert.IsTrue(HideCheck.CheckedLately(c, chillers[0]));
        HidingPlace pick = HideCheck.Pick(c, HideCheck.Centre(chillers[0]), 1f, null);
        Assert.AreNotEqual(chillers[0], pick, "checked 10 s ago");
        c.HideChecked[chillers[0]] = c.Now - HideCheck.RecheckSeconds - 1f;
        Assert.IsFalse(HideCheck.CheckedLately(c, chillers[0]));
    }

    [Test]
    public void EveryChiller_HasADoorSheCanOpen_AndRacksAreKneeled()
    {
        foreach (HidingPlace p in chillers) Assert.IsNotNull(HideCheck.DoorOf(p), p.name + ": its door");
        Assert.IsTrue(HidingPlace.All.Any(p => p.Usable && p.requiresCrouch), "a rack to kneel at");
    }
}

public class Kehai052HideCheckRuleTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void TheChance_GrowsWithTheReason_AndIsNeverCertain()
    {
        Assert.AreEqual(0f, HideCheck.Chance(HideCheck.MinReason - 0.001f), "below a reason: never");
        Assert.AreEqual(HideCheck.MinReason * HideCheck.ChancePerReason, HideCheck.Chance(HideCheck.MinReason), 1e-5f);
        Assert.Less(HideCheck.Chance(0.1f), HideCheck.Chance(0.2f));
        Assert.AreEqual(HideCheck.MaxChance, HideCheck.Chance(1f), 1e-5f);
        Assert.Less(HideCheck.MaxChance, 1f, "never certain");
        Assert.AreEqual(1.5f, HideCheck.Reach, "Q24: within 1.5 m");
    }

    [SetUp] public void Empty() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    static HingeDoor Door(Vector3 centre, bool moving)
    {
        var door = new GameObject("door").AddComponent<HingeDoor>();
        typeof(HingeDoor).GetField("closedCentre", Private).SetValue(door, centre);
        typeof(HingeDoor).GetField("isMoving", Private).SetValue(door, moving);
        return door;
    }

    [Test]
    public void ADoorSeenSwinging_PointsPastIt_AwayFromHer()
    {
        HingeDoor door = Door(new Vector3(0f, 1f, 0f), moving: true);
        var trace = new Trace { Kind = TraceKind.DoorOpened, Position = new Vector3(0.5f, 0f, 0f), Source = door };
        Vector3 eye = new Vector3(0f, 1.6f, -5f);
        Assert.IsTrue(Sensorium.SwingingDoor(trace, eye, out Vector3 beyond, out Vector3 through));
        Assert.AreEqual(Sensorium.BeyondDoor, beyond.z, 1e-4f, "2 m past the door, on the far side from her");
        Assert.AreEqual(0f, beyond.x, 1e-4f);
        Assert.AreEqual(Vector3.forward, through);
    }

    [Test]
    public void AStillDoor_IsNoSwingingDoor()
    {
        HingeDoor door = Door(Vector3.up, moving: false);
        var trace = new Trace { Kind = TraceKind.DoorOpened, Position = Vector3.zero, Source = door };
        Assert.IsFalse(Sensorium.SwingingDoor(trace, new Vector3(0f, 1.6f, -5f), out _, out _));
        var footprint = new Trace { Kind = TraceKind.WetFootprint, Position = Vector3.zero, Source = Door(Vector3.up, true) };
        Assert.IsFalse(Sensorium.SwingingDoor(footprint, new Vector3(0f, 1.6f, -5f), out _, out _), "only a door trace");
    }

    [Test]
    public void ShuttingADoor_LeavesATrace_OnlyWhileItSwings()
    {
        HingeDoor door = Door(Vector3.up, moving: true);
        int before = TraceRegistry.All.Count;
        typeof(TraceRegistry).GetMethod("OnDoor", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { door, false });
        Trace shut = TraceRegistry.All.Skip(before).Single();
        Assert.AreEqual(TraceKind.DoorOpened, shut.Kind);
        Assert.AreEqual(TraceRegistry.ShutLifetime, shut.Lifetime, 1e-5f);
        Assert.Less(TraceRegistry.ShutLifetime, TraceRegistry.OpenedLifetime);
        shut.Resolved = true;
        TraceRegistry.Prune();
    }
}
