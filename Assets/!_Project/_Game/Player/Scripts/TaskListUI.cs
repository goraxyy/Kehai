using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shift checklist in the corner of the screen. Redraws only when something actually
// changes rather than every frame; F hides and shows it.
public class TaskListUI : MonoBehaviour
{
    [Header("References")]
    public TaskManager taskManager;
    public ShiftManager shiftManager;
    public CanvasGroup panel;
    public TextMeshProUGUI text;

    [Header("Input")]
    public KeyCode toggleKey = KeyCode.C;

    [Header("Appearance")]
    public string headerWhenIdle = "OFF SHIFT";

    bool visible = true;
    public bool Visible => visible;
    readonly StringBuilder builder = new StringBuilder();
    int lastWholeSecond = -1;

    void Awake()
    {
        if (taskManager == null) taskManager = FindAnyObjectByType<TaskManager>();
        if (shiftManager == null) shiftManager = FindAnyObjectByType<ShiftManager>();
    }

    void OnEnable()
    {
        if (taskManager != null) taskManager.Changed += Redraw;
        if (shiftManager != null) shiftManager.ShiftStateChanged += Redraw;
        Redraw();
    }

    void OnDisable()
    {
        if (taskManager != null) taskManager.Changed -= Redraw;
        if (shiftManager != null) shiftManager.ShiftStateChanged -= Redraw;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey) && !GamePause.Paused)
        {
            visible = !visible;
            ApplyVisibility();
        }

        // The tell for a falsified line: the list blinks out for exactly one frame.
        if (panel != null)
        {
            bool hide = Kehai.Aiko.HudFeed.HideForTell;
            float alpha = visible && !hide ? 1f : 0f;
            if (!Mathf.Approximately(panel.alpha, alpha)) panel.alpha = alpha;
        }

        // The clock is the only thing that needs a periodic redraw, and only once a second.
        if (shiftManager != null && shiftManager.IsShiftActive)
        {
            int whole = Mathf.CeilToInt(shiftManager.TimeRemaining);
            if (whole != lastWholeSecond)
            {
                lastWholeSecond = whole;
                Redraw();
            }
        }
    }

    void ApplyVisibility()
    {
        if (panel == null) return;
        panel.alpha = visible ? 1f : 0f;
        panel.blocksRaycasts = visible;
    }

    void Redraw()
    {
        if (text == null) return;

        builder.Clear();

        if (shiftManager != null && shiftManager.IsShiftActive)
        {
            int remaining = Mathf.Max(0, Mathf.CeilToInt(shiftManager.TimeRemaining));
            builder.AppendLine($"<b>SHIFT {shiftManager.ShiftNumber}</b>");
            builder.AppendLine($"Time left  <b>{remaining / 60:0}:{remaining % 60:00}</b>");
            if (!shiftManager.CustomersAllowed)
                builder.AppendLine("<i>Store closed - clock out</i>");
        }
        else
        {
            builder.AppendLine($"<b>{headerWhenIdle}</b>");
            float full = shiftManager != null ? shiftManager.shiftDurationSeconds : 0f;
            int whole = Mathf.CeilToInt(full);
            builder.AppendLine($"Shift length  {whole / 60:0}:{whole % 60:00}");
            builder.AppendLine("<i>Clock in at the puncher</i>");
        }

        builder.AppendLine();

        if (taskManager == null || !taskManager.HasTasks)
        {
            builder.AppendLine("No tasks");
        }
        else
        {
            // Aiko can stand between the task list and your HUD (aiko.md §8.2).
            foreach (TaskManager.ShiftTask task in Kehai.Aiko.HudFeed.Shown(taskManager.Tasks))
            {
                // TMP renders <s> as a strikethrough, which is how a finished task reads.
                // Restocking and trash are live states, so they come back if a customer
                // empties a shelf or uses the bin again.
                if (task.IsComplete)
                    builder.AppendLine($"<s>{task}</s>");
                else
                    builder.AppendLine(task.ToString());
            }

            if (shiftManager != null && !shiftManager.CanClockOut)
                builder.AppendLine("\n<i>Finish all tasks to clock out</i>");
        }

        builder.Append($"\n<size=70%>[{toggleKey}] hide</size>");
        text.text = builder.ToString();
    }
}
