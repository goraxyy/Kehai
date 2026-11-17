using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051BlackoutTests
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
    public void BlackoutSuppressesAmbientProbesEmissionAndUnlitPanels_AndRestoresExactOverrides()
    {
        lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lit.EnableKeyword("_EMISSION");                    // a lamp panel that glows
        lit.SetColor("_EmissionColor", Color.white);
        var plainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));   // a wall: no glow
        var wall = Box("plain wall", null, Vector3.right * 3f, Vector3.one).GetComponent<Renderer>();
        wall.sharedMaterial = plainMaterial;
        var fixture = Box("mixed fixture", null, Vector3.zero, Vector3.one).GetComponent<Renderer>();
        fixture.sharedMaterials = new[] { lit, unlit };
        var original = new MaterialPropertyBlock();
        original.SetColor("_EmissionColor", Color.red);
        original.SetFloat("_TestValue", .37f);
        fixture.SetPropertyBlock(original, 0);
        var inherited = new MaterialPropertyBlock();
        inherited.SetFloat("_InheritedValue", .65f);
        fixture.SetPropertyBlock(inherited);
        var probe = new GameObject("room reflection").AddComponent<ReflectionProbe>();
        probe.intensity = .72f;
        var camera = new GameObject("eye").AddComponent<Camera>();
        camera.backgroundColor = Color.blue;
        RenderSettings.fogColor = Color.grey;
        Mains();
        power.CutPower();
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        Assert.AreEqual(Color.black, RenderSettings.ambientSkyColor);
        Assert.AreEqual(Color.black, RenderSettings.ambientEquatorColor);
        Assert.AreEqual(Color.black, RenderSettings.ambientGroundColor);
        Assert.AreEqual(0f, RenderSettings.reflectionIntensity);
        Assert.AreEqual(0f, probe.intensity);
        Assert.AreEqual(Color.black, RenderSettings.fogColor);
        Assert.AreEqual(Color.black, camera.backgroundColor);
        var actual = new MaterialPropertyBlock();
        fixture.GetPropertyBlock(actual, 0);
        Assert.AreEqual(Color.black, actual.GetColor("_EmissionColor"));
        Assert.AreEqual(.37f, actual.GetFloat("_TestValue"));
        fixture.GetPropertyBlock(actual, 1);
        Assert.AreEqual(Color.black, actual.GetColor("_BaseColor"), "unlit lamp panels and CCTV textures must not glow");
        Assert.AreEqual(.65f, actual.GetFloat("_InheritedValue"));
        var wallBlock = new MaterialPropertyBlock();
        wall.GetPropertyBlock(wallBlock, 0);
        Assert.IsTrue(wallBlock.isEmpty, "a material that doesn't glow is left alone (it stays on the fast batched path)");
        power.RestorePower();
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        fixture.GetPropertyBlock(actual, 0);
        Assert.AreEqual(Color.red, actual.GetColor("_EmissionColor"));
        Assert.AreEqual(.37f, actual.GetFloat("_TestValue"));
        fixture.GetPropertyBlock(actual, 1);
        Assert.IsTrue(actual.isEmpty, "remove the temporary indexed override so the renderer block applies again");
        Assert.AreEqual(.72f, probe.intensity);
        Assert.AreEqual(Color.blue, camera.backgroundColor);
        Assert.IsFalse(wall.HasPropertyBlock(), "restoring leaves no block behind on untouched renderers");
        Object.DestroyImmediate(plainMaterial);
    }

    [Test]
    public void BlackoutStopsExtraRoomLights_AndRestorationKeepsAnIndependentRoomSwitchOff()
    {
        var lamp = new GameObject("task light").AddComponent<Light>();
        lamp.transform.position = new Vector3(18f, 2f, -142f);
        var sw = new GameObject("room switch").AddComponent<RoomLightSwitch>();
        sw.Configure(RoomLightSwitch.RoomOf(lamp.transform.position), new[] { lamp });
        Call(sw, "OnEnable");   // EditMode doesn't run OnEnable: register the switch as the game would
        Mains();
        power.CutPower();
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        Assert.IsFalse(lamp.enabled);
        lamp.enabled = true; // Simulate an in-progress flicker callback.
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        Assert.IsFalse(lamp.enabled);
        sw.SetOff(true);
        power.RestorePower();
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        Assert.IsFalse(lamp.enabled, "restoring mains must not cancel a separate room-light effect");
        sw.Restore();
        Assert.IsTrue(lamp.enabled);
        Call(sw, "OnDisable");
    }
}
