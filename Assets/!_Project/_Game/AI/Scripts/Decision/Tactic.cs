using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Karen
{
    // One entry in the tactic library (Karen.md §8).
    //
    // The spec asks for ScriptableObject assets; this repository carries code only, so a
    // tactic is a class instead — with exactly the same contract. Adding a scare is adding a
    // subclass with its preconditions, cost, cooldown, expected-panic prior, the tell it
    // must emit and the chore it leaves behind. Nothing else in Karen has to change.
    public abstract class Tactic
    {
        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract GoalId[] Goals { get; }
        public abstract int Tier { get; }               // 0 movement, 1 cheap, 2 moderate, 3 big, 4 the chase
        public abstract int IntroducedShift { get; }    // career table, §10.2
        public abstract float TensionCost { get; }
        public abstract float Cooldown { get; }         // seconds
        public abstract float PanicPrior { get; }       // expected panic delta before any learning
        public abstract TellKind Tell { get; }
        public virtual float TellLead => 1f;
        public abstract string Chore { get; }           // what it leaves the player to do
        public abstract string Attacks { get; }         // sight, work, sound, trust, space, social, you

        // Movement goals (patrol, sweep...) aren't bandit arms — they're how she gets about.
        public virtual bool Learnable => Tier > 0;

        [System.NonSerialized] public float LastUsed = -9999f;
        [System.NonSerialized] public int UsesThisShift;

        public bool OnCooldown(float now) => now - LastUsed < Cooldown;

        // Preconditions. `why` explains a refusal for the thought log.
        public virtual bool Available(KarenContext c, out string why)
        {
            why = null;
            return true;
        }

        // How exposed doing this leaves her (§6.3 w_risk). 0 = unseen, 1 = in plain view.
        public virtual float ExposureRisk(KarenContext c) => 0f;

        // HTN decomposition into primitives. Build through PlanBuilder so a planning budget
        // overrun yields the best partial plan instead of a stall.
        public abstract void Plan(KarenContext c, PlanBuilder plan);

        public override string ToString() => Id;
    }

    // Collects a plan's steps and enforces the planner's anytime budget.
    public sealed class PlanBuilder
    {
        public readonly List<Primitive> Steps = new List<Primitive>();
        readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        readonly float budgetMs;

        // A budget of zero or less means no wall-clock cut-off: plans then depend only on the
        // seed, which the eval harness needs for "same seed, same shift".
        public bool OverBudget => budgetMs > 0f && watch.Elapsed.TotalMilliseconds > budgetMs;
        public bool Truncated { get; private set; }
        public string Target;            // what the plan is aimed at, for the log

        public PlanBuilder(float budgetMs) => this.budgetMs = budgetMs;

        // Anytime: past the budget, further steps are dropped and the plan runs as far as it
        // got. It never comes back empty — the first steps are always kept, so a slow frame
        // (a first-call JIT, say) degrades a plan instead of cancelling it.
        public PlanBuilder Add(Primitive step)
        {
            if (OverBudget && Steps.Count >= 2)
            {
                Truncated = true;
                return this;
            }
            Steps.Add(step);
            return this;
        }

        public PlanBuilder Go(Vector3 at, KarenBody.Pace pace, string label, float arrive = 1.3f) =>
            Add(MoveTo.Point(at, pace, label, arrive));
    }

    // Every tactic, registered once. Tests iterate this to check the fairness contract.
    public static class TacticLibrary
    {
        static List<Tactic> all;

        public static IReadOnlyList<Tactic> All
        {
            get
            {
                if (all == null) Build();
                return all;
            }
        }

        static void Build()
        {
            all = new List<Tactic>
            {
                // movement (tier 0)
                new PatrolTactic(), new InvestigateTactic(), new SweepTactic(), new PollWitnessesTactic(),
                new WithdrawTactic(),

                // §8.1 sight
                new BlackoutTactic(), new MirrorBlackTactic(), new FogTactic(), new CameraBoltOnTactic(),
                // §8.2 work
                new ShelfSweepTactic(), new SpillTactic(), new BinTamperTactic(), new TaskFalsificationTactic(),
                new ToolTheftTactic(), new OvertimeTactic(),
                // §8.3 sound and trust
                new PaDecoyTactic(), new PaAnnouncePositionTactic(), new PaTaskReadbackTactic(),
                new PaCountdownTactic(), new PaFootstepsTactic(), new PhantomChimeTactic(), new SilenceTactic(),
                // §8.4 space
                new CrateWallTactic(), new DoorLockTactic(), new ShelfRelocationTactic(), new FunnelTactic(),
                // §8.5 social
                new MimicryTactic(), new WitnessTactic(), new UnderstudyTactic(),
                // §8.6 you
                new StalkTactic(), new AmbushTactic(), new ChaseTactic(), new FollowTactic(), new FavourTactic(),
                // IDEAS.md blink channel
                new BlinkAdvanceTactic()
            };
        }

        public static IEnumerable<Tactic> For(GoalId goal) => All.Where(t => t.Goals.Contains(goal));

        public static Tactic Get(string id) => All.FirstOrDefault(t => t.Id == id);

        public static void ResetShift()
        {
            foreach (Tactic t in All)
            {
                t.UsesThisShift = 0;
                t.LastUsed = -9999f;
            }
        }
    }
}
