using System.Collections.Generic;
using Kehai;
using Kehai.Karen;
using Kehai.Blink;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

// Esc: the settings, in the game's black, red and white, softened since 0.4.1 (UiTheme:
// charcoal, coral, warm white, round corners, a rounded face). One page of settings (sound,
// display, mouse, the webcam), a Keys tab listing every key in the game, and a Tricks tab
// (0.4.1): everything Karen can do, each with a short video and three plain lines
// (TrickGuide). The shift's own choices (resume, restart, main menu, quit) sit in the bar
// along the bottom. The game pauses while it's open, and everything chosen here is
// remembered. The main menu opens the same pages, without the shift.
public sealed class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }
    public bool Visible { get; private set; }

    enum Tab { Settings, Keys, Tricks }
    Tab tab;
    Vector2 scroll;
    GUIStyle text, dim, heading, title, centred;
    Texture2D faintTexture, thumbTexture;
    int size;
    float labelWidth;
    int slider;              // numbers the sliders as they're drawn
    int dragging = -1;       // the slider the mouse is holding
    bool confirmingRestart, restartNow, confirmingMenu, menuNow;
    bool fromMainMenu;       // opened from the main menu: no shift to restart, and Back returns there
    AudioSource preview;

    const string SensitivityKey = "Kehai.MouseSensitivity";
    const float MinSensitivity = 0.5f, MaxSensitivity = 10f;

    // The three colours, and white at two strengths for what's secondary or behind: the
    // theme's (0.4.1), so the menu matches the map and the HUD.
    static readonly Color Black = UiTheme.Panel;
    static readonly Color Red = UiTheme.Accent;
    static readonly Color White = UiTheme.Text;
    static readonly Color Dim = UiTheme.Dim;
    static readonly Color Faint = UiTheme.Faint;

    // The Tricks tab: which trick is open, and its video.
    int trick;
    VideoPlayer video;
    RenderTexture videoPicture;
    string videoOf;

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

    void OnDestroy()
    {
        if (faintTexture != null) Destroy(faintTexture);
        if (thumbTexture != null) Destroy(thumbTexture);
        StopVideo();
        if (videoPicture != null) { videoPicture.Release(); Destroy(videoPicture); }
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
        StopVideo();
        FullScreenPanel.Set(this, false);
        if (!fromMainMenu) GamePause.Set(false);
        fromMainMenu = false;
        PlayerPrefs.Save();
    }

    // ---- the page -----------------------------------------------------------------------------

    void OnGUI()
    {
        if (!Visible) return;
        GUI.depth = -100;
        size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 42f, 15f, 36f));
        Styles();

        Fill(new Rect(0f, 0f, Screen.width, Screen.height), UiTheme.Scrim);
        // The Tricks tab is wider: a list beside a video.
        float w = Mathf.Min(size * (tab == Tab.Tricks ? 46f : 34f), Screen.width - size * 2f);
        var panel = new Rect((Screen.width - w) * 0.5f, size, w, Screen.height - size * 2f);
        Fill(panel, Black, size * 0.9f);
        Fill(new Rect(panel.x + size * 1.2f, panel.y + size * 0.45f, size * 2.4f, Mathf.Max(3f, size * 0.18f)), Red, size);
        var inner = new Rect(panel.x + size * 1.2f, panel.y + size, panel.width - size * 2.4f, panel.height - size * 2f);

        GUI.Label(new Rect(inner.x, inner.y, inner.width * 0.4f, size * 2f), fromMainMenu ? "SETTINGS" : "PAUSED", title);
        float tabWidth = size * 5.5f;
        if (TabButton(new Rect(inner.xMax - tabWidth * 3f, inner.y, tabWidth, size * 2f), "SETTINGS", tab == Tab.Settings)) Go(Tab.Settings);
        if (TabButton(new Rect(inner.xMax - tabWidth * 2f, inner.y, tabWidth, size * 2f), "KEYS", tab == Tab.Keys)) Go(Tab.Keys);
        if (TabButton(new Rect(inner.xMax - tabWidth, inner.y, tabWidth, size * 2f), "TRICKS", tab == Tab.Tricks)) Go(Tab.Tricks);

        var content = new Rect(inner.x, inner.y + size * 2.8f, inner.width, inner.height - size * 5.6f);
        labelWidth = content.width * 0.38f;
        slider = 0;

        // The scroll bar, in the page's colours: the skin's own thumb is swapped for the length
        // of this page, so nothing else drawn with IMGUI changes.
        GUIStyle thumb = GUI.skin.verticalScrollbarThumb;
        Texture2D thumbWas = thumb.normal.background;
        thumb.normal.background = thumbTexture;
        var bar = new GUIStyle(GUI.skin.verticalScrollbar) { normal = { background = faintTexture } };
        GUILayout.BeginArea(content);
        scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, bar);
        if (tab == Tab.Settings) DrawSettings(); else if (tab == Tab.Keys) DrawKeys(); else DrawTricks(content.width);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        thumb.normal.background = thumbWas;

        DrawBar(new Rect(inner.x, inner.yMax - size * 1.9f, inner.width, size * 1.9f));
    }

    void Go(Tab to)
    {
        tab = to;
        scroll = Vector2.zero;
        if (to != Tab.Tricks) StopVideo();
    }

    // ---- the Tricks tab (0.4.1) ----------------------------------------------------------------

    // Everything Karen can do: her tricks down the left, the one you pick on the right with
    // its video and three plain lines.
    void DrawTricks(float width)
    {
        TrickGuide.Entry[] all = TrickGuide.All;
        trick = Mathf.Clamp(trick, 0, all.Length - 1);
        float listWidth = Mathf.Min(size * 11f, width * 0.3f);

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(listWidth));
        GUILayout.Space(size * 0.4f);
        for (int i = 0; i < all.Length; i++)
        {
            Rect row = GUILayoutUtility.GetRect(listWidth, size * 1.7f, GUILayout.Width(listWidth), GUILayout.Height(size * 1.7f));
            bool on = i == trick, hover = row.Contains(Event.current.mousePosition);
            if (on) Fill(row, Red, UiTheme.Corner(row.height));
            else if (hover) Fill(row, Faint, UiTheme.Corner(row.height));
            Color was = text.normal.textColor;
            text.normal.textColor = on ? Color.white : hover ? White : Dim;
            GUI.Label(new Rect(row.x + size * 0.7f, row.y, row.width - size * 0.9f, row.height), all[i].Title, text);
            text.normal.textColor = was;
            if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { trick = i; StopVideo(); }
            GUILayout.Space(size * 0.12f);
        }
        GUILayout.EndVertical();

        GUILayout.Space(size * 1.2f);
        GUILayout.BeginVertical();
        GUILayout.Space(size * 0.4f);
        TrickGuide.Entry e = all[trick];
        GUILayout.Label(e.Title, title);
        GUILayout.Space(size * 0.3f);

        // The video, 16 by 9, in a rounded frame; or a note where it would be.
        float videoWidth = width - listWidth - size * 2.2f;
        Rect frame = GUILayoutUtility.GetRect(videoWidth, videoWidth * 9f / 16f, GUILayout.Width(videoWidth), GUILayout.Height(videoWidth * 9f / 16f));
        Fill(frame, UiTheme.Ink, size * 0.6f);
        Texture picture = VideoFor(e.Id);
        if (picture != null)
        {
            var inside = new Rect(frame.x + 3f, frame.y + 3f, frame.width - 6f, frame.height - 6f);
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(inside, picture, ScaleMode.ScaleToFit, false, 0f, Color.white, 0f, size * 0.5f);
        }
        else
        {
            GUI.Label(frame, "No video of this one yet.", NoVideo());
        }

        GUILayout.Space(size * 0.5f);
        Line("WHAT SHE DOES", e.Does);
        Line("HOW YOU NOTICE", e.Notice);
        Line("WHAT YOU CAN DO", e.Answer);
        if (e.Chore)
        {
            GUILayout.Space(size * 0.3f);
            Rect chip = GUILayoutUtility.GetRect(size * 9.5f, size * 1.4f, GUILayout.Width(size * 9.5f), GUILayout.Height(size * 1.4f));
            Fill(chip, UiTheme.AccentDim, chip.height * 0.5f);
            GUI.Label(chip, "Leaves you a job", centred);
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    void Line(string label, string words)
    {
        GUILayout.Space(size * 0.35f);
        GUILayout.Label(label, heading);
        GUILayout.Label(words, text);
    }

    GUIStyle noVideo;
    GUIStyle NoVideo()
    {
        if (noVideo == null) noVideo = new GUIStyle(centred);
        noVideo.fontSize = size;
        noVideo.normal.textColor = Dim;
        return noVideo;
    }

    // The picture of a trick's video, playing; null when there's none. The game is paused under
    // the menu, so the player runs on real time, and silent.
    Texture VideoFor(string id)
    {
        if (videoOf == id) return video != null && video.isPrepared ? videoPicture : null;
        StopVideo();
        videoOf = id;
        string path = TrickGuide.VideoPath(id);
        if (path == null) return null;
        if (videoPicture == null) videoPicture = new RenderTexture(960, 540, 0) { name = "Trick video", hideFlags = HideFlags.DontSave };
        if (video == null)
        {
            video = gameObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = videoPicture;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.isLooping = true;
            video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            video.skipOnDrop = true;
        }
        video.source = VideoSource.Url;
        video.url = "file://" + path;
        video.Play();
        return null;
    }

    void StopVideo()
    {
        videoOf = null;
        if (video != null && (video.isPlaying || video.isPrepared)) video.Stop();
    }

    void DrawSettings()
    {
        Heading("SOUND");
        float master = Slider("All sounds", SoundSettings.Master, out bool masterDone);
        if (!Mathf.Approximately(master, SoundSettings.Master)) SoundSettings.Master = master;
        if (masterDone) Preview(SoundKind.Effects);
        foreach (SoundKind kind in System.Enum.GetValues(typeof(SoundKind)))
        {
            float level = Slider(ShortLabel(kind), SoundSettings.Get(kind), out bool done);
            if (!Mathf.Approximately(level, SoundSettings.Get(kind))) SoundSettings.Set(kind, level);
            if (done) Preview(kind);
        }

        Heading("DISPLAY");
        List<Vector2Int> sizes = DisplaySettings.Resolutions();
        Vector2Int now = DisplaySettings.Resolution;
        int at = Mathf.Max(0, sizes.IndexOf(now));
        int step = Stepper("Resolution", $"{now.x} × {now.y}");
        if (step != 0) DisplaySettings.SetResolution(sizes[Mathf.Clamp(at + step, 0, sizes.Count - 1)]);
        bool full = Switch("Full screen", DisplaySettings.FullScreen);
        if (full != DisplaySettings.FullScreen) DisplaySettings.FullScreen = full;
        int quality = (int)DisplaySettings.Current + Stepper("Quality", DisplaySettings.Current.ToString());
        quality = Mathf.Clamp(quality, 0, 2);
        if (quality != (int)DisplaySettings.Current) DisplaySettings.SetQuality((DisplaySettings.Quality)quality);
        if (Application.isEditor) GUILayout.Label("Resolution and full screen change in the built game.", dim);

        Heading("MOUSE");
        float sensitivity = Sensitivity;
        float picked = Slider("Sensitivity", Mathf.InverseLerp(MinSensitivity, MaxSensitivity, sensitivity), out _, $"{sensitivity:0.0}");
        float value = Mathf.Round(Mathf.Lerp(MinSensitivity, MaxSensitivity, picked) * 10f) / 10f;
        if (!Mathf.Approximately(value, sensitivity)) Sensitivity = value;

        Heading("WEBCAM BLINKING");
        DrawWebcam();
    }

    void DrawWebcam()
    {
        BlinkTracker tracker = BlinkTracker.Instance;
        string status = !BlinkTracker.Consented ? "Off"
            : tracker != null && tracker.WebcamLive ? (tracker.Calibrated ? "On" : "On, not calibrated")
            : "On, no signal";
        GUILayout.BeginHorizontal();
        GUILayout.Label("Status", text, GUILayout.Width(labelWidth));
        GUILayout.Label(status, text);
        GUILayout.EndHorizontal();
        if (fromMainMenu)
        {
            GUILayout.Label("Set it up in the store: Esc.", dim);
            return;
        }
        if (tracker == null) return;
        GUILayout.BeginHorizontal();
        GUILayout.Space(labelWidth);
        if (!BlinkTracker.Consented)
        {
            if (LayoutButton("TURN ON", size * 6f)) { Close(); tracker.AskConsent(); }
        }
        else
        {
            if (LayoutButton("TURN OFF", size * 6f)) tracker.RevokeConsent();
            GUILayout.Space(size * 0.4f);
            if (LayoutButton("CALIBRATE", size * 6f)) { Close(); tracker.BeginCalibration(); }
        }
        GUILayout.Space(size * 0.4f);
        if (LayoutButton("TEST", size * 4.5f))
        {
            Close();
            var panel = FindAnyObjectByType<BlinkTestPanel>();
            if (panel != null) panel.Show();
        }
        GUILayout.EndHorizontal();
    }

    void DrawKeys()
    {
        foreach (Controls.Section section in Controls.All)
        {
            Heading(section.Title.ToUpperInvariant());
            foreach (Controls.Entry entry in section.Entries)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{entry.Keys}</b>", text, GUILayout.Width(labelWidth * 0.8f));
                GUILayout.Label(entry.Action, dim);
                GUILayout.EndHorizontal();
            }
        }
    }

    // Along the bottom: back to the game, or out of the shift, each asked once.
    void DrawBar(Rect r)
    {
        float gap = size * 0.5f, w = size * 7.5f;
        if (fromMainMenu)
        {
            if (Button(new Rect(r.x, r.y, w, r.height), "BACK", true)) Close();
            return;
        }
        if (confirmingRestart || confirmingMenu)
        {
            string question = confirmingRestart ? "Restart this shift? Its progress is lost." : "Leave the shift for the main menu?";
            GUI.Label(new Rect(r.x, r.y, r.width - (w + gap) * 2f, r.height), question, text);
            if (Button(new Rect(r.xMax - w * 2f - gap, r.y, w, r.height), "YES", true))
            {
                if (confirmingRestart) restartNow = true; else menuNow = true;
            }
            if (Button(new Rect(r.xMax - w, r.y, w, r.height), "NO")) confirmingRestart = confirmingMenu = false;
            return;
        }
        if (Button(new Rect(r.x, r.y, w, r.height), "RESUME", true)) Close();
        if (Button(new Rect(r.x + w + gap, r.y, w * 1.25f, r.height), "RESTART SHIFT")) confirmingRestart = true;
        if (Button(new Rect(r.x + w * 2.25f + gap * 2f, r.y, w * 1.1f, r.height), "MAIN MENU")) confirmingMenu = true;
        if (Button(new Rect(r.xMax - w, r.y, w, r.height), Application.isEditor ? "STOP" : "QUIT")) Quit();
    }

    // ---- the pieces ---------------------------------------------------------------------------

    void Heading(string label)
    {
        GUILayout.Space(size * 0.9f);
        GUILayout.Label(label, heading);
    }

    // A wide slider that's easy to grab: label, bar, value. `released` is true on the frame
    // the mouse lets go of it.
    float Slider(string label, float value01, out bool released, string shown = null)
    {
        int id = slider++;
        released = false;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, text, GUILayout.Width(labelWidth));
        Rect r = GUILayoutUtility.GetRect(size * 6f, size * 1.6f, GUILayout.ExpandWidth(true));
        GUILayout.Label(shown ?? $"{value01 * 100f:0}%", dim, GUILayout.Width(size * 3f));
        GUILayout.EndHorizontal();

        var track = new Rect(r.x + size * 0.4f, r.center.y - size * 0.13f, r.width - size * 0.8f, Mathf.Max(4f, size * 0.26f));
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
            Fill(track, Faint, track.height);
            Fill(new Rect(track.x, track.y, Mathf.Max(track.height, track.width * value01), track.height), Red, track.height);
            float knob = size * 0.8f;
            Fill(new Rect(track.x + track.width * value01 - knob * 0.5f, r.center.y - knob * 0.5f, knob, knob), White, knob);
        }
        return value01;
    }

    // "<  value  >": -1, 0 or +1 for the arrow pressed.
    int Stepper(string label, string value)
    {
        int step = 0;
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, text, GUILayout.Width(labelWidth));
        if (LayoutButton("<", size * 2f)) step = -1;
        GUILayout.Label(value, centred, GUILayout.Width(size * 8f), GUILayout.Height(size * 1.6f));
        if (LayoutButton(">", size * 2f)) step = 1;
        GUILayout.EndHorizontal();
        return step;
    }

    // An ON / OFF switch: red when on.
    bool Switch(string label, bool on)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, text, GUILayout.Width(labelWidth));
        Rect r = GUILayoutUtility.GetRect(size * 4f, size * 1.6f, GUILayout.Width(size * 4f), GUILayout.Height(size * 1.6f));
        GUILayout.EndHorizontal();
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, on ? Red : hover ? new Color(1f, 1f, 1f, 0.2f) : Faint, r.height * 0.5f);
        GUI.Label(r, on ? "ON" : "OFF", centred);
        return GUI.Button(r, GUIContent.none, GUIStyle.none) ? !on : on;
    }

    bool LayoutButton(string label, float width) =>
        Button(GUILayoutUtility.GetRect(width, size * 1.6f, GUILayout.Width(width), GUILayout.Height(size * 1.6f)), label);

    // A flat button. The main one is white with black text; any button turns red under the mouse.
    bool Button(Rect r, string label, bool main = false)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, hover ? Red : main ? White : Faint, UiTheme.Corner(r.height));
        Color was = centred.normal.textColor;
        centred.normal.textColor = main && !hover ? (Color)UiTheme.Ink : White;
        GUI.Label(r, label, centred);
        centred.normal.textColor = was;
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    bool TabButton(Rect r, string label, bool on)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Color was = centred.normal.textColor;
        centred.normal.textColor = on || hover ? White : Dim;
        GUI.Label(r, label, centred);
        centred.normal.textColor = was;
        if (on) Fill(new Rect(r.x + size, r.yMax - size * 0.32f, r.width - size * 2f, Mathf.Max(3f, size * 0.18f)), Red, size);
        return GUI.Button(r, GUIContent.none, GUIStyle.none) && !on;
    }

    string ShortLabel(SoundKind kind)
    {
        switch (kind)
        {
            case SoundKind.Karen: return GameNames.Antagonist;
            case SoundKind.Music: return "Music";
            case SoundKind.Voice: return "Announcements";
            default: return "Effects";
        }
    }

    // ---- what the settings do -------------------------------------------------------------------

    void Preview(SoundKind kind)
    {
        AudioClip clip = kind == SoundKind.Karen ? ProceduralAudio.Tell(TellKind.RevealSting)
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
        if (text == null)
        {
            text = UiTheme.Soften(new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft });
            dim = new GUIStyle(text);
            heading = new GUIStyle(text) { fontStyle = FontStyle.Bold };
            title = new GUIStyle(text) { fontStyle = FontStyle.Bold };
            centred = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            faintTexture = Solid(Faint);
            thumbTexture = Solid(new Color(1f, 1f, 1f, 0.45f));
        }
        text.fontSize = dim.fontSize = centred.fontSize = size;
        heading.fontSize = Mathf.RoundToInt(size * 0.8f);
        title.fontSize = Mathf.RoundToInt(size * 1.5f);
        text.normal.textColor = title.normal.textColor = centred.normal.textColor = White;
        dim.normal.textColor = Dim;
        heading.normal.textColor = Red;
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    // A filled shape; with a radius, round at the corners (0.4.1).
    static void Fill(Rect r, Color c, float radius = 0f) => UiTheme.Fill(r, c, radius);
}
