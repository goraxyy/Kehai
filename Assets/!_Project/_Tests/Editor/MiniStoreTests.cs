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
    public void TheThirdBin_MovesOutOfTheDoorway_TheArtsBinsOnlyWithTheArt()
    {
        Assert.AreEqual(2.03f, MiniStore.LobbyShift("Lobby_Furnished_Recycling_03", false), 1e-4f);
        Assert.AreEqual(0f, MiniStore.LobbyShift("Lobby_Furnished_Recycling_01", false));
        Assert.AreEqual(-2.28f, MiniStore.LobbyShift("Lobby_Furnished_Recycling_01", true), 1e-4f);
        Assert.AreEqual(1.46f, MiniStore.LobbyShift("Lobby_Furnished_Recycling_02", true), 1e-4f);
        Assert.AreEqual(0f, MiniStore.LobbyShift("Counter_Facing_01", true));
        Assert.AreEqual("Bay_-40_25", MiniStoreRoadside.KeyFor("Tall shelf", new Vector3(-4f, 0f, 2.5f)));
        Assert.AreEqual("Checkout_Art", MiniStoreRoadside.KeyFor("Checkout_03", Vector3.zero));
        Assert.IsNull(MiniStoreRoadside.KeyFor("Lobby_Furnished_Recycling_01", Vector3.zero));
    }

    [Test]
    public void ADoorway_IsFilledUpToItsDoor()
    {
        // A 3 m automatic pair in a 3.2 m doorway: 10 cm of jamb each side.
        List<Vector2> jambs = MiniStore.Jambs(0f, 3.2f, -1.5f, 1.5f);
        Assert.AreEqual(2, jambs.Count);
        Assert.That(jambs[0].x, Is.EqualTo(-1.6f).Within(0.001f));
        Assert.That(jambs[0].y, Is.EqualTo(-1.5f).Within(0.001f));
        Assert.That(jambs[1].x, Is.EqualTo(1.5f).Within(0.001f));
        Assert.That(jambs[1].y, Is.EqualTo(1.6f).Within(0.001f));
        // Off-centre: the whole leftover on one side.
        jambs = MiniStore.Jambs(0f, 1.7f, -0.85f, 0.65f);
        Assert.AreEqual(1, jambs.Count);
        Assert.That(jambs[0].x, Is.EqualTo(0.65f).Within(0.001f));
        // A door as wide as its doorway, or wider: nothing.
        Assert.AreEqual(0, MiniStore.Jambs(0f, 2f, -1.002f, 1.1f).Count);
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

    // The time clock shows the night's time (25 November): 22:00 at the start, 06:00 at the end.
    [Test]
    public void TheTimeClock_ShowsTheNightsTime()
    {
        Assert.AreEqual("22:00", TimeClockDisplay.Format(WallClockHands.NightTime(300f, 300f, true)));
        Assert.AreEqual("02:00", TimeClockDisplay.Format(WallClockHands.NightTime(300f, 150f, true)));
        Assert.AreEqual("05:59", TimeClockDisplay.Format(WallClockHands.NightTime(300f, 0.5f, true)));
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
