using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Every trick, end to end, in a real shift (0.3.0 build plan C5.5):
    //
    //   Unity -batchmode -nographics -projectPath <clone> -executeMethod EvalBatch.Play -kehai-trick-check
    //
    // One long shift on the real store, with an efficient test player doing its jobs and
    // answering her chores. One at a time, each of the owner's tricks is forced from the
    // developer hook (KarenBrain.Force, past the Director's gate but not past its own
    // preconditions), and the check writes down whether it:
    //
    //   started   — it found something to do (or why not);
    //   landed    — an EFFECT reached her thought log, or its effect is running;
    //   showed    — its chore is waiting somewhere (FixAt) or its effect runs;
    //   fixed     — the test player answered the chore, and how long that took;
    //   and at the end, whether the shift still ended: nothing left stuck.
    //
    // Fairness violations over the run are counted too (rules 1, 2, 4–9). The report goes to
    // kehai_eval/trick_check_<time>.md and the log; it's a check of plumbing on whatever
    // scene the project holds, not a measure of how scary anything is.
    public sealed class TrickCheck : MonoBehaviour
    {
        public static readonly string[] Tricks =
        {
            // Ambush straight after Blackout: with the lights out it waits on your way to the
            // breaker box (FixAt), which is the 0.3.0 combo path.
            "shelf_sweep", "spill", "bin_tamper", "phantom_chime", "door_lock", "blackout", "ambush", "fog",
            "camera_bolt_on", "camera_watch", "camera_break", "tool_theft", "understudy", "overtime",
        };

        public const float StartWithin = 30f, PlanWithin = 130f, FixWithin = 240f, EndWithin = 420f;
        public const int MaxRestarts = 6;

        sealed class Row
        {
            public string Id, Started = "—", Landed = "—", Showed = "—", Fixed = "—", Note = "";
            // For the trick videos (0.4.1, tools/tricks/record_tricks.sh): the replay it's in,
            // and when in that shift it started and first landed (-1: it didn't).
            public string Krec = "";
            public float StartedAt = -1f, LandedAt = -1f;
        }

        readonly List<Row> rows = new List<Row>();
        string[] tricks = Tricks;
        readonly HashSet<string> retried = new HashSet<string>();
        Coroutine botRun;
        readonly List<string> botFailures = new List<string>();
        public string ReportPath { get; private set; }
        public bool Finished { get; private set; }
        public System.Action<TrickCheck> Completed;

        // `only`: a comma-separated list of trick ids to check, instead of all of them.
        public static TrickCheck Run(int seed, string only = null)
        {
            KehaiEnv env = KehaiEnv.Ensure();
            var check = env.gameObject.AddComponent<TrickCheck>();
            if (!string.IsNullOrEmpty(only))
                check.tricks = only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            check.StartCoroutine(check.Execute(env, seed));
            return check;
        }

        IEnumerator Execute(KehaiEnv env, int seed)
        {
            var config = new EnvConfig
            {
                seed = seed, rung = "E", shiftSeconds = 900f, fps = 15, render = false, freshLedger = true,
                startShift = 10, agent = "efficient", syntheticBlinks = true, overtimeCap = 300f,
            };
            int violationsBefore = FairnessGuard.Violations;
            KarenBrain brain = null;
            bool botDone = true;
            int restarts = -1;
            for (int n = 0; n < tricks.Length; n++)
            {
                string id = tricks[n];
                // The overtime needs the shift's last two minutes: a short shift of its own.
                if (id == "overtime" && config.shiftSeconds > 300f)
                {
                    // 130 s: the last two minutes start as the shift settles, before she can catch anyone.
                    config.shiftSeconds = 130f;
                    if (botRun != null) StopCoroutine(botRun);   // one test player at a time
                    botDone = true;
                }

                // A new shift when there's none: at the start, and after she catches the test
                // player (a catch ends the shift).
                if (botDone)
                {
                    if (++restarts > MaxRestarts) { rows.Add(new Row { Id = id, Note = "too many restarts" }); continue; }
                    yield return env.ResetEpisode(config);
                    brain = KarenBrain.Instance;
                    if (brain == null) { Fail("no Karen in the scene"); yield break; }
                    var bot = new SimulatedPlayer(env, PlayerProfile.Efficient, seed * 31 + restarts, reactive: true);
                    botDone = false;
                    botRun = StartCoroutine(Play(bot, () => botDone = true));
                    yield return Wait(10f);   // let the shift settle
                }
                if (id == "overtime")
                {
                    // The last two minutes, as the trick itself asks.
                    float until = Time.time + 900f;
                    while (!botDone && env.Shift != null && env.Shift.TimeRemaining > OvertimeTactic.LastSeconds && Time.time < until)
                        yield return null;
                }
                int before = rows.Count;
                yield return Check(brain, id, () => botDone);
                // The shift ended under it (a catch): once more, in the next shift.
                if (botDone && rows.Count > before && rows[rows.Count - 1].Started == "no" && retried.Add(id))
                {
                    rows.RemoveAt(rows.Count - 1);
                    n--;
                }
            }

            float endBy = Time.time + EndWithin;
            while (!botDone && Time.time < endBy) yield return null;
            int violations = FairnessGuard.Violations - violationsBefore;

            Write(env, botDone, violations, Mathf.Max(0, restarts));
            Finished = true;
            Completed?.Invoke(this);
        }

        IEnumerator Play(SimulatedPlayer bot, System.Action done)
        {
            yield return bot.PlayShift();
            KehaiEnv env = KehaiEnv.Instance;
            if (env != null && env.Metrics != null) botFailures.AddRange(env.Metrics.Failures);
            done();
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        IEnumerator Check(KarenBrain brain, string id, System.Func<bool> over)
        {
            var row = new Row { Id = id };
            rows.Add(row);
            Tactic t = TacticLibrary.Get(id);
            if (t == null) { row.Note = "not in the library"; yield break; }

            // Wait for her to be free, then ask until it starts (its own preconditions may need a
            // moment: a full bay, a quiet stretch).
            float give = Time.time + StartWithin;
            string why = null;
            bool started = false;
            while (!over() && Time.time < give)
            {
                if (!brain.ForcedPlanRunning && brain.Force(id, out why)) { started = true; break; }
                yield return Wait(2f);
            }
            if (!started) { row.Started = "no"; row.Note = why ?? "the shift ended"; yield break; }
            row.Started = "yes";
            float startedAt = brain.ShiftTime;
            row.StartedAt = startedAt;
            row.Krec = Kehai.Replay.ReplayRecorder.Instance != null ? Kehai.Replay.ReplayRecorder.Instance.CurrentFile ?? "" : "";
            try { yield return Follow(brain, t, row, startedAt, over); }
            finally
            {
                ThoughtRecord landed = brain.Log.Records.FirstOrDefault(r => r.Kind == "EFFECT" && r.T >= startedAt - 0.01f);
                if (landed != null) row.LandedAt = landed.T;
                row.Note += Thoughts(brain, id, startedAt);
            }
        }

        // What she thought about it, for the report: her plan, its checks and its effects.
        static string Thoughts(KarenBrain brain, string id, float since)
        {
            if (brain == null) return "";
            var lines = brain.Log.Records
                .Where(r => r.T >= since - 0.01f && (r.Kind == "PLAN" || r.Kind == "CHECK" || r.Kind == "EFFECT"))
                .Select(r => r.Text.Length > 150 ? r.Text.Substring(0, 150) + "…" : r.Text)
                .Take(4).ToList();
            return lines.Count == 0 ? "" : string.Join(" ⏐ ", lines);
        }

        IEnumerator Follow(KarenBrain brain, Tactic t, Row row, float startedAt, System.Func<bool> over)
        {

            // Watch it while her plan runs: a chore can be answered before she's walked away.
            float planBy = Time.time + PlanWithin, ranAt = -1f, stoppedAt = -1f;
            string choreAt = null;
            while (brain.ForcedPlanRunning && Time.time < planBy)
            {
                bool now = t.EffectRunning(brain.Ctx);
                if (now && ranAt < 0f) ranAt = Time.time;
                if (now && choreAt == null && t.FixAt(brain.Ctx, out Vector3 spot)) choreAt = brain.Map.NameAt(spot);
                if (!now && ranAt >= 0f && stoppedAt < 0f) stoppedAt = Time.time;
                yield return null;
            }
            bool effect = brain.Log.Records.Any(r => r.Kind == "EFFECT" && r.T >= startedAt - 0.01f);
            bool running = t.EffectRunning(brain.Ctx);
            if (running && ranAt < 0f) ranAt = Time.time;
            row.Landed = effect || ranAt >= 0f ? "yes" : t.Id == "ambush" ? "n/a: it waits" : "no";
            if (brain.ForcedPlanRunning) row.Note = $"its plan was still running after {PlanWithin:0} s; ";

            bool chore = t.FixAt(brain.Ctx, out Vector3 at);
            if (chore) choreAt = brain.Map.NameAt(at);
            row.Showed = choreAt != null ? "at " + choreAt : ranAt >= 0f ? "running" : "nothing to fix";
            // Blackout then Ambush: the lights stay out for Ambush to wait by the breaker box.
            if (t.Id == "blackout" && running) { row.Fixed = "left for Ambush"; yield break; }
            if (t.Id == "ambush")
            {
                bool atChore = brain.Log.Records.Any(r => r.T >= startedAt - 0.01f && r.Kind == "PLAN" && r.Text.Contains("left you a chore"));
                row.Showed = atChore ? "waited on the way to her last trick's chore" : "waited on a route of yours";
            }

            if (!running)
            {
                row.Fixed = stoppedAt >= 0f ? $"after {stoppedAt - ranAt:0} s, while she was still at it" : chore ? "?" : "n/a";
                yield break;
            }

            float since = ranAt >= 0f ? ranAt : Time.time, fixBy = Time.time + FixWithin;
            while (!over() && Time.time < fixBy && t.EffectRunning(brain.Ctx)) yield return null;
            if (over()) { row.Fixed = "the shift ended first"; yield break; }
            row.Fixed = t.EffectRunning(brain.Ctx) ? $"no, after {FixWithin:0} s" : $"after {Time.time - since:0} s";
        }

        void Fail(string why)
        {
            rows.Add(new Row { Id = "—", Note = why });
            Write(null, false, 0, 0);
            Finished = true;
            Completed?.Invoke(this);
        }

        void Write(KehaiEnv env, bool ended, int violations, int restarts)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Trick check, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("One long shift (rung E, career shift 10, an efficient test player), each trick forced once in turn.");
            sb.AppendLine();
            sb.AppendLine("| trick | started | landed | chore | fixed | note |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (Row r in rows)
                sb.AppendLine($"| {r.Id} | {r.Started} | {r.Landed} | {r.Showed} | {r.Fixed} | {r.Note.Replace("|", "/")} |");
            sb.AppendLine();
            sb.AppendLine($"Shift ended (clocked out or timed out, nothing stuck): **{(ended ? "yes" : "no")}**. " +
                          $"Shifts restarted after a catch: {restarts}.");
            sb.AppendLine($"Fairness violations during the check: **{violations}**.");
            if (botFailures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("The test player's failed actions (the last 12):");
                foreach (string f in botFailures.Skip(Mathf.Max(0, botFailures.Count - 12))) sb.AppendLine("- " + f);
            }
            if (FairnessGuard.ViolationLog.Count > 0)
            {
                sb.AppendLine();
                foreach (string v in FairnessGuard.ViolationLog.Skip(Mathf.Max(0, FairnessGuard.ViolationLog.Count - 20))) sb.AppendLine("- " + v);
            }

            string dir = KehaiEnv.EvalDirectory;
            Directory.CreateDirectory(dir);
            ReportPath = Path.Combine(dir, "trick_check_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".md");
            File.WriteAllText(ReportPath, sb.ToString());

            // The same, as tab-separated lines a script can read: where each trick is on film.
            var film = new StringBuilder("id\tstarted\tlanded\tkrec\n");
            foreach (Row r in rows)
                film.Append(r.Id).Append('\t').Append(r.StartedAt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(r.LandedAt.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(r.Krec).Append('\n');
            File.WriteAllText(Path.ChangeExtension(ReportPath, ".tsv"), film.ToString());
            Debug.Log("TrickCheck report " + ReportPath + "\n" + sb);
        }
    }
}
