using Kehai;
using UnityEngine;
using UnityEngine.SceneManagement;

// The hold bar (0.4.1). Fixing things takes twenty seconds now (BackstreetFix), and a hold that
// long needs to show how far along it is. While the crosshair is on something that's held, a
// soft pill under it says what holding does; while E is down, a bar fills and the seconds
// left count down. Taps get no prompt, as before: the yellow outline says "use me".
public sealed class InteractHud : MonoBehaviour
{
    public static InteractHud Instance { get; private set; }

    GUIStyle label, count;
    int styledFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode || Instance != null) return;
        var go = new GameObject("~InteractHud");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<InteractHud>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // What the bar shows for a hold `progress` (0–1) along with `secondsLeft` to go.
    public static string Countdown(float secondsLeft) => secondsLeft >= 9.5f ? $"{Mathf.CeilToInt(secondsLeft)} s" : $"{secondsLeft:0.0} s";

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        PlayerInteract hands = PlayerInteract.Local;
        if (hands == null || GamePause.Paused || FullScreenPanel.AnyOpen) return;
        if (!hands.IsHolding && !hands.CanHoldTarget) return;
        IInteractable target = hands.CurrentTarget;
        if (target == null) return;
        string prompt = target.GetPrompt();
        if (string.IsNullOrEmpty(prompt)) return;

        int size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 46f, 14f, 30f));
        if (label == null || styledFor != size)
        {
            styledFor = size;
            label = UiTheme.Label(size, UiTheme.Text, TextAnchor.MiddleCenter, false);
            count = UiTheme.Label(Mathf.RoundToInt(size * 0.85f), UiTheme.Dim, TextAnchor.MiddleCenter, false);
        }

        float w = Mathf.Max(size * 14f, label.CalcSize(new GUIContent(prompt)).x + size * 2.4f);
        bool holding = hands.IsHolding;
        float h = size * (holding ? 3.7f : 2.1f);
        var card = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.58f, w, h);
        UiTheme.Fill(card, UiTheme.Ink, UiTheme.Corner(card.height));
        GUI.Label(new Rect(card.x, card.y + size * 0.25f, card.width, size * 1.6f), prompt, label);
        if (!holding) return;

        var bar = new Rect(card.x + size * 1.2f, card.y + size * 2.05f, card.width - size * 2.4f, size * 0.42f);
        UiTheme.Bar(bar, hands.HoldProgress01, UiTheme.Faint, UiTheme.Accent);
        GUI.Label(new Rect(card.x, bar.yMax + size * 0.1f, card.width, size * 1.1f), Countdown(hands.HoldSecondsLeft), count);
    }
}
