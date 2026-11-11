using UnityEngine;

namespace Kehai.Eval
{
    // A simulated player's trained mind (0.4.0, build plan §7.1, D8): the "choices" layer, on top
    // of the scripted reflexes. Where its choices come from:
    //   the trainer, through ML-Agents (MlAgentsEmployee, compiled with KAREN_MLAGENTS)
    //   a generated network (EmployeeBrain.g.cs, run by BrainNet) once Employee-1 is trained
    //   none: the scripted heuristic (SimulatedPlayer.Choose) chooses, as it always has
    public interface IEmployeeMind
    {
        string Name { get; }

        // Whether it will choose at all (a bridge with no trainer, or recording demonstrations,
        // leaves every choice to the heuristic, which then keeps its hiding reflex too).
        bool Active { get; }

        // Pick what, how and which among the unmasked options. `chances` (its probabilities for
        // the what) may be filled for the log. False: no choice, the heuristic picks.
        bool TryChoose(float[] inputs, bool[] what, bool[] how, bool[] which, Persona persona, System.Random dice, out EmployeeChoice choice);

        // What the last choice earned (EmployeeReward), handed over with the next one.
        void AddReward(float reward);

        // The shift is the episode; `careerOver` after its last shift.
        void EndShift(bool careerOver);
    }

    // A mind that learns from the heuristic: shown each scripted choice it didn't make, it records
    // it as a demonstration (behavioural cloning's warm start, §7.6).
    public interface IEmployeeDemonstrator
    {
        bool Recording { get; }
        void Demonstrate(float[] inputs, bool[] what, bool[] how, bool[] which, EmployeeChoice choice);
    }

    public static class EmployeeMinds
    {
        // Set by the ML-Agents bridge when it's compiled in.
        public static System.Func<IEmployeeMind> MlAgentsFactory;

        // -bot-mind trainer|generated|heuristic (default heuristic).
        public static IEmployeeMind Create(string kind)
        {
            string k = (kind ?? "heuristic").Trim();
            // "generated:EmployeeBrain_r1": an older round's mind, kept in the pool (§10.2).
            if (k.StartsWith("generated:", System.StringComparison.OrdinalIgnoreCase))
            {
                IEmployeeMind old = GeneratedEmployeeMind.TryCreate(out string why, k.Substring("generated:".Length));
                if (old == null) Debug.LogWarning("bot mind: " + why + "; the heuristic chooses");
                return old;
            }
            switch (k.ToLowerInvariant())
            {
                case "trainer": case "mlagents":
                    if (MlAgentsFactory != null) return MlAgentsFactory();
                    Debug.LogWarning("bot mind: the ML-Agents bridge isn't compiled in (KAREN_MLAGENTS); the heuristic chooses");
                    return null;
                case "generated":
                    IEmployeeMind net = GeneratedEmployeeMind.TryCreate(out string why);
                    if (net == null) Debug.LogWarning("bot mind: " + why + "; the heuristic chooses");
                    return net;
                default:
                    return null;
            }
        }

        // Dice, loaded by consistency (§7.4): an erratic persona draws from flatter chances, a
        // consistent one almost always takes its top choice. Returns the index drawn and its chance.
        public static int Draw(float[] logits, bool[] mask, float consistency, System.Random dice, out float chance)
        {
            float temperature = Mathf.Lerp(2f, 0.3f, Mathf.Clamp01(consistency));
            var p = new float[logits.Length];
            float max = float.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++) if (mask == null || mask[i]) max = Mathf.Max(max, logits[i]);
            float sum = 0f;
            for (int i = 0; i < logits.Length; i++)
            {
                p[i] = mask != null && !mask[i] ? 0f : Mathf.Exp((logits[i] - max) / temperature);
                sum += p[i];
            }
            if (sum <= 0f) { chance = 0f; return -1; }
            double u = dice.NextDouble() * sum, acc = 0.0;
            int last = -1;
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i] <= 0f) continue;
                last = i;
                acc += p[i];
                if (u < acc) { chance = p[i] / sum; return i; }
            }
            chance = last >= 0 ? p[last] / sum : 0f;
            return last;
        }
    }

    // Employee-1 and later, from generated C# (EmployeeBrain.g.cs, tools/learn/export_brain.py
    // --as EmployeeBrain). Until it is trained there is no such file, and this says so.
    public sealed class GeneratedEmployeeMind : IEmployeeMind
    {
        readonly Kehai.Karen.BrainNet net;
        public string Name => "generated " + net.Card.Name + " " + net.Card.Version;

        GeneratedEmployeeMind(Kehai.Karen.BrainNet net) => this.net = net;

        public static GeneratedEmployeeMind TryCreate(out string why, string name = "EmployeeBrain")
        {
            Kehai.Karen.BrainNet n = Kehai.Karen.BrainNets.Find(name, out why);
            if (n == null) return null;
            if (n.InputSize != EmployeeObservation.Size) { why = $"EmployeeBrain expects {n.InputSize} inputs, the observation has {EmployeeObservation.Size}"; return null; }
            return new GeneratedEmployeeMind(n);
        }

        public bool TryChoose(float[] inputs, bool[] what, bool[] how, bool[] which, Persona persona, System.Random dice, out EmployeeChoice choice)
        {
            choice = default;
            float[][] heads = net.Logits(inputs);
            if (heads.Length < 3) return false;
            float consistency = persona != null ? persona[Trait.Consistency] : 0.5f;
            int w = EmployeeMinds.Draw(heads[0], what, consistency, dice, out float c0);
            int h = EmployeeMinds.Draw(heads[1], how, consistency, dice, out float c1);
            int k = EmployeeMinds.Draw(heads[2], which, consistency, dice, out float c2);
            if (w < 0 || h < 0 || k < 0) return false;
            choice = new EmployeeChoice { What = (EmployeeWhat)w, How = (EmployeeHow)h, Which = (EmployeeWhich)k, Chance = c0 * c1 * c2, By = Name };
            return true;
        }

        public bool Active => true;
        public void AddReward(float reward) { }
        public void EndShift(bool careerOver) { }
    }
}
