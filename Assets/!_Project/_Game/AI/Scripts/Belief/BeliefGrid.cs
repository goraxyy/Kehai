using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Where Karen thinks you are (Karen.md §4).
    //
    // Never a "last known position": a probability distribution over every walkable cell
    // of the store map, Σ b(c) = 1, updated like a Bayes filter.
    //
    //   Predict — mass leaks along the floor the way a person can walk. How it moves is the
    //             motion model's business (MotionModel.cs): the shipped one is a hand-written
    //             step, faster if she thinks you can still sprint, towards unfinished work
    //             ("you are predictable because you are employed"), towards your habits.
    //   Correct — each observation multiplies in a likelihood. A sighting is a tight bump,
    //             a noise a bump smeared along the floor, a footprint a bump pushed along
    //             its heading, testimony a wide fading one — and a sweep that finds nothing
    //             *removes* mass from everything it looked at.
    //
    // A motion model may keep a heading per cell (still, or one of eight directions). Then the
    // state is cell × heading, evidence multiplies every heading of a cell alike, and
    // everything that reads the grid sees the sum over headings.
    //
    // If everything is ruled out, belief resets to the prior: she has lost the scent, and
    // the player can feel it happen.
    public sealed class BeliefGrid
    {
        readonly StoreMap map;
        readonly float[] b;            // over cells: the state itself, or its marginal with headings
        readonly float[] state;        // cells × headings; the same array as b when there are none
        readonly float[] scratch;
        readonly float[] prior;
        readonly float[] regionMass;
        float[] distanceScratch;
        readonly bool[] stamp;
        readonly List<int> nearby = new List<int>();
        readonly Likelihood like = new Likelihood();

        // Per-cell layers that shape prediction. Owned by the brain, refreshed on change.
        public float[] DesireDistance;   // metres to the nearest unfinished task (∞ = none)
        public float[] HabitPrior;       // 0..1 from the Ledger: where this player tends to be
        public float[] Danger;           // 0..1: where the player has reason to avoid

        public StoreMap Map => map;
        public IMotionModel Motion { get; }
        public int Headings { get; }
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

        // What the eval harness reads about the cost of prediction (Karen.md §12.4).
        public int PredictTicks { get; private set; }
        public double PredictMilliseconds { get; private set; }

        // Everything that changes the grid, as it happens: the trace logger and the shadow
        // filters listen here (Eval/BeliefTrace, Eval/BeliefShadows).
        public readonly List<IBeliefTap> Taps = new List<IBeliefTap>();

        // Tunables (KarenConfig overrides these).
        public float Floor = 1e-4f;                      // likelihood floor outside a bump
        public float HabitGain = 1.2f;                   // how much habit weighs in the prior

        public BeliefGrid(StoreMap map, IMotionModel motion = null)
        {
            this.map = map;
            Motion = motion ?? new HandMotion();
            Headings = Mathf.Max(1, Motion.Headings);
            b = new float[map.CellCount];
            state = Headings == 1 ? b : new float[map.CellCount * Headings];
            scratch = new float[state.Length];
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

        public float[] Prior => prior;

        public void ResetToPrior()
        {
            RebuildPrior();
            System.Array.Copy(prior, b, b.Length);
            Expand();
            Refresh();
            for (int i = 0; i < Taps.Count; i++) Taps[i].OnReset(this);
        }

        // Certainty at a point — for a fresh shift with the player at the time clock, which
        // Karen knows because the time clock is hers.
        public void ResetTo(Vector3 position, float sigma)
        {
            System.Array.Copy(prior, b, b.Length);
            Expand();
            BuildBump(position, sigma, 1f);
            MultiplyIn(like);
            Normalise();
            for (int i = 0; i < Taps.Count; i++) Taps[i].OnReset(this);
        }

        // Take another grid's belief as this one's, over the same map — how a shadow filter
        // starts a shift with Karen (Eval/BeliefShadows). Headings start from the model's prior.
        public void CopyFrom(BeliefGrid other)
        {
            System.Array.Copy(other.prior, prior, prior.Length);
            for (int c = 0; c < b.Length; c++) b[c] = other.b[c];
            Expand();
            Refresh();
        }

        // ---- predict --------------------------------------------------------------

        // One step of the motion model, then the leak back to the prior. `speed` is how fast
        // Karen believes you move on average — lower once she thinks you're out of energy,
        // which is burnout narrowing the search.
        public void Predict(float dt, float speed)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Motion.Transition(this, state, scratch, dt, speed);

            // The leak is the model's humility: a share of belief per second handed back to the
            // prior, so one confident mistake can't make her deaf to what contradicts it.
            float u = Mathf.Clamp01(Motion.Leak * dt);
            if (Headings == 1)
            {
                for (int c = 0; c < b.Length; c++) b[c] = (1f - u) * scratch[c] + u * prior[c];
            }
            else
            {
                float[] hp = Motion.HeadingPrior;
                for (int c = 0, i = 0; c < b.Length; c++)
                    for (int h = 0; h < Headings; h++, i++) state[i] = (1f - u) * scratch[i] + u * prior[c] * hp[h];
            }
            Refresh();

            PredictTicks++;
            PredictMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            for (int i = 0; i < Taps.Count; i++) Taps[i].OnPredict(this, dt, speed);
        }

        // ---- correct ----------------------------------------------------------------

        // Returns true if the evidence contradicted everything and belief had to reset.
        public bool Apply(in Observation o)
        {
            if (o.IsNegative) return false;   // negatives come in batches, see ApplySweep

            switch (o.Channel)
            {
                case SenseChannel.Hearing:
                    BuildAlongFloor(o.Position, o.Sigma, o.Confidence);
                    break;

                case SenseChannel.Trace:
                    // A footprint says where you *went*, not where you stood.
                    Vector3 centre = o.Position;
                    if (o.Heading.sqrMagnitude > 0.01f)
                        centre += o.Heading.normalized * Mathf.Min(6f, 1.5f + (Time.time - o.Timestamp) * 1.2f);
                    BuildAlongFloor(centre, o.Sigma, o.Confidence);
                    break;

                default:
                    BuildBump(o.Position, o.Sigma, o.Confidence);
                    break;
            }

            like.Channel = o.Channel;
            like.Strong = o.IsStrong;
            if (o.IsStrong) LastStrongObservation = Time.time;
            return Correct(like);
        }

        // "I looked and there was nothing here." Every swept cell keeps only the share of
        // its mass that could have gone unseen.
        public bool ApplySweep(List<int> cells, List<float> detectProbability)
        {
            like.Begin(1f, SenseChannel.Absence);
            for (int i = 0; i < cells.Count; i++) like.Add(cells[i], 1f - Mathf.Clamp01(detectProbability[i]));
            return Correct(like);
        }

        // Multiplies a likelihood that was built elsewhere — the same evidence fed to a shadow
        // filter, or replayed. Returns true if belief had to reset.
        public bool Correct(Likelihood l)
        {
            if (l.Strong && l != like) LastStrongObservation = Time.time;
            MultiplyIn(l);
            bool collapsed = Normalise();
            for (int i = 0; i < Taps.Count; i++) Taps[i].OnEvidence(this, l, collapsed);
            return collapsed;
        }

        // Straight-line Gaussian: sightings, testimony, infrastructure. `confidence` blends
        // between "barely moves belief" and "belief collapses onto this point".
        void BuildBump(Vector3 position, float sigma, float confidence)
        {
            map.CellsWithin(position, sigma * 3f, nearby);
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            like.Begin(floor, SenseChannel.Sight);
            for (int i = 0; i < nearby.Count; i++)
            {
                int c = nearby[i];
                Vector3 d = map.CellPosition[c] - position;
                d.y = 0f;
                like.Add(c, floor + Mathf.Exp(-d.sqrMagnitude * inv));
            }
        }

        // The same bump, measured in walking distance: a noise around a corner belongs to
        // the aisle it came from, not the one on the other side of the shelf.
        void BuildAlongFloor(Vector3 position, float sigma, float confidence)
        {
            int source = map.CellAt(position);
            if (source < 0) { BuildBump(position, sigma, confidence); return; }

            distanceScratch = map.Distances(source, sigma * 3f, distanceScratch);
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            like.Begin(floor, SenseChannel.Hearing);
            for (int c = 0; c < b.Length; c++)
            {
                float d = distanceScratch[c];
                if (!float.IsInfinity(d)) like.Add(c, floor + Mathf.Exp(-d * d * inv));
            }
        }

        // How much weight a place *outside* the bump keeps. Geometric in confidence, so a sure
        // sighting all but rules out everywhere else (0.9 → ×1/4000) while a vague noise only
        // nudges (0.3 → ×1/16). A linear blend here left even certain sightings diffuse.
        float LikelihoodFloor(float confidence) => Mathf.Pow(Floor, Mathf.Clamp01(confidence));

        // Every cell is multiplied exactly once: by its listed value, or by the floor. Listed
        // values are kept in double and the product rounded once, which is what the old inline
        // `b[c] *= floor + Mathf.Exp(…)` did under Mono (it keeps float intermediates in double)
        // and under IL2CPP alike (a float product is exact in double).
        void MultiplyIn(Likelihood l)
        {
            for (int i = 0; i < l.Count; i++)
            {
                int c = l.Cells[i];
                stamp[c] = true;
                double v = l.Values[i];
                if (Headings == 1) b[c] = (float)(b[c] * v);
                else for (int h = 0, s = c * Headings; h < Headings; h++, s++) state[s] = (float)(state[s] * v);
            }
            if (l.Floor != 1f)
            {
                for (int s = 0; s < state.Length; s++)
                    if (!stamp[s / Headings]) state[s] *= l.Floor;
            }
            for (int i = 0; i < l.Count; i++) stamp[l.Cells[i]] = false;
        }

        // ---- the Director's bounded hint ---------------------------------------------

        // Moves up to `amount` of the total mass onto a region. The Director alone calls
        // this, and caps it (Karen.md §9.2); the grid just does the arithmetic. Shadow
        // filters don't get it: it is the truth leaking in, and they measure the filter alone.
        public void Bias(int region, float amount)
        {
            if (region < 0 || amount <= 0f) return;
            Region r = map.Regions[region];
            if (r.Cells.Count == 0) return;

            amount = Mathf.Clamp01(amount);
            for (int s = 0; s < state.Length; s++) state[s] *= 1f - amount;
            float share = amount / r.Cells.Count;
            if (Headings == 1) foreach (int c in r.Cells) b[c] += share;
            else
            {
                float[] hp = Motion.HeadingPrior;
                foreach (int c in r.Cells)
                    for (int h = 0; h < Headings; h++) state[c * Headings + h] += share * hp[h];
            }
            Normalise();
            for (int i = 0; i < Taps.Count; i++) Taps[i].OnBias(this, region, amount);
        }

        // ---- bookkeeping --------------------------------------------------------------

        bool Normalise()
        {
            // A float running sum, as it always was. With headings the state is nine times
            // longer and a float sum drifts by up to 1e-3 of the total, so that one is kept in double.
            float sum = 0f;
            if (Headings == 1) for (int s = 0; s < state.Length; s++) sum += state[s];
            else
            {
                double total = 0.0;
                for (int s = 0; s < state.Length; s++) total += state[s];
                sum = (float)total;
            }

            if (sum < 1e-12f || float.IsNaN(sum))
            {
                // Everything ruled out — the scent is lost.
                Collapses++;
                System.Array.Copy(prior, b, b.Length);
                Expand();
                Refresh();
                return true;
            }

            float inv = 1f / sum;
            for (int s = 0; s < state.Length; s++) state[s] *= inv;
            Refresh();
            return false;
        }

        // From a belief over cells to the state: each cell's mass split by the heading prior.
        void Expand()
        {
            if (Headings == 1) return;
            float[] hp = Motion.HeadingPrior;
            for (int c = 0, s = 0; c < b.Length; c++)
                for (int h = 0; h < Headings; h++, s++) state[s] = b[c] * hp[h];
        }

        void Refresh()
        {
            if (Headings > 1)
            {
                for (int c = 0, s = 0; c < b.Length; c++)
                {
                    float m = 0f;
                    for (int h = 0; h < Headings; h++, s++) m += state[s];
                    b[c] = m;
                }
            }

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

        // The full state (cells × headings), for the trace logger and the tests.
        public float[] State => state;

        // Sum of belief per heading, over the whole store: how sure she is of which way you're going.
        public float HeadingMass(int heading)
        {
            if (Headings == 1) return heading == 0 ? 1f : 0f;
            float m = 0f;
            for (int s = heading; s < state.Length; s += Headings) m += state[s];
            return m;
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

    // One piece of evidence as the grid multiplies it in: a value for each listed cell, and
    // the floor for every other. Kept as numbers rather than as the Observation so the same
    // evidence can be fed to several filters and written to a trace exactly as computed.
    public sealed class Likelihood
    {
        public float Floor = 1f;
        public SenseChannel Channel;
        public bool Strong;
        public int Count;
        public int[] Cells = new int[256];
        public double[] Values = new double[256];

        public void Begin(float floor, SenseChannel channel)
        {
            Floor = floor;
            Channel = channel;
            Strong = false;
            Count = 0;
        }

        public void Add(int cell, double value)
        {
            if (Count == Cells.Length)
            {
                System.Array.Resize(ref Cells, Count * 2);
                System.Array.Resize(ref Values, Count * 2);
            }
            Cells[Count] = cell;
            Values[Count] = value;
            Count++;
        }
    }

    // Listens to a belief grid (Eval/BeliefShadows, Eval/BeliefTrace). Called after the grid
    // has changed, so the grid's own state is already the new one.
    public interface IBeliefTap
    {
        void OnEvidence(BeliefGrid grid, Likelihood likelihood, bool collapsed);
        void OnBias(BeliefGrid grid, int region, float amount);
        void OnPredict(BeliefGrid grid, float dt, float speed);
        void OnReset(BeliefGrid grid);
    }
}
