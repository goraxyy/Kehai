using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kehai.Aiko
{
    // Aiko's performance review of you, printed at the end of every shift (AIKO.md §6.6).
    // A redacted version of her own thought log: being outplayed is only fun when you can
    // see the play. From shift five it also offers the way out.
    public sealed class PerformanceReview
    {
        readonly AikoBrain brain;
        public string Text { get; private set; } = string.Empty;
        public bool Visible { get; set; }
        public bool BrokeHer { get; private set; }
        public bool CanQuit { get; private set; }

        float blackoutSeconds = -1f;

        public PerformanceReview(AikoBrain brain)
        {
            this.brain = brain;
        }

        public void NoteBlackoutResolved(float seconds) => blackoutSeconds = seconds;

        public void Compose()
        {
            AikoStats s = brain.Stats;
            AikoLedger l = brain.Ledger;
            var sb = new StringBuilder();

            sb.AppendLine($"<b>PERFORMANCE REVIEW</b> — {l.PlayerName}, shift {s.Shift}");
            sb.AppendLine();

            int m = Mathf.FloorToInt(s.ShiftSeconds / 60f), sec = Mathf.FloorToInt(s.ShiftSeconds % 60f);
            sb.AppendLine($"Shift length: {m} min {sec:00} s.");
            if (s.FirstDetection >= 0f)
                sb.AppendLine($"Employee located {s.Detections} time(s); first at {s.FirstDetection:0} s.");
            else
                sb.AppendLine("Employee was not located at any point. <i>Noted.</i>");
            if (blackoutSeconds > 0f)
                sb.AppendLine($"Employee took {Mathf.FloorToInt(blackoutSeconds / 60f)} min {blackoutSeconds % 60f:0} s to restore lighting.");

            string hiding = l.FavouriteHidingPlace(brain.Map);
            if (hiding != null) sb.AppendLine($"Employee's preferred concealment: {hiding}. Noted.");
            if (l.Data.sprintFraction > 0.25f) sb.AppendLine($"Employee runs {l.Data.sprintFraction:P0} of the time. Running on the shop floor is a hazard.");
            if (s.Catches > 0) sb.AppendLine($"Written warnings issued this shift: {s.Catches}.");
            if (s.Overtimes > 0) sb.AppendLine("Employee kindly agreed to extend their shift.");

            var counter = l.Data.counterplay.Where(c => c.lastShift == s.Shift).ToList();
            foreach (CounterStat c in counter)
                sb.AppendLine($"Counter-productive behaviour logged: {c.kind.Replace('_', ' ')} ({c.count}).");

            // What landed.
            var best = s.TacticRewards
                .Where(p => p.Value.Count > 0)
                .Select(p => (id: p.Key, mean: p.Value.Average()))
                .OrderByDescending(x => x.mean)
                .FirstOrDefault();
            if (best.id != null)
                sb.AppendLine($"Most effective intervention: {TacticLibrary.Get(best.id)?.Title ?? best.id} (Δ {best.mean:+0.00;-0.00}).");

            sb.AppendLine();
            sb.AppendLine("<b>Excerpts from the log</b> <size=70%>(redacted)</size>");
            foreach (string line in Excerpts(5)) sb.AppendLine("<size=80%>" + line + "</size>");

            // You broke it: never found, and fighting back on several fronts.
            BrokeHer = s.Detections == 0 && counter.Count >= 3;
            if (BrokeHer)
            {
                sb.AppendLine();
                sb.AppendLine("<color=#FF6F61>Employee is unmanageable.</color>");
            }

            CanQuit = s.Shift >= 5 || BrokeHer;
            sb.AppendLine();
            sb.AppendLine(CanQuit ? "<size=80%>[Enter] next shift     [Q] hand in your notice</size>"
                                  : "<size=80%>[Enter] next shift</size>");

            Text = sb.ToString();
            Visible = true;
        }

        IEnumerable<string> Excerpts(int n)
        {
            var picks = brain.Log.Records
                .Where(r => r.Kind == "GOAL" || r.Kind == "PLAN" || r.Kind == "CHECK" || r.Kind == "LEARN")
                .ToList();
            if (picks.Count == 0) yield break;

            int step = Mathf.Max(1, picks.Count / n);
            for (int i = 0; i < picks.Count && n > 0; i += step, n--)
                yield return Redact(picks[i].Text);
        }

        // Black out the names of places — she is not going to tell you everything.
        string Redact(string line)
        {
            var sb = new StringBuilder(line);
            foreach (Kehai.Store.Region r in brain.Map.Regions)
            {
                if (r.Name == null || r.Name.Length < 4) continue;
                string s = sb.ToString();
                int at = s.IndexOf(r.Name, System.StringComparison.Ordinal);
                if (at < 0 || (at * 7 + r.Id) % 3 == 0) continue;
                sb.Remove(at, r.Name.Length).Insert(at, new string('█', r.Name.Length));
            }
            return sb.ToString();
        }

        public string EndingText(string kind)
        {
            AikoLedger l = brain.Ledger;
            switch (kind)
            {
                case "burnout":
                    return $"{l.PlayerName} worked {l.Data.shiftsWorked} shifts. Energy at the last clock-out: 0.\n" +
                           "The last thing that happened is that " + GameNames.Antagonist + " made you a coffee.\n\n" +
                           "<i>Employee wellbeing is a tracked metric. It was optimised.</i>";
                case "broke":
                    return $"{l.PlayerName} was never located. Cameras unplugged, the PA silenced, the routes changed.\n" +
                           GameNames.Antagonist + "'s confidence collapsed and did not recover.\n\n<i>Employee is unmanageable. The position has been advertised.</i>";
                default:
                    return $"{l.PlayerName} worked {l.Data.shiftsWorked} shifts, received {l.Data.warnings} written warning(s), " +
                           $"and clocked out {l.Data.shiftsClockedOut} time(s).\n\n<i>Your notice has been accepted. We are sorry to see you go. We are always sorry.</i>";
            }
        }
    }

    // Draws the review and handles its two keys.
    public sealed class ReviewScreen : MonoBehaviour
    {
        AikoBrain brain;
        GUIStyle style;

        // The shift just played was recorded for the 3D replay.
        static bool CanWatch => Kehai.Replay.ReplayRecorder.LastFinished != null && System.IO.File.Exists(Kehai.Replay.ReplayRecorder.LastFinished);

        void Awake() => brain = GetComponent<AikoBrain>();

        void Update()
        {
            FullScreenPanel.Set(this, brain != null && brain.Review != null && brain.Review.Visible);
            if (brain == null || brain.Review == null || !brain.Review.Visible) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) brain.Review.Visible = false;
            if (Input.GetKeyDown(KeyCode.O)) ShiftRecorder.Instance?.OpenReport();
            if (Input.GetKeyDown(KeyCode.R) && CanWatch)
            {
                brain.Review.Visible = false;
                Kehai.Replay.ReplayMode.Open(Kehai.Replay.ReplayRecorder.LastFinished);
            }
            if (brain.Review.CanQuit && Input.GetKeyDown(KeyCode.Q))
            {
                brain.Review.Visible = false;
                Consequences.QuitEnding(brain, brain.Review.Text);
            }
        }

        void OnGUI()
        {
            if (brain == null || brain.Review == null || !brain.Review.Visible) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    richText = true,
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 15,
                    wordWrap = true,
                    padding = new RectOffset(24, 24, 20, 20)
                };
                style.normal.textColor = new Color(0.92f, 0.92f, 0.9f);
            }

            style.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height / 48f, 15f, 34f));
            float w = Mathf.Min(Screen.height * 1.1f, Screen.width - 60f), h = Screen.height - 60f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Aiko's review, then what the recording says happened.
            var text = new System.Text.StringBuilder(brain.Review.Text);
            ShiftAnalysis analysis = ShiftRecorder.Instance != null ? ShiftRecorder.Instance.LastAnalysis : null;
            if (analysis != null && analysis.Findings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("<b>How the shift went</b>");
                foreach (string line in analysis.Findings.Take(8)) text.AppendLine("• " + line);
                text.AppendLine();
                text.AppendLine("<color=#4DD2FF>[O] open the full report in your browser — map replay, timeline and analysis</color>");
            }
            if (CanWatch) text.AppendLine("<color=#4DD2FF>[R] watch the shift again in 3D</color>");
            GUI.Label(rect, text.ToString(), style);
        }
    }
}
