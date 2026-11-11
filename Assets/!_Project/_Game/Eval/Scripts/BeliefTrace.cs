using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // The belief trace (0.4.0, build plan §8.5–8.7, C4.2): everything the Python replay
    // (tools/belief/replay.py) needs to rebuild her belief tick for tick, on today's map (her
    // cells, the outside cell and its exits), plus the answers a fit needs: where the player
    // really was, where they were heading, what they could sense of her.
    //
    //   -belief-trace <folder>
    //
    //   <folder>/map.json                 the map and the grid's constants, once a run
    //   <folder>/trace_<seed>_<shift>_<agent>.jsonl, one op a line:
    //     start, reset, layers (desire sources, habit), obs (as applied, with its raw inputs), sweep,
    //     bias, features (the destinations' inputs, every 2 s), predict, check (the whole belief,
    //     every 25 predicts: the parity gate), truth (every predict: the player's cell, destination,
    //     what they sensed of her)
    // The truth lines are for training and judging only: the game never reads them back.
    public sealed class BeliefTrace
    {
        public static string Folder;            // from -belief-trace; null: off
        public const int CheckEvery = 25;

        StreamWriter w;
        KarenBrain brain;
        int predicts;
        int desireHash = int.MinValue, habitHash = int.MinValue;
        float featuresAt = -999f;

        public static bool On => !string.IsNullOrEmpty(Folder);

        public void Begin(KarenBrain b, int seed, int shift, string agent)
        {
            End();
            if (!On || b == null) return;
            brain = b;
            KarenBrain.FeaturesWanted = true;
            Directory.CreateDirectory(Folder);
            string map = Path.Combine(Folder, "map.json");
            if (!File.Exists(map)) File.WriteAllText(map, MapJson(b));
            w = new StreamWriter(Path.Combine(Folder, $"trace_{seed}_{shift}_{agent}.jsonl"), false, new UTF8Encoding(false));
            Line("start", $"\"seed\":{seed},\"shift\":{shift},\"agent\":{MiniJson.Escape(agent)},\"motion\":{MiniJson.Escape(b.config.motion)},\"senses\":{MiniJson.Escape(b.config.senses)},\"mirror\":\"{b.config.mirror}\",\"cells\":{b.Map.CellCount}");
            b.Belief.Applied += OnApplied;
            b.Belief.Swept += OnSwept;
            b.Belief.Biased += OnBiased;
            b.Belief.Predicted += OnPredicted;
            b.Belief.Predicting += OnPredicting;
            b.Belief.WasResetTo += OnReset;
            b.Sensed += OnSensed;
            predicts = 0;
            desireHash = habitHash = int.MinValue;
            Layers();
            Check();
        }

        public void End()
        {
            if (brain != null && brain.Belief != null)
            {
                brain.Belief.Applied -= OnApplied;
                brain.Belief.Swept -= OnSwept;
                brain.Belief.Biased -= OnBiased;
                brain.Belief.Predicted -= OnPredicted;
                brain.Belief.Predicting -= OnPredicting;
                brain.Belief.WasResetTo -= OnReset;
                brain.Sensed -= OnSensed;
            }
            w?.Flush();
            w?.Dispose();
            w = null;
            brain = null;
        }

        static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", CultureInfo.InvariantCulture);

        void Line(string op, string body) => w?.WriteLine("{\"op\":\"" + op + "\",\"t\":" + N(Time.time) + (body.Length > 0 ? "," + body : "") + "}");

        SenseRaw nextRaw;
        bool hasRaw;

        void OnSensed(Observation o, SenseRaw raw) { nextRaw = raw; hasRaw = true; }

        void OnApplied(Observation o)
        {
            Layers();
            var sb = new StringBuilder();
            sb.Append("\"ch\":\"").Append(o.Channel).Append("\",\"x\":").Append(N(o.Position.x)).Append(",\"y\":").Append(N(o.Position.y)).Append(",\"z\":").Append(N(o.Position.z))
              .Append(",\"conf\":").Append(N(o.Confidence)).Append(",\"sigma\":").Append(N(o.Sigma)).Append(",\"hx\":").Append(N(o.Heading.x)).Append(",\"hz\":").Append(N(o.Heading.z))
              .Append(",\"ts\":").Append(N(o.Timestamp)).Append(",\"outside\":").Append(brain.Belief.IsOutside(o.Position) ? "true" : "false")
              .Append(",\"cell\":").Append(brain.Map.CellAt(o.Position));
            if (o.Channel == SenseChannel.Trace)
            {
                // A footprint says where you went: the bump's centre, as BeliefGrid.Apply moved it.
                Vector3 centre = o.Position;
                if (o.Heading.sqrMagnitude > 0.01f) centre += o.Heading.normalized * Mathf.Min(6f, 1.5f + (Time.time - o.Timestamp) * 1.2f);
                sb.Append(",\"cx\":").Append(N(centre.x)).Append(",\"cz\":").Append(N(centre.z)).Append(",\"centre_cell\":").Append(brain.Map.CellAt(centre));
            }
            if (hasRaw)
                sb.Append(",\"raw\":{\"kind\":").Append(nextRaw.Kind).Append(",\"a\":").Append(N(nextRaw.Awareness)).Append(",\"d\":").Append(N(nextRaw.D))
                  .Append(",\"carry\":").Append(N(nextRaw.Carry)).Append(",\"straight\":").Append(N(nextRaw.Straight)).Append(",\"age\":").Append(N(nextRaw.Age))
                  .Append(",\"fresh\":").Append(N(nextRaw.Freshness)).Append(",\"dark\":").Append(nextRaw.LightsOut ? "true" : "false").Append('}');
            hasRaw = false;
            Line("obs", sb.ToString());
        }

        void OnSwept(List<int> cells, List<float> p) =>
            Line("sweep", "\"cells\":[" + string.Join(",", cells) + "],\"p\":[" + string.Join(",", p.Select(N)) + "]");

        void OnBiased(int region, float amount) => Line("bias", $"\"region\":{region},\"amount\":{N(amount)}");

        void OnReset(Vector3 at, float sigma)
        {
            Layers();
            Line("reset", $"\"x\":{N(at.x)},\"y\":{N(at.y)},\"z\":{N(at.z)},\"sigma\":{N(sigma)},\"outside\":{(sigma > 0f && brain.Belief.IsOutside(at) ? "true" : "false")}");
        }

        // The layers and the destinations as this step will use them, then the step.
        void OnPredicting()
        {
            Layers();
            Features();
        }

        void OnPredicted(float dt, float speed)
        {
            Line("predict", $"\"dt\":{N(dt)},\"speed\":{N(speed)}");
            predicts++;
            if (predicts % CheckEvery == 0) Check();
        }

        // The desire field's sources and the habit layer, whenever they change.
        void Layers()
        {
            if (brain == null) return;
            int dh = Hash(brain.DesireTargets);
            if (dh != desireHash)
            {
                desireHash = dh;
                Line("layers", "\"desire_sources\":[" + string.Join(",", brain.DesireTargets) + "]");
            }
            float[] habit = brain.Belief.HabitPrior;
            int hh = 17;
            for (int i = 0; i < habit.Length; i++) if (habit[i] != 0f) hh = hh * 31 + i * 7 + habit[i].GetHashCode();
            if (hh != habitHash)
            {
                habitHash = hh;
                var sb = new StringBuilder("\"habit\":{");
                bool first = true;
                for (int i = 0; i < habit.Length; i++)
                {
                    if (habit[i] == 0f) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(i).Append("\":").Append(N(habit[i]));
                }
                Line("layers", sb.Append('}').ToString());
            }
        }

        static int Hash(List<int> xs)
        {
            int h = 19;
            foreach (int x in xs) h = h * 31 + x;
            return h;
        }

        // The destinations' inputs, each time they're refreshed (every 2 s).
        void Features()
        {
            MotionFeatures f = brain.Features;
            if (f == null || f.RefreshedAt == featuresAt) return;
            featuresAt = f.RefreshedAt;
            var sb = new StringBuilder();
            sb.Append("\"her\":[").Append(N(f.HerPosition.x)).Append(',').Append(N(f.HerPosition.z)).Append("],\"thought\":[").Append(N(f.SheIsThought.x)).Append(',').Append(N(f.SheIsThought.z))
              .Append("],\"seen\":[").Append(N(f.LastSeenAt.x)).Append(',').Append(N(f.LastSeenAt.z)).Append("],\"heading\":[").Append(N(f.LastSeenHeading.x)).Append(',').Append(N(f.LastSeenHeading.z))
              .Append("],\"time_left\":").Append(N(f.TimeLeft01)).Append(",\"open\":").Append(f.StoreOpen ? "true" : "false").Append(",\"places\":[");
            for (int i = 0; i < f.Count; i++)
            {
                Destination d = f.List[i];
                if (i > 0) sb.Append(',');
                float answer = d.AnswerSeconds, habit = d.Habit;
                sb.Append("{\"kind\":\"").Append(d.Kind).Append("\",\"cell\":").Append(d.Cell).Append(",\"waited\":").Append(N(d.Waited))
                  .Append(",\"x\":").Append(N(d.Position.x)).Append(",\"z\":").Append(N(d.Position.z)).Append(",\"answer_s\":").Append(N(answer)).Append(",\"habit\":").Append(N(habit)).Append('}');
            }
            Line("features", sb.Append(']').ToString());
        }

        float HabitOf(string region)
        {
            foreach (Region r in brain.Map.Regions) if (r.Name == region) return brain.Ledger.Habit(r);
            return 0f;
        }

        void Check()
        {
            float[] b = brain.Belief.Snapshot();
            Line("check", "\"b\":[" + string.Join(",", b.Select(N)) + "]");
        }

        // Every eval tick isn't needed: one truth line per predict is enough (10 a second).
        int truthPredicts = -1;

        public void Tick(Vector3 player, Vector3? destination)
        {
            if (w == null || brain == null || truthPredicts == predicts) return;
            truthPredicts = predicts;
            PlayerSenses.Sense(PlayerPresence.Current, brain, out bool saw, out bool heard);
            Line("truth", $"\"cell\":{brain.Map.CellAt(player)},\"x\":{N(player.x)},\"z\":{N(player.z)}," +
                          $"\"dest\":{(destination.HasValue ? $"[{N(destination.Value.x)},{N(destination.Value.z)}]" : "null")}," +
                          $"\"karen\":[{N(brain.Body.Position.x)},{N(brain.Body.Position.z)}],\"sees\":{(brain.Body.Sight.Awareness >= brain.Body.Sight.seeAt ? "true" : "false")}," +
                          $"\"saw\":{(saw ? "true" : "false")},\"heard\":{(heard ? "true" : "false")}");
        }

        // The map and the grid's constants, as the replay needs them.
        public static string MapJson(KarenBrain b)
        {
            StoreMap m = b.Map;
            BeliefGrid g = b.Belief;
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"cell_size\":").Append(N(StoreMap.CellSize)).Append(",\"cells\":").Append(m.CellCount)
              .Append(",\"x\":[").Append(string.Join(",", m.CellPosition.Select(p => N(p.x)))).Append("],\"z\":[").Append(string.Join(",", m.CellPosition.Select(p => N(p.z))))
              .Append("],\"region\":[").Append(string.Join(",", m.CellRegion)).Append("],\"regions\":").Append(m.Regions.Count)
              .Append(",\"hers\":[").Append(string.Join(",", Enumerable.Range(0, m.CellCount).Select(c => g.IsHers(c) ? "1" : "0")))
              .Append("],\"edge_start\":[").Append(string.Join(",", m.EdgeStart)).Append("],\"edge_to\":[").Append(string.Join(",", m.EdgeTo))
              .Append("],\"edge_length\":[").Append(string.Join(",", m.EdgeLength.Select(N))).Append("],\"outside_cell\":").Append(g.OutsideCell)
              .Append(",\"exits\":[").Append(string.Join(",", Enumerable.Range(0, g.ExitCount).Select(k => "[" + string.Join(",", g.ExitBorder(k)) + "]")))
              .Append("],\"exit_delay\":[").Append(string.Join(",", Enumerable.Range(0, g.ExitCount).Select(k => "[" + string.Join(",", Enumerable.Range(0, g.ExitCount).Select(j => N(g.ExitDelay(k, j)))) + "]")))
              .Append("],\"floor\":").Append(N(g.Floor)).Append(",\"moving\":").Append(N(g.MovingProbability)).Append(",\"desire_gain\":").Append(N(g.DesireGain))
              .Append(",\"habit_gain\":").Append(N(g.HabitGain)).Append(",\"danger_avoidance\":").Append(N(g.DangerAvoidance)).Append(",\"uncertainty\":").Append(N(g.Uncertainty))
              .Append(",\"outside_return\":").Append(N(g.OutsideReturn)).Append(",\"outside_prior\":").Append(N(g.OutsidePrior))
              .Append(",\"line_seconds\":").Append(BeliefGrid.ExitLineSeconds).Append(",\"outside_cells\":").Append(g.OutsideCellCount).Append('}');
            return sb.ToString();
        }
    }
}
