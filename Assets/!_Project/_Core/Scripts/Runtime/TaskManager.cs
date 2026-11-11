using System;
using System.Collections.Generic;
using UnityEngine;

// The shift checklist.
//
// Every task is a simple state. Mopping, restocking and the bin are all "is it clean
// right now?" — the task un-checks itself the moment a customer spills something, takes
// something off a shelf, or uses the bin. Customers spill on a chance roll and there is
// no ceiling on how many spills can be down at once.
//
// The shift can only be clocked out once all three read complete.
public class TaskManager : MonoBehaviour
{
    // Stored by name in the shift records, and by number nowhere: new kinds go at the end.
    public enum TaskKind { Mop, Trash, Stock, Serve, Directions, ReturnTool, RestartClock }

    public readonly struct ShiftTask
    {
        public readonly TaskKind Kind;
        public readonly string Label;
        public readonly string Detail;     // "3/5" for the quota task, empty for state tasks
        public readonly bool IsComplete;

        public ShiftTask(TaskKind kind, string label, string detail, bool isComplete)
        {
            Kind = kind; Label = label; Detail = detail; IsComplete = isComplete;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Detail) ? Label : $"{Label}  {Detail}";
        }
    }

    public bool ShiftRunning { get; private set; }

    public event Action Changed;

    // ---- live world state ----
    // Spills currently on the floor. The job is done when there are none, however many
    // there have been over the shift.
    public int SpillsOutstanding => Dirt.ActiveCount;
    public bool FloorClean => Dirt.ActiveCount == 0;

    public bool ShelvesStocked => ShelfUnit.NotFullCount == 0;

    public int TrashOutstanding
    {
        get
        {
            int total = 0;
            var cans = Trashcan.All;
            for (int i = 0; i < cans.Count; i++) total += cans[i].UsageCount;
            return total;
        }
    }

    // Bagging a bin isn't enough — the bag has to reach the container out back.
    public bool TrashEmpty => TrashOutstanding == 0 && TrashBag.ActiveCount == 0;

    // Nobody may be left standing at the till when the shift closes.
    public int CustomersWaiting => CustomerNPC.WaitingCount;
    public bool AllCustomersServed => CustomersWaiting == 0;

    // Shoppers who stopped to ask where something is and haven't been dealt with.
    public int CustomersAsking => CustomerRequest.PendingCount;
    public bool AllDirectionsGiven => CustomersAsking == 0;

    // Karen's chores (0.3.0). A tool she took and hid (Tool theft): it has to go back on its
    // rack, by hand — the rack won't recall it.
    public int ToolsMissing => ToolSnapPoint.TakenCount;
    public bool ToolsHome => ToolsMissing == 0;

    // The time clock she stopped (the overtime): restart it at the clock.
    public bool ClockRunning => Shift == null || !Shift.IsFrozen;

    public bool AllComplete => FloorClean && ShelvesStocked && TrashEmpty
                            && AllCustomersServed && AllDirectionsGiven && ToolsHome && ClockRunning;

    // Where a job is, when somebody knows: "Aisle 4" after she swept a bay there. Shown beside
    // the job while it's open, and dropped once the job's done.
    readonly Dictionary<TaskKind, string> hints = new Dictionary<TaskKind, string>();

    public void Hint(TaskKind kind, string where)
    {
        if (string.IsNullOrEmpty(where)) hints.Remove(kind);
        else hints[kind] = where;
        NotifyChanged();
    }

    public string HintFor(TaskKind kind) => hints.TryGetValue(kind, out string where) ? where : string.Empty;

    string Detail(TaskKind kind, bool complete, string otherwise)
    {
        if (complete) { hints.Remove(kind); return otherwise; }
        string hint = HintFor(kind);
        if (hint.Length == 0) return otherwise;
        return otherwise.Length == 0 ? hint : hint + " · " + otherwise;
    }

    [Tooltip("The shift's clock, for the overtime's job. Found in the scene when empty.")]
    public ShiftManager shiftManager;
    ShiftManager Shift => shiftManager != null ? shiftManager : shiftManager = FindAnyObjectByType<ShiftManager>();
    public bool HasTasks => ShiftRunning;

    public IEnumerable<ShiftTask> Tasks
    {
        get
        {
            // No running total — just whether the floor needs attention at all.
            yield return new ShiftTask(TaskKind.Mop, "Mop up spills",
                Detail(TaskKind.Mop, FloorClean, string.Empty), FloorClean);

            yield return new ShiftTask(TaskKind.Stock, "Restock the shelves",
                Detail(TaskKind.Stock, ShelvesStocked, string.Empty), ShelvesStocked);

            string trashDetail = TrashBag.ActiveCount > 0 ? "take the bag out back" : string.Empty;
            yield return new ShiftTask(TaskKind.Trash, "Empty the trash", Detail(TaskKind.Trash, TrashEmpty, trashDetail), TrashEmpty);

            string serveDetail = CustomersWaiting > 0 ? $"{CustomersWaiting} waiting" : string.Empty;
            yield return new ShiftTask(TaskKind.Serve, "Serve the customers", serveDetail, AllCustomersServed);

            // Unlike the others this one is not a standing job. It appears only while
            // somebody is actually waiting on an answer and disappears once they aren't,
            // rather than sitting in the list struck through.
            if (CustomersAsking > 0)
                yield return new ShiftTask(TaskKind.Directions, "Help customers find things",
                    $"{CustomersAsking} asking", false);

            // Karen's chores, also only while they're waiting.
            foreach (ToolSnapPoint rack in ToolSnapPoint.TakenRacks)
                yield return new ShiftTask(TaskKind.ReturnTool, "Return the " + rack.ToolName,
                    Detail(TaskKind.ReturnTool, false, string.Empty), false);

            if (!ClockRunning)
                yield return new ShiftTask(TaskKind.RestartClock, "Restart the time clock",
                    Detail(TaskKind.RestartClock, false, "staff room"), false);
        }
    }

    // shiftNumber is 1-based: the first shift uses the base quota.
    public void BeginShift(int shiftNumber)
    {
        ShiftRunning = true;
        hints.Clear();
        NotifyChanged();
    }

    public void EndShift()
    {
        ShiftRunning = false;
        NotifyChanged();
    }

    // Called by dirt, shelves and bins whenever they change state.
    public void NotifyChanged() => Changed?.Invoke();

    public static void NotifyWorldChanged()
    {
        if (Instance != null) Instance.NotifyChanged();
    }

    static TaskManager instance;
    public static TaskManager Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<TaskManager>();
            return instance;
        }
    }

    void Awake() => instance = this;
}
