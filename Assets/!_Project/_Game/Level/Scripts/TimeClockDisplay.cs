using UnityEngine;

// The time clock shows the shift's time itself (the owner's call of 25 November): the night from
// 22:00 to 06:00 squeezed into the shift's length, as the wall clocks reckon it
// (WallClockHands.NightTime). When Karen stops it (The overtime) the time stops and blinks red
// until you hold E at the time clock (Puncher) to start it again. The wall clock that hung above
// it goes: the time clock is the clock now.
public sealed class TimeClockDisplay : MonoBehaviour
{
    public const string WallClockAbove = "Staff_Clock_01";
    public static readonly Color Running = new Color(0.55f, 1f, 0.6f), Stopped = new Color(1f, 0.25f, 0.2f);
    public const float BlinkSeconds = 0.5f;

    TMPro.TextMeshPro text;
    ShiftManager shift;

    public string Shown => text != null ? text.text : "";
    public bool ShownRed => text != null && text.color == Stopped;

    // On every time clock in the scene, and the wall clock above the store's one taken down.
    public static void Ensure()
    {
        foreach (Puncher clock in FindObjectsByType<Puncher>())
            if (clock != null && clock.GetComponent<TimeClockDisplay>() == null) clock.gameObject.AddComponent<TimeClockDisplay>();
        GameObject above = SceneObjectNames.Find(WallClockAbove);
        if (above != null) above.SetActive(false);
    }

    void Awake()
    {
        shift = GetComponent<Puncher>()?.shiftManager ?? FindAnyObjectByType<ShiftManager>();
        Front(out Vector3 face, out Vector3 normal, out float height);
        var go = new GameObject("Time clock display");
        go.transform.SetParent(transform, true);
        go.transform.position = face + normal * 0.012f + Vector3.up * height * 0.22f;
        go.transform.rotation = Quaternion.LookRotation(-normal);   // reads from the front
        text = go.AddComponent<TMPro.TextMeshPro>();
        if (Kehai.UiTheme.TmpFont != null) text.font = Kehai.UiTheme.TmpFont;
        text.fontSize = 0.9f;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(0.3f, 0.1f);
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        text.color = Running;
        Refresh();
    }

    void Update() => Refresh();

    // While Karen stands at it to stop it (The overtime), its display flickers.
    bool flicker;
    public static void Flicker(Vector3 near, bool on)
    {
        foreach (TimeClockDisplay d in FindObjectsByType<TimeClockDisplay>())
            if (d != null && (d.transform.position - near).sqrMagnitude < 4f) d.flicker = on;
    }

    void Refresh()
    {
        if (text == null) return;
        bool active = shift != null && shift.IsShiftActive;
        float hours = shift != null ? WallClockHands.NightTime(shift.shiftDurationSeconds, shift.TimeRemaining, active) : WallClockHands.StartHour;
        text.text = Format(hours);
        bool stopped = shift != null && shift.IsFrozen;
        // Stopped: red, and blinking so it reads from across the room.
        text.color = stopped ? Stopped : Running;
        text.enabled = stopped ? Mathf.Repeat(Time.unscaledTime, BlinkSeconds * 2f) < BlinkSeconds
                     : !flicker || Random.value > 0.35f;
    }

    // "03:47" for 27.78 hours past the night's midnight-before.
    public static string Format(float hours)
    {
        int minutes = Mathf.FloorToInt(hours * 60f) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    // The face of the time clock that looks into the room: across its thinnest side, the side
    // with no wall right behind it.
    void Front(out Vector3 face, out Vector3 normal, out float height)
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0) { b = renderers[0].bounds; foreach (Renderer r in renderers) b.Encapsulate(r.bounds); }
        height = b.size.y;
        Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        float half = Mathf.Min(b.extents.x, b.extents.z);
        normal = axis;
        foreach (Vector3 side in new[] { axis, -axis })
        {
            Vector3 from = b.center + side * (half + 0.02f);
            if (!Physics.Raycast(from, side, 0.3f, ~0, QueryTriggerInteraction.Ignore)) { normal = side; break; }
        }
        face = b.center + normal * half;
    }
}
