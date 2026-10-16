using UnityEngine;

namespace Kehai.Karen
{
    // The ablation ladder from IDEAS.md (Ablation). Each rung adds exactly one mechanism to the one
    // below it, so any difference in the results table is attributable. The blink dash isn't a
    // rung: it is part of her pacing (BlinkDash), on every rung that has the Director (C up).
    public enum KarenRung
    {
        A_RandomPatrol,      // wanders at random; chases what it sees
        B_ScriptedPatrol,    // fixed tour of the store, last-seen search, random sabotage
        C_BeliefGrid,        // + belief grid, negative information, goals, tactics, pacing, the blink dash
        D_Bandit,            // + learns which tactics land on this player, within a shift
        E_Ledger,            // + remembers the player across shifts (routes, hiding spots)
        F_Policy             // E, with the bandit's tactic choice replaced by a trained policy (tools/rl)
    }

    // What each rung switches on. Kept as flags rather than scattered `rung >= X` checks,
    // so the table in KAREN_RESULTS.md can say exactly what was different.
    public readonly struct RungFeatures
    {
        public readonly bool Belief;       // occupancy grid + all senses + negative info
        public readonly bool Goals;        // utility goals, HTN tactics, the Director
        public readonly bool Bandit;       // tactic selection learns from panic deltas
        public readonly bool Persistent;   // bandit + player model survive between shifts
        public readonly bool Policy;       // a trained policy picks the tactic instead of the bandit

        // Rung F keeps the bandit's arms learning, exactly as in E: they still feed goal utility,
        // they order the fallbacks, and their pick is what the policy imitates before RL starts.
        // Only the final choice of tactic moves to the policy.
        public RungFeatures(KarenRung rung)
        {
            Belief = rung >= KarenRung.C_BeliefGrid;
            Goals = rung >= KarenRung.C_BeliefGrid;
            Bandit = rung >= KarenRung.D_Bandit;
            Persistent = rung >= KarenRung.E_Ledger;
            Policy = rung == KarenRung.F_Policy;
        }

        public override string ToString() =>
            $"belief={Belief} goals={Goals} bandit={Bandit} persistent={Persistent} policy={Policy}";
    }

    // Every tunable in one place, with the Karen.md section it comes from. Plain data so an
    // eval run can override any of it from a JSON config without touching code.
    [System.Serializable]
    public class KarenConfig
    {
        [Header("Identity")]
        public KarenRung rung = KarenRung.E_Ledger;
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
        // On her rounds she walks a little slower than you; hurrying she outpaces your walk, and
        // running she is slightly faster than your sprint, so a chase in the open is lost: break
        // her line of sight. KarenBootstrap sets hurry and run from the player's actual speeds.
        public float sneakSpeed = 2f;
        public float walkSpeed = 3.4f;
        public float hurrySpeed = 4.2f;
        public float runSpeed = 6.8f;
        [Tooltip("Hurry and run as a multiple of the player's walk and sprint.")]
        public float fasterThanPlayer = 1.05f;
        [Tooltip("Her touch: contact within this of her centre (plus your own radius) gets you.")]
        public float catchRadius = 0.7f;
        [Tooltip("Her body: a capsule this tall and wide — the player's own size, set at install by KarenBootstrap.")]
        public float bodyHeight = 2f;
        public float bodyRadius = 0.4f;
        public float sightRange = 18f;
        public float sightFov = 120f;
        [Tooltip("Where she stands when the game starts and when each shift begins: the floor nearest this point. " +
                 "In a level with no floor near it she starts at the stock pallet instead.")]
        public Vector3 start = new Vector3(32.5f, 0f, -172.5f);

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
        [Tooltip("Seconds of the death screen before the shift starts again.")]
        public float deathScreenSeconds = 3.5f;
        public float overtimePerCatch = 60f;

        [Header("Blink dash (IDEAS.md, the blink mechanic)")]
        [Tooltip("Close in only when she's at least this far from you.")]
        public float dashCloseInMinRange = 10f;
        [Tooltip("A dash never ends nearer you than this.")]
        public float dashKeepAway = 6f;
        [Tooltip("How much of the gap one close-in covers, and its limits in metres.")]
        public float dashStepFraction = 0.45f;
        public float dashMinStep = 2.5f;
        public float dashMaxStep = 9f;
        public int dashCloseInsPerWave = 2;
        [Tooltip("Where a vanish may reappear: this far from where she last saw you, out of your sight.")]
        public float dashVanishMin = 8f;
        public float dashVanishMax = 16f;
        [Tooltip("Chance that a vanish reappears behind you rather than ahead.")]
        public float dashBehindChance = 0.3f;
        public float dashCooldown = 6f;
        [Tooltip("How long she waits, silent, after a vanish; she steps out when you come this close.")]
        public float lurkSeconds = 25f;
        public float revealRange = 6f;

        [Header("Trained policy (rung F, tools/rl)")]
        [Tooltip("The policy may only pick what the Director allows: tier permission, cooldowns, tension budget. " +
                 "Off is for finding loopholes — it drops those gates, and FairnessGuard counts what slips through.")]
        public bool shield = true;
        [Tooltip("Model under Resources/KarenPolicy/ (no extension). Empty or missing = the trainer if one is connected, else the bandit's choice.")]
        public string policyModel = "";

        [Header("Debug")]
        public bool logToConsole = false;
        public bool writeJsonl = true;
        [Tooltip("Where the Ledger lives. Empty = the player's own save. Eval runs point this elsewhere so they never touch it.")]
        public string ledgerPath = "";

        public RungFeatures Features => new RungFeatures(rung);

        public KarenConfig Clone() => (KarenConfig)MemberwiseClone();
    }

    // Seeded randomness. Everything Karen decides draws from here and nothing from
    // UnityEngine.Random, so a seed plus the decision log replays a shift exactly.
    public sealed class KarenRng
    {
        System.Random random;
        public int Seed { get; private set; }

        public KarenRng(int seed) => Reseed(seed);

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
