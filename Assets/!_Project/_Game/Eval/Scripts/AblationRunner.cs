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
    public sealed class AblationPlan
    {
        public KarenRung[] rungs = { KarenRung.A_RandomPatrol, KarenRung.B_ScriptedPatrol, KarenRung.C_BeliefGrid, KarenRung.D_Bandit, KarenRung.E_Ledger, KarenRung.F_Blink };
        public PlayerProfile[] profiles = { PlayerProfile.Efficient, PlayerProfile.Skittish, PlayerProfile.Reckless };
        public int careers = 1;
        public int shiftsPerCareer = 4;
        public int startShift = 3;
        public float shiftSeconds = 150f;
        public int fps = 15;
        public int baseSeed = 1000;
        public bool render = false;
        public bool verbose = false;
        public string outDirectory;
    }

    // The ablation ladder (IDEAS.md §2), run end to end with simulated players.
    //
    // Paired design: for a given player profile and career, every rung sees the same seed —
    // the same customers, the same spills — so a difference between rungs is Karen, not luck.
    // A career is several consecutive shifts in one session, which is what gives the
    // persistent Ledger (rung E) something to remember.
    public sealed class AblationRunner : MonoBehaviour
    {
        public AblationPlan Plan { get; private set; }
        public readonly List<EpisodeMetrics> Results = new List<EpisodeMetrics>();
        public bool Finished { get; private set; }
        public string Progress { get; private set; } = "starting";
        public string JsonlPath { get; private set; }
        public string MarkdownPath { get; private set; }
        public System.Action<AblationRunner> Completed;

        float startedAt;

        public static AblationRunner Run(AblationPlan plan)
        {
            KehaiEnv env = KehaiEnv.Ensure();
            var runner = env.gameObject.AddComponent<AblationRunner>();
            runner.Plan = plan;
            runner.StartCoroutine(runner.Execute(env));
            return runner;
        }

        IEnumerator Execute(KehaiEnv env)
        {
            startedAt = Time.realtimeSinceStartup;
            string dir = Plan.outDirectory ?? KehaiEnv.EvalDirectory;
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            JsonlPath = Path.Combine(dir, $"ablation_{stamp}.jsonl");
            MarkdownPath = Path.Combine(dir, $"ablation_{stamp}.md");

            int total = Plan.rungs.Length * Plan.profiles.Length * Plan.careers * Plan.shiftsPerCareer;
            int done = 0;

            foreach (PlayerProfile profile in Plan.profiles)
            for (int career = 0; career < Plan.careers; career++)
            {
                int seed = Plan.baseSeed + career * 100 + (int)profile * 10;
                foreach (KarenRung rung in Plan.rungs)
                {
                    var config = new EnvConfig
                    {
                        seed = seed,
                        rung = ((char)('A' + (int)rung)).ToString(),
                        shiftSeconds = Plan.shiftSeconds,
                        fps = Plan.fps,
                        render = Plan.render,
                        freshLedger = true,
                        startShift = Plan.startShift,
                        agent = profile.ToString().ToLowerInvariant(),
                        syntheticBlinks = true,
                        verbose = Plan.verbose
                    };
                    yield return env.ResetEpisode(config);

                    for (int s = 0; s < Plan.shiftsPerCareer; s++)
                    {
                        Progress = $"{done + 1}/{total}: rung {config.rung}, {config.agent}, career {career + 1}, shift {Plan.startShift + s}";
                        var bot = new SimulatedPlayer(env, profile, seed * 31 + s);
                        yield return bot.PlayShift();

                        EpisodeMetrics m = env.Metrics;
                        Results.Add(m);
                        File.AppendAllText(JsonlPath, MiniJson.Serialize(m.ToDictionary()) + "\n");
                        done++;
                        Debug.Log($"Ablation {done}/{total} rung {m.Rung} {m.Agent} shift {m.Shift}: " +
                                  $"{(m.ClockedOut ? "clocked out" : m.TimedOut ? "timed out" : "ended")} at {m.SimSeconds:0}s sim / {m.WallSeconds:0}s wall, " +
                                  $"steps {m.Steps}, mopped {m.SpillsMopped}, restocked {m.ShelvesRestocked}, served {m.CustomersServed}, " +
                                  $"detections {m.Detections}, catches {m.Catches}, tactics {m.TacticsUsed} [{m.TopTactics}], " +
                                  $"panic {m.MeanPanic:0.00}, failures [{string.Join(",", m.Failures)}]");

                        if (s < Plan.shiftsPerCareer - 1) yield return env.ClockIn();
                    }
                }
            }

            File.WriteAllText(MarkdownPath, Summarise());
            Progress = $"done: {Results.Count} shifts in {(Time.realtimeSinceStartup - startedAt) / 60f:0.0} min";
            Finished = true;
            Completed?.Invoke(this);
        }

        // ---- the table ---------------------------------------------------------------------

        public string Summarise()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Generated {System.DateTime.Now:yyyy-MM-dd HH:mm} by `AblationRunner` — {Results.Count} simulated shifts, " +
                          $"{Plan.careers} career(s) × {Plan.shiftsPerCareer} shifts (career shifts {Plan.startShift}–{Plan.startShift + Plan.shiftsPerCareer - 1}) " +
                          $"× {Plan.profiles.Length} player profiles × {Plan.rungs.Length} rungs; {Plan.shiftSeconds:0} s shifts at a fixed {Plan.fps} fps step.");
            sb.AppendLine();
            sb.AppendLine("### By rung (all profiles)");
            sb.AppendLine();
            sb.AppendLine("| rung | shifts | completed | detected | first detection (s) | catches/shift | tactics/shift | tactic entropy (bits) | mean panic | setpoint RMSE | customers lost/shift |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in Results.GroupBy(r => r.Rung).OrderBy(g => g.Key)) Row(sb, "**" + g.Key + "**", g.ToList());

            sb.AppendLine();
            sb.AppendLine("### By rung and player profile");
            sb.AppendLine();
            sb.AppendLine("| rung | profile | shifts | completed | detected | first detection (s) | catches/shift | tactics/shift | tactic entropy (bits) | mean panic | setpoint RMSE | customers lost/shift |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var g in Results.GroupBy(r => (r.Rung, r.Agent)).OrderBy(g => g.Key.Rung).ThenBy(g => g.Key.Agent))
                Row(sb, $"{g.Key.Rung} | {g.Key.Agent}", g.ToList());

            sb.AppendLine();
            sb.AppendLine("### What " + GameNames.Antagonist + " reached for, per profile (rungs D–F)");
            sb.AppendLine();
            sb.AppendLine("| rung | profile | most-used tactics across the career |");
            sb.AppendLine("|---|---|---|");
            foreach (var g in Results.Where(r => string.CompareOrdinal(r.Rung, "D") >= 0)
                                     .GroupBy(r => (r.Rung, r.Agent)).OrderBy(g => g.Key.Rung).ThenBy(g => g.Key.Agent))
            {
                var counts = new Dictionary<string, int>();
                foreach (EpisodeMetrics m in g)
                    foreach (string part in m.TopTactics.Split(' '))
                    {
                        int x = part.LastIndexOf('×');
                        if (x <= 0) continue;
                        string id = part.Substring(0, x);
                        counts.TryGetValue(id, out int n);
                        counts[id] = n + int.Parse(part.Substring(x + 1), CultureInfo.InvariantCulture);
                    }
                sb.AppendLine($"| {g.Key.Rung} | {g.Key.Agent} | {string.Join(", ", counts.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key} ×{p.Value}"))} |");
            }

            sb.AppendLine();
            sb.AppendLine("### Player failures (the eval taxonomy, applied to the simulated players)");
            sb.AppendLine();
            sb.AppendLine("| rung | " + string.Join(" | ", Taxonomy) + " |");
            sb.AppendLine("|---|" + string.Concat(Enumerable.Repeat("---|", Taxonomy.Length)));
            foreach (var g in Results.GroupBy(r => r.Rung).OrderBy(g => g.Key))
                sb.AppendLine($"| {g.Key} | " + string.Join(" | ", Taxonomy.Select(t => $"{g.Count(r => r.Failures.Contains(t))}/{g.Count()}")) + " |");
            return sb.ToString();
        }

        static readonly string[] Taxonomy =
        {
            FailureTaxonomy.Starvation, FailureTaxonomy.Thrashing, FailureTaxonomy.InterferenceBlindness,
            FailureTaxonomy.ClockBlindness, FailureTaxonomy.ResourceMismanagement, FailureTaxonomy.SpuriousCompletion
        };

        static void Row(StringBuilder sb, string label, List<EpisodeMetrics> g)
        {
            int n = g.Count;
            var detected = g.Where(r => r.FirstDetection >= 0f).ToList();
            string first = detected.Count > 0 ? detected.Average(r => r.FirstDetection).ToString("0", CultureInfo.InvariantCulture) : "—";
            sb.AppendLine($"| {label} | {n} | {Pct(g.Count(r => r.ClockedOut), n)} | {Pct(detected.Count, n)} | {first} | " +
                          $"{g.Average(r => r.Catches):0.00} | {g.Average(r => r.TacticsUsed):0.0} | {g.Average(r => r.TacticEntropy):0.00} | " +
                          $"{g.Average(r => r.MeanPanic):0.00} | {g.Average(r => r.PanicSetpointRmse):0.00} | {g.Average(r => r.CustomersLost):0.0} |");
        }

        static string Pct(int k, int n) => n == 0 ? "—" : $"{100f * k / n:0}%";
    }

    // Command-line entry for builds (IDEAS.md §1 "Headless + time-scaled"):
    //
    //   Kehai -batchmode -nographics -kehai-env 5555
    //   Kehai -batchmode -nographics -kehai-ablation -ablation-careers 2 -ablation-shifts 4
    public static class EvalCommandLine
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            if (KehaiEnv.Instance != null) return;   // already running

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-kehai-env")
                {
                    int port = i + 1 < args.Length && int.TryParse(args[i + 1], out int p) ? p : 5555;
                    EnvServer.Start(port, Application.isBatchMode);
                    return;
                }
                if (args[i] == "-kehai-ablation")
                {
                    var plan = new AblationPlan
                    {
                        careers = Int(args, "-ablation-careers", 1),
                        shiftsPerCareer = Int(args, "-ablation-shifts", 4),
                        startShift = Int(args, "-ablation-start", 3),
                        shiftSeconds = Int(args, "-ablation-seconds", 150),
                        fps = Int(args, "-ablation-fps", 15),
                        baseSeed = Int(args, "-ablation-seed", 1000),
                        outDirectory = Str(args, "-ablation-out", null),
                        verbose = System.Array.IndexOf(args, "-ablation-verbose") >= 0
                    };
                    string rungs = Str(args, "-ablation-rungs", null);
                    if (rungs != null)
                        plan.rungs = rungs.Where(char.IsLetter).Select(ch => (KarenRung)(char.ToUpperInvariant(ch) - 'A')).ToArray();
                    string profiles = Str(args, "-ablation-profiles", null);   // e.g. "efficient,reckless"
                    if (profiles != null)
                        plan.profiles = profiles.Split(',').Select(p => (PlayerProfile)System.Enum.Parse(typeof(PlayerProfile), p.Trim(), true)).ToArray();
                    AblationRunner runner = AblationRunner.Run(plan);
                    runner.Completed = r =>
                    {
                        Debug.Log($"Ablation finished — {r.Progress}\n{r.JsonlPath}\n{r.MarkdownPath}\n\n{r.Summarise()}");
                        if (!Application.isBatchMode) return;
#if UNITY_EDITOR
                        UnityEditor.EditorApplication.Exit(0);
#else
                        Application.Quit();
#endif
                    };
                    return;
                }
            }
        }

        static int Int(string[] args, string key, int fallback)
        {
            string s = Str(args, key, null);
            return s != null && int.TryParse(s, out int v) ? v : fallback;
        }

        static string Str(string[] args, string key, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return fallback;
        }
    }
}
