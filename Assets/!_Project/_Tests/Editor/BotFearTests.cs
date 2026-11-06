using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kehai.Eval;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// The reacting simulated players (SimulatedPlayer, BotFear): who frightens easily and how
// fear behaves — and that they know only what the observation tells them.
public class BotFearTests
{
    static float Sum(BotTraits t) => t.Susceptibility.Sum();

    [Test]
    public void Profiles_AreOrdered_ForEverySeed()
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var efficient = BotTraits.For(PlayerProfile.Efficient, seed);
            var skittish = BotTraits.For(PlayerProfile.Skittish, seed);
            var reckless = BotTraits.For(PlayerProfile.Reckless, seed);
            var cautious = BotTraits.For(PlayerProfile.Cautious, seed);

            Assert.Greater(skittish[Stimulus.TellSound], efficient[Stimulus.TellSound], $"seed {seed}: a rattle should frighten the skittish more");
            Assert.Greater(skittish[Stimulus.Blackout], efficient[Stimulus.Blackout], $"seed {seed}");
            Assert.Less(Sum(reckless), Sum(efficient), $"seed {seed}: the reckless player is the hardest to frighten");
            Assert.Greater(cautious[Stimulus.Vanish], efficient[Stimulus.Vanish], $"seed {seed}: the cautious player minds her dashes");
            Assert.Greater(reckless[Stimulus.Chase], reckless[Stimulus.TellSound] * 5f, "only being chased gets to the reckless one");
        }
    }

    [Test]
    public void Thresholds_MatchTheProfiles()
    {
        Assert.Greater(BotTraits.For(PlayerProfile.Reckless, 1).HideAt, 1f, "the reckless player never hides");
        Assert.Less(BotTraits.For(PlayerProfile.Cautious, 1).HideAt, BotTraits.For(PlayerProfile.Skittish, 1).HideAt, "the cautious one hides earliest");
        Assert.IsTrue(BotTraits.For(PlayerProfile.Skittish, 1).FreezeOnStartle);
        Assert.IsFalse(BotTraits.For(PlayerProfile.Reckless, 1).FreezeOnStartle);
        Assert.Greater(BotTraits.For(PlayerProfile.Skittish, 1).DropChance, 0f);
        Assert.IsFalse(BotTraits.For(PlayerProfile.Cautious, 1).Hurries);

        // Everyone keeps out of her reach; the reckless the least, and not for footsteps.
        foreach (PlayerProfile p in System.Enum.GetValues(typeof(PlayerProfile)))
            Assert.Greater(BotTraits.For(p, 1).KeepClear, 2.2f, $"{p} should back off before she's within reach");
        Assert.Less(BotTraits.For(PlayerProfile.Reckless, 1).KeepClear, BotTraits.For(PlayerProfile.Efficient, 1).KeepClear);
        Assert.AreEqual(0f, BotTraits.For(PlayerProfile.Reckless, 1).StepsClear);
        Assert.Greater(BotTraits.For(PlayerProfile.Cautious, 1).KeepClear, BotTraits.For(PlayerProfile.Skittish, 1).KeepClear);
    }

    [Test]
    public void Seeds_MakeDifferentPeople()
    {
        var a = BotTraits.For(PlayerProfile.Skittish, 1);
        var b = BotTraits.For(PlayerProfile.Skittish, 2);
        Assert.IsTrue(a.Susceptibility.Zip(b.Susceptibility, (x, y) => Mathf.Abs(x - y)).Any(d => d > 0.01f));
        CollectionAssert.AreEqual(a.Susceptibility, BotTraits.For(PlayerProfile.Skittish, 1).Susceptibility, "the same seed, the same person");
    }

    [Test]
    public void Fear_SaturatesStartlesAndDrains()
    {
        var traits = BotTraits.For(PlayerProfile.Skittish, 3);
        var fear = new BotFear();

        Assert.IsTrue(fear.Feel(Stimulus.Appeared, traits), "her stepping out should startle a skittish player");
        Assert.AreEqual(1, fear.Startles);
        for (int i = 0; i < 50; i++) fear.Feel(Stimulus.Chase, traits);
        Assert.LessOrEqual(fear.Fear, 1f);

        float before = fear.Fear;
        fear.Decay(traits.Tau, traits);
        Assert.AreEqual(before * Mathf.Exp(-1f), fear.Fear, 1e-4f, "one time constant drains it to 37%");
        Assert.Greater(fear.MeanFear, 0f);

        var calm = new BotFear();
        Assert.IsFalse(calm.Feel(Stimulus.TellSound, BotTraits.For(PlayerProfile.Reckless, 3)), "a rattle doesn't startle the reckless");
    }

    [Test]
    public void HeardCategories_MapToStimuli()
    {
        Assert.AreEqual("light", EnvWorld.TellCategory(TellKind.Flicker));
        Assert.AreEqual("pa", EnvWorld.TellCategory(TellKind.PaChime));
        Assert.AreEqual("her", EnvWorld.TellCategory(TellKind.Screech));
        Assert.AreEqual("sound", EnvWorld.TellCategory(TellKind.ShelfRattle));
        Assert.IsTrue(EnvWorld.Storewide(TellKind.PaChime));
        Assert.IsFalse(EnvWorld.Storewide(TellKind.ShelfRattle));

        Assert.AreEqual(Stimulus.TellLight, SimulatedPlayer.FromCategory("light"));
        Assert.AreEqual(Stimulus.Vanish, SimulatedPlayer.FromCategory("vanish"));
        Assert.AreEqual(Stimulus.CloseIn, SimulatedPlayer.FromCategory("close_in"));
        Assert.AreEqual(Stimulus.HerSteps, SimulatedPlayer.FromCategory("steps"));
    }

    // ---- her Hunt's tricks (0.2.0) -----------------------------------------------------------

    [Test]
    public void EveryProfile_TurnsBackBeforeBackingOff_AndDodgesSometimes()
    {
        foreach (PlayerProfile p in System.Enum.GetValues(typeof(PlayerProfile)))
        {
            BotTraits t = BotTraits.For(p, 1);
            Assert.Greater(t.TurnBackAt, t.KeepClear, $"{p} should turn back from her before she's close enough to back off from");
            Assert.That(t.DodgeChance, Is.InRange(0.3f, 0.95f), $"{p}: nobody dodges never or always");
            Assert.That(t.DodgeReaction, Is.InRange(0.2f, 0.5f), $"{p}: a person's reaction time");
        }
        Assert.Less(BotTraits.For(PlayerProfile.Reckless, 1).TurnBackAt, BotTraits.For(PlayerProfile.Efficient, 1).TurnBackAt, "the reckless one turns back latest");
        Assert.Greater(BotTraits.For(PlayerProfile.Cautious, 1).DodgeChance, BotTraits.For(PlayerProfile.Reckless, 1).DodgeChance);
    }

    [Test]
    public void SheIsOnTheWay_OnlyAheadAndNearerThanTheGoal()
    {
        Vector3 me = Vector3.zero, goal = new Vector3(0f, 0f, 30f);
        Assert.IsTrue(SimulatedPlayer.OnTheWay(me, goal, new Vector3(1f, 0f, 12f), 15f), "standing in the aisle ahead");
        Assert.IsFalse(SimulatedPlayer.OnTheWay(me, goal, new Vector3(1f, 0f, 18f), 15f), "too far off to turn back yet");
        Assert.IsFalse(SimulatedPlayer.OnTheWay(me, goal, new Vector3(12f, 0f, 6f), 15f), "off to the side");
        Assert.IsFalse(SimulatedPlayer.OnTheWay(me, goal, new Vector3(0f, 0f, -8f), 15f), "behind: that's for backing off");
        Assert.IsFalse(SimulatedPlayer.OnTheWay(me, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 10f), 15f), "past where it's going");
    }

    [Test]
    public void AThrow_IsComingAtYou_OnlyOnALineThatReachesYou()
    {
        Vector3 me = Vector3.zero, from = new Vector3(0f, 1.6f, 10f), v = new Vector3(0f, 0f, -16f);
        Assert.IsTrue(EnvWorld.Incoming(me, from, v, out float eta));
        Assert.AreEqual(10f / 16f, eta, 1e-3f, "nearest when it gets here");
        Assert.IsTrue(EnvWorld.Incoming(me, new Vector3(1f, 1.6f, 10f), v, out _), "a metre off is still at you");
        Assert.IsFalse(EnvWorld.Incoming(me, new Vector3(2.5f, 1.6f, 10f), v, out _), "passing 2.5 m to the side");
        Assert.IsFalse(EnvWorld.Incoming(me, from, -v, out _), "flying away from you");
        Assert.IsFalse(EnvWorld.Incoming(me, from, Vector3.down * 9f, out _), "dropping straight down");
    }

    [Test]
    public void ToDodge_YouStepToTheSideItsLineIsFurtherFrom()
    {
        // Facing +z (right is +x), a throw coming straight back at you.
        Vector3 facing = Vector3.forward, v = new Vector3(0f, 0f, -16f);
        Assert.AreEqual("left", EnvWorld.StepAside(Vector3.zero, facing, new Vector3(0.5f, 1.6f, 10f), v), "its line passes to your right");
        Assert.AreEqual("right", EnvWorld.StepAside(Vector3.zero, facing, new Vector3(-0.5f, 1.6f, 10f), v), "its line passes to your left");
        // Facing the other way, the same throw: sides swap.
        Assert.AreEqual("right", EnvWorld.StepAside(Vector3.zero, Vector3.back, new Vector3(0.5f, 1.6f, 10f), v));
    }

    // The players know her only through the observation, the way an outside agent would.
    [Test]
    public void Players_NeverReadKaren()
    {
        string dir = Path.Combine(Application.dataPath, "!_Project/_Game/Eval/Scripts");
        var leaks = new[] { "SimulatedPlayers.cs", "BotFear.cs" }
            .SelectMany(f => File.ReadAllLines(Path.Combine(dir, f)).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line.Split(new[] { "//" }, System.StringSplitOptions.None)[0], @"\b(KarenBrain|KarenBody|KarenDirector|KarenLedger)\b"))
            .Select(x => $"{x.f}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.IsEmpty(leaks, "a simulated player read Karen directly:\n" + string.Join("\n", leaks));
    }
}
