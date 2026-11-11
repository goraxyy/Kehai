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

    // Which brain answers "what next?" (0.4.0, build plan §5). The planner is today's game.
    public enum KarenBrainKind { Planner, Basic, Vital }

    // What she assumes you know of where she is (build plan §8.8).
    public enum MirrorMode { K0, K1, K2 }

    // Every tunable in one place, with the Karen.md section it comes from. Plain data so an
    // eval run can override any of it from a JSON config without touching code.
    [System.Serializable]
    public class KarenConfig
    {
        [Header("Identity")]
        public KarenRung rung = KarenRung.E_Ledger;
        [Tooltip("0 = pick from the clock. Any other value makes every decision reproducible (§9.8).")]
        public int seed = 0;
        [Tooltip("Tricks unlock shift by shift (the career table, each trick's IntroducedShift). Off since 8 November, " +
                 "the owner's call: every trick from shift 1 while they're tested. On brings the lock back.")]
        public bool careerTable = false;

        [Header("Tick rates (§6.1)")]
        public float believeHz = 10f;
        public float appraiseHz = 5f;
        public float decideHz = 2f;
        public float directorHz = 5f;
        [Tooltip("Planner budget in milliseconds; past it the best partial plan is used. 0 = no limit (deterministic).")]
        public float planBudgetMs = 3f;

        [Header("Body")]
        // On her rounds she walks a little slower than you; hurrying she outpaces your walk, and
        // hunting she sprints at 7.5 m/s against your 6.5 (0.2.0): she gains a metre a second, so
        // break her line of sight. KarenBootstrap sets hurry and run from the player's actual speeds.
        public float sneakSpeed = 2f;
        public float walkSpeed = 3.4f;
        public float hurrySpeed = 4.2f;
        public float runSpeed = 7.5f;
        [Tooltip("Hurry as a multiple of the player's walk.")]
        public float fasterThanPlayer = 1.05f;
        [Tooltip("Hunt's sprint as a multiple of the player's sprint: 7.5 m/s against 6.5.")]
        public float sprintsFasterThanPlayer = 7.5f / 6.5f;
        [Tooltip("How long a hunt keeps running on her belief after she loses you, with nothing new.")]
        public float huntMemorySeconds = 6f;
        [Tooltip("What a hunt costs from the Director's tension budget, and how long after one before the next: the Hunt action's pacing, whatever trick it runs.")]
        public float huntTensionCost = 0.6f;
        public float huntCooldownSeconds = 90f;
        [Tooltip("Unless she's running (a chase), she stops this far from you: her touch kills, and she doesn't walk into you by accident.")]
        public float personalSpace = 2.2f;
        [Tooltip("Her touch: contact within this of her centre (plus your own radius) gets you — her body's radius and a hand's breadth, so passing her in an aisle isn't contact.")]
        public float catchRadius = 0.5f;
        [Tooltip("Her body: a capsule this tall and wide — the player's own size, set at install by KarenBootstrap.")]
        public float bodyHeight = 2f;
        public float bodyRadius = 0.4f;
        [Header("Her eyes (0.2.0: KarenBody.ConfigureEye)")]
        [Tooltip("A cone 150° wide and 36 m long, at half strength 16 m away. The cameras and shoppers keep 18 m, 120° and 8 m.")]
        public float sightRange = 36f;
        public float sightFov = 150f;
        public float sightHalfStrength = 16f;
        [Tooltip("Awareness per second at a score of 1: as low as still hits the walk-by targets (AwarenessTests).")]
        public float sightGain = 1.65f;
        [Tooltip("A score below this is nothing seen: crouched and mostly hidden in the dim stays unseen.")]
        public float sightFloor = 0.04f;
        [Tooltip("Seconds her awareness holds after losing you, before it drains.")]
        public float sightGrace = 0.5f;
        [Tooltip("Up to this much more gain where her belief already expects you: 1 doubles it.")]
        public float sightExpectedBoost = 1f;
        [Tooltip("Cells her \"nothing here\" sweep checks a tick, all of them inside her cone.")]
        public int sweepCellsPerTick = 120;
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
        [Tooltip("The combo learner (0.2.0): the bandit sees her last trick and the combo room. Off, it's rung E's bandit as before, for the paired comparison.")]
        public bool comboContext = true;

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
        public float recoverySeconds = 45f;
        public float denyMaxCompletion = 0.8f;
        public float ambushMaxWait = 90f;
        public float ambushAbortPanic = 0.85f;

        [Header("Her tricks (0.3.0)")]
        [Tooltip("How far anyone can see inside her fog, in metres: you, her, her cameras, Kenji. " +
                 "The F1 developer overlay has a slider for it.")]
        [Range(0.5f, 10f)] public float fogVisibility = 2f;

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

        [Header("0.4.0: two Karens, her senses, the mirror map (build plan §5, §8.7, §8.8)")]
        [Tooltip("What picks her action and trick: the hand-built planner (today), Basic (the game's) or Vital (catch-oriented).")]
        public KarenBrainKind brain = KarenBrainKind.Planner;
        [Tooltip("What she assumes you know of her: K0 where she is (today), K1 where you last sensed her, K2 the mirror map.")]
        public MirrorMode mirror = MirrorMode.K0;
        [Tooltip("Her senses' numbers: s0 today's hand-set ones, s1 fitted, s2 fitted with walls and lights.")]
        public string senses = "s0";
        [Tooltip("Her belief's predict step: p0 today's, p1 fitted, p2 destinations, p3 destinations reading your Ledger.")]
        public string motion = "p0";
        [Tooltip("The hunter: the planner with pacing and the Director's hint off, every fairness rule on (Vital's baseline).")]
        public bool hunter;
        [Tooltip("Her brain's picks come from the ML-Agents trainer (the league's learner), not from its generated C#.")]
        public bool brainFromTrainer;
        [Tooltip("Write one learning record per decision (Diagnostics/LearningRecord.cs).")]
        public bool learningRecords;
        [Tooltip("Where the learning records go. Empty: <persistent data>/learning/learning_<launch>.jsonl.")]
        public string learningPath = "";
        [Tooltip("A model pack to load instead of the build's own numbers (Decision/Policy/ModelPack.cs). Empty: the newest good pack, else the build's.")]
        public string modelPack = "";

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
