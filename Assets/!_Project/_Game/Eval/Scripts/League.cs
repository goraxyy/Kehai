using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // The league (0.4.0, build plan §10.2, C5.6): one side learns while everyone else is frozen.
    //
    //   -league tools/rl/leagues/vital_r2.json
    //
    // A league file says who learns ("employee", "vital" or "basic"), who they meet and in what
    // shares, and the careers: each is one persona, six shifts, a fresh Ledger. Frozen Karens run
    // from generated C# (or the planner); frozen players are scripted or run a generated mind.
    //
    //   {
    //     "name": "vital_r2", "learner": "vital", "split": "training",
    //     "careers": 100000, "shifts": 6, "start_shift": 1, "shift_seconds": 150,
    //     "players": [ { "kind": "scripted", "profile": "efficient", "share": 1 },
    //                  { "kind": "persona", "share": 2 },               // drawn from the ranges
    //                  { "kind": "employee", "share": 2 },              // a persona, run by EmployeeBrain
    //                  { "kind": "survivor", "share": 0.5 } ],
    //     "karen":   [ { "brain": "planner", "rung": "E", "share": 1 },
    //                  { "brain": "basic", "share": 1 } ],               // frozen: from generated C#
    //     "lessons": [ { "name": "job", "karen": "none" }, ... ]          // the Employee's curriculum
    //   }
    //
    // Nothing held out is ever met in training: the cautious profile, the careful corner (for a
    // Karen learner) and any seed outside the split (EvalSplits). A file that asks for one stops.
    public sealed class LeagueSpec
    {
        public string name = "league";
        public string learner = "employee";          // employee | vital | basic | none (a test run)
        public Split split = Split.Training;
        public int careers = 1, shifts = 6, startShift = 1, seedOffset;
        public float shiftSeconds = 150f;
        public int fps = 15;
        public readonly List<PlayerSpec> players = new List<PlayerSpec>();
        public readonly List<KarenSpec> karen = new List<KarenSpec>();
        public readonly List<LessonSpec> lessons = new List<LessonSpec>();
        // Where personas are drawn from: "ranges": {"low": [14], "high": [14]} (fit_personas.py writes it).
        public PersonaRanges ranges = PersonaRanges.Everything;

        public bool KarenLearns => learner == "vital" || learner == "basic";

        public static LeagueSpec Parse(string json)
        {
            Dictionary<string, object> o = MiniJson.ParseObject(json) ?? throw new System.FormatException("not a JSON object");
            var s = new LeagueSpec
            {
                name = o.GetString("name", "league"),
                learner = o.GetString("learner", "employee").ToLowerInvariant(),
                careers = (int)o.GetNumber("careers", 1),
                shifts = (int)o.GetNumber("shifts", 6),
                startShift = (int)o.GetNumber("start_shift", 1),
                shiftSeconds = (float)o.GetNumber("shift_seconds", 150),
                fps = (int)o.GetNumber("fps", 15),
                seedOffset = (int)o.GetNumber("seed_offset", 0)
            };
            string split = o.GetString("split", "training");
            if (!System.Enum.TryParse(split, true, out s.split) || s.split == Split.Legacy) throw new System.FormatException("split: training, validation or test");
            foreach (object p in List(o, "players")) s.players.Add(PlayerSpec.From((Dictionary<string, object>)p));
            foreach (object k in List(o, "karen")) s.karen.Add(KarenSpec.From((Dictionary<string, object>)k));
            foreach (object l in List(o, "lessons")) s.lessons.Add(LessonSpec.From((Dictionary<string, object>)l));
            if (o.TryGetValue("ranges", out object r) && r is Dictionary<string, object> rd)
            {
                var low = List(rd, "low");
                var high = List(rd, "high");
                if (low.Count != Persona.Count || high.Count != Persona.Count) throw new System.FormatException("ranges: 14 lows and 14 highs");
                for (int i = 0; i < Persona.Count; i++)
                {
                    s.ranges.Low[i] = Mathf.Clamp01(System.Convert.ToSingle(low[i], System.Globalization.CultureInfo.InvariantCulture));
                    s.ranges.High[i] = Mathf.Clamp01(System.Convert.ToSingle(high[i], System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            if (s.players.Count == 0) throw new System.FormatException("a league needs players");
            if (s.karen.Count == 0 && s.lessons.Count == 0) s.karen.Add(new KarenSpec());
            return s;
        }

        static List<object> List(Dictionary<string, object> o, string key) => o.TryGetValue(key, out object v) && v is List<object> l ? l : new List<object>();
    }

    public sealed class PlayerSpec
    {
        public string kind = "scripted";              // scripted | persona | corner | employee | survivor
        public PlayerProfile profile = PlayerProfile.Efficient;
        public string mind = "heuristic";             // heuristic | generated | trainer
        public float share = 1f;

        public static PlayerSpec From(Dictionary<string, object> o)
        {
            var p = new PlayerSpec
            {
                kind = o.GetString("kind", "scripted").ToLowerInvariant(),
                mind = o.GetString("mind", null)?.ToLowerInvariant(),
                share = (float)o.GetNumber("share", 1)
            };
            if (System.Enum.TryParse(o.GetString("profile", "efficient"), true, out PlayerProfile profile)) p.profile = profile;
            if (p.mind == null) p.mind = p.kind == "employee" ? "generated" : "heuristic";
            return p;
        }
    }

    public sealed class KarenSpec
    {
        public string brain = "planner";              // planner | basic | vital | hunter | none
        public string rung = "E";
        public float share = 1f;
        public bool learner;                          // the trainer picks for her (set by the runner)

        public static KarenSpec From(Dictionary<string, object> o) => new KarenSpec
        {
            brain = o.GetString("brain", "planner").ToLowerInvariant(),
            rung = o.GetString("rung", "E"),
            share = (float)o.GetNumber("share", 1)
        };

        public void Apply(KarenConfig c)
        {
            if (KarenBootstrap.TryParseRung(rung, out KarenRung r)) c.rung = r;
            c.hunter = brain == "hunter";
            c.brain = brain == "vital" ? KarenBrainKind.Vital : brain == "basic" ? KarenBrainKind.Basic : KarenBrainKind.Planner;
            c.brainFromTrainer = learner;
        }
    }

    // One lesson of the Employee's curriculum (ML-Agents' environment parameter "lesson").
    public sealed class LessonSpec
    {
        public string name = "lesson";
        public bool noKaren;
        public readonly List<KarenSpec> karen = new List<KarenSpec>();

        public static LessonSpec From(Dictionary<string, object> o)
        {
            var l = new LessonSpec { name = o.GetString("name", "lesson") };
            if (o.TryGetValue("karen", out object k))
            {
                if (k is string none && none == "none") l.noKaren = true;
                else if (k is List<object> list) foreach (object x in list) l.karen.Add(KarenSpec.From((Dictionary<string, object>)x));
            }
            return l;
        }
    }

    // One career as the league draws it.
    public struct LeagueCareer
    {
        public int Index, Seed;
        public Persona Persona;
        public PlayerSpec Player;
        public KarenSpec Karen;      // null: no Karen (the job lesson)
    }

    public static class LeagueDraw
    {
        // The career's seed, persona and opponent. Deterministic in (spec, index); refuses anything
        // held out (EvalSplits), so a wrong file stops the run instead of training on the test.
        public static LeagueCareer Career(LeagueSpec spec, int index, int lesson = -1)
        {
            int seed = EvalSplits.SeedFor(spec.split, (spec.seedOffset + index) % EvalSplits.Span);
            var random = new System.Random(seed * 92821 + 17);
            // The test run's groups are exact (EvalSplits.TestCareersPerGroup each), not drawn:
            // career i plays group i mod the groups, each spec counted share times.
            PlayerSpec player = spec.split == Split.Test ? Stratified(spec.players, index) : Pick(spec.players, random);
            Persona persona = PersonaFor(player, spec, random, index);

            List<KarenSpec> karens = spec.karen;
            bool noKaren = false;
            if (lesson >= 0 && lesson < spec.lessons.Count)
            {
                noKaren = spec.lessons[lesson].noKaren;
                if (spec.lessons[lesson].karen.Count > 0) karens = spec.lessons[lesson].karen;
            }
            KarenSpec karen = noKaren || karens.Count == 0 ? null : Pick(karens, random);
            if (karen != null && spec.KarenLearns && karen.brain == spec.learner)
                karen = new KarenSpec { brain = karen.brain, rung = karen.rung, share = karen.share, learner = true };

            if (spec.split != Split.Test)
            {
                if (spec.KarenLearns) EvalSplits.AssertKarenTraining(seed, persona);
                else if (spec.learner == "employee" && !EvalSplits.PlayersMayTrainOn(seed, persona))
                    throw new System.InvalidOperationException("held out of the players' training: " + EvalSplits.Describe(seed, persona));
            }
            return new LeagueCareer { Index = index, Seed = seed, Persona = persona, Player = player, Karen = karen };
        }

        static Persona PersonaFor(PlayerSpec p, LeagueSpec spec, System.Random random, int index)
        {
            switch (p.kind)
            {
                case "survivor": return Persona.Survivor;
                case "corner": return EvalSplits.DrawCorner(random, "corner-" + index);
                case "persona":
                case "employee":
                    // Karen's side never meets the careful corner in training; the bots' side may.
                    return spec.KarenLearns || spec.split != Split.Training
                        ? EvalSplits.DrawForKaren(random, spec.ranges, p.kind + "-" + index)
                        : Persona.Draw(random, spec.ranges, p.kind + "-" + index);
                default:
                    return Persona.Of(p.profile);
            }
        }

        public static PlayerSpec Stratified(List<PlayerSpec> list, int index)
        {
            var groups = new List<PlayerSpec>();
            foreach (PlayerSpec p in list)
                for (int k = 0; k < Mathf.Max(1, Mathf.RoundToInt(p.share)); k++) groups.Add(p);
            return groups[index % groups.Count];
        }

        static T Pick<T>(List<T> list, System.Random random) where T : class
        {
            float total = 0f;
            foreach (T x in list) total += Share(x);
            double u = random.NextDouble() * total, acc = 0.0;
            foreach (T x in list)
            {
                acc += Share(x);
                if (u < acc) return x;
            }
            return list[list.Count - 1];
        }

        static float Share(object x) => x is PlayerSpec p ? Mathf.Max(0f, p.share) : x is KarenSpec k ? Mathf.Max(0f, k.share) : 1f;
    }

    // Plays the league: career after career, shift after shift.
    public sealed class LeagueRunner : MonoBehaviour
    {
        public LeagueSpec Spec { get; private set; }
        public string Progress { get; private set; } = "starting";
        public bool Finished { get; private set; }
        public System.Action<LeagueRunner> Completed;
        public readonly List<EpisodeMetrics> Results = new List<EpisodeMetrics>();
        public string JsonlPath { get; private set; }

        // The current lesson (ML-Agents curriculum), when a trainer sets one; else -1.
        public static System.Func<int> Lesson;

        public static LeagueRunner Run(LeagueSpec spec)
        {
            KehaiEnv env = KehaiEnv.Ensure();
            var runner = env.gameObject.AddComponent<LeagueRunner>();
            runner.Spec = spec;
            runner.StartCoroutine(runner.Execute(env));
            return runner;
        }

        IEnumerator Execute(KehaiEnv env)
        {
            string dir = KehaiEnv.EvalDirectory;
            Directory.CreateDirectory(dir);
            JsonlPath = Path.Combine(dir, $"league_{Spec.name}_{KehaiEnv.RunStamp}.jsonl");
            var log = new EpisodeLog(JsonlPath);
            IEmployeeMind trainerMind = Spec.learner == "employee" ? EmployeeMinds.Create("trainer") : null;

            for (int career = 0; career < Spec.careers; career++)
            {
                int lesson = Lesson != null ? Lesson() : Spec.lessons.Count > 0 ? 0 : -1;
                LeagueCareer c = LeagueDraw.Career(Spec, career, lesson);
                KarenSpec karen = c.Karen;
                KehaiEnv.KarenSetup = cfg => karen?.Apply(cfg);
                var config = new EnvConfig
                {
                    seed = c.Seed, rung = karen?.rung ?? "C", karen = karen != null && karen.brain != "none",
                    shiftSeconds = Spec.shiftSeconds, fps = Spec.fps, render = false, freshLedger = true,
                    startShift = Spec.startShift, agent = c.Persona.Name, syntheticBlinks = true
                };
                yield return env.ResetEpisode(config);

                var memory = new BotMemory();
                IEmployeeMind mind = c.Player.mind == "trainer" ? trainerMind : EmployeeMinds.Create(c.Player.mind);
                for (int s = 0; s < Spec.shifts; s++)
                {
                    if (!env.Metrics.Started) break;
                    Progress = $"{Spec.name}: career {career + 1}/{Spec.careers}, shift {s + 1}/{Spec.shifts}, {EvalSplits.Describe(c.Seed, c.Persona)} against {karen?.brain ?? "no Karen"}";
                    var bot = new SimulatedPlayer(env, c.Persona, c.Seed * 31 + s, true, memory, mind) { CareerOver = s == Spec.shifts - 1 };
                    yield return bot.PlayShift();
                    EpisodeMetrics m = env.Metrics;
                    if (log.TryAppend(m, out _)) Results.Add(m);
                    if (s < Spec.shifts - 1) yield return env.ClockIn();
                }
                yield return null;   // a frame between careers, whatever happened
            }
            KehaiEnv.KarenSetup = null;
            Progress = $"done: {Results.Count} shifts";
            Finished = true;
            Completed?.Invoke(this);
        }
    }
}
