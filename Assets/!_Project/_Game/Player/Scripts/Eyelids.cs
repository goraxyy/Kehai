using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Two black lids that slide in from the top and bottom of the screen until they meet in
// the middle, closing the player's eyes. Fully shut is total black — the lids overlap
// slightly so no hairline of the world survives between them.
//
// Built entirely in code on its own canvas, above every other, so the scene needs no UI
// authoring and nothing in the HUD shows through.
//
// Closed01 is the whole interface: 0 is wide open, 1 is shut. Blink, Close and Open are
// conveniences over it. Anything else can drive it directly instead — a cutscene, passing
// out from burnout — without touching this script, because nothing here decides *when* the
// eyes close, only what that looks like.
//
// The player's own eyes come in through SetShut (0.4.1): two states, open and shut, with the
// trip between them at a real eyelid's speed (BlinkLids).
public class Eyelids : MonoBehaviour
{
    [Header("State")]
    [Range(0f, 1f)]
    [Tooltip("0 = eyes open, 1 = fully shut. Safe to drive every frame from anywhere.")]
    public float closed;

    [Header("Shape")]
    [Range(0.5f, 1f)]
    [Tooltip("Share of the travel taken by the top lid. Real eyelids close mostly from above.")]
    public float topShare = 0.68f;

    [Tooltip("Lids accelerate shut and settle open rather than moving linearly.")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Range(0f, 0.05f)]
    [Tooltip("How far the lids overrun each other where they meet, as a share of screen " +
             "height. Stops a seam of the world showing through when fully closed.")]
    public float seamOverlap = 0.004f;

    [Header("Timing")]
    [Tooltip("A blink: shut and open again.")]
    public float blinkDuration = 0.18f;
    public float closeDuration = 0.9f;
    public float openDuration = 0.6f;

    [Header("Appearance")]
    public Color lidColour = Color.black;

    [Tooltip("Above the HUD canvases, which sit at 0.")]
    public int sortingOrder = 999;

    [Header("Debug")]
    [Tooltip("Press to blink, for trying the effect out. None by default so it can't " +
             "collide with a real binding.")]
    public KeyCode testBlinkKey = KeyCode.None;

    // 0 open, 1 shut.
    public float Closed01 => closed;
    public bool IsFullyClosed => closed >= 0.999f;
    public bool IsFullyOpen => closed <= 0.001f;

    // The player's eyes, as two states: see BlinkLids. Only moves the lids while it's in use.
    readonly BlinkLids blink = new BlinkLids();
    bool blinkDriven;
    public BlinkLids BlinkState => blink;

    Canvas canvas;
    RectTransform topLid;
    RectTransform bottomLid;
    Coroutine running;
    bool built;

    void Awake() => EnsureBuilt();

    void LateUpdate()
    {
        if (testBlinkKey != KeyCode.None && Input.GetKeyDown(testBlinkKey)) Blink();
        if (blinkDriven) StepBlink(Time.unscaledDeltaTime);

        // Applied every frame rather than only on change, so an external driver can just
        // write to `closed` and never call anything.
        Apply(closed);
    }

    // --- driving it ----------------------------------------------------------

    // Snap straight to a position. This is the one an eye tracker would call.
    public void SetClosed(float amount)
    {
        StopRunning();
        closed = Mathf.Clamp01(amount);
        Apply(closed);
    }

    // The player's eyes: shut or open, nothing between (0.4.1). Call it every frame or only on
    // a change; it does nothing while the state asked for is the one it already has, so it
    // doesn't fight a cutscene that's closing the eyes for its own reasons.
    public void SetShut(bool shut)
    {
        if (!blinkDriven)
        {
            if (!shut) return;                 // open and asked to be open: nothing to do
            StopRunning();
            blink.Snap(false);
            blinkDriven = true;
        }
        blink.Set(shut);
    }

    // One step of the eyes' trip. LateUpdate drives it; public so a test can.
    public void StepBlink(float dt)
    {
        if (!blinkDriven) return;
        blink.Step(dt);
        closed = blink.Closed01;
        if (!blink.WantShut && blink.IsOpen) blinkDriven = false;   // back open: hand the lids back
    }

    public void Blink() => Blink(blinkDuration);

    public void Blink(float duration)
    {
        StopRunning();
        running = StartCoroutine(BlinkRoutine(Mathf.Max(0.01f, duration)));
    }

    public void Close() => Close(closeDuration);

    public void Close(float duration)
    {
        StopRunning();
        running = StartCoroutine(MoveTo(1f, duration));
    }

    public void Open() => Open(openDuration);

    public void Open(float duration)
    {
        StopRunning();
        running = StartCoroutine(MoveTo(0f, duration));
    }

    IEnumerator BlinkRoutine(float duration)
    {
        // Shutting is the fast half of a blink; the opening lags a little behind it.
        yield return MoveTo(1f, duration * 0.4f);
        yield return MoveTo(0f, duration * 0.6f);
        running = null;
    }

    IEnumerator MoveTo(float target, float duration)
    {
        float from = closed;

        if (duration <= 0f)
        {
            closed = target;
            Apply(closed);
            yield break;
        }

        // Unscaled, so the eyes still close on a paused or slowed game.
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            closed = Mathf.Lerp(from, target, t / duration);
            Apply(closed);
            yield return null;
        }

        closed = target;
        Apply(closed);
    }

    void StopRunning()
    {
        blinkDriven = false;
        if (running == null) return;
        StopCoroutine(running);
        running = null;
    }

    // --- drawing -------------------------------------------------------------

    void Apply(float amount)
    {
        EnsureBuilt();
        if (topLid == null || bottomLid == null) return;

        float eased = ease != null ? ease.Evaluate(Mathf.Clamp01(amount)) : Mathf.Clamp01(amount);

        // The two shares add up to 1 at full close, so the lids meet exactly; the overlap
        // is added on top of that so they cross rather than abut.
        float overlap = eased * seamOverlap;
        float top = Mathf.Clamp01(eased * topShare + overlap);
        float bottom = Mathf.Clamp01(eased * (1f - topShare) + overlap);

        // Anchors rather than sizes, so this is resolution and aspect independent.
        topLid.anchorMin = new Vector2(0f, 1f - top);
        topLid.anchorMax = Vector2.one;
        topLid.offsetMin = Vector2.zero;
        topLid.offsetMax = Vector2.zero;

        bottomLid.anchorMin = Vector2.zero;
        bottomLid.anchorMax = new Vector2(1f, bottom);
        bottomLid.offsetMin = Vector2.zero;
        bottomLid.offsetMax = Vector2.zero;

        // Nothing to draw with the eyes open; skip the canvas entirely.
        if (canvas != null) canvas.enabled = eased > 0.0001f;
    }

    void EnsureBuilt()
    {
        if (built && topLid != null && bottomLid != null) return;

        var existing = transform.Find("~Eyelids");
        if (existing != null) DestroyImmediate(existing.gameObject);

        var root = new GameObject("~Eyelids");
        root.transform.SetParent(transform, false);
        root.layer = gameObject.layer;

        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        root.AddComponent<CanvasScaler>();
        // No GraphicRaycaster: the lids are scenery, and should never eat a click.

        topLid = MakeLid(root.transform, "TopLid");
        bottomLid = MakeLid(root.transform, "BottomLid");

        built = true;
    }

    RectTransform MakeLid(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<Image>();
        image.color = lidColour;
        image.raycastTarget = false;

        return (RectTransform)go.transform;
    }

#if UNITY_EDITOR
    // Dragging `closed` in the Inspector previews the effect live. Deferred: Unity won't let
    // UI objects be built or resized from inside OnValidate itself.
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && Application.isPlaying) Apply(closed);
        };
    }
#endif
}
