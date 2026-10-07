using UnityEngine;

namespace Kehai.Aiko
{
    // The ablation ladder from IDEAS.md §2. Each rung adds exactly one mechanism to the one
    // below it, so any difference in the results table is attributable.
    public enum AikoRung
    {
        A_RandomPatrol,      // wanders at random; chases what it sees
        B_ScriptedPatrol,    // fixed tour of the store, last-seen search, random sabotage
        C_BeliefGrid,        // + belief grid, negative information, goals, tactics, pacing
        D_Bandit,            // + learns which tactics land on this player, within a shift
        E_Ledger,            // + remembers the player across shifts (routes, hiding spots)
        F_Blink              // + the blink channel from the webcam
    }

    // What each rung switches on. Kept as flags rather than scattered `rung >= X` checks,
    // so the table in AIKO_RESULTS.md can say exactly what was different.
    public readonly struct RungFeatures
    {
        public readonly bool Belief;       // occupancy grid + all senses + negative info
        public readonly bool Goals;        // utility goals, HTN tactics, the Director
        public readonly bool Bandit;       // tactic selection learns from panic deltas
        public readonly bool Persistent;   // bandit + player model survive between shifts
        public readonly bool Blink;        // eyes-closed windows are exploited

        public RungFeatures(AikoRung rung)
        {
            Belief = rung >= AikoRung.C_BeliefGrid;
            Goals = rung >= AikoRung.C_BeliefGrid;
            Bandit = rung >= AikoRung.D_Bandit;
            Persistent = rung >= AikoRung.E_Ledger;
            Blink = rung >= AikoRung.F_Blink;
        }

        public override string ToString() =>
            $"belief={Belief} goals={Goals} bandit={Bandit} persistent={Persistent} blink={Blink}";
    }

    // Every tunable in one place, with the Aiko.md section it comes from. Plain data so an
    // eval run can override any of it from a JSON config without touching code.
    [System.Serializable]
    public class AikoConfig
    {
        [Header("Identity")]
        public AikoRung rung = AikoRung.F_Blink;
        [Tooltip("0 = pick from the clock. Any other value makes every decision reproducible (§9.8).")]
        public int seed = 0;

        [Header("Tick rates (§6.1)")]
        public float believeHz = 10f;
        public float appraiseHz = 5f;
        public float decideHz = 2f;
        public float directorHz = 5f;
        [Tooltip("Planner budget in milliseconds; past it the best partial plan is used. 0 = no limit (deterministic).")]
        public float planBudgetMs = 3f;

        [Header("Body")]
        // Walking, she's a little slower than you walking; hurrying, she outpaces you unless you
        // sprint; running, she is only just slower than a sprint. Never faster: AikoBootstrap caps every pace
        // under the player's actual sprint speed, whatever the scene sets it to.
        public float sneakSpeed = 2f;
        public float walkSpeed = 3.4f;
        public float hurrySpeed = 5f;
        public float runSpeed = 6.1f;
        public float catchRadius = 1.1f;
        public float sightRange = 18f;
        public float sightFov = 120f;

        [Header("Goal selection (§6.3)")]
        public float wConfidence = 1f;
        public float wStress = 0.6f;
        public float wTask = 0.7f;
        public float wNovelty = 0.25f;
        public float wCost = 0.35f;
        public float wRisk = 0.3f;
        public float hysteresis = 0.08f;
        public float minCommitSeconds = 2.5f;

        [Header("Bandit (§7.2)")]
        public bool thompson = false;
        public float ucbC = 0.35f;
        public float habituationLambda = 0.5f;
        public float habituationTau = 240f;      // seconds
        public float learningRate = 0.25f;       // α in Q̂ ← Q̂ + α(Δ − Q̂)
        public float rewardWindow = 20f;         // seconds of panic measured after a tactic
        public float priorWeight = 0.6f;

        [Header("Director (§7.3–7.4)")]
        public float kp = 0.8f;
        public float ki = 0.02f;
        public float integralLimit = 12f;
        public float tensionRegenPerSecond = 0.012f;
        public float panicAttack = 0.4f;
        public float panicDecay = 25f;
        public float baselineSeconds = 60f;
        public float careerSetpointRise = 0.02f;

        [Header("Fairness contract (§9)")]
        public float searchBiasPerMinute = 0.15f;
        public float searchBiasMaxMass = 0.5f;
        public float minTellLead = 0.8f;
        public float recoverySeconds = 45f;
        public float denyMaxCompletion = 0.8f;
        public float ambushMaxWait = 90f;
        public float ambushAbortPanic = 0.85f;

        [Header("Being caught (§8.6)")]
        public float lectureSeconds = 30f;
        public float overtimePerCatch = 60f;

        [Header("Blink channel (IDEAS.md)")]
        public float blinkAdvanceSpeed = 11f;
        public float blinkAdvanceMaxDistance = 3f;

        [Header("Debug")]
        public bool logToConsole = false;
        public bool writeJsonl = true;
        [Tooltip("Where the Ledger lives. Empty = the player's own save. Eval runs point this elsewhere so they never touch it.")]
        public string ledgerPath = "";

        public RungFeatures Features => new RungFeatures(rung);

        public AikoConfig Clone() => (AikoConfig)MemberwiseClone();
    }

    // Seeded randomness. Everything Aiko decides draws from here and nothing from
    // UnityEngine.Random, so a seed plus the decision log replays a shift exactly.
    public sealed class AikoRng
    {
        System.Random random;
        public int Seed { get; private set; }

        public AikoRng(int seed) => Reseed(seed);

        public void Reseed(int seed)
        {
            Seed = seed == 0 ? System.Environment.TickCount : seed;
            random = new System.Random(Seed);
        }

        public float Value => (float)random.NextDouble();
        public float Range(float min, float max) => min + (max - min) * Value;
        public int Range(int min, int maxExclusive) => random.Next(min, maxExclusive);
        public bool Chance(float p) => Value < p;

        // Standard normal via Box-Muller — for Thompson sampling.
        public float Gaussian()
        {
            double u1 = 1.0 - random.NextDouble();
            double u2 = random.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Sin(2.0 * System.Math.PI * u2));
        }

        public T Pick<T>(System.Collections.Generic.IList<T> list) => list.Count == 0 ? default : list[random.Next(list.Count)];
    }
}
