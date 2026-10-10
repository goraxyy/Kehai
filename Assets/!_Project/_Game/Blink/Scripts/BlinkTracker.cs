using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Blink
{
    // Turns whichever blink source is live into one clean signal (IDEAS.md "Architecture"):
    //
    //   IBlinkSource → BlinkTracker (calibration, smoothing, confidence)
    //                       ├─→ Eyelids.Closed01            the player's own eyes, mirrored
    //                       └─→ OnBlinkStart / OnEyesClosedFor(t) → Karen
    //
    // Non-negotiables, all enforced here:
    //   • Blink is a modifier, never a requirement — with no camera the keyboard stands in,
    //     and with neither the game is simply played with the eyes open.
    //   • Opt-in, local-only, never recorded, and said plainly on screen before the camera
    //     path is ever touched.
    //   • Exploit the window, don't chase the latency: a blink lasts ~300 ms and is learnt of
    //     ~100 ms in, so the tracker predicts when the eyes will reopen and Karen acts inside
    //     what is left.
    public sealed class BlinkTracker : MonoBehaviour
    {
        public static BlinkTracker Instance { get; private set; }

        [Header("Sources")]
        public KeyCode keyboardKey = KeyCode.B;
        public int udpPort = 5066;
        [Tooltip("Blinks per minute for the synthetic source the simulated players use.")]
        public float syntheticRate = 17f;

        [Header("Keys")]
        public KeyCode consentKey = KeyCode.F8;
        public KeyCode calibrateKey = KeyCode.F9;

        [Header("Signal")]
        [Range(0f, 1f)] public float closeThreshold = 0.5f;
        [Range(0f, 1f)] public float openThreshold = 0.3f;
        [Tooltip("Mirror the player's real eyes onto the on-screen eyelids.")]
        public bool mirrorToEyelids = true;

        // ---- outputs ---------------------------------------------------------------------
        public float Closed01 { get; private set; }
        public bool EyesClosed { get; private set; }
        public float Confidence { get; private set; }
        public bool Live => source != null && source.IsLive;
        public string SourceName => source != null ? source.Name : "none";
        public float LatencyMs => source != null ? source.MeasuredLatencyMs : 0f;
        public float ExpectedBlinkSeconds { get; private set; } = 0.3f;
        public int Blinks { get; private set; }

        // For the F10 test panel.
        public bool WebcamListening => webcam != null;
        public bool WebcamLive => webcam != null && webcam.IsLive;
        public bool UsingWebcam => source != null && source == webcam;
        public int Packets => webcam != null ? webcam.Packets : 0;
        public float SidecarFps => webcam != null ? webcam.SidecarFps : 0f;
        public float EyeRatio => webcam != null ? webcam.Ratio : 0f;
        public float UsualEyeRatio => webcam != null ? webcam.OpenRatio : 0f;
        public float RawClosed { get; private set; }
        public bool Calibrated { get; private set; }
        // The consent question is up, or was answered this frame (its Esc isn't the menu's).
        public bool ConsentBusy => askingConsent || consentAnsweredFrame == Time.frameCount;
        public float OpenLevel => openLevel;
        public float ClosedLevel => closedLevel;
        public float LastBlinkSeconds { get; private set; }
        public float BlinksPerMinute
        {
            get
            {
                while (recentBlinks.Count > 0 && Time.unscaledTime - recentBlinks.Peek() > 60f) recentBlinks.Dequeue();
                return recentBlinks.Count;
            }
        }
        readonly Queue<float> recentBlinks = new Queue<float>();

        // Seconds until the eyes are expected to open again, from the shape of a blink and
        // how long ago this one really began (detection time minus measured latency).
        public float PredictedReopenIn => EyesClosed
            ? Mathf.Max(0f, ExpectedBlinkSeconds - (float)(BlinkClock.Now - blinkStartedAt))
            : 0f;

        public event System.Action<double> OnBlinkStart;
        public event System.Action<float> OnBlinkEnd;
        public event System.Action<float> OnEyesClosedFor;   // fires at 0.5 s, 1 s and 2 s

        IBlinkSource source;
        IBlinkSource keyboard;
        UdpBlinkSource webcam;
        double blinkStartedAt;
        readonly float[] closedForMarks = { 0.5f, 1f, 2f };
        int nextMark;
        Eyelids lids;

        // Calibration (IDEAS.md "Per-player calibration"), guided so nobody has to guess what
        // "blink normally" means: eyes open for a few seconds, then shut until a beep, then
        // three ordinary blinks to check the result. The player's own open and shut readings
        // normalise everything after, and are remembered for each camera helper.
        public enum CalibrationStep { None, Open, Shut, Blinks }
        public CalibrationStep Calibrating { get; private set; }
        public bool IsCalibrating => Calibrating != CalibrationStep.None;
        public float CalibrationLeft => IsCalibrating ? Mathf.Max(0f, stepEnds - Time.unscaledTime) : 0f;
        public string CalibrationText { get; private set; } = "";
        public string CalibrationResult { get; private set; } = "";

        const float OpenSeconds = 3f, ShutSeconds = 3.5f, ShutSettle = 1f, BlinkSeconds = 6f;
        float openLevel = 0f, closedLevel = 1f;
        float stepStarted, stepEnds;
        readonly List<float> openSamples = new List<float>(), shutSamples = new List<float>();
        int blinksAtCheck;
        float oldOpen, oldClosed;
        bool oldCalibrated;
        string loadedFor = "";
        AudioSource beeper;
        readonly Dictionary<float, AudioClip> tones = new Dictionary<float, AudioClip>();

        public const string ConsentKey = "karen.blink.consent";
        public static bool Consented => PlayerPrefs.GetInt(ConsentKey, 0) == 1;
        bool askingConsent;
        int consentAnsweredFrame = -1;

        void Awake()
        {
            Instance = this;
            keyboard = new KeyboardBlinkSource(keyboardKey);
            source = keyboard;
            lids = FindAnyObjectByType<Eyelids>();
            if (Consented) StartWebcam();
        }

        void OnDestroy()
        {
            webcam?.Dispose();
            BlinkSidecar.Stop();
            if (Instance == this) Instance = null;
        }

        // For tests and the simulated players.
        public void UseSource(IBlinkSource replacement)
        {
            source = replacement ?? keyboard;
        }

        public void UseSynthetic(int seed) => UseSource(ReplayBlinkSource.Synthetic(syntheticRate, seed));

        // Listen for the camera helper, and start it (it only ever sends to this computer).
        public void StartWebcam()
        {
            if (webcam == null)
            {
                try { webcam = new UdpBlinkSource(udpPort); }
                catch (System.Exception e) { Debug.LogWarning($"Blink: couldn't listen on udp {udpPort}: {e.Message}"); }
            }
            BlinkSidecar.Start(udpPort);
        }

        public void StopWebcam()
        {
            BlinkSidecar.Stop();
            webcam?.Dispose();
            webcam = null;
            source = keyboard;
        }

        void Update()
        {
            if (!GamePause.Paused) HandleKeys();

            // Prefer the camera whenever the sidecar is actually sending; fall back to the
            // keyboard the moment it stops. Never a requirement.
            if (webcam != null && webcam.IsLive && !(source is ReplayBlinkSource)) source = webcam;
            else if (source == webcam && (webcam == null || !webcam.IsLive)) source = keyboard;
            if (source == webcam && loadedFor != webcam.Src) LoadCalibration();

            bool got = source.TryRead(out BlinkSample s);
            // The keyboard still works with the camera on: holding B always shuts your eyes.
            BlinkSample k = default;
            bool keyShut = source != keyboard && keyboard.TryRead(out k) && k.Closed > 0.5f;
            if (keyShut) { s = k; got = true; }

            if (got)
            {
                if (!keyShut)
                {
                    RawClosed = s.Closed;
                    if (IsCalibrating && source == webcam) CalibrationSample(s.Closed);
                }
                float normalised = keyShut ? 1f : Mathf.InverseLerp(openLevel, closedLevel, s.Closed);

                // Light smoothing on the way down, none on the way up: a closing eye should
                // register the instant it's seen.
                Closed01 = normalised > Closed01 ? normalised : Mathf.Lerp(Closed01, normalised, 0.6f);
                Confidence = s.Confidence;
                Step(s);
            }

            AdvanceCalibration();

            if (mirrorToEyelids && lids != null && Live && Confidence > 0.3f && !(source is ReplayBlinkSource))
                lids.SetClosed(Closed01);

            PushToKaren();
        }

        void Step(BlinkSample s)
        {
            // Hysteresis: shut above one threshold, open again below a lower one.
            if (!EyesClosed && Closed01 >= closeThreshold)
            {
                EyesClosed = true;
                blinkStartedAt = s.Captured;
                nextMark = 0;
                Blinks++;
                recentBlinks.Enqueue(Time.unscaledTime);
                OnBlinkStart?.Invoke(blinkStartedAt);
                // Closing your eyes because the calibration asked you to isn't a chance for Karen.
                if (!IsCalibrating && !GamePause.Paused) KarenBrain.Instance?.OnBlinkStarted();
            }
            else if (EyesClosed && Closed01 <= openThreshold)
            {
                EyesClosed = false;
                float length = (float)(s.Captured - blinkStartedAt);
                LastBlinkSeconds = length;
                // Learn this player's blink length, for predicting the next reopening.
                if (length > 0.08f && length < 0.8f) ExpectedBlinkSeconds = Mathf.Lerp(ExpectedBlinkSeconds, length, 0.1f);
                OnBlinkEnd?.Invoke(length);
            }

            if (EyesClosed && nextMark < closedForMarks.Length)
            {
                float held = (float)(BlinkClock.Now - blinkStartedAt);
                if (held >= closedForMarks[nextMark])
                {
                    OnEyesClosedFor?.Invoke(closedForMarks[nextMark]);
                    nextMark++;
                }
            }
        }

        void PushToKaren()
        {
            KarenBrain brain = KarenBrain.Instance;
            if (brain == null) return;
            // The keyboard counts as a live channel — it's how the mechanic is played without a
            // camera — but only a real (or recorded) pair of eyes says anything about stress.
            brain.BlinkLive = Live;
            brain.BlinkPhysiological = Live && !(source is KeyboardBlinkSource);
            brain.EyesClosed = EyesClosed && !IsCalibrating;
            brain.PredictedReopenIn = PredictedReopenIn;
        }

        // ---- consent and calibration ----------------------------------------------------------

        void HandleKeys()
        {
            if (Input.GetKeyDown(consentKey))
            {
                if (Consented) RevokeConsent();
                else AskConsent();
            }

            if (askingConsent)
            {
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    askingConsent = false;
                    consentAnsweredFrame = Time.frameCount;
                    PlayerPrefs.SetInt(ConsentKey, 1);
                    StartWebcam();
                    KarenScreen.Ensure().Subtitle(BlinkSidecar.Running
                        ? "Webcam blink tracking on. Press F10 to watch it read your eyes, and F9 to calibrate."
                        : "Webcam blink tracking on, but the camera helper couldn't start — press F10 for what to do.", 6f);
                }
                else if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.Escape))
                {
                    askingConsent = false;
                    consentAnsweredFrame = Time.frameCount;
                }
            }

            if (Input.GetKeyDown(calibrateKey) && !IsCalibrating) BeginCalibration();
        }

        public void AskConsent() => askingConsent = true;

        public void RevokeConsent()
        {
            PlayerPrefs.SetInt(ConsentKey, 0);
            StopWebcam();
            KarenScreen.Ensure().Subtitle("Webcam blink tracking off. The camera helper has been stopped.", 3f);
        }

        public void BeginCalibration()
        {
            if (!WebcamLive)
            {
                CalibrationResult = "Calibration needs the webcam. Press F8 to turn it on, and wait for step 3 in the F10 panel to turn green.";
                KarenScreen.Ensure().Subtitle(CalibrationResult, 5f);
                return;
            }
            oldOpen = openLevel;
            oldClosed = closedLevel;
            oldCalibrated = Calibrated;
            openSamples.Clear();
            shutSamples.Clear();
            CalibrationResult = "";
            Enter(CalibrationStep.Open, OpenSeconds, "Keep your eyes open and look at the screen.");
        }

        void Enter(CalibrationStep step, float seconds, string text)
        {
            Calibrating = step;
            stepStarted = Time.unscaledTime;
            stepEnds = stepStarted + seconds;
            CalibrationText = text;
            if (step != CalibrationStep.None) KarenScreen.Ensure().Subtitle(text, seconds);
        }

        void CalibrationSample(float raw)
        {
            float into = Time.unscaledTime - stepStarted;
            // Skip the first moments of each step: that's you reading the instruction.
            if (Calibrating == CalibrationStep.Open && into > 0.5f) openSamples.Add(raw);
            else if (Calibrating == CalibrationStep.Shut && into > ShutSettle) shutSamples.Add(raw);
        }

        void AdvanceCalibration()
        {
            if (!IsCalibrating || Time.unscaledTime < stepEnds) return;
            switch (Calibrating)
            {
                case CalibrationStep.Open:
                    Enter(CalibrationStep.Shut, ShutSeconds, "Now close your eyes, and keep them closed until you hear the beep.");
                    break;
                case CalibrationStep.Shut:
                    Beep(880f);
                    if (!Fit()) return;
                    blinksAtCheck = Blinks;
                    Enter(CalibrationStep.Blinks, BlinkSeconds, "Open your eyes. Now blink 3 times, the way you usually do.");
                    break;
                case CalibrationStep.Blinks:
                    int seen = Blinks - blinksAtCheck;
                    CalibrationResult = seen >= 3 ? $"Calibrated. It caught all {seen} of your blinks."
                        : seen > 0 ? $"Calibrated, but it only caught {seen} of your 3 blinks. Blink a bit more fully, or press F9 to try again."
                        : "Calibrated, but it didn't catch your blinks. Try more light on your face, or press F9 to try again.";
                    Enter(CalibrationStep.None, 0f, "");
                    KarenScreen.Ensure().Subtitle(CalibrationResult, 5f);
                    break;
            }
        }

        // Open is the middle of the open readings, shut the middle of the shut ones.
        bool Fit()
        {
            if (openSamples.Count < 10 || shutSamples.Count < 10)
                return Fail("Calibration didn't get enough readings from the camera. Check the F10 panel shows a signal, then press F9 again.");
            float open = Median(openSamples), shut = Median(shutSamples);
            if (shut - open < 0.12f)
                return Fail($"Your eyes read almost the same open ({open:0.00}) as closed ({shut:0.00}). Face the camera with some light on your face, then press F9 again.");
            openLevel = open;
            closedLevel = shut;
            Calibrated = true;
            SaveCalibration();
            return true;
        }

        bool Fail(string why)
        {
            openLevel = oldOpen;
            closedLevel = oldClosed;
            Calibrated = oldCalibrated;
            CalibrationResult = why;
            Enter(CalibrationStep.None, 0f, "");
            KarenScreen.Ensure().Subtitle(why, 6f);
            return false;
        }

        static float Median(List<float> values)
        {
            values.Sort();
            return values[values.Count / 2];
        }

        // Remembered per helper: Apple Vision and MediaPipe read the same eyes differently.
        string CalibrationKey => "karen.blink.cal." + (webcam != null ? webcam.Src : "none");

        void SaveCalibration()
        {
            PlayerPrefs.SetFloat(CalibrationKey + ".open", openLevel);
            PlayerPrefs.SetFloat(CalibrationKey + ".closed", closedLevel);
            PlayerPrefs.Save();
        }

        void LoadCalibration()
        {
            loadedFor = webcam.Src;
            if (!PlayerPrefs.HasKey(CalibrationKey + ".open"))
            {
                openLevel = 0f;
                closedLevel = 1f;
                Calibrated = false;
                return;
            }
            openLevel = PlayerPrefs.GetFloat(CalibrationKey + ".open");
            closedLevel = PlayerPrefs.GetFloat(CalibrationKey + ".closed");
            Calibrated = true;
        }

        // A short tone, so you know when to open your eyes without looking.
        void Beep(float hz)
        {
            if (beeper == null)
            {
                beeper = gameObject.AddComponent<AudioSource>();
                beeper.spatialBlend = 0f;
                beeper.playOnAwake = false;
                beeper.ignoreListenerPause = true;
            }
            if (!tones.TryGetValue(hz, out AudioClip clip))
            {
                const int rate = 44100;
                int n = rate / 4;
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float envelope = Mathf.Min(1f, i / 400f) * Mathf.Min(1f, (n - i) / 3000f);
                    data[i] = Mathf.Sin(2f * Mathf.PI * hz * i / rate) * 0.5f * envelope;
                }
                clip = AudioClip.Create("Karen_CalibrationBeep", n, 1, rate, false);
                clip.SetData(data, 0);
                tones[hz] = clip;
            }
            beeper.PlayOneShot(clip, 0.7f);
        }

        void OnGUI()
        {
            if (!askingConsent) return;
            int size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 46f, 15f, 32f));
            var style = new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true, fontSize = size, alignment = TextAnchor.UpperLeft, padding = new RectOffset(20, 20, 16, 16) };
            float w = Mathf.Min(size * 42f, Screen.width - 40f);
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.2f, w, size * 19f);
            GUI.color = new Color(0f, 0f, 0f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(rect,
                "<b>Use your webcam to track blinks?</b>\n\n" +
                GameNames.Antagonist + " can react when your real eyes close. The camera is read by a small helper program on this " +
                "computer, which the game starts for you. It sends the game only one number — how closed your eyes are.\n\n" +
                "• <b>Local only.</b> Nothing leaves this machine.\n" +
                "• <b>Never recorded.</b> No frames are saved or stored.\n" +
                "• <b>Optional.</b> Everything works without it; you can switch it off with F8.\n" +
                "• macOS will ask once whether Unity may use the camera.\n\n" +
                "[Y] enable      [N] not now", style);
        }
    }
}
