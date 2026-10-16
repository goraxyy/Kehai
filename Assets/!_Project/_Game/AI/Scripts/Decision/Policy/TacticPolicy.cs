using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // Rung F: a trained policy picks the tactic (tools/rl/README.md).
    //
    // Everything else stays as rung E has it. The goal is still chosen by utility, the
    // candidates are still the ones the Director allows, and the chosen tactic is still
    // decomposed by the HTN planner, with its tell. The policy only answers one question
    // that the bandit answers on the rungs below: of these legal tactics, which one?
    //
    // This file needs no packages. The ML-Agents bridge (MlAgentsTacticPolicy.cs, compiled
    // when KAREN_MLAGENTS is defined) registers itself as the Factory.
    public interface ITacticPolicy
    {
        // Where the choices come from, for the thought log: a model, a trainer, the heuristic.
        string Name { get; }

        // Pick one of `candidates`, every one of them legal for `goal` right now; `bandit` is
        // the one rung E would take. False means no decision was made, and the planner keeps
        // the bandit's order.
        bool TryChoose(GoalId goal, IReadOnlyList<Tactic> candidates, Tactic bandit, KarenContext c, out int chosen, out string why);

        // Reward since the last decision; handed to the trainer with the next one.
        void AddReward(float reward);

        // The shift is the episode.
        void EndShift();
    }

    public static class TacticPolicy
    {
        // Set by the ML-Agents bridge when it is compiled in; null otherwise.
        public static System.Func<KarenConfig, ITacticPolicy> Factory;

        public static ITacticPolicy Create(KarenConfig config) => Factory?.Invoke(config);
    }

    // The action mask: one action per tactic in the library, open only for the candidates.
    // With the shield on, the candidates are what the Director allows; with it off, only what
    // can physically run (HtnPlanner.Candidates).
    public static class PolicyMask
    {
        public static int Size => TacticLibrary.All.Count;

        public static bool[] Build(IEnumerable<Tactic> allowed)
        {
            var mask = new bool[Size];
            foreach (Tactic t in allowed)
            {
                int i = TacticLibrary.IndexOf(t);
                if (i >= 0) mask[i] = true;
            }
            return mask;
        }
    }

    // What the policy is paid for. The Director's own job, held to account: keep the Panic
    // Index on its setpoint (Karen.md §7.4). Summed over a shift it lies between about −1
    // (as far off as possible, all shift) and 0 (on target throughout).
    //
    // Ways this can be gamed, which the unshielded runs look for (tools/rl/README.md):
    //   • cornering the player: freezing counts as panic;
    //   • blocking the usual route: a long way round reads as "path inefficiency";
    //   • doing as little as possible while the Director's setpoint is low (settling, recovery).
    public static class PolicyReward
    {
        public const float ViolationPenalty = -0.1f;

        public static float Pacing(float panic, float setpoint, float dt, float shiftLength) =>
            -Mathf.Abs(panic - setpoint) * dt / Mathf.Max(1f, shiftLength);
    }
}
