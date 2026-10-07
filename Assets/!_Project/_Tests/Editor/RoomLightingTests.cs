using NUnit.Framework;
using UnityEngine;

// Each room's lights light that room and nothing through its walls (RoomLighting), on rendering
// layers URP actually keeps.
public class RoomLightingTests
{
    static readonly int[] RoomBits =
    {
        RoomLighting.Outside, RoomLighting.SalesFloor, RoomLighting.Lobby, RoomLighting.Stockroom, RoomLighting.StaffRoom,
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
}
