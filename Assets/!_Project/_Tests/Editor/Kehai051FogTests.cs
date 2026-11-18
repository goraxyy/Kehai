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

    [Test]
    public void FogCloudsAreNeutralGreyAndStayHiddenInABlackout()
    {
        Mains();
        GameObject cloud = KarenFogCloud();
        ParticleSystem ps = cloud.GetComponent<ParticleSystem>();
        FogVisuals.Neutralise(ps);
        Color start = ps.main.startColor.color;
        Assert.AreEqual(start.r, start.g, 0.001f);
        Assert.AreEqual(start.g, start.b, 0.001f);
        Color tint = cloud.GetComponent<ParticleSystemRenderer>().sharedMaterial.color;
        Assert.AreEqual(tint.r, tint.b, 0.001f, "material tint is not blue");

        var draw = typeof(FogVisuals).GetMethod("Draw", BindingFlags.Static | BindingFlags.NonPublic);
        var renderer = cloud.GetComponent<Renderer>();
        draw.Invoke(null, new object[] { ps, true });
        Assert.IsTrue(renderer.enabled, "shown with the lights on");
        power.CutPower();
        draw.Invoke(null, new object[] { ps, true });
        Assert.IsFalse(renderer.enabled, "a blackout hides the cloud, so it cannot glow");
        power.RestorePower();
        draw.Invoke(null, new object[] { ps, true });
        Assert.IsTrue(renderer.enabled, "power back, the cloud returns");
        draw.Invoke(null, new object[] { ps, false });
        Assert.IsFalse(renderer.enabled, "still hidden from other rooms");
    }

    static GameObject KarenFogCloud() => Kehai.Karen.FogCloud.Build(Vector3.zero, 1f, 5f).gameObject;
}
