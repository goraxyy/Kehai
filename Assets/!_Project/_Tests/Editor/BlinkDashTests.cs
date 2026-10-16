using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// The blink dash's rules (IDEAS.md, the blink mechanic; Decision/BlinkDash.cs): when it may
// move, and which way — checked without a store.
public class BlinkDashTests
{
    static readonly KarenConfig Config = new KarenConfig();
    const KarenDirector.Phase Build = KarenDirector.Phase.Build, Spike = KarenDirector.Phase.Spike;

    static BlinkDash.Kind Choose(KarenDirector.Phase phase, float gap, int closeIns = 0, bool vanished = false,
                                 bool inRecovery = false, bool chasing = false, bool sees = true, bool watched = true,
                                 float sinceArmed = 10f, float sinceLast = 60f) =>
        BlinkDash.Choose(phase, inRecovery, chasing, sees, watched, gap, closeIns, vanished, sinceArmed, sinceLast, Config);

    [Test]
    public void Calm_NeverDashes()
    {
        foreach (var phase in new[] { KarenDirector.Phase.OffShift, KarenDirector.Phase.Settle, KarenDirector.Phase.Recover })
            Assert.AreEqual(BlinkDash.Kind.None, Choose(phase, 20f), phase.ToString());
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, inRecovery: true));
    }

    [Test]
    public void OnlyWhenSheIsInYourView_AndNotInAChase()
    {
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, sees: false));
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, watched: false));
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, chasing: true));
    }

    [Test]
    public void TheWaveIsToldFirst_AndDashesAreSpaced()
    {
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, sinceArmed: Config.minTellLead * 0.5f), "no dash before the wave's tell has landed");
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 20f, sinceLast: Config.dashCooldown * 0.5f));
    }

    [Test]
    public void Build_ClosesInFromAfar_ThenVanishesWhenNear()
    {
        Assert.AreEqual(BlinkDash.Kind.CloseIn, Choose(Build, Config.dashCloseInMinRange + 5f));
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, Config.dashCloseInMinRange + 5f, closeIns: Config.dashCloseInsPerWave),
                        "at most dashCloseInsPerWave close-ins a wave");
        Assert.AreEqual(BlinkDash.Kind.Vanish, Choose(Build, 7f));
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 7f, vanished: true), "one vanish a wave");
        Assert.AreEqual(BlinkDash.Kind.None, Choose(Build, 2f), "too close to vanish fairly");
    }

    [Test]
    public void Spike_VanishesFirst_ThenClosesIn()
    {
        Assert.AreEqual(BlinkDash.Kind.Vanish, Choose(Spike, 20f));
        Assert.AreEqual(BlinkDash.Kind.CloseIn, Choose(Spike, 20f, vanished: true));
    }

    [Test]
    public void AlongPath_WalksTheCorners()
    {
        var corners = new[] { Vector3.zero, new Vector3(4f, 0f, 0f), new Vector3(4f, 0f, 6f) };
        Assert.AreEqual(new Vector3(2f, 0f, 0f), BlinkDash.AlongPath(corners, 2f));
        Assert.AreEqual(new Vector3(4f, 0f, 3f), BlinkDash.AlongPath(corners, 7f));
        Assert.AreEqual(corners[2], BlinkDash.AlongPath(corners, 50f), "past the end stays at the end");
    }
}

// The shoppers: how often they ask, how many come in, and how fast.
public class CustomerPacingTests
{
    [Test]
    public void Shoppers_AskOneTimeInTen()
    {
        Assert.AreEqual(0.1f, CustomerRequest.AskChance, 1e-6f);
    }

    [Test]
    public void AtMostFive_AndSlowerTheFullerTheStore()
    {
        var spawner = new GameObject("spawner").AddComponent<CustomerSpawner>();
        try
        {
            spawner.maxActiveCustomers = 12;
            Assert.AreEqual(5, spawner.Cap, "the scene can't raise the ceiling");
            spawner.maxActiveCustomers = 3;
            Assert.AreEqual(3, spawner.Cap, "but it can lower it");

            float slowestEmpty = spawner.spawnIntervalRange.y;
            for (int i = 0; i < 20; i++)
                Assert.Greater(spawner.NextDelay(4), slowestEmpty, "a busy store should wait longer than an empty one");
        }
        finally { Object.DestroyImmediate(spawner.gameObject); }
    }
}
