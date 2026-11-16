using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

public class Kehai051EnvironmentTests
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
        power.RestorePower();
        Call(power.GetComponent<BlackoutRendering>(), "LateUpdate");
        fixture.GetPropertyBlock(actual, 0);
        Assert.AreEqual(Color.red, actual.GetColor("_EmissionColor"));
        Assert.AreEqual(.37f, actual.GetFloat("_TestValue"));
        fixture.GetPropertyBlock(actual, 1);
        Assert.IsTrue(actual.isEmpty, "remove the temporary indexed override so the renderer block applies again");
        Assert.AreEqual(.72f, probe.intensity);
        Assert.AreEqual(Color.blue, camera.backgroundColor);
    }

    [Test]
    public void BlackoutStopsExtraRoomLights_AndRestorationKeepsAnIndependentRoomSwitchOff()
    {
        var lamp = new GameObject("task light").AddComponent<Light>();
        lamp.transform.position = new Vector3(18f, 2f, -142f);
        var sw = new GameObject("room switch").AddComponent<RoomLightSwitch>();
        sw.Configure(RoomLightSwitch.RoomOf(lamp.transform.position), new[] { lamp });
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
