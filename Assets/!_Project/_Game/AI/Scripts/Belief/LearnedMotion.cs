using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Learned motion models (m2–m4), trained in tools/belief through a replay of Karen's own
    // filter and shipped as generated C# (MotionWeights.g.cs). Plain C#: no inference engine.
    //
    // Every model gives, for each state, a rate of leaving (per second) and a choice among the
    // ways out. The chance of leaving in a tick is 1 − exp(−rate·dt), so a model works at any
    // tick length, and mass only moves along walkable edges.
    //
    //   m2  log-linear: log rate = w·x(cell); each edge scores v·y(edge) + a bias per direction,
    //       and the choice is a softmax over the edges there are
    //   m3  m2 plus a small MLP per cell (48 → 32 → 32 → 9) that adds to the log rate and to
    //       each direction's score
    //   m4  m3 with a heading in the state: still, or one of eight directions. From (cell, h)
    //       she may move along an edge (and then head that way) or stop. A learned 9×8 table
    //       adds to each direction's score by the heading you had, so going on straight can be
    //       likelier than turning; the rates of moving and of stopping depend on it too.
    //
    // The kernel is rebuilt when the features change (MotionFeatures: per map, 2 s, 2 Hz) and
    // applied every believe tick. tools/belief/kernels.py computes the same numbers; the
    // EditMode parity test holds the two to 1e-5.
    public sealed class LearnedMotion : IMotionModel
    {
        public enum Kind { LogLinear, Mlp, Heading }

        public const int Hidden = 32;
        public const int HeadingCount = 9;

        // Weight packs, in the order tools/belief/export_cs.py writes them.
        public const int HeadSize = MotionFeatures.CellCount + 1 + MotionFeatures.EdgeCount + MotionFeatures.Slots + 1;
        public static int MlpOutputs(Kind k) => k == Kind.Heading ? 10 : 9;
        public static int MlpSize(Kind k) =>
            MotionFeatures.MlpInputs * Hidden + Hidden + Hidden * Hidden + Hidden + Hidden * MlpOutputs(k) + MlpOutputs(k);
        public const int HeadingSize = HeadingCount + HeadingCount * MotionFeatures.Slots + HeadingCount + MotionFeatures.CellCount + 1 + HeadingCount;
        public static int PackSize(Kind k) =>
            HeadSize + (k == Kind.LogLinear ? 0 : MlpSize(k)) + (k == Kind.Heading ? HeadingSize : 0);

        readonly Kind kind;
        readonly MotionFeatures features;
        readonly float[] w;

        // Offsets into w.
        readonly int rateW, rateB, edgeW, dirB, logLeak;
        readonly int w1, b1, w2, b2, w3, b3, outputs;
        readonly int rho, mom, sigma, stopW, stopB, prior;

        readonly float leak;
        readonly float[] headingPrior;

        // The kernel: per state the rate of leaving, per (edge, heading) the share that goes
        // that way, per state the share that stops (m4).
        float[] rate, edgeShare, stopShare, stay;
        // The linear head (refreshed with the features) and the MLP's outputs (refreshed with
        // the features, or a slice of the cells per tick).
        float[] linRate, linStop, linEdge, mlpOut;
        int builtMap = -1, builtSlow = -1, builtFast = -1, seenResets = -1, tick;
        StoreMap builtFor;
        bool mlpPending = true;
        float stayDt = -1f;
        bool stayStale = true;
        readonly float[] u = new float[MotionFeatures.MlpInputs];
        readonly float[] h1 = new float[Hidden], h2 = new float[Hidden];
        readonly float[] z = new float[MotionFeatures.Slots];

        public LearnedMotion(Kind kind, MotionFeatures features, float[] weights = null, int slices = 1)
        {
            this.kind = kind;
            this.features = features;
            Slices = Mathf.Max(1, slices);
            w = weights ?? MotionWeights.Pack(kind);
            if (w == null || w.Length != PackSize(kind))
                throw new System.ArgumentException($"{kind}: expected {PackSize(kind)} weights, got {(w == null ? 0 : w.Length)} — regenerate MotionWeights.g.cs");

            int at = 0;
            rateW = at; at += MotionFeatures.CellCount;
            rateB = at; at += 1;
            edgeW = at; at += MotionFeatures.EdgeCount;
            dirB = at; at += MotionFeatures.Slots;
            logLeak = at; at += 1;
            if (kind != Kind.LogLinear)
            {
                outputs = MlpOutputs(kind);
                w1 = at; at += MotionFeatures.MlpInputs * Hidden;
                b1 = at; at += Hidden;
                w2 = at; at += Hidden * Hidden;
                b2 = at; at += Hidden;
                w3 = at; at += Hidden * outputs;
                b3 = at; at += outputs;
            }
            if (kind == Kind.Heading)
            {
                rho = at; at += HeadingCount;
                mom = at; at += HeadingCount * MotionFeatures.Slots;
                sigma = at; at += HeadingCount;
                stopW = at; at += MotionFeatures.CellCount;
                stopB = at; at += 1;
                prior = at; at += HeadingCount;
                headingPrior = Softmax(w, prior, HeadingCount);
            }
            leak = Mathf.Exp(w[logLeak]);
        }

        // With Slices > 1 the MLP's part of the kernel is refreshed a slice of the cells per
        // believe tick (cell c when c mod Slices is the tick's), so each cell's is at most that
        // many ticks old; the linear head still follows every refresh. Slices = 1 rebuilds the
        // whole kernel at each refresh. tools/belief/kernels.py does the same, tick for tick.
        public int Slices { get; }

        public string Id => (kind == Kind.LogLinear ? "m2" : kind == Kind.Mlp ? "m3" : "m4") + (Slices > 1 ? "/" + Slices : "");
        public int Headings => kind == Kind.Heading ? HeadingCount : 1;
        public float Leak => leak;
        public float[] HeadingPrior => headingPrior;
        public int Rebuilds { get; private set; }

        static float[] Softmax(float[] v, int at, int n)
        {
            float max = float.NegativeInfinity;
            for (int i = 0; i < n; i++) max = Mathf.Max(max, v[at + i]);
            var p = new float[n];
            float sum = 0f;
            for (int i = 0; i < n; i++) { p[i] = Mathf.Exp(v[at + i] - max); sum += p[i]; }
            for (int i = 0; i < n; i++) p[i] /= sum;
            return p;
        }

        // ---- applying the kernel ------------------------------------------------------------

        public void Transition(BeliefGrid grid, float[] from, float[] to, float dt, float speed)
        {
            StoreMap map = grid.Map;
            if (features == null || features.Map != map) { System.Array.Copy(from, to, from.Length); return; }
            Prepare(map, grid.Resets);
            int hc = Headings;
            if (stayStale || dt != stayDt)
            {
                for (int s = 0; s < rate.Length; s++) stay[s] = Mathf.Exp(-rate[s] * dt);
                stayDt = dt;
                stayStale = false;
            }

            System.Array.Clear(to, 0, to.Length);
            for (int c = 0; c < map.CellCount; c++)
            {
                int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
                for (int h = 0; h < hc; h++)
                {
                    int s = c * hc + h;
                    float m = from[s];
                    if (m <= 0f) continue;
                    to[s] += m * stay[s];
                    float leave = m * (1f - stay[s]);
                    if (leave <= 0f) continue;
                    if (hc == 1)
                    {
                        for (int e = start; e < end; e++) to[map.EdgeTo[e]] += leave * edgeShare[e];
                    }
                    else
                    {
                        to[c * hc] += leave * stopShare[s];
                        for (int e = start; e < end; e++)
                            to[map.EdgeTo[e] * hc + 1 + features.EdgeSlot[e]] += leave * edgeShare[e * hc + h];
                    }
                }
            }
        }

        // ---- building it --------------------------------------------------------------------

        // The kernel for the features as they are now (the trace's kernel probe, the parity test).
        public void EnsureBuilt(StoreMap map) => Prepare(map, seenResets);

        void Prepare(StoreMap map, int resets)
        {
            bool newMap = builtFor != map || builtMap != features.MapVersion || rate == null;
            bool refreshed = builtSlow != features.SlowVersion || builtFast != features.FastVersion;
            if (newMap) Allocate(map);
            if (resets != seenResets) { seenResets = resets; mlpPending = true; }
            if (!newMap && !refreshed && (Slices == 1 || kind == Kind.LogLinear) && !(mlpPending && Slices > 1)) return;
            builtFor = map;
            builtMap = features.MapVersion;
            builtSlow = features.SlowVersion;
            builtFast = features.FastVersion;
            features.Tabulate();
            Rebuilds++;
            stayStale = true;

            if (newMap || refreshed)
                for (int c = 0; c < map.CellCount; c++) LinearHead(map, c);

            if (kind == Kind.LogLinear) { for (int c = 0; c < map.CellCount; c++) Assemble(map, c); return; }

            if (Slices == 1 || mlpPending || newMap)
            {
                for (int c = 0; c < map.CellCount; c++) Mlp(c);
                mlpPending = false;
                tick = 0;
                for (int c = 0; c < map.CellCount; c++) Assemble(map, c);
                return;
            }

            int slice = tick % Slices;
            tick++;
            for (int c = slice; c < map.CellCount; c += Slices) Mlp(c);
            if (newMap || refreshed) for (int c = 0; c < map.CellCount; c++) Assemble(map, c);
            else for (int c = slice; c < map.CellCount; c += Slices) Assemble(map, c);
        }

        void Allocate(StoreMap map)
        {
            int hc = Headings, n = map.CellCount, edges = map.EdgeTo.Length;
            rate = new float[n * hc];
            stay = new float[rate.Length];
            edgeShare = new float[edges * hc];
            stopShare = hc > 1 ? new float[rate.Length] : null;
            linRate = new float[n];
            linStop = new float[n];
            linEdge = new float[edges];
            mlpOut = new float[n * Mathf.Max(1, outputs)];
            mlpPending = true;
        }

        // Public for the parity test, which builds the kernel on a fixed state and compares.
        public void BuildKernel(StoreMap map, bool allocate = true)
        {
            if (allocate || rate == null) Allocate(map);
            features.Tabulate();
            for (int c = 0; c < map.CellCount; c++)
            {
                LinearHead(map, c);
                if (kind != Kind.LogLinear) Mlp(c);
                Assemble(map, c);
            }
            stayStale = true;
        }

        void LinearHead(StoreMap map, int c)
        {
            float[] X = features.X, Y = features.Y;
            int xc = c * MotionFeatures.CellCount;
            float r = w[rateB];
            for (int i = 0; i < MotionFeatures.CellCount; i++) r += w[rateW + i] * X[xc + i];
            linRate[c] = r;
            if (kind == Kind.Heading)
            {
                float st = w[stopB];
                for (int i = 0; i < MotionFeatures.CellCount; i++) st += w[stopW + i] * X[xc + i];
                linStop[c] = st;
            }
            for (int e = map.EdgeStart[c]; e < map.EdgeStart[c + 1]; e++)
            {
                float score = w[dirB + features.EdgeSlot[e]];
                for (int i = 0; i < MotionFeatures.EdgeCount; i++) score += w[edgeW + i] * Y[e * MotionFeatures.EdgeCount + i];
                linEdge[e] = score;
            }
        }

        // Rates and shares of cell c, from the linear head and the MLP's last outputs.
        void Assemble(StoreMap map, int c)
        {
            int start = map.EdgeStart[c], end = map.EdgeStart[c + 1];
            int oc = c * outputs;
            float logRate = linRate[c] + (outputs > 0 ? mlpOut[oc] : 0f);
            for (int k = 0; k < MotionFeatures.Slots; k++) z[k] = 0f;
            for (int e = start; e < end; e++)
            {
                int k = features.EdgeSlot[e];
                z[k] = linEdge[e] + (outputs > 0 ? mlpOut[oc + 1 + k] : 0f);
            }

            if (kind != Kind.Heading)
            {
                rate[c] = start == end ? 0f : Mathf.Exp(logRate);
                SoftmaxEdges(start, end, 0, 1f);
                return;
            }

            float logStop = linStop[c] + mlpOut[oc + 9];
            for (int h = 0; h < HeadingCount; h++)
            {
                int s = c * HeadingCount + h;
                float move = start == end ? 0f : Mathf.Exp(logRate + w[rho + h]);
                float stop = h == 0 ? 0f : Mathf.Exp(logStop + w[sigma + h]);
                float total = move + stop;
                rate[s] = total;
                stopShare[s] = total > 0f ? stop / total : 0f;
                SoftmaxEdges(start, end, h, total > 0f ? move / total : 0f);
            }
        }

        // The choice among a cell's edges for heading h, scaled by the share that moves at all.
        void SoftmaxEdges(int start, int end, int h, float moving)
        {
            int hc = Headings;
            float max = float.NegativeInfinity;
            for (int e = start; e < end; e++) max = Mathf.Max(max, Score(e, h));
            float sum = 0f;
            for (int e = start; e < end; e++) sum += Mathf.Exp(Score(e, h) - max);
            for (int e = start; e < end; e++) edgeShare[e * hc + h] = moving * Mathf.Exp(Score(e, h) - max) / sum;
        }

        float Score(int e, int h)
        {
            int k = features.EdgeSlot[e];
            return kind == Kind.Heading ? z[k] + w[mom + h * MotionFeatures.Slots + k] : z[k];
        }

        void Mlp(int c)
        {
            features.MlpInput(c, u);
            for (int j = 0; j < Hidden; j++)
            {
                float acc = w[b1 + j];
                for (int i = 0; i < MotionFeatures.MlpInputs; i++) acc += u[i] * w[w1 + i * Hidden + j];
                h1[j] = acc > 0f ? acc : 0f;
            }
            for (int j = 0; j < Hidden; j++)
            {
                float acc = w[b2 + j];
                for (int i = 0; i < Hidden; i++) acc += h1[i] * w[w2 + i * Hidden + j];
                h2[j] = acc > 0f ? acc : 0f;
            }
            int oc = c * outputs;
            for (int j = 0; j < outputs; j++)
            {
                float acc = w[b3 + j];
                for (int i = 0; i < Hidden; i++) acc += h2[i] * w[w3 + i * outputs + j];
                mlpOut[oc + j] = acc;
            }
        }

        // For the parity test: the kernel as built.
        public float[] Rates => rate;
        public float[] EdgeShares => edgeShare;
        public float[] StopShares => stopShare;
    }
}
