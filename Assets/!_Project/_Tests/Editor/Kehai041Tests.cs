using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kehai;
using Kehai.Karen;
using Kehai.Store;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai 0.4.1: the owner's 17 items of 12 November. Sight, doors and the automatic doors are in
// Kehai041SightAndDoorsTests; this is the rest that can be checked without the store: the fog's
// rooms, the 20 s fixes, closing time, the cameras and their two tricks, the eyelids, the map's
// colours, the F1 cursor, the trick sounds' words, the trick guide.
public class Kehai041Tests
{
    GameObject root;
    float now;
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [SetUp]
    public void SetUp()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("Kehai041Tests");
        now = 0f;
        FogState.Reset();
        FogState.Clock = () => now;
        ShiftManager.CountCustomers = () => 0;

        // A muted voice of the test's own: the game's pool uses DontDestroyOnLoad, which isn't
        // there in EditMode (as in DoorBlockTests), and nothing a test runs may make a sound.
        var voice = new GameObject("muted test voice").AddComponent<AudioSource>();
        voice.transform.SetParent(root.transform);
        voice.playOnAwake = false;
        voice.mute = true;
        poolWas = Pool.GetValue(null);
        nextWas = Next.GetValue(null);
        Pool.SetValue(null, new[] { voice });
        Next.SetValue(null, 0);
    }

    static readonly FieldInfo Pool = typeof(OneShotAudio).GetField("pool", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly FieldInfo Next = typeof(OneShotAudio).GetField("next", BindingFlags.Static | BindingFlags.NonPublic);
    object poolWas, nextWas;

    [TearDown]
    public void TearDown()
    {
        Pool.SetValue(null, poolWas);
        Next.SetValue(null, nextWas);
        if (StoreCameras.Instance != null) Object.DestroyImmediate(StoreCameras.Instance.gameObject);
        if (CameraBox.Instance != null) Object.DestroyImmediate(CameraBox.Instance.gameObject);
        Object.DestroyImmediate(root);
        FogState.Reset();
        FogState.RoomOf = p => StoreMap.AreaAt(p);
        FogState.Clock = () => Time.time;
        ShiftManager.CountCustomers = () => CustomerSpawner.InStore;
        TaskManager.CamerasBroken = null;
        TacticLibrary.ResetShift();
    }

    T Add<T>(string name = null) where T : Component
    {
        var go = new GameObject(name ?? typeof(T).Name);
        go.transform.SetParent(root.transform);
        return go.AddComponent<T>();
    }

    static GameObject Box(string name, Vector3 centre, Vector3 size)
    {
        var go = new GameObject(name);
        go.transform.position = centre;
        go.AddComponent<BoxCollider>().size = size;
        Physics.SyncTransforms();
        return go;
    }

    ShiftManager Shift(out TaskManager tasks)
    {
        tasks = root.AddComponent<TaskManager>();
        var shift = root.AddComponent<ShiftManager>();
        shift.taskManager = tasks;
        tasks.shiftManager = shift;
        shift.shiftDurationSeconds = 300f;
        return shift;
    }

    // ---- item 1: fog ------------------------------------------------------------------------

    [Test]
    public void Fog_IsSeenOnlyFromInsideItsRoom()
    {
        Assert.IsTrue(FogVisuals.Shows("Stockroom", "Stockroom"));
        Assert.IsFalse(FogVisuals.Shows("Stockroom", "Sales floor"), "from the next room it isn't drawn");
        Assert.IsFalse(FogVisuals.Shows("Stockroom", "Cold storage"));
        Assert.IsFalse(FogVisuals.Shows("Stockroom", null));
        Assert.IsFalse(FogVisuals.Shows(null, null));
    }

    [Test]
    public void Fog_NeverFillsTheSalesFloorOrTheStaffRoom()
    {
        CollectionAssert.DoesNotContain(KarenWorld.FogRooms, "Sales floor");
        CollectionAssert.DoesNotContain(KarenWorld.FogRooms, "Staff room");
        CollectionAssert.Contains(KarenWorld.FogRooms, "Cold storage");
        CollectionAssert.Contains(KarenWorld.FogRooms, "Stockroom");
    }

    [Test]
    public void FogInOneRoom_LeavesTheNextRoomClear_ForEveryEye()
    {
        FogState.RoomOf = p => p.x < 0f ? "Stockroom" : "Sales floor";
        FogState.Fill("Stockroom", 2f, 0.01f);
        now = 1f;
        Assert.AreEqual(2f, FogState.VisibilityAt(new Vector3(-5f, 0f, 0f)), 1e-3f, "in the stockroom");
        Assert.AreEqual(FogState.Open, FogState.VisibilityAt(new Vector3(5f, 0f, 0f)), 1e-3f, "on the sales floor");
        Assert.GreaterOrEqual(FogState.Open, new KarenConfig().sightRange, "no fog must not shorten her 72 m eye");
    }

    // ---- item 10: the backstreet's fixes take twenty seconds -------------------------------------

    [Test]
    public void EveryBackstreetFix_IsAHoldOfTwentySeconds()
    {
        Assert.AreEqual(20f, BackstreetFix.HoldSeconds);
        Assert.AreEqual(20f, Add<FogBox>().HoldDuration);
        Assert.AreEqual(20f, Add<ElectricBox>().HoldDuration);
        Assert.AreEqual(20f, Add<CameraBox>().HoldDuration);
        Assert.AreEqual("Hold E to fix the cameras (20 s)", BackstreetFix.Prompt("fix the cameras"));
    }

    [Test]
    public void TheBreakers_ComeBackByHoldingTheBox_NotByTappingASwitch()
    {
        var box = Add<ElectricBox>();
        var panel = root.AddComponent<BreakerPanel>();
        typeof(BreakerPanel).GetProperty("Instance").SetValue(null, panel);
        try
        {
            Assert.IsFalse(box.CanHold(null), "nothing tripped: nothing to hold");
            panel.Trip(LightCircuit.East);
            panel.Trip(LightCircuit.Back);
            Assert.AreEqual(2, panel.TrippedCount);
            Assert.IsTrue(box.CanHold(null));
            StringAssert.Contains("20 s", box.GetPrompt());

            // A tap on a tripped switch no longer throws it on.
            panel.Flip((int)LightCircuit.East, Vector3.zero);
            Assert.IsFalse(panel.IsOn(LightCircuit.East));
            // A live one can still be thrown off.
            panel.Flip((int)LightCircuit.West, Vector3.zero);
            Assert.IsFalse(panel.IsOn(LightCircuit.West));

            // The switches light in turn as the hold goes on, and only the hold's end resets them.
            box.OnHoldProgress(0.4f);
            Assert.IsTrue(panel.ShownOn(0));
            Assert.IsFalse(panel.ShownOn(2));
            Assert.AreEqual(3, panel.TrippedCount, "progress shows; nothing is reset yet");
            box.OnHoldCancelled();
            Assert.IsFalse(panel.ShownOn(0), "let go: it starts again");

            panel.ResetAll();
            Assert.IsTrue(panel.AllOn);
            Assert.IsFalse(box.CanHold(null));
        }
        finally { typeof(BreakerPanel).GetProperty("Instance").SetValue(null, null); }
    }

    [Test]
    public void TheHoldBar_CountsDown()
    {
        Assert.AreEqual("20 s", InteractHud.Countdown(20f));
        Assert.AreEqual("12 s", InteractHud.Countdown(11.2f));
        Assert.AreEqual("3.4 s", InteractHud.Countdown(3.4f));
    }

    // ---- item 11: closing time ------------------------------------------------------------------

    [Test]
    public void AtClosingTime_CustomersKeepComing_UntilTheAnnouncement()
    {
        ShiftManager shift = Shift(out TaskManager tasks);
        shift.StartShift();
        Assert.IsFalse(shift.ClosingTime);
        Assert.IsFalse(shift.AnnounceClosing(), "not before closing time");
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.AnnounceClosing));

        shift.ReachClosingTime();
        Assert.IsTrue(shift.ClosingTime);
        Assert.IsTrue(shift.CustomersAllowed, "the doors don't close by themselves any more");
        Assert.IsFalse(shift.DoorsClosed);
        Assert.IsTrue(shift.AwaitingAnnouncement);
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.AnnounceClosing), "the task list asks for it");
        Assert.IsFalse(tasks.AllComplete);

        Assert.IsTrue(shift.AnnounceClosing());
        Assert.IsFalse(shift.CustomersAllowed, "no more customers");
        Assert.IsTrue(shift.DoorsClosed);
        Assert.IsTrue(shift.ClosingAnnounced);
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.AnnounceClosing));
        Assert.IsFalse(shift.AnnounceClosing(), "once");
    }

    [Test]
    public void BeforeTheAnnouncement_TheShiftCantEnd_WhileACustomerIsInTheStore()
    {
        ShiftManager shift = Shift(out _);
        shift.StartShift();
        int inStore = 2;
        ShiftManager.CountCustomers = () => inStore;

        Assert.IsFalse(shift.CanClockOut, "two customers inside, nothing announced");
        Assert.AreEqual("Customers are still in the store", shift.ClockOutBlocker);
        shift.ToggleShift();
        Assert.IsTrue(shift.IsShiftActive, "the time clock refused");

        shift.ReachClosingTime();
        Assert.IsFalse(shift.CanClockOut);
        Assert.AreEqual("Finish your tasks first", shift.ClockOutBlocker, "the announcement is a task now");

        shift.AnnounceClosing();
        Assert.IsTrue(shift.CanClockOut, "announced: the ones still shopping don't hold the shift");
        shift.ToggleShift();
        Assert.IsFalse(shift.IsShiftActive);
    }

    [Test]
    public void AnEmptyStore_DoesNotHoldTheShift_AndANewShiftOwesANewAnnouncement()
    {
        ShiftManager shift = Shift(out _);
        shift.StartShift();
        Assert.IsTrue(shift.CanClockOut, "nobody inside, every job done: as before 0.4.1");
        shift.ReachClosingTime();
        shift.AnnounceClosing();
        shift.EndShift();
        shift.StartShift();
        Assert.IsFalse(shift.ClosingAnnounced);
        Assert.IsTrue(shift.CustomersAllowed);
    }

    [Test]
    public void ThePaMicrophone_SaysWhyItWont_AndAnnouncesWhenItCan()
    {
        ShiftManager shift = Shift(out _);
        Assert.AreEqual("The PA microphone", ClosingMic.Refusal(shift, null));
        shift.StartShift();
        Assert.AreEqual("Not closing time yet", ClosingMic.Refusal(shift, null));
        shift.ReachClosingTime();
        Assert.AreEqual(string.Empty, ClosingMic.Refusal(shift, null));

        var pa = root.AddComponent<PaSystem>();
        pa.SetPowered(false);
        Assert.AreEqual("The PA is switched off", ClosingMic.Refusal(shift, pa), "the microphone needs the PA");
        pa.SetPowered(true);
        Assert.AreEqual(string.Empty, ClosingMic.Refusal(shift, pa));

        shift.AnnounceClosing();
        Assert.AreEqual("The store is closed", ClosingMic.Refusal(shift, pa));
    }

    [Test]
    public void ThePaMicrophone_IsBuilt_WithItsModelOrItsOwnBoxes_AndHighlights()
    {
        ClosingMic mic = ClosingMic.Build(new Vector3(2f, 1.45f, 0f), Vector3.forward, null);
        mic.transform.SetParent(root.transform);
        Assert.IsNotNull(mic.GetComponent<OutlineHighlight>(), "it highlights like the store's other things");
        Assert.IsNotNull(mic.GetComponent<BoxCollider>());
        Assert.AreEqual(LayerMask.NameToLayer("Interactable"), mic.gameObject.layer);
        if (PropModels.Has(PropModels.ClosingMic))
            Assert.IsNotNull(mic.transform.Find(PropModels.ClosingMic), "the imported model, in place of its own boxes");
        else
            Assert.Greater(mic.GetComponentsInChildren<Renderer>().Length, 3, "a case, a grille, a button, a microphone, a lamp");
        Assert.IsNotNull(mic.transform.Find("Lamp"), "its own lamp, with or without the model");
        Assert.AreEqual("The PA microphone", mic.GetPrompt());
    }

    // ---- items 8 and 9: the cameras and her two tricks -------------------------------------------

    // One camera in a room of boxes: a floor, a wall to hang it on, and the box to fix it at.
    StoreCameras OneCamera()
    {
        Box("floor", new Vector3(0f, -0.1f, 0f), new Vector3(40f, 0.2f, 40f)).transform.SetParent(root.transform);
        Box("wall", new Vector3(-6f, 2f, 0f), new Vector3(0.2f, 4f, 20f)).transform.SetParent(root.transform);
        var spots = new[] { new StoreCameras.Spot("TEST ROOM", "Test room", new Vector2(-4f, 0f), new Vector2(-1f, 0f), new Vector3(4f, 0f, 0f)) };
        StoreCameras cams = StoreCameras.InstallAt(spots, null);
        Assert.IsNotNull(cams, "a floor under the spot should give a camera");
        return cams;
    }

    [Test]
    public void ACamera_HangsHighOnItsWall_AndLooksAtItsRoom()
    {
        StoreCameras cams = OneCamera();
        Assert.AreEqual(1, cams.Cameras.Count);
        StoreCamera cam = cams.Cameras[0];
        Assert.AreEqual(StoreCameras.MountHeight, cam.transform.position.y, 0.05f);
        Assert.AreEqual(-5.9f + 0.24f, cam.transform.position.x, 0.1f, "just off the wall");
        Assert.Greater(Vector3.Dot(cam.transform.forward, (new Vector3(4f, 0f, 0f) - cam.transform.position).normalized), 0.99f);
        Assert.IsNull(cams.Wall, "no office monitors in this scene: no screen wall");
    }

    [Test]
    public void NoFloorUnderASpot_NoCamera()
    {
        Assert.IsFalse(StoreCameras.Mount(new StoreCameras.Spot("NOWHERE", "x", new Vector2(500f, 500f), Vector2.up, Vector3.zero), out _));
        Assert.IsNull(StoreCameras.InstallAt(new[] { new StoreCameras.Spot("NOWHERE", "x", new Vector2(500f, 500f), Vector2.up, Vector3.zero) }, null));
        Assert.IsNull(StoreCameras.Instance, "a scene that isn't the store is left alone");
    }

    [Test]
    public void TheSixCameras_AreTheOnesTheOwnerAskedFor()
    {
        CollectionAssert.AreEquivalent(new[] { "Cold storage", "Stockroom", "Lobby", "Staff room", "Pavement", "Backstreet" },
            StoreCameras.Spots.Select(s => s.Area).ToArray());
        foreach (StoreCameras.Spot s in StoreCameras.Spots)
        {
            Assert.AreEqual(s.Area, StoreMap.AreaAt(new Vector3(s.Probe.x, 0f, s.Probe.y)), s.Name + " hangs in its own room");
            Assert.AreEqual(s.Area, StoreMap.AreaAt(s.LookAt), s.Name + " looks at its own room");
            Assert.IsTrue(StoreMap.KarenWorksIn(s.Area));
        }
        Assert.Greater(CctvWall.Screen.x, CctvWall.OldMonitorWidth * 1.3f, "the screens are bigger than the old monitors");
    }

    // You, where a camera can see you: a body the sensors may look at.
    PlayerPresence You(Vector3 at)
    {
        var go = new GameObject("you");
        go.transform.SetParent(root.transform);
        go.transform.position = at;
        var you = go.AddComponent<PlayerPresence>();
        typeof(PlayerPresence).GetMethod("Awake", Private).Invoke(you, null);
        return you;
    }

    [Test]
    public void TheCamerasTellHerNothing_UnlessSheIsAtTheScreens()
    {
        StoreCameras cams = OneCamera();
        PlayerPresence you = You(new Vector3(2f, 0f, 0f));
        var reports = new List<Observation>();
        System.Action<Observation> listen = o => reports.Add(o);
        InfrastructureFeed.Reported += listen;
        try
        {
            for (int i = 0; i < 100; i++) cams.Step(0.1f);
            Assert.AreEqual(0, reports.Count, "nobody at the screens: the cameras aren't her eyes");
            Assert.AreEqual(0f, cams.Cameras[0].Eye.Awareness);

            cams.SetWatching(true);
            Assert.IsTrue(cams.Watching);
            for (int i = 0; i < 100; i++) cams.Step(0.1f);
            Assert.GreaterOrEqual(reports.Count, 1, "watched, a camera that sees you says so");
            Assert.AreEqual(SenseChannel.Infrastructure, reports[0].Channel);
            Assert.Less(Vector3.Distance(reports[0].Position, you.Position), 0.5f, "and says where");
            Assert.GreaterOrEqual(cams.Sightings, 1);

            // She gets up: it stops, and what the cameras had built up is gone.
            cams.SetWatching(false);
            int had = reports.Count;
            for (int i = 0; i < 100; i++) cams.Step(0.1f);
            Assert.AreEqual(had, reports.Count);
        }
        finally
        {
            InfrastructureFeed.Reported -= listen;
            typeof(PlayerPresence).GetMethod("OnDestroy", Private).Invoke(you, null);
        }
    }

    [Test]
    public void ACamera_SeesOnlyItsOwnRoom_AndOnlyEyeToEye()
    {
        StoreCameras cams = OneCamera();
        // Behind a wall from the camera: in "its" direction, but no line to you.
        Box("partition", new Vector3(1f, 2f, 0f), new Vector3(0.2f, 4f, 20f)).transform.SetParent(root.transform);
        PlayerPresence you = You(new Vector3(4f, 0f, 0f));
        int reports = 0;
        System.Action<Observation> listen = _ => reports++;
        InfrastructureFeed.Reported += listen;
        try
        {
            cams.SetWatching(true);
            for (int i = 0; i < 100; i++) cams.Step(0.1f);
            Assert.AreEqual(0, reports, "a wall between the camera and you");
        }
        finally
        {
            InfrastructureFeed.Reported -= listen;
            typeof(PlayerPresence).GetMethod("OnDestroy", Private).Invoke(you, null);
        }
    }

    [Test]
    public void BrokenCameras_ShowNobodyAnything_UntilTheBoxIsHeld()
    {
        StoreCameras cams = OneCamera();
        var tasks = root.AddComponent<TaskManager>();
        TaskManager.CamerasBroken = () => StoreCameras.Instance != null && StoreCameras.Instance.Broken;
        CameraBox box = CameraBox.Build(new Vector3(-5.9f, 1.45f, 3f), Vector3.right, null);
        box.transform.SetParent(root.transform);

        Assert.IsFalse(box.CanHold(null));
        Assert.AreEqual("Cameras: all working", box.GetPrompt());
        Assert.IsTrue(tasks.AllComplete);

        Assert.IsTrue(cams.Break());
        Assert.IsFalse(cams.Break(), "already broken");
        Assert.IsTrue(cams.Cameras[0].IsBroken);
        Assert.IsFalse(tasks.AllComplete, "fixing them is a job");
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.FixCameras));
        cams.SetWatching(true);
        Assert.IsFalse(cams.Watching, "broken cameras show her nothing either");

        Assert.IsTrue(box.CanHold(null));
        Assert.AreEqual("Hold E to fix the cameras (20 s)", box.GetPrompt());
        box.OnHoldComplete(null);
        Assert.IsFalse(cams.Broken);
        Assert.IsTrue(tasks.AllComplete);
        Assert.IsFalse(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.FixCameras));
    }

    static KarenContext Context() => new KarenContext { Config = new KarenConfig { seed = 7 }, Rng = new KarenRng(7) };

    [Test]
    public void TheTwoCameraTricks_AreInTheLibrary_Live_WithTheirSoundsAndWords()
    {
        Tactic watch = TacticLibrary.Get("camera_watch"), brk = TacticLibrary.Get("camera_break");
        Assert.IsNotNull(watch);
        Assert.IsNotNull(brk);
        Assert.IsFalse(watch.Draft || brk.Draft);
        // Appended, so every older trick keeps its number for a trained policy.
        Assert.AreEqual(TacticLibrary.All.Count - 2, TacticLibrary.IndexOf(watch));
        Assert.AreEqual(TacticLibrary.All.Count - 1, TacticLibrary.IndexOf(brk));

        Assert.IsTrue(watch.IsHandsOn, "she has to be at the screens");
        Assert.IsFalse(watch.LeavesChore);
        Assert.AreEqual(TellKind.MonitorHum, watch.Tell);
        Assert.IsFalse(brk.IsHandsOn, "the building's own, like the lights");
        Assert.IsTrue(brk.LeavesChore, "so not after the doors close (rule 10)");
        Assert.AreEqual(TellKind.CameraFizz, brk.Tell);
        StringAssert.Contains("camera box", brk.Chore);
        StringAssert.Contains("20 s", brk.Chore);

        foreach (Tactic t in new[] { watch, brk })
        {
            Assert.AreNotEqual(t.Id.Replace('_', ' '), KarenNarrator.Tactic(t.Id), t.Id + " has no plain words");
            Assert.IsNotNull(ProceduralAudio.Tell(t.Tell));
            Assert.IsNotNull(TrickGuide.Get(t.Id), t.Id + " isn't in the trick guide");
        }
    }

    [Test]
    public void WithoutCameras_NeitherTrickIsAvailable()
    {
        Assert.IsNull(StoreCameras.Instance);
        Assert.IsFalse(TacticLibrary.Get("camera_watch").Available(Context(), out string why));
        Assert.AreEqual("the store has no cameras", why);
        Assert.IsFalse(TacticLibrary.Get("camera_break").Available(Context(), out _));
    }

    [Test]
    public void CameraBreak_BreaksThem_AndPointsAtTheBox()
    {
        StoreCameras cams = OneCamera();
        CameraBox box = CameraBox.Build(new Vector3(-5.9f, 1.45f, 3f), Vector3.right, null);
        box.transform.SetParent(root.transform);
        typeof(StoreCameras).GetProperty("Box").SetValue(cams, box);
        var brk = (CameraBreakTactic)TacticLibrary.Get("camera_break");
        KarenContext c = Context();

        Assert.IsTrue(brk.Available(c, out string why), why);
        Assert.IsFalse(brk.EffectRunning(c));
        Assert.IsFalse(brk.FixAt(c, out _));

        Assert.IsTrue(cams.Break());
        Assert.IsTrue(brk.EffectRunning(c));
        Assert.IsTrue(brk.FixAt(c, out Vector3 at));
        Assert.Less(Vector3.Distance(at, box.transform.position), 0.01f, "you fix it at the camera box");
        Assert.IsFalse(brk.Available(c, out why));
        Assert.AreEqual("the cameras are already broken", why);
        Assert.IsFalse(TacticLibrary.Get("camera_watch").Available(c, out why), "nothing to watch");
    }

    [Test]
    public void TheWatch_RunsOnlyFromTheScreens_AndEndsHoweverItEnds()
    {
        StoreCameras cams = OneCamera();
        var watch = (CameraWatchTactic)TacticLibrary.Get("camera_watch");
        Vector3 screens = new Vector3(10f, 1.5f, 0f);

        // From across the room: it fails, and the cameras stay off.
        var far = new CameraWatchTactic.WatchCameras(watch, _ => screens, 5f) { Hands = _ => new Vector3(-3f, 0f, 0f) };
        Assert.AreEqual(Status.Failure, far.Run(Context(), 0.1f));
        Assert.IsFalse(cams.Watching);

        // At the desk: on for its seconds, then off.
        var near = new CameraWatchTactic.WatchCameras(watch, _ => screens, 5f) { Hands = _ => new Vector3(8.5f, 0f, 0f) };
        KarenContext c = Context();
        Assert.AreEqual(Status.Running, near.Run(c, 0.1f));
        Assert.IsTrue(cams.Watching);
        Status s = Status.Running;
        for (int i = 0; i < 60 && s == Status.Running; i++) s = near.Run(c, 0.1f);
        Assert.AreEqual(Status.Success, s);
        Assert.IsFalse(cams.Watching, "she got up");

        // Cut short (a hunt): off at once.
        var cut = new CameraWatchTactic.WatchCameras(watch, _ => screens, 5f) { Hands = _ => new Vector3(8.5f, 0f, 0f) };
        cut.Run(c, 0.1f);
        Assert.IsTrue(cams.Watching);
        cut.Abort(c);
        Assert.IsFalse(cams.Watching);

        // The cameras break under her: the watch fails.
        var dark = new CameraWatchTactic.WatchCameras(watch, _ => screens, 5f) { Hands = _ => new Vector3(8.5f, 0f, 0f) };
        dark.Run(c, 0.1f);
        cams.Break();
        Assert.AreEqual(Status.Failure, dark.Run(c, 0.1f));
    }

    // ---- item 14: the eyelids ---------------------------------------------------------------------

    [Test]
    public void TheLids_AreOpenOrShut_AndTheTripTakesARealBlinksTime()
    {
        var lids = new BlinkLids();
        Assert.IsTrue(lids.IsOpen);

        // Shut: 76 ms down.
        lids.Set(true);
        float t = 0f;
        while (!lids.IsShut && t < 1f) { lids.Step(0.001f); t += 0.001f; }
        Assert.AreEqual(BlinkLids.CloseSeconds, t, 0.003f);
        Assert.AreEqual(1f, lids.Closed01);

        // They stay shut for as long as the eyes do.
        for (int i = 0; i < 500; i++) lids.Step(0.001f);
        Assert.IsTrue(lids.IsShut);

        // Open: 165 ms up.
        lids.Set(false);
        t = 0f;
        while (!lids.IsOpen && t < 1f) { lids.Step(0.001f); t += 0.001f; }
        Assert.AreEqual(BlinkLids.OpenSeconds, t, 0.003f);
        Assert.AreEqual(0f, lids.Closed01);
        Assert.Less(BlinkLids.CloseSeconds, BlinkLids.OpenSeconds, "a lid falls faster than it rises");
    }

    [Test]
    public void TheQuickestBlink_StillShutsTheLidsCompletely()
    {
        // Eyes shut for one frame only: the lids go all the way down, rest, and come back.
        var lids = new BlinkLids();
        lids.Set(true);
        lids.Step(1f / 60f);
        lids.Set(false);
        bool shut = false;
        float t = 1f / 60f;
        while (!lids.IsOpen && t < 1f)
        {
            lids.Step(0.001f);
            t += 0.001f;
            shut |= lids.IsShut;
        }
        Assert.IsFalse(shut, "eyes open again before the lids were down: they turn back, no full blink is invented");

        // Shut for 80 ms, as a real blink's closing takes: a full blink, never shorter than a real one.
        lids = new BlinkLids();
        lids.Set(true);
        for (int i = 0; i < 80; i++) lids.Step(0.001f);
        Assert.IsTrue(lids.IsShut);
        lids.Set(false);
        t = 0.08f;
        while (!lids.IsOpen && t < 1f) { lids.Step(0.001f); t += 0.001f; }
        Assert.GreaterOrEqual(t, BlinkLids.ShortestBlink - 0.004f);
        Assert.Less(t, 0.4f, "a blink is over within 0.4 s");
    }

    [Test]
    public void TheEyelids_FollowShutOrOpen_NotTheAnalogReading()
    {
        var lids = Add<Eyelids>();
        lids.SetShut(false);
        Assert.AreEqual(0f, lids.Closed01);

        lids.SetShut(true);
        for (int i = 0; i < 20; i++) lids.StepBlink(0.01f);
        Assert.IsTrue(lids.IsFullyClosed, "eyes shut: the lids are fully shut, with no slit left");

        lids.SetShut(false);
        for (int i = 0; i < 40; i++) lids.StepBlink(0.01f);
        Assert.IsTrue(lids.IsFullyOpen);

        // Something else closing the eyes (passing out) isn't fought by "your eyes are open".
        lids.SetClosed(0.7f);
        lids.SetShut(false);
        lids.StepBlink(0.1f);
        Assert.AreEqual(0.7f, lids.Closed01, 1e-4f);
    }

    // ---- item 4: the F1 map's cursor -----------------------------------------------------------------

    [Test]
    public void TheF1Map_LeavesTheMouseToTheGame()
    {
        Assert.IsFalse(KarenDebugOverlay.CursorFree(map: true, replay: false, trickList: false), "F1 alone: no cursor");
        Assert.IsFalse(KarenDebugOverlay.CursorFree(false, false, false));
        Assert.IsTrue(KarenDebugOverlay.CursorFree(false, true, false), "the replay's timeline is dragged");
        Assert.IsTrue(KarenDebugOverlay.CursorFree(true, false, true), "the developer's trick list is clicked");
        Assert.IsFalse(KarenDebugOverlay.CursorFree(false, false, true), "the list is only on the map");
    }

    // ---- item 5: the trick sounds' words ----------------------------------------------------------------

    [Test]
    public void ATrickSound_SaysWhatIsHappening_NotThatSomethingIsAboutTo()
    {
        foreach (TellKind kind in System.Enum.GetValues(typeof(TellKind)))
        {
            string line = KarenNarrator.TrickSound(kind, "in Aisle 3");
            if (line == null) continue;
            StringAssert.DoesNotContain("Warning", line, kind.ToString());
            StringAssert.DoesNotContain("about to happen", line, kind.ToString());
            StringAssert.Contains(GameNames.Antagonist, line, kind + " should say what she is doing");
            Assert.IsTrue(line.EndsWith("."), kind.ToString());
        }
        Assert.AreEqual("A shelf rattles in Aisle 3: " + GameNames.Antagonist + " is knocking its stock onto the floor.",
            KarenNarrator.TrickSound(TellKind.ShelfRattle, "in Aisle 3"));
        StringAssert.Contains("Phantom chime", KarenNarrator.TrickSound(TellKind.DoorMotor, "at the door between the Lobby and the Pavement"));
        StringAssert.Contains("cutting the power", KarenNarrator.TrickSound(TellKind.BallastWhine, "in Aisle 3"));
    }

    [Test]
    public void SoundsThatMeanNothingByThemselves_AreNotLines()
    {
        // Retired with their tricks in 0.3.0, or not a trick's sound at all.
        foreach (TellKind kind in new[] { TellKind.Flicker, TellKind.CrtTick, TellKind.SpeakerCrackle, TellKind.EyeFlicker,
                                          TellKind.PaChime, TellKind.Footsteps, TellKind.RevealSting })
            Assert.IsNull(KarenNarrator.TrickSound(kind, "in Aisle 3"), kind.ToString());
        // Every sound a live trick makes has its line.
        foreach (Tactic t in TacticLibrary.Live.Where(t => !t.OwnMove && t.Tell != TellKind.PaChime && t.Tell != TellKind.Footsteps))
            Assert.IsNotNull(KarenNarrator.TrickSound(t.Tell, "in Aisle 3"), t.Id + "'s sound has no line");
    }

    // ---- item 12: the map's colours and tags -----------------------------------------------------------

    [Test]
    public void EveryRoomAndAisle_HasItsOwnColour()
    {
        var names = new List<string> { "Lobby", "Staff room", "Office", "WC", "Stockroom", "Cold storage", "Backstreet", "Pavement" };
        for (int i = 1; i <= 13; i++) names.Add("Aisle " + i);
        for (int a = 0; a < names.Count; a++)
        for (int b = a + 1; b < names.Count; b++)
        {
            Color ca = StoreFloorPlan.ZoneColour(names[a]), cb = StoreFloorPlan.ZoneColour(names[b]);
            float apart = Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b);
            Assert.Greater(apart, 0.04f, $"{names[a]} and {names[b]} look the same");
        }
        // Aisles side by side differ clearly.
        for (int i = 1; i < 13; i++)
        {
            Color.RGBToHSV(StoreFloorPlan.ZoneColour("Aisle " + i), out float h1, out _, out _);
            Color.RGBToHSV(StoreFloorPlan.ZoneColour("Aisle " + (i + 1)), out float h2, out _, out _);
            float d = Mathf.Abs(h1 - h2);
            Assert.Greater(Mathf.Min(d, 1f - d), 0.15f, $"Aisle {i} and {i + 1} are too close in hue");
        }
        // Soft: nothing saturated, nothing glaring.
        foreach (string n in names)
        {
            Color.RGBToHSV(StoreFloorPlan.ZoneColour(n), out _, out float s, out float v);
            Assert.Less(s, 0.45f, n);
            Assert.Less(v, 0.7f, n);
        }
    }

    [Test]
    public void AShelf_TakesItsAislesColour_Brighter()
    {
        Color floor = StoreFloorPlan.ZoneColour("Aisle 7"), shelf = StoreFloorPlan.SectionColour("Aisle 7 · Noodles, Pasta & Rice");
        Color.RGBToHSV(floor, out float hf, out _, out float vf);
        Color.RGBToHSV(shelf, out float hs, out _, out float vs);
        Assert.AreEqual(hf, hs, 0.01f);
        Assert.Greater(vs, vf + 0.15f);
        Assert.AreEqual("Noodles, Pasta & Rice", StoreFloorPlan.SignDetail("Aisle 7 · Noodles, Pasta & Rice"));
        Assert.AreEqual("A7", MapPainter.AisleTag("Aisle 7"));
        Assert.AreEqual("Stockroom", MapPainter.AisleTag("Stockroom"));
    }

    // ---- item 6: the theme ------------------------------------------------------------------------------

    [Test]
    public void TheTheme_IsThePattern_Softened()
    {
        // Still black, red, white: but none of them pure.
        Color ink = UiTheme.Ink, accent = UiTheme.Accent, text = UiTheme.Text;
        Assert.Less(Mathf.Max(ink.r, ink.g, ink.b), 0.15f, "the black is still dark");
        Assert.Greater(Mathf.Min(ink.r, ink.g, ink.b), 0.05f, "but not pure black");
        Assert.Greater(accent.r, accent.g + 0.3f, "the red is still red");
        Color.RGBToHSV(accent, out _, out float s, out _);
        Assert.Less(s, 0.7f, "but softer than crimson (0.91)");
        Assert.Greater(Mathf.Min(text.r, text.g, text.b), 0.85f, "the white is still light");
        Assert.Greater(text.r, text.b, "but warm");

        Sprite rounded = UiTheme.Rounded;
        Assert.IsNotNull(rounded);
        Assert.Greater(rounded.border.x, 0f, "nine-sliced, so corners stay round at any size");
        Assert.AreEqual(0, rounded.texture.GetPixel(0, 0).a, 0.01f, "the corner is cut");
        Assert.AreEqual(1f, rounded.texture.GetPixel(32, 32).a, 0.01f);
        Assert.Greater(UiTheme.Corner(40f), 0f);
    }

    // ---- item 13: the trick guide ----------------------------------------------------------------------

    [Test]
    public void TheTrickGuide_CoversEveryLiveTrick_InPlainWords()
    {
        // Every trick she can use in play has an entry (the three throws share one).
        foreach (Tactic t in TacticLibrary.Live.Where(t => !t.OwnMove))
        {
            string id = t.Id.StartsWith("throw_") ? "throw_flour" : t.Id;
            Assert.IsNotNull(TrickGuide.Get(id), t.Id + " has no entry in the trick guide");
        }
        // And every entry is something she can do: a live trick, or the hunt itself.
        var ids = new HashSet<string>();
        foreach (TrickGuide.Entry e in TrickGuide.All)
        {
            Assert.IsTrue(ids.Add(e.Id), e.Id + " is listed twice");
            Tactic t = TacticLibrary.Get(e.Id);
            Assert.IsNotNull(t, e.Id + " isn't in the library");
            Assert.IsFalse(t.Draft, e.Id + " is a draft: players never meet it");
            if (e.Chore) Assert.IsTrue(t.LeavesChore, e.Id + " is marked as leaving a job, and doesn't");
            foreach (string words in new[] { e.Title, e.Does, e.Notice, e.Answer })
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(words), e.Id);
                Assert.LessOrEqual(words.Length, 170, e.Id + ": keep it short");
            }
        }
    }

    [Test]
    public void ATricksVideo_IsFoundBesideTheGame_AndNowhereInGit()
    {
        Assert.IsNull(TrickGuide.VideoPath("no_such_trick"));
        string first = TrickGuide.VideoFolders.First();
        StringAssert.Contains("StreamingAssets", first);
        StringAssert.EndsWith(TrickGuide.Folder, first);
        string ignore = File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".gitignore"));
        StringAssert.Contains("/Assets/StreamingAssets/" + TrickGuide.Folder + "/", ignore, "trick videos are media: they stay out of git");
    }
}

// Kehai 0.4.1, the parts that need the store itself: the map stops at the building, every
// camera finds its wall, the office's six monitors are found, and the stockroom has somewhere
// for the fog to come from.
public class Kehai041InTheStoreTests
{
    const string ScenePath = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
    StoreMap map;

    [OneTimeSetUp]
    public void LoadStore()
    {
        if (!EditorSceneManager.GetActiveScene().path.EndsWith("SampleScene.unity"))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        map = StoreMap.Rebuild();
        Physics.SyncTransforms();
    }

    [Test]
    public void TheMap_StopsAtTheBuilding()
    {
        StoreFloorPlan plan = StoreFloorPlan.Build(map);
        TestContext.WriteLine($"bounds x {plan.Bounds.xMin:0.0}..{plan.Bounds.xMax:0.0}, z {plan.Bounds.yMin:0.0}..{plan.Bounds.yMax:0.0}; {plan.Zones.Count} zones");
        Assert.Less(plan.Bounds.yMax, StoreMap.PavementEdge + StoreFloorPlan.Margin + 1.5f, "the map still reaches into the street and the parking lot");
        Assert.Greater(plan.Bounds.yMax, -112f, "the pavement in front of the doors is on it");
        Assert.Less(plan.Bounds.width, 90f);
        Assert.Less(plan.Bounds.height, 100f);

        var names = plan.Zones.Select(z => z.Name).ToList();
        foreach (string room in new[] { "Lobby", "Staff room", "Office", "WC", "Stockroom", "Cold storage", "Backstreet", "Pavement" })
            CollectionAssert.Contains(names, room);
        Assert.GreaterOrEqual(plan.Zones.Count(z => z.Aisle), 10, "the sales floor is drawn aisle by aisle");
        CollectionAssert.DoesNotContain(names, "Street");
        foreach (StoreFloorPlan.Zone z in plan.Zones)
            Assert.IsTrue(plan.Bounds.Contains(z.LabelAt), z.Name + "'s label is off the page");

        Texture2D picture = FloorPlanTexture.Render(plan, 12f);
        try { Assert.Greater(picture.width, 400); }
        finally { Object.DestroyImmediate(picture); }
    }

    [Test]
    public void EveryCamera_FindsItsWall_InItsOwnRoom()
    {
        foreach (StoreCameras.Spot spot in StoreCameras.Spots)
        {
            Assert.IsTrue(StoreCameras.Mount(spot, out Vector3 at), spot.Name + ": no floor under its spot");
            TestContext.WriteLine($"{spot.Name}: hangs at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}), {Vector3.Distance(at, spot.LookAt):0.0} m from what it looks at");
            Assert.Greater(at.y, 1.9f, spot.Name);
            Assert.Less(at.y, 3.3f, spot.Name);
            // A metre in from the wall it hangs on: the area table's edges are the walls' middles.
            Vector3 inRoom = at - new Vector3(spot.Wall.x, 0f, spot.Wall.y).normalized * 1f;
            Assert.AreEqual(spot.Area, StoreMap.AreaAt(inRoom), spot.Name + " hangs in the wrong room");
            Assert.Less(Vector3.Distance(at, spot.LookAt), 30f, spot.Name + " is further from its room than its eye reaches");
        }
    }

    [Test]
    public void TheOffice_HasItsSecurityDesk_ForTheScreensToStandOn()
    {
        Transform desk = CctvWall.FindConsole();
        Assert.IsNotNull(desk, "the security desk");
        Assert.AreEqual("Office", StoreMap.AreaAt(desk.position), "the store's own desk, not the copy on the models island");

        // The wall of screens, built on it: six, in the office, facing into the room.
        var cameras = new List<StoreCamera>();
        var holder = new GameObject("test cameras");
        try
        {
            foreach (StoreCameras.Spot spot in StoreCameras.Spots)
                cameras.Add(StoreCamera.Build(holder.transform, spot, new Vector3(spot.Probe.x, 3f, spot.Probe.y)));
            CctvWall wall = CctvWall.Build(holder.transform, cameras, desk);
            Assert.IsNotNull(wall);
            Assert.AreEqual(6, wall.GetComponentsInChildren<TMPro.TextMeshPro>().Length, "one name a screen");
            Assert.AreEqual("Office", StoreMap.AreaAt(wall.StandPoint), "she stands in the office to watch them");
            TestContext.WriteLine($"the wall is at {wall.transform.position}, faces {wall.Front}, she stands at {wall.StandPoint}");
            Assert.Greater(wall.transform.position.y + CctvWall.Pitch.y * 0.5f + CctvWall.Screen.y * 0.5f, 2.11f, "taller than the old monitors, so it covers them");
        }
        finally { Object.DestroyImmediate(holder); }
    }

    [Test]
    public void TheStockroom_HasSomewhereForTheFogToComeFrom()
    {
        var world = new GameObject("world").AddComponent<KarenWorld>();
        try
        {
            foreach (string room in KarenWorld.FogRooms)
            {
                IReadOnlyList<Vector3> sources = world.FogSources(room);
                TestContext.WriteLine($"{room}: {sources.Count} fog source(s), {world.FridgesIn(room).Count} fridge(s)");
                Assert.Greater(sources.Count, 0, room + " can't be fogged");
            }
        }
        finally { Object.DestroyImmediate(world.gameObject); }
    }
}
