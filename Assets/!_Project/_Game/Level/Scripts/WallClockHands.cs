using System.Collections.Generic;
using UnityEngine;

// The wall clocks' hands (Karen 0.3.0; build plan B2.4). They run with the shift: a night shift
// from 22:00 to 06:00 squeezed into the shift's length. When Karen stops the time clock (the
// overtime) the countdown stops, and so do the hands, until someone restarts it.
//
// The clocks are built by StaffRoomModels.Clock: a face with a "Minute hand" and an "Hour hand"
// as separate boxes. Each hand gets a pivot at the face's centre, so it turns about it.
public sealed class WallClockHands : MonoBehaviour
{
    public const float StartHour = 22f, NightHours = 8f;
    public static readonly Vector3 FaceCentre = new Vector3(0f, 0.23f, 0f);   // in the clock's own space

    sealed class Hands { public Transform Minute, Hour; }

    readonly List<Hands> clocks = new List<Hands>();
    ShiftManager shift;

    public static void Ensure()
    {
        if (FindAnyObjectByType<WallClockHands>() != null) return;
        var go = new GameObject("KAREN_WallClockHands");
        var hands = go.AddComponent<WallClockHands>();
        hands.Find();
        if (hands.clocks.Count == 0) Destroy(go);
    }

    static bool Is(Transform t, string what) =>
        t.name.Replace('_', ' ').ToLowerInvariant().StartsWith(what);

    void Find()
    {
        shift = FindAnyObjectByType<ShiftManager>();
        var byClock = new Dictionary<Transform, Hands>();
        foreach (Transform t in FindObjectsByType<Transform>())
        {
            bool minute = Is(t, "minute hand"), hour = Is(t, "hour hand");
            if (!minute && !hour || t.parent == null) continue;
            if (!byClock.TryGetValue(t.parent, out Hands h)) byClock[t.parent] = h = new Hands();
            Transform pivot = Pivot(t);
            if (minute) h.Minute = pivot; else h.Hour = pivot;
        }
        foreach (Hands h in byClock.Values) if (h.Minute != null || h.Hour != null) clocks.Add(h);
    }

    // A pivot at the face's centre, at the hand's depth, holding the hand where it was.
    static Transform Pivot(Transform hand)
    {
        Transform clock = hand.parent;
        var pivot = new GameObject(hand.name + " pivot").transform;
        pivot.SetParent(clock, false);
        pivot.localPosition = new Vector3(FaceCentre.x, FaceCentre.y, hand.localPosition.z);
        pivot.localRotation = Quaternion.identity;
        hand.SetParent(pivot, true);
        return pivot;
    }

    // The night's time, in hours past midnight (22.0 to 30.0), from the shift's countdown.
    public static float NightTime(float duration, float remaining, bool active)
    {
        if (!active || duration <= 0f) return StartHour;
        float done = Mathf.Clamp01(1f - remaining / duration);
        return StartHour + done * NightHours;
    }

    void Update()
    {
        if (shift == null) return;
        float hours = NightTime(shift.shiftDurationSeconds, shift.TimeRemaining, shift.IsShiftActive);
        // The built hands point at 12 (minute) and at 3 (hour): turn them from there.
        float minuteAngle = (hours % 1f) * 360f;
        float hourAngle = (hours % 12f) / 12f * 360f - 90f;
        foreach (Hands h in clocks)
        {
            // The face looks along the clock's -Z, so clockwise seen from the front is +Z about it.
            if (h.Minute != null) h.Minute.localRotation = Quaternion.Euler(0f, 0f, -minuteAngle);
            if (h.Hour != null) h.Hour.localRotation = Quaternion.Euler(0f, 0f, -hourAngle);
        }
    }
}
