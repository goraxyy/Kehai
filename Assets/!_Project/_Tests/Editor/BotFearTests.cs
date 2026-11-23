using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kehai.Eval;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

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
        Assert.AreEqual("light", EnvWorld.TellCategory(TellKind.BallastWhine));
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

    [Test]
    public void HideChoosesAnOpenEntranceCoveredFromTheRememberedThreat()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box, area = 0,
            transform = Matrix4x4.Translate(Vector3.down * 0.1f), size = new Vector3(30f, 0.2f, 30f)
        };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), new List<NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(30f, 5f, 30f)), Vector3.zero, Quaternion.identity);
        Assert.IsNotNull(data);
        var mesh = NavMesh.AddNavMeshData(data);
        var root = new GameObject("hiding test");
        try
        {
            GameObject At(string name, Vector3 point)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform);
                go.transform.position = point;
                return go;
            }
            BoxCollider Box(string name, Vector3 centre, Vector3 size)
            {
                var box = At(name, centre).AddComponent<BoxCollider>();
                box.size = size;
                return box;
            }

            var interior = new Bounds(Vector3.up, new Vector3(2f, 2f, 2f));
            HidingPlace.Author(At("near, exposed", new Vector3(0f, 0f, 3f)), interior);
            var covered = HidingPlace.Author(At("covered", new Vector3(6f, 0f, 6f)), interior);
            Box("wall", new Vector3(4f, 1.5f, -2f), new Vector3(5f, 3f, 0.2f));
            var body = Box("moving body", new Vector3(0f, 1.5f, -2f), new Vector3(2f, 3f, 0.3f));
            body.gameObject.AddComponent<NavMeshAgent>().enabled = false;
            body.gameObject.AddComponent<Rigidbody>().isKinematic = true;

            bool Choose(out HidingPlace place, out Vector3 feet) => SimulatedPlayer.TryChooseOpenHide(
                Vector3.zero, Vector3.back * 8f, 0.45f, 1f, 0.35f, out place, out feet);
            Physics.SyncTransforms();
            Assert.IsTrue(Choose(out HidingPlace chosen, out Vector3 target));
            Assert.AreSame(covered, chosen, "moving bodies don't turn the nearer exposed entrance into cover");
            Assert.Less(Vector3.Distance(covered.transform.position, target), 0.4f);

            body.transform.position += Vector3.left * 10f;
            var stock = Box("loose stock", new Vector3(0f, 1.5f, -2f), new Vector3(2f, 3f, 0.3f));
            var stockBody = stock.gameObject.AddComponent<Rigidbody>();
            stockBody.useGravity = false;
            Physics.SyncTransforms();
            Assert.IsTrue(Choose(out chosen, out _));
            Assert.AreSame(covered, chosen, "loose stock doesn't supply cover");

            stock.gameObject.AddComponent<Item>();
            stockBody.isKinematic = true;
            Physics.SyncTransforms();
            Assert.IsTrue(Choose(out chosen, out _));
            Assert.AreSame(covered, chosen, "carryable items don't supply cover even if kinematic");

            // This panel is absent from the NavMesh. The entrance must still be open in physics.
            var panel = Box("closed panel", new Vector3(5.25f, 1.25f, 5.1f), new Vector3(2.5f, 2.5f, 0.15f));
            panel.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();
            Assert.IsFalse(Choose(out _, out _), "a kinematic door panel blocks the entrance and leaves the flee fallback");
        }
        finally
        {
            mesh.Remove();
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(root);
        }
    }

    // The players know her only through the observation, the way an outside agent would.
    [Test]
    public void Players_NeverReadKaren()
    {
        string dir = Path.Combine(Application.dataPath, "!_Project/_Game/Eval/Scripts");
        // 0.4.0 (C2.4): the persona, the options, the memory, the mind and its bridge too. Not
        // League.cs, which must know her: it sets up who she is for each career.
        var leaks = new[] { "SimulatedPlayers.cs", "BotFear.cs", "Persona.cs", "EmployeeOptions.cs", "BotMemory.cs",
                            "EmployeeObservation.cs", "EmployeeReward.cs", "EmployeeMind.cs", "MlAgentsEmployee.cs" }
            .SelectMany(f => File.ReadAllLines(Path.Combine(dir, f)).Select((line, i) => (f, i, line)))
            .Where(x => Regex.IsMatch(x.line.Split(new[] { "//" }, System.StringSplitOptions.None)[0], @"\b(KarenBrain|KarenBody|KarenDirector|KarenLedger)\b"))
            .Select(x => $"{x.f}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.IsEmpty(leaks, "a simulated player read Karen directly:\n" + string.Join("\n", leaks));
    }
}
