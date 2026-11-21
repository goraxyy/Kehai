using System.Reflection;
using Kehai.Karen;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.2, Phase 11: the sight sandbox's kit, layout and readouts, in edit mode. Her body is
// built only in play mode (SightSandboxPlayTests), because KarenBody.Build destroys a collider.
public class SandboxTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    Transform root;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("sandbox test").transform;
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            Object.DestroyImmediate(go);
    }

    // A person the size of the game's player (SandboxPlayer), standing or crouched.
    PlayerPresence Person(Vector3 feet, bool crouched = false)
    {
        var go = new GameObject("person");
        go.transform.position = feet;
        var controller = go.AddComponent<CharacterController>();
        controller.radius = SandboxPlayer.Radius;
        var motor = go.AddComponent<PlayerMotor>();
        typeof(PlayerMotor).GetField("isCrouching", Private).SetValue(motor, crouched);
        controller.height = crouched ? motor.crouchControllerHeight : SandboxPlayer.Height;
        controller.center = Vector3.up * controller.height * 0.5f;
        var presence = go.AddComponent<PlayerPresence>();
        typeof(PlayerPresence).GetMethod("Awake", Private).Invoke(presence, null);
        return presence;
    }

    [Test]
    public void Fridge_HidesYouInside_ButNotInFrontOfIt()
    {
        HidingPlace fridge = SandboxKit.StockFridge(root, Vector3.zero, 0f);
        PlayerPresence inside = Person(new Vector3(0f, 0f, SandboxKit.FridgeInside.z * 0.5f));
        float coverage = fridge.Coverage(inside);
        Assert.IsTrue(fridge.Conceals(inside, coverage), $"inside the fridge should hide you (coverage {coverage:0.00})");
        Object.DestroyImmediate(inside.gameObject);

        PlayerPresence outside = Person(new Vector3(0f, 0f, -1.2f));
        Assert.IsFalse(fridge.Conceals(outside, fridge.Coverage(outside)), "standing in front of the fridge hides nothing");
    }

    [Test]
    public void Rack_HidesYouOnlyCrouchedUnderIt()
    {
        HidingPlace rack = SandboxKit.StockRack(root, Vector3.zero, 0f);
        Assert.IsTrue(rack.requiresCrouch);
        PlayerPresence crouched = Person(Vector3.zero, crouched: true);
        float coverage = rack.Coverage(crouched);
        Assert.IsTrue(rack.Conceals(crouched, coverage), $"crouched under the rack should hide you (coverage {coverage:0.00})");
        Object.DestroyImmediate(crouched.gameObject);

        PlayerPresence standing = Person(Vector3.zero);
        Assert.IsFalse(rack.Conceals(standing, rack.Coverage(standing)), "standing in the rack's footprint hides nothing");
    }

    // Her eye at her own height (KarenBody.BuildLook: her body height less 0.15 m), with her settings.
    SightSensor HerEye(Vector3 feet)
    {
        var config = new KarenConfig();
        var eye = new GameObject("her eye").AddComponent<SightSensor>();
        KarenBody.ConfigureEye(eye, config);
        eye.transform.position = feet + Vector3.up * (config.bodyHeight - 0.15f);
        return eye;
    }

    [Test]
    public void ShortShelf_HidesCrouchedEyes_ButNotStandingOnes()
    {
        SandboxKit.ShelfUnit(root, "short", new Vector3(0f, 0f, 4f), 0f, SandboxKit.ShortShelf);
        SightSensor eye = HerEye(Vector3.zero);
        Physics.SyncTransforms();
        Vector3 behind = new Vector3(0f, 0f, 5f);
        float standing = eye.ExposureTo(behind + Vector3.up * 1.6f, behind + Vector3.up * 1.2f, behind, null);
        float crouched = eye.ExposureTo(behind + Vector3.up * 0.35f, behind + Vector3.up * 0.55f, behind, null);
        Assert.AreEqual(1f, standing, "standing eyes show over a short shelf");
        Assert.AreEqual(0f, crouched, "crouched eyes are hidden behind a short shelf");
    }

    [Test]
    public void TallShelf_HidesStandingEyes()
    {
        SandboxKit.ShelfUnit(root, "tall", new Vector3(0f, 0f, 4f), 0f, SandboxKit.TallShelf);
        SightSensor eye = HerEye(Vector3.zero);
        Physics.SyncTransforms();
        Vector3 behind = new Vector3(0f, 0f, 5f);
        Assert.AreEqual(0f, eye.ExposureTo(behind + Vector3.up * 1.6f, behind + Vector3.up * 1.2f, behind, null));
    }

    [Test]
    public void Layout_KeepsEverythingInTheRoom_AndThePanelsWithinReach()
    {
        Assert.LessOrEqual(SightSandbox.PanelHeight, 1.6f, "the panels must be at eye level or lower");
        float hx = SightSandbox.RoomSize.x * 0.5f - SandboxKit.WallThickness * 0.5f;
        float hz = SightSandbox.RoomSize.y * 0.5f - SandboxKit.WallThickness * 0.5f;
        bool Inside(Vector3 p, float margin) => Mathf.Abs(p.x) <= hx - margin && Mathf.Abs(p.z) <= hz - margin;

        // Her loop stays inside the room with room for her body, and off every shelf.
        float radius = new KarenConfig().bodyRadius;
        foreach (Vector3 p in SightSandbox.Loop)
        {
            Assert.IsTrue(Inside(p, radius), $"loop point {p} is outside the room");
            foreach (Vector3 s in SightSandbox.TallShelves) Assert.IsFalse(Overlaps(p, radius, s, SandboxKit.TallShelf), $"loop point {p} is in a tall shelf");
            foreach (Vector3 s in SightSandbox.ShortShelves) Assert.IsFalse(Overlaps(p, radius, s, SandboxKit.ShortShelf), $"loop point {p} is in a short shelf");
        }
        // The fridge opens west into the room and its back stays clear of the east wall.
        float back = SightSandbox.FridgeFront.x + SandboxKit.FridgeInside.z + SandboxKit.FridgeWall;
        Assert.Less(back, hx, "the fridge's back must stand inside the east wall");
        Assert.IsTrue(Inside(SightSandbox.RackCentre, SandboxKit.RackDepth * 0.5f), "the rack must stand inside the room");
        Assert.IsFalse(Inside(SightSandbox.PlayerStart, 0f), "you start outside the room, by the door");
    }

    static bool Overlaps(Vector3 p, float radius, Vector3 centre, Vector3 size) =>
        Mathf.Abs(p.x - centre.x) < size.x * 0.5f + radius && Mathf.Abs(p.z - centre.z) < size.z * 0.5f + radius;

    [Test]
    public void Readouts_SayHiddenInsideTheFridge_AndNothingFarAway()
    {
        HidingPlace fridge = SandboxKit.StockFridge(root, Vector3.zero, 0f);
        PlayerPresence inside = Person(new Vector3(0f, 0f, SandboxKit.FridgeInside.z * 0.5f));
        StringAssert.StartsWith("HIDDEN", SandboxIndicators.PlaceReport(fridge, inside));
        Object.DestroyImmediate(inside.gameObject);
        PlayerPresence far = Person(new Vector3(0f, 0f, -10f));
        Assert.AreEqual("", SandboxIndicators.PlaceReport(fridge, far));

        SightSensor eye = HerEye(Vector3.zero);
        StringAssert.StartsWith("nothing", SandboxIndicators.EyeReport(eye));
    }

    [Test]
    public void Indicators_ExistOnlyInsideTheSandboxes()
    {
        // C11.4: nothing outside the sandboxes' own code makes or reads them, so no scene, edition or
        // build of the game can show one.
        string root = System.IO.Path.Combine(Application.dataPath, "!_Project");
        foreach (string file in System.IO.Directory.GetFiles(root, "*.cs", System.IO.SearchOption.AllDirectories))
        {
            string f = file.Replace('\\', '/');
            if (f.Contains("/_Game/Sandbox/") || f.Contains("/_Tests/")) continue;
            StringAssert.DoesNotContain("SandboxIndicators", System.IO.File.ReadAllText(file), f);
        }
    }

    [Test]
    public void Panel_ButtonsRunTheirActions_AndSayWhatTheyDo()
    {
        int started = 0;
        SandboxButton[] buttons = SandboxButton.Panel(root, "panel", Vector3.up * SightSandbox.PanelHeight, 0f,
            ("START", "Start her", Color.green, () => started++));
        Assert.AreEqual(1, buttons.Length);
        Assert.AreEqual("Start her", buttons[0].GetPrompt());
        buttons[0].Press();
        Assert.AreEqual(1, started);
    }
}
