using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // Where Aiko thinks you are (Aiko.md §4).
    //
    // Never a "last known position": a probability distribution over every walkable cell
    // of the store map, Σ b(c) = 1, updated like a Bayes filter.
    //
    //   Predict — mass leaks along the floor the way a person can walk: faster if she
    //             thinks you can still sprint, towards unfinished work ("you are
    //             predictable because you are employed"), towards your habits.
    //   Correct — each observation multiplies in a likelihood. A sighting is a tight bump,
    //             a noise a bump smeared along the floor, a footprint a bump pushed along
    //             its heading, testimony a wide fading one — and a sweep that finds nothing
    //             *removes* mass from everything it looked at.
    //
    // If everything is ruled out, belief resets to the prior: she has lost the scent, and
    // the player can feel it happen.
    public sealed class BeliefGrid
    {
        readonly StoreMap map;
        readonly float[] b;
        readonly float[] scratch;
        readonly float[] prior;
        readonly float[] regionMass;
        float[] distanceScratch;
        readonly bool[] stamp;
        readonly List<int> nearby = new List<int>();

        // Per-cell layers that shape prediction. Owned by the brain, refreshed on change.
        public float[] DesireDistance;   // metres to the nearest unfinished task (∞ = none)
        public float[] HabitPrior;       // 0..1 from the Ledger: where this player tends to be
        public float[] Danger;           // 0..1: where the player has reason to avoid

        public int CellCount => b.Length;
        public float this[int cell] => b[cell];
        public float[] RegionMass => regionMass;

        public int PeakCell { get; private set; }
        public int PeakRegion { get; private set; }
        public float Confidence { get; private set; }       // max region mass
        public float Entropy { get; private set; }          // over regions, nats
        public float MaxEntropy { get; private set; }
        public float EntropyNormalised => MaxEntropy > 0f ? Entropy / MaxEntropy : 0f;
        public float LastStrongObservation { get; private set; } = -999f;
        public float Staleness => Time.time - LastStrongObservation;
        public int Collapses { get; private set; }

        // Tunables (AikoConfig overrides these).
        public float Floor = 1e-4f;                      // likelihood floor outside a bump
        public float MovingProbability = 0.65f;          // share of time a person is walking
        public float DesireGain = 0.9f;
        public float HabitGain = 1.2f;
        public float DangerAvoidance = 0.6f;

        // Share of belief per second handed back to the prior. The diffusion model is a
        // guess about how people move; this is its humility — without it one confident
        // mistake would make her deaf to everything that contradicts it.
        public float Uncertainty = 0.015f;

        public BeliefGrid(StoreMap map)
        {
            this.map = map;
            b = new float[map.CellCount];
            scratch = new float[map.CellCount];
            prior = new float[map.CellCount];
            stamp = new bool[map.CellCount];
            regionMass = new float[map.Regions.Count];
            DesireDistance = new float[map.CellCount];
            HabitPrior = new float[map.CellCount];
            Danger = new float[map.CellCount];
            for (int i = 0; i < DesireDistance.Length; i++) DesireDistance[i] = float.PositiveInfinity;
            MaxEntropy = Mathf.Log(Mathf.Max(2, map.Regions.Count));
            ResetToPrior();
        }

        // ---- the prior ----------------------------------------------------------

        // Uniform over the floor, weighted toward work and toward habit. Outdoors is given
        // little weight: an employee on shift is rarely in the street.
        public void RebuildPrior()
        {
            float sum = 0f;
            for (int c = 0; c < prior.Length; c++)
            {
                Region region = map.Regions[map.CellRegion[c]];
                float w = StoreMap.IsOutdoors(region.Area) ? 0.25f : 1f;
                if (!float.IsInfinity(DesireDistance[c])) w *= 1f + 1.5f * Mathf.Exp(-DesireDistance[c] / 8f);
                w *= 1f + HabitGain * HabitPrior[c];
                prior[c] = w;
                sum += w;
            }
            for (int c = 0; c < prior.Length; c++) prior[c] /= sum;
        }

        public void ResetToPrior()
        {
            RebuildPrior();
            System.Array.Copy(prior, b, b.Length);
            Refresh();
        }

        // Certainty at a point — for a fresh shift with the player at the time clock, which
        // Aiko knows because the time clock is hers.
        public void ResetTo(Vector3 position, float sigma)
        {
            System.Array.Copy(prior, b, b.Length);
            Multiply(position, sigma, 1f);
            Normalise();
        }

        // ---- predict --------------------------------------------------------------

        // One step of diffusion. `speed` is how fast Aiko believes you move on average —
        // lower once she thinks you're out of energy, which is burnout narrowing the search.
        public void Predict(float dt, float speed)
        {
            float mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * MovingProbability;
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
                float here = DesireDistance[c];
                for (int e = start; e < end; e++)
                    total += Weight(here, map.EdgeTo[e]);

                for (int e = start; e < end; e++)
                    scratch[map.EdgeTo[e]] += leaving * Weight(here, map.EdgeTo[e]) / total;
            }

            float u = Mathf.Clamp01(Uncertainty * dt);
            for (int c = 0; c < b.Length; c++) b[c] = (1f - u) * scratch[c] + u * prior[c];
            Refresh();
        }

        float Weight(float hereDesire, int n)
        {
            float w = 1f;
            float there = DesireDistance[n];
            // A step that closes the distance to work is favoured, one that opens it isn't.
            if (!float.IsInfinity(hereDesire) && !float.IsInfinity(there))
                w *= 1f + DesireGain * 0.5f * Mathf.Clamp((hereDesire - there) / StoreMap.CellSize, -1f, 1f);
            w *= 1f + HabitGain * HabitPrior[n];
            w *= 1f - DangerAvoidance * Mathf.Clamp01(Danger[n]);
            return Mathf.Max(0.02f, w);
        }

        // ---- correct ----------------------------------------------------------------

        // Returns true if the evidence contradicted everything and belief had to reset.
        public bool Apply(in Observation o)
        {
            if (o.IsNegative) return false;   // negatives come in batches, see ApplySweep

            switch (o.Channel)
            {
                case SenseChannel.Hearing:
                    MultiplyAlongFloor(o.Position, o.Sigma, o.Confidence);
                    break;

                case SenseChannel.Trace:
                    // A footprint says where you *went*, not where you stood.
                    Vector3 centre = o.Position;
                    if (o.Heading.sqrMagnitude > 0.01f)
                        centre += o.Heading.normalized * Mathf.Min(6f, 1.5f + (Time.time - o.Timestamp) * 1.2f);
                    MultiplyAlongFloor(centre, o.Sigma, o.Confidence);
                    break;

                default:
                    Multiply(o.Position, o.Sigma, o.Confidence);
                    break;
            }

            if (o.IsStrong) LastStrongObservation = Time.time;
            return Normalise();
        }

        // "I looked and there was nothing here." Every swept cell keeps only the share of
        // its mass that could have gone unseen.
        public bool ApplySweep(List<int> cells, List<float> detectProbability)
        {
            for (int i = 0; i < cells.Count; i++)
                b[cells[i]] *= 1f - Mathf.Clamp01(detectProbability[i]);
            return Normalise();
        }

        // Straight-line Gaussian: sightings, testimony, infrastructure. `confidence` blends
        // between "barely moves belief" and "belief collapses onto this point".
        void Multiply(Vector3 position, float sigma, float confidence)
        {
            map.CellsWithin(position, sigma * 3f, nearby);
            MarkNearby();
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            for (int c = 0; c < b.Length; c++)
            {
                if (!stamp[c]) { b[c] *= floor; continue; }
                Vector3 d = map.CellPosition[c] - position;
                d.y = 0f;
                b[c] *= floor + Mathf.Exp(-d.sqrMagnitude * inv);
            }
            ClearStamp();
        }

        // The same bump, measured in walking distance: a noise around a corner belongs to
        // the aisle it came from, not the one on the other side of the shelf.
        void MultiplyAlongFloor(Vector3 position, float sigma, float confidence)
        {
            int source = map.CellAt(position);
            if (source < 0) { Multiply(position, sigma, confidence); return; }

            distanceScratch = map.Distances(source, sigma * 3f, distanceScratch);
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            for (int c = 0; c < b.Length; c++)
            {
                float d = distanceScratch[c];
                b[c] *= float.IsInfinity(d) ? floor : floor + Mathf.Exp(-d * d * inv);
            }
        }

        // How much weight a place *outside* the bump keeps. Geometric in confidence, so a sure
        // sighting all but rules out everywhere else (0.9 → ×1/4000) while a vague noise only
        // nudges (0.3 → ×1/16). A linear blend here left even certain sightings diffuse.
        float LikelihoodFloor(float confidence) => Mathf.Pow(Floor, Mathf.Clamp01(confidence));

        void MarkNearby()
        {
            for (int i = 0; i < nearby.Count; i++) stamp[nearby[i]] = true;
        }

        void ClearStamp()
        {
            for (int i = 0; i < nearby.Count; i++) stamp[nearby[i]] = false;
        }

        // ---- the Director's bounded hint ---------------------------------------------

        // Moves up to `amount` of the total mass onto a region. The Director alone calls
        // this, and caps it (Aiko.md §9.2); the grid just does the arithmetic.
        public void Bias(int region, float amount)
        {
            if (region < 0 || amount <= 0f) return;
            Region r = map.Regions[region];
            if (r.Cells.Count == 0) return;

            amount = Mathf.Clamp01(amount);
            for (int c = 0; c < b.Length; c++) b[c] *= 1f - amount;
            float share = amount / r.Cells.Count;
            foreach (int c in r.Cells) b[c] += share;
            Normalise();
        }

        // ---- bookkeeping --------------------------------------------------------------

        bool Normalise()
        {
            float sum = 0f;
            for (int c = 0; c < b.Length; c++) sum += b[c];

            if (sum < 1e-12f || float.IsNaN(sum))
            {
                // Everything ruled out — the scent is lost.
                Collapses++;
                System.Array.Copy(prior, b, b.Length);
                Refresh();
                return true;
            }

            float inv = 1f / sum;
            for (int c = 0; c < b.Length; c++) b[c] *= inv;
            Refresh();
            return false;
        }

        void Refresh()
        {
            System.Array.Clear(regionMass, 0, regionMass.Length);
            float peak = -1f;
            for (int c = 0; c < b.Length; c++)
            {
                regionMass[map.CellRegion[c]] += b[c];
                if (b[c] > peak) { peak = b[c]; PeakCell = c; }
            }

            float best = -1f, entropy = 0f;
            for (int r = 0; r < regionMass.Length; r++)
            {
                float m = regionMass[r];
                if (m > best) { best = m; PeakRegion = r; }
                if (m > 1e-9f) entropy -= m * Mathf.Log(m);
            }
            Confidence = best;
            Entropy = entropy;
        }

        public float MassIn(ICollection<int> regions)
        {
            float sum = 0f;
            foreach (int r in regions) sum += regionMass[r];
            return sum;
        }

        public Vector3 PeakPosition => map.CellPosition[PeakCell];

        // Where the mass is, as a point: the centroid of the most likely region's cells,
        // weighted by belief. Better than the single peak cell to walk towards.
        public Vector3 RegionCentroid(int region)
        {
            Region r = map.Regions[region];
            Vector3 sum = Vector3.zero;
            float weight = 0f;
            foreach (int c in r.Cells)
            {
                sum += map.CellPosition[c] * b[c];
                weight += b[c];
            }
            return weight > 1e-9f ? sum / weight : r.Centroid;
        }

        // Areas and sections she has all but ruled out — for the thought log's "ruled_out".
        public List<string> RuledOut(float threshold = 0.01f)
        {
            var mass = new Dictionary<string, float>();
            var cells = new Dictionary<string, int>();
            foreach (Region r in map.Regions)
            {
                string key = r.IsDoor ? null : (string.IsNullOrEmpty(r.Section) ? r.Area : StoreMap.ShortSign(r.Section));
                if (key == null) continue;
                mass.TryGetValue(key, out float m);
                mass[key] = m + regionMass[r.Id];
                cells.TryGetValue(key, out int n);
                cells[key] = n + r.Cells.Count;
            }

            var result = new List<string>();
            foreach (KeyValuePair<string, float> pair in mass)
                if (pair.Value < threshold && cells[pair.Key] > 20) result.Add(pair.Key);
            result.Sort();
            return result;
        }

        // A compact copy of belief per region, for the replay scrubber.
        public float[] SnapshotRegions()
        {
            var copy = new float[regionMass.Length];
            System.Array.Copy(regionMass, copy, copy.Length);
            return copy;
        }
    }
}
