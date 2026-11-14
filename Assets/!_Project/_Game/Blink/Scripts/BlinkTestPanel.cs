using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Blink
{
    // F10: see whether the game is actually reading your eyes.
    //
    // A checklist of the whole chain — consent, the camera helper, the signal arriving,
    // calibration — each ticked or with what to do next, then a big live meter of how
    // closed your eyes are, OPEN/CLOSED, a blink counter and the last ten seconds as a graph
    // with the two thresholds drawn on it. Blink and you should see a spike cross the line.
    public sealed class BlinkTestPanel : MonoBehaviour
    {
        public KeyCode key = KeyCode.F10;
        public bool visible;

        BlinkTracker tracker;
        GUIStyle body, heading, small;
        readonly List<(float t, float closed, bool shut)> history = new List<(float, float, bool)>();
        int blinksAtOpen;

        void Awake() => tracker = GetComponent<BlinkTracker>();

        public void Show()
        {
            visible = true;
            if (tracker != null) blinksAtOpen = tracker.Blinks;
        }

        void OnDisable() => FullScreenPanel.Set(this, false);

        void Update()
        {
            if (Input.GetKeyDown(key) && !GamePause.Paused)
            {
                visible = !visible;
                if (visible && tracker != null) blinksAtOpen = tracker.Blinks;
            }
            FullScreenPanel.Set(this, visible);
            if (!visible || tracker == null) return;
            history.Add((Time.unscaledTime, tracker.Closed01, tracker.EyesClosed));
            while (history.Count > 0 && Time.unscaledTime - history[0].t > 10f) history.RemoveAt(0);
            if (Input.GetKeyDown(KeyCode.Escape)) { visible = false; FullScreenPanel.Set(this, false); return; }
            if (Input.GetKeyDown(KeyCode.R)) { BlinkSidecar.Stop(); tracker.StartWebcam(); }
            if (!BlinkTracker.Consented) return;
            if (Input.GetKeyDown(KeyCode.V)) BlinkSidecar.NextCamera(tracker.udpPort);
            if (Input.GetKeyDown(KeyCode.M) && BlinkSidecar.VisionReady && BlinkSidecar.MediaPipeReady) BlinkSidecar.SwitchHelper(tracker.udpPort);
        }

        void OnGUI()
        {
            if (!visible || tracker == null) return;
            int size = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 44f, 15f, 36f));
            if (body == null)
            {
                body = UiTheme.Soften(new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
                heading = new GUIStyle(body) { fontStyle = FontStyle.Bold };
                small = new GUIStyle(body);
            }
            body.fontSize = size;
            heading.fontSize = Mathf.RoundToInt(size * 1.4f);
            small.fontSize = Mathf.RoundToInt(size * 0.8f);
            body.normal.textColor = heading.normal.textColor = new Color(0.94f, 0.94f, 0.92f);
            small.normal.textColor = new Color(0.7f, 0.72f, 0.76f);

            UiTheme.Fill(new Rect(0, 0, Screen.width, Screen.height), UiTheme.Ink);

            float m = size;
            var left = new Rect(m, m, Screen.width * 0.46f - m, Screen.height - m * 2f);
            GUILayout.BeginArea(left);
            GUILayout.Label("Blink test", heading);
            GUILayout.Label("Does the game see your eyes? Work down the list.", small);
            GUILayout.Space(size * 0.5f);

            bool consent = BlinkTracker.Consented;
            Step(consent, "1. Allow the webcam", consent ? "Allowed. (F8 turns it off again.)" : "Press <b>F8</b>, then <b>Y</b>.");

            bool helper = BlinkSidecar.Running;
            string helperText = helper ? $"Running: {BlinkSidecar.Which}."
                : !consent ? "Starts automatically once you allow the webcam."
                : string.IsNullOrEmpty(BlinkSidecar.Problem) ? "Not running. Press <b>R</b> to start it." : BlinkSidecar.Problem + "  Then press <b>R</b>.";
            Step(helper, "2. Camera helper", helperText);

            bool signal = tracker.WebcamLive;
            string signalText = signal ? $"Receiving your eyes: {tracker.SidecarFps:0} frames a second, {tracker.LatencyMs:0} ms behind."
                : helper ? "Waiting for the camera… If macOS asked about the camera, click Allow. Face the camera, with some light on your face."
                : "—";
            Step(signal, "3. Signal", signalText);

            bool calibrated = tracker.Calibrated;
            string calText = tracker.IsCalibrating ? "Follow the instructions on the right."
                : !string.IsNullOrEmpty(tracker.CalibrationResult) ? tracker.CalibrationResult + (calibrated ? "  <b>F9</b> redoes it." : "")
                : calibrated ? $"Done (open {tracker.OpenLevel:0.00}, closed {tracker.ClosedLevel:0.00}). Press <b>F9</b> to redo it."
                : signal ? "Press <b>F9</b>. It takes 12 seconds: eyes open for 3, closed until a beep, then 3 blinks." : "—";
            Step(calibrated, "4. Calibrate", calText);

            GUILayout.Space(size * 0.8f);
            GUILayout.Label("No webcam? Hold <b>B</b> to close your eyes with the keyboard — " + GameNames.Antagonist + " reacts the same way.", small);
            GUILayout.Label($"Reading from: <b>{tracker.SourceName}</b>", small);
            if (BlinkSidecar.Cameras.Count > 1)
                GUILayout.Label($"Camera: <b>{BlinkSidecar.CameraName}</b>  ({BlinkSidecar.Cameras.Count} found, <b>V</b> switches)", small);
            if (BlinkSidecar.VisionReady && BlinkSidecar.MediaPipeReady)
                GUILayout.Label($"Helper: <b>{(BlinkSidecar.PreferMediaPipe ? "MediaPipe" : "Apple Vision")}</b>  (<b>M</b> switches)", small);
            else if (BlinkSidecar.VisionReady && tracker.WebcamLive)
                GUILayout.Label("Not accurate enough? tools/blink/setup_mediapipe.sh adds the more accurate MediaPipe helper.", small);

            GUILayout.Space(size * 0.8f);
            GUILayout.Label("<b>Camera helper output</b>", small);
            IReadOnlyList<string> output = BlinkSidecar.Output;
            for (int i = Mathf.Max(0, output.Count - 8); i < output.Count; i++) GUILayout.Label(output[i], small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("F10 / Esc close · F8 webcam on/off · F9 calibrate · R restart the helper · V next camera · M switch helper · B keyboard blink", small);
            GUILayout.EndArea();

            // The live reading. While calibrating, the instruction comes first and big.
            var right = new Rect(Screen.width * 0.5f, m, Screen.width * 0.5f - m, Screen.height - m * 2f);
            if (tracker.IsCalibrating)
            {
                var big = new GUIStyle(heading) { fontSize = Mathf.RoundToInt(size * 1.8f), wordWrap = true };
                float h = big.CalcHeight(new GUIContent(tracker.CalibrationText), right.width) + size * 2.2f;
                Fill(new Rect(right.x - size * 0.5f, right.y - size * 0.3f, right.width + size, h), new Color(0.16f, 0.13f, 0.03f));
                GUI.Label(new Rect(right.x, right.y, right.width, h), $"<color=#FFD640>{tracker.CalibrationText}</color>", big);
                GUI.Label(new Rect(right.x, right.y + h - size * 1.9f, right.width, size * 1.6f),
                    $"Step {(int)tracker.Calibrating} of 3 · {Mathf.CeilToInt(tracker.CalibrationLeft)} s", body);
                right.yMin += h + size * 0.6f;
            }
            bool shut = tracker.EyesClosed;
            GUI.Label(new Rect(right.x, right.y, right.width, size * 3f),
                shut ? "<color=#FF5454><b>EYES CLOSED</b></color>" : "<color=#40C86E><b>EYES OPEN</b></color>",
                new GUIStyle(heading) { fontSize = size * 3 });

            var meter = new Rect(right.x, right.y + size * 3.6f, right.width, size * 1.6f);
            Fill(meter, new Color(0.18f, 0.2f, 0.24f));
            Fill(new Rect(meter.x, meter.y, meter.width * Mathf.Clamp01(tracker.Closed01), meter.height), shut ? new Color(1f, 0.33f, 0.33f) : new Color(0.3f, 0.82f, 1f));
            Fill(new Rect(meter.x + meter.width * tracker.closeThreshold - 1f, meter.y - 4f, 3f, meter.height + 8f), new Color(1f, 0.85f, 0.25f));
            Fill(new Rect(meter.x + meter.width * tracker.openThreshold - 1f, meter.y - 4f, 3f, meter.height + 8f), new Color(0.6f, 0.6f, 0.65f));
            string eye = tracker.UsingWebcam && tracker.UsualEyeRatio > 0f
                ? $"   ·   eye height {tracker.EyeRatio:0.000} (usually {tracker.UsualEyeRatio:0.000})" : "";
            GUI.Label(new Rect(meter.x, meter.yMax + 4f, meter.width, size * 1.4f),
                $"how closed: {tracker.Closed01:0.00}   (closes above the yellow line, opens below the grey one){eye}", small);

            int since = tracker.Blinks - blinksAtOpen;
            GUI.Label(new Rect(right.x, meter.yMax + size * 2f, right.width, size * 4f),
                $"Blinks since you opened this: <b>{since}</b>\nBlinks in the last minute: <b>{tracker.BlinksPerMinute:0}</b>   (most people: 12–20)\n" +
                $"Last blink: <b>{tracker.LastBlinkSeconds * 1000f:0} ms</b>   ·   the game expects ~{tracker.ExpectedBlinkSeconds * 1000f:0} ms",
                body);

            // Ten seconds of signal.
            var graph = new Rect(right.x, meter.yMax + size * 6.6f, right.width, Mathf.Max(size * 6f, right.yMax - (meter.yMax + size * 6.6f) - size * 2f));
            Fill(graph, new Color(0.1f, 0.11f, 0.14f));
            Fill(new Rect(graph.x, graph.yMax - graph.height * tracker.closeThreshold, graph.width, 2f), new Color(1f, 0.85f, 0.25f, 0.8f));
            Fill(new Rect(graph.x, graph.yMax - graph.height * tracker.openThreshold, graph.width, 1f), new Color(0.6f, 0.6f, 0.65f, 0.8f));
            float now = Time.unscaledTime;
            foreach (var (t, closed, isShut) in history)
            {
                float x = graph.x + graph.width * (1f - (now - t) / 10f);
                float h = Mathf.Max(2f, graph.height * Mathf.Clamp01(closed));
                Fill(new Rect(x, graph.yMax - h, 2f, h), isShut ? new Color(1f, 0.33f, 0.33f) : new Color(0.3f, 0.82f, 1f, 0.8f));
            }
            GUI.Label(new Rect(graph.x, graph.yMax + 4f, graph.width, size * 1.4f), "the last 10 seconds — each blink should be a red spike above the yellow line", small);
        }

        void Step(bool done, string title, string detail)
        {
            GUILayout.Label($"{(done ? "<color=#40C86E>✔</color>" : "<color=#F2C94C>●</color>")} <b>{title}</b>", body);
            GUILayout.Label(detail, small);
            GUILayout.Space(body.fontSize * 0.4f);
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
