using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051IslandTests
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
    public void IslandMigrationMovesOnlyTheIdentifiedPair_AndIsRepeatable()
    {
        var root = new GameObject(ColdStoreBuilder.RootName);
        var storage = new GameObject("Cold Storage Cabinets"); storage.transform.SetParent(root.transform);
        var islands = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            islands[i] = new GameObject("Back-to-back island " + (i + 1));
            islands[i].transform.SetParent(storage.transform);
            Box("cabinet assembly", islands[i].transform, new Vector3(i == 0 ? 62.2f : 77.8f, 1.3f, -182.5f), new Vector3(7.2f, 2.6f, 2.8f));
        }
        Assert.IsTrue(ColdStoreBuilder.MoveIslandsCloser());
        var positions = new[] { islands[0].transform.position, islands[1].transform.position };
        Assert.IsTrue(ColdStoreBuilder.MoveIslandsCloser());
        for (int i = 0; i < 2; i++)
        {
            FixturePresentation.BoundsOf(islands[i].transform, out Bounds b);
            Assert.AreEqual(i == 0 ? 64.5f : 75.5f, b.center.x, 1e-5f);
            Assert.AreEqual(-182.5f, b.center.z, 1e-5f);
            Assert.AreEqual(positions[i], islands[i].transform.position);
        }
    }
}
