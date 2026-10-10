using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kehai;
using Kehai.Karen;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The title screen. It opens over the store, with time stopped and the HUD put away, when the
// game starts, when you leave a shift for it from the Esc menu, and after a career ends.
//   Continue        the career, at its next shift (Karen saves it after every shift)
//   New career      she forgets what she has learned about you; your settings stay
//   Settings, Controls   the Esc menu's own pages
// It keeps out of the way of the eval harness, the 3D replay and batch runs. `-skip-menu` on
// the command line skips it, and so does Kehai → Main Menu → Skip It in the editor.
public sealed class MainMenu : MonoBehaviour
{
    public enum Choice { Continue, FirstShift, NewCareer, Settings, Controls, Quit }

    public static MainMenu Instance { get; private set; }
    public static bool Visible => Instance != null && Instance.visible;

    // What the menu offers for a career with `shiftsWorked` shifts on file.
    public static List<Choice> ChoicesFor(int shiftsWorked, bool careerOver)
    {
        var choices = new List<Choice>();
        if (careerOver) choices.Add(Choice.NewCareer);
        else if (shiftsWorked > 0) { choices.Add(Choice.Continue); choices.Add(Choice.NewCareer); }
        else choices.Add(Choice.FirstShift);
        choices.Add(Choice.Settings);
        choices.Add(Choice.Controls);
        choices.Add(Choice.Quit);
        return choices;
    }

    static readonly Color Crimson = new Color32(0xDC, 0x14, 0x3C, 0xFF);
    static readonly Color SoftBlack = new Color32(0x15, 0x15, 0x18, 0xFF);
    static readonly Color Paper = new Color(0.96f, 0.96f, 0.95f);
    static readonly Color Grey = new Color(0.66f, 0.66f, 0.71f);
    static readonly Color DimGrey = new Color(0.5f, 0.5f, 0.55f);

    const float ColumnX = 160f;
    const float ListTop = -560f, RowHeight = 66f;

    static bool showOnNextLoad;
    bool visible, careerRead, confirming, busy;
    int framesWaited;
    (int shifts, int warnings, bool over, string name) career;

    Canvas canvas;
    CanvasGroup content;
    Image curtain;
    RectTransform column, list;
    TextMeshProUGUI status;
    EventSystem ownEvents;
    readonly List<MenuButton> buttons = new List<MenuButton>();
    readonly List<Canvas> steppedAside = new List<Canvas>();
    readonly Dictionary<Component, Coroutine> fades = new Dictionary<Component, Coroutine>();

    // ---- when it opens ----------------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode || Instance != null) return;
        var go = new GameObject("Main menu");
        DontDestroyOnLoad(go);
        go.AddComponent<MainMenu>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => showOnNextLoad = false;

    void Awake()
    {
        Instance = this;
        Build();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // The store the game starts in. By now Karen, or a replay, has been put into it.
    void Start()
    {
        if (Wanted(firstLoad: true)) Show();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        Consequences.CareerOver = false;
        bool wanted = showOnNextLoad;
        showOnNextLoad = false;
        if (wanted && Wanted(firstLoad: false)) Show();
        Fade(curtain, 0f, 0.6f, () => { if (!visible) canvas.enabled = false; });
    }

    static bool Wanted(bool firstLoad)
    {
        if (Application.isBatchMode || FindAnyObjectByType<ShiftManager>() == null) return false;
        if (OtherModeRunning || Kehai.Replay.ReplayMode.Arg("-replay") != null || Kehai.Replay.ReplayMode.Rendering) return false;
        foreach (string flag in new[] { "-skip-menu", "-kehai-env", "-kehai-ablation" })
            if (Kehai.Replay.ReplayMode.Flag(flag)) return false;
#if UNITY_EDITOR
        if (firstLoad && UnityEditor.EditorPrefs.GetBool(SkipInEditorKey, false)) return false;
#endif
        return true;
    }

    // The eval harness or the 3D replay has the store.
    static bool OtherModeRunning => Kehai.Eval.KehaiEnv.Instance != null || Kehai.Replay.ReplayPlayer.Instance != null;

    static bool SettingsOpen => SettingsMenu.Instance != null && SettingsMenu.Instance.Visible;

    // From the Esc menu, and after a career ends: the store loads again with the menu over it.
    public static void ReturnToMenu()
    {
        if (Instance == null) { ShiftRestart.Reload(-1); return; }
        Instance.Reload(-1, menuAfter: true);
    }

    void Show()
    {
        visible = true;
        careerRead = confirming = busy = false;
        framesWaited = 0;
        GamePause.Set(true);
        FullScreenPanel.Set(this, true);   // before our canvas is on, so it isn't put away with the HUD
        EnsureEventSystem();
        ClearList();
        status.text = string.Empty;
        content.alpha = 0f;
        content.interactable = true;
        canvas.enabled = true;
        HideOtherCanvases();
        Fade(content, 1f, 0.7f);
    }

    void Hide()
    {
        visible = false;
        busy = false;
        canvas.enabled = curtain.color.a > 0.001f;
        FullScreenPanel.Set(this, false);
        foreach (Canvas c in steppedAside) if (c != null) c.enabled = true;
        steppedAside.Clear();
        GamePause.Set(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // ---- every frame -------------------------------------------------------------------------------

    void Update()
    {
        if (!visible)
        {
            if (Consequences.CareerOver && !SettingsOpen && !busy && EnterPressed) ReturnToMenu();
            return;
        }
        if (OtherModeRunning) { content.alpha = 0f; Hide(); return; }
        if (!careerRead) ReadCareer();
        if (Time.frameCount % 15 == 0) HideOtherCanvases();

        // The settings are drawn underneath any canvas, so the menu steps aside for them, and
        // its buttons stop answering the keyboard until they close.
        bool settings = SettingsOpen;
        if (canvas.enabled == settings)
        {
            canvas.enabled = !settings;
            content.interactable = !settings && !busy;
            if (settings && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (!settings) Select(null);
        }
        if (settings || busy) return;

        // A playtest screen is over the menu: it answers nothing until that's done.
        bool covered = Kehai.Playtest.PlaytestScreens.Covering;
        if (content.interactable == covered) content.interactable = !covered;
        if (covered) return;

        if (confirming && Input.GetKeyDown(KeyCode.Escape)) ShowChoices();
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null && Input.anyKeyDown) Select(null);
    }

    // The HUD (and anything made after the menu opened) stays out of the way of the menu.
    void HideOtherCanvases()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>())
            if (c != canvas && c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.sortingOrder < canvas.sortingOrder)
            {
                c.enabled = false;
                steppedAside.Add(c);
            }
    }

    static bool EnterPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

    // Read the career again (the playtest has just given this tester a fresh one).
    public void RefreshCareer()
    {
        if (!visible || confirming || busy) return;
        careerRead = false;
        framesWaited = 0;
    }

    // Karen loads her ledger in her Start, which may come after ours.
    void ReadCareer()
    {
        KarenBrain brain = KarenBrain.Instance;
        if (brain != null && brain.Ledger == null) return;
        if (brain == null && ++framesWaited < 10) return;

        LedgerData data = brain != null && brain.Ledger.Persistent ? brain.Ledger.Data : null;
        career = data == null ? (0, 0, false, null) : (data.shiftsWorked, data.warnings, data.endingReached, data.playerName);
        careerRead = true;
        ShowChoices();
    }

    // ---- what the choices do -----------------------------------------------------------------------

    void Pick(Choice choice)
    {
        if (busy) return;
        switch (choice)
        {
            case Choice.Continue: StartCoroutine(IntoTheStore(career.shifts)); break;
            case Choice.FirstShift: StartCoroutine(IntoTheStore(0)); break;
            case Choice.NewCareer: ShowConfirm(); break;
            case Choice.Settings: if (SettingsMenu.Instance != null) SettingsMenu.Instance.OpenFromMainMenu(keys: false); break;
            case Choice.Controls: if (SettingsMenu.Instance != null) SettingsMenu.Instance.OpenFromMainMenu(keys: true); break;
            case Choice.Quit: Quit(); break;
        }
    }

    // The menu fades and you're at the store's door; the shift starts when you clock in.
    IEnumerator IntoTheStore(int completedShifts)
    {
        busy = true;
        content.interactable = false;
        ShiftManager shift = FindAnyObjectByType<ShiftManager>();
        if (shift != null) shift.SetShiftNumber(completedShifts);
        yield return Fade(content, 0f, 0.4f);
        Hide();
    }

    // Karen forgets you. The store loads again, so the Karen in it starts from nothing.
    void StartOver()
    {
        KarenBrain brain = KarenBrain.Instance;
        if (brain != null && brain.Ledger != null) brain.Ledger.Wipe();
        else if (File.Exists(KarenLedger.DefaultPath)) File.Delete(KarenLedger.DefaultPath);
        Reload(0, menuAfter: false);
    }

    void Reload(int completedShifts, bool menuAfter)
    {
        busy = true;
        content.interactable = false;
        canvas.enabled = true;
        GamePause.Set(true);
        Fade(curtain, 1f, 0.35f, () =>
        {
            visible = false;
            FullScreenPanel.Set(this, false);
            steppedAside.Clear();   // the store they belong to is about to go
            showOnNextLoad = menuAfter;
            ShiftRestart.Reload(completedShifts);
        });
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- the list ----------------------------------------------------------------------------------

    void ShowChoices()
    {
        confirming = false;
        ClearList();
        status.text = career.over ? $"{career.name}'s file is closed."
            : career.shifts > 0 ? $"{career.name}  ·  {Shifts(career.shifts)} on file" + (career.warnings > 0 ? $"  ·  {career.warnings} written warning{(career.warnings == 1 ? "" : "s")}" : "")
            : "New employee. Orientation: clock in at the puncher.";

        List<Choice> choices = ChoicesFor(career.shifts, career.over);
        for (int i = 0; i < choices.Count; i++)
        {
            Choice choice = choices[i];
            AddButton(LabelFor(choice), ListTop - i * RowHeight, () => Pick(choice));
        }
        Link();
        Select(null);
    }

    string LabelFor(Choice choice)
    {
        switch (choice)
        {
            case Choice.Continue: return $"Continue<color=#8A8A93><size=60%>     shift {career.shifts + 1}</size></color>";
            case Choice.FirstShift: return "Start your first shift";
            case Choice.NewCareer: return career.over ? "Start a new career" : "New career";
            case Choice.Settings: return "Settings";
            case Choice.Controls: return "Controls";
            default: return Application.isEditor ? "Stop playing" : "Quit";
        }
    }

    void ShowConfirm()
    {
        confirming = true;
        ClearList();
        var question = MakeText(list, "Question", GameFonts.Body, 38f, Paper, new Vector2(0f, ListTop - 4f), new Vector2(760f, 60f));
        question.text = career.over ? "Start a new career?" : "Start a new career? This one ends here.";
        var detail = MakeText(list, "Detail", GameFonts.Body, 24f, Grey, new Vector2(0f, ListTop - 64f), new Vector2(700f, 100f));
        detail.text = GameNames.Antagonist + " forgets everything she has learned about you: where you hide, the routes you take, and which of her tricks work on you. Your settings stay.";
        AddButton("Yes, start over", ListTop - 190f, StartOver);
        AddButton("No, go back", ListTop - 190f - RowHeight, ShowChoices);
        Link();
        Select(buttons[1]);
    }

    static string Shifts(int n) => n == 1 ? "1 shift" : n + " shifts";

    void AddButton(string label, float y, Action onClick) =>
        buttons.Add(MenuButton.Make(list, label, new Vector2(0f, y), new Vector2(760f, RowHeight - 8f), onClick));

    void ClearList()
    {
        buttons.Clear();
        for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
    }

    // Up and down move through the list and wrap around.
    void Link()
    {
        for (int i = 0; i < buttons.Count; i++)
            buttons[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = buttons[(i - 1 + buttons.Count) % buttons.Count],
                selectOnDown = buttons[(i + 1) % buttons.Count],
            };
    }

    void Select(MenuButton button)
    {
        if (EventSystem.current == null || buttons.Count == 0) return;
        GameObject current = EventSystem.current.currentSelectedGameObject;
        if (button == null && current != null && buttons.Exists(b => b != null && b.gameObject == current)) return;
        EventSystem.current.SetSelectedGameObject((button != null ? button : buttons[0]).gameObject);
    }

    void EnsureEventSystem()
    {
        bool another = false;
        foreach (EventSystem es in FindObjectsByType<EventSystem>())
            if (es != ownEvents) another = true;
        if (another) { if (ownEvents != null) ownEvents.gameObject.SetActive(false); return; }
        if (ownEvents == null)
        {
            var go = new GameObject("Event system", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(transform, false);
            ownEvents = go.GetComponent<EventSystem>();
        }
        ownEvents.gameObject.SetActive(true);
    }

    // ---- building it -------------------------------------------------------------------------------

    void Build()
    {
        if (canvas != null) return;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // over the HUD, Karen's screen (900) and the eyelids (999)
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;   // sized by the height, so wide screens just get more store
        gameObject.AddComponent<GraphicRaycaster>();
        canvas.enabled = false;

        RectTransform root = Stretch(transform, "Menu");
        content = root.gameObject.AddComponent<CanvasGroup>();

        // The store shows through on the right; the left, where the words are, is dark.
        Image shade = Stretch(root, "Shade").gameObject.AddComponent<Image>();
        shade.sprite = Gradient();
        shade.color = SoftBlack;
        shade.raycastTarget = true;   // clicks on the backdrop go nowhere

        column = Box(root, "Column", new Vector2(ColumnX, 0f), new Vector2(820f, 1080f), new Vector2(0f, 0.5f));
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 1f);
        column.anchoredPosition = new Vector2(ColumnX, 0f);
        column.sizeDelta = new Vector2(820f, 0f);

        TMP_FontAsset japanese = GameFonts.Japanese;
        if (japanese != null)
        {
            TextMeshProUGUI kanji = MakeText(column, "気配", japanese, 64f, Crimson, new Vector2(4f, -150f), new Vector2(400f, 90f));
            kanji.characterSpacing = 12f;
            kanji.text = GameNames.GameJapanese;
        }
        TextMeshProUGUI title = MakeText(column, "Title", GameFonts.Heading, 156f, Paper, new Vector2(-4f, -232f), new Vector2(820f, 190f));
        title.characterSpacing = 8f;
        title.text = GameNames.Game.ToUpperInvariant();
        MakeText(column, "Tagline", GameFonts.Body, 30f, Grey, new Vector2(2f, -418f), new Vector2(820f, 46f)).text = "The sense that someone is there.";
        Image rule = Box(column, "Rule", new Vector2(4f, -482f), new Vector2(72f, 4f)).gameObject.AddComponent<Image>();
        rule.color = Crimson;
        rule.raycastTarget = false;
        status = MakeText(column, "Status", GameFonts.Body, 24f, Grey, new Vector2(2f, -508f), new Vector2(820f, 36f));

        list = Box(column, "List", Vector2.zero, new Vector2(820f, 0f));

        MakeText(root, "Version", GameFonts.Body, 20f, DimGrey, new Vector2(ColumnX, 56f), new Vector2(1200f, 30f), new Vector2(0f, 0f))
            .text = $"v{Application.version}      Arrows or mouse to choose  ·  Enter to select";
        TextMeshProUGUI phones = MakeText(root, "Headphones", GameFonts.Body, 20f, DimGrey, new Vector2(-80f, 56f), new Vector2(700f, 30f), new Vector2(1f, 0f));
        phones.alignment = TextAlignmentOptions.BottomRight;
        phones.text = "Best played with headphones";

        curtain = Stretch(transform, "Curtain").gameObject.AddComponent<Image>();
        curtain.color = new Color(0f, 0f, 0f, 0f);
        curtain.raycastTarget = false;
    }

    // Dark on the left, fading to a light veil over the right of the screen.
    static Sprite Gradient()
    {
        var tex = new Texture2D(256, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave, name = "Menu shade",
        };
        for (int x = 0; x < tex.width; x++)
        {
            float u = x / (tex.width - 1f);
            float a = Mathf.Lerp(0.93f, 0.22f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.78f, u)));
            tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }

    static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rt = Box(parent, name, Vector2.zero, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    // A box placed from `anchor` (top left unless said), which is also its pivot.
    static RectTransform Box(Transform parent, string name, Vector2 position, Vector2 size, Vector2? anchor = null)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        Vector2 a = anchor ?? new Vector2(0f, 1f);
        rt.anchorMin = rt.anchorMax = rt.pivot = a;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        return rt;
    }

    public static TextMeshProUGUI MakeText(Transform parent, string name, TMP_FontAsset font, float size, Color colour, Vector2 position, Vector2 box, Vector2? anchor = null)
    {
        RectTransform rt = Box(parent, name, position, box, anchor);
        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = size;
        text.color = colour;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.richText = true;
        return text;
    }

    // One fade at a time on each thing: a new one takes over from the last.
    Coroutine Fade(Component target, float to, float seconds, Action then = null)
    {
        if (fades.TryGetValue(target, out Coroutine running) && running != null) StopCoroutine(running);
        return fades[target] = StartCoroutine(Raise(target, to, seconds, then));
    }

    // Fades a canvas group or an image to `to` over `seconds` of real time (the game is paused).
    static IEnumerator Raise(Component target, float to, float seconds, Action then = null)
    {
        float Get() => target is CanvasGroup g ? g.alpha : ((Image)target).color.a;
        void Set(float a)
        {
            if (target is CanvasGroup g) g.alpha = a;
            else { var image = (Image)target; Color c = image.color; c.a = a; image.color = c; }
        }
        float from = Get();
        for (float t = 0f; t < seconds && target != null; t += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f))
        {
            Set(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        if (target != null) Set(to);
        then?.Invoke();
    }

#if UNITY_EDITOR
    const string SkipInEditorKey = "Kehai.MainMenu.SkipInEditor";
    const string SkipMenuItem = "Kehai/Main Menu/Skip It When Playing in the Editor";

    [UnityEditor.MenuItem(SkipMenuItem)]
    static void ToggleSkipInEditor() => UnityEditor.EditorPrefs.SetBool(SkipInEditorKey, !UnityEditor.EditorPrefs.GetBool(SkipInEditorKey, false));

    [UnityEditor.MenuItem(SkipMenuItem, true)]
    static bool ShowSkipInEditor()
    {
        UnityEditor.Menu.SetChecked(SkipMenuItem, UnityEditor.EditorPrefs.GetBool(SkipInEditorKey, false));
        return true;
    }
#endif
}

// One line of the main menu. The keyboard and the mouse share one highlight: pointing at a
// line selects it. Selected, it steps right behind a crimson bar and turns white.
public sealed class MenuButton : Button
{
    static readonly Color Idle = new Color(0.72f, 0.72f, 0.76f);

    TextMeshProUGUI label;
    Image bar;
    float lit;
    bool on;

    public static MenuButton Make(RectTransform parent, string text, Vector2 position, Vector2 size, Action onClick)
    {
        var rt = (RectTransform)new GameObject(text, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);   // invisible, but it takes the mouse

        var button = rt.gameObject.AddComponent<MenuButton>();
        button.targetGraphic = hit;
        button.transition = Transition.None;
        button.onClick.AddListener(() => onClick());

        var barRt = (RectTransform)new GameObject("Bar", typeof(RectTransform)).transform;
        barRt.SetParent(rt, false);
        barRt.anchorMin = barRt.anchorMax = barRt.pivot = new Vector2(0f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, 0f);
        barRt.sizeDelta = new Vector2(5f, size.y * 0.58f);
        button.bar = barRt.gameObject.AddComponent<Image>();
        button.bar.color = new Color32(0xDC, 0x14, 0x3C, 0x00);
        button.bar.raycastTarget = false;

        button.label = MainMenu.MakeText(rt, "Label", GameFonts.Body, 40f, Idle, Vector2.zero, size);
        button.label.alignment = TextAlignmentOptions.Left;   // by the font's line, so every row sits the same
        button.label.textWrappingMode = TextWrappingModes.NoWrap;
        button.label.text = text;
        button.Apply();
        return button;
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        // Lit when selected, not merely pointed at: pointing selects anyway, and a cursor left
        // resting on the first line kept it lit while the arrows moved the selection away.
        on = state == SelectionState.Selected || state == SelectionState.Pressed;
        if (instant) { lit = on ? 1f : 0f; Apply(); }
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (IsInteractable()) Select();
    }

    void Update()
    {
        float target = on ? 1f : 0f;
        if (Mathf.Approximately(lit, target)) return;
        lit = Mathf.MoveTowards(lit, target, Time.unscaledDeltaTime * 7f);
        Apply();
    }

    void Apply()
    {
        if (label == null) return;
        float e = Mathf.SmoothStep(0f, 1f, lit);
        label.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(0f, 26f, e), 0f);
        label.color = Color.Lerp(Idle, Color.white, e);
        Color c = bar.color;
        c.a = e;
        bar.color = c;
    }
}
