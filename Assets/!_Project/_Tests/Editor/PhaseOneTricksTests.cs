using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// Native integration tests. The pure policies are additionally executed by
// tools/tricks/check_phase1_rules.py without launching Unity.
public class PhaseOneTricksTests
{
    static readonly string[] Ids = { "flash_sale", "screen_loop", "room_lights", "freezer_alarm", "complaint" };
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    GameObject root;
    Func<Vector3, string> previousRoom;
    KehaiEdition? previousEdition;
    [SetUp]
    public void Setup()
    {
        previousEdition = Edition.EditorOverride;
        Edition.EditorOverride = KehaiEdition.Testing;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        root = new GameObject("PhaseOneTricksTests");
        previousRoom = RoomLightSwitch.RoomOf;
        RoomLightSwitch.RoomOf = p => p.x < 0f ? "Cold storage" : "Office";
        FlashSaleState.End();
        CustomerComplaint.ResetForShift();
    }
    [TearDown]
    public void Cleanup()
    {
        FlashSaleState.End();
        Object.DestroyImmediate(root);
        RoomLightSwitch.RoomOf = previousRoom;
        TaskManager.CamerasBroken = null;
        CustomerComplaint.ResetForShift();
        Edition.EditorOverride = previousEdition;
    }
    T Add<T>(bool active = true) where T : Component
    {
        var go = new GameObject(typeof(T).Name);
        go.transform.SetParent(root.transform);
        go.SetActive(active);
        return go.AddComponent<T>();
    }
    static void Set(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
    static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    ShiftManager OpenShift()
    {
        var shift = Add<ShiftManager>();
        // Activate only the pure career state: no layout build, save or clock-start side effects.
        var career = (CareerProgress)typeof(ShiftManager).GetField("career", Private).GetValue(shift);
        Assert.IsTrue(career.TryStart());
        Set(shift, "CustomersAllowed", true);
        Set(shift, "TimeRemaining", 300f);
        return shift;
    }
    ClosingMic Mic(PaSystem pa)
    {
        // Inactive world supplies the existing mic dependency without creating UI/audio.
        var world = Add<KarenWorld>(false);
        Set(world, "Pa", pa);
        typeof(KarenWorld).GetProperty("Instance").SetValue(null, world);
        return Add<ClosingMic>();
    }
    [Test]
    public void FiveApprovedTricksAppendWithoutRenumberingEarlierActionsOrTells()
    {
        Assert.AreEqual(34, TacticLibrary.All.Count);
        for (int i = 0; i < Ids.Length; i++)
        {
            var tactic = TacticLibrary.Get(Ids[i]);
            Assert.IsNotNull(tactic);
            Assert.IsFalse(tactic.Draft);
            Assert.IsTrue(tactic.LeavesChore);
            Assert.AreEqual(29 + i, TacticLibrary.IndexOf(tactic));
        }
        Assert.AreEqual(27, TacticLibrary.IndexOf(TacticLibrary.Get("camera_watch")));
        Assert.AreEqual(28, TacticLibrary.IndexOf(TacticLibrary.Get("camera_break")));
        Assert.IsTrue(TacticLibrary.Get("screen_loop").IsHandsOn);
        Assert.IsTrue(TacticLibrary.Get("freezer_alarm").IsHandsOn);
        Assert.IsTrue(TacticLibrary.Get("complaint").IsHandsOn);
        Assert.IsFalse(TacticLibrary.Get("flash_sale").IsHandsOn);
        Assert.IsFalse(TacticLibrary.Get("room_lights").IsHandsOn);
        Assert.AreEqual((int)TellKind.MonitorHum + 1, (int)TellKind.FrozenClock);
    }
    [TestCase("flash_sale")][TestCase("screen_loop")][TestCase("room_lights")]
    [TestCase("freezer_alarm")][TestCase("complaint")]
    public void NoNewTrickChoreIsAvailableAfterTheDoorsClose(string id)
    {
        var shift = OpenShift();
        Set(shift, "CustomersAllowed", false);
        var context = new KarenContext { Shift = shift };
        Assert.IsFalse(TacticLibrary.Get(id).Available(context, out string why));
        Assert.AreEqual(FairnessGuard.ClosedWhy, why);
    }
    [Test]
    public void PaOffCannotStartOrCancelSaleAndNeverChangesArrivalPacing()
    {
        var shift = OpenShift();
        var pa = Add<PaSystem>(false);
        var spawner = Add<CustomerSpawner>();
        var first = spawner.firstArrivalRange;
        var interval = spawner.spawnIntervalRange;
        pa.SetPowered(false);
        Assert.IsFalse(FlashSaleState.Start(shift, pa));
        pa.SetPowered(true);
        Assert.IsTrue(FlashSaleState.Start(shift, pa));
        Assert.AreEqual(4, spawner.Cap);
        pa.SetPowered(false);
        Assert.IsTrue(FlashSaleState.Active);
        Assert.AreEqual(4, spawner.Cap);
        Assert.AreEqual(first, spawner.firstArrivalRange);
        Assert.AreEqual(interval, spawner.spawnIntervalRange);
        FlashSaleState.End();
        Assert.AreEqual(2, spawner.Cap);
    }
    [Test]
    public void SaleMicRequiresTwentySecondsAndPowerLossCancelsItsPartialNativeHold()
    {
        var shift = OpenShift();
        var pa = Add<PaSystem>(false);
        var mic = Mic(pa);
        Assert.IsTrue(FlashSaleState.Start(shift, pa));
        Assert.IsTrue(mic.CanHold(null));
        Assert.AreEqual(20f, mic.HoldDuration);
        StringAssert.Contains("20 s", mic.GetPrompt());
        mic.Interact(null);
        Assert.IsTrue(FlashSaleState.Active, "a tap never calls off a sale");
        var camera = Add<Camera>(); camera.tag = "MainCamera";
        var hands = Add<PlayerInteract>();
        hands.forcedHold = true;
        Field(hands, "currentTarget", mic);
        Field(hands, "holdTarget", mic);
        Field(hands, "holdTimer", 19f);
        pa.SetPowered(false);
        typeof(PlayerInteract).GetMethod("HandleHoldInteraction", Private).Invoke(hands, null);
        Assert.IsFalse(hands.IsHolding);
        Assert.AreEqual(0f, typeof(PlayerInteract).GetField("holdTimer", Private).GetValue(hands));
        mic.OnHoldComplete(hands);
        Assert.IsTrue(FlashSaleState.Active, "power loss must not complete a partial hold");
        pa.SetPowered(true);
        Assert.IsTrue(mic.CanHold(hands));
        Assert.AreEqual(20f, mic.HoldDuration, "restarting needs the full hold");
    }
    [Test]
    public void SaleEndsWithTheDoorsAndClosingAnnouncementTakesPriorityAtTheMic()
    {
        var shift = OpenShift();
        var pa = Add<PaSystem>(false);
        var mic = Mic(pa);
        FlashSaleState.Start(shift, pa);
        Set(shift, "TimeRemaining", 0f);
        Assert.IsFalse(mic.CanHold(null));
        StringAssert.Contains("store is closed", mic.GetPrompt());
        Set(shift, "CustomersAllowed", false);
        FlashSaleState.Step(shift);
        Assert.IsFalse(FlashSaleState.Active);
        Assert.IsFalse(FlashSaleState.Start(shift, pa));
    }
    [Test]
    public void LocalLightsFlickerExactlyTwiceAndAbortRestoresThem()
    {
        var light = Add<Light>(); light.enabled = true;
        var sw = Add<RoomLightSwitch>(); sw.Configure("Office", new[] { light });
        var flicker = sw.FlickerTwice();
        Assert.IsTrue(flicker.MoveNext()); Assert.IsFalse(light.enabled);
        Assert.IsTrue(flicker.MoveNext()); Assert.IsTrue(light.enabled);
        Assert.IsTrue(flicker.MoveNext()); Assert.IsFalse(light.enabled);
        Assert.IsTrue(flicker.MoveNext()); Assert.IsTrue(light.enabled);
        Assert.IsFalse(flicker.MoveNext()); Assert.IsFalse(sw.Warning);
        flicker = sw.FlickerTwice(); flicker.MoveNext();
        (flicker as IDisposable).Dispose();
        Assert.IsTrue(light.enabled); Assert.IsFalse(sw.Warning);
        sw.SetOff(true);
        Assert.IsFalse(light.enabled);
        Assert.IsFalse(RoomLightSwitch.Allows(Vector3.right));
        Assert.IsTrue(RoomLightSwitch.Allows(Vector3.left));
        sw.Interact(null);
        Assert.IsTrue(light.enabled);
    }
    FreezerAlarm Alarm()
    {
        var door = Add<FurnitureDoor>(); door.transform.position = Vector3.left;
        var alarm = door.gameObject.AddComponent<FreezerAlarm>(); alarm.Configure(door);
        return alarm;
    }
    [Test]
    public void FreezerMasksBothAuthorsAndAllStepKindsOnlyInsideItsPhysicalRoom()
    {
        var alarm = Alarm();
        Assert.IsTrue(alarm.StartAlarm());
        foreach (NoiseKind kind in Enum.GetValues(typeof(NoiseKind)))
        {
            Assert.AreEqual(FreezerAlarm.IsStep(kind), FreezerAlarm.Masks(Vector3.left, kind));
            Assert.IsFalse(FreezerAlarm.Masks(Vector3.right, kind));
        }
        long cursor = NoiseBus.Latest;
        NoiseBus.Emit(Vector3.left, 1f, NoiseKind.Sprint, NoiseAuthor.Player);
        NoiseBus.Emit(Vector3.left, 1f, NoiseKind.KarenStep, NoiseAuthor.Karen);
        NoiseBus.Emit(Vector3.right, 1f, NoiseKind.Footstep, NoiseAuthor.Player);
        NoiseBus.Emit(Vector3.left, 1f, NoiseKind.Serve, NoiseAuthor.Player);
        var heard = new List<NoiseEvent>(); NoiseBus.ReadSince(ref cursor, heard);
        Assert.AreEqual(2, heard.Count);
        Assert.IsTrue(heard.Any(n => n.Kind == NoiseKind.Serve));
        alarm.Door.Interact(null);
        Assert.IsFalse(alarm.Ringing);
        Assert.IsFalse(FreezerAlarm.Masks(Vector3.left));
    }
    StoreCameras Cameras(out StoreCamera first, out StoreCamera other)
    {
        var cameras = Add<StoreCameras>();
        typeof(StoreCameras).GetProperty("Instance").SetValue(null, cameras);
        first = Add<StoreCamera>(); other = Add<StoreCamera>();
        Set(first, "Eye", first.gameObject.AddComponent<SightSensor>());
        Set(other, "Eye", other.gameObject.AddComponent<SightSensor>());
        var list = (List<StoreCamera>)typeof(StoreCameras).GetField("cameras", Private).GetValue(cameras);
        list.Add(first); list.Add(other);
        Set(cameras, "Box", Add<CameraBox>());
        TaskManager.CamerasBroken = () => cameras.NeedsReset;
        return cameras;
    }
    [Test]
    public void OnlyOneFeedLoopsItsClockStopsAndTheExistingTwentySecondBoxResetsIt()
    {
        var cameras = Cameras(out var first, out var other);
        Set(first, "FeedClock", 123f);
        Assert.IsTrue(cameras.Loop(first));
        Assert.IsTrue(first.Looped); Assert.IsFalse(other.Looped);
        first.RenderFeed();
        Assert.AreEqual(123f, first.FeedClock);
        Assert.IsFalse(cameras.Loop(other));
        Assert.IsTrue(cameras.Box.CanHold(null));
        Assert.AreEqual(20f, cameras.Box.HoldDuration);
        var tasks = Add<TaskManager>();
        Assert.IsFalse(tasks.CamerasWorking);
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.FixCameras));
        Assert.IsTrue(cameras.Fix());
        Assert.IsFalse(first.Looped); Assert.IsFalse(cameras.NeedsReset);
        Assert.IsTrue(tasks.CamerasWorking);
        Assert.IsFalse(cameras.Fix(), "fix/reset emits its completion only once");
    }
    [Test]
    public void CameraBreakAndLoopShareOneResetAndNewShiftClearsBoth()
    {
        var cameras = Cameras(out var first, out var other);
        cameras.Loop(first); cameras.Break();
        Assert.IsTrue(cameras.Broken && cameras.Looped);
        cameras.ResetForShift();
        Assert.IsFalse(cameras.NeedsReset);
        Assert.IsFalse(first.IsBroken || other.IsBroken || first.Looped);
    }
    [Test]
    public void FrozenDisplayKeepsItsTimestampWhileItsRealSensorStillReportsMovement()
    {
        var cameras = Cameras(out var first, out _);
        var you = Add<PlayerPresence>();
        typeof(PlayerPresence).GetMethod("Awake", Private).Invoke(you, null);
        you.transform.position = Vector3.forward * 3f;
        Set(first, "FeedClock", 123f);
        Assert.IsTrue(cameras.Loop(first));
        cameras.SetWatching(true);
        for (int i = 0; i < 100; i++) cameras.Step(.1f);
        Assert.Greater(cameras.Sightings, 0);
        Assert.AreEqual(you.Position, first.Eye.LastSeenPosition);
        you.transform.position = Vector3.forward * 4f;
        cameras.Step(.1f);
        Assert.AreEqual(you.Position, first.Eye.LastSeenPosition);
        first.RenderFeed();
        Assert.AreEqual(123f, first.FeedClock);
        cameras.SetWatching(false);
        Assert.AreEqual(0f, first.Eye.Awareness);
    }
    [Test]
    public void TrainingScreenAndCctvFactoriesKeepLogicalTargetsWithoutAnyUiOrPictures()
    {
        Edition.EditorOverride = KehaiEdition.Training;
        var screen = KarenScreen.Ensure(); screen.transform.SetParent(root.transform);
        screen.Subtitle("silent subtitle", 2f); screen.Banner("silent banner", 2f); screen.SetFade(1f);
        typeof(KarenScreen).GetMethod("Update", Private).Invoke(screen, null);
        var cameras = Cameras(out var first, out _);
        var wall = CctvWall.Build(root.transform, cameras.Cameras, root.transform);
        Assert.IsNotNull(wall);
        Assert.IsEmpty(root.GetComponentsInChildren<TMPro.TMP_Text>(true));
        Assert.IsEmpty(root.GetComponentsInChildren<Canvas>(true));
        Assert.IsFalse(StoreCameras.HasGraphics);
        Set(first, "Feed", first.gameObject.AddComponent<Camera>());
        first.RenderFeed(); cameras.Loop(first); cameras.RenderAllNow();
        Assert.IsNull(first.Picture);
        Assert.IsTrue(cameras.NeedsReset);
        Assert.IsTrue(cameras.Fix());
    }
    [Test]
    public void TrainingPaKeepsChimeThenSpeechSaleTimingWithoutUiAudioOrClips()
    {
        Edition.EditorOverride = KehaiEdition.Training;
        var clips = (Dictionary<string, AudioClip>)typeof(ProceduralAudio).GetField("cache", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var clipsBefore = clips.Keys.ToArray();
        var shift = OpenShift();
        var pa = Add<PaSystem>();
        var world = Add<KarenWorld>(false); Set(world, "Pa", pa);
        var context = new KarenContext { Shift = shift, World = world };
        var sale = new FlashSaleTactic.SaleAnnouncement();
        int chimes = 0, speeches = 0;
        pa.ChimeStarted += _ => chimes++;
        pa.SpeechStarted += _ => speeches++;
        var update = typeof(PaSystem).GetMethod("Update", Private);
        Assert.AreEqual(Status.Running, sale.Run(context, .1f));
        update.Invoke(pa, null);
        Assert.AreEqual(1, chimes); Assert.AreEqual(0, speeches);
        Assert.IsFalse(FlashSaleState.Active, "the chime alone cannot start the sale");
        Field(pa, "phaseEnds", Time.time - 1f);
        update.Invoke(pa, null);
        Assert.AreEqual(1, speeches); Assert.IsTrue(FlashSaleState.Active);
        Assert.AreEqual(Status.Success, sale.Run(context, .1f));
        Field(pa, "phaseEnds", Time.time - 1f);
        update.Invoke(pa, null);
        Assert.IsFalse(pa.Busy);
        Assert.IsTrue(pa.SayAs("You", ClosingMic.Words));
        var alarm = Alarm(); alarm.StartAlarm();
        typeof(FreezerAlarm).GetMethod("Update", Private).Invoke(alarm, null);
        Assert.IsEmpty(root.GetComponentsInChildren<AudioSource>(true));
        Assert.IsEmpty(root.GetComponentsInChildren<TMPro.TMP_Text>(true));
        CollectionAssert.AreEquivalent(clipsBefore, clips.Keys, "Training must not synthesise PA/alarm audio");
        Assert.IsTrue(KarenScreen.Instance == null, "Training PA must not invoke the screen factory");
    }
    [Test]
    public void ComplaintWaitingAgentResumesItsExitAfterItIsHeardOrTimesOut()
    {
        var source = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.Translate(Vector3.down * .1f), size = new Vector3(20f, .2f, 20f) };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), new List<NavMeshBuildSource> { source },
            new Bounds(Vector3.zero, new Vector3(20f, 5f, 20f)), Vector3.zero, Quaternion.identity);
        Assert.IsNotNull(data);
        var mesh = NavMesh.AddNavMeshData(data);
        try
        {
            foreach (bool heard in new[] { false, true })
            {
                var complaint = Complaint();
                var npc = complaint.GetComponent<CustomerNPC>();
                npc.agent = npc.gameObject.AddComponent<NavMeshAgent>();
                Assert.IsTrue(npc.agent.Warp(Vector3.zero));
                npc.exitPoint = new GameObject("complaint exit").transform;
                npc.exitPoint.SetParent(root.transform);
                npc.exitPoint.position = Vector3.forward * 5f;
                ((ComplaintPatience)typeof(CustomerComplaint).GetField("patience", Private).GetValue(complaint)).Arrive();
                npc.WaitForComplaint(); npc.agent.isStopped = true;
                if (heard) complaint.HearOut(); else complaint.Step(91f);
                Assert.IsFalse(npc.agent.isStopped, "resolved shoppers must walk out, not wait for stuck-timeout despawn");
                Assert.IsFalse(npc.IsWaitingToBeServed);
            }
        }
        finally { mesh.Remove(); Object.DestroyImmediate(data); }
    }
    CustomerComplaint Complaint()
    {
        var customer = Add<CustomerNPC>();
        var complaint = customer.gameObject.AddComponent<CustomerComplaint>();
        Field(complaint, "npc", customer); Set(complaint, "Active", true);
        return complaint;
    }
    [Test]
    public void ComplaintTimeoutIsLostExactlyOnceButHearingIsSuccessfulAndDistinct()
    {
        var ignored = Complaint();
        ignored.Step(121f);
        Assert.IsFalse(ignored.Active); Assert.IsFalse(ignored.HeardOut);
        Assert.AreEqual(1, CustomerComplaint.LostThisShift);
        ignored.Step(1000f);
        Assert.AreEqual(1, CustomerComplaint.LostThisShift);
        var heard = Complaint();
        ((ComplaintPatience)typeof(CustomerComplaint).GetField("patience", Private).GetValue(heard)).Arrive();
        Assert.IsTrue(heard.CanHold);
        Assert.AreEqual(20f, heard.GetComponent<CustomerNPC>().HoldDuration);
        heard.HearOut();
        Assert.IsFalse(heard.Active); Assert.IsTrue(heard.HeardOut);
        Assert.AreEqual(1, CustomerComplaint.LostThisShift);
    }
    [Test]
    public void NewJobsNameTheirFixLocationsAndPreventClockoutUntilAnsweredOrExpired()
    {
        var tasks = Add<TaskManager>();
        Assert.IsTrue(tasks.AllComplete);
        var light = Add<Light>(); light.enabled = true;
        var sw = Add<RoomLightSwitch>(); sw.Configure("Office", new[] { light }); sw.SetOff(true);
        var alarm = Alarm(); alarm.StartAlarm();
        var complaint = Complaint();
        Assert.IsFalse(tasks.AllComplete);
        Assert.IsFalse(tasks.RoomsLit || tasks.FreezersClosed || tasks.ComplaintsHeard);
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.RestoreRoomLights && t.Detail.Contains("Office")));
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.CloseFreezer && t.Detail.Contains("Cold storage")));
        Assert.IsTrue(tasks.Tasks.Any(t => t.Kind == TaskManager.TaskKind.HearComplaint && t.Detail.Contains("till")));
        sw.Interact(null); alarm.Close(); complaint.Step(121f);
        Assert.IsTrue(tasks.AllComplete, "a lost complaint clears its chore instead of wedging the shift");
    }
    [Test]
    public void PhysicalCuesNeverBecomeGlobalLinesRevealingAnUnseenTrick()
    {
        foreach (var cue in new[] { TellKind.FrozenClock, TellKind.RoomFlicker, TellKind.FreezerAlarm, TellKind.AngryWalk })
            Assert.IsNull(KarenNarrator.TrickSound(cue, "in a hidden room"));
    }
}
