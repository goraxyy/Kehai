using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // The predict half of Karen's belief filter (Karen.md §4.2): how she thinks a person moves
    // between two looks. Behind one interface so the hand-written step and the learned ones
    // (LearnedMotion.cs) can be swapped, and compared on the same evidence (Eval/BeliefShadows).
    //
    // A model only says how mass moves: from each state, how much stays and how much goes
    // along each walkable edge of the store map. Mass moves only along edges and the total is
    // kept, so the evidence half of the filter never needs to know which model runs.
    //
    //   id    model                                                        state
    //   m0    the shipped hand step ("hand"), exactly as it always was     cell
    //   m1    the hand step with its gains tuned on data, plus keeping     cell
    //         away from her (fills the Danger layer from where she is)
    //   m2    learned, log-linear in the features (MotionFeatures)         cell
    //   m3    m2 plus a small MLP per cell                                 cell
    //   m4    m3 with a heading: still, or one of eight directions         cell × 9
    public interface IMotionModel
    {
        string Id { get; }
        int Headings { get; }             // 1 = position only; 9 = still + 8 directions
        float Leak { get; }               // share of belief per second handed back to the prior
        float[] HeadingPrior { get; }     // how belief over a cell splits over headings (Headings > 1)

        // to = T · from, over cells × headings. `to` is overwritten.
        void Transition(BeliefGrid grid, float[] from, float[] to, float dt, float speed);
    }

    public static class MotionModels
    {
        public const string Shipped = "hand";
        public static readonly string[] Ladder = { "m0", "m1", "m2", "m3", "m4" };

        // "hand" and "m0" are the same model. "m3/8" is m3 with its MLP refreshed in 8 slices,
        // one per believe tick, to spread the cost (LearnedMotion). Null for an id it doesn't know.
        public static IMotionModel Create(string id, MotionFeatures features)
        {
            string s = Canonical(id);
            int slices = 1;
            int slash = s.IndexOf('/');
            if (slash > 0)
            {
                if (!int.TryParse(s.Substring(slash + 1), out slices) || slices < 1) return null;
                s = s.Substring(0, slash);
            }
            if (s != "m0" && features != null) features.Wanted = true;
            switch (s)
            {
                case "m0": return slash > 0 ? null : new HandMotion();
                case "m1": return slash > 0 ? null : new HandMotion(MotionWeights.M1Gains, features);
                case "m2": return new LearnedMotion(LearnedMotion.Kind.LogLinear, features, null, slices);
                case "m3": return new LearnedMotion(LearnedMotion.Kind.Mlp, features, null, slices);
                case "m4": return new LearnedMotion(LearnedMotion.Kind.Heading, features, null, slices);
                default: return null;
            }
        }

        public static string Canonical(string id)
        {
            string s = (id ?? Shipped).Trim().ToLowerInvariant();
            if (s.EndsWith("/1")) s = s.Substring(0, s.Length - 2);
            return s == "" || s == "hand" ? "m0" : s;
        }

        // For the eval's jsonl keys: "m4/8" → "m4s8".
        public static string Key(string id) => Canonical(id).Replace('/', 's');
    }

    // The hand-written step. With the shipped gains and no features (m0) it is the code that
    // shipped, moved here unchanged: the same arithmetic in the same order, so its numbers are
    // bit for bit what they were. With features (m1) it also keeps away from Karen.
    public sealed class HandMotion : IMotionModel
    {
        [System.Serializable]
        public struct Gains
        {
            public float MovingProbability;   // share of time a person is walking
            public float DesireGain;          // pull towards unfinished work
            public float HabitGain;           // pull towards where this player tends to be
            public float DangerAvoidance;     // push away from Danger
            public float Uncertainty;         // the leak back to the prior, per second
            public float SpeedScale;          // m1: how fast she thinks you move, against her belief
            public float DangerRadius;        // m1: metres of walking distance her presence is felt over
            public float TrailWeight;         // m1: how much where she has just been counts as danger

            public static Gains Shipped => new Gains
            {
                MovingProbability = 0.65f, DesireGain = 0.9f, HabitGain = 1.2f, DangerAvoidance = 0.6f,
                Uncertainty = 0.015f, SpeedScale = 1f, DangerRadius = 6f, TrailWeight = 0.5f
            };
        }

        public readonly Gains G;
        readonly MotionFeatures features;     // null for m0: Danger is left as the brain has it
        int dangerFor = -1, dangerMap = -1;
        float[] dangerFilled;

        public HandMotion() : this(Gains.Shipped, null) { }

        public HandMotion(Gains gains, MotionFeatures features)
        {
            G = gains;
            this.features = features;
        }

        public string Id => features == null ? "m0" : "m1";
        public int Headings => 1;
        public float Leak => G.Uncertainty;
        public float[] HeadingPrior => null;

        // One step of diffusion. `speed` is how fast Karen believes you move on average.
        public void Transition(BeliefGrid grid, float[] from, float[] to, float dt, float speed)
        {
            StoreMap map = grid.Map;
            float[] b = from, scratch = to;
            float mobility;
            if (features == null)
            {
                mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * G.MovingProbability;
            }
            else
            {
                FillDanger(grid);
                mobility = Mathf.Clamp(speed * G.SpeedScale * dt / StoreMap.CellSize, 0f, 0.8f) * G.MovingProbability;
            }
            System.Array.Clear(scratch, 0, scratch.Length);

            for (int c = 0; c < b.Length; c++)
            {
                float mass = b[c];
                if (mass <= 0f) continue;

                int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
                if (start == end) { scratch[c] += mass; continue; }

                float leaving = mass * mobility;
                scratch[c] += mass - leaving;

                // Weight each neighbour by whether it takes you closer to unfinished work,
                // toward places you habitually go, and away from where she has been.
                float total = 0f;
                float here = grid.DesireDistance[c];
                for (int e = start; e < end; e++)
                    total += Weight(grid, here, map.EdgeTo[e]);

                for (int e = start; e < end; e++)
                    scratch[map.EdgeTo[e]] += leaving * Weight(grid, here, map.EdgeTo[e]) / total;
            }
        }

        float Weight(BeliefGrid grid, float hereDesire, int n)
        {
            float w = 1f;
            float there = grid.DesireDistance[n];
            // A step that closes the distance to work is favoured, one that opens it isn't.
            if (!float.IsInfinity(hereDesire) && !float.IsInfinity(there))
                w *= 1f + G.DesireGain * 0.5f * Mathf.Clamp((hereDesire - there) / StoreMap.CellSize, -1f, 1f);
            w *= 1f + G.HabitGain * grid.HabitPrior[n];
            w *= 1f - G.DangerAvoidance * Mathf.Clamp01(grid.Danger[n]);
            return Mathf.Max(0.02f, w);
        }

        // m1's danger: near her by walking distance, or where she has just walked.
        void FillDanger(BeliefGrid grid)
        {
            if (dangerFor == features.FastVersion && dangerMap == features.MapVersion && dangerFilled == grid.Danger) return;
            dangerFor = features.FastVersion;
            dangerMap = features.MapVersion;
            dangerFilled = grid.Danger;
            if (features.KarenDistance == null || features.KarenDistance.Length != grid.Danger.Length) return;
            for (int c = 0; c < grid.Danger.Length; c++)
                grid.Danger[c] = Danger(features.KarenDistance[c], features.Trail[c], G.DangerRadius, G.TrailWeight);
        }

        public static float Danger(float karenDistance, float trail, float radius, float trailWeight) =>
            Mathf.Max(Mathf.Exp(-karenDistance / Mathf.Max(0.1f, radius)), trailWeight * trail);
    }
}
