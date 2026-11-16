using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051HidingTests
{
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
        typeof(PlayerPresence).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(player, null);
        place = HidingPlace.Author(new GameObject("cabinet interior"), new Bounds(new Vector3(0f, 1.5f, 6f), new Vector3(4f, 3f, 2f)));
    }

    void At(float z)
    {
        player.transform.position = new Vector3(0f, 0f, z);
        Physics.SyncTransforms();
        eye.Tick(0.02f);
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
    public void SeenEntry_RemembersVisiblePosition_AndCatchesWithinThreeMetres()
    {
        At(5.29f);
        Vector3 observed = player.Position;
        At(5.31f);
        Assert.IsTrue(eye.HiddenEntry.Active);
        Assert.AreEqual(observed, eye.HiddenEntry.Position);
        Assert.IsTrue(eye.CanCatchWitnessedHide(player.Position + Vector3.back * 3f));
        Assert.IsFalse(eye.CanCatchWitnessedHide(player.Position + Vector3.back * 3.001f));
        At(6f);
        Assert.AreEqual(observed, eye.HiddenEntry.Position, "no hidden destination tracking");
    }

    [Test]
    public void UnwitnessedEntry_NeverArmsCatch_EvenWithClearEyes()
    {
        At(6f);
        Assert.IsFalse(eye.SeesNow);
        Assert.IsFalse(eye.HiddenEntry.Active);
        Assert.IsFalse(eye.CanCatchWitnessedHide(player.Position));
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
    public void EscapePermanentlyRevokesCatch_EvenOnAnUnseenReturn()
    {
        At(5.29f); At(5.31f);
        Vector3 remembered = eye.HiddenEntry.Position;
        eye.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        At(4f);
        Assert.IsTrue(eye.HiddenEntry.Active, "she can still search the remembered entrance");
        Assert.IsFalse(eye.HiddenEntry.CatchEligible);
        At(6f);
        Assert.IsFalse(eye.CanCatchWitnessedHide(player.Position));
        Assert.AreEqual(remembered, eye.HiddenEntry.Position);
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
}
