using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // P2 and P3 (0.4.0, build plan §8.3, C4.4): the destination first, then the walk. Every cell's
    // mass flows along the shortest ways to the places you might be heading, in the shares her
    // model gives each from there (MotionFeatures), at its mix of speeds, bent away from where you
    // think she is. Some stays put, for working and hiding. P3 is P2 reading your Ledger (how soon
    // you answer each chore, your hiding spots). Mass moves only along walkable edges and out by
    // the exits, and none is made or lost.
    public sealed class DestinationMotion : IMotionModel
    {
        readonly string id;
        readonly MotionFeatures features;
        public readonly bool ReadsLedger;

        public DestinationMotion(string id, MotionFeatures features, bool readLedger)
        {
            this.id = id;
            this.features = features;
            ReadsLedger = readLedger;
            if (features != null && readLedger) features.ReadLedger = true;
        }

        public string Id => id;
        public MotionFeatures Features => features;

        public void Spread(BeliefGrid grid, float[] b, float[] to, float[] outflow, float dt, float speed)
        {
            StoreMap map = grid.Map;
            float[] rate = features?.EdgeRate, exitRate = features?.ExitRate;
            if (rate == null || rate.Length != map.EdgeTo.Length || exitRate.Length != b.Length)
            {
                // Not refreshed yet (the first ticks of a shift): today's step.
                HandMotion.Today.Spread(grid, b, to, outflow, dt, speed);
                return;
            }
            for (int c = 0; c < b.Length; c++)
            {
                if (!grid.IsHers(c)) continue;
                float mass = b[c];
                if (mass <= 0f) continue;
                int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
                float total = exitRate[c];
                for (int e = start; e < end; e++) total += rate[e];
                float leaving = Mathf.Min(0.8f, total * dt);
                if (total <= 0f || leaving <= 0f) { to[c] += mass; continue; }
                float scale = mass * leaving / total;
                to[c] += mass * (1f - leaving);
                for (int e = start; e < end; e++)
                {
                    if (rate[e] <= 0f) continue;
                    int n = map.EdgeTo[e];
                    if (grid.IsHers(n)) to[n] += rate[e] * scale;
                    else if (outflow != null) outflow[grid.ExitIndexOf(c)] += rate[e] * scale;
                    else to[c] += rate[e] * scale;
                }
                if (exitRate[c] > 0f)
                {
                    int exit = grid.ExitIndexOf(c);
                    if (outflow != null && exit >= 0) outflow[exit] += exitRate[c] * scale;
                    else to[c] += exitRate[c] * scale;
                }
            }
        }
    }
}
