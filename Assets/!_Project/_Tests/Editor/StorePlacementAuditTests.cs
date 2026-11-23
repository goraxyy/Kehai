using System;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public class StorePlacementAuditTests
{
    const float FloorY = .4f;
    PlayerInteract player;
    NavMeshSurface surface;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        player = new GameObject("Player").AddComponent<PlayerInteract>();
        var motor = player.gameObject.AddComponent<PlayerMotor>();
        var body = motor.GetComponent<CharacterController>();
        body.center = Vector3.up;
        body.height = 2f;
        body.radius = .28f;
        player.transform.position = new Vector3(0f, FloorY, 3f);
        player.interactLayer = 1 << 2; // Geometry stays in the default layer, the switch is aimed at separately.
        surface = new GameObject("Navigation").AddComponent<NavMeshSurface>();
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = 1;
        var ignore = player.gameObject.AddComponent<NavMeshModifier>();
        ignore.ignoreFromBuild = true;
    }

    [TearDown]
    public void TearDown()
    {
        var data = surface != null ? surface.navMeshData : null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (data != null) UnityEngine.Object.DestroyImmediate(data);
    }

    static GameObject Box(string name, Vector3 centre, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = centre;
        box.transform.localScale = size;
        return box;
    }

    void Bake()
    {
        Physics.SyncTransforms();
        surface.BuildNavMesh();
        Assert.IsNotNull(surface.navMeshData);
    }

    static RoomLightSwitch Switch(float height)
    {
        var go = Box("Room switch", new Vector3(0f, FloorY + height, 0f), new Vector3(.14f, .2f, .1f));
        go.layer = 2;
        var button = go.AddComponent<RoomLightSwitch>();
        button.Configure("Staff room", Array.Empty<Light>());
        Physics.SyncTransforms();
        return button;
    }

    [Test]
    public void MeasuresTheColliderFromTheRaisedStandingFloor()
    {
        Box("Floor", new Vector3(0f, FloorY - .25f, 1f), new Vector3(10f, .5f, 10f));
        Bake();
        var result = new PlacementProbe(player, surface).Check(Switch(1.72f));
        Assert.That(result.Height, Is.EqualTo(1.72f).Within(.01f));
        Assert.AreEqual("above eye level", result.HeightStatus);
        Assert.AreEqual("reachable", result.Approach);
        Assert.That(result.Standing.Value.y, Is.EqualTo(FloorY).Within(.02f));
        Assert.AreEqual("Switch off Staff room lights", result.Prompt);
        Assert.AreEqual("present", result.PromptStatus);
    }

    [Test]
    public void StandingAndNavMeshAloneDoNotPassAnOccludedSwitch()
    {
        Box("Floor", new Vector3(0f, FloorY - .25f, 1f), new Vector3(10f, .5f, 10f));
        Bake();
        var button = Switch(1.35f);
        Box("Solid barrier added after the bake", new Vector3(0f, FloorY + 1f, .28f), new Vector3(8f, 2f, .10f));
        Physics.SyncTransforms();
        var result = new PlacementProbe(player, surface).Check(button);
        Assert.AreEqual("within limit", result.HeightStatus);
        Assert.AreEqual("unreachable", result.Approach);
    }

    [Test]
    public void RequiresACompletePathFromSpawn()
    {
        Box("Switch island", new Vector3(0f, FloorY - .25f, .4f), new Vector3(5f, .5f, 2f));
        Box("Spawn island", new Vector3(0f, FloorY - .25f, 3.5f), new Vector3(5f, .5f, 2f));
        Bake();
        var result = new PlacementProbe(player, surface).Check(Switch(1.35f));
        Assert.AreEqual("within limit", result.HeightStatus);
        Assert.AreEqual("unreachable", result.Approach);
        Assert.IsFalse(result.MissingEvidence);
    }

    [Test]
    public void MissingNavigationDoesNotReportUnreachableOrAssumeZeroFloor()
    {
        var result = new PlacementProbe(player, surface).Check(Switch(1.35f));
        Assert.IsNull(result.Height);
        Assert.IsNull(result.Approach);
        Assert.IsTrue(result.MissingEvidence);
        Assert.AreEqual("present", result.PromptStatus);
    }

    [Test]
    public void WallBoxHeightUsesTheFloorInFrontOfItsOwnMount()
    {
        Box("Raised floor", new Vector3(0f, FloorY - .25f, 1f), new Vector3(10f, .5f, 10f));
        Box("Wall", new Vector3(0f, 1.5f, -.1f), new Vector3(5f, 3f, .2f));
        Box("Plinth under mount", new Vector3(0f, FloorY + .4f, .08f), new Vector3(.4f, .8f, .2f));
        Physics.SyncTransforms();
        Vector3 mount = KarenFixtures.WallBoxAt(new Vector3(0f, 0f, .04f), Vector3.forward);
        Assert.That(mount.y, Is.EqualTo(FloorY + KarenFixtures.BoxHeight).Within(.01f));
        Assert.That(mount.z, Is.EqualTo(0f).Within(.01f));
    }

    [Test]
    public void DistinguishesConditionalPromptFromMissingAndUnreadablePrompts()
    {
        var result = new PlacementProbe.Result();
        PlacementProbe.ReadPrompt(result, () => "", conditional: true, mayBeSilent: true);
        Assert.AreEqual("conditional", result.PromptStatus);
        PlacementProbe.ReadPrompt(result, () => " ", conditional: false);
        Assert.AreEqual("missing", result.PromptStatus);
        PlacementProbe.ReadPrompt(result, () => "", conditional: true);
        Assert.AreEqual("missing", result.PromptStatus, "a condition can still require a prompt");
        PlacementProbe.ReadPrompt(result, () => throw new InvalidOperationException(), conditional: false);
        Assert.AreEqual("unmeasured", result.PromptStatus);
    }

    // 23 November: a shelf slot counts its board's height, not the middle of the pack on it.
    [TestCase(1.50f, "within limit")]
    [TestCase(1.65f, "above eye level")]
    public void ShelfSlotsAreMeasuredAtTheirBoard(float board, string status)
    {
        Box("Floor", new Vector3(0f, FloorY - .25f, 1f), new Vector3(10f, .5f, 10f));
        Bake();
        var frame = new GameObject("Bay").transform;
        frame.position = new Vector3(0f, FloorY, 0f);
        var slot = new ShelfSlot(null, frame, new Vector3(0f, board, 0f), false, new Vector2(.25f, .25f), .34f);
        var result = new PlacementProbe(player, surface).CheckSlot(slot);
        Assert.That(result.Height, Is.EqualTo(board).Within(.02f), "the board, not the pack's middle (" + (board + .17f) + " m)");
        Assert.AreEqual(status, result.HeightStatus);
    }

    [Test]
    public void UpperBoardLeavesRoomForTallestDisplayPack()
    {
        const float halfBoard = .01f, aimPadding = .04f;
        float centre = StoreLayouts.UpperBoard + halfBoard + (ProductLook.MaxDisplayHeight + aimPadding) * .5f;
        Assert.LessOrEqual(centre, PlacementProbe.MaximumHeight);
        Assert.Greater(StoreLayouts.UpperBoard - StoreLayouts.SharedBoard - 2f * halfBoard, ProductLook.MaxDisplayHeight);
    }
}
