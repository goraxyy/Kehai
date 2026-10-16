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
        public int Cells;
        public int OffMap;           // samples where the player wasn't on Karen's map
        public readonly Tally InView = new Tally(), OutOfView = new Tally();
        public readonly Tally LastSeenInView = new Tally(), LastSeenOutOfView = new Tally();

        float began, timer;
        bool running;
        float[] grid, baseline;

        public void Begin()
        {
            began = Time.time;
            timer = 0f;
            running = true;
        }

        public void End() => running = false;

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
            if (sight.LastSeenTime >= began)
                BeliefScore.FillLastSeen(baseline, map.CellPosition, sight.LastSeenPosition, Time.time - sight.LastSeenTime);
            else if (clock.HasValue)
                BeliefScore.FillLastSeen(baseline, map.CellPosition, clock.Value.Position, Time.time - began);
            else
                BeliefScore.FillUniform(baseline);
            BeliefScore.Result lastSeen = BeliefScore.Score(baseline, map.CellPosition, truth);
            (inView ? LastSeenInView : LastSeenOutOfView).Add(lastSeen);

            BeliefScore.Result own = lastSeen;
            Source = "last_seen";
            BeliefGrid belief = brain.Belief;
            if (brain.config.Features.Belief && belief != null && belief.CellCount == n)
            {
                for (int c = 0; c < n; c++) grid[c] = belief[c];
                own = BeliefScore.Score(grid, map.CellPosition, truth);
                Source = "grid";
            }
            (inView ? InView : OutOfView).Add(own);
        }

        public void AddTo(Dictionary<string, object> d)
        {
            d["belief_source"] = Source;
            d["belief_cells"] = Cells;
            d["belief_off_map_samples"] = OffMap;
            d["belief_in_view_samples"] = InView.Samples;
            d["belief_out_of_view_samples"] = OutOfView.Samples;
            Put(d, "belief_in_view", InView);
            Put(d, "belief_out_of_view", OutOfView);
            Put(d, "last_seen_in_view", LastSeenInView);
            Put(d, "last_seen_out_of_view", LastSeenOutOfView);
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
