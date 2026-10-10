using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // The store map, and what Karen is up to, in plain words.
    //
    //   F1  the live map: the real floor plan with you, Karen (and where she's looking), her
    //       guess of where you are, every customer and what they're doing, the sounds being
    //       made, her warnings, and the jobs. Beside it, what she's doing and a running story
    //       of the shift. H adds her guess as a heat map; T swaps the story for the technical
    //       numbers.
    //   F2  the replay of this shift (or the last one): drag the timeline, Space to play,
    //       1/2/3 for speed, ←/→ to skip, O to open the full report in your browser.
    public sealed class KarenDebugOverlay : MonoBehaviour
    {
        public KeyCode overlayKey = KeyCode.F1;
        public KeyCode replayKey = KeyCode.F2;
        public KeyCode heatKey = KeyCode.H;
        public KeyCode technicalKey = KeyCode.T;
        public bool showOverlay;
        public bool showScrubber;
        public bool showHeat;
        public bool showTechnical;

        KarenBrain brain;
        MapPainter painter;
        GUIStyle body, heading, small;

        float replayT;
        bool playing;
        float speed = 4f;

        void Awake() => brain = GetComponent<KarenBrain>();

        void OnDisable() => FullScreenPanel.Set(this, false);

        void Update()
        {
            if (GamePause.Paused) return;   // the Esc menu is open
            if (Input.GetKeyDown(KeyCode.Escape) && (showOverlay || showScrubber))
            {
                showOverlay = false;
                SetReplay(false);
            }
            if (Input.GetKeyDown(overlayKey))
            {
                showOverlay = !showOverlay;
                if (showOverlay) SetReplay(false);
                AudioMeter.Ensure();
            }
            if (Input.GetKeyDown(replayKey)) SetReplay(!showScrubber);
            FullScreenPanel.Set(this, showOverlay || showScrubber);
            if (showOverlay && Input.GetKeyDown(heatKey)) showHeat = !showHeat;
            if (showOverlay && Input.GetKeyDown(technicalKey)) showTechnical = !showTechnical;

            if (showScrubber)
            {
                ShiftRecording r = Recording;
                if (r != null && r.Frames.Count > 0)
                {
                    float end = r.Frames[r.Frames.Count - 1].T;
                    if (Input.GetKeyDown(KeyCode.Space)) playing = !playing;
                    if (Input.GetKeyDown(KeyCode.Alpha1)) speed = 1f;
                    if (Input.GetKeyDown(KeyCode.Alpha2)) speed = 4f;
                    if (Input.GetKeyDown(KeyCode.Alpha3)) speed = 16f;
                    if (Input.GetKeyDown(KeyCode.RightArrow)) replayT = Mathf.Min(end, replayT + 5f);
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) replayT = Mathf.Max(0f, replayT - 5f);
                    if (playing) replayT = Mathf.Min(end, replayT + Time.unscaledDeltaTime * speed);
                    if (replayT >= end) playing = false;
                }
                if (Input.GetKeyDown(KeyCode.O)) ShiftRecorder.Instance?.OpenReport();
            }
        }

        void SetReplay(bool on)
        {
            showScrubber = on;
            if (on)
            {
                showOverlay = false;
                ShiftRecording r = Recording;
                replayT = 0f;
                playing = r != null && r.Frames.Count > 0;
            }
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
        }

        static ShiftRecording Recording => ShiftRecorder.Instance != null ? ShiftRecorder.Instance.Current ?? ShiftRecorder.Instance.Last : null;

        // A finished shift has its moments; one in progress gets them worked out again when a
        // new marker arrives, and once a second as the shift grows (a moment near the end is
        // cut off at the shift's length so far).
        ShiftRecording momentsOf;
        int momentsMarkers = -1, momentsFrames = -1;
        List<ClipMoment> moments = new List<ClipMoment>();

        List<ClipMoment> MomentsOf(ShiftRecording r)
        {
            if (r.Moments != null) return r.Moments;
            if (r != momentsOf || r.Markers.Count != momentsMarkers || r.Frames.Count - momentsFrames >= 5)
            {
                momentsOf = r;
                momentsMarkers = r.Markers.Count;
                momentsFrames = r.Frames.Count;
                moments = ClipMoments.Build(r);
            }
            return moments;
        }

        int FontSize => Mathf.RoundToInt(Mathf.Clamp(Screen.height / 42f, 15f, 42f));

        void Styles()
        {
            int size = FontSize;
            if (body == null)
            {
                body = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                heading = new GUIStyle(body) { fontStyle = FontStyle.Bold };
                small = new GUIStyle(body);
            }
            body.fontSize = size;
            heading.fontSize = Mathf.RoundToInt(size * 1.3f);
            small.fontSize = Mathf.RoundToInt(size * 0.8f);
            body.normal.textColor = heading.normal.textColor = new Color(0.94f, 0.94f, 0.92f);
            small.normal.textColor = new Color(0.75f, 0.77f, 0.8f);
        }

        void OnGUI()
        {
            if (!showOverlay && !showScrubber) return;
            if (brain == null || brain.Map == null) return;
            if (painter == null) painter = new MapPainter(StoreFloorPlan.Current);
            Styles();

            GUI.color = new Color(0.04f, 0.045f, 0.06f, 0.94f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (showScrubber) DrawReplay();
            else DrawLive();
        }

        // ---- F1 -----------------------------------------------------------------------------------

        void DrawLive()
        {
            int size = FontSize;
            float margin = size;
            float mapWidth = Screen.width * 0.58f;
            float legendH = MapPainter.LegendHeight(mapWidth, size);
            painter.Layout(new Rect(margin, margin, mapWidth - margin, Screen.height - legendH - margin * 3f));
            ShiftRecorder rec = ShiftRecorder.Instance;
            ShiftFrame live = rec != null ? rec.Live : null;
            if (live == null) return;

            painter.Draw(live, rec.Recent, Time.time, true, Mathf.RoundToInt(size * 0.85f));
            if (showHeat && brain.Belief != null) painter.DrawBelief(brain.Belief);
            MapPainter.Legend(new Rect(margin, painter.Area.yMax + margin, mapWidth - margin, legendH), size);

            var panel = new Rect(mapWidth + margin, margin, Screen.width - mapWidth - margin * 2f, Screen.height - margin * 2f);
            GUILayout.BeginArea(panel);
            if (showTechnical) Technical();
            else Story(live);
            GUILayout.EndArea();
        }

        void Story(ShiftFrame f)
        {
            GUILayout.Label(brain.StatusLine, heading);

            string guess;
            if (f.KarenSees) guess = "<color=#FF5454><b>She can see you right now!</b></color>";
            else if (!brain.ShiftActive) guess = "Clock in at the time clock in the Staff room to start the shift.";
            else if (f.GuessConfidence < 0.12f) guess = "She has no idea where you are.";
            else guess = $"She thinks you're <b>{KarenNarrator.In(new Vector3(f.Guess.x, 0f, f.Guess.y))}</b> — {KarenNarrator.Sureness(f.GuessConfidence)}.";
            GUILayout.Label(guess, body);

            if (brain.ShiftActive && brain.Director != null)
            {
                float d = Vector2.Distance(f.Player, f.Karen);
                GUILayout.Label($"Pace of the shift: <b>{KarenNarrator.PhaseShort(brain.Director.CurrentPhase)}</b>.  She is <b>{d:0} m</b> from you.", body);
            }

            // Your side.
            var you = new StringBuilder();
            you.Append($"Your energy <b>{f.Energy:P0}</b>");
            TaskManager tasks = brain.Tasks;
            if (tasks != null && tasks.HasTasks)
            {
                var left = new List<string>();
                if (tasks.SpillsOutstanding > 0) left.Add($"{tasks.SpillsOutstanding} spill{(tasks.SpillsOutstanding == 1 ? "" : "s")}");
                if (ShelfUnit.NotFullCount > 0) left.Add($"{ShelfUnit.NotFullCount} shel{(ShelfUnit.NotFullCount == 1 ? "f" : "ves")}");
                if (tasks.TrashOutstanding > 0 || TrashBag.ActiveCount > 0) left.Add("rubbish");
                if (tasks.CustomersWaiting > 0) left.Add($"{tasks.CustomersWaiting} at the till");
                if (tasks.CustomersAsking > 0) left.Add($"{tasks.CustomersAsking} asking for directions");
                you.Append(left.Count > 0 ? " · still to do: " + string.Join(", ", left)
                    : brain.Shift != null && brain.Shift.CustomersAllowed ? " · <color=#40C86E>nothing to do right now — customers are still coming</color>"
                    : " · <color=#40C86E>all jobs done — go and clock out</color>");
            }
            GUILayout.Label(you.ToString(), body);

            AudioMeter meter = AudioMeter.Instance;
            if (meter != null)
            {
                int bars = Mathf.Clamp(Mathf.RoundToInt(meter.Level * 12f), 0, 10);
                string meterText = $"Game sound: <color=#4DD2FF>{new string('|', bars)}</color><color=#555A66>{new string('|', 10 - bars)}</color>";
                if (Time.unscaledTime - meter.LastSoundAt > 8f) meterText += "  <color=#F2C94C>(nothing playing — try the radio (E) or walk around)</color>";
                GUILayout.Label(meterText, small);
            }

            GUILayout.Space(FontSize * 0.6f);
            GUILayout.Label("What just happened", heading);
            int shown = 0;
            IReadOnlyList<StoryLine> story = KarenNarrator.Recent;
            ShiftRecorder rec = ShiftRecorder.Instance;
            var lines = new List<(float t, string text, Color c)>();
            for (int i = story.Count - 1; i >= 0 && lines.Count < 14; i--)
                lines.Add((story[i].Time, story[i].Text, StoryColour(story[i].Kind)));
            if (rec != null)
                foreach (ShiftEvent e in rec.Recent)
                    if (e.Kind == "job" || e.Kind == "customer" || e.Kind == "store")
                        lines.Add((e.WallTime, e.Text, e.Kind == "job" ? MapPainter.Following : e.Kind == "customer" ? MapPainter.Asking : MapPainter.SoundStore));
            foreach (var line in lines.OrderByDescending(l => l.t).Take(14))
            {
                float ago = Time.time - line.t;
                string when = ago < 2f ? "now" : ago < 60f ? $"{ago:0}s ago" : $"{ago / 60f:0}m ago";
                GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(line.c)}>●</color> <color=#8A909C>{when,-8}</color> {line.text}", body);
                shown++;
            }
            if (shown == 0) GUILayout.Label("Nothing yet.", small);

            GUILayout.FlexibleSpace();
            GUILayout.Label("F1 close · F2 replay the shift · H her guess as a heat map · T technical details · F10 blink test", small);
        }

        static Color StoryColour(StoryKind kind)
        {
            switch (kind)
            {
                case StoryKind.Seen:
                case StoryKind.Chase: return MapPainter.KarenRed;
                case StoryKind.Warning: return MapPainter.Warning;
                case StoryKind.Guess: return MapPainter.Guess;
                case StoryKind.Heard: return new Color(1f, 0.6f, 0.5f);
                case StoryKind.Plan: return new Color(1f, 0.45f, 0.45f);
                case StoryKind.Mood: return new Color(0.7f, 0.75f, 0.85f);
                case StoryKind.Learned: return new Color(0.85f, 0.6f, 1f);
                case StoryKind.Blink: return MapPainter.You;
                default: return MapPainter.SoundStore;
            }
        }

        void Technical()
        {
            var sb = new StringBuilder();
            KarenDirector d = brain.Director;
            StoreMap map = brain.Map;
            sb.AppendLine($"<b>{GameNames.Antagonist}</b>  rung {brain.config.rung}  shift {brain.Stats.Shift}  t={brain.ShiftTime:0}s");
            if (d != null) sb.AppendLine($"phase <b>{d.PhaseName}</b>  panic {d.Panic:0.00} → setpoint {d.Setpoint:0.00}  pressure {d.Pressure:+0.00;-0.00}  tension {d.Tension:0.00}");
            if (brain.Belief != null) sb.AppendLine($"belief peak <b>{map.RegionName(brain.Belief.PeakRegion)}</b> p={brain.Belief.Confidence:0.00} H={brain.Belief.Entropy:0.00} stale={Mathf.Min(999f, brain.Belief.Staleness):0}s");
            sb.AppendLine($"sight {brain.Body.Sight.Band} ({brain.Body.Sight.Awareness:0.00})  energy belief {brain.Energy.Energy:0.00}");
            if (brain.CurrentPlan != null)
                sb.AppendLine($"plan <b>{brain.CurrentPlan.Name}</b> step {brain.CurrentPlan.Plan.Index + 1}/{brain.CurrentPlan.Plan.Count}: {brain.CurrentPlan.Plan.Current}");
            sb.AppendLine("goals: " + string.Join("  ", brain.Goals.LastOptions.Select(o => $"{o.Name} {o.Utility:0.00}")));
            sb.AppendLine();
            foreach (ThoughtRecord r in brain.Log.Latest(16)) sb.AppendLine(r.Text);
            GUILayout.Label(sb.ToString(), small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("T back to the plain story", small);
        }

        // ---- F2 -----------------------------------------------------------------------------------

        void DrawReplay()
        {
            int size = FontSize;
            float margin = size;
            ShiftRecording r = Recording;
            if (r == null || r.Frames.Count == 0)
            {
                GUI.Label(new Rect(margin, margin, Screen.width - margin * 2, size * 4), "Nothing recorded yet — the recording starts when you clock in.\n[F2] close", heading);
                return;
            }

            float end = r.Frames[r.Frames.Count - 1].T;
            float mapWidth = Screen.width * 0.6f;
            float timelineH = size * 3.2f;
            painter.Layout(new Rect(margin, margin, mapWidth - margin, Screen.height - timelineH - margin * 3f));
            ShiftFrame f = r.At(replayT);
            painter.Draw(f, r.Events, replayT, false, Mathf.RoundToInt(size * 0.85f));

            // The timeline, with the moments that matter marked on it.
            var bar = new Rect(margin, Screen.height - timelineH, Screen.width - margin * 2f, size * 1.2f);
            GUI.color = new Color(0.2f, 0.22f, 0.27f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            foreach (ShiftEvent e in r.Events)
            {
                Color c;
                if (e.Kind == nameof(StoryKind.Chase)) c = MapPainter.KarenRed;
                else if (e.Kind == nameof(StoryKind.Seen) && e.Text.Contains("spotted")) c = new Color(1f, 0.45f, 0.45f);
                else if (e.Kind == nameof(StoryKind.Warning)) c = MapPainter.Warning;
                else if (e.Kind == "job") c = MapPainter.Following;
                else continue;
                GUI.color = c;
                GUI.DrawTexture(new Rect(bar.x + bar.width * e.T / Mathf.Max(1f, end) - 1f, bar.y, 3f, bar.height), Texture2D.whiteTexture);
            }

            // Clip moments in gold across the bar, and the markers that made them above it:
            // taller and brighter the better the clip; a marked bug in red.
            List<ClipMoment> clipMoments = MomentsOf(r);
            foreach (ClipMoment m in clipMoments)
            {
                GUI.color = new Color(1f, 0.84f, 0.25f, 0.22f);
                GUI.DrawTexture(new Rect(bar.x + bar.width * m.Start / Mathf.Max(1f, end), bar.y,
                                         Mathf.Max(2f, bar.width * (m.End - m.Start) / Mathf.Max(1f, end)), bar.height), Texture2D.whiteTexture);
            }
            foreach (ClipMarker m in r.Markers)
            {
                ClipMarkerKind k = m.Kind;
                if (k == null) continue;
                float strength = Mathf.Clamp01(k.Weight / 10f);
                GUI.color = m.Id == "manual_bug" ? MapPainter.KarenRed : Color.Lerp(new Color(0.55f, 0.57f, 0.62f), new Color(1f, 0.84f, 0.25f), strength);
                float h = size * 0.6f * (0.4f + 0.6f * strength);
                GUI.DrawTexture(new Rect(bar.x + bar.width * m.T / Mathf.Max(1f, end) - 1f, bar.y - h - 2f, 2f, h), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(bar.x + bar.width * replayT / Mathf.Max(1f, end) - 2f, bar.y - 4f, 4f, bar.height + 8f), Texture2D.whiteTexture);
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag)
                if (new Rect(bar.x, bar.y - 10f, bar.width, bar.height + 20f).Contains(Event.current.mousePosition))
                {
                    replayT = Mathf.Clamp01((Event.current.mousePosition.x - bar.x) / bar.width) * end;
                    playing = false;
                    Event.current.Use();
                }
            GUI.Label(new Rect(bar.x, bar.yMax + 4f, bar.width, size * 1.6f),
                $"{KarenNarrator.Clock(replayT)} / {KarenNarrator.Clock(end)}   {(playing ? "playing" : "paused")} at {speed:0}×   " +
                "<color=#8A909C>Space play/pause · 1/2/3 speed · ←/→ 5 s · drag the bar · gold: clip moments (F7 marks one) · O open the full report · F2 close</color>", small);

            // The story around this moment.
            var panel = new Rect(mapWidth + margin, margin, Screen.width - mapWidth - margin * 2f, Screen.height - timelineH - margin * 2f);
            GUILayout.BeginArea(panel);
            GUILayout.Label($"Shift {r.ShiftNumber} — replay", heading);
            GUILayout.Label($"{GameNames.Antagonist} is {Vector2.Distance(f.Player, f.Karen):0} m from you" + (f.KarenSees ? " and <color=#FF5454><b>can see you</b></color>." : "."), body);
            ClipMoment here = clipMoments.FirstOrDefault(m => replayT >= m.Start && replayT <= m.End);
            if (here != null)
                GUILayout.Label($"<color=#FFD640>Clip moment</color> {KarenNarrator.Clock(here.Start)}–{KarenNarrator.Clock(here.End)}, score {here.Score:0}: {string.Join(", ", here.Markers.Select(m => m.Id).Distinct())}", body);
            var near = r.Events.Where(e => e.Kind != "sound" && e.T <= replayT && e.T > replayT - 90f).Reverse().Take(14);
            foreach (ShiftEvent e in near)
                GUILayout.Label($"<color=#8A909C>{KarenNarrator.Clock(e.T)}</color>  {e.Text}", body);
            GUILayout.FlexibleSpace();
            MapPainter.Legend(GUILayoutUtility.GetRect(panel.width, MapPainter.LegendHeight(panel.width, size)), size);
            GUILayout.EndArea();
        }
    }
}
