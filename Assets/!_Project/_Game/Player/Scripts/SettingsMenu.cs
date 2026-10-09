using Kehai;
using Kehai.Aiko;
using Kehai.Blink;
using UnityEngine;
using UnityEngine.SceneManagement;

// Esc: the settings. Sound first: one slider for all game sounds at once, then each kind of
// sound. Then restart the shift or leave it for the main menu, mouse sensitivity, Aiko's floor
// cone, the webcam, and a Keys tab listing every key in the game. The game pauses while it's
// open, and everything chosen here is remembered. The main menu opens the same pages, without
// the shift.
public sealed class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }
    public bool Visible { get; private set; }

    enum Tab { Settings, Keys }
    Tab tab;
    Vector2 scroll;
    GUIStyle body, heading, title, small, button, toggle;
    int size;
    float labelWidth;
    int slider;              // numbers the sliders as they're drawn
    int dragging = -1;       // the slider the mouse is holding
    bool confirmingRestart, restartNow, confirmingMenu, menuNow;
    bool fromMainMenu;       // opened from the main menu: no shift to restart, and Back returns there
    AudioSource preview;

    const string SensitivityKey = "Kehai.MouseSensitivity";
    const float MinSensitivity = 0.5f, MaxSensitivity = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode || Instance != null) return;
        var go = new GameObject("Settings");
        DontDestroyOnLoad(go);
        go.AddComponent<SettingsMenu>();
    }

    void Awake() => Instance = this;

    void Start() => ApplySensitivity();

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Visible) Close();
    }

    // A restarted store has a new player, who gets your sensitivity too.
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplySensitivity();

    void Update()
    {
        if (restartNow)
        {
            // Not from inside OnGUI: loading a scene mid-layout leaves the GUI stack unbalanced.
            restartNow = false;
            Close();
            ShiftRestart.Restart();
            return;
        }
        if (menuNow)
        {
            menuNow = false;
            Close();
            MainMenu.ReturnToMenu();
            return;
        }
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (Visible) { Close(); return; }
        // Esc closes whatever else is open first: the map, the replay, the blink test, the
        // webcam question, the end-of-shift review.
        if (FullScreenPanel.AnyOpen || FullScreenPanel.ClosedOnFrame == Time.frameCount) return;
        if (BlinkTracker.Instance != null && BlinkTracker.Instance.ConsentBusy) return;
        Open();
    }

    public void Open()
    {
        Visible = true;
        confirmingRestart = confirmingMenu = false;
        fromMainMenu = false;
        tab = Tab.Settings;
        scroll = Vector2.zero;
        FullScreenPanel.Set(this, true);
        GamePause.Set(true);
    }

    // The main menu's Settings and Controls: the same pages, and the game stays paused after.
    public void OpenFromMainMenu(bool keys)
    {
        Open();
        fromMainMenu = true;
        tab = keys ? Tab.Keys : Tab.Settings;
    }

    public void Close()
    {
        Visible = false;
        dragging = -1;
        FullScreenPanel.Set(this, false);
        if (!fromMainMenu) GamePause.Set(false);
        fromMainMenu = false;
        PlayerPrefs.Save();
    }

    // ---- drawing ------------------------------------------------------------------------------

    void OnGUI()
    {
        if (!Visible) return;
        GUI.depth = -100;
        size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 40f, 16f, 40f));
        Styles();

        Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.75f));
        float w = Mathf.Min(size * 40f, Screen.width - size * 2f);
        var panel = new Rect((Screen.width - w) * 0.5f, size, w, Screen.height - size * 2f);
        Fill(panel, new Color(0.07f, 0.075f, 0.095f, 0.98f));
        var inner = new Rect(panel.x + size, panel.y + size * 0.8f, panel.width - size * 2f, panel.height - size * 1.6f);

        GUI.Label(new Rect(inner.x, inner.y, inner.width * 0.5f, size * 2.2f), fromMainMenu ? "Settings" : "Paused", title);
        float tabWidth = size * 7f;
        if (Tab_(new Rect(inner.xMax - tabWidth * 2f - size * 0.5f, inner.y, tabWidth, size * 2f), "Settings", tab == Tab.Settings)) { tab = Tab.Settings; scroll = Vector2.zero; }
        if (Tab_(new Rect(inner.xMax - tabWidth, inner.y, tabWidth, size * 2f), "Keys", tab == Tab.Keys)) { tab = Tab.Keys; scroll = Vector2.zero; }

        var content = new Rect(inner.x, inner.y + size * 3f, inner.width, inner.height - size * 6f);
        labelWidth = content.width * 0.42f;
        slider = 0;
        GUILayout.BeginArea(content);
        scroll = GUILayout.BeginScrollView(scroll);
        if (tab == Tab.Settings) DrawSettings(); else DrawKeys();
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        float y = inner.yMax - size * 2.2f;
        if (GUI.Button(new Rect(inner.x, y, size * 9f, size * 2.2f), fromMainMenu ? "Back  (Esc)" : "Resume  (Esc)", button)) Close();
        if (!fromMainMenu && GUI.Button(new Rect(inner.xMax - size * 9f, y, size * 9f, size * 2.2f), Application.isEditor ? "Stop playing" : "Quit the game", button)) Quit();
    }

    void DrawSettings()
    {
        // The one slider for every sound comes first, so it never has to be looked for.
        Heading("Sound");
        float master = Slider("All game sounds", SoundSettings.Master, out bool masterDone);
        if (!Mathf.Approximately(master, SoundSettings.Master)) SoundSettings.Master = master;
        if (masterDone) Preview(SoundKind.Effects);
        GUILayout.Label("Turns every sound in the game up or down at once: the store, you, " + GameNames.Antagonist +
                        ", the radio and the PA. Below, each of them on its own.", small);
        foreach (SoundKind kind in System.Enum.GetValues(typeof(SoundKind)))
        {
            float level = Slider(SoundSettings.Label(kind), SoundSettings.Get(kind), out bool done);
            if (!Mathf.Approximately(level, SoundSettings.Get(kind))) SoundSettings.Set(kind, level);
            if (done) Preview(kind);
        }
        GUILayout.Label(GameNames.Antagonist + "'s warning sounds tell you a trick is coming, so keep her audible. You hear a sample when you let go of a slider.", small);

        if (!fromMainMenu) DrawShift();

        Heading("Mouse");
        float sensitivity = Sensitivity;
        float picked = Slider("Look sensitivity", Mathf.InverseLerp(MinSensitivity, MaxSensitivity, sensitivity), out _, $"{sensitivity:0.0}");
        float value = Mathf.Round(Mathf.Lerp(MinSensitivity, MaxSensitivity, picked) * 10f) / 10f;
        if (!Mathf.Approximately(value, sensitivity)) Sensitivity = value;

        Heading(GameNames.Antagonist);
        bool cone = GUILayout.Toggle(AikoFloorCone.Enabled, "  Show where " + GameNames.Antagonist + " is looking, as a cone on the floor", toggle);
        if (cone != AikoFloorCone.Enabled) AikoFloorCone.Enabled = cone;
        GUILayout.Label("Blue: walking her rounds. Orange: she noticed something. Red: she's hunting you, or can see you right now.", small);

        Heading("Webcam blinking");
        BlinkTracker tracker = BlinkTracker.Instance;
        string status = !BlinkTracker.Consented ? "Off. " + GameNames.Antagonist + " can react when your real eyes close; the camera never records and nothing leaves this computer."
            : tracker != null && tracker.WebcamLive ? $"On, reading your eyes through {BlinkSidecar.Which}." + (tracker.Calibrated ? " Calibrated." : " Not calibrated yet: press Calibrate.")
            : "On, but no signal from the camera yet. Open the blink test to see why.";
        GUILayout.Label(status, body);
        if (fromMainMenu)
        {
            GUILayout.Label("Turn it on, calibrate it and test it from this menu once you're in the store (Esc).", small);
            return;
        }
        GUILayout.BeginHorizontal();
        if (tracker != null)
        {
            if (!BlinkTracker.Consented)
            {
                if (GUILayout.Button("Turn it on…", button, GUILayout.Width(size * 9f))) { Close(); tracker.AskConsent(); }
            }
            else
            {
                if (GUILayout.Button("Turn it off", button, GUILayout.Width(size * 8f))) tracker.RevokeConsent();
                if (GUILayout.Button("Calibrate", button, GUILayout.Width(size * 8f))) { Close(); tracker.BeginCalibration(); }
            }
            if (GUILayout.Button("Blink test", button, GUILayout.Width(size * 8f)))
            {
                Close();
                var panel = FindAnyObjectByType<BlinkTestPanel>();
                if (panel != null) panel.Show();
            }
        }
        GUILayout.EndHorizontal();
    }

    void DrawShift()
    {
        Heading("Shift");
        if (confirmingRestart)
        {
            GUILayout.Label("Restart this shift? What you've done so far in it is lost; " + GameNames.Antagonist + " remembers earlier shifts.", body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yes, restart", button, GUILayout.Width(size * 8f))) restartNow = true;
            if (GUILayout.Button("No", button, GUILayout.Width(size * 5f))) confirmingRestart = false;
            GUILayout.EndHorizontal();
        }
        else if (confirmingMenu)
        {
            GUILayout.Label("Leave for the main menu? The shift under way, if there is one, is lost; " + GameNames.Antagonist + " remembers earlier shifts.", body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Yes, main menu", button, GUILayout.Width(size * 9f))) menuNow = true;
            if (GUILayout.Button("No", button, GUILayout.Width(size * 5f))) confirmingMenu = false;
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restart this shift", button, GUILayout.Width(size * 11f))) confirmingRestart = true;
            if (GUILayout.Button("Main menu", button, GUILayout.Width(size * 8f))) confirmingMenu = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Restart starts the shift again from the beginning: the store resets and you go back to where you start.", small);
        }
    }

    void DrawKeys()
    {
        foreach (Controls.Section section in Controls.All)
        {
            Heading(section.Title);
            foreach (Controls.Entry entry in section.Entries)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{entry.Keys}</b>", body, GUILayout.Width(labelWidth * 0.8f));
                GUILayout.Label(entry.Action, body);
                GUILayout.EndHorizontal();
            }
        }
        GUILayout.Space(size * 0.5f);
        GUILayout.Label("The same list is in CONTROLS.md, next to the project.", small);
    }

    void Heading(string text)
    {
        GUILayout.Space(size * 0.6f);
        GUILayout.Label(text, heading);
    }

    // A wide slider that's easy to grab: label, bar, value. `released` is true on the frame
    // the mouse lets go of it.
    float Slider(string label, float value01, out bool released, string shown = null)
    {
        int id = slider++;
        released = false;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, body, GUILayout.Width(labelWidth));
        Rect r = GUILayoutUtility.GetRect(size * 6f, size * 1.7f, GUILayout.ExpandWidth(true));
        GUILayout.Label(shown ?? $"{value01 * 100f:0}%", body, GUILayout.Width(size * 3.5f));
        GUILayout.EndHorizontal();

        var track = new Rect(r.x + size * 0.5f, r.center.y - size * 0.2f, r.width - size, size * 0.4f);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) dragging = id;
        if (dragging == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            value01 = Mathf.Clamp01((e.mousePosition.x - track.x) / track.width);
            e.Use();
        }
        else if (dragging == id && e.type == EventType.MouseUp)
        {
            dragging = -1;
            released = true;
            e.Use();
        }
        if (e.type == EventType.Repaint)
        {
            Fill(track, new Color(0.2f, 0.22f, 0.27f));
            Fill(new Rect(track.x, track.y, track.width * value01, track.height), new Color(0.3f, 0.82f, 1f));
            float knob = size * 0.9f;
            Fill(new Rect(track.x + track.width * value01 - knob * 0.5f, r.center.y - knob * 0.5f, knob, knob), Color.white);
        }
        return value01;
    }

    bool Tab_(Rect r, string text, bool on)
    {
        Fill(r, on ? new Color(0.24f, 0.28f, 0.36f) : new Color(0.13f, 0.14f, 0.18f));
        return GUI.Button(r, text, button) && !on;
    }

    // ---- what the settings do -------------------------------------------------------------------

    void Preview(SoundKind kind)
    {
        AudioClip clip = kind == SoundKind.Aiko ? ProceduralAudio.AikoStep()
            : kind == SoundKind.Voice ? ProceduralAudio.PaChime()
            : kind == SoundKind.Effects ? ProceduralAudio.PlayerStep(0)
            : null;   // music: the radio is its own sample
        if (clip == null) return;
        if (preview == null)
        {
            preview = gameObject.AddComponent<AudioSource>();
            preview.spatialBlend = 0f;
            preview.playOnAwake = false;
            preview.ignoreListenerPause = true;
        }
        preview.PlayOneShot(clip, SoundSettings.Get(kind));
    }

    static float Sensitivity
    {
        get
        {
            if (PlayerPrefs.HasKey(SensitivityKey)) return PlayerPrefs.GetFloat(SensitivityKey);
            var motor = FindAnyObjectByType<PlayerMotor>();
            return motor != null ? motor.mouseSensitivity : 4f;
        }
        set
        {
            PlayerPrefs.SetFloat(SensitivityKey, Mathf.Clamp(value, MinSensitivity, MaxSensitivity));
            ApplySensitivity();
        }
    }

    static void ApplySensitivity()
    {
        if (!PlayerPrefs.HasKey(SensitivityKey)) return;   // until changed, the scene's own value stands
        float value = PlayerPrefs.GetFloat(SensitivityKey);
        foreach (PlayerMotor motor in FindObjectsByType<PlayerMotor>()) motor.mouseSensitivity = value;
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Styles()
    {
        if (body == null)
        {
            body = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            heading = new GUIStyle(body) { fontStyle = FontStyle.Bold };
            title = new GUIStyle(heading);
            small = new GUIStyle(body);
            button = new GUIStyle(GUI.skin.button);
            toggle = new GUIStyle(GUI.skin.toggle) { wordWrap = true };
        }
        body.fontSize = size;
        heading.fontSize = Mathf.RoundToInt(size * 1.2f);
        title.fontSize = Mathf.RoundToInt(size * 1.6f);
        small.fontSize = Mathf.RoundToInt(size * 0.8f);
        button.fontSize = size;
        toggle.fontSize = size;
        body.normal.textColor = heading.normal.textColor = title.normal.textColor = toggle.normal.textColor = new Color(0.94f, 0.94f, 0.92f);
        toggle.onNormal.textColor = toggle.hover.textColor = toggle.onHover.textColor = toggle.normal.textColor;
        small.normal.textColor = new Color(0.68f, 0.7f, 0.75f);
    }

    static void Fill(Rect r, Color c)
    {
        Color before = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = before;
    }
}
