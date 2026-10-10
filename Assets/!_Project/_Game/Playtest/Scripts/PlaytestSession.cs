using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Kehai.Karen;
using Kehai.Replay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Playtest
{
    // One launch of a playtest build, recorded from the first frame to quitting (PLAYTEST.md):
    // the computer it ran on, every menu and panel opened and closed, the keys that matter and
    // what E was pointed at, where the tester stood and looked twice a second (with the 3D
    // replay file and time it belongs to), the frame rate, errors, shifts, bug notes and the
    // answers to the questions. At the end it's packed with the shifts' recordings and sent.
    //
    // Batch mode (headless tests): no screens; -playtest-code <code> and
    // -playtest-consent send|keep stand in for what a tester would type.
    public sealed class PlaytestSession : MonoBehaviour
    {
        public static PlaytestSession Instance { get; private set; }

        public PlaytestConfig Config { get; private set; }
        public string Stamp { get; private set; }          // the launch, yyyyMMdd_HHmmss
        public string Folder { get; private set; }
        public bool Open => log != null;
        public int ShiftsThisSession { get; private set; }
        public int CareerShifts => shiftManager != null ? shiftManager.ShiftNumber : 0;
        public string LastPackage { get; private set; }

        SessionLog log;
        DateTime startedUtc;
        float startedAt;
        bool wrappingUp;

        // What it watches.
        ShiftManager shiftManager;
        bool shiftActive;
        int shiftsBefore;
        readonly Dictionary<string, bool> panels = new Dictionary<string, bool>();
        float nextSample, nextFlush, fpsSince;
        int fpsFrames;
        float fpsWorst;
        string pendingFreshCareer;
        readonly ConcurrentQueue<(string kind, string message, string stack)> errors = new ConcurrentQueue<(string, string, string)>();
        readonly HashSet<string> errorsSeen = new HashSet<string>();

        const float SampleSeconds = 0.5f, FpsSeconds = 5f, FlushSeconds = 2f;
        const int MostErrors = 100;

        static readonly KeyCode[] Watched =
        {
            KeyCode.E, KeyCode.Q, KeyCode.C, KeyCode.F1, KeyCode.F2, KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10,
            KeyCode.L, KeyCode.Escape,
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Instance != null || !PlaytestConfig.Active) return;
            ReplayRecorder.RecordInterludes = true;
            var go = new GameObject("Playtest");
            DontDestroyOnLoad(go);
            go.AddComponent<PlaytestSession>();
            if (!Application.isBatchMode) go.AddComponent<PlaytestScreens>();
        }

        void Awake()
        {
            Instance = this;
            Config = PlaytestConfig.Current;
            PackCrashedSession();
            BeginSession();

            if (Application.isBatchMode)
            {
                string code = TesterCode.Normalize(PlaytestConfig.Arg("-playtest-code") ?? "BOT");
                if (code != null && code != TesterState.Code) SetCode(code);
                if (!TesterState.ConsentAsked) SetConsent(PlaytestConfig.Arg("-playtest-consent") == "send");
            }
        }

        void OnEnable()
        {
            Application.logMessageReceivedThreaded += OnLogMessage;
            Application.wantsToQuit += OnWantsToQuit;
            Application.focusChanged += OnFocus;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDisable()
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;
            Application.wantsToQuit -= OnWantsToQuit;
            Application.focusChanged -= OnFocus;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void Start()
        {
            if (TesterState.Sends) StartCoroutine(PlaytestUploader.SendAll(Config));   // anything left from last time
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- the session ------------------------------------------------------------------

        public void BeginSession(string continuing = null)
        {
            Stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            Folder = Path.Combine(PlaytestPaths.Sessions, Stamp);
            startedUtc = DateTime.UtcNow;
            startedAt = Time.realtimeSinceStartup;
            ShiftsThisSession = 0;
            wrappingUp = false;
            panels.Clear();
            errorsSeen.Clear();
            log = new SessionLog(Path.Combine(Folder, PlaytestPaths.LogName));
            TesterState.Running = Stamp;

            Resolution screen = Screen.currentResolution;
            Note("start", new Dictionary<string, object>
            {
                ["utc"] = startedUtc.ToString("o"),
                ["round"] = Config.round,
                ["build"] = Config.build,
                ["version"] = Application.version,
                ["code"] = TesterState.Code,
                ["sends"] = TesterState.Sends,
                ["continues"] = continuing,
                ["platform"] = Application.platform.ToString(),
                ["os"] = SystemInfo.operatingSystem,
                ["device"] = SystemInfo.deviceModel,
                ["cpu"] = $"{SystemInfo.processorType} ({SystemInfo.processorCount} threads)",
                ["ramMB"] = SystemInfo.systemMemorySize,
                ["gpu"] = SystemInfo.graphicsDeviceName,
                ["gpuMB"] = SystemInfo.graphicsMemorySize,
                ["graphics"] = SystemInfo.graphicsDeviceType.ToString(),
                ["screen"] = $"{Screen.width}x{Screen.height} of {screen.width}x{screen.height} @{screen.refreshRateRatio.value:0}Hz, {Screen.fullScreenMode}",
                ["quality"] = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "",
                ["language"] = Application.systemLanguage.ToString(),
            });
        }

        // Writes one line to the session's log.
        public void Note(string kind, Dictionary<string, object> fields = null)
        {
            if (log == null) return;
            try { log.Write(Time.realtimeSinceStartup - startedAt, kind, fields); }
            catch (Exception e) { Debug.LogWarning("Playtest: couldn't write the session log: " + e.Message); }
        }

        // Closes the log and packs the session into the outbox. Returns the zip.
        public string WrapUp(string reason)
        {
            if (log == null) return LastPackage;
            Note("end", new Dictionary<string, object> { ["reason"] = reason, ["shifts"] = ShiftsThisSession, ["careerShifts"] = CareerShifts });
            // Finish what's being recorded, so the package has it: a shift quit halfway, or the
            // stretch since the last clock-out (a new one starts if they keep playing).
            if (ShiftRecorder.Instance != null) ShiftRecorder.Instance.FinishNow();
            if (ReplayRecorder.Instance != null) ReplayRecorder.Instance.Cut();
            log.Dispose();
            log = null;
            LastPackage = Pack(Folder, startedUtc, Stamp, crashed: false);
            TesterState.Running = "";
            return LastPackage;
        }

        string Pack(string folder, DateTime since, string stamp, bool crashed)
        {
            try
            {
                var files = PlaytestPackage.Collect(folder, since, ShiftRecorder.Folder,
                    Path.Combine(Application.persistentDataPath, "karen_logs"), KarenLedger.DefaultPath,
                    Application.consoleLogPath, previousLogToo: crashed);
                string zip = Path.Combine(PlaytestPaths.Outbox, PlaytestPackage.ZipName(Config.round, TesterState.Code, stamp));
                PlaytestPackage.Pack(zip, files);
                Debug.Log($"Playtest: packed {files.Count} files into {zip} ({new FileInfo(zip).Length / 1024} KB)");
                return zip;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Playtest: couldn't pack the session: " + e.Message);
                return null;
            }
        }

        // The last launch never got to quit: pack what it left (and the game's log from then).
        void PackCrashedSession()
        {
            string stamp = TesterState.Running;
            if (string.IsNullOrEmpty(stamp)) return;
            string folder = Path.Combine(PlaytestPaths.Sessions, stamp);
            string logPath = Path.Combine(folder, PlaytestPaths.LogName);
            if (!File.Exists(logPath)) return;
            try { File.AppendAllText(logPath, MiniJson.Serialize(new Dictionary<string, object> { ["t"] = -1, ["k"] = "end", ["reason"] = "crashed or killed" }) + "\n"); }
            catch (Exception) { }
            Pack(folder, SessionLog.StartedUtc(logPath) ?? DateTime.UtcNow.AddHours(-12), stamp, crashed: true);
        }

        // ---- the tester -------------------------------------------------------------------

        public void SetCode(string code)
        {
            TesterState.Code = code;
            Note("code", new Dictionary<string, object> { ["code"] = code });
            if (TesterState.NeedsFreshCareer(code)) pendingFreshCareer = code;
        }

        public void SetConsent(bool send)
        {
            TesterState.SetConsent(send);
            Note("consent", new Dictionary<string, object> { ["sends"] = send });
        }

        // A new tester is a new employee: Karen forgets the last one, and calls this one by their code.
        void StartFreshCareer()
        {
            KarenBrain brain = KarenBrain.Instance;
            if (brain == null || brain.Ledger == null || shiftManager == null || shiftManager.IsShiftActive) return;
            string code = pendingFreshCareer;
            pendingFreshCareer = null;
            brain.Ledger.Wipe();
            brain.Ledger.Data.playerName = "Employee " + code;
            brain.Ledger.Save();
            shiftManager.SetShiftNumber(0);
            TesterState.CareerOf = code;
            if (MainMenu.Instance != null) MainMenu.Instance.RefreshCareer();
            Note("career", new Dictionary<string, object> { ["fresh"] = true, ["employee"] = brain.Ledger.Data.playerName });
        }

        public void Answers(Dictionary<string, object> answers)
        {
            Note("answers", answers);
            TesterState.MarkAnswered();
        }

        public void BugNote(string note)
        {
            ShiftRecorder recorder = ShiftRecorder.Instance;
            ReplayRecorder replay = ReplayRecorder.Instance;
            var fields = new Dictionary<string, object> { ["note"] = note ?? "" };
            AddWhere(fields, recorder, replay);
            Note("bug", fields);
            log?.Flush();
        }

        // ---- quitting ---------------------------------------------------------------------

        bool allowQuit;

        // Quitting asks the questions (once per tester) and sends the session first.
        bool OnWantsToQuit()
        {
            if (allowQuit || Application.isBatchMode || wrappingUp) { WrapUp("quit"); return true; }
            PlaytestScreens screens = GetComponent<PlaytestScreens>();
            if (screens == null) { WrapUp("quit"); return true; }
            wrappingUp = true;
            screens.Finish(quitAfter: true);
            return false;
        }

        public void QuitNow()
        {
            allowQuit = true;
            if (log != null) WrapUp("quit");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void CancelQuit() => wrappingUp = false;

        void OnApplicationQuit()
        {
            if (log != null) WrapUp("quit");   // the editor's Stop, or a quit that skipped the screens
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) log?.Flush();
        }

        // ---- watching ---------------------------------------------------------------------

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Note("scene", new Dictionary<string, object> { ["name"] = scene.name, ["mode"] = mode.ToString() });
            if (shiftManager != null) shiftManager.ShiftStateChanged -= OnShiftChanged;
            shiftManager = null;
        }

        void OnFocus(bool focused) => Note("focus", new Dictionary<string, object> { ["on"] = focused });

        void OnLogMessage(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Enqueue((type.ToString(), message, stack));
        }

        void Update()
        {
            if (log == null) return;

            if (shiftManager == null)
            {
                shiftManager = FindAnyObjectByType<ShiftManager>();
                if (shiftManager != null)
                {
                    shiftManager.ShiftStateChanged += OnShiftChanged;
                    shiftActive = shiftManager.IsShiftActive;
                }
            }
            if (pendingFreshCareer != null) StartFreshCareer();

            while (errors.TryDequeue(out var error))
            {
                if (errorsSeen.Count >= MostErrors || !errorsSeen.Add(error.message)) continue;
                Note("error", new Dictionary<string, object> { ["type"] = error.kind, ["message"] = error.message, ["stack"] = Shorten(error.stack, 1200) });
            }

            WatchPanels();
            WatchKeys();
            CountFrames();

            float now = Time.realtimeSinceStartup;
            if (now >= nextSample && !GamePause.Paused)
            {
                nextSample = now + SampleSeconds;
                Sample();
            }
            if (now >= nextFlush)
            {
                nextFlush = now + FlushSeconds;
                log.Flush();
            }
        }

        void OnShiftChanged()
        {
            if (shiftManager == null || shiftManager.IsShiftActive == shiftActive) return;
            shiftActive = shiftManager.IsShiftActive;
            var fields = new Dictionary<string, object> { ["n"] = shiftManager.ShiftNumber, ["state"] = shiftActive ? "start" : "end" };
            ShiftRecorder recorder = ShiftRecorder.Instance;
            if (recorder != null && !string.IsNullOrEmpty(recorder.Stem)) fields["stem"] = Path.GetFileName(recorder.Stem);
            if (!shiftActive) ShiftsThisSession++;
            Note("shift", fields);
        }

        // The panels, menus and screens a tester can have open, as they open and close.
        void WatchPanels()
        {
            Panel("main menu", MainMenu.Visible);
            Panel("settings", SettingsMenu.Instance != null && SettingsMenu.Instance.Visible);
            Panel("paused", GamePause.Paused);
            TaskListUI tasks = FindTaskList();
            Panel("task list", tasks != null && tasks.Visible);
            KarenDebugOverlay overlay = KarenBrain.Instance != null ? KarenBrain.Instance.GetComponent<KarenDebugOverlay>() : null;
            Panel("map (F1)", overlay != null && overlay.showOverlay);
            Panel("shift replay (F2)", overlay != null && overlay.showScrubber);
            Panel("performance review", KarenBrain.Instance != null && KarenBrain.Instance.Review != null && KarenBrain.Instance.Review.Visible);
            Panel("career over", Consequences.CareerOver);
            Panel("3D replay", ReplayPlayer.Instance != null);
            Panel("webcam blinking", Kehai.Blink.BlinkTracker.Consented);
            PlaytestScreens screens = GetComponent<PlaytestScreens>();
            string page = screens != null && screens.Showing ? screens.Page : "";
            if (page != shownPage)
            {
                if (shownPage.Length > 0) Note("panel", new Dictionary<string, object> { ["name"] = "playtest: " + shownPage, ["open"] = false });
                if (page.Length > 0) Note("panel", new Dictionary<string, object> { ["name"] = "playtest: " + page, ["open"] = true });
                shownPage = page;
            }
        }

        string shownPage = "";

        TaskListUI taskList;
        TaskListUI FindTaskList()
        {
            if (taskList == null) taskList = FindAnyObjectByType<TaskListUI>();
            return taskList;
        }

        void Panel(string name, bool open)
        {
            bool was = panels.TryGetValue(name, out bool v) && v;
            if (was == open) return;
            panels[name] = open;
            Note("panel", new Dictionary<string, object> { ["name"] = name, ["open"] = open });
        }

        void WatchKeys()
        {
            if (PlaytestScreens.Covering) return;   // typing a code or a note isn't playing
            foreach (KeyCode key in Watched)
            {
                if (!Input.GetKeyDown(key)) continue;
                var fields = new Dictionary<string, object> { ["key"] = key.ToString() };
                if (key == KeyCode.F7 && Input.GetKey(KeyCode.LeftShift)) fields["bug"] = true;
                if (key == KeyCode.E)
                {
                    PlayerInteract hands = FindAnyObjectByType<PlayerInteract>();
                    object target = hands != null ? hands.CurrentTarget : null;
                    fields["target"] = target is Component c && c != null ? c.name : target != null ? target.GetType().Name : "nothing";
                }
                Note("key", fields);
            }
        }

        void CountFrames()
        {
            float dt = Time.unscaledDeltaTime;
            fpsFrames++;
            fpsWorst = Mathf.Max(fpsWorst, dt);
            float now = Time.realtimeSinceStartup;
            if (fpsSince <= 0f) fpsSince = now;
            if (now - fpsSince < FpsSeconds) return;
            Note("fps", new Dictionary<string, object>
            {
                ["avg"] = fpsFrames / (now - fpsSince),
                ["worstMs"] = fpsWorst * 1000f,
            });
            fpsFrames = 0;
            fpsWorst = 0f;
            fpsSince = now;
        }

        // Where the tester is and where they look, tied to the 3D replay being written.
        void Sample()
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) return;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            Vector3 p = player.transform.position;
            var fields = new Dictionary<string, object>
            {
                ["p"] = new List<object> { Round(p.x), Round(p.y), Round(p.z) },
                ["yaw"] = Mathf.Round(player.transform.eulerAngles.y),
                ["pitch"] = motor != null ? Mathf.Round(motor.Pitch) : 0f,
                ["move"] = motor == null ? "" : motor.IsCrouching ? "crouch" : motor.PlanarSpeed > motor.sprintSpeed * 0.85f ? "sprint" : motor.IsMoving ? "walk" : "still",
                ["shift"] = shiftActive,
            };
            AddWhere(fields, ShiftRecorder.Instance, ReplayRecorder.Instance);
            Note("pos", fields);
        }

        static void AddWhere(Dictionary<string, object> fields, ShiftRecorder recorder, ReplayRecorder replay)
        {
            if (recorder != null && recorder.Recording) fields["shiftTime"] = Round(recorder.ShiftTime);
            if (replay != null && replay.Recording)
            {
                fields["rec"] = Path.GetFileName(replay.CurrentFile);
                fields["recTime"] = Round(replay.RecordingTime);
            }
        }

        static float Round(float v) => Mathf.Round(v * 100f) / 100f;

        static string Shorten(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
