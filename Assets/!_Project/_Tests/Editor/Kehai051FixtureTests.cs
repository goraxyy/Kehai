using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.1 items 4 and 12: names over the fixtures, the microphone on its own reachable table, the
// coffee machine left where the scene has it, and the decorative office camera gone.
public class Kehai051FixtureTests
{
    const float FloorTop = 0.5f;   // not 0, so nothing can pass by assuming the floor is at zero
    const float WallFace = 24.925f;
    const float ClockZ = -139.3f;

    [SetUp]
    public void EmptyScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SceneObjectNames.Invalidate();
    }

    [TearDown]
    public void Cleanup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = centre;
        go.transform.localScale = size;
        return go;
    }

    // A floor, the staff room's east wall at x 25, the time clock on it, and a ledge on the wall
    // line above the clock (the wall clock's place), which a ray straight down the wall hits.
    static Puncher StaffWall()
    {
        Box("Floor", null, new Vector3(20f, FloorTop - 0.25f, -140f), new Vector3(40f, 0.5f, 40f));
        Box("Wall", null, new Vector3(25f, 2f, -140f), new Vector3(0.15f, 4f, 20f));
        Box("Wall clock ledge", null, new Vector3(24.86f, FloorTop + 0.9f, ClockZ), new Vector3(0.1f, 0.1f, 0.4f));
        var clock = Box("Time clock", null, new Vector3(24.86f, FloorTop + 1.2f, ClockZ), new Vector3(0.1f, 0.3f, 0.3f));
        var puncher = clock.AddComponent<Puncher>();
        Physics.SyncTransforms();
        return puncher;
    }

    [Test]
    public void MicrophoneStandsOnItsOwnTable_OnTheRealFloor_WithinReachOfSomeoneStanding()
    {
        StaffWall();
        ClosingMic mic = KarenFixtures.PlaceClosingMic(null);
        Assert.IsNotNull(mic);
        var table = GameObject.Find(KarenFixtures.MicTableName);
        Assert.IsNotNull(table, "the microphone has a table of its own");
        FixturePresentation.BoundsOf(table.transform, out Bounds top);
        Assert.AreEqual(FloorTop, top.min.y, 0.01f, "the table stands on the floor, wherever the floor is");
        Assert.AreEqual(FloorTop + KarenFixtures.MicTableHeight, top.max.y, 0.01f);

        Vector3 centre = mic.GetComponent<BoxCollider>().bounds.center;
        Assert.That(centre.y - FloorTop, Is.InRange(0.9f, 1.2f), "mic centre at a reachable height above the floor");
        Assert.Less(centre.x, WallFace, "in front of the wall");
        Assert.Greater(centre.x, WallFace - KarenFixtures.MicTableDeep, "on the table, not past its front edge");
        Assert.That(centre.z, Is.InRange(top.min.z, top.max.z));
        Assert.GreaterOrEqual(mic.transform.position.y - ClosingMic.Size.y * 0.5f, top.max.y - 0.01f, "standing on the table top");

        // From the floor: the player's ray (2.5 m, the Interactable layer) from eye height, 1.5 m out.
        Physics.SyncTransforms();
        Vector3 eye = new Vector3(centre.x - 1.5f, FloorTop + 1.6f, centre.z);
        int interactable = 1 << LayerMask.NameToLayer("Interactable");
        Assert.IsTrue(Physics.Raycast(eye, (centre - eye).normalized, out RaycastHit hit, 2.5f, interactable, QueryTriggerInteraction.Collide));
        Assert.IsNotNull(hit.collider.GetComponentInParent<ClosingMic>());
    }

    // 0.5.2 (C12.2): the scene's radio, 2.1 m up, comes down onto its wall within reach.
    [Test]
    public void TheRadio_ComesDownOntoItsWall_WithinReach_AndStaysThere()
    {
        StaffWall();
        float z = ClockZ + 2f;
        var look = Box("Radio", null, new Vector3(WallFace - 0.4f, FloorTop + 2.1f, z), new Vector3(0.3f, 0.2f, 0.15f));
        look.AddComponent<MusicBox>();
        Physics.SyncTransforms();

        MusicBox radio = KarenFixtures.PlaceRadio();
        Assert.IsNotNull(radio);
        FixturePresentation.BoundsOf(radio.transform, out Bounds b);
        Assert.AreEqual(FloorTop + KarenFixtures.RadioHeight, b.center.y, 0.02f, "its centre 1.4 m over the floor, below your eyes");
        Assert.AreEqual(WallFace, b.max.x, 0.03f, "its back against the wall");
        Assert.AreEqual(z, b.center.z, 0.01f, "the same spot along the wall");

        Vector3 placed = radio.transform.position;
        KarenFixtures.PlaceRadio();
        Assert.AreEqual(placed, radio.transform.position, "within reach already: it isn't moved again");
    }

    [Test]
    public void WallBoxes_AreMeasuredFromTheFloor_NotFromWhatHangsAboveTheClock()
    {
        StaffWall();
        PaBox box = KarenFixtures.PlacePaBox(null);
        Assert.IsNotNull(box);
        Assert.AreEqual(FloorTop + KarenFixtures.BoxHeight, box.transform.position.y, 0.02f);
    }

    [Test]
    public void MicrophoneTable_GoesToTheOtherSideOfTheClock_WhenTheNearSideIsTaken()
    {
        var puncher = StaffWall();
        Box("Cabinet", null, new Vector3(24.5f, FloorTop + 0.5f, ClockZ + 1.1f), new Vector3(0.6f, 1f, 0.6f));
        Physics.SyncTransforms();
        ClosingMic mic = KarenFixtures.PlaceClosingMic(null);
        Assert.Less(mic.transform.position.z, puncher.transform.position.z, "the free side of the clock");
    }

    [Test]
    public void CoffeeMachine_IsNotMoved_AndNothingIsPutOnTheDiningTable()
    {
        var room = new GameObject("Staff Room - Furnished");
        Box("Shared dining table", room.transform, new Vector3(16.8f, .4f, -142.35f), new Vector3(4.8f, .8f, 1.6f));
        var coffee = Box("coffee", null, new Vector3(10.57f, 0.97f, -148.04f), new Vector3(.6f, .7f, .5f));
        coffee.AddComponent<CoffeeMachine>();
        FixturePresentation.ApplyPlacement();
        FixturePresentation.ApplyPlacement();
        Assert.AreEqual(new Vector3(10.57f, 0.97f, -148.04f), coffee.transform.position);
        Assert.AreEqual(string.Empty, FixturePresentation.CoffeeMachineSurface);
    }

    [Test]
    public void RetiredOfficeCamera_IsRemovedByTheBuilder_AndARealFeedSurvives()
    {
        var props = new GameObject("Furniture_And_Props");
        var old = new GameObject("Office_Camera_01"); old.transform.SetParent(props.transform);
        old.AddComponent<MeshFilter>();
        var oldAuthored = new GameObject("Office camera");
        var feed = new GameObject("Office_Camera_01"); feed.transform.SetParent(props.transform);
        feed.AddComponent<Camera>().enabled = false;   // even a disabled real feed
        var monitor = new GameObject("Office_Monitor_01");

        Assert.AreEqual(2, EnvironmentFitBuilder.RemoveRetiredOfficeCamera());
        Assert.IsTrue(old == null && oldAuthored == null, "gone, not hidden");
        Assert.IsTrue(feed != null && monitor != null);
        Assert.AreEqual(0, EnvironmentFitBuilder.RemoveRetiredOfficeCamera(), "a second run finds nothing: it does not come back");
    }

    [Test]
    public void RuntimeSafeguard_HidesTheCameraInASceneTheBuilderHasNotBeenRerunOn()
    {
        var office = new GameObject("Office - Furnished");
        var old = new GameObject("Office camera"); old.transform.SetParent(office.transform);
        var working = new GameObject("Working feed").AddComponent<Camera>();
        FixturePresentation.ApplyPlacement();
        Assert.IsFalse(old.activeSelf);
        Assert.IsTrue(working.gameObject.activeSelf);
        old.SetActive(true);
        old.AddComponent<Camera>().enabled = false;
        FixturePresentation.ApplyPlacement();
        Assert.IsTrue(old.activeSelf, "a real feed must survive");
    }

    FixturePresentation Labelled(out GameObject coffee, out GameObject clockProp)
    {
        coffee = Box("coffee", null, new Vector3(0f, 1f, 0f), new Vector3(.6f, .7f, .5f));
        coffee.AddComponent<CoffeeMachine>();
        Box("clock", null, new Vector3(2f, 1.2f, 0f), new Vector3(.3f, .3f, .1f)).AddComponent<Puncher>();
        Box("mic", null, new Vector3(-2f, 1f, 0f), new Vector3(.26f, .3f, .12f)).AddComponent<ClosingMic>();
        Box("pa", null, new Vector3(-4f, 1f, 0f), new Vector3(.28f, .36f, .11f)).AddComponent<PaBox>();
        var room = new GameObject("Staff Room - Furnished");
        clockProp = Box("Staff clock", room.transform, new Vector3(0f, 2.1f, 3f), new Vector3(.4f, .4f, .05f));
        var fp = new GameObject("names").AddComponent<FixturePresentation>();
        fp.BuildLabels();
        return fp;
    }

    [Test]
    public void EveryFixtureGetsOneLabel_WithItsName_AboveItsBounds_AndNoSecondOnRebuild()
    {
        var fp = Labelled(out GameObject coffee, out GameObject wallClock);
        Assert.AreEqual(5, fp.LabelCount);
        fp.BuildLabels();
        fp.BuildLabels();
        Assert.AreEqual(5, fp.LabelCount, "labels are made once");
        string[] names = { "Coffee machine", "Time clock (Puncher)", "PA microphone", "PA controls" };
        int found = 0;
        foreach (var f in Object.FindObjectsByType<HighlightInteractable>())
        {
            var label = fp.LabelOf(f.transform);
            Assert.IsNotNull(label);
            Assert.AreEqual(FixturePresentation.NameOf(f), label.transform.Find("Text").GetComponent<TextMesh>().text);
            Assert.Contains(label.transform.Find("Text").GetComponent<TextMesh>().text, names);
            FixturePresentation.BoundsOf(f.transform, out Bounds b);
            Assert.AreEqual(b.max.y + FixturePresentation.Above, label.transform.position.y, 1e-4f);
            Assert.AreEqual("Shadow", label.transform.GetChild(0).name);
            Assert.IsNull(label.GetComponentInChildren<Light>());
            found++;
        }
        Assert.AreEqual(4, found);
        var clockLabel = fp.LabelOf(wallClock.transform);
        Assert.AreEqual("Clock", clockLabel.transform.Find("Text").GetComponent<TextMesh>().text);
        Assert.AreEqual(2.1f + 0.2f + FixturePresentation.Above, clockLabel.transform.position.y, 1e-3f);
    }

    [Test]
    public void LabelShowsNearAndInTheClear_FacesTheEye_AndHidesFarBehindWallsPausedOrTraining()
    {
        var fp = Labelled(out GameObject coffee, out _);
        var label = fp.LabelOf(coffee.transform);
        Physics.SyncTransforms();
        var eye = new GameObject("eye").transform;
        eye.position = new Vector3(0f, 1.6f, -2.5f);

        fp.UpdateLabels(eye, false);
        Assert.IsTrue(label.activeSelf, "2.5 m away, clear");
        Assert.AreEqual(0f, Vector3.Angle(label.transform.forward, Vector3.forward), 1f, "turned to face the eye, yaw only");

        eye.position = new Vector3(0f, 1.6f, -6f);
        fp.UpdateLabels(eye, false);
        Assert.IsFalse(label.activeSelf, "beyond 4 m");

        eye.position = new Vector3(0f, 1.6f, -2.5f);
        var wall = Box("wall", null, new Vector3(0f, 1.5f, -1.5f), new Vector3(4f, 3f, 0.1f));
        Physics.SyncTransforms();
        fp.UpdateLabels(eye, false);
        Assert.IsFalse(label.activeSelf, "behind a wall");
        Object.DestroyImmediate(wall);
        Physics.SyncTransforms();
        fp.UpdateLabels(eye, false);
        Assert.IsTrue(label.activeSelf);
        fp.UpdateLabels(eye, true);
        Assert.IsFalse(label.activeSelf, "paused, or the training edition");
    }
}
