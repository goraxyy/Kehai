using System;
using UnityEngine;

// A shift runs from clocking in at the puncher to clocking out again.
// The clock stops letting customers in once the shift time is up, but the shift itself
// only ends when the player punches out.
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

    // Asked when the employee tries to clock out with everything done. Returning true
    // refuses — Karen's overtime (Karen.md §8.2). Null means nobody objects.
    public System.Func<bool> ClockOutGuard;

    public bool IsShiftActive { get; private set; }
    public bool CustomersAllowed { get; private set; }
    public float TimeRemaining { get; private set; }
    public int ShiftNumber { get; private set; }

    public event Action ShiftStateChanged;

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
        if (!IsShiftActive || !CustomersAllowed) return;

        TimeRemaining -= Time.deltaTime;
        if (TimeRemaining <= 0f)
        {
            TimeRemaining = 0f;
            SetCustomersAllowed(false);      // doors close; shift ends when the player punches out
            ShiftStateChanged?.Invoke();
        }
    }

    // The shift can't be closed with spills on the floor, a full bin, or gaps on the shelves.
    public bool CanClockOut => !IsShiftActive || taskManager == null || taskManager.AllComplete;

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

        if (ClockOutGuard != null && ClockOutGuard())
        {
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

    // More shift: the doors stay open longer and customers keep coming. Used by Karen's
    // overtime and by the lecture after being caught.
    public void AddOvertime(float seconds)
    {
        if (!IsShiftActive || seconds <= 0f) return;
        TimeRemaining += seconds;
        if (!CustomersAllowed) SetCustomersAllowed(true);
        ShiftStateChanged?.Invoke();
    }

    public void EndShift()
    {
        if (!IsShiftActive) return;

        IsShiftActive = false;
        TimeRemaining = 0f;
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

        if (TimeRemaining > 0f && !CustomersAllowed) SetCustomersAllowed(true);
        if (TimeRemaining <= 0f && CustomersAllowed) SetCustomersAllowed(false);

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
