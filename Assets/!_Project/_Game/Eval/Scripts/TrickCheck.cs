using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Karen;
using Kehai.Replay;
using UnityEngine;

namespace Kehai.Eval
{
    // EvalBatch.Play -kehai-trick-check [-trick-check-only id,id] [-trick-check-strict]
    // The strict batch run returns nonzero for missing evidence, recording or fairness failures.
    public sealed class TrickCheck : MonoBehaviour
    {
        // Overtime needs a shorter shift. Keep it last so the other checks can share a shift.
        public static readonly string[] Tricks = TacticLibrary.Live.Where(t => !t.OwnMove)
            .OrderBy(t => t.Id == "overtime").Select(t => t.Id).ToArray();

        public const float StartWithin = 30f, PlanWithin = 130f, FixWithin = 240f, EndWithin = 420f;
        public const int MaxRestarts = 6;

        [Serializable]
        public sealed class Row
        {
            public string id, expected, target, effect, counter, failure, krec, shiftRecord;
            public float attempted = -1f, started = -1f, landed = -1f, answered = -1f;
            public bool recorded, passed;
        }

        [Serializable]
        sealed class Report
        {
            public int seed, episodes, timeouts, fairnessViolations;
            public string lastShift;
            public bool passed;
            public Row[] rows;
        }

        readonly List<Row> rows = new List<Row>();
        readonly List<string> botFailures = new List<string>();
        string[] tricks = Tricks;
        SimulatedPlayer bot;
        Coroutine botRun;
        int timeouts;
        public string ReportPath { get; private set; }
        public bool Finished { get; private set; }
        public bool Passed { get; private set; }
        public Action<TrickCheck> Completed;

        public static TrickCheck Run(int seed, string only = null)
        {
            var env = KehaiEnv.Ensure();
            var check = env.gameObject.AddComponent<TrickCheck>();
            if (!string.IsNullOrEmpty(only))
                check.tricks = only.Split(',').Select(x => x.Trim()).Distinct().ToArray();
            check.StartCoroutine(check.Execute(env, seed));
            return check;
        }

        IEnumerator Execute(KehaiEnv env, int seed)
        {
            yield return null; // give callers time to register Completed, including for invalid IDs
            var config = new EnvConfig
            {
                seed = seed, rung = "E", shiftSeconds = 900f, fps = 15, render = false, freshLedger = true,
                startShift = 4, agent = "efficient", syntheticBlinks = true, overtimeCap = 300f,
            };
            int before = FairnessGuard.Violations, episodes = 0;
            foreach (string id in tricks)
            {
                var t = TacticLibrary.Get(id);
                var row = new Row { id = id, expected = t?.Chore };
                rows.Add(row);
                if (t == null || t.OwnMove || t.Draft) { row.failure = "Unknown or inactive trick ID"; continue; }

                float duration = id == "overtime" ? 130f : 900f;
                if (botRun == null || env.Done || duration != config.shiftSeconds)
                {
                    FinishEpisode();
                    config.shiftSeconds = duration;
                    yield return env.ResetEpisode(config);
                    episodes++;
                    if (!env.Metrics.Started || KarenBrain.Instance == null)
                    {
                        row.failure = "Shift or Karen failed to start";
                        continue;
                    }
                    bot = new SimulatedPlayer(env, PlayerProfile.Efficient, seed * 31 + episodes - 1, reactive: true);
                    botRun = StartCoroutine(Play(env));
                    float ready = Time.time + 10f;
                    while (!env.Done && Time.time < ready) yield return null;
                }
                yield return Check(env, KarenBrain.Instance, t, row);
            }

            // Allow the last shift to finish normally. Timeouts and the report deadline are
            // different outcomes from either a clock-out or a catch.
            float by = Time.time + EndWithin;
            while (botRun != null && !env.Done && Time.time < by) yield return null;
            string lastShift = episodes == 0 || !env.Metrics.Started ? "no shift" :
                env.Metrics.TimedOut ? "timed out" : env.Metrics.ClockedOut ? "clocked out" :
                env.Done ? "caught" : "report deadline reached";
            FinishEpisode();
            Write(seed, episodes, FairnessGuard.Violations - before, lastShift);
            Finished = true;
            Completed?.Invoke(this);
        }

        IEnumerator Play(KehaiEnv env)
        {
            yield return bot.PlayShift();
            botFailures.AddRange(env.Metrics.Failures);
            if (env.Metrics.TimedOut) timeouts++;
        }

        void FinishEpisode()
        {
            if (botRun == null) return;
            StopCoroutine(botRun);
            botRun = null;
            // A short overtime shift reload or a report deadline can interrupt a live shift.
            // FinishNow closes both existing writers before paths are checked or the scene unloads.
            ShiftRecorder.Instance?.FinishNow();
        }

        static float Stamp(KarenBrain brain) => ShiftRecorder.Instance != null && ShiftRecorder.Instance.Recording
            ? ShiftRecorder.Instance.ShiftTime : brain.ShiftTime;

        IEnumerator Check(KehaiEnv env, KarenBrain brain, Tactic t, Row row)
        {
            var shift = ShiftRecorder.Instance;
            if (shift != null && shift.Recording)
            {
                row.krec = ReplayRecorder.Instance?.CurrentFile;
                row.shiftRecord = shift.Stem + ".json";
            }
            row.attempted = Stamp(brain);
            float startBy = Time.time + StartWithin;
            string why = "the shift ended";
            while (!env.Done && brain.ShiftActive && Time.time < startBy)
            {
                if (brain.ForcedPlanRunning) why = "the previous forced plan is still running";
                else if (t.EffectRunning(brain.Ctx)) why = "an earlier use is still active";
                else if (brain.Force(t.Id, out why)) break;
                yield return null;
            }
            PlanTree plan = brain.CurrentPlan;
            if (why != null || !brain.ForcedPlanRunning || plan?.Tactic != t)
            {
                row.failure = "Did not start: " + (why ?? "shift ended");
                yield break;
            }
            row.started = Stamp(brain);
            row.target = plan.Target;
            var evidence = new TrickCheckEvidence(env, brain, bot, plan, row);
            try
            {
                float planBy = Time.time + PlanWithin;
                while (!env.Done && brain.ShiftActive && Time.time < planBy && brain.CurrentPlan == plan)
                {
                    evidence.Sample();
                    yield return null;
                }
                if (!env.Done && brain.ShiftActive) evidence.Sample();
                if (brain.CurrentPlan == plan && Time.time >= planBy) row.failure = "Forced plan exceeded its deadline";
                float answerBy = Time.time + FixWithin;
                while (row.failure == null && !evidence.Complete && !env.Done && brain.ShiftActive &&
                       evidence.WaitingForOutcome && Time.time < answerBy)
                {
                    yield return null;
                    if (!env.Done && brain.ShiftActive) evidence.Sample();
                }
                if (row.failure == null && !evidence.Complete)
                    row.failure = env.Done || !brain.ShiftActive ? "Shift ended before the required evidence" :
                        Time.time >= answerBy ? "Answer deadline reached" : "Missing " + evidence.Missing;
            }
            finally { evidence.Dispose(); }
        }

        static bool HasFile(string path) => !string.IsNullOrEmpty(path) && File.Exists(path) && new FileInfo(path).Length > 0;

        void Write(int seed, int episodes, int violations, string lastShift)
        {
            var sb = new StringBuilder("# Recorded trick checks\n\n");
            sb.AppendLine($"Seed {seed}, rung E, career shift 4, efficient bot. Episodes {episodes}. Final shift {lastShift}.");
            sb.AppendLine($"Episode timeouts {timeouts}. Fairness violations {violations}.");
            sb.AppendLine();
            sb.AppendLine("| Trick | Expected | Plan target | Observed effect | Bot response | Result |");
            sb.AppendLine("|---|---|---|---|---|---|");
            var film = new StringBuilder("id\tstarted\tlanded\tkrec\n");
            foreach (Row row in rows)
            {
                row.recorded = HasFile(row.krec) && HasFile(row.shiftRecord);
                if (!row.recorded) row.failure = string.IsNullOrEmpty(row.failure) ? "Missing replay or shift record" :
                    row.failure + ". Missing replay or shift record";
                row.passed = row.failure == null && row.started >= 0 && row.landed >= row.started && row.answered >= row.landed;
                string Cell(string value) => (value ?? "—").Replace("|", "/").Replace("\n", " ");
                sb.AppendLine($"| {Cell(row.id)} | {Cell(row.expected)} | {Cell(row.target)} | {Cell(row.effect)} | {Cell(row.counter)} | {(row.passed ? "pass" : "fail: " + Cell(row.failure))} |");
                film.Append(row.id).Append('\t').Append(row.started.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.landed.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t').Append(row.krec).Append('\n');
            }
            Passed = rows.Count > 0 && rows.All(r => r.passed) && violations == 0 && timeouts == 0 &&
                (lastShift == "clocked out" || lastShift == "caught");
            sb.AppendLine($"\nStrict result **{(Passed ? "pass" : "fail")}**.");
            if (botFailures.Count > 0)
            {
                sb.AppendLine("\nRecent failed bot actions\n");
                foreach (string f in botFailures.Skip(Mathf.Max(0, botFailures.Count - 12))) sb.AppendLine("- " + f);
            }

            Directory.CreateDirectory(KehaiEnv.EvalDirectory);
            ReportPath = Path.Combine(KehaiEnv.EvalDirectory, $"trick_check_{DateTime.Now:yyyyMMdd_HHmmss_fff}.md");
            File.WriteAllText(ReportPath, sb.ToString());
            File.WriteAllText(Path.ChangeExtension(ReportPath, ".tsv"), film.ToString());
            File.WriteAllText(Path.ChangeExtension(ReportPath, ".json"), JsonUtility.ToJson(new Report {
                seed = seed, episodes = episodes, lastShift = lastShift, timeouts = timeouts,
                fairnessViolations = violations, passed = Passed, rows = rows.ToArray()
            }, true));
            Debug.Log("TrickCheck report " + ReportPath + "\n" + sb);
        }
    }
}
