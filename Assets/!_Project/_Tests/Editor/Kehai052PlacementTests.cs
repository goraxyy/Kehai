using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.2, C12.2: the placement fixes of 26 November. A slot directly behind a display one is
// display too (and only that one: the first try marked every deeper slot on the board, 826 store
// display slots became 3,200), and a switched-off interactable can't be used and still blocks
// the aim (a display chiller's shut glass).
public class Kehai052PlacementTests
{
    [SetUp]
    public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    static ShelfSlot Slot(Transform frame, Vector3 local) =>
        new ShelfSlot(null, frame, local, ShelfGrid.Side.Front, new Vector2(0.25f, 0.25f), 0.2f);

    [Test]
    public void OnlyTheSlotDirectlyBehindADisplayOneIsDisplay()
    {
        Transform frame = new GameObject("bay").transform;
        Vector3 outward = ShelfGrid.Outward(ShelfGrid.Side.Front);
        Vector3 across = Vector3.Cross(Vector3.up, outward);
        ShelfSlot front = Slot(frame, Vector3.up * 0.5f + outward * 0.25f);
        ShelfSlot behind = Slot(frame, Vector3.up * 0.5f);
        ShelfSlot besideBehind = Slot(frame, Vector3.up * 0.5f + across * 0.25f);
        ShelfSlot boardAbove = Slot(frame, Vector3.up * 0.9f);
        front.Display = true;

        StoreLayout.MarkBehindDisplay(new List<ShelfSlot> { front, behind, besideBehind, boardAbove });

        Assert.IsTrue(behind.Display, "the slot you'd reach past the display one");
        Assert.IsFalse(besideBehind.Display, "deeper, but beside it: its own column");
        Assert.IsFalse(boardAbove.Display, "another board");
    }

    [Test]
    public void ASwitchedOffDoorCantBeUsedAndStillBlocksTheAim()
    {
        var player = new GameObject("Player").AddComponent<PlayerInteract>();
        player.interactLayer = 1 << 2;
        var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leaf.layer = 2;
        leaf.transform.position = new Vector3(0f, 1f, 1.5f);
        var door = leaf.AddComponent<FurnitureDoor>();
        Physics.SyncTransforms();
        var ray = new Ray(new Vector3(0f, 1f, 0f), Vector3.forward);

        Assert.AreSame(door, player.FindAimedTarget(ray));
        Assert.IsTrue(door.IsHidingDoor);

        door.enabled = false;
        Assert.IsNull(player.FindAimedTarget(ray), "a shut display door isn't usable, and nothing behind it is reached");
        Assert.IsFalse(door.IsHidingDoor, "Karen doesn't wait on a door that won't open");
    }
}
