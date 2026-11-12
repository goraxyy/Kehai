using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // A trained brain picking her (action, trick) pair (0.4.0, build plan §5, C5.1, C5.5, C5.7).
    //   generated   KarenBrainBasic.g.cs / KarenBrainVital.g.cs (or the model pack's Basic), run by BrainNet
    //   trainer     the ML-Agents bridge (MlAgentsBrain, KAREN_MLAGENTS), when the league says she learns
    // With neither, the planner decides, and the thought log says so once.
    public interface IBrainPolicy
    {
        string Name { get; }
        string Version { get; }

        // Pick one of `legal`; `plannerPick` is what the planner would take (the teacher for demos).
        // False: no pick, the planner's stands.
        bool Choose(float[] inputs, IReadOnlyList<BrainSeam.Pair> legal, int plannerPick, out int index, out float chance);

        void AddReward(float reward);
        void EndShift();
    }

    public static class BrainPolicies
    {
        // Set by the ML-Agents bridge when it's compiled in: (kind, the config) → the trainer's brain.
        public static System.Func<KarenBrainKind, KarenConfig, IBrainPolicy> TrainerFactory;

        public static IBrainPolicy Create(KarenConfig config, out string why)
        {
            why = null;
            if (config.brain == KarenBrainKind.Planner) return null;
            bool demos = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-karen-record-demos") >= 0;
            if (config.brainFromTrainer || demos)
            {
                if (TrainerFactory != null) return TrainerFactory(config.brain, config);
                why = "the ML-Agents bridge isn't compiled in (KAREN_MLAGENTS)";
                return null;
            }
            string name = config.brain == KarenBrainKind.Vital ? "KarenBrainVital" : "KarenBrainBasic";
            BrainNet net = BrainNets.Find(name, out why);
            if (net == null) return null;
            if (!BrainSeam.Accepts(net, out string refused) && refused != null) { why = refused; return null; }
            return new GeneratedBrain(net, config.seed, refused);
        }
    }

    // A frozen brain from generated C#: its chances over the legal pairs, and a draw with its own dice.
    public sealed class GeneratedBrain : IBrainPolicy
    {
        readonly BrainNet net;
        readonly System.Random dice;
        public readonly string Warning;

        public GeneratedBrain(BrainNet net, int seed, string warning = null)
        {
            this.net = net;
            dice = new System.Random(seed == 0 ? System.Environment.TickCount : seed * 31337 + 7);
            Warning = warning;
        }

        public string Name => net.Card.Name;
        public string Version => net.Card.Version;

        public bool Choose(float[] inputs, IReadOnlyList<BrainSeam.Pair> legal, int plannerPick, out int index, out float chance)
        {
            float[] p = BrainSeam.Chances(net, inputs, legal);
            index = BrainSeam.Draw(p, dice, out chance);
            return index >= 0;
        }

        public void AddReward(float reward) { }
        public void EndShift() { }
    }
}
