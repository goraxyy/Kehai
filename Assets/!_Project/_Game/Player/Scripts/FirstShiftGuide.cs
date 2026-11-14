using System.Collections.Generic;
using Kehai;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;
using UnityEngine.SceneManagement;

// Text only. A steady outline shares the hover outline without fighting the crosshair.
public sealed class FirstShiftGuide : MonoBehaviour
{
    public static FirstShiftGuide Instance { get; private set; }
    public FirstShiftGuideState State { get; private set; } = new FirstShiftGuideState();
    public static bool WantsTaskList => Instance != null && Instance.State.Step == GuideStep.TaskList;
    public bool Available => Eligible() && State.Running && !CareerRuntime.GuideSkipped;
    ShiftManager shift;
    Component target;
    ShelfUnit shelf;
    ToolSnapPoint mopRack;
    HingeDoor staffDoor, wcDoor, backDoor;
    readonly List<OutlineHighlight> highlights = new List<OutlineHighlight>();
    float nextPoll;
    GUIStyle thoughtStyle, promptStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        Instance = null;
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
    }
    static void Loaded(Scene scene, LoadSceneMode mode) { if (mode == LoadSceneMode.Single) Install(); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (!Edition.AllowGuide || Instance != null) return;
        var manager = FindAnyObjectByType<ShiftManager>();
        if (manager != null) manager.gameObject.AddComponent<FirstShiftGuide>();
    }
    void Awake() { Instance = this; shift = GetComponent<ShiftManager>(); }
    bool Eligible() => shift != null && FirstShiftGuideState.Eligible(Edition.Current,
        shift.IsShiftActive ? shift.ShiftNumber : shift.LastShiftCompleted ? shift.ShiftNumber : shift.UpcomingShiftNumber,
        Application.isBatchMode, KarenBootstrap.Override != null || Kehai.Eval.KehaiEnv.Instance != null,
        Kehai.Replay.ReplayPlayer.Instance != null || Kehai.Replay.ReplayMode.Rendering);
    void OnEnable()
    {
        GameEvents.PlayerPickedUp += PickedUp;
        GameEvents.ShelfRestocked += Stocked;
        GameEvents.SpillCleaned += Cleaned;
        GameEvents.CustomerServed += Served;
        GameEvents.DirectionsGiven += Answered;
        GameEvents.BinBagged += Bagged;
        GameEvents.BagDisposed += Disposed;
        GameEvents.CoffeeDrunk += Coffee;
        GameEvents.DoorUsed += DoorUsed;
        GameEvents.TaskListOpened += TaskOpened;
        PowerSystem.PowerChanged += PowerChanged;
        if (shift != null) shift.ShiftStateChanged += ShiftChanged;
    }
    void OnDisable()
    {
        GameEvents.PlayerPickedUp -= PickedUp;
        GameEvents.ShelfRestocked -= Stocked;
        GameEvents.SpillCleaned -= Cleaned;
        GameEvents.CustomerServed -= Served;
        GameEvents.DirectionsGiven -= Answered;
        GameEvents.BinBagged -= Bagged;
        GameEvents.BagDisposed -= Disposed;
        GameEvents.CoffeeDrunk -= Coffee;
        GameEvents.DoorUsed -= DoorUsed;
        GameEvents.TaskListOpened -= TaskOpened;
        PowerSystem.PowerChanged -= PowerChanged;
        if (shift != null) shift.ShiftStateChanged -= ShiftChanged;
        ClearHighlight();
    }
    void OnDestroy() { if (Instance == this) Instance = null; }
    public void Skip() { State.Skip(); CareerRuntime.SkipGuide(shift != null ? shift.CompletedShifts : 0); ClearHighlight(); }
    void Observe(GuideSignal signal) { if (Available) State.Observe(signal); }
    void PickedUp(Item item)
    {
        if (item == null) return;
        if (item.GetComponent<StockCrate>() != null) Observe(GuideSignal.CrateHeld);
        if (item.type == ItemType.Mop) Observe(GuideSignal.MopHeld);
        if (item.GetComponent<TrashBag>() != null) Observe(GuideSignal.BagHeld);
    }
    void Stocked(ShelfUnit unit, int count) { if (count > 0) Observe(GuideSignal.Stocked); }
    void Cleaned(Dirt dirt) => Observe(GuideSignal.Cleaned);
    void Served(CustomerNPC npc) => Observe(GuideSignal.Served);
    void Answered(CustomerNPC npc) => Observe(GuideSignal.Answered);
    void Bagged(Trashcan can) => Observe(GuideSignal.BagHeld);
    void Disposed(TrashBag bag) => Observe(GuideSignal.Disposed);
    void Coffee(Vector3 at) => Observe(GuideSignal.DrankCoffee);
    void TaskOpened() => Observe(GuideSignal.TaskListOpened);
    void DoorUsed(Component door, bool opened) { if (opened && door != null && door.GetComponent<AutoDoubleDoor>() == null) Observe(GuideSignal.OpenedDoor); }
    void PowerChanged(bool on) => Observe(on ? GuideSignal.PowerRestored : GuideSignal.PowerLost);
    void ShiftChanged()
    {
        if (shift.IsShiftActive) Observe(GuideSignal.ClockedIn);
        if (shift.ClosingTime) Observe(GuideSignal.ClosingTime);
        if (shift.ClosingAnnounced) Observe(GuideSignal.ClosingSaid);
        if (!shift.IsShiftActive && shift.ShiftNumber == 1 && shift.LastShiftCompleted) Observe(GuideSignal.ClockedOut);
    }
    void Update()
    {
        if (!Available) { ClearHighlight(); return; }
        if (GamePause.Paused || FullScreenPanel.AnyOpen) return;
        if (Time.time >= nextPoll) { nextPoll = Time.time + 0.25f; Poll(); }
        GuideStep before = State.Step;
        State.Tick(Time.deltaTime);
        if (State.Step != before) RefreshTarget();
    }
    void Poll()
    {
        var hands = PlayerInteract.Local;
        if (hands == null) return;
        string area = StoreMap.AreaAt(hands.transform.position);
        if (area == "Lobby" || area == "Sales floor" || area == "Staff room") Observe(GuideSignal.EnteredStore);
        if (area == "Staff room") Observe(GuideSignal.EnteredStaffRoom);
        var clock = FindAnyObjectByType<Puncher>();
        if (staffDoor == null && clock != null) staffDoor = NearestDoor(clock.transform.position);
        if (wcDoor == null) wcDoor = DoorIn("WC");
        if (backDoor == null) backDoor = DoorIn("Backstreet");
        if (staffDoor != null && Camera.main != null && Vector3.Angle(Camera.main.transform.forward,
            staffDoor.transform.position - Camera.main.transform.position) < 18f) Observe(GuideSignal.SawStaffDoor);
        foreach (Item item in Item.All)
        {
            if (item == null || !item.isCarried) continue;
            PickedUp(item);
        }
        if (ShelfUnit.NotFullCount > 0) Observe(GuideSignal.NeedsStock);
        if (Dirt.ActiveCount > 0) Observe(GuideSignal.NeedsMop);
        if (CustomerNPC.WaitingCount > 0) Observe(GuideSignal.TillWaiting);
        if (CustomerRequest.PendingCount > 0) Observe(GuideSignal.QuestionAsked);
        foreach (Trashcan bin in Trashcan.All) if (bin != null && bin.IsFull) Observe(GuideSignal.BinFull);
        var energy = shift.burnoutSystem;
        if (energy != null && energy.Energy01 < 0.35f) Observe(GuideSignal.LowEnergy);
        if (!PowerSystem.PowerOn) Observe(GuideSignal.PowerLost);
        foreach (var rack in FindObjectsByType<ToolSnapPoint>())
            if (rack.tool != null && rack.tool.type == ItemType.Mop) { mopRack = rack; break; }
        if (mopRack != null && mopRack.IsToolHome && State.Step == GuideStep.MopRack) Observe(GuideSignal.MopHome);
        ShiftChanged();
        RefreshTarget();
    }
    static HingeDoor NearestDoor(Vector3 at)
    {
        HingeDoor best = null; float distance = float.MaxValue;
        foreach (var door in HingeDoor.All)
        {
            if (door == null) continue;
            float d = (door.transform.position - at).sqrMagnitude;
            if (d < distance) { best = door; distance = d; }
        }
        return best;
    }
    static HingeDoor DoorIn(string area)
    {
        foreach (var door in HingeDoor.All)
            if (door != null && StoreMap.AreaAt(door.transform.position) == area) return door;
        return null;
    }
    Component JobTarget()
    {
        switch (State.Step)
        {
            case GuideStep.Arrival: return FindAnyObjectByType<AutoDoubleDoor>();
            case GuideStep.Lobby: case GuideStep.Door: return staffDoor;
            case GuideStep.ClockIn: case GuideStep.ClockOut: return FindAnyObjectByType<Puncher>();
            case GuideStep.Crate: return FindAnyObjectByType<StockCrate>();
            case GuideStep.Stock:
                if (shelf == null || shelf.IsFull)
                    foreach (var s in FindObjectsByType<ShelfUnit>()) if (!s.IsFull) { shelf = s; break; }
                return shelf;
            case GuideStep.Mop:
                if (mopRack == null || mopRack.tool == null) return null;
                var hands = PlayerInteract.Local;
                string area = hands != null ? StoreMap.AreaAt(hands.transform.position) : "";
                if (area != "Staff room" && area != "Office" && area != "WC" && staffDoor != null && !staffDoor.IsOpen) return staffDoor;
                return RouteDoor(wcDoor, "WC") ?? (Component)mopRack.tool;
            case GuideStep.Spill: return Dirt.All.Count > 0 ? Dirt.All[0] : null;
            case GuideStep.MopRack: return mopRack;
            case GuideStep.Till:
                foreach (var npc in CustomerNPC.All) if (npc != null && npc.IsWaitingToBeServed) return npc;
                return null;
            case GuideStep.Directions:
                foreach (var request in FindObjectsByType<CustomerRequest>())
                    if (request.CurrentStage == CustomerRequest.Stage.Asking || request.CurrentStage == CustomerRequest.Stage.Talking) return request;
                return null;
            case GuideStep.Bin:
                foreach (var bin in Trashcan.All) if (bin != null && bin.IsFull) return bin;
                return null;
            case GuideStep.Skip: return RouteDoor(backDoor, "Backstreet") ?? (Component)FindAnyObjectByType<TrashContainer>();
            case GuideStep.Coffee: return FindAnyObjectByType<CoffeeMachine>();
            case GuideStep.Power: return RouteDoor(backDoor, "Backstreet") ?? (Component)FindAnyObjectByType<BreakerPanel>();
            case GuideStep.Closing: return RouteDoor(staffDoor, "Staff room") ?? (Component)FindAnyObjectByType<ClosingMic>();
            default: return null;
        }
    }
    Component RouteDoor(HingeDoor door, string destination)
    {
        var hands = PlayerInteract.Local;
        return hands != null && StoreMap.AreaAt(hands.transform.position) != destination && door != null && !door.IsOpen ? door : null;
    }
    void RefreshTarget()
    {
        Component next = JobTarget();
        if (next == target) return;
        ClearHighlight(); target = next;
        if (target == null) return;
        Highlight(target);
        if(State.Step==GuideStep.Arrival)
        {
            foreach(var door in FindObjectsByType<AutoDoubleDoor>()) Highlight(door);
            Highlight(staffDoor);Highlight(FindAnyObjectByType<Puncher>());
        }
        else if(State.Step==GuideStep.Lobby || State.Step==GuideStep.Door) Highlight(FindAnyObjectByType<Puncher>());
    }
    void Highlight(Component value)
    {
        if(value==null)return;
        var outline = value.GetComponent<OutlineHighlight>() ?? value.GetComponentInChildren<OutlineHighlight>();
        if (outline == null)
        {
            Material material = null;
            foreach (var existing in FindObjectsByType<OutlineHighlight>())
                if (existing.outlineMaterial != null) { material = existing.outlineMaterial; break; }
            if (material != null)
            {
                outline = value.gameObject.AddComponent<OutlineHighlight>();
                outline.outlineMaterial = material;
            }
        }
        if (outline != null && !highlights.Contains(outline)) { outline.SetGuided(true); highlights.Add(outline); }
    }
    void ClearHighlight()
    {
        foreach (var outline in highlights) if (outline != null) outline.SetGuided(false);
        highlights.Clear(); target = null;
    }
    bool LookingAtTarget()
    {
        var hands = PlayerInteract.Local;
        if (hands == null || target == null) return false;
        if (hands.CurrentTarget is ShelfSlot slot) return target is ShelfUnit unit && slot.owner == unit;
        var aimed = hands.CurrentTarget as Component;
        if (aimed != null && (aimed.transform.IsChildOf(target.transform) || target.transform.IsChildOf(aimed.transform))) return true;
        // Nearby-E doors predate the interaction interface; their panel can still be looked at.
        Transform eye = hands.rayOrigin;
        return eye != null && Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, hands.interactRange) &&
            (hit.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.transform));
    }
    void OnGUI()
    {
        if (!Available || GamePause.Paused || FullScreenPanel.AnyOpen || Event.current.type != EventType.Repaint) return;
        int size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 45f, 16f, 28f));
        thoughtStyle = UiTheme.Label(size, UiTheme.Text, TextAnchor.MiddleCenter, false);
        promptStyle = UiTheme.Label(size - 2, UiTheme.Dim, TextAnchor.MiddleCenter, false);
        string thought = State.ThoughtVisible ? FirstShiftGuideState.Thought(State.Step, shelf != null ? shelf.section : null) : "";
        string prompt = State.Step == GuideStep.TaskList || LookingAtTarget() ? FirstShiftGuideState.Prompt(State.Step) : "";
        // Automatic doors never receive an E prompt. Intermediate manual doors use their own action.
        if (target is AutoDoubleDoor) prompt = "";
        else if (LookingAtTarget() && target is HingeDoor && State.Step != GuideStep.Door) prompt = "E to open doors";
        float w = Mathf.Min(Screen.width - 40f, 920f);
        var rect = new Rect((Screen.width - w) / 2f, Screen.height * 0.8f, w, size * 4.2f);
        if (thought.Length > 0 || prompt.Length > 0)
        {
            UiTheme.Fill(rect, UiTheme.Ink, size * 0.5f);
            GUI.Label(new Rect(rect.x + 12f, rect.y, rect.width - 24f, size * 2.3f), thought, thoughtStyle);
            GUI.Label(new Rect(rect.x, rect.y + size * 2.3f, rect.width, size * 1.5f), prompt, promptStyle);
        }
    }
}
