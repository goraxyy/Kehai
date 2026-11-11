using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // The prediction model's inputs (0.4.0, build plan §8.3, C4.4), refreshed every 2 s: the
    // destinations, how far every cell is from each, each cell's chance of heading for each, and
    // where you think she is (the mirror map, or where she is for K0). From what she knows only:
    // her map, her belief, her own position, her chores; never where you are.
    //
    // One refresh serves four readers: the predict step (P1's Danger layer; P2 and P3's flows),
    // her route guesses (RoutePredictor), trick placement "on your way", and the brains' inputs
    // (her three best guesses).
    public sealed class MotionFeatures
    {
        public const float RefreshSeconds = 2f;

        public readonly Destinations Places = new Destinations();
        public PredictionWeights Weights = PredictionWeights.Current;

        // Set by the brain before each refresh.
        public Vector3 HerPosition;          // where she is
        public Vector3 SheIsThought;         // where you think she is (K0: where she is)
        public float SheIsThoughtStale;      // seconds since you probably sensed her (K0: 0)
        public Vector3 LastSeenAt, LastSeenHeading;
        public float TimeLeft01 = 1f;        // the shift's time left, 0..1
        public bool StoreOpen = true;
        public bool ReadLedger;              // P3

        public float RefreshedAt { get; private set; } = -999f;
        public int Count => places.Count;
        public IReadOnlyList<Destination> List => places;

        readonly List<Destination> places = new List<Destination>();
        StoreMap map;
        float[][] distance = new float[0][];      // per destination: metres from every cell (∞ unreachable)
        float[] chance = new float[0];            // [cell × destinations]: her chance you head for each, from there
        float[] rate = new float[0];              // per edge: share of a cell's mass a second moving along it
        float[] exitRate = new float[0];          // per cell: share a second leaving by its exit
        float[] danger = new float[0];

        public float[] EdgeRate => rate;
        public float[] ExitRate => exitRate;
        public float Distance(int destination, int cell) => distance[destination][cell];
        public float Chance(int cell, int destination) => chance[cell * places.Count + destination];

        public bool Due(float now) => now - RefreshedAt >= RefreshSeconds;

        public void Refresh(KarenContext c, float now)
        {
            RefreshedAt = now;
            Weights = PredictionWeights.Current;
            Vector3 from = c.Belief != null ? c.Belief.PeakPosition : HerPosition;
            List<Destination> built = Places.Build(c, from, now);
            // The Ledger's numbers for P3, written onto each place (the trace records them too).
            for (int i = 0; i < built.Count; i++)
            {
                Destination d = built[i];
                d.AnswerSeconds = d.Kind == DestinationKind.Chore && !string.IsNullOrEmpty(d.AnswerKind) && c.Ledger != null
                    ? c.Ledger.AnswerReading(d.AnswerKind, LedgerStart.Current).Guess : -1f;
                d.Habit = d.Kind == DestinationKind.Hide && c.Ledger != null ? HabitOf(c, d.Name) : 0f;
                built[i] = d;
            }
            Compute(c.Map, c.Belief, built);
        }

        // The arithmetic, from plain inputs (tests and the Python replay give it the same ones).
        public void Compute(StoreMap m, BeliefGrid belief, IList<Destination> list)
        {
            map = m;
            int n = map.CellCount;
            places.Clear();
            places.AddRange(list);
            int k = places.Count;

            if (distance.Length != k) distance = new float[k][];
            bool[] notHers = new bool[n];
            for (int i = 0; i < n; i++) notHers[i] = belief != null && !belief.IsHers(i);
            for (int d = 0; d < k; d++)
            {
                if (places[d].Cell < 0) { distance[d] = null; continue; }
                var sources = new List<int>();
                if (places[d].Kind == DestinationKind.Exit && belief != null)
                {
                    int exit = belief.ExitOf(places[d].Cell);
                    if (exit >= 0) sources.AddRange(belief.ExitBorder(exit));
                }
                if (sources.Count == 0) sources.Add(places[d].Cell);
                distance[d] = map.DistancesFrom(sources, float.MaxValue, distance[d], notHers);
            }

            FillDanger(n);
            FillChances(n, k);
            FillRates(belief, n, k);
        }

        // P1's Danger layer, and P2's bend: near where you think she is, by walking distance.
        void FillDanger(int n)
        {
            if (danger.Length != n) danger = new float[n];
            int herCell = map.CellAt(SheIsThought);
            if (herCell < 0) { System.Array.Clear(danger, 0, n); return; }
            float radius = Mathf.Max(0.5f, Weights.DangerRadius);
            float[] d = map.Distances(herCell, radius * 4f);
            for (int i = 0; i < n; i++) danger[i] = float.IsInfinity(d[i]) ? 0f : Mathf.Exp(-d[i] / radius);
        }

        // Writes the Danger layer into a grid (P1 reads it there, as the hand step always did).
        public void WriteDanger(float[] into)
        {
            if (into == null || danger.Length != into.Length) return;
            System.Array.Copy(danger, into, into.Length);
        }

        void FillChances(int n, int k)
        {
            if (chance.Length != n * k) chance = new float[n * k];
            PredictionWeights w = Weights;
            var logits = new float[k];
            var global = new float[k];
            for (int d = 0; d < k; d++) global[d] = GlobalLogit(places[d], w);
            for (int cell = 0; cell < n; cell++)
            {
                float max = float.NegativeInfinity;
                for (int d = 0; d < k; d++)
                {
                    float l = global[d];
                    Destination dest = places[d];
                    if (dest.Cell >= 0)
                    {
                        float metres = distance[d][cell];
                        if (float.IsInfinity(metres)) { logits[d] = float.NegativeInfinity; continue; }
                        l += w.Distance * Mathf.Log(1f + metres);
                        if (Between(map.CellPosition[cell], dest.Position)) l += w.Between;
                    }
                    logits[d] = l;
                    if (l > max) max = l;
                }
                float sum = 0f;
                for (int d = 0; d < k; d++)
                {
                    float e = float.IsNegativeInfinity(logits[d]) ? 0f : Mathf.Exp(logits[d] - max);
                    chance[cell * k + d] = e;
                    sum += e;
                }
                for (int d = 0; d < k; d++) chance[cell * k + d] = sum > 0f ? chance[cell * k + d] / sum : 0f;
            }
        }

        // The part of a destination's score that is the same from every cell.
        float GlobalLogit(Destination d, PredictionWeights w)
        {
            float l = w.Kind[(int)d.Kind];
            if (d.Kind == DestinationKind.Job || d.Kind == DestinationKind.Chore || d.Kind == DestinationKind.Queue)
                l += w.Wait * d.Waited / (d.Waited + 30f);
            if (d.Kind == DestinationKind.Clock) l += w.ClockLate * (StoreOpen ? 1f - TimeLeft01 : 1.5f);
            if (d.Cell >= 0 && LastSeenHeading.sqrMagnitude > 0.01f)
            {
                Vector3 to = d.Position - LastSeenAt;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) l += w.Heading * Vector3.Dot(to.normalized, new Vector3(LastSeenHeading.x, 0f, LastSeenHeading.z).normalized);
            }
            if (ReadLedger)
            {
                if (d.Kind == DestinationKind.Chore && d.AnswerSeconds >= 0f)
                    l += w.LedgerAnswer * (1f - d.AnswerSeconds / (d.AnswerSeconds + 60f));
                if (d.Kind == DestinationKind.Hide) l += w.LedgerHide * d.Habit;
            }
            return l;
        }

        static float HabitOf(KarenContext c, string region)
        {
            if (c.Map == null) return 0f;
            foreach (Region r in c.Map.Regions) if (r.Name == region) return c.Ledger.Habit(r);
            return 0f;
        }

        // Would you think she stands between this cell and that place? Within 4 m of the straight
        // way there, and nearer than the place.
        bool Between(Vector3 from, Vector3 to)
        {
            Vector3 her = SheIsThought;
            Vector3 a = new Vector3(from.x, 0f, from.z), b = new Vector3(to.x, 0f, to.z), h = new Vector3(her.x, 0f, her.z);
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.01f) return false;
            float t = Vector3.Dot(h - a, ab) / len2;
            if (t <= 0f || t >= 1f) return false;
            return (a + ab * t - h).sqrMagnitude < 16f;
        }

        // Per edge, the share of a cell's mass a second that moves along it: for each destination,
        // its chance × the mean speed ÷ the cell size, spread over the neighbours that are nearer to
        // it, bent away from where you think she is. Staying and turning back are kept in place and
        // sent back the way you came.
        void FillRates(BeliefGrid belief, int n, int k)
        {
            int edges = map.EdgeTo.Length;
            if (rate.Length != edges) rate = new float[edges];
            if (exitRate.Length != n) exitRate = new float[n];
            System.Array.Clear(rate, 0, edges);
            System.Array.Clear(exitRate, 0, n);
            float move = Weights.MeanSpeed / StoreMap.CellSize;
            float avoid = Mathf.Clamp01(Weights.Avoid);
            for (int cell = 0; cell < n; cell++)
            {
                if (belief != null && !belief.IsHers(cell)) continue;
                int start = map.EdgeStart[cell], end = map.EdgeStart[cell + 1];
                for (int d = 0; d < k; d++)
                {
                    float p = chance[cell * k + d];
                    if (p <= 1e-5f) continue;
                    Destination dest = places[d];
                    if (dest.Kind == DestinationKind.Stay) continue;
                    if (dest.Kind == DestinationKind.TurnBack)
                    {
                        Vector3 here = map.CellPosition[cell];
                        Downhill(cell, start, end, p * move, avoid, e =>
                            -((map.CellPosition[map.EdgeTo[e]].x - here.x) * LastSeenHeading.x + (map.CellPosition[map.EdgeTo[e]].z - here.z) * LastSeenHeading.z));
                        continue;
                    }
                    float[] dist = distance[d];
                    if (dist == null) continue;
                    if (dist[cell] <= 0.01f)
                    {
                        // Arrived: an exit's mass goes out; anything else stays to work or hide.
                        if (dest.Kind == DestinationKind.Exit && belief != null && belief.ExitOf(cell) >= 0) exitRate[cell] += p * move;
                        continue;
                    }
                    Downhill(cell, start, end, p * move, avoid, e => dist[cell] - dist[map.EdgeTo[e]]);
                }
            }
        }

        void Downhill(int cell, int start, int end, float amount, float avoid, System.Func<int, float> gain)
        {
            float total = 0f;
            for (int e = start; e < end; e++)
            {
                float g = gain(e);
                if (g > 0f) total += g * (1f - avoid * danger[map.EdgeTo[e]]);
            }
            if (total <= 0f) return;
            for (int e = start; e < end; e++)
            {
                float g = gain(e);
                if (g > 0f) rate[e] += amount * g * (1f - avoid * danger[map.EdgeTo[e]]) / total;
            }
        }

        // Her best guesses at where you're going, from where her belief puts you: kind, metres, chance.
        public List<(Destination place, float metres, float chance)> Top(BeliefGrid belief, int count)
        {
            var result = new List<(Destination, float, float)>();
            if (belief == null || places.Count == 0 || map == null) return result;
            int k = places.Count;
            var total = new float[k];
            var metres = new float[k];
            for (int cell = 0; cell < map.CellCount; cell++)
            {
                float m = belief[cell];
                if (m <= 1e-6f || cell * k + k > chance.Length) continue;
                for (int d = 0; d < k; d++)
                {
                    total[d] += m * chance[cell * k + d];
                    if (distance[d] != null && !float.IsInfinity(distance[d][cell])) metres[d] += m * distance[d][cell];
                }
            }
            var order = new List<int>();
            for (int d = 0; d < k; d++) order.Add(d);
            order.Sort((a, b) => total[b].CompareTo(total[a]));
            for (int i = 0; i < order.Count && result.Count < count; i++)
            {
                int d = order[i];
                result.Add((places[d], total[d] > 0f ? metres[d] / Mathf.Max(1e-6f, SumMass(belief)) : 0f, total[d]));
            }
            return result;
        }

        static float SumMass(BeliefGrid b)
        {
            float s = 0f;
            for (int i = 0; i < b.CellCount; i++) s += b[i];
            return s;
        }
    }
}
