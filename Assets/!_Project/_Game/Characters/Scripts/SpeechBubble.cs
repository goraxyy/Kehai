using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A world-space caption that floats over a character's head and turns to face the camera.
// Built in code so a customer prefab needs no UI authoring, and torn down with its owner.
public class SpeechBubble : MonoBehaviour
{
    const float PixelsPerMetre = 300f;
    const float PanelWidth = 460f;

    public Transform anchor;
    public float height = 2.1f;

    [Tooltip("Metres out along the anchor's facing direction, so the caption hangs in " +
             "front of the character instead of on top of it.")]
    public float forwardOffset = 0.75f;

    RectTransform panel;
    TextMeshProUGUI label;
    Camera view;

    readonly StringBuilder builder = new StringBuilder();

    public static SpeechBubble Create(Transform anchor, float height, float forwardOffset = 0.75f)
    {
        GameObject root = new GameObject("SpeechBubble");
        if (!Edition.ShowUI) return root.AddComponent<SpeechBubble>();

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        RectTransform canvasRect = (RectTransform)root.transform;
        canvasRect.sizeDelta = new Vector2(PanelWidth, 10f);
        canvasRect.localScale = Vector3.one / PixelsPerMetre;

        // Background panel: grows to fit whatever text is in it.
        GameObject panelObject = new GameObject("Panel", typeof(RectTransform));
        panelObject.transform.SetParent(root.transform, false);

        Image background = panelObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.72f);
        background.raycastTarget = false;

        var layout = panelObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 16, 16);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = panelObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform panelRect = (RectTransform)panelObject.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.sizeDelta = new Vector2(PanelWidth, 0f);

        GameObject textObject = new GameObject("Label", typeof(RectTransform));
        textObject.transform.SetParent(panelObject.transform, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 30f;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.color = Color.white;
        text.raycastTarget = false;

        SpeechBubble bubble = root.AddComponent<SpeechBubble>();
        bubble.anchor = anchor;
        bubble.height = height;
        bubble.forwardOffset = forwardOffset;
        bubble.panel = panelRect;
        bubble.label = text;
        bubble.view = Camera.main;
        bubble.Hide();

        return bubble;
    }

    public void Show(string body)
    {
        if (label == null) return;
        label.text = body;
        gameObject.SetActive(true);
    }

    // The question plus a pick-one list. The selected line gets the caret and the colour.
    public void ShowChoices(string question, string[] options, int selected)
    {
        if (label == null) return;

        builder.Clear();
        builder.AppendLine(question);
        builder.AppendLine();

        for (int i = 0; i < options.Length; i++)
        {
            if (i == selected)
                builder.AppendLine($"<color=#FFD400>> {options[i]}</color>");   // plain ">": the bubble font has no arrow glyph
            else
                builder.AppendLine($"<color=#9A9A9A>   {options[i]}</color>");
        }

        builder.Append("<size=60%><color=#7A7A7A>↑↓ choose   E select</color></size>");

        label.text = builder.ToString();
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (anchor == null) { Destroy(gameObject); return; }

        if (view == null)
        {
            view = Camera.main;
            if (view == null) return;
        }

        // Out in front of whoever is speaking. Customers turn to face the player while they
        // talk, so their forward vector is the direction the caption should sit in.
        Vector3 facing = anchor.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;

        transform.position = anchor.position
                           + Vector3.up * height
                           + facing.normalized * forwardOffset;

        // Face the camera, upright — billboarding by rotation, not by LookAt, so the
        // caption never rolls when the player tilts their view.
        Vector3 forward = transform.position - view.transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    void OnDestroy()
    {
        // Nothing to release: the canvas dies with this object.
    }
}
