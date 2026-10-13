using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Each room's lights light that room and nothing through its walls (RoomLighting), on rendering
// layers URP actually keeps.
public class RoomLightingTests
{
    static readonly int[] RoomBits =
    {
        RoomLighting.Outside, RoomLighting.SalesFloor, RoomLighting.Lobby, RoomLighting.Stockroom, RoomLighting.StaffRoom,
        RoomLighting.Spare[0], RoomLighting.Spare[1],
    };

    // URP drops a light's layers that aren't named in Tags and Layers. The rooms were once on
    // layers 8-12, which aren't, and every room was left lit by nothing but the ambient.
    [Test]
    public void TheRoomsLayers_AreOnesUrpKeeps()
    {
        Assert.IsTrue(RoomLighting.LayersDefined,
            $"defined rendering layers 0x{RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue():X}, rooms need 0x{RoomLighting.AllRooms:X}");
    }

    [Test]
    public void EachRoom_HasItsOwnLayer_AndMovingThingsKeepTheDefault()
    {
        int seen = 0;
        foreach (int bit in RoomBits)
        {
            Assert.IsTrue(Mathf.IsPowerOfTwo(bit), $"0x{bit:X} is one layer");
            Assert.AreEqual(0, seen & bit, $"0x{bit:X} is shared");
            Assert.AreEqual(0u, (uint)bit & RoomLighting.Moving, "a room is on the default layer");
            seen |= bit;
        }
        Assert.AreEqual(RoomLighting.AllRooms, seen);
    }

    [Test]
    public void AWallBetweenTwoRooms_IsLitFromBothSides()
    {
        // The stockroom wall, z = -175.5.
        int mask = RoomLighting.MaskFor(new Bounds(new Vector3(50f, 1.5f, -175.5f), new Vector3(4f, 3f, 0.2f)));
        Assert.AreEqual(RoomLighting.SalesFloor | RoomLighting.Stockroom, mask);
    }

    [Test]
    public void AShelfInTheMiddleOfTheShop_IsOnTheSalesFloorAlone()
    {
        int mask = RoomLighting.MaskFor(new Bounds(new Vector3(55f, 1f, -150f), new Vector3(4f, 2f, 1f)));
        Assert.AreEqual(RoomLighting.SalesFloor, mask);
    }

    // ---- the walled rooms of the store itself ----

    static StoreMap Store()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene("Assets/!_Project/_Core/Scenes/SampleScene.unity", OpenSceneMode.Single);
        return StoreMap.Current;
    }

    // The storage room and the cold room are both the stockroom, and the staff room has a room
    // behind it; on one layer each pair, one room's lights lit the other through the wall.
    [Test]
    public void TwoRoomsInOnePartOfTheBuilding_EachHaveTheirOwnLayer()
    {
        StoreMap map = Store();
        int storage = RoomLighting.BitAt(new Vector3(35f, 1f, -182f), map), cold = RoomLighting.BitAt(new Vector3(70f, 1f, -182f), map);
        int staff = RoomLighting.BitAt(new Vector3(18f, 1f, -140f), map), behind = RoomLighting.BitAt(new Vector3(18f, 1f, -120f), map);
        Assert.AreNotEqual(storage, cold, "storage and cold room");
        Assert.AreNotEqual(staff, behind, "the two staff rooms");
        foreach (int bit in new[] { storage, cold, staff, behind })
            Assert.IsTrue(Mathf.IsPowerOfTwo(bit) && bit != RoomLighting.Outside, $"0x{bit:X} is one indoor layer");
    }

    // A wall between the two staff rooms runs up to the outside wall: it's lit by the two rooms,
    // not by the sun, which lit the staff room through it.
    [Test]
    public void AWallThatMeetsTheOutsideWall_IsntOutside()
    {
        StoreMap map = Store();
        int mask = RoomLighting.MaskFor(new Bounds(new Vector3(17.75f, 1.5f, -130.7f), new Vector3(14.5f, 3f, 0.2f)), map);
        int staff = RoomLighting.BitAt(new Vector3(18f, 1f, -140f), map), behind = RoomLighting.BitAt(new Vector3(18f, 1f, -120f), map);
        Assert.AreEqual(staff | behind, mask);
    }

    // The west wall stands half a metre inside the area table's line: its inside face is the
    // staff room's, lit by the staff room's lights, and only its outside face is the street's.
    [Test]
    public void AnOutsideWall_IsLitByTheRoomInsideIt_AsWellAsOutside()
    {
        StoreMap map = Store();
        int mask = RoomLighting.MaskFor(new Bounds(new Vector3(10f, 2f, -137.5f), new Vector3(0.03f, 4f, 5f)), map);
        int staff = RoomLighting.BitAt(new Vector3(18f, 1f, -140f), map);
        Assert.AreEqual(RoomLighting.Outside | staff, mask);
    }
}
