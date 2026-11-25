using Kehai.Eval;
using NUnit.Framework;
using UnityEngine;

// The test players' counters to the decoy throw and the swinging door (0.5.2 Phase 9, point 3).
// The door wait needs a real door and her in view, so the mini store check covers it; the aim is
// pure and tested here.
public class Kehai052BotCounterTests
{
    static float Open(Vector3 _) => 10f;

    [Test]
    public void ADecoy_IsThrownAwayFromTheHide()
    {
        Vector3 me = Vector3.zero, hide = new Vector3(0f, 0f, 3f);
        Assert.IsTrue(SimulatedPlayer.DecoyAim(me, hide, Open, out Vector3 way));
        Assert.AreEqual(Vector3.back, way, "straight away from the hide lands furthest from it");
    }

    [Test]
    public void AWallInTheWay_PicksTheNextBestWay()
    {
        Vector3 me = Vector3.zero, hide = new Vector3(0f, 0f, 3f);
        Assert.IsTrue(SimulatedPlayer.DecoyAim(me, hide, d => d == Vector3.back ? 1f : 10f, out Vector3 way));
        Assert.AreNotEqual(Vector3.back, way, "not into the wall a step behind");
        Assert.Less(way.z, 0f, "still away from the hide");
    }

    [Test]
    public void BoxedIn_ThrowsNothing()
    {
        Assert.IsFalse(SimulatedPlayer.DecoyAim(Vector3.zero, new Vector3(0f, 0f, 3f), _ => 1.5f, out _),
                       "with nowhere to throw it, it would land at its feet and give it away");
    }

    [Test]
    public void OnlyWaysThatLandItFurtherOff_Count()
    {
        // Only the way towards the hide is open: no throw.
        Vector3 hide = new Vector3(0f, 0f, 3f);
        Assert.IsFalse(SimulatedPlayer.DecoyAim(Vector3.zero, hide, d => d == Vector3.forward ? 10f : 0.5f, out _));
    }

    [Test]
    public void TheAim_IsTheSameEveryTime()
    {
        Vector3 me = new Vector3(2f, 0f, -1f), hide = new Vector3(-3f, 0f, 4f);
        SimulatedPlayer.DecoyAim(me, hide, Open, out Vector3 a);
        SimulatedPlayer.DecoyAim(me, hide, Open, out Vector3 b);
        Assert.AreEqual(a, b, "no random draw: a shift's random stream is left alone");
    }

    [Test]
    public void OnlyProducts_AreDecoys()
    {
        var go = new GameObject("product");
        try
        {
            var item = go.AddComponent<Item>();
            item.type = ItemType.Stock;
            Assert.IsTrue(SimulatedPlayer.IsDecoy(item));
            item.type = ItemType.Flashlight;
            Assert.IsFalse(SimulatedPlayer.IsDecoy(item), "the torch stays in hand");
            item.type = ItemType.Mop;
            Assert.IsFalse(SimulatedPlayer.IsDecoy(item), "a tool would leave a lost-tool job");
            Assert.IsFalse(SimulatedPlayer.IsDecoy(null));
        }
        finally { Object.DestroyImmediate(go); }
    }
}
