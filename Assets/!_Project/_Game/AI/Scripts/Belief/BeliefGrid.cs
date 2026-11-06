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
    //
    // Only her part of the building has cells of its own (StoreMap.KarenWorksIn): indoors, the
    // backstreet and the pavement. Everything else — the street and parking lot, the alleys,
    // the yard — is one cell, OutsideCell, standing for "outside". It remembers which of her
    // exits you left by: its mass comes back in through that exit first, and through another
    // only once you could have walked round outside to it.
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

        // Tunables (KarenConfig overrides these).
        public float Floor = 1e-4f;                      // likelihood floor outside a bump
        public float MovingProbability = 0.65f;          // share of time a person is walking
        public float DesireGain = 0.9f;
        public float HabitGain = 1.2f;
        public float DangerAvoidance = 0.6f;

        // Share of belief per second handed back to the prior. The diffusion model is a
        // guess about how people move; this is its humility — without it one confident
        // mistake would make her deaf to everything that contradicts it.
        public float Uncertainty = 0.015f;

        // ---- outside her part of the building ----------------------------------------------

        // Share of the outside's mass per second that comes back in through an exit.
        public float OutsideReturn = 0.08f;
        // The outside's share of the prior: almost nothing, but not nothing, or a sighting out
        // there could never move her belief outside (zero times any likelihood is zero).
        public float OutsidePrior = 0.002f;
        // The fastest you can walk round outside from one exit to another (your sprint).
        public float OutsideSprint = 6.5f;
        const int LineSeconds = 40;
        const float ExitSpan = 9f;   // metres of edge one exit covers

        readonly bool[] hers;
        readonly int[] exitOf;                              // per cell: its exit if it borders outside, else -1
        readonly List<int[]> exitBorder = new List<int[]>(); // per exit: her cells next to outside
        readonly int outsideCells;
        float[,] exitDelay;                                 // seconds from one exit to another, round outside
        float[][] line;                                     // per exit: outside mass by seconds since it left
        float[] inflow;
        float[] returning;
        float lineClock;
        int lastExit;

        public int OutsideCell { get; } = -1;
        public float OutsideMass => OutsideCell >= 0 ? b[OutsideCell] : 0f;
        public int ExitCount => exitBorder.Count;
        public bool IsHers(int cell) => hers[cell];
        public int ExitOf(int cell) => exitOf[cell];
        public IReadOnlyList<int> ExitBorder(int exit) => exitBorder[exit];
        public float ExitDelay(int from, int to) => exitDelay[from, to];

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

            hers = new bool[map.CellCount];
            exitOf = new int[map.CellCount];
            for (int c = 0; c < hers.Length; c++)
            {
                hers[c] = map.IsKarens(c);
                exitOf[c] = -1;
                if (!hers[c]) outsideCells++;
            }
            if (outsideCells > 0)
            {
                OutsideCell = PickOutsideCell();
                FindExits();
            }
            ResetToPrior();
        }

        // The outside cell sits in the middle of the outside: the parking lot.
        int PickOutsideCell()
        {
            Vector3 sum = Vector3.zero;
            for (int c = 0; c < hers.Length; c++) if (!hers[c]) sum += map.CellPosition[c];
            Vector3 middle = sum / outsideCells;
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int c = 0; c < hers.Length; c++)
            {
                if (hers[c]) continue;
                Vector3 d = map.CellPosition[c] - middle;
                float sqr = d.x * d.x + d.z * d.z;
                if (sqr < bestSqr) { bestSqr = sqr; best = c; }
            }
            return best;
        }

        // Her cells next to the outside, grouped into ways out: cells within 3 m of each other
        // are one stretch of edge (the pavement's, the backstreet's mouth), cut into exits of
        // at most ExitSpan along it, so where along the pavement you left still counts. Then
        // the walk between every pair of exits, round the outside only.
        void FindExits()
        {
            var border = new List<int>();
            for (int c = 0; c < hers.Length; c++)
            {
                if (!hers[c]) continue;
                for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++)
                    if (!hers[map.EdgeTo[e]]) { border.Add(c); break; }
            }

            var group = new int[border.Count];
            for (int i = 0; i < group.Length; i++) group[i] = -1;
            int groups = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < border.Count; i++)
            {
                if (group[i] >= 0) continue;
                group[i] = groups;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    Vector3 here = map.CellPosition[border[stack.Pop()]];
                    for (int j = 0; j < border.Count; j++)
                    {
                        if (group[j] >= 0) continue;
                        Vector3 d = map.CellPosition[border[j]] - here;
                        if (d.x * d.x + d.z * d.z > 9f) continue;
                        group[j] = groups;
                        stack.Push(j);
                    }
                }
                groups++;
            }

            var stretches = new List<int>[groups];
            for (int k = 0; k < groups; k++) stretches[k] = new List<int>();
            for (int i = 0; i < border.Count; i++) stretches[group[i]].Add(border[i]);
            foreach (List<int> stretch in stretches)
            {
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (int c in stretch)
                {
                    Vector3 p = map.CellPosition[c];
                    minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                    minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                }
                bool alongX = maxX - minX >= maxZ - minZ;
                float from = alongX ? minX : minZ;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(((alongX ? maxX : maxZ) - from + 0.01f) / ExitSpan));
                var cut = new List<int>[pieces];
                for (int k = 0; k < pieces; k++) cut[k] = new List<int>();
                foreach (int c in stretch)
                {
                    float at = alongX ? map.CellPosition[c].x : map.CellPosition[c].z;
                    cut[Mathf.Min(pieces - 1, Mathf.FloorToInt((at - from) / ExitSpan))].Add(c);
                }
                foreach (List<int> piece in cut)
                {
                    if (piece.Count == 0) continue;
                    foreach (int c in piece) exitOf[c] = exitBorder.Count;
                    exitBorder.Add(piece.ToArray());
                }
            }
            groups = exitBorder.Count;

            exitDelay = new float[groups, groups];
            var doorsteps = new List<int>[groups];
            for (int k = 0; k < groups; k++)
            {
                doorsteps[k] = new List<int>();
                foreach (int c in exitBorder[k])
                    for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++)
                        if (!hers[map.EdgeTo[e]]) doorsteps[k].Add(map.EdgeTo[e]);
            }
            for (int k = 0; k < groups; k++)
            {
                float[] d = map.DistancesFrom(doorsteps[k], float.MaxValue, null, hers);
                for (int j = 0; j < groups; j++)
                {
                    float best = float.PositiveInfinity;
                    foreach (int c in doorsteps[j]) best = Mathf.Min(best, d[c]);
                    exitDelay[k, j] = k == j ? 0f : best / OutsideSprint;
                }
            }

            line = new float[groups][];
            for (int k = 0; k < groups; k++) line[k] = new float[LineSeconds];
            inflow = new float[groups];
        }

        // ---- the prior ----------------------------------------------------------

        // Uniform over her part of the building, weighted toward work and toward habit. The
        // outside starts with next to nothing: an employee on shift is in the building.
        public void RebuildPrior()
        {
            float sum = 0f;
            for (int c = 0; c < prior.Length; c++)
            {
                if (!hers[c]) { prior[c] = 0f; continue; }
                float w = 1f;
                if (!float.IsInfinity(DesireDistance[c])) w *= 1f + 1.5f * Mathf.Exp(-DesireDistance[c] / 8f);
                w *= 1f + HabitGain * HabitPrior[c];
                prior[c] = w;
                sum += w;
            }
            if (sum <= 0f) return;
            float inside = OutsideCell >= 0 ? 1f - OutsidePrior : 1f;
            for (int c = 0; c < prior.Length; c++) prior[c] *= inside / sum;
            if (OutsideCell >= 0) prior[OutsideCell] = OutsidePrior;
        }

        public void ResetToPrior()
        {
            RebuildPrior();
            System.Array.Copy(prior, b, b.Length);
            ClearLines();
            SyncLines();
            Refresh();
        }

        // Certainty at a point — for a fresh shift with the player at the time clock, which
        // Karen knows because the time clock is hers.
        public void ResetTo(Vector3 position, float sigma)
        {
            System.Array.Copy(prior, b, b.Length);
            ClearLines();
            Multiply(position, sigma, 1f, IsOutside(position));
            Normalise();
        }

        // ---- predict --------------------------------------------------------------

        // One step of diffusion. `speed` is how fast Karen believes you move on average —
        // lower once she thinks you're out of energy, which is burnout narrowing the search.
        public void Predict(float dt, float speed)
        {
            float mobility = Mathf.Clamp(speed * dt / StoreMap.CellSize, 0f, 0.8f) * MovingProbability;
            System.Array.Clear(scratch, 0, scratch.Length);
            if (inflow != null) System.Array.Clear(inflow, 0, inflow.Length);

            for (int c = 0; c < b.Length; c++)
            {
                if (!hers[c]) continue;            // the outside moves in its exit lines, below
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
                {
                    int n = map.EdgeTo[e];
                    float share = leaving * Weight(here, n) / total;
                    if (hers[n]) scratch[n] += share;
                    else inflow[exitOf[c]] += share;   // out through this exit
                }
            }

            if (OutsideCell >= 0) scratch[OutsideCell] = StepOutside(dt);

            float u = Mathf.Clamp01(Uncertainty * dt);
            for (int c = 0; c < b.Length; c++) b[c] = (1f - u) * scratch[c] + u * prior[c];
            SyncLines();
            Refresh();
        }

        // The outside's mass comes back through the exit it went out by at any time, and
        // through another only once the walk round outside could have been done. Returns the
        // mass still outside.
        float StepOutside(float dt)
        {
            int exits = exitBorder.Count;
            float back = Mathf.Clamp01(OutsideReturn * dt);
            if (returning == null || returning.Length != exits) returning = new float[exits];
            System.Array.Clear(returning, 0, exits);
            for (int k = 0; k < exits; k++)
            {
                float[] l = line[k];
                for (int a = 0; a < LineSeconds; a++)
                {
                    float m = l[a];
                    if (m <= 0f) continue;
                    float r = m * back;
                    l[a] = m - r;

                    float age = a + lineClock;
                    int open = 0;
                    for (int j = 0; j < exits; j++) if (j == k || age >= exitDelay[k, j]) open++;
                    for (int j = 0; j < exits; j++)
                        if (j == k || age >= exitDelay[k, j]) returning[j] += r / open;
                }
                if (inflow[k] > 0f)
                {
                    l[0] += inflow[k];
                    lastExit = k;
                }
            }

            for (int j = 0; j < exits; j++)
            {
                if (returning[j] <= 0f) continue;
                int[] border = exitBorder[j];
                float share = returning[j] / border.Length;
                foreach (int cell in border) scratch[cell] += share;
            }

            lineClock += dt;
            while (lineClock >= 1f)
            {
                lineClock -= 1f;
                foreach (float[] l in line)
                {
                    l[LineSeconds - 1] += l[LineSeconds - 2];
                    for (int a = LineSeconds - 2; a > 0; a--) l[a] = l[a - 1];
                    l[0] = 0f;
                }
            }
            return LineMass();
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

            bool outside = IsOutside(o.Position);
            switch (o.Channel)
            {
                case SenseChannel.Hearing:
                    MultiplyAlongFloor(o.Position, o.Sigma, o.Confidence, outside);
                    break;

                case SenseChannel.Trace:
                    // A footprint says where you *went*, not where you stood.
                    Vector3 centre = o.Position;
                    if (o.Heading.sqrMagnitude > 0.01f)
                        centre += o.Heading.normalized * Mathf.Min(6f, 1.5f + (Time.time - o.Timestamp) * 1.2f);
                    MultiplyAlongFloor(centre, o.Sigma, o.Confidence, outside);
                    break;

                default:
                    Multiply(o.Position, o.Sigma, o.Confidence, outside);
                    break;
            }

            if (outside) Regroup(o.Position);
            if (o.IsStrong) LastStrongObservation = Time.time;
            return Normalise();
        }

        // How much she expects you at a spot (0.2.0, awareness D): her belief on its cell and the
        // cells next to it, against an even spread over her cells. 0 where it's no more than
        // even, 1 at four times that or more. Outside, 0: she never hunts you there.
        public float Expectation(Vector3 position)
        {
            int cell = map.CellAt(position);
            if (cell < 0 || !hers[cell]) return 0f;
            float mass = b[cell];
            int n = 1;
            for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
            {
                int next = map.EdgeTo[e];
                if (!hers[next]) continue;
                mass += b[next];
                n++;
            }
            float even = n / (float)Mathf.Max(1, hers.Length - outsideCells);
            return Mathf.Clamp01((mass / even - 1f) / 3f);
        }

        // Whether a point is outside her part of the building.
        public bool IsOutside(Vector3 position)
        {
            if (OutsideCell < 0) return false;
            int cell = map.CellAt(position);
            return cell >= 0 ? !hers[cell] : !StoreMap.KarenWorksIn(StoreMap.AreaAt(position));
        }

        // "I looked and there was nothing here." Every swept cell keeps only the share of
        // its mass that could have gone unseen. A look into the outside rules out the share of
        // it she saw.
        public bool ApplySweep(List<int> cells, List<float> detectProbability)
        {
            float lookedOutside = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                float p = Mathf.Clamp01(detectProbability[i]);
                if (hers[c]) b[c] *= 1f - p;
                else lookedOutside += p;
            }
            if (OutsideCell >= 0 && lookedOutside > 0f)
                b[OutsideCell] *= 1f - Mathf.Clamp01(lookedOutside / outsideCells);
            return Normalise();
        }

        // Straight-line Gaussian: sightings, testimony, infrastructure. `confidence` blends
        // between "barely moves belief" and "belief collapses onto this point". When the point
        // is outside, the outside cell is where it says you are.
        void Multiply(Vector3 position, float sigma, float confidence, bool atOutside)
        {
            map.CellsWithin(position, sigma * 3f, nearby);
            MarkNearby();
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            for (int c = 0; c < b.Length; c++)
            {
                if (c == OutsideCell) { b[c] *= atOutside ? 1f : floor; continue; }
                if (!stamp[c]) { b[c] *= floor; continue; }
                Vector3 d = map.CellPosition[c] - position;
                d.y = 0f;
                b[c] *= floor + Mathf.Exp(-d.sqrMagnitude * inv);
            }
            ClearStamp();
        }

        // The same bump, measured in walking distance: a noise around a corner belongs to
        // the aisle it came from, not the one on the other side of the shelf.
        void MultiplyAlongFloor(Vector3 position, float sigma, float confidence, bool atOutside)
        {
            int source = map.CellAt(position);
            if (source < 0) { Multiply(position, sigma, confidence, atOutside); return; }

            distanceScratch = map.Distances(source, sigma * 3f, distanceScratch);
            float floor = LikelihoodFloor(confidence);
            float inv = 1f / (2f * sigma * sigma);

            for (int c = 0; c < b.Length; c++)
            {
                if (c == OutsideCell) { b[c] *= atOutside ? 1f : floor; continue; }
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

        // Sensed outside, near one of her exits: what's outside now waits by that one, fresh.
        void Regroup(Vector3 position)
        {
            if (OutsideCell < 0 || exitBorder.Count == 0) return;
            int nearest = 0;
            float bestSqr = float.MaxValue;
            for (int k = 0; k < exitBorder.Count; k++)
                foreach (int c in exitBorder[k])
                {
                    Vector3 d = map.CellPosition[c] - position;
                    float sqr = d.x * d.x + d.z * d.z;
                    if (sqr < bestSqr) { bestSqr = sqr; nearest = k; }
                }
            float mass = LineMass();
            ClearLines();
            line[nearest][0] = mass;
            lastExit = nearest;
        }

        // ---- the Director's bounded hint ---------------------------------------------

        // Moves up to `amount` of the total mass onto a region. The Director alone calls
        // this, and caps it (Karen.md §9.2); the grid just does the arithmetic. A hint toward
        // a place outside goes to the outside cell.
        public void Bias(int region, float amount)
        {
            if (region < 0 || amount <= 0f) return;
            Region r = map.Regions[region];
            if (r.Cells.Count == 0) return;

            amount = Mathf.Clamp01(amount);
            for (int c = 0; c < b.Length; c++) b[c] *= 1f - amount;
            float share = amount / r.Cells.Count;
            foreach (int c in r.Cells)
            {
                if (hers[c]) b[c] += share;
                else if (OutsideCell >= 0) b[OutsideCell] += share;
            }
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
                ClearLines();
                SyncLines();
                Refresh();
                return true;
            }

            float inv = 1f / sum;
            for (int c = 0; c < b.Length; c++) b[c] *= inv;
            SyncLines();
            Refresh();
            return false;
        }

        // The exit lines always add up to the outside cell's mass.
        void SyncLines()
        {
            if (OutsideCell < 0) return;
            float want = b[OutsideCell], have = LineMass();
            if (have > 1e-12f) ScaleLines(want / have);
            else if (want > 0f && line != null && line.Length > 0) line[lastExit][0] = want;
        }

        float LineMass()
        {
            float sum = 0f;
            if (line != null) foreach (float[] l in line) foreach (float m in l) sum += m;
            return sum;
        }

        void ScaleLines(float factor)
        {
            if (line == null) return;
            foreach (float[] l in line)
                for (int a = 0; a < l.Length; a++) l[a] *= factor;
        }

        void ClearLines()
        {
            if (line == null) return;
            foreach (float[] l in line) System.Array.Clear(l, 0, l.Length);
            lineClock = 0f;
        }

        // How much of the outside's mass went out by each exit.
        public float OutsideMassVia(int exit)
        {
            float sum = 0f;
            foreach (float m in line[exit]) sum += m;
            return sum;
        }

        // The exit most of the outside's mass went out by, or -1 with nothing outside.
        public int LikeliestExit()
        {
            if (OutsideCell < 0 || OutsideMass <= 0f) return -1;
            int best = 0;
            for (int k = 1; k < exitBorder.Count; k++)
                if (OutsideMassVia(k) > OutsideMassVia(best)) best = k;
            return best;
        }

        // Where an exit is: its border cell nearest the exit's middle.
        public Vector3 ExitPosition(int exit)
        {
            int[] border = exitBorder[exit];
            Vector3 sum = Vector3.zero;
            foreach (int c in border) sum += map.CellPosition[c];
            Vector3 middle = sum / border.Length;
            int best = border[0];
            float bestSqr = float.MaxValue;
            foreach (int c in border)
            {
                Vector3 d = map.CellPosition[c] - middle;
                if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; best = c; }
            }
            return map.CellPosition[best];
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
                if (key == null || r.Cells.Count == 0 || !hers[r.Cells[0]]) continue;   // the outside is one cell, not ruled out
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
