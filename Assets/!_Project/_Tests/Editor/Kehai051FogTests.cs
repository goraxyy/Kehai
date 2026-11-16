using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051FogTests
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
    public void FogUsesNeutralColourAndBlackoutDoesNotChangeItsDistanceLimit()
    {
        Mains();
        var fog = new GameObject("fog").AddComponent<FogVisuals>();
        var apply = typeof(FogVisuals).GetMethod("Apply", Private);
        apply.Invoke(fog, new object[] { 2f });
        Assert.AreEqual(FogVisuals.Colour.r, FogVisuals.Colour.g);
        Assert.AreEqual(FogVisuals.Colour.g, FogVisuals.Colour.b);
        Assert.AreEqual(FogVisuals.Colour, RenderSettings.fogColor);
        power.CutPower();
        apply.Invoke(fog, new object[] { 2f });
        Assert.AreEqual(Color.black, RenderSettings.fogColor);
        Assert.AreEqual(2f, RenderSettings.fogEndDistance);
        power.RestorePower();
        apply.Invoke(fog, new object[] { 2f });
        Assert.AreEqual(FogVisuals.Colour, RenderSettings.fogColor);
    }
}
