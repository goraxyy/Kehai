using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // A trained network, run in plain C# (0.4.0, build plan §5.5, D6, C5.7). The game needs neither
    // ML-Agents nor a model file: tools/learn/export_brain.py writes the numbers as generated C#
    // (KarenBrainBasic.g.cs, KarenBrainVital.g.cs, EmployeeBrain.g.cs), each with a model card, and
    // the same numbers travel in the model pack (ModelPack.cs).
    //
    // The shape is ML-Agents' own actor, so a checkpoint's numbers drop straight in:
    //   inputs → normalised ((x − mean) / std, clamped to ±5) → [Linear → Swish] × layers
    //          → one Linear head per discrete branch → logits
    // Outputs are tied to names ("flush:blackout"), not positions: BrainSeam maps them onto the
    // legal pairs, a removed trick's output is ignored, and a new trick has none yet.
    public sealed class BrainCard
    {
        public string Name = "";              // KarenBrainBasic, KarenBrainVital, EmployeeBrain
        public string Version = "";           // e.g. "basic-r2-a@1.2M"
        public int InputsVersion;             // the observation's version it was trained on
        public bool Trained;                  // false: a hand-made stand-in (tests, or "not trained")
        public string TrainedAgainst = "";
        public long Steps;
        public string Validation = "";        // its validation scores, as text
        public string Command = "";           // the command that made it
        public string[][] OutputNames;        // per head: the name of each output

        public override string ToString() =>
            $"{Name} {Version} (inputs v{InputsVersion}, {(Trained ? $"{Steps} steps against {TrainedAgainst}" : "not trained")})";
    }

    public sealed class BrainNet
    {
        public readonly BrainCard Card;
        public int InputSize => mean.Length;
        public int Heads => headWeights.Length;

        readonly float[] mean, std;
        readonly float[][] weights, biases;   // per hidden layer: [out × in] row-major, [out]
        readonly int[] layerSizes;            // per hidden layer: out
        readonly float[][] headWeights, headBiases;
        readonly int[] headSizes;

        public BrainNet(BrainCard card, float[] mean, float[] std, int[] layerSizes, float[][] weights, float[][] biases,
                        int[] headSizes, float[][] headWeights, float[][] headBiases)
        {
            Card = card ?? new BrainCard();
            this.mean = mean;
            this.std = std;
            this.layerSizes = layerSizes;
            this.weights = weights;
            this.biases = biases;
            this.headSizes = headSizes;
            this.headWeights = headWeights;
            this.headBiases = headBiases;
            Validate();
        }

        void Validate()
        {
            int inSize = mean.Length;
            if (std.Length != inSize) throw new System.ArgumentException("normaliser sizes differ");
            for (int l = 0; l < layerSizes.Length; l++)
            {
                if (weights[l].Length != layerSizes[l] * inSize || biases[l].Length != layerSizes[l])
                    throw new System.ArgumentException($"layer {l}: {weights[l].Length} weights for {layerSizes[l]}×{inSize}");
                inSize = layerSizes[l];
            }
            for (int h = 0; h < headSizes.Length; h++)
                if (headWeights[h].Length != headSizes[h] * inSize || headBiases[h].Length != headSizes[h])
                    throw new System.ArgumentException($"head {h}: {headWeights[h].Length} weights for {headSizes[h]}×{inSize}");
            if (Card.OutputNames != null)
                for (int h = 0; h < Card.OutputNames.Length && h < headSizes.Length; h++)
                    if (Card.OutputNames[h] != null && Card.OutputNames[h].Length != headSizes[h])
                        throw new System.ArgumentException($"head {h}: {Card.OutputNames[h].Length} names for {headSizes[h]} outputs");
        }

        // The raw scores of every head. Doubles inside, so it matches the trainer to 1e-5.
        public float[][] Logits(float[] input)
        {
            if (input.Length != InputSize) throw new System.ArgumentException($"{Card.Name} takes {InputSize} inputs, given {input.Length}");
            var x = new double[InputSize];
            for (int i = 0; i < x.Length; i++)
            {
                double s = std[i] > 1e-8f ? std[i] : 1.0;
                double v = (input[i] - mean[i]) / s;
                x[i] = v < -5.0 ? -5.0 : v > 5.0 ? 5.0 : v;
            }
            for (int l = 0; l < layerSizes.Length; l++) x = Swish(Dense(x, weights[l], biases[l], layerSizes[l]));
            var outs = new float[headSizes.Length][];
            for (int h = 0; h < headSizes.Length; h++)
            {
                double[] y = Dense(x, headWeights[h], headBiases[h], headSizes[h]);
                outs[h] = new float[y.Length];
                for (int i = 0; i < y.Length; i++) outs[h][i] = (float)y[i];
            }
            return outs;
        }

        // A head's chances over the open outputs (closed ones get exactly 0).
        public static float[] Softmax(float[] logits, bool[] open)
        {
            var p = new float[logits.Length];
            double max = double.NegativeInfinity, sum = 0.0;
            for (int i = 0; i < logits.Length; i++) if (open == null || open[i]) max = System.Math.Max(max, logits[i]);
            if (double.IsNegativeInfinity(max)) return p;
            var e = new double[logits.Length];
            for (int i = 0; i < logits.Length; i++)
            {
                e[i] = open != null && !open[i] ? 0.0 : System.Math.Exp(logits[i] - max);
                sum += e[i];
            }
            for (int i = 0; i < p.Length; i++) p[i] = (float)(e[i] / sum);
            return p;
        }

        static double[] Dense(double[] x, float[] w, float[] b, int outSize)
        {
            int inSize = x.Length;
            var y = new double[outSize];
            for (int o = 0; o < outSize; o++)
            {
                double s = b[o];
                int row = o * inSize;
                for (int i = 0; i < inSize; i++) s += w[row + i] * x[i];
                y[o] = s;
            }
            return y;
        }

        static double[] Swish(double[] x)
        {
            for (int i = 0; i < x.Length; i++) x[i] = x[i] / (1.0 + System.Math.Exp(-x[i]));
            return x;
        }
    }

    // Where a trained network comes from: the model pack in use (ModelPack.Current), else the build's
    // generated class (Kehai.Karen.Generated.<name>.Build()), else none, with the reason.
    public static class BrainNets
    {
        static readonly Dictionary<string, BrainNet> cache = new Dictionary<string, BrainNet>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cache.Clear();

        public static void Forget() => cache.Clear();

        public static BrainNet Find(string name, out string why)
        {
            why = null;
            BrainNet fromPack = ModelPack.Current?.Brain(name);
            if (fromPack != null) return fromPack;
            if (cache.TryGetValue(name, out BrainNet net)) return net;

            System.Type type = typeof(BrainNets).Assembly.GetType("Kehai.Karen.Generated." + name);
            System.Reflection.MethodInfo build = type?.GetMethod("Build", System.Type.EmptyTypes);
            if (build == null) { why = $"no trained {name} yet (tools/learn/export_brain.py writes {name}.g.cs)"; return null; }
            net = build.Invoke(null, null) as BrainNet;
            if (net == null) { why = $"{name}.Build() made nothing"; return null; }
            cache[name] = net;
            return net;
        }
    }
}
