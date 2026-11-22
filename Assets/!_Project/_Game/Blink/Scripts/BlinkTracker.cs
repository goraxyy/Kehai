using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Blink
{
    // Turns whichever blink source is live into one clean signal (IDEAS.md "Architecture"):
    //
    //   IBlinkSource → BlinkTracker (calibration, smoothing, confidence)
    //                       ├─→ Eyelids.SetShut(EyesClosed)  the player's own eyes: open or shut (0.4.1)
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

        [Header("Signal")]
        [Range(0f, 1f)] public float closeThreshold = 0.5f;
        [Range(0f, 1f)] public float openThreshold = 0.3f;
        [Tooltip("Show the player's real eyes on the on-screen eyelids: shut while EyesClosed, open otherwise (two states, 0.4.1).")]
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

        // For the F8 webcam panel.
        public bool WebcamListening => webcam != null;
        public bool WebcamLive => webcam != null && webcam.IsLive;
        public bool UsingWebcam => source != null && source == webcam;
        public int Packets => webcam != null ? webcam.Packets : 0;
        public float SidecarFps => webcam != null ? webcam.SidecarFps : 0f;
        public float EyeRatio => webcam != null ? webcam.Ratio : 0f;
        public float UsualEyeRatio => webcam != null ? webcam.OpenRatio : 0f;
        public float RawClosed { get; private set; }
        public bool Calibrated { get; private set; }
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
        double blinkStartedAt, readingOpenedAt;
        bool readingOpened;
        int blinksLearnt;
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
            if (!Consented || !Edition.ShowUI || Application.isBatchMode) return;
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
                Step(s, normalised);
            }

            AdvanceCalibration();

            // The lids are open or shut, as EyesClosed is (0.4.1): the yes-or-no Karen acts on, not
            // the analog reading, which drew half-shut lids for a squint and left a slit when
            // the eyes were fully shut.
            if (mirrorToEyelids && lids != null && Live && !(source is ReplayBlinkSource))
                lids.SetShut(EyesClosed && Confidence > 0.3f);

            PushToKaren();
        }

        void Step(BlinkSample s, float normalised)
        {
            // Hysteresis: shut above one threshold, open again below a lower one.
            if (!EyesClosed && Closed01 >= closeThreshold)
            {
                EyesClosed = true;
                blinkStartedAt = s.Captured;
                readingOpened = false;
                nextMark = 0;
                Blinks++;
                recentBlinks.Enqueue(Time.unscaledTime);
                OnBlinkStart?.Invoke(blinkStartedAt);
                // Closing your eyes because the calibration asked you to isn't a chance for Karen.
                if (!IsCalibrating && !GamePause.Paused) KarenBrain.Instance?.OnBlinkStarted();
            }
            else if (EyesClosed)
            {
                // When the eyes really opened: the last time the reading itself fell back under
                // the level that started the blink. The hysteresis only confirms it a frame or two
                // later (the smoothing on the way up, then the lower threshold), while the start
                // is seen on its own frame.
                if (normalised >= closeThreshold) readingOpened = false;
                else if (!readingOpened) { readingOpened = true; readingOpenedAt = s.Captured; }

                if (Closed01 <= openThreshold)
                {
                    EyesClosed = false;
                    float detected = (float)(s.Captured - blinkStartedAt);
                    float length = readingOpened ? (float)(readingOpenedAt - blinkStartedAt) : detected;
                    LastBlinkSeconds = length;
                    // Learn this player's blink length, for predicting the next reopening. Learning
                    // the detected length made PredictedReopenIn late by those frames; which blinks
                    // are learnt from is still judged on it. The 0.3 s guess counts as one blink:
                    // the average of the blinks so far until there are ten, then a moving average,
                    // so a guess longer than this player's blinks is gone in a few, not a minute.
                    if (detected > 0.08f && detected < 0.8f)
                    {
                        blinksLearnt++;
                        ExpectedBlinkSeconds = Mathf.Lerp(ExpectedBlinkSeconds, length, Mathf.Max(0.1f, 1f / (blinksLearnt + 1)));
                    }
                    OnBlinkEnd?.Invoke(length);
                }
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

        internal void SetWebcamConsent(bool allowed)
        {
            PlayerPrefs.SetInt(ConsentKey, allowed ? 1 : 0);
            PlayerPrefs.Save();
            if (allowed) StartWebcam();
            else
            {
                CancelCalibration();
                StopWebcam();
            }
        }

        internal void CancelCalibration()
        {
            if (!IsCalibrating) return;
            CalibrationResult = Calibrating == CalibrationStep.Blinks ? "Calibration saved. Blink check stopped." : "Calibration cancelled.";
            Enter(CalibrationStep.None, 0f, "");
        }

        public void BeginCalibration()
        {
            if (IsCalibrating) return;
            if (!WebcamLive)
            {
                CalibrationResult = "Wait for the F8 webcam panel to show a signal before calibrating.";
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
                        : seen > 0 ? $"Calibrated, but it only caught {seen} of your 3 blinks. Blink a bit more fully and try again."
                        : "Calibrated, but it didn't catch your blinks. Try more light on your face and calibrate again.";
                    Enter(CalibrationStep.None, 0f, "");
                    break;
            }
        }

        // Open is the middle of the open readings, shut the middle of the shut ones.
        bool Fit()
        {
            if (openSamples.Count < 10 || shutSamples.Count < 10)
                return Fail("Calibration didn't get enough readings. Wait for the camera signal, then try again.");
            float open = Median(openSamples), shut = Median(shutSamples);
            if (shut - open < 0.12f)
                return Fail($"Your eyes read almost the same open ({open:0.00}) as closed ({shut:0.00}). Face the camera with some light on your face and try again.");
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
    }
}
