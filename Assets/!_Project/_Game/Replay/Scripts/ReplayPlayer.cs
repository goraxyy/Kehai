using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kehai.Replay
{
    // Watches a recorded shift in 3D: the store rebuilt (ReplayStage), a camera to look at it
    // with (ReplayCameras), Karen's mind drawn in (MindLayers), the shift's sounds where they
    // happened (ReplaySound), and a timeline to move through it with the clip moments and
    // markers on it. Opened by ReplayMode; renders a shot instead when ReplayRender asks.
    public sealed class ReplayPlayer : MonoBehaviour
    {
        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };
        public const float FrameStep = 1f / 30f;
        public const float JumpSeconds = 5f;
        public const float AudibleUpTo = 2f;   // faster than this, the sounds would be a smear

        public struct Marker
        {
            public string Id;
            public float T, End;
            public int Weight;
        }

        public struct Moment
        {
            public int Rank;
            public float Start, End, Score;
        }

        public static ReplayPlayer Instance { get; private set; }

        public string File { get; private set; }
        public ReplayData Data { get; private set; }
        public ReplayStage Stage { get; private set; }
        public ReplayCameras Cameras { get; private set; }
        public MindLayers Mind { get; private set; }
        public ShotPath Path { get; private set; }
        public readonly List<Marker> Markers = new List<Marker>();
        public readonly List<Moment> Moments = new List<Moment>();
        public float T { get; private set; }
        public bool Playing { get; set; }
        public int SpeedIndex { get; private set; } = 3;
        public float Speed => Speeds[SpeedIndex];
        public bool HudVisible { get; set; } = true;
        public bool Ready { get; private set; }
        public string Error { get; private set; }

        ReplayRender render;
        bool help, scrubbing;
        string toast;
        float toastUntil;
        GUIStyle label, small, big;

        public static ReplayPlayer Install(string krec)
        {
            var go = new GameObject("Replay");
            var player = go.AddComponent<ReplayPlayer>();
            player.File = krec;
            return player;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Stage?.Dispose();
        }

        void Start()
        {
            try
            {
                Data = KrecReader.Load(File);
                Stage = new ReplayStage(Data, transform);
                Stage.Build();
                Cameras = new ReplayCameras(Stage);
                Mind = new MindLayers(Stage, transform);
                ReadMarkers();
                string pathFile = ShotPath.PathFor(File);
                Path = System.IO.File.Exists(pathFile) ? ShotPath.Load(pathFile) : new ShotPath { Krec = System.IO.Path.GetFileName(File) };
                Cameras.Path = Path;
                T = Stage.Start;
                Debug.Log($"Replay: {File} — shift {Data.Header.Shift}, {Data.End - Stage.Start:0} s, {Markers.Count} markers, {Moments.Count} moments. {Stage.Summary}");
            }
            catch (System.Exception e)
            {
                Error = e.Message;
                Debug.LogError($"Couldn't open the replay {File}: {e}");
                if (ReplayMode.Rendering) ReplayRender.Quit(2);
                return;
            }

            if (ReplayMode.Rendering)
            {
                render = ReplayRender.FromCommandLine(this);
                if (render == null) return;   // it has said why and quits
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Playing = true;
            }
            Ready = true;
            ApplyAt(T, 0f, false);
        }

        void ReadMarkers()
        {
            string path = File.EndsWith(".krec") ? File.Substring(0, File.Length - 5) + ".markers.json" : File + ".markers.json";
            if (!System.IO.File.Exists(path)) return;
            try
            {
                Dictionary<string, object> o = MiniJson.ParseObject(System.IO.File.ReadAllText(path));
                if (o != null && o.TryGetValue("markers", out object ms) && ms is List<object> markers)
                    foreach (object m in markers)
                        if (m is Dictionary<string, object> d)
                            Markers.Add(new Marker { Id = d.GetString("id", ""), T = (float)d.GetNumber("t"), End = (float)d.GetNumber("end"), Weight = (int)d.GetNumber("weight") });
                if (o != null && o.TryGetValue("moments", out object mo) && mo is List<object> moments)
                    foreach (object m in moments)
                        if (m is Dictionary<string, object> d)
                            Moments.Add(new Moment { Rank = (int)d.GetNumber("rank"), Start = (float)d.GetNumber("start"), End = (float)d.GetNumber("end"), Score = (float)d.GetNumber("score") });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Couldn't read the clip markers {path}: {e.Message}");
            }
        }

        // Everything as it was at t: the store, the camera, her mind.
        public void ApplyAt(float t, float dt, bool input)
        {
            T = Mathf.Clamp(t, Stage.Start, Stage.End);
            Stage.Apply(T, dt);
            Cameras.Apply(T, dt, input);
            int mind = 1 << ReplayLook.MindLayer;
            Stage.Camera.cullingMask = Mind.Shown != MindLayer.None ? Stage.Camera.cullingMask | mind : Stage.Camera.cullingMask & ~mind;
            Mind.Apply(T, Stage.Camera);
        }

        public void Seek(float t) => ApplyAt(t, 0f, false);

        void Update()
        {
            if (!Ready) return;
            if (render != null)
            {
                render.Step();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (!GamePause.Paused)
            {
                if (Cursor.lockState != CursorLockMode.None && !Input.GetMouseButton(1)) Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Keys();
            }

            float from = T;
            float to = T;
            if (Playing && !scrubbing)
            {
                to = T + dt * Speed;
                if (to >= Stage.End)
                {
                    to = Stage.End;
                    Playing = false;
                }
            }
            ApplyAt(to, dt, !GamePause.Paused);
            if (to > from && Speed <= AudibleUpTo && !scrubbing) ReplaySound.Play(Data, from, to);
        }

        void Keys()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift);
            if (Input.GetKeyDown(KeyCode.Space)) Playing = !Playing;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Seek(T - JumpSeconds);
            if (Input.GetKeyDown(KeyCode.RightArrow)) Seek(T + JumpSeconds);
            if (Input.GetKeyDown(KeyCode.Comma)) { Playing = false; Seek(T - FrameStep); }
            if (Input.GetKeyDown(KeyCode.Period)) { Playing = false; Seek(T + FrameStep); }
            if (Input.GetKeyDown(KeyCode.Minus)) SpeedIndex = Mathf.Max(0, SpeedIndex - 1);
            if (Input.GetKeyDown(KeyCode.Equals)) SpeedIndex = Mathf.Min(Speeds.Length - 1, SpeedIndex + 1);
            if (Input.GetKeyDown(KeyCode.Home)) Seek(Stage.Start);
            if (Input.GetKeyDown(KeyCode.End)) Seek(Stage.End);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) JumpToMoment(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) JumpToMoment(+1);

            for (int i = 0; i < 7; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Cameras.Use((ShotPreset)i);
            if (Input.GetKeyDown(KeyCode.Tab)) Cameras.CycleSubject();
            if (Input.GetKeyDown(KeyCode.F)) Cameras.DepthOfFieldOn = !Cameras.DepthOfFieldOn;
            if (Input.GetKeyDown(KeyCode.K)) Keyframe(!shift);

            if (Input.GetKeyDown(KeyCode.M)) Mind.Shown = Mind.Shown == MindLayer.None ? MindLayer.All & ~MindLayer.Actors : MindLayer.None;
            if (Input.GetKeyDown(KeyCode.B)) Mind.Shown ^= MindLayer.Belief;
            if (Input.GetKeyDown(KeyCode.G)) Mind.Shown ^= MindLayer.Guess;
            if (Input.GetKeyDown(KeyCode.V)) Mind.Shown ^= MindLayer.Cone;
            if (Input.GetKeyDown(KeyCode.N)) Mind.Shown ^= MindLayer.Sound;
            if (Input.GetKeyDown(KeyCode.T)) Mind.Shown ^= MindLayer.Thoughts;

            if (Input.GetKeyDown(KeyCode.H)) HudVisible = !HudVisible;
            if (Input.GetKeyDown(KeyCode.F1)) help = !help;
            if (Input.GetKeyDown(KeyCode.Backspace)) ReplayMode.Leave();
        }

        void JumpToMoment(int direction)
        {
            var starts = new List<float>();
            foreach (Moment m in Moments) starts.Add(m.Start);
            starts.Sort();
            float target = float.NaN;
            if (direction > 0) { foreach (float s in starts) if (s > T + 0.5f) { target = s; break; } }
            else for (int i = starts.Count - 1; i >= 0; i--) if (starts[i] < T - 0.5f) { target = starts[i]; break; }
            if (!float.IsNaN(target)) Seek(target);
        }

        // K adds the camera as it is now to the path (and saves it); Shift+K takes the last one off.
        void Keyframe(bool add)
        {
            if (add) Path.Add(Cameras.KeyNow(T));
            else if (!Path.RemoveLast()) return;
            try
            {
                string file = ShotPath.PathFor(File);
                Path.Save(file);
                Toast($"{(add ? "Keyframe added" : "Last keyframe removed")}: {Path.Keys.Count} in {System.IO.Path.GetFileName(file)} (7 plays it)");
            }
            catch (System.Exception e)
            {
                Toast("Couldn't save the path: " + e.Message);
            }
        }

        void Toast(string text)
        {
            toast = text;
            toastUntil = Time.unscaledTime + 3f;
        }

        // ---- the HUD -----------------------------------------------------------------------

        void OnGUI()
        {
            if (render != null) return;
            Styles();
            if (!Ready)
            {
                GUI.Label(new Rect(20, 20, Screen.width - 40, 60), Error != null ? "Couldn't open the replay: " + Error + "\n[Backspace] back to the store" : "Opening the replay…", big);
                if (Error != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Backspace) ReplayMode.Leave();
                return;
            }
            if (!HudVisible) return;

            float w = Screen.width, h = Screen.height;
            var bar = new Rect(20, h - 92, w - 40, 76);
            Fill(bar, new Color(0.06f, 0.07f, 0.09f, 0.82f));

            // Line 1: what's playing.
            string layers = Mind.Shown == MindLayer.None ? "off" : MindLayers.Describe(Mind.Shown);
            string subject = Cameras.Preset == ShotPreset.Pov || Cameras.Preset == ShotPreset.Free || Cameras.Preset == ShotPreset.Path ? "" :
                " · " + (Cameras.Subject == KrecKind.Karen ? GameNames.Antagonist : "you");
            GUI.Label(new Rect(bar.x + 12, bar.y + 6, bar.width - 24, 22),
                $"{(Playing ? "Playing" : "Paused")}  {Clock(T)} / {Clock(Stage.End)}   {Speed:0.##}×   camera: {Name(Cameras.Preset)}{subject}{(Cameras.DepthOfFieldOn ? " · focus" : "")}   her mind: {layers}", label);
            GUI.Label(new Rect(bar.x + 12, bar.y + 6, bar.width - 24, 22), "F1 keys · H hide · Backspace leave", RightAligned());

            // Line 2: the timeline, with the moments (gold) and the markers (ticks; a bug in red).
            var line = new Rect(bar.x + 12, bar.y + 36, bar.width - 24, 26);
            Fill(line, new Color(1f, 1f, 1f, 0.06f));
            float span = Mathf.Max(1f, Stage.End - Stage.Start);
            float X(float t) => line.x + Mathf.Clamp01((t - Stage.Start) / span) * line.width;
            foreach (Moment m in Moments)
                Fill(new Rect(X(m.Start), line.y, Mathf.Max(2f, X(m.End) - X(m.Start)), line.height), new Color(1f, 0.84f, 0.25f, 0.22f));
            foreach (Marker m in Markers)
            {
                float tall = Mathf.Lerp(6f, line.height, Mathf.Clamp01(m.Weight / 10f));
                Color c = m.Id == "manual_bug" ? ReplayLook.Karen : m.Id == "manual_good" ? ReplayLook.You : new Color(1f, 0.84f, 0.25f, 0.9f);
                Fill(new Rect(X(m.T) - 1f, line.yMax - tall, 2f, tall), c);
            }
            Fill(new Rect(X(T) - 1.5f, line.y - 4f, 3f, line.height + 8f), Color.white);
            Scrub(line, span);

            // Top left: the shift; the power, if it's off.
            string power = (Stage.CircuitBits & 1) == 0 ? "   <color=#ff5454>MAINS OFF</color>" : (Stage.CircuitBits & 14) != 14 ? "   <color=#f28c32>breaker tripped</color>" : "";
            GUI.Label(new Rect(20, 16, w - 40, 26), $"<b>{GameNames.Game} replay</b>  ·  shift {Data.Header.Shift}  ·  {Data.Header.Started}  ·  rung {Data.Header.Rung}{power}", label);

            // Under the picture: the narrator, and the PA.
            string said = Latest(KrecEvent.Story, 5f) ?? Latest(KrecEvent.PaSpeech, 6f);
            if (said != null) GUI.Label(new Rect(w * 0.15f, bar.y - 64, w * 0.7f, 56), said, Centered());

            if (toast != null && Time.unscaledTime < toastUntil) GUI.Label(new Rect(20, 46, w - 40, 24), toast, label);
            if (help) Help();
        }

        void Scrub(Rect line, float span)
        {
            Event e = Event.current;
            Rect hit = new Rect(line.x, line.y - 8f, line.width, line.height + 16f);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition)) scrubbing = true;
            if (scrubbing && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                Seek(Stage.Start + Mathf.Clamp01((e.mousePosition.x - line.x) / line.width) * span);
                e.Use();
            }
            if (scrubbing && e.rawType == EventType.MouseUp) scrubbing = false;
        }

        string Latest(KrecEvent type, float within)
        {
            for (int i = Data.FirstEventAt(T + 1e-4f) - 1; i >= 0; i--)
            {
                ReplayEvent e = Data.Events[i];
                if (T - e.T > within) return null;
                if (e.Type == type) return type == KrecEvent.PaSpeech ? "<color=#ffd640>PA:</color> " + e.Text : e.Text;
            }
            return null;
        }

        void Help()
        {
            var rect = new Rect(20, 50, 520, 470);
            Fill(rect, new Color(0.06f, 0.07f, 0.09f, 0.92f));
            var sb = new System.Text.StringBuilder("<b>3D replay</b>\n");
            foreach (Controls.Section s in Controls.All)
                if (s.Title == Controls.ReplaySection)
                    foreach (Controls.Entry k in s.Entries) sb.Append("<color=#ffd640>").Append(k.Keys).Append("</color>  ").Append(k.Action).Append('\n');
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, rect.width - 28, rect.height - 20), sb.ToString(), small);
        }

        static string Name(ShotPreset p)
        {
            switch (p)
            {
                case ShotPreset.Pov: return "your eyes";
                case ShotPreset.Cctv: return "CCTV";
                case ShotPreset.TopDown: return "top down";
                case ShotPreset.Free: return "free";
                case ShotPreset.Path: return "your path";
                default: return p.ToString().ToLowerInvariant();
            }
        }

        public static string Clock(float t)
        {
            if (t < 0f) t = 0f;
            int m = (int)(t / 60f);
            return $"{m}:{t - m * 60f:00.0}";
        }

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
            label.normal.textColor = new Color(0.91f, 0.91f, 0.93f);
            small = new GUIStyle(label) { fontSize = 13, wordWrap = true };
            big = new GUIStyle(label) { fontSize = 20, wordWrap = true };
        }

        GUIStyle RightAligned() => new GUIStyle(label) { alignment = TextAnchor.UpperRight, fontSize = 12 };

        GUIStyle Centered()
        {
            var s = new GUIStyle(label) { alignment = TextAnchor.LowerCenter, wordWrap = true, fontSize = 17 };
            return s;
        }

        static void Fill(Rect r, Color c)
        {
            Color was = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = was;
        }
    }
}
