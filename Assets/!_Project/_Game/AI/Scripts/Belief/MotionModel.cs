using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // The predict half of her belief filter (0.4.0, build plan §8, C4.1): how she thinks a person
    // moves between two looks. Carried over from PR #68 onto today's map: the grid keeps the outside
    // cell, its exit lines, the leak back to the prior and the bookkeeping; a model only says how
    // mass moves between her cells, and how much leaves by each exit. Mass moves only along walkable
    // edges, and none is made or lost, so the evidence half never needs to know which model runs.
    //
    //   id   model                                                              file
    //   p0   today's hand-written step (HandMotion.Today), the same arithmetic   here
    //   p1   the hand step, its gains fitted, keeping away from her (Danger)     here
    //   p2   destinations, learned from everyone                                 DestinationMotion.cs
    //   p3   p2, reading your Ledger                                             DestinationMotion.cs
    public interface IMotionModel
    {
        string Id { get; }

        // One step: from her cells' mass `from`, add into `to` (already cleared) what stays and what
        // moves, and into `outflow` (per exit, already cleared; null with no outside) what leaves.
        void Spread(BeliefGrid grid, float[] from, float[] to, float[] outflow, float dt, float speed);
    }

    // The hand-written step. With today's gains (Today) it is the code that shipped, moved here
    // unchanged: the same arithmetic in the same order, so its numbers are bit for bit what they
    // were (MotionModelTests holds a frozen copy of the old code to check). With other gains it is
    // P1, and a filled Danger layer keeps it away from her.
    public sealed class HandMotion : IMotionModel
    {
        public static readonly HandMotion Today = new HandMotion("p0", null);

        public readonly string id;
        // P1's fitted gains; null: the grid's own (today's, or what KarenConfig set on it).
        public readonly Gains? Fitted;

        public struct Gains
        {
            public float MovingProbability, DesireGain, HabitGain, DangerAvoidance, SpeedScale;
        }

        public HandMotion(string id, Gains? fitted)
        {
            this.id = id;
            Fitted = fitted;
        }

        public string Id => id;

        public void Spread(BeliefGrid grid, float[] b, float[] scratch, float[] inflow, float dt, float speed)
        {
            StoreMap map = grid.Map;
            float moving = Fitted?.MovingProbability ?? grid.MovingProbability;
            float desireGain = Fitted?.DesireGain ?? grid.DesireGain;
            float habitGain = Fitted?.HabitGain ?? grid.HabitGain;
            float dangerAvoidance = Fitted?.DangerAvoidance ?? grid.DangerAvoidance;
            if (Fitted.HasValue) speed *= Fitted.Value.SpeedScale;
            float mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * moving;
            float[] desire = grid.DesireDistance, habit = grid.HabitPrior, danger = grid.Danger;

            for (int c = 0; c < b.Length; c++)
            {
                if (!grid.IsHers(c)) continue;            // the outside moves in its exit lines
                float mass = b[c];
                if (mass <= 0f) continue;

                int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
                if (start == end) { scratch[c] += mass; continue; }

                float leaving = mass * mobility;
                scratch[c] += mass - leaving;

                // Weight each neighbour by whether it takes you closer to unfinished work,
                // toward places you habitually go, and away from where she has been.
                float total = 0f;
                float here = desire[c];
                for (int e = start; e < end; e++)
                    total += Weight(desire, habit, danger, desireGain, habitGain, dangerAvoidance, here, map.EdgeTo[e]);

                for (int e = start; e < end; e++)
                {
                    int n = map.EdgeTo[e];
                    float share = leaving * Weight(desire, habit, danger, desireGain, habitGain, dangerAvoidance, here, n) / total;
                    if (grid.IsHers(n)) scratch[n] += share;
                    else inflow[grid.ExitIndexOf(c)] += share;   // out through this exit
                }
            }
        }

        static float Weight(float[] desire, float[] habit, float[] danger, float desireGain, float habitGain, float dangerAvoidance, float hereDesire, int n)
        {
            float w = 1f;
            float there = desire[n];
            // A step that closes the distance to work is favoured, one that opens it isn't.
            if (!float.IsInfinity(hereDesire) && !float.IsInfinity(there))
                w *= 1f + desireGain * 0.5f * Mathf.Clamp((hereDesire - there) / StoreMap.CellSize, -1f, 1f);
            w *= 1f + habitGain * habit[n];
            w *= 1f - dangerAvoidance * Mathf.Clamp01(danger[n]);
            return Mathf.Max(0.02f, w);
        }
    }

    public static class MotionModels
    {
        public static readonly string[] Ladder = { "p0", "p1", "p2", "p3" };

        // "p0" (or "hand", or empty) is today's step. Null for an id it doesn't know.
        public static IMotionModel Create(string id, MotionFeatures features)
        {
            switch (Canonical(id))
            {
                case "p0": return HandMotion.Today;
                case "p1": return new HandMotion("p1", PredictionWeights.Current.P1Gains);
                case "p2": return new DestinationMotion("p2", features, readLedger: false);
                case "p3": return new DestinationMotion("p3", features, readLedger: true);
                default: return null;
            }
        }

        public static string Canonical(string id)
        {
            string s = (id ?? "p0").Trim().ToLowerInvariant();
            return s == "" || s == "hand" || s == "m0" ? "p0" : s == "m1" ? "p1" : s;
        }
    }
}
