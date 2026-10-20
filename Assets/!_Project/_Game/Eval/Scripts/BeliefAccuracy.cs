using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Eval
{
    // How well Karen knows where the player is: her belief scored against where they really
    // are. The harness may look at the player; she may not (KarenTests' omniscience check
    // covers her code, not this).
    //
    // Five times a second of sim time the player's true cell is looked up and the belief is
    // scored on it:
    //   log p    the belief's log-probability of the true cell, in nats, floored at 1e-6 —
    //            the log-likelihood a learned motion model would be trained to raise
    //   top-k    is the true cell among the k likeliest (k = 1, 5, 10)?
    //   error    metres from the likeliest cell to the true one
    // split by whether her sight has the player this frame. Out of view is where a belief
    // earns its keep: in view, anything that remembers the last sighting is right.
    //
    // Rungs A and B keep no belief, so they are scored on "last seen": a Gaussian round her
    // last fix on the player (the punch at the time clock, then her sight), widening at a
    // walking pace. The same baseline is scored on every rung, so C–F can also be read
    // against it within the same shift.
    //
    // Out of view is also split by how long it has been since her last fix (0–5, 5–15, 15–45
    // and 45+ s): where evidence is fresh the motion model matters little, and where it is
    // stale it is nearly all there is. Shadow filters (BeliefShadows) are scored on the same
    // samples, so differences between them are paired sample by sample.
    public static class BeliefScore
    {
        public const float ProbabilityFloor = 1e-6f;
        public const float FixSigma = StoreMap.CellSize;   // metres, at the moment of the fix
        public const float SpreadSpeed = 1f;               // metres per second the baseline widens

        public struct Result
        {
            public float LogP;
            public float Top1, Top5, Top10;   // 0..1; ties count as the chance a random order puts the truth inside
            public float Error;               // metres; ties at the top average over the tied cells
        }

        // p: a probability per cell, summing to one. Distances are on the floor plane, cell
        // centre to cell centre, so a top-1 hit is an error of 0.
        public static Result Score(float[] p, Vector3[] cellPosition, int truth)
        {
            float pt = p[truth];
            float max = float.NegativeInfinity;
            int greater = 0, tied = 0;
            for (int c = 0; c < p.Length; c++)
            {
                if (p[c] > pt) greater++;
                else if (p[c] == pt) tied++;
                if (p[c] > max) max = p[c];
            }

            double distance = 0.0;
            int peaks = 0;
            Vector3 at = cellPosition[truth];
            for (int c = 0; c < p.Length; c++)
            {
                if (p[c] != max) continue;
                distance += Flat(cellPosition[c] - at);
                peaks++;
            }

            return new Result
            {
                LogP = Mathf.Log(Mathf.Max(pt, ProbabilityFloor)),
                Top1 = Within(1, greater, tied),
                Top5 = Within(5, greater, tied),
                Top10 = Within(10, greater, tied),
                Error = (float)(distance / peaks)
            };
        }

        static float Within(int k, int greater, int tied) => Mathf.Clamp01((k - greater) / (float)tied);

        static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);

        // The last-seen baseline: as sure as a sighting at the fix, and a metre less sure for
        // every second since. Ranking cells by it is ranking them by distance from the fix, so
        // only its log p depends on the two constants above.
        public static void FillLastSeen(float[] into, Vector3[] cellPosition, Vector3 fix, float secondsSince)
        {
            float sigma = FixSigma + SpreadSpeed * Mathf.Max(0f, secondsSince);
            float inv = 1f / (2f * sigma * sigma);
            double sum = 0.0;
            for (int c = 0; c < into.Length; c++)
            {
                Vector3 d = cellPosition[c] - fix;
                into[c] = Mathf.Exp(-(d.x * d.x + d.z * d.z) * inv);
                sum += into[c];
            }
            if (sum < 1e-30) { FillUniform(into); return; }
            float scale = (float)(1.0 / sum);
            for (int c = 0; c < into.Length; c++) into[c] *= scale;
        }

        public static void FillUniform(float[] into)
        {
            float share = 1f / into.Length;
            for (int c = 0; c < into.Length; c++) into[c] = share;
        }
    }

    // Per-episode means of BeliefScore, sampled during the shift.
    public sealed class BeliefAccuracy
    {
        public const float SampleHz = 5f;
        public static readonly float[] LostEdges = { 5f, 15f, 45f };
        public static readonly string[] LostNames = { "0_5", "5_15", "15_45", "45_up" };

        public static int LostBucket(float secondsSinceFix)
        {
            int k = 0;
            while (k < LostEdges.Length && secondsSinceFix >= LostEdges[k]) k++;
            return k;
        }

        // One source's tallies: in view, out of view, and out of view by time since the fix.
        public sealed class Scores
        {
            public readonly Tally InView = new Tally(), OutOfView = new Tally();
            public readonly Tally[] Lost = { new Tally(), new Tally(), new Tally(), new Tally() };

            public void Add(in BeliefScore.Result r, bool inView, float sinceFix)
            {
                (inView ? InView : OutOfView).Add(r);
                if (!inView) Lost[LostBucket(sinceFix)].Add(r);
            }
        }

        public sealed class Tally
        {
            public int Samples;
            double logP, top1, top5, top10, error;

            public void Add(in BeliefScore.Result r)
            {
                Samples++;
                logP += r.LogP;
                top1 += r.Top1;
                top5 += r.Top5;
                top10 += r.Top10;
                error += r.Error;
            }

            public float LogP => Mean(logP);
            public float Top1 => Mean(top1);
            public float Top5 => Mean(top5);
            public float Top10 => Mean(top10);
            public float Error => Mean(error);

            // NaN when nothing was sampled; MiniJson writes it as null.
            float Mean(double sum) => Samples > 0 ? (float)(sum / Samples) : float.NaN;
        }

        public string Source = "";   // "grid" (rungs C–F) or "last_seen" (A and B)
        public string Motion = "";   // her own grid's motion model
        public int Cells;
        public int OffMap;           // samples where the player wasn't on Karen's map
        public readonly Scores Own = new Scores(), LastSeen = new Scores();
        public Tally InView => Own.InView;
        public Tally OutOfView => Own.OutOfView;
        public Tally LastSeenInView => LastSeen.InView;
        public Tally LastSeenOutOfView => LastSeen.OutOfView;

        // Shadow filters, by motion model id, when the run has them.
        public readonly List<string> ShadowIds = new List<string>();
        public readonly List<Scores> Shadow = new List<Scores>();
        double[] shadowPredictMsAtBegin, shadowEvidenceMsAtBegin;
        int shadowTicksAtBegin;
        public float[] ShadowPredictMs = new float[0], ShadowEvidenceMs = new float[0];
        public int[] ShadowCollapses = new int[0];
        public float OwnPredictMs = float.NaN;
        BeliefGrid ownGridAtBegin;
        double ownMsAtBegin;
        int ownTicksAtBegin;

        float began, timer;
        bool running;
        float[] grid, baseline;
        float[] shadowLogP = new float[0];
        Vector3 lastPlayer;
        float lastPlayerAt = -1f;

        public void Begin()
        {
            began = Time.time;
            timer = 0f;
            running = true;
            lastPlayerAt = -1f;

            KarenBrain brain = KarenBrain.Instance;
            BeliefShadows shadows = BeliefShadows.Current;
            ShadowIds.Clear();
            Shadow.Clear();
            if (brain != null && shadows != null && shadows.Follows(brain))
            {
                foreach (string id in shadows.Ids) { ShadowIds.Add(id); Shadow.Add(new Scores()); }
                shadowPredictMsAtBegin = shadows.PredictMilliseconds.ToArray();
                shadowEvidenceMsAtBegin = shadows.EvidenceMilliseconds.ToArray();
                shadowTicksAtBegin = shadows.Ticks;
            }
            shadowLogP = new float[ShadowIds.Count];
            if (brain != null && brain.Belief != null)
            {
                Motion = brain.Belief.Motion.Id;
                ownGridAtBegin = brain.Belief;
                ownMsAtBegin = brain.Belief.PredictMilliseconds;
                ownTicksAtBegin = brain.Belief.PredictTicks;
            }
        }

        public void End()
        {
            if (!running) return;
            running = false;
            KarenBrain brain = KarenBrain.Instance;
            if (brain != null && brain.Belief != null && brain.Belief.PredictTicks > 0)
            {
                bool same = brain.Belief == ownGridAtBegin;
                double ms = brain.Belief.PredictMilliseconds - (same ? ownMsAtBegin : 0.0);
                int ticks = brain.Belief.PredictTicks - (same ? ownTicksAtBegin : 0);
                OwnPredictMs = ticks > 0 ? (float)(ms / ticks) : float.NaN;
            }
            BeliefShadows shadows = BeliefShadows.Current;
            int n = ShadowIds.Count;
            ShadowPredictMs = new float[n];
            ShadowEvidenceMs = new float[n];
            ShadowCollapses = new int[n];
            if (shadows != null && n > 0 && shadows.Ids.Count == n)
            {
                int ticks = Mathf.Max(1, shadows.Ticks - shadowTicksAtBegin);
                for (int i = 0; i < n; i++)
                {
                    ShadowPredictMs[i] = (float)((shadows.PredictMilliseconds[i] - shadowPredictMsAtBegin[i]) / ticks);
                    ShadowEvidenceMs[i] = (float)((shadows.EvidenceMilliseconds[i] - shadowEvidenceMsAtBegin[i]) / ticks);
                    ShadowCollapses[i] = i < shadows.Grids.Count ? shadows.Grids[i].Collapses : 0;
                }
            }
        }

        public void Tick(float dt, Vector3 player)
        {
            if (!running) return;
            timer += dt;
            const float period = 1f / SampleHz;
            if (timer < period - 1e-4f) return;
            timer = Mathf.Max(0f, timer - period);

            KarenBrain brain = KarenBrain.Instance;
            if (brain == null || !brain.ShiftActive || brain.Map == null || brain.Body == null) return;
            Sample(brain, player);
        }

        void Sample(KarenBrain brain, Vector3 player)
        {
            StoreMap map = brain.Map;
            int truth = map.CellAt(player);
            if (truth < 0) { OffMap++; return; }

            int n = map.CellCount;
            Cells = n;
            if (baseline == null || baseline.Length != n)
            {
                baseline = new float[n];
                grid = new float[n];
            }

            SightSensor sight = brain.Body.Sight;
            bool inView = sight.SeesNow;

            // Her last fix: a sighting this shift, else the punch at the time clock.
            Landmark? clock = map.FindLandmark(LandmarkKind.TimeClock);
            float sinceFix = sight.LastSeenTime >= began ? Time.time - sight.LastSeenTime : Time.time - began;
            if (sight.LastSeenTime >= began)
                BeliefScore.FillLastSeen(baseline, map.CellPosition, sight.LastSeenPosition, Time.time - sight.LastSeenTime);
            else if (clock.HasValue)
                BeliefScore.FillLastSeen(baseline, map.CellPosition, clock.Value.Position, Time.time - began);
            else
                BeliefScore.FillUniform(baseline);
            BeliefScore.Result lastSeen = BeliefScore.Score(baseline, map.CellPosition, truth);
            LastSeen.Add(lastSeen, inView, sinceFix);

            BeliefScore.Result own = lastSeen;
            Source = "last_seen";
            BeliefGrid belief = brain.Belief;
            if (brain.config.Features.Belief && belief != null && belief.CellCount == n)
            {
                for (int c = 0; c < n; c++) grid[c] = belief[c];
                own = BeliefScore.Score(grid, map.CellPosition, truth);
                Source = "grid";
            }
            Own.Add(own, inView, sinceFix);

            // The shadows, on the same sample.
            BeliefShadows shadows = BeliefShadows.Current;
            if (ShadowIds.Count > 0 && shadows != null && shadows.Follows(brain))
            {
                for (int i = 0; i < ShadowIds.Count && i < shadows.Grids.Count; i++)
                {
                    BeliefGrid g = shadows.Grids[i];
                    if (g.CellCount != n) continue;
                    for (int c = 0; c < n; c++) grid[c] = g[c];
                    BeliefScore.Result r = BeliefScore.Score(grid, map.CellPosition, truth);
                    Shadow[i].Add(r, inView, sinceFix);
                    shadowLogP[i] = r.LogP;
                }
            }

            // The trace gets the truth and every grid's score of it: the replay's target.
            Vector2 velocity = Vector2.zero;
            if (lastPlayerAt >= 0f && Time.time > lastPlayerAt)
                velocity = new Vector2(player.x - lastPlayer.x, player.z - lastPlayer.z) / (Time.time - lastPlayerAt);
            lastPlayer = player;
            lastPlayerAt = Time.time;
            BeliefTrace.Current?.Sample(truth, inView, sinceFix, velocity, own.LogP, shadowLogP, lastSeen.LogP);
        }

        public void AddTo(Dictionary<string, object> d)
        {
            d["belief_source"] = Source;
            d["belief_motion"] = Motion;
            d["belief_cells"] = Cells;
            d["belief_off_map_samples"] = OffMap;
            d["belief_in_view_samples"] = InView.Samples;
            d["belief_out_of_view_samples"] = OutOfView.Samples;
            d["belief_ms_per_tick"] = OwnPredictMs;
            Put(d, "belief_in_view", InView);
            Put(d, "belief_out_of_view", OutOfView);
            Put(d, "last_seen_in_view", LastSeenInView);
            Put(d, "last_seen_out_of_view", LastSeenOutOfView);
            PutLost(d, "belief", Own);
            PutLost(d, "last_seen", LastSeen);

            d["belief_shadows"] = string.Join(",", ShadowIds);
            for (int i = 0; i < ShadowIds.Count; i++)
            {
                string key = "shadow_" + ShadowIds[i];
                Put(d, key + "_in_view", Shadow[i].InView);
                Put(d, key + "_out_of_view", Shadow[i].OutOfView);
                PutLost(d, key, Shadow[i]);
                if (i < ShadowPredictMs.Length)
                {
                    d[key + "_ms_predict"] = ShadowPredictMs[i];
                    d[key + "_ms_evidence"] = ShadowEvidenceMs[i];
                    d[key + "_collapses"] = ShadowCollapses[i];
                }
            }
        }

        static void PutLost(Dictionary<string, object> d, string prefix, Scores s)
        {
            for (int k = 0; k < LostNames.Length; k++)
            {
                Put(d, $"{prefix}_lost_{LostNames[k]}", s.Lost[k]);
                d[$"{prefix}_lost_{LostNames[k]}_samples"] = s.Lost[k].Samples;
            }
        }

        static void Put(Dictionary<string, object> d, string prefix, Tally t)
        {
            d[prefix + "_logp"] = t.LogP;
            d[prefix + "_top1"] = t.Top1;
            d[prefix + "_top5"] = t.Top5;
            d[prefix + "_top10"] = t.Top10;
            d[prefix + "_err_m"] = t.Error;
        }
    }
}
