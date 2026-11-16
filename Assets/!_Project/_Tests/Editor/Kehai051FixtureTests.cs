using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051FixtureTests
{
    PowerSystem power;
    Material lit, unlit;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);

    [SetUp]
    public void EmptyScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        typeof(PowerSystem).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        SceneObjectNames.Invalidate();
    }

    [TearDown]
    public void Cleanup()
    {
        if (power != null && !power.HasPower) power.RestorePower();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (lit != null) Object.DestroyImmediate(lit);
        if (unlit != null) Object.DestroyImmediate(unlit);
    }

    PowerSystem Mains(Transform lamps = null)
    {
        power = new GameObject("mains").AddComponent<PowerSystem>();
        power.lightRoot = lamps;
        Call(power, "Awake");
        Call(power, "Start");
        return power;
    }

    static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = size;
        return go;
    }

    [Test]
    public void PlacementKeepsCoffeeAndMicrophoneOnTheTable_AndOnlyRetiresTheExactUnusedCamera()
    {
        var room = new GameObject("Staff Room - Furnished");
        var table = Box("Shared dining table", room.transform, new Vector3(16.8f, .4f, -142.35f), new Vector3(4.8f, .8f, 1.6f));
        var coffee = Box("coffee", null, new Vector3(3f, 6f, 3f), new Vector3(.6f, .7f, .5f));
        coffee.AddComponent<CoffeeMachine>();
        var mic = Box("microphone", null, new Vector3(9f, 6f, 9f), new Vector3(.26f, .3f, .3f));
        mic.AddComponent<ClosingMic>();
        var office = new GameObject("Office - Furnished");
        var old = new GameObject("Office camera"); old.transform.SetParent(office.transform);
        var working = new GameObject("Working feed").AddComponent<Camera>();
        FixturePresentation.ApplyPlacement();
        FixturePresentation.BoundsOf(table.transform, out Bounds top);
        foreach (var prop in new[] { coffee, mic })
        {
            FixturePresentation.BoundsOf(prop.transform, out Bounds placed);
            Assert.AreEqual(top.max.y, placed.min.y, 1e-5f);
            Assert.GreaterOrEqual(placed.min.x, top.min.x);
            Assert.LessOrEqual(placed.max.x, top.max.x);
            Assert.GreaterOrEqual(placed.min.z, top.min.z);
            Assert.LessOrEqual(placed.max.z, top.max.z);
            Assert.Less(placed.center.y, 1.5f, "reachable from floor level");
        }
        Vector3 first = coffee.transform.position;
        FixturePresentation.ApplyPlacement();
        Assert.AreEqual(first, coffee.transform.position, "runtime migration must be repeatable");
        Assert.IsFalse(old.activeSelf);
        Assert.IsTrue(working.gameObject.activeSelf);
        old.SetActive(true);
        old.AddComponent<Camera>().enabled = false;
        FixturePresentation.ApplyPlacement();
        Assert.IsTrue(old.activeSelf, "even a disabled real feed must survive");
    }
}
