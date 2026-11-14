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
        bool TryChoose(ActionId goal, IReadOnlyList<Tactic> candidates, Tactic bandit, KarenContext c, out int chosen, out string why);

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

namespace Kehai.Karen
{
    // What the two Karens are paid for (0.4.0, build plan §5.4, C5.3). A shift is one episode; in
    // test runs a catch ends the shift.
    //
    //   Vital   a catch: +1, less the share of the shift that had passed; no catch: −1, however the
    //           shift ends; a fairness violation: −0.1 each. Your fear: nothing.
    //   Basic   pacing: −|panic − target| per second ÷ shift length (between −1 and 0 over a shift);
    //           the catch dial: w for a catch in a tense phase (Spike or Crunch), 0 otherwise (w starts
    //           at +0.15, turned between rounds until her catch rate sits in 0.20–0.35 a shift);
    //           sameness: −0.02 for each use of a trick past its third in a shift;
    //           a stuck shift: −0.5; a fairness violation: −0.1 each.
    public static class BrainReward
    {
        public const float Violation = -0.1f, Sameness = -0.02f, Stuck = -0.5f, CatchDialStart = 0.15f;
        public const int FreeUses = 3;

        public static float VitalCatch(float caughtAt, float shiftLength) => 1f - Mathf.Clamp01(caughtAt / Mathf.Max(1f, shiftLength));
        public const float VitalNoCatch = -1f;

        public static float BasicPacing(float panic, float target, float dt, float shiftLength) =>
            -Mathf.Abs(panic - target) * dt / Mathf.Max(1f, shiftLength);

        public static float BasicCatch(KarenDirector.Phase phase, float dial) =>
            phase == KarenDirector.Phase.Spike || phase == KarenDirector.Phase.Crunch ? dial : 0f;

        // The use about to be made, given the uses already this shift.
        public static float BasicSameness(int usesBefore) => usesBefore >= FreeUses ? Sameness : 0f;
    }

    // One shift's reward for one brain, step by step: what the trainer is handed with each decision.
    public sealed class BrainPay
    {
        public readonly KarenBrainKind Kind;
        public float Dial = BrainReward.CatchDialStart;
        public float Total { get; private set; }
        float pending;

        public BrainPay(KarenBrainKind kind) => Kind = kind;

        public void Pacing(float panic, float target, float dt, float length)
        {
            if (Kind == KarenBrainKind.Basic) Add(BrainReward.BasicPacing(panic, target, dt, length));
        }

        public void Used(int usesBefore)
        {
            if (Kind == KarenBrainKind.Basic) Add(BrainReward.BasicSameness(usesBefore));
        }

        public void Violations(int n) => Add(BrainReward.Violation * n);

        public void Caught(float at, float length, KarenDirector.Phase phase) =>
            Add(Kind == KarenBrainKind.Vital ? BrainReward.VitalCatch(at, length) : BrainReward.BasicCatch(phase, Dial));

        public void EndShift(bool caught, bool stuck)
        {
            if (Kind == KarenBrainKind.Vital && !caught) Add(BrainReward.VitalNoCatch);
            if (Kind == KarenBrainKind.Basic && stuck) Add(BrainReward.Stuck);
        }

        void Add(float r)
        {
            pending += r;
            Total += r;
        }

        // What's earned since the last ask.
        public float Take()
        {
            float r = pending;
            pending = 0f;
            return r;
        }

        public void Reset()
        {
            pending = 0f;
            Total = 0f;
        }
    }
}
