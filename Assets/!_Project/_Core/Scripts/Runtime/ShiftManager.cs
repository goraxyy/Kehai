using System;
using UnityEngine;

// A shift runs from clocking in at the puncher to clocking out again.
//
// 0.4.1: when the shift's time is up it's closing time, and nothing more: customers keep
// coming until you announce over the PA that the store is closed (the PA microphone in the
// staff room, ClosingMic). The announcement closes the doors. Until it's made, the shift
// can't be ended while a customer is in the store. Before 0.4.1 the doors closed by
// themselves when the countdown reached zero.
public class ShiftManager : MonoBehaviour
{
    [Header("Shift Settings")]
    [Min(10f)]
    [Tooltip("How long one shift lasts, in seconds. 300 = 5 minutes. Safe to change " +
             "in the Inspector while playing — the clock on a running shift adjusts to match.")]
    public float shiftDurationSeconds = 300f;   // 5 minutes

    // Shown next to the seconds field so the value is readable at a glance.
    public string ShiftLengthLabel
    {
        get
        {
            int whole = Mathf.CeilToInt(shiftDurationSeconds);
            return $"{whole / 60:0}:{whole % 60:00}";
        }
    }

    [Header("References")]
    public TaskManager taskManager;
    public CustomerSpawner customerSpawner;
    public BurnoutSystem burnoutSystem;

    public bool IsShiftActive { get; private set; }
    public bool CustomersAllowed { get; private set; }
    public float TimeRemaining { get; private set; }
    public int ShiftNumber { get; private set; }

    // Karen's overtime (0.3.0): she stops the time clock, and the shift's countdown stops with
    // it. Customers keep coming, and the task list gains "Restart the time clock", until
    // someone restarts it at the clock. Cleared when a shift starts or ends.
    public bool IsFrozen { get; private set; }

    // Seconds the clock has stood stopped this shift: the eval's time-out allows for them.
    public float FrozenSeconds { get; private set; }

    public event Action ShiftStateChanged;
    public event Action FreezeChanged;

    void Awake()
    {
        if (taskManager == null) taskManager = FindAnyObjectByType<TaskManager>();
        if (customerSpawner == null) customerSpawner = FindAnyObjectByType<CustomerSpawner>();
        if (burnoutSystem == null) burnoutSystem = FindAnyObjectByType<BurnoutSystem>();
    }

    void Start()
    {
        // Nothing runs until the player clocks in.
        SetCustomersAllowed(false);
    }

    void Update()
    {
        if (IsShiftActive && IsFrozen) FrozenSeconds += Time.deltaTime;
        if (!IsShiftActive || !CustomersAllowed || IsFrozen || TimeRemaining <= 0f) return;

        TimeRemaining -= Time.deltaTime;
        if (TimeRemaining <= 0f) ReachClosingTime();
    }

    // ---- closing (0.4.1) ---------------------------------------------------------------------

    // The countdown has run out. Customers still come in until the announcement.
    public bool ClosingTime => IsShiftActive && TimeRemaining <= 0f;

    // The store was announced closed over the PA this shift.
    public bool ClosingAnnounced { get; private set; }

    // Closing time, and nobody has said so yet: the task list asks for the announcement.
    public bool AwaitingAnnouncement => ClosingTime && !ClosingAnnounced;

    // The doors are closed to customers: the announcement has been made, and the shift waits
    // for the clock-out. From then on Karen makes no new chores (0.3.0, the owner's call of 8
    // November).
    public bool DoorsClosed => IsShiftActive && !CustomersAllowed;

    // What the countdown does when it reaches zero: closing time starts. Public for tests.
    public void ReachClosingTime()
    {
        if (!IsShiftActive || closingTold) return;
        TimeRemaining = 0f;
        closingTold = true;
        if (taskManager != null) taskManager.NotifyChanged();
        ShiftStateChanged?.Invoke();
    }
    bool closingTold;

    // The announcement, from the PA microphone: the store is closed, no more customers come in.
    // Only at closing time, and once. False when it isn't time, or it's been said.
    public bool AnnounceClosing()
    {
        if (!AwaitingAnnouncement) return false;
        CloseDoors();
        return true;
    }

    // The doors close: what the announcement does. Public for tests, and for anything that
    // has to close the store without the microphone.
    public void CloseDoors()
    {
        if (!IsShiftActive || !CustomersAllowed) return;
        TimeRemaining = 0f;
        closingTold = true;
        ClosingAnnounced = true;
        SetCustomersAllowed(false);      // doors close; shift ends when the player punches out
        if (taskManager != null) taskManager.NotifyChanged();
        ShiftStateChanged?.Invoke();
    }

    // Customers in the store right now, from every spawner (unless a test says otherwise).
    public static System.Func<int> CountCustomers = () => CustomerSpawner.InStore;
    public static int CustomersInStore => CountCustomers();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => CountCustomers = () => CustomerSpawner.InStore;

    // Until the store is announced closed, the shift can't end with a customer inside.
    public bool CustomersHoldTheShift => IsShiftActive && !ClosingAnnounced && CustomersInStore > 0;

    // The shift can't be closed with spills on the floor, a full bin, or gaps on the shelves;
    // nor, before the closing announcement, with a customer in the store (0.4.1).
    public bool CanClockOut => !IsShiftActive || (taskManager == null || taskManager.AllComplete) && !CustomersHoldTheShift;

    // Why the time clock refuses, in the clock's own words.
    public string ClockOutBlocker
    {
        get
        {
            if (CanClockOut) return string.Empty;
            if (taskManager != null && !taskManager.AllComplete) return "Finish your tasks first";
            return AwaitingAnnouncement ? "Announce closing on the PA first" : "Customers are still in the store";
        }
    }

    // Called by the puncher.
    public void ToggleShift()
    {
        if (!IsShiftActive) { StartShift(); return; }

        if (!CanClockOut)
        {
            Debug.Log("Can't clock out yet — finish the shift tasks first.");
            GameEvents.RaisePunchAttempted(false);
            return;
        }

        GameEvents.RaisePunchAttempted(true);
        EndShift();
    }

    public void StartShift()
    {
        if (IsShiftActive) return;

        ShiftNumber++;
        IsShiftActive = true;
        SetFrozen(false);
        FrozenSeconds = 0f;
        ClosingAnnounced = false;
        closingTold = false;
        TimeRemaining = shiftDurationSeconds;
        SetCustomersAllowed(true);

        if (taskManager != null)
            taskManager.BeginShift(ShiftNumber);

        if (burnoutSystem != null)
            burnoutSystem.ResetForNewShift(ShiftNumber - 1);

#if UNITY_EDITOR
        appliedDuration = shiftDurationSeconds;
#endif

        Debug.Log($"Shift {ShiftNumber} started ({ShiftLengthLabel}).");
        ShiftStateChanged?.Invoke();
    }

    // Places the career: the next shift started will be number `completed + 1`. For the
    // eval harness and the ablation runner, which start careers part-way through.
    public void SetShiftNumber(int completed)
    {
        if (IsShiftActive) return;
        ShiftNumber = Mathf.Max(0, completed);
    }

    // Stop the clock. Only during a shift that's still counting down.
    public bool Freeze()
    {
        if (!IsShiftActive || IsFrozen || TimeRemaining <= 0f) return false;
        SetFrozen(true);
        return true;
    }

    // Start it again, from where it stopped.
    public bool Unfreeze()
    {
        if (!IsFrozen) return false;
        SetFrozen(false);
        return true;
    }

    void SetFrozen(bool frozen)
    {
        if (IsFrozen == frozen) return;
        IsFrozen = frozen;
        FreezeChanged?.Invoke();
        if (taskManager != null) taskManager.NotifyChanged();
        ShiftStateChanged?.Invoke();
    }

    public void EndShift()
    {
        if (!IsShiftActive) return;

        IsShiftActive = false;
        SetFrozen(false);
        TimeRemaining = 0f;
        ClosingAnnounced = false;
        closingTold = false;
        SetCustomersAllowed(false);

        if (taskManager != null)
            taskManager.EndShift();

        Debug.Log($"Shift {ShiftNumber} ended.");
        ShiftStateChanged?.Invoke();
    }

#if UNITY_EDITOR
    float appliedDuration;

    // Retuning the shift length mid-playtest is the whole point of it being a field, so
    // move the running clock by however much the length changed instead of waiting for
    // the next shift. Re-opens the doors if the shift was over and has just been extended.
    void OnValidate()
    {
        if (!Application.isPlaying || !IsShiftActive) return;

        float delta = shiftDurationSeconds - appliedDuration;
        if (Mathf.Approximately(delta, 0f)) return;

        appliedDuration = shiftDurationSeconds;
        TimeRemaining = Mathf.Max(0f, TimeRemaining + delta);

        // Extended past closing time: the doors open again and the announcement is owed again.
        if (TimeRemaining > 0f && !CustomersAllowed) { ClosingAnnounced = false; closingTold = false; SetCustomersAllowed(true); }

        ShiftStateChanged?.Invoke();
    }
#endif

    void SetCustomersAllowed(bool allowed)
    {
        CustomersAllowed = allowed;
        if (customerSpawner != null)
            customerSpawner.spawningEnabled = allowed;
    }
}
