using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.1, items 1 and 2: hiding places conceal you from a clear eye ray; a seen entry is
// remembered as where she saw you, and the 3 m catch is measured to that, not to you.
//
// Edit mode: OnEnable doesn't run, so HidingPlace.Author registers the place itself, and
// Time.time stands still, so every step here happens "at once" (inside every time window).
public class Kehai051HidingTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    SightSensor eye;
    PlayerPresence player;
    HidingPlace place;
    CharacterController controller;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        eye = new GameObject("eye").AddComponent<SightSensor>();
        eye.transform.position = new Vector3(0f, 1.6f, 0f);
        var person = new GameObject("employee");
        controller = person.AddComponent<CharacterController>();
        controller.radius = 0.5f;
        controller.height = 1.8f;
        controller.center = Vector3.up * 0.9f;
        player = person.AddComponent<PlayerPresence>();
        Awake(player);
        // Interior z 5 to 7: the body (1 m deep) is 80% in at z 5.30.
        place = HidingPlace.Author(new GameObject("cabinet interior"), new Bounds(new Vector3(0f, 1.5f, 6f), new Vector3(4f, 3f, 2f)));
    }

    [TearDown]
    public void Cleanup()
    {
        // Authoring registers places in edit mode; leave none behind for the next class.
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            Object.DestroyImmediate(go);
    }

    static void Awake(PlayerPresence p) => typeof(PlayerPresence).GetMethod("Awake", Private).Invoke(p, null);

    void At(float z) => At(new Vector3(0f, 0f, z));

    void At(Vector3 feet)
    {
        player.transform.position = feet;
        Physics.SyncTransforms();
        eye.Tick(0.02f);
    }

    // A PlayerMotor that says it's crouched, with the controller at crouch height (PlayerMotor.SetCrouch).
    void Crouch(bool crouched)
    {
        if (!player.TryGetComponent(out PlayerMotor motor)) motor = player.gameObject.AddComponent<PlayerMotor>();
        typeof(PlayerMotor).GetField("isCrouching", Private).SetValue(motor, crouched);
        controller.height = crouched ? motor.crouchControllerHeight : 2f;
        controller.center = Vector3.up * controller.height * 0.5f;
        Awake(player);
    }

    [TestCase(0.7999f, false)]
    [TestCase(0.8f, true)]
    [TestCase(0.8001f, true)]
    public void CoverageThreshold_IsInclusive(float fraction, bool hidden) => Assert.AreEqual(hidden, HidingPlace.Conceals(fraction));

    [Test]
    public void Coverage_IsBodyEnvelopeOverlap_NotEyeRayOrFurnitureBounds()
    {
        Bounds body = new Bounds(Vector3.one * 0.5f, Vector3.one);
        Assert.AreEqual(0.8f, HidingPlace.OverlapFraction(body, new Bounds(new Vector3(0.6f, 0.5f, 0.5f), new Vector3(0.8f, 1f, 1f))), 1e-6f);
        At(5.29f);
        Assert.AreEqual(0.79f, place.Coverage(player), 1e-5f);
        Assert.IsTrue(eye.SeesNow);
        At(5.31f);
        Assert.IsTrue(eye.CanSee(player.Head), "open door still has a clear eye ray");
        Assert.IsFalse(eye.SeesNow, "81% of the body is inside the hiding interior");
    }

    [Test]
    public void ControllerHeightAndRotatedVolume_DetermineCoverage()
    {
        place.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        place.interior = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(4f, 1f, 4f));
        controller.height = 1f; controller.center = Vector3.up * 0.5f;
        Assert.AreEqual(1f, place.Coverage(player), 1e-6f);
        controller.height = 2f; controller.center = Vector3.up;
        Assert.AreEqual(0.5f, place.Coverage(player), 1e-6f);
    }

    [Test]
    public void SeenEntry_RemembersVisiblePosition_AndCatchesWithinThreeMetresOfIt()
    {
        At(5.29f);
        Vector3 observed = player.Position;
        At(5.31f);
        Assert.IsTrue(eye.HiddenEntry.Active);
        Assert.AreEqual(observed, eye.HiddenEntry.Position);
        // Measured from her to where she saw you, not to where you are.
        Assert.IsTrue(eye.CanCatchWitnessedHide(observed + Vector3.back * 2.999f));
        Assert.IsFalse(eye.CanCatchWitnessedHide(observed + Vector3.back * 3.001f));
        At(6f);
        Assert.AreEqual(observed, eye.HiddenEntry.Position, "no hidden destination tracking");
        Assert.IsTrue(eye.CanCatchWitnessedHide(observed + Vector3.back * 2.999f), "still hiding in the same place");
    }

    [Test]
    public void MovingTheHiddenPlayer_NeverMovesHerDestination_OrHerCatch()
    {
        // A long rack-like interior, z 5 to 15.
        place.interior = new Bounds(new Vector3(0f, 1.5f, 10f), new Vector3(4f, 3f, 10f));
        At(5.29f);
        Vector3 observed = player.Position;
        At(5.31f);
        Assert.IsTrue(eye.HiddenEntry.Active);
        Vector3 destination = HidingApproach.ClosestReachable(null, eye.HiddenEntry.Position);
        Assert.AreEqual(observed, destination, "her destination is the remembered entry");

        // Deep inside the same place, unseen: the memory and her destination stay put, and
        // so does the catch: 1 m from you but 7.7 m from where she saw you is no catch; 2.9 m
        // from where she saw you is one, though you are 11.6 m away.
        At(14f);
        Assert.IsFalse(eye.SeesNow);
        Assert.AreEqual(observed, eye.HiddenEntry.Position);
        Assert.AreEqual(destination, HidingApproach.ClosestReachable(null, eye.HiddenEntry.Position));
        Assert.IsFalse(eye.CanCatchWitnessedHide(new Vector3(0f, 0f, 13f)));
        Assert.IsTrue(eye.CanCatchWitnessedHide(observed + Vector3.back * 2.9f));

        // The player object teleported far off: still the same destination, and no catch.
        player.transform.position = new Vector3(40f, 0f, -30f);
        Physics.SyncTransforms();
        Assert.IsFalse(eye.CanCatchWitnessedHide(observed));
        Assert.AreEqual(observed, eye.HiddenEntry.Position);
        Assert.AreEqual(destination, HidingApproach.ClosestReachable(null, eye.HiddenEntry.Position));
    }

    [Test]
    public void UnwitnessedEntry_NeverArmsCatch_EvenWithClearEyes()
    {
        At(6f);
        Assert.IsTrue(eye.CanSee(player.Head), "nothing between her eye and yours");
        Assert.IsFalse(eye.SeesNow);
        Assert.IsFalse(eye.HiddenEntry.Active);
        Assert.IsFalse(eye.CanCatchWitnessedHide(player.Position));
        Assert.IsFalse(eye.CanCatchWitnessedHide(eye.transform.position));
    }

    [Test]
    public void UnrelatedLossBehindWall_IsNotWitnessedEntry()
    {
        At(4f);
        var wall = new GameObject("wall").AddComponent<BoxCollider>();
        wall.transform.position = new Vector3(0f, 1.5f, 4.7f);
        wall.size = new Vector3(5f, 3f, 0.1f);
        At(5.29f);
        Assert.IsFalse(eye.SeesNow);
        At(5.31f);
        Assert.IsFalse(eye.HiddenEntry.Active);
    }

    [Test]
    public void EyeLineBrokenJustBeforeConcealment_StillWitnessesTheEntry()
    {
        // Under a rack the board cuts the line to your eyes before 80% of you is under it.
        At(5.0f);
        Assert.IsTrue(eye.SeesNow, "seen at the edge");
        var board = new GameObject("board").AddComponent<BoxCollider>();
        board.transform.position = new Vector3(0f, 1.6f, 4.9f);
        board.size = new Vector3(3f, 0.2f, 0.1f);
        At(5.2f);
        Assert.IsFalse(eye.SeesNow, "the board is between her eyes and yours");
        Assert.Less(place.Coverage(player), HidingPlace.CoveredThreshold);
        At(5.31f);
        Assert.IsTrue(eye.HiddenEntry.Active, "she saw you go in");
        Assert.AreEqual(new Vector3(0f, 0f, 5.0f), eye.HiddenEntry.Position, "remembered where she last saw you");
    }

    [Test]
    public void EscapePermanentlyRevokesCatch_EvenOnAnUnseenReturn()
    {
        At(5.29f); At(5.31f);
        Vector3 remembered = eye.HiddenEntry.Position;
        Assert.IsTrue(eye.CanCatchWitnessedHide(remembered));
        eye.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        At(4f);
        Assert.IsFalse(eye.SeesNow);
        Assert.IsTrue(eye.HiddenEntry.Active, "she can still search the remembered entrance");
        Assert.IsFalse(eye.CanCatchWitnessedHide(remembered), "you left the place");
        At(6f);
        Assert.IsFalse(eye.CanCatchWitnessedHide(remembered), "an unseen return doesn't re-arm it");
        Assert.AreEqual(remembered, eye.HiddenEntry.Position);
    }

    [Test]
    public void ChangingHidingPlaces_RevokesTheCatch()
    {
        var other = HidingPlace.Author(new GameObject("next cupboard"), new Bounds(new Vector3(3f, 1.5f, 6f), new Vector3(2f, 3f, 2f)));
        At(5.29f); At(5.31f);
        Vector3 remembered = eye.HiddenEntry.Position;
        eye.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        At(new Vector3(3f, 0f, 6f));
        Assert.AreSame(other, HidingPlace.At(player, out _));
        Assert.IsFalse(eye.CanCatchWitnessedHide(remembered));
        Assert.IsFalse(eye.CanCatchWitnessedHide(new Vector3(3f, 0f, 4f)));
    }

    [Test]
    public void UnderARack_CrouchedAtEightyPercentHides_StandingNever()
    {
        // A rack's crawl space: 1 m under its lowest board, crouch only.
        // A crouched controller is never shorter than it is wide (0.8 m asked, 1 m with a 0.5 m
        // radius), and it rests with its bottom on the floor: its transform 0.1 m up.
        place.interior = new Bounds(new Vector3(0f, 0.5f, 6f), new Vector3(4f, 1f, 2f));
        place.requiresCrouch = true;
        Crouch(true);
        float lift = Mathf.Max(0f, controller.radius - controller.height * 0.5f);
        At(new Vector3(0f, lift, 5.29f));
        Assert.AreEqual(0.79f, place.Coverage(player), 1e-5f);
        Assert.IsTrue(eye.SeesNow, "79% under: seen");
        At(new Vector3(0f, lift, 5.31f));
        Assert.IsFalse(eye.SeesNow, "81% under, crouched: hidden");
        Assert.IsTrue(eye.HiddenEntry.Active, "and she saw you crouch in");

        // Standing at the same spot is never hidden by a rack, however much of you is in its volume.
        eye.ResetAwareness();
        place.interior = new Bounds(new Vector3(0f, 1.5f, 6f), new Vector3(4f, 3f, 2f));
        Crouch(false);
        At(6f);
        Assert.AreEqual(1f, place.Coverage(player), 1e-5f);
        Assert.IsTrue(eye.SeesNow, "standing: seen");
    }

    [Test]
    public void ResetClearsWitness_AndLowShelfGivesFullExposure()
    {
        At(5.29f); At(5.31f);
        eye.ResetAwareness();
        Assert.IsFalse(eye.HiddenEntry.Active);
        Assert.AreEqual(1f, SightSensor.ExposureOf(true, false, false));
        Assert.AreEqual(0f, SightSensor.ExposureOf(false, true, true));
        var shelf = new GameObject("small shelf").AddComponent<BoxCollider>();
        shelf.transform.position = new Vector3(0f, 0.7f, 4f);
        shelf.size = new Vector3(3f, 1.4f, 0.5f);
        Physics.SyncTransforms();
        Assert.AreEqual(1f, eye.ExposureTo(new Vector3(0f, 1.6f, 5f), new Vector3(0f, 1.2f, 5f), new Vector3(0f, 0.1f, 5f)));
    }

    [Test]
    public void SmallShelf_SameSpeedAsNoShelf_WhenSheSeesOverIt()
    {
        // The player 5 m off, a 1.4 m shelf between: her line to your eyes clears it.
        At(5.29f - 1f);
        float open = eye.DetectNow;
        Assert.Greater(open, 0f);
        var shelf = new GameObject("small shelf").AddComponent<BoxCollider>();
        shelf.transform.position = new Vector3(0f, 0.7f, 3f);
        shelf.size = new Vector3(3f, 1.4f, 0.5f);
        eye.ResetAwareness();
        At(5.29f - 1f);
        Assert.AreEqual(open, eye.DetectNow, 1e-6f, "a shelf she sees over doesn't slow her");
        Assert.AreEqual(1f, eye.Exposure);
    }

    [Test]
    public void Concealed_EvenAtArmsLength_WhereTheDarkCannotHideYou()
    {
        // In the open, at arm's length she sees you whatever the light (SightSensor.Detect's
        // close-range floor). Inside a hiding place, not even there: concealment comes first.
        eye.transform.position = new Vector3(0f, 1.6f, 4.6f);
        At(5.0f);
        Assert.IsTrue(eye.SeesNow, "half in, at arm's length: seen");
        eye.ResetAwareness();
        At(6f);
        Assert.IsTrue(eye.CanSee(player.Head));
        Assert.IsFalse(eye.SeesNow, "all in, at arm's length: hidden");
        Assert.AreEqual(0f, eye.DetectNow);
    }

    [Test]
    public void MemoryExpires_AndDisabledPlaceCannotCatch()
    {
        var memory = new WitnessedHide();
        memory.Remember(place, Vector3.zero, 10f);
        memory.UpdateOccupancy(place, true, 70f);
        Assert.IsFalse(memory.Active);
        memory.Remember(place, Vector3.zero, 10f);
        place.enabled = false;
        memory.UpdateOccupancy(place, true, 11f);
        Assert.IsFalse(memory.Active);
    }

    [Test]
    public void Witnessed_NeedsARecentNearbySighting()
    {
        Vector3 edge = new Vector3(0f, 0f, 5f);
        Assert.IsTrue(WitnessedHide.Witnessed(place, edge, 10f, edge + Vector3.forward * 0.3f, 10.5f));
        Assert.IsFalse(WitnessedHide.Witnessed(place, edge, 10f, edge + Vector3.forward * 0.3f, 10.51f), "too long ago");
        Assert.IsFalse(WitnessedHide.Witnessed(place, edge + Vector3.back * 0.61f, 10f, edge, 10f), "too far from the place");
        Assert.IsFalse(WitnessedHide.Witnessed(place, edge, 10f, edge + Vector3.forward * 1.51f, 10f), "moved too far since");
        Assert.IsFalse(WitnessedHide.Witnessed(null, edge, 10f, edge, 10f));
    }
}

// Item 1 against the real store (the clone has the scene): every hiding place the game
// authors at load, by kind, with room for a body that it would conceal. Prints each one.
public class Kehai051HidingInTheStoreTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    readonly List<HidingPlace> added = new List<HidingPlace>();
    List<HidingPlace> store;
    float radius = 0.5f, standing = 2f, crouched = 0.8f;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var before = new HashSet<HidingPlace>(Object.FindObjectsByType<HidingPlace>(FindObjectsInactive.Include));
        HidingPlaceAuthoring.Apply(EditorSceneManager.GetActiveScene());
        foreach (var p in Object.FindObjectsByType<HidingPlace>(FindObjectsInactive.Include))
            if (!before.Contains(p)) added.Add(p);
        Physics.SyncTransforms();

        var you = Object.FindAnyObjectByType<PlayerPresence>(FindObjectsInactive.Include);
        if (you != null && you.TryGetComponent(out CharacterController cc)) radius = cc.radius * Mathf.Abs(you.transform.lossyScale.x);
        if (you != null && you.TryGetComponent(out PlayerMotor motor)) crouched = motor.crouchControllerHeight;

        store = Object.FindObjectsByType<HidingPlace>(FindObjectsInactive.Include)
            .Where(p => p.transform.root.name != "Models_Island" && p.gameObject.activeInHierarchy).ToList();
        TestContext.WriteLine($"player radius {radius:0.00} m, standing {standing} m, crouched {crouched} m; {store.Count} hiding places in the store");
        foreach (var p in store)
            TestContext.WriteLine($"{Kind(p),-14} {Path(p.transform)}  interior {p.interior.center} size {p.interior.size} scale {p.transform.lossyScale}{(p.requiresCrouch ? " crouch" : "")}");
    }

    [OneTimeTearDown]
    public void Restore()
    {
        foreach (var p in added) if (p != null) Object.DestroyImmediate(p);
        added.Clear();
    }

    static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;

    static string Kind(HidingPlace p)
    {
        if (p.GetComponent<ColdHideInterior>() != null)
        {
            string key = HidingPlaceAuthoring.Key(SceneObjectNames.Original(p.transform)) + HidingPlaceAuthoring.Key(p.name);
            return key.Contains("locker") ? "locker" : key.Contains("cupboard") ? "cupboard" : "cold cabinet";
        }
        if (HidingPlaceAuthoring.IsRack(p.transform)) return "rack";
        string name = HidingPlaceAuthoring.Key(p.name) + "|" + HidingPlaceAuthoring.Key(SceneObjectNames.Original(p.transform));
        if (name.Contains("changingcabin")) return "changing cabin";
        if (name.Contains("cabin")) return "toilet cabin";
        return "other";
    }

    [Test]
    public void EveryKindTheOwnerNamed_IsAHidingPlace()
    {
        var kinds = store.GroupBy(Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var k in kinds) TestContext.WriteLine($"{k.Key}: {k.Value}");
        Assert.That(kinds.GetValueOrDefault("cold cabinet"), Is.GreaterThan(0), "fridge (cold hiding cabinets)");
        Assert.That(kinds.GetValueOrDefault("toilet cabin"), Is.GreaterThan(0), "toilet stalls");
        Assert.AreEqual(2, kinds.GetValueOrDefault("changing cabin"), "the staff room's two changing cabins");
        Assert.That(kinds.GetValueOrDefault("locker") + kinds.GetValueOrDefault("cupboard"), Is.GreaterThan(0), "closets");
        Assert.That(kinds.GetValueOrDefault("rack"), Is.GreaterThan(0), "stock shelving (stockroom racks)");
        Assert.AreEqual(0, kinds.GetValueOrDefault("other"), "only known hiding furniture");
    }

    [Test]
    public void EveryHidingPlace_HasRoomForABodyItConceals()
    {
        var failures = new List<string>();
        foreach (var p in store)
        {
            // The controller is never shorter than it is wide (PlayerPresence.BodyEnvelopeIn).
            float height = Mathf.Max(p.requiresCrouch ? crouched : standing, 2f * radius);
            if (!Fits(p, height, out Vector3 at)) { failures.Add($"{Kind(p)} {Path(p.transform)}: no free spot {height} m tall that it conceals"); continue; }
            TestContext.WriteLine($"{Kind(p),-14} {Path(p.transform)}: a body {height} m tall fits at {at}");
            if (p.requiresCrouch && Fits(p, standing, out Vector3 up))
                failures.Add($"{Kind(p)} {Path(p.transform)}: a standing body fits under it at {up}, so its board is not overhead");
        }
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    // A free upright capsule `height` tall standing on the interior's floor, 80% or more inside it.
    bool Fits(HidingPlace p, float height, out Vector3 found)
    {
        found = Vector3.zero;
        Transform f = p.transform;
        Bounds b = p.interior;
        float sx = Mathf.Max(0.001f, Mathf.Abs(f.lossyScale.x)), sz = Mathf.Max(0.001f, Mathf.Abs(f.lossyScale.z));
        float step = 0.1f;
        float x0 = b.min.x + radius / sx, x1 = b.max.x - radius / sx;
        float z0 = b.min.z + radius / sz, z1 = b.max.z - radius / sz;
        if (x1 < x0) x0 = x1 = b.center.x;
        if (z1 < z0) z0 = z1 = b.center.z;
        for (float x = x0; x <= x1 + 1e-4f; x += step / sx)
        for (float z = z0; z <= z1 + 1e-4f; z += step / sz)
        {
            Vector3 feet = f.TransformPoint(new Vector3(x, b.min.y, z));
            Bounds body = PlayerPresence.EnvelopeIn(f, feet + Vector3.up * height * 0.5f, radius, height);
            if (!HidingPlace.Conceals(HidingPlace.OverlapFraction(body, b))) continue;
            if (Blocked(feet, height)) continue;
            found = feet;
            return true;
        }
        return false;
    }

    bool Blocked(Vector3 feet, float height)
    {
        float r = radius - 0.02f;
        Vector3 bottom = feet + Vector3.up * (r + 0.05f), top = feet + Vector3.up * Mathf.Max(r + 0.05f, height - r);
        foreach (Collider c in Physics.OverlapCapsule(bottom, top, r, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is CharacterController) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;   // loose stock
            return true;
        }
        return false;
    }
}
