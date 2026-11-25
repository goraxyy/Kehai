using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Sandbox;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// The mini store sandbox (0.5.2, C11.17): the parts that need no scene. The building, her map in
// it, the doors and her tricks there are played for real by MiniStoreCheck.
public sealed class MiniStoreTests
{
    [TearDown]
    public void Cleanup() => StoreFrame.Use(null);

    [Test]
    public void AWall_IsCutAroundItsDoorways()
    {
        List<Vector2> pieces = MiniStore.Pieces(-15f, 15f, new[] { -6f, 2.1f, 9f, 2.1f });
        Assert.AreEqual(3, pieces.Count);
        Assert.That(pieces[0].y, Is.EqualTo(-7.05f).Within(0.001f));
        Assert.That(pieces[1].x, Is.EqualTo(-4.95f).Within(0.001f));
        Assert.That(pieces[1].y, Is.EqualTo(7.95f).Within(0.001f));
        Assert.That(pieces[2].x, Is.EqualTo(10.05f).Within(0.001f));
        Assert.AreEqual(1, MiniStore.Pieces(-15f, 15f, new float[0]).Count, "no doorway: one piece");
    }

    [Test]
    public void ItsRooms_ReadAsTheStoresRooms()
    {
        StoreFrame.Use(MiniStore.ToStore);
        foreach (var (name, store, _) in MiniStore.Rooms)
            Assert.AreEqual(name, StoreMap.AreaAt(MiniStore.Half(new Vector3(store.center.x, 0f, store.center.y))), name);
        // Half size both ways.
        var p = new Vector3(60f, 1f, -150f);
        Vector3 back = MiniStore.ToStore(MiniStore.Half(p));
        Assert.That(Vector3.Distance(p, back), Is.LessThan(0.001f));
        Assert.That(MiniStore.Half(new Vector3(85f, 0f, -110f)).x - MiniStore.Half(new Vector3(10f, 0f, -110f)).x, Is.EqualTo(37.5f).Within(0.001f));
        // Elsewhere a point is itself.
        var far = new Vector3(60f, 0f, -150f);
        Assert.AreEqual(far, MiniStore.ToStore(far));
    }

    [Test]
    public void ItsLamps_AreOnTheBreakersOfThePartsTheyStandFor()
    {
        StoreFrame.Use(MiniStore.ToStore);
        Assert.AreEqual(LightCircuit.West, LightControl.CircuitOf(MiniStore.Half(new Vector3(40f, 5f, -150f))));
        Assert.AreEqual(LightCircuit.East, LightControl.CircuitOf(MiniStore.Half(new Vector3(70f, 5f, -150f))));
        Assert.AreEqual(LightCircuit.Back, LightControl.CircuitOf(MiniStore.Half(new Vector3(30f, 5f, -182f))));
        Assert.AreEqual(LightCircuit.Back, LightControl.CircuitOf(MiniStore.Half(new Vector3(17f, 5f, -130f))));
    }

    [Test]
    public void ThePanelsChoices_BecomeHerLimits()
    {
        var config = new KarenConfig { sandboxNoTricks = true };
        var before = (MiniStoreSettings.Allowed, MiniStoreSettings.Chosen);
        try
        {
            MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.None;
            MiniStoreSettings.ApplyTo(config);
            Assert.IsFalse(config.sandboxNoTricks);
            Assert.IsTrue(config.sandboxPacingOff);
            Assert.IsNotNull(config.sandboxTricks);
            Assert.IsEmpty(config.sandboxTricks);
            Assert.IsTrue(config.sandboxTrickIds.Contains("poll_witnesses"), "the tricks list counts Ask the customers");

            MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.OnlyOne;
            MiniStoreSettings.Chosen = "fog";
            MiniStoreSettings.ApplyTo(config);
            CollectionAssert.AreEquivalent(new[] { "fog" }, config.sandboxTricks);

            MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.All;
            MiniStoreSettings.ApplyTo(config);
            Assert.IsNull(config.sandboxTricks);
        }
        finally { (MiniStoreSettings.Allowed, MiniStoreSettings.Chosen) = before; }
    }
}
