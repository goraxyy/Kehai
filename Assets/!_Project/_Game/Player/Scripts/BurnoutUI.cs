using UnityEngine;
using UnityEngine.UI;

// The energy bar that sits just above the inventory: a black backing strip with a
// coloured fill that shrinks and shifts green -> yellow -> red as the shift wears on.
public class BurnoutUI : MonoBehaviour
{
    [Header("References")]
    public BurnoutSystem burnout;
    public RectTransform fill;
    public Image fillImage;

    [Header("Colours")]
    public Color fullColour = new Color(0.30f, 0.85f, 0.30f);
    public Color midColour = new Color(0.95f, 0.80f, 0.20f);
    public Color emptyColour = new Color(0.85f, 0.22f, 0.20f);

    [Tooltip("How fast the bar slides to a new value, e.g. after a coffee.")]
    public float smoothing = 8f;

    float shown = 1f;

    void Awake()
    {
        if (burnout == null) burnout = FindAnyObjectByType<BurnoutSystem>();
        if (burnout != null) shown = burnout.Energy01;
    }

    void Update() => Step(Time.deltaTime);

    // One frame of the bar over `dt` seconds. Public so tests can step it with a known time.
    public void Step(float dt)
    {
        if (burnout == null || fill == null) return;

        float target = burnout.Energy01;
        shown = smoothing > 0f
            ? Mathf.MoveTowards(shown, target, smoothing * dt)
            : target;

        // Stretch the fill across the backing strip by anchor, so no sprite is needed.
        Vector2 max = fill.anchorMax;
        max.x = Mathf.Clamp01(shown);
        fill.anchorMax = max;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;

        if (fillImage != null)
            fillImage.color = ColourFor(shown);
    }

    Color ColourFor(float value)
    {
        // Green in the top half, yellow at the midpoint, red as it bottoms out.
        return value > 0.5f
            ? Color.Lerp(midColour, fullColour, (value - 0.5f) * 2f)
            : Color.Lerp(emptyColour, midColour, value * 2f);
    }
}
