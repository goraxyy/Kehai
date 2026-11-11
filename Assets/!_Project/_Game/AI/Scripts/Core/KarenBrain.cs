using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Karen — the orchestrator (Karen.md §2, §6.1).
    //
    // Owns the three minds and runs them at their own rates:
    //
    //   Sense     every frame      the eye integrates; the plan's current primitive runs
    //   Believe   10 Hz            sensorium → belief grid, then predict
    //   Appraise   5 Hz            grid + director → the handful of scalars decisions use
    //   Director   5 Hz            panic, setpoint, tension, permissions
    //   Decide     2 Hz or event   goal by utility → tactic → plan (HTN, anytime)
    //
    // The rung (IDEAS.md, Ablation) decides how much of this is switched on. A and B are the old
    // patrol-and-chase guard, kept as the ablation floor; C and up are Karen.
    [DefaultExecutionOrder(100)]
    public sealed class KarenBrain : MonoBehaviour
    {
        public static KarenBrain Instance { get; private set; }

        public KarenConfig config = new KarenConfig();

        [Tooltip("Every tactic regardless of the career table — for testing.")]
        public bool careerOverride;

        // ---- the parts ----------------------------------------------------------------
        public StoreMap Map { get; private set; }
        public BeliefGrid Belief { get; private set; }
        public KarenBody Body { get; private set; }
        public KarenDirector Director { get; private set; }
        public KarenLedger Ledger { get; private set; }
        public KarenWorld World { get; private set; }
        public ThoughtLog Log { get; } = new ThoughtLog();
        public KarenRng Rng { get; private set; }
        public Sensorium Sensorium { get; private set; }
        public EnergyBelief Energy { get; } = new EnergyBelief();
        public ActionSet Actions { get; } = new ActionSet();
        public KarenContext Ctx { get; private set; }
        public PerformanceReview Review { get; private set; }
        public KarenStats Stats { get; } = new KarenStats();
        public BlinkDash Dash { get; } = new BlinkDash();   // her move while you blink (Decision/BlinkDash.cs)
        public LearningRecorder Learning { get; } = new LearningRecorder();   // one line per decision (0.4.0, C1.1)
        static readonly string sessionStamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        public TaskManager Tasks { get; private set; }
        public ShiftManager Shift { get; private set; }
        public BurnoutSystem Burnout { get; private set; }

        public PlanTree CurrentPlan { get; private set; }
        public ITacticPolicy Policy { get; private set; }   // rung F only
        public bool ShiftActive { get; private set; }
        public float ShiftTime => ShiftActive ? Time.time - shiftStartedAt : 0f;
        public bool CareerOverride => careerOverride;
        public bool FinalShiftException { get; set; }

        float shiftStartedAt;
        float believeTimer, appraiseTimer, decideTimer, directorTimer, desireTimer;
        bool decideNow;
        float planStartedAt;
        bool chasing;
        int violationsSeen;

        readonly Dictionary<int, float> lastVisited = new Dictionary<int, float>();
        readonly Dictionary<int, float> closedUntil = new Dictionary<int, float>();
        readonly HashSet<int> closedNow = new HashSet<int>();
        readonly HashSet<int> closedAll = new HashSet<int>();
        readonly List<(float t, Vector3 p)> heard = new List<(float, Vector3)>();
        readonly List<(Tactic tactic, float t0, float p0, string after)> pendingRewards = new List<(Tactic, float, float, string)>();

        // Her last trick, for the combo learner (0.2.0).
        string lastTrick;
        float lastTrickAt = -999f;
        readonly HashSet<ShelfUnit> sabotaged = new HashSet<ShelfUnit>();
        readonly Dictionary<TaskManager.TaskKind, float> peakOutstanding = new Dictionary<TaskManager.TaskKind, float>();

        // ---- lifecycle -----------------------------------------------------------------

        void Awake()
        {
            Instance = this;
            Tasks = FindAnyObjectByType<TaskManager>();
            Shift = FindAnyObjectByType<ShiftManager>();
            Burnout = FindAnyObjectByType<BurnoutSystem>();
        }

        void Start()
        {
            // Before the map: the bake that keeps her body to her part of the building.
            string area = KarenArea.Install();
            Map = StoreMap.Current;
            Rng = new KarenRng(config.seed);
            Ledger = new KarenLedger(config) { Persistent = config.Features.Persistent };
            Ledger.Load();
            Director = new KarenDirector(config, Ledger);
            World = gameObject.GetOrAdd<KarenWorld>();
            Review = new PerformanceReview(this);

            Body = KarenBody.Build(transform, config, StartPosition());
            Body.Touched += OnTouched;

            RebuildBelief();
            Log.EchoToConsole = config.logToConsole;

            Ctx = new KarenContext
            {
                Brain = this, Body = Body, Map = Map, Belief = Belief, Director = Director, Ledger = Ledger,
                World = World, Log = Log, Rng = Rng, Config = config, Tasks = Tasks, Shift = Shift
            };
            InstallPolicy();
            Learning.Enabled = config.learningRecords;
            if (Learning.Enabled) Learning.FilePath = string.IsNullOrEmpty(config.learningPath) ? LearningRecorder.DefaultPath(sessionStamp) : config.learningPath;

            TraceRegistry.Listen();
            NoiseBus.Emitted += OnNoise;
            World.Pa.ChimeStarted += OnPaChime;
            World.Pa.SpeechStarted += OnPaSpeech;
            GameEvents.ShelfRestocked += OnRestocked;
            GameEvents.SpillCleaned += OnSpillRoute;
            GameEvents.BinBagged += OnBinRoute;
            GameEvents.BagDisposed += OnSkipRoute;
            GameEvents.CoffeeDrunk += OnCoffeeRoute;
            GameEvents.CustomerServed += OnServed;
            GameEvents.PunchAttempted += OnClockRoute;
            GameEvents.DoorUsed += OnDoorSensor;

            // Your answers to her tricks (0.3.0): what you did, and how long it took.
            FogState.Filled += OnFogFilled;
            FogState.Cleared += OnFogCleared;
            Understudy.SentAway += OnUnderstudySentAway;
            if (World != null) World.Pa.PowerChanged += OnPaPower;

            if (Shift != null)
            {
                Shift.ShiftStateChanged += OnShiftStateChanged;
                Shift.FreezeChanged += OnFreezeChanged;
                if (Shift.IsShiftActive) BeginShift();
            }

            Note("SYSTEM", $"{GameNames.Antagonist} online — rung {config.rung} ({config.Features}), seed {Rng.Seed}. Map: {Map.Summary()}");
            Note("SYSTEM", area);
        }

        // Routes end at a job: the Ledger learns the paths taken between them.
        void OnSpillRoute(Dirt d) => Ledger.CompleteRoute("spill");
        void OnBinRoute(Trashcan b) => Ledger.CompleteRoute("bin");
        void OnSkipRoute(TrashBag b) => Ledger.CompleteRoute("skip");
        void OnCoffeeRoute(Vector3 p) => Ledger.CompleteRoute("coffee");
        void OnClockRoute(bool accepted) => Ledger.CompleteRoute("time clock");

        // The events are static and outlive the scene; an eval reset reloads it, so every
        // subscription is undone here or the old Karen would go on listening.
        void OnDestroy()
        {
            NoiseBus.Emitted -= OnNoise;
            GameEvents.ShelfRestocked -= OnRestocked;
            GameEvents.SpillCleaned -= OnSpillRoute;
            GameEvents.BinBagged -= OnBinRoute;
            GameEvents.BagDisposed -= OnSkipRoute;
            GameEvents.CoffeeDrunk -= OnCoffeeRoute;
            GameEvents.PunchAttempted -= OnClockRoute;
            GameEvents.CustomerServed -= OnServed;
            GameEvents.DoorUsed -= OnDoorSensor;
            FogState.Filled -= OnFogFilled;
            FogState.Cleared -= OnFogCleared;
            Understudy.SentAway -= OnUnderstudySentAway;
            if (World != null && World.Pa != null) World.Pa.PowerChanged -= OnPaPower;
            if (Shift != null) Shift.FreezeChanged -= OnFreezeChanged;
            Director?.Dispose();
            if (Shift != null) Shift.ShiftStateChanged -= OnShiftStateChanged;
            Sensorium?.Dispose();
            if (Instance == this) Instance = null;
        }

        void RebuildBelief()
        {
            Sensorium?.Dispose();
            Belief = new BeliefGrid(Map);
            Predictor = new RoutePredictor(Map);
            Sensorium = new Sensorium(Map, Energy);
            ApplyRungToSenses();
            if (Ctx != null)
            {
                Ctx.Map = Map;
                Ctx.Belief = Belief;
            }
        }

        void ApplyRungToSenses()
        {
            bool full = config.Features.Belief;
            Sensorium.UseHearing = full;
            Sensorium.UseTraces = full;
            Sensorium.UseTestimony = full;
            Sensorium.UseInfrastructure = full;
            Sensorium.UseNegative = full;
            Sensorium.SweepCellsPerTick = config.sweepCellsPerTick;
            // Awareness D (0.2.0): her eyes are quicker where her belief already puts you.
            if (Body != null) Body.Sight.Expectation = full ? Belief.Expectation : (System.Func<Vector3, float>)null;
        }

        // Swap rung mid-session (the ablation runner does this between episodes).
        public void SetRung(KarenRung rung)
        {
            config.rung = rung;
            Ledger.Persistent = config.Features.Persistent;
            ApplyRungToSenses();
            InstallPolicy();
            Note("SYSTEM", $"rung → {rung} ({config.Features})");
        }

        // Rung F's tactic chooser. Without the ML-Agents bridge compiled in there is none, and
        // the planner keeps the bandit's choice — said once, so a results table can't pass it
        // off as a policy.
        void InstallPolicy()
        {
            Policy = config.Features.Policy ? TacticPolicy.Create(config) : null;
            Ctx.Policy = Policy;
            if (!config.Features.Policy) return;
            Note("SYSTEM", Policy != null
                ? $"tactic choice: {Policy.Name}{(config.shield ? string.Empty : " — UNSHIELDED: the Director's gates are off")}"
                : "rung F, but the ML-Agents bridge isn't compiled in (KAREN_MLAGENTS) — the bandit chooses, as on rung E");
        }

        // ---- shifts ---------------------------------------------------------------------

        void OnShiftStateChanged()
        {
            if (Shift == null) return;
            if (Shift.IsShiftActive && !ShiftActive) BeginShift();
            else if (!Shift.IsShiftActive && ShiftActive) EndShift(!KilledThisShift);
        }

        public void BeginShift()
        {
            int number = Shift != null ? Mathf.Max(1, Shift.ShiftNumber) : 1;
            if (config.seed != 0) Rng.Reseed(config.seed * 7919 + number);

            Log.Clear();
            Stats.BeginShift(number);
            TacticLibrary.ResetShift();
            TraceRegistry.Clear();
            closedUntil.Clear();
            closedNow.Clear();
            pendingRewards.Clear();
            PlaceWatch.Clear();
            lastTrick = null;
            lastTrickAt = -999f;
            sabotaged.Clear();
            peakOutstanding.Clear();
            KilledThisShift = false;
            Dash.BeginShift();
            Learning.BeginShift();
            biasSeen = Director.BiasApplied;

            // Between shifts the store may be rearranged (§5.5). Done before anything reads
            // the map for this shift.
            if (config.Features.Goals && number >= 7 && !careerOverride)
            {
                var moves = MazeMutation.Mutate(Rng, 2, out string report);
                Note("DIRECTOR", report.TrimEnd());
                if (moves.Count > 0) MazeChanged();
            }

            Ledger.BeginShift(number);
            Energy.BeginShift(number - 1, Burnout);
            Director.BeginShift(number, Shift != null ? Shift.shiftDurationSeconds : 300f);

            Ledger.FillHabitPrior(Map, Belief.HabitPrior);
            RefreshDesire();
            // She knows you clocked in: the time clock is hers.
            Landmark? clock = Map.FindLandmark(LandmarkKind.TimeClock);
            if (clock.HasValue) Belief.ResetTo(clock.Value.Position, 3f);
            else Belief.ResetToPrior();

            Sensorium.ForgetShift();
            Actions.Reset();
            AbortPlan();
            Body.Sight.ResetAwareness();
            Body.Warp(StartPosition());
            Body.SetMood(KarenBody.Mood.Calm);

            ShiftActive = true;
            shiftStartedAt = Time.time;
            decideNow = true;
            violationsSeen = FairnessGuard.Violations;

            Note("SYSTEM", $"shift {number} begins — employee {Ledger.PlayerName}, shift {number} of the career");
        }

        // Where she starts the game and every shift: the floor nearest her config's spot, or the
        // stock pallet in a level with no floor there.
        Vector3 StartPosition()
        {
            if (NavMesh.SamplePosition(config.start, out NavMeshHit hit, 5f, KarenArea.AgentMask)) return hit.position;
            Landmark? home = Map.FindLandmark(LandmarkKind.StockCrateHome);
            return home.HasValue ? home.Value.Position : transform.position;
        }

        public void EndShift(bool clockedOut)
        {
            if (!ShiftActive) return;
            ShiftActive = false;
            Dash.Cancel(Ctx);
            AbortPlan();
            Director.EndShift();
            Policy?.EndShift();
            Learning.EndShift();
            float energy = Burnout != null ? Burnout.Energy01 : 1f;
            PlaceWatch.EndShift(Ctx, Time.time);   // before the Ledger fades what it knows
            Ledger.EndShift(ShiftTime, energy, clockedOut);
            Stats.EndShift(this);
            if (config.writeJsonl)
            {
                string path = Log.Flush(Stats.Shift, Rng.Seed);
                Note("SYSTEM", "thought log written to " + path);
            }
            Review.Compose();
            World.ClearShift();
            Body.Stop();
        }

        // ---- the loop ---------------------------------------------------------------------

        void Update()
        {
            if (Body == null) return;
            float dt = Time.deltaTime;

            using (ShiftTiming.Measure("sight")) Body.Sight.Tick(dt);
            if (!ShiftActive)
            {
                Body.Stop();
                return;
            }

            if (!config.Features.Belief)
            {
                SimpleGuard(dt);
                TickDirector(dt);
                return;
            }

            believeTimer += dt;
            if (believeTimer >= 1f / config.believeHz) { using (ShiftTiming.Measure("believe")) Believe(believeTimer); believeTimer = 0f; }

            appraiseTimer += dt;
            if (appraiseTimer >= 1f / config.appraiseHz) { using (ShiftTiming.Measure("appraise")) Appraise(); appraiseTimer = 0f; }

            using (ShiftTiming.Measure("director")) TickDirector(dt);
            using (ShiftTiming.Measure("predictor")) UpdatePredictor(dt);

            // What her tricks' places paid, once a second (0.3.0).
            placeTimer += dt;
            if (placeTimer >= 1f) { placeTimer = 0f; using (ShiftTiming.Measure("places")) PlaceWatch.Tick(Ctx, Time.time); }

            // After a vanish she does nothing but wait (BlinkDash.TickLurk).
            if (Dash.Lurking)
            {
                Dash.TickLurk(Ctx);
                ExpireClosures();
                return;
            }

            decideTimer += dt;
            if (decideNow || decideTimer >= 1f / config.decideHz)
            {
                bool forced = decideNow;
                decideNow = false;
                decideTimer = 0f;
                using (ShiftTiming.Measure("decide")) Decide(forced);
            }

            using (ShiftTiming.Measure("plan")) RunPlan(dt);
            ExpireClosures();
        }

        void TickDirector(float dt)
        {
            directorTimer += dt;
            if (directorTimer < 1f / config.directorHz) return;
            float step = directorTimer;
            directorTimer = 0f;

            Ctx.A = Appraised();
            Director.Tick(step, Ctx);
            if (ShiftActive) Dash.Tick(Ctx);
            Director.ObserveForLedger(Ctx, step);
            if (ShiftActive) Stats.SamplePacing(Director.Panic, Director.Setpoint);
            if (ShiftActive) PayPolicy(step);
            SettleRewards();
            if (ShiftActive && Learning.Enabled)
            {
                if (Director.BiasApplied > biasSeen) Learning.HintFired(Director.LastBiasAt);
                biasSeen = Director.BiasApplied;
                Learning.Tick(Time.time, Director.Panic);
            }
        }

        float biasSeen;

        // One learning record (C1.1): what she knew, what she could pick, what she picked. Built
        // from what Plan already ranked, so it draws nothing and changes nothing.
        void RecordDecision(PlanTree plan, Tactic tactic)
        {
            if (!Learning.Enabled || plan == null || tactic == null) return;
            List<Tactic> legal = HtnPlanner.LastCandidates;
            var names = new string[legal.Count];
            for (int i = 0; i < legal.Count; i++) names[i] = PickKey(plan.Action, legal[i]);
            Learning.Add(new LearningRecord
            {
                Shift = Stats.Shift, Seed = Rng.Seed, Player = Ledger.PlayerName, Rung = config.rung.ToString(),
                T = ShiftTime, Now = Time.time,
                Brain = "planner", BrainVersion = "hand",
                Goal = plan.Action.ToString(),
                InputsVersion = PolicyObservation.Version,
                Inputs = PolicyObservation.Build(plan.Action, Ctx),
                Legal = names,
                Pick = PickKey(plan.Action, tactic),
                Chance = 1f,
                Panic = Director.Panic
            });
        }

        // "flush:blackout": an (action, trick) pair by name (build plan §5.5, item 6).
        public static string PickKey(ActionId action, Tactic tactic) =>
            action.ToString().ToLowerInvariant() + ":" + (tactic != null ? tactic.Id : "none");

        // Rung F's reward (PolicyReward): how close panic sat to the setpoint over this tick,
        // less anything the fairness guard caught since the last one.
        void PayPolicy(float dt)
        {
            if (Policy == null) return;
            float length = Shift != null ? Shift.shiftDurationSeconds : 300f;
            Policy.AddReward(PolicyReward.Pacing(Director.Panic, Director.Setpoint, dt, length));
            int violations = FairnessGuard.Violations;
            if (violations > violationsSeen) Policy.AddReward(PolicyReward.ViolationPenalty * (violations - violationsSeen));
            violationsSeen = violations;
        }

        // ---- believe ----------------------------------------------------------------------

        bool wasSeeing;
        float lostSightAt = -1f;

        void Believe(float dt)
        {
            Energy.Tick(dt);
            Sensorium.Collect(Body.Position, Body.Sight, Body.transform);

            foreach (Observation o in Sensorium.Positive)
            {
                if (Belief.Apply(o)) Note("BELIEF", $"lost the scent — everything ruled out, back to the prior (after {o.Label})");
                if (o.Channel == SenseChannel.Hearing) heard.Add((Time.time, o.Position));
                if (o.Channel != SenseChannel.Sight && o.Confidence > 0.3f) NoteSense(o);
            }

            bool seeing = Body.Sight.Awareness >= Body.Sight.seeAt;
            if (seeing && !wasSeeing)
            {
                Stats.Detected(Time.time - shiftStartedAt);
                Note("SENSE", $"sight {Body.Sight.Band} ({Body.Sight.Awareness:0.00}) at {Map.Describe(Body.Sight.LastSeenPosition)}");
                if (ShiftActive) KarenNarrator.Say(StoryKind.Seen, $"{GameNames.Antagonist} spotted you in {KarenNarrator.Place(Body.Sight.LastSeenPosition)}!", Body.Sight.LastSeenPosition);
                lostSightAt = -1f;
            }
            else if (!seeing && wasSeeing) lostSightAt = Time.time;
            if (lostSightAt > 0f && Time.time - lostSightAt > 3f)
            {
                lostSightAt = -1f;
                if (ShiftActive) KarenNarrator.Say(StoryKind.Seen, GameNames.Antagonist + " lost sight of you.", Body.Sight.LastSeenPosition);
            }
            wasSeeing = seeing;
            NarrateGuess();

            if (Sensorium.SweptCells.Count > 0 && Belief.ApplySweep(Sensorium.SweptCells, Sensorium.SweptProbability))
                Note("SENSE", "NEGATIVE sweep ruled out everything — back to the prior");

            Director.MaybeBias(Ctx, dt);
            Belief.Predict(dt, Energy.SpeedEstimate);

            int here = Map.RegionAt(Body.Position);
            if (here >= 0) lastVisited[here] = Time.time;

            desireTimer += dt;
            if (desireTimer > 2f) { desireTimer = 0f; RefreshDesire(); }

            while (heard.Count > 0 && Time.time - heard[0].t > 10f) heard.RemoveAt(0);
            if (Sensorium.LoudNoiseNearby && CurrentPlan != null && CurrentPlan.Action != ActionId.Hunt
                && !IgnoresNoise(CurrentPlan.Tactic, PlanAge)) decideNow = true;
        }

        float lastSenseNote;

        void NoteSense(in Observation o)
        {
            // Rate-limited: footsteps would otherwise drown the log.
            if (o.Channel == SenseChannel.Hearing && Time.time - lastSenseNote < 3f) return;
            lastSenseNote = Time.time;
            Note("SENSE", $"{o.Channel} {o.Label} at {Map.Describe(o.Position)} (conf {o.Confidence:0.00})");
            if (!ShiftActive) return;
            switch (o.Channel)
            {
                case SenseChannel.Hearing:
                    KarenNarrator.Say(StoryKind.Heard, $"{GameNames.Antagonist} heard {KarenNarrator.Evidence(o.Label)} near {KarenNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Testimony:
                    KarenNarrator.Say(StoryKind.Heard, $"A customer told {GameNames.Antagonist} they saw you near {KarenNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Trace:
                    KarenNarrator.Say(StoryKind.Heard, $"{GameNames.Antagonist} found a trace of you — {KarenNarrator.Evidence(o.Label)} — near {KarenNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Infrastructure:
                {
                    string what = KarenNarrator.Evidence(o.Label), where = KarenNarrator.Place(o.Position);
                    KarenNarrator.Say(StoryKind.Heard, what.Contains("door") && where.Contains("door")
                        ? $"{GameNames.Antagonist}'s door sensors saw someone use {where}."
                        : $"{GameNames.Antagonist}'s sensors picked up {what} {KarenNarrator.In(o.Position)}.", o.Position);
                    break;
                }
            }
        }

        void OnNoise(NoiseEvent e)
        {
            // A crash close by re-decides immediately rather than waiting up to half a second.
            if (!ShiftActive || e.Author != NoiseAuthor.Player || e.Loudness < 0.7f) return;
            if (Vector3.Distance(e.Position, Body.Position) < e.Carry * 0.5f) decideNow = true;
        }

        // Unfinished work, as a distance field: where the employee is drawn to.
        void RefreshDesire()
        {
            var targets = new List<int>();
            foreach (KeyValuePair<string, Vector3> t in FairnessGuardTargets())
            {
                int cell = Map.CellAt(t.Value);
                if (cell >= 0) targets.Add(cell);
            }
            if (targets.Count == 0)
            {
                for (int i = 0; i < Belief.DesireDistance.Length; i++) Belief.DesireDistance[i] = float.PositiveInfinity;
                return;
            }
            float[] field = Map.DistancesFrom(targets);
            System.Array.Copy(field, Belief.DesireDistance, field.Length);
        }

        // The same list the fairness guard checks — every open job and the time clock. It
        // names objects in the store, not the employee, so Karen may use it: she wrote it.
        static IEnumerable<KeyValuePair<string, Vector3>> FairnessGuardTargets() => FairnessGuard.RequiredTargets(StoreMap.Current);

        public int LikelyNextJobRegion
        {
            get
            {
                // With the lights out, the job is the lights (0.2.0): Ambush waits on that way.
                if (!PowerSystem.PowerOn)
                {
                    Landmark? box = Map.FindLandmark(LandmarkKind.BreakerBox);
                    if (box.HasValue && box.Value.Region >= 0) return box.Value.Region;
                }
                float[] fromBelief = TacticHelpers.DistanceFromBelief(Ctx);
                int best = -1;
                float bestDistance = float.MaxValue;
                foreach (KeyValuePair<string, Vector3> t in FairnessGuardTargets())
                {
                    int cell = Map.CellAt(t.Value);
                    if (cell < 0) continue;
                    float d = fromBelief[cell];
                    if (d < bestDistance) { bestDistance = d; best = Map.CellRegion[cell]; }
                }
                return best;
            }
        }

        // ---- appraise ---------------------------------------------------------------------

        void Appraise() => Ctx.A = Appraised();

        Appraisal Appraised()
        {
            var a = new Appraisal
            {
                Confidence = Belief.Confidence,
                Entropy = Belief.Entropy,
                EntropyNorm = Belief.EntropyNormalised,
                Staleness = Mathf.Min(999f, Belief.Staleness),
                Panic = Director.Panic,
                Setpoint = Director.Setpoint,
                Pressure = Director.Pressure,
                Tension = Director.Tension,
                TaskLoad = TaskLoad(),
                EnergyBelief = Energy.Energy,
                Awareness = Body.Sight.Awareness,
                PeakRegion = Belief.PeakRegion,
                PeakPosition = Belief.PeakPosition,
                InRecovery = Director.InRecovery,
                Blinking = EyesClosed
            };

            // Containment: belief mass within a short walk of the peak.
            float[] d = TacticHelpers.DistanceFromBelief(Ctx);
            float mass = 0f;
            for (int c = 0; c < d.Length; c++) if (d[c] < 8f) mass += Belief[c];
            a.Containment = mass;
            return a;
        }

        // How close the employee is to clocking out: the share of standing jobs done, scaled
        // by how far through the shift it is. A tidy store at 9 a.m. isn't freedom — the
        // customers haven't arrived yet — but a tidy store with the doors shut is.
        public float TaskLoad()
        {
            if (Tasks == null || !Tasks.HasTasks) return 0f;
            int total = 0, done = 0;
            foreach (TaskManager.ShiftTask t in Tasks.Tasks)
            {
                total++;
                if (t.IsComplete) done++;
                TrackOutstanding(t.Kind);
            }
            float complete = total > 0 ? (float)done / total : 0f;
            float closeness = 1f;
            if (Shift != null && Shift.TimeRemaining > 0f)
                closeness = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(1f - Shift.TimeRemaining / Mathf.Max(1f, Shift.shiftDurationSeconds)));
            return complete * closeness;
        }

        void TrackOutstanding(TaskManager.TaskKind kind)
        {
            float now = Outstanding(kind);
            peakOutstanding.TryGetValue(kind, out float peak);
            if (now > peak) peakOutstanding[kind] = now;
        }

        float Outstanding(TaskManager.TaskKind kind)
        {
            switch (kind)
            {
                case TaskManager.TaskKind.Mop: return Dirt.ActiveCount;
                case TaskManager.TaskKind.Stock: return ShelfUnit.NotFullCount;
                case TaskManager.TaskKind.Trash: return Tasks.TrashOutstanding + TrashBag.ActiveCount;
                case TaskManager.TaskKind.Serve: return CustomerNPC.WaitingCount;
                default: return CustomerRequest.PendingCount;
            }
        }

        // How far through a job the employee is, against the worst it's been this shift.
        public float TaskProgress(TaskManager.TaskKind kind)
        {
            peakOutstanding.TryGetValue(kind, out float peak);
            if (peak <= 0f) return 1f;
            return Mathf.Clamp01(1f - Outstanding(kind) / peak);
        }

        // ---- decide -----------------------------------------------------------------------

        // Finish what she starts (0.3.0, the owner's call of 8 November). She used to decide again
        // on the walk to a hands-on trick's spot, twice a second and at every loud noise, and drop
        // it: in the ablation's check Spill landed once in 55 starts. Now a hands-on trick under
        // way is kept unless a hunt wins. Its own steps can still fail, rule 10 still stops it if
        // the doors close before it lands, and after CommitSeconds she decides as usual, so a plan
        // that can't finish doesn't hold her.
        public const float CommitSeconds = 60f;

        public static bool FinishesFirst(Tactic running, float runningFor, ActionId next) =>
            running != null && running.IsHandsOn && runningFor < CommitSeconds && next != ActionId.Hunt;

        public static bool IgnoresNoise(Tactic running, float runningFor) =>
            running != null && running.IsHandsOn && runningFor < CommitSeconds;

        float PlanAge => Time.time - planStartedAt;

        void Decide(bool forced)
        {
            // A trick started from the developer overlay runs to its end.
            if (forcedPlan && CurrentPlan != null) return;
            forcedPlan = false;

            Ctx.A = Appraised();
            Ctx.Last = LastTrickNow();
            Tactic running = CurrentPlan?.Tactic;
            bool finishing = IgnoresNoise(running, PlanAge);
            ActionId goal = Actions.Choose(Ctx, forced, out string because, commit: !finishing);
            if (finishing)
            {
                if (FinishesFirst(running, PlanAge, goal)) return;
                Actions.Commit(goal, Ctx);
                Note("PLAN", $"PLAN     {CurrentPlan.Name} dropped for {goal.Name()}");
            }

            bool keep = CurrentPlan != null && CurrentPlan.Action == goal && !forced;
            if (keep) return;

            WriteBelief();
            Log.Write(Record("GOAL", $"GOAL     {HtnPlanner.Describe(Actions.LastOptions)}", Actions.LastOptions, goal.ToString(), because));
            Note("", $"  why    {because}");

            AbortPlan();
            PlanTree plan = HtnPlanner.Plan(goal, Ctx, Interrupt, out Tactic tactic, out List<ThoughtOption> options, out string why);
            if (plan == null)
            {
                Note("PLAN", $"PLAN     {goal.Name()}: nothing workable ({why}) → roam");
                plan = HtnPlanner.Plan(ActionId.Roam, Ctx, Interrupt, out tactic, out options, out why);
                if (plan == null) return;
            }

            if (tactic.TensionCost > 0f && !Director.TrySpend(tactic.TensionCost))
            {
                if (HtnPlanner.Shielded(Ctx))
                {
                    Note("PLAN", $"PLAN     {tactic.Id} unaffordable at the last moment → patrol");
                    return;
                }
                Note("PLAN", $"PLAN     {tactic.Id} over the tension budget — unshielded, so it goes ahead");
            }
            FairnessGuard.CheckPacing(tactic, Director);

            tactic.LastUsed = Time.time;
            tactic.UsesThisShift++;
            Ledger.MarkUsed(tactic, Time.time);
            if (tactic.Learnable)
            {
                // A trick that starts while her last one is in play is a chain's second part.
                pendingRewards.Add((tactic, Time.time, Director.Panic, Ledger.ChainedOn(tactic, Ctx)));
                lastTrick = tactic.Id;
                lastTrickAt = Time.time;
            }
            Stats.Used(tactic);

            CurrentPlan = plan;
            planStartedAt = Time.time;

            // A hunt is the Hunt action, whichever of its moves or tricks runs (0.2.0): the chase
            // state, its cost and the recovery after follow the action, not one step of it.
            if (plan.Action == ActionId.Hunt) { if (!chasing) SetChasing(true); }
            else if (chasing) SetChasing(false);
            Log.Write(Record("PLAN", $"PLAN     {tactic.Id} → {why}", options, tactic.Id, why));
            RecordDecision(plan, tactic);
            NarratePlan(plan);
        }

        void WriteBelief()
        {
            Log.Write(Record("BELIEF",
                $"BELIEF   peak={Map.RegionName(Belief.PeakRegion)} p={Belief.Confidence:0.00} H={Belief.Entropy:0.00} stale={Mathf.Min(999f, Belief.Staleness):0.0}s",
                null, null, null));
        }

        // The global interrupt branch of every plan (§6.5).
        bool Interrupt(KarenContext c)
        {
            if (CurrentPlan == null) return false;
            ActionId g = CurrentPlan.Action;
            bool exempt = g == ActionId.Hunt || g == ActionId.Loom || g == ActionId.LieInWait || g == ActionId.Lure || g == ActionId.BackOff;
            // Only when a hunt could actually start: with Hunt cooling down (0.2.0), a sighting
            // would otherwise cut the plan that's already hurrying to you, every tick.
            if (!exempt && c.A.Awareness >= Body.Sight.seeAt && Director.ActionPermission(ActionId.Hunt, c) > 0f
                && HtnPlanner.Candidates(ActionId.Hunt, c, null).Count > 0)
            {
                Note("", "  interrupt: SeeingPlayerAtRange");
                return true;
            }
            if (g != ActionId.Hunt && Sensorium.LoudNoiseNearby && !IgnoresNoise(CurrentPlan.Tactic, PlanAge))
            {
                Note("", "  interrupt: LoudNoiseAdjacent");
                return true;
            }
            return false;
        }

        void RunPlan(float dt)
        {
            if (CurrentPlan == null) { decideNow = true; return; }

            Status s = CurrentPlan.Tick(Ctx, dt);
            if (s == Status.Running) return;

            if (s == Status.Failure && CurrentPlan.StoppedAtClosing)
                Note("PLAN", $"PLAN     {CurrentPlan.Name} stopped: {FairnessGuard.ClosedWhy}");
            else if (s == Status.Failure && !CurrentPlan.Interrupted && CurrentPlan.Plan.ViolatedGuard != null)
                Note("PLAN", $"PLAN     {CurrentPlan.Name} aborted — precondition violated: {CurrentPlan.Plan.ViolatedGuard}");
            else if (s == Status.Failure && !CurrentPlan.Interrupted)
                Note("PLAN", $"PLAN     {CurrentPlan.Name} failed at step {CurrentPlan.Plan.Index + 1}/{CurrentPlan.Plan.Count}");

            CurrentPlan = null;
            decideNow = true;
            Body.Stop();   // a finished plan doesn't leave her walking on towards its last destination
        }

        // The dash takes over after a vanish, and hands back when she steps out or gives up.
        public void HandOverToDash() => AbortPlan();
        public void ResumeDeciding() => decideNow = true;

        // ---- your answers (0.3.0) -------------------------------------------------------------

        readonly Dictionary<string, float> fogFilled = new Dictionary<string, float>();
        float clockStoppedAt = -1f;

        void OnFogFilled(string room) => fogFilled[room] = Time.time;

        void OnFogCleared(string room)
        {
            if (!ShiftActive) return;   // the shift's end clears it, not you
            float since = fogFilled.TryGetValue(room, out float at) ? Time.time - at : -1f;
            fogFilled.Remove(room);
            Answered("fog_reset", since, $"the fog in {room} reset");
        }

        void OnFreezeChanged()
        {
            if (Shift == null || !ShiftActive) return;
            if (Shift.IsFrozen) { clockStoppedAt = Time.time; return; }
            Answered("clock_restarted", clockStoppedAt >= 0f ? Time.time - clockStoppedAt : -1f, "the time clock restarted");
            clockStoppedAt = -1f;
        }

        void OnUnderstudySentAway(float seconds) => Answered("understudy_sent_away", seconds, "Kenji sent to the tills");

        void OnPaPower(bool on)
        {
            if (!on && ShiftActive) Answered("pa_off", -1f, "the PA switched off");
        }

        // Written to the Ledger and the thought log. The tricks' own places hear the same answer
        // through their watches (PlaceRewards); this is the record of what you did.
        public void Answered(string kind, float seconds, string what)
        {
            Ledger.RecordCounterplay(kind, seconds);
            Stats.Answers.Add((kind, seconds));
            Note("LEARN", seconds >= 0f ? $"LEARN    answer: {what} after {seconds:0} s" : $"LEARN    answer: {what}");
        }

        // ---- placement memory's rewards (0.3.0) ---------------------------------------------

        // Each trick leaves a watch on the place it used; this asks them once a second.
        public PlaceRewards PlaceWatch { get; } = new PlaceRewards();
        float placeTimer;

        // ---- the developer overlay: force a trick (0.3.0) ----------------------------------

        bool forcedPlan;
        public bool ForcedPlanRunning => forcedPlan && CurrentPlan != null;

        // Start a trick now, past the Director's gate (tier, cooldown, budget, unlock shift) and
        // the drafts list, but not past its own preconditions: a trick with nothing to work on
        // says why. It isn't learned from, but it does count as her last trick, so a forced
        // Tool theft then a forced Ambush tries the combo. Developer builds and the editor only.
        public bool Force(string tacticId, out string why)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Tactic t = TacticLibrary.Get(tacticId);
            if (t == null) { why = "no trick called " + tacticId; return false; }
            if (!ShiftActive) { why = "no shift running"; return false; }
            Ctx.A = Appraised();
            Ctx.Last = LastTrickNow();
            if (!t.Available(Ctx, out why)) { why = why ?? "not available"; return false; }

            var builder = new PlanBuilder(0f);
            t.Plan(Ctx, builder);
            if (builder.Steps.Count == 0) { why = "it found nothing to do"; return false; }

            AbortPlan();
            ActionId goal = t.Actions.Length > 0 ? t.Actions[0] : ActionId.Roam;
            CurrentPlan = new PlanTree($"{goal.Name()}({t.Id})", goal, t, builder.Steps, _ => false) { Target = builder.Target };
            forcedPlan = true;
            planStartedAt = Time.time;
            t.LastUsed = Time.time;
            t.UsesThisShift++;
            if (t.Learnable) { lastTrick = t.Id; lastTrickAt = Time.time; }
            Stats.Used(t);
            Note("PLAN", $"PLAN     {t.Id} forced from the developer overlay → {builder.Target}");
            NarratePlan(CurrentPlan);
            why = null;
            return true;
#else
            why = "developer builds only";
            return false;
#endif
        }

        void AbortPlan()
        {
            if (CurrentPlan == null) return;
            CurrentPlan.Abort(Ctx);
            CurrentPlan = null;
            if (chasing) SetChasing(false);
            Body.DropCarried();   // a theft cut short leaves the mop where she stood (0.3.0)
            Body.Silent = false;
            Body.SweepHead(0f);
            Body.Stop();
        }

        // ---- learning -----------------------------------------------------------------------

        // Each tactic's reward is the change in panic over the window after it ran (§7.2).
        void SettleRewards()
        {
            for (int i = pendingRewards.Count - 1; i >= 0; i--)
            {
                var (tactic, t0, p0, after) = pendingRewards[i];
                if (Time.time - t0 < config.rewardWindow) continue;
                pendingRewards.RemoveAt(i);
                float delta = Director.MeanPanic(t0, t0 + config.rewardWindow) - p0;
                if (config.Features.Bandit)
                {
                    ChainStat found = Ledger.Settle(tactic.Id, after, delta);
                    if (after != null) NoteChain(after, tactic, delta, found);
                    else Note("LEARN", $"LEARN    {tactic.Id} Δpanic {delta:+0.00;-0.00} → Q̂ {Ledger.Alone(tactic.Id):+0.00;-0.00}");
                    if (Mathf.Abs(delta) >= 0.08f)
                        KarenNarrator.Say(StoryKind.Learned, delta > 0f
                            ? $"{GameNames.Antagonist} noticed that {KarenNarrator.Tactic(tactic.Id).TrimEnd('!')} rattled you. She'll remember."
                            : $"{GameNames.Antagonist} noticed that {KarenNarrator.Tactic(tactic.Id).TrimEnd('!')} didn't bother you.");
                }
                Stats.Rewarded(tactic, delta);
            }
        }

        // ---- the combo room (0.2.0) ---------------------------------------------------------

        LastTrick LastTrickNow()
        {
            if (!config.comboContext || lastTrick == null) return default;
            Tactic t = TacticLibrary.Get(lastTrick);
            return new LastTrick { Id = lastTrick, Ago = Time.time - lastTrickAt, Running = t != null && t.EffectRunning(Ctx) };
        }

        void NoteChain(string first, Tactic then, float delta, ChainStat found)
        {
            ChainStat chain = found ?? Ledger.Room.Find(first, then.Id);
            Note("LEARN", $"LEARN    chain {chain.Name} Δpanic {delta:+0.00;-0.00} → {chain.q:+0.00;-0.00} over {chain.n:0.#} uses (alone {Ledger.Alone(then.Id):+0.00;-0.00})");
            if (found == null) return;

            Note("COMBO", $"COMBO    found {found.Name} on shift {found.found}: {found.n:0.#} uses, paid {found.q:+0.00;-0.00} against {found.alone:+0.00;-0.00} alone");
            Stats.ChainFound(found);
            if (ShiftActive)
                KarenNarrator.Say(StoryKind.Learned,
                    $"{GameNames.Antagonist} found a combo: {KarenNarrator.Tactic(first).TrimEnd('!')}, then {KarenNarrator.Tactic(then.Id).TrimEnd('!')}.");
        }

        // ---- tells and effects -------------------------------------------------------------

        // A trick's sound, logged where it played. Until 0.2.0 each had to come 0.8 s before its
        // effect (fairness rule 3) and the audit timed the gap; the rule is gone, the log stays.
        // Told each time a trick sounds: what, where, and how long the sound runs.
        public event System.Action<TellKind, Vector3, float> Told;

        public void RecordTell(TellKind kind, Vector3 at, float lead)
        {
            FairnessGuard.NoteTell(kind, lead);
            Told?.Invoke(kind, at, lead);
            if (ShiftActive && kind != TellKind.PaChime && kind != TellKind.Footsteps)
                KarenNarrator.Say(StoryKind.Warning, $"Warning: {KarenNarrator.Tell(kind)} near {KarenNarrator.Place(at)} — something's about to happen.", at);
            Note("TELL", $"TELL     {kind} at {Map.Describe(at)} ({lead:0.0}s lead)");
        }

        public void RecordEffect(string label)
        {
            // The first effect of a plan is its trick landing (0.3.0): started isn't landed.
            if (CurrentPlan != null && CurrentPlan != landedPlan && CurrentPlan.Tactic != null)
            {
                landedPlan = CurrentPlan;
                Stats.Landed(CurrentPlan.Tactic);
            }
            Note("EFFECT", "EFFECT   " + label);
        }
        PlanTree landedPlan;

        // An effect that carries its own sound before it — PA speech, after its chime.
        void RecordToldEffect(string label, string by, float toldAt) =>
            Note("EFFECT", $"EFFECT   {label} ({by} {Time.time - toldAt:0.0}s before)");

        void OnPaChime(PaAnnouncement a) => RecordTell(TellKind.PaChime, Body.Position, PaSystem.ChimeSeconds);   // storewide; logged where she is
        void OnPaSpeech(PaAnnouncement a)
        {
            RecordToldEffect("PA: " + a.Text, "chime", a.Started);
            KarenNarrator.Say(StoryKind.Store, $"{GameNames.Antagonist} over the speakers: \"{a.Text}\"");
        }

        // ---- the simple guard (rungs A and B) ---------------------------------------------

        Vector3 guardTarget;
        bool guardHasTarget;
        int tourIndex;
        List<Vector3> tour;
        float searchUntil;

        // The old EnemyAI, rebuilt on the same sensors so the ablation is honest: patrol
        // (random for A, a fixed tour for B), chase what you see, search where you were last
        // seen. No belief, no goals, no Director.
        void SimpleGuard(float dt)
        {
            SightSensor sight = Body.Sight;
            bool sees = sight.Awareness >= sight.seeAt;
            if (sees && !wasSeeing) Stats.Detected(ShiftTime);
            wasSeeing = sees;

            if (sees)
            {
                if (!chasing) SetChasing(true);
                Body.MoveTo(sight.LastSeenPosition, KarenBody.Pace.Run);
                searchUntil = Time.time + 6f;
                return;
            }

            if (chasing && Time.time - sight.LastSeenTime > 4f) SetChasing(false);

            if (config.rung == KarenRung.B_ScriptedPatrol && Time.time < searchUntil)
            {
                if (!Body.Arrived(1f)) return;
                Body.SweepHead(Mathf.Sin(Time.time * 2f) * 60f);
                return;
            }

            if (config.rung == KarenRung.B_ScriptedPatrol && Rng.Value < 0.002f)
            {
                // The old random sabotage.
                ShelfSlot slot = ShelfStock.Current.Nearest(Body.Position, 3f, s => s.isFilled);
                if (slot != null) slot.Eject();
            }

            if (!guardHasTarget || Body.Arrived(1.5f))
            {
                guardTarget = config.rung == KarenRung.A_RandomPatrol ? RandomFloor() : NextOnTour();
                guardHasTarget = true;
                Body.MoveTo(guardTarget, KarenBody.Pace.Walk);
            }
        }

        Vector3 RandomFloor()
        {
            int cell = Rng.Range(0, Map.CellCount);
            return Map.CellPosition[cell];
        }

        Vector3 NextOnTour()
        {
            if (tour == null)
            {
                // A serpentine through the sales floor, the same every shift.
                tour = Map.Regions
                    .Where(r => r.Area == "Sales floor" && !r.IsDoor && r.Cells.Count >= 6)
                    .OrderBy(r => Mathf.Round(r.Centroid.z / 10f))
                    .ThenBy(r => (Mathf.RoundToInt(r.Centroid.z / 10f) % 2 == 0 ? 1 : -1) * r.Centroid.x)
                    .Select(r => r.Centroid).ToList();
            }
            if (tour.Count == 0) return Body.Position;
            tourIndex = (tourIndex + 1) % tour.Count;
            return tour[tourIndex];
        }

        // ---- being caught -------------------------------------------------------------------

        // Contact kills (§8.6). Only when she's bringing you something (the Assist goal) is a
        // touch not an attack.
        void OnTouched(Collider other)
        {
            if (!ShiftActive || KilledThisShift) return;

            // Contact is the one certain observation she ever gets.
            if (Belief != null) Belief.Apply(new Observation(SenseChannel.Touch, Body.Position, 1f, 1f, "touch"));

            if (CurrentPlan != null && CurrentPlan.Action == ActionId.Lure) { decideNow = true; return; }
            Kill();
        }

        // Set when she gets you; the shift then ends without counting as clocked out.
        public bool KilledThisShift { get; private set; }

        void Kill()
        {
            string doing = CurrentPlan != null ? CurrentPlan.Name : "no plan";
            KilledThisShift = true;
            Stats.Caught();
            Stats.CatchCauses.Add(CauseOfCatch());
            Learning.Caught(Time.time);
            if (FogState.Any) Stats.CaughtInFog();
            if (CurrentPlan != null && CurrentPlan.Tactic is AmbushTactic ambush && ambush.AtChore) Stats.CaughtAtChore();
            Ledger.Data.caughtCount++;
            Dash.Cancel(Ctx);
            AbortPlan();
            Body.Stop();
            Body.SetMood(KarenBody.Mood.Hunt);
            Burnout?.SetChaseState(false);

            Log.Write(Record("CAUGHT", $"CAUGHT   she got you at {Map.Describe(Body.Position)} ({doing}, {Body.CurrentPace}, chasing {chasing})", null, null,
                                "contact — the shift starts again"));
            Debug.Log($"{GameNames.Antagonist} got the employee at {Map.Describe(Body.Position)} after {ShiftTime:0}s: {doing}, pace {Body.CurrentPace}, chasing {chasing}");
            chasing = false;
            World.PlayTell(TellKind.RevealSting, Body.Position, 0f);
            KarenNarrator.Say(StoryKind.Chase, $"{GameNames.Antagonist} got you. The shift starts again.", Body.Position);
            StartCoroutine(Consequences.Death(this, config.deathScreenSeconds));
        }

        // Why this catch happened (C1.2): what she was doing, whether she'd seen you in the last
        // 3 s, and how far it was from the time clock (the clock-camp guard, §5.6).
        CatchCause CauseOfCatch()
        {
            Landmark? clock = Map.FindLandmark(LandmarkKind.TimeClock);
            Vector3 at = Body.Position;
            float toClock = -1f;
            if (clock.HasValue)
            {
                Vector3 d = clock.Value.Position - at;
                d.y = 0f;
                toClock = d.magnitude;
            }
            return new CatchCause
            {
                T = ShiftTime,
                Action = CurrentPlan != null ? CurrentPlan.Action.ToString() : "none",
                Tactic = CurrentPlan?.Tactic != null ? CurrentPlan.Tactic.Id : "none",
                Chasing = chasing,
                SeenWithin3s = Body.Sight.SeesNow || Body.Sight.Time_SinceSeen() <= 3f,
                ClockDistance = toClock,
                Where = Map.Describe(at),
                Dash = Dash.Lurking
            };
        }

        public void SetChasing(bool on)
        {
            if (chasing == on) return;
            chasing = on;
            if (on && config.Features.Goals) Director.TrySpend(config.huntTensionCost);
            if (!on)
            {
                Director.NoteHuntEnded();
                ThrewThisHunt = false;
                replannedForSighting = -999f;
            }
            Burnout?.SetChaseState(on);
            if (ShiftActive) KarenNarrator.Say(StoryKind.Chase, on ? GameNames.Antagonist + " is chasing you — run!" : GameNames.Antagonist + " gave up the chase.", Body.Position);
            Body.SetMood(on ? KarenBody.Mood.Hunt : KarenBody.Mood.Calm);
            if (on) Stats.Chased();
            else if (config.Features.Goals && ShiftActive) Director.StartRecovery(config.recoverySeconds, Ctx, "after a chase");
        }

        public void SuppressPursuit(float seconds) => Director.SuppressPursuit(seconds);

        // ---- where you're going, once a hunt has lost you (0.2.0) ------------------------------

        public RoutePredictor Predictor { get; private set; }
        public bool ThrewThisHunt { get; set; }      // Throw: once a hunt
        float predictorTimer, replannedForSighting = -999f;

        // Four times a second: guesses built when a hunt loses you, reweighed against her belief
        // after, and kept for half a minute so Ambush can still wait where they go once the hunt
        // has handed over. Seeing you again drops them.
        void UpdatePredictor(float dt)
        {
            if (!chasing && !(Predictor.Active && Time.time - Predictor.SeenAt < 30f))
            {
                if (Predictor.Active) Predictor.Clear();
                return;
            }
            predictorTimer -= dt;
            if (predictorTimer > 0f) return;
            predictorTimer = 0.25f;

            SightSensor s = Body.Sight;
            if (s.SeesNow)
            {
                if (Predictor.Active) Predictor.Clear();
                return;
            }
            if (chasing && (!Predictor.Active || Predictor.SeenAt != s.LastSeenTime))
                Predictor.Build(s.LastSeenPosition, s.LastSeenVelocity, s.LastSeenTime, PredictorGoals());
            Predictor.Reweigh(Belief, Time.time);
        }

        // Where you might be going: your jobs and the time clock, the breaker box with the
        // lights out, and her ways out of the building.
        IEnumerable<(string name, Vector3 at)> PredictorGoals()
        {
            foreach (KeyValuePair<string, Vector3> t in FairnessGuardTargets()) yield return ("to " + t.Key, t.Value);
            if (!PowerSystem.PowerOn)
            {
                Landmark? box = Map.FindLandmark(LandmarkKind.BreakerBox);
                if (box.HasValue) yield return ("to the breaker box", box.Value.Position);
            }
            for (int k = 0; k < Belief.ExitCount; k++) yield return ("out by " + Map.NameAt(Belief.ExitPosition(k)), Belief.ExitPosition(k));
        }

        // Once per time she loses you, a hunt may stop its run to choose again with the guesses
        // in hand (Cut, or the run). True the first time it's asked for this loss.
        public bool MayReplanHunt(float lastSeen)
        {
            if (replannedForSighting == lastSeen) return false;
            replannedForSighting = lastSeen;
            return true;
        }

        // ---- the overtime (0.3.0) --------------------------------------------------------------

        // She stopped the time clock (OvertimeTactic): counted for the shift report and the eval.
        public void ClockStopped(Vector3 at)
        {
            Note("EFFECT", "EFFECT   the time clock stopped; the shift's countdown with it");
            Stats.ClockStopped();
        }

        // ---- the endgame (§10.3) ---------------------------------------------------------------

        public bool EndgameCoffeeDue =>
            Ledger.Data.burnouts >= 3 && !Ledger.Data.endingReached && Burnout != null && Burnout.Energy01 < 0.05f;

        public void ServeCoffee(Vector3 at, bool last)
        {
            CoffeeCup cup = World.Coffee(at, last);
            if (last) cup.Drunk += () => StartCoroutine(Consequences.KehaiEnding(this));
        }

        // ---- closures, visits, sabotage -------------------------------------------------------

        // Timed closures, plus every door a box still blocks: those last until someone moves the box.
        public ICollection<int> ClosedRegions
        {
            get
            {
                closedAll.Clear();
                closedAll.UnionWith(closedNow);
                DoorBlock.AddClosedRegions(closedAll);
                return closedAll;
            }
        }

        public void Close(int region, float seconds)
        {
            closedUntil[region] = Time.time + seconds;
            closedNow.Add(region);
        }

        void ExpireClosures()
        {
            if (closedUntil.Count == 0) return;
            foreach (int r in closedUntil.Where(p => Time.time > p.Value).Select(p => p.Key).ToList())
            {
                closedUntil.Remove(r);
                closedNow.Remove(r);
            }
        }

        public bool StillWinnable(ICollection<int> closed, out string why) => FairnessGuard.StillWinnable(Map, closed, out why);
        public bool StillWinnableByNavMesh(out string why) => FairnessGuard.StillWinnableByNavMesh(out why);

        public void MazeChanged()
        {
            TacticHelpers.ForgetStands();
            Map = StoreMap.Rebuild();
            RebuildBelief();
            Ledger.FillHabitPrior(Map, Belief.HabitPrior);
            RefreshDesire();
            Belief.ResetToPrior();
            tour = null;
        }

        public float LastVisited(int region) => lastVisited.TryGetValue(region, out float t) ? t : -999f;

        public bool HeardPlayerWithin(float radius, float seconds)
        {
            float sqr = radius * radius;
            foreach (var h in heard)
                if (Time.time - h.t <= seconds && (h.p - Body.Position).sqrMagnitude <= sqr) return true;
            return false;
        }

        // Told when she has emptied a shelf on purpose (the clip markers listen for it).
        public event System.Action<ShelfUnit> ShelfSabotaged;

        public void MarkSabotaged(ShelfUnit unit)
        {
            sabotaged.Add(unit);
            ShelfSabotaged?.Invoke(unit);
        }

        void OnRestocked(ShelfUnit unit, int filled)
        {
            Ledger.CompleteRoute("shelf");
            if (unit == null || !sabotaged.Remove(unit)) return;
            // You undid her work — she knows you were here, and when (§3.3).
            TraceRegistry.Add(TraceKind.UndoneSabotage, unit.transform.position, float.PositiveInfinity, default, unit);
        }

        void OnServed(CustomerNPC customer)
        {
            Ledger.CompleteRoute("till");
            // The till is hers: serving puts you in front of the most reliable witness.
            if (customer != null)
                InfrastructureFeed.Report(new Observation(SenseChannel.Infrastructure, customer.transform.position,
                    0.85f, 3f, "till (POS)"));
        }

        void OnDoorSensor(Component door, bool opened)
        {
            if (door == null || !opened) return;
            InfrastructureFeed.Report(new Observation(SenseChannel.Infrastructure, door.transform.position, 0.7f, 2.5f,
                "door sensor (" + Map.Describe(door.transform.position) + ")"));
        }

        // ---- blink channel ------------------------------------------------------------------

        public bool BlinkLive { get; set; }
        public bool BlinkPhysiological { get; set; }
        public bool EyesClosed { get; set; }
        public float PredictedReopenIn { get; set; }

        // Every blink: the Panic Index counts it, and the dash may use it (BlinkDash).
        public void OnBlinkStarted()
        {
            Director?.Index.NoteBlink();
            if (!ShiftActive || KilledThisShift) return;
            Stats.BlinkSeen();
            Dash.OnBlink(Ctx);
        }

        // ---- telling the player what she's doing ---------------------------------------------

        // What she's doing right now, in a sentence, for the F1 map.
        public string StatusLine
        {
            get
            {
                if (!ShiftActive) return GameNames.Antagonist + " is off duty until you clock in.";
                if (KilledThisShift) return GameNames.Antagonist + " got you.";
                if (Dash.Lurking) return GameNames.Antagonist + " is nowhere to be seen.";
                if (chasing) return GameNames.Antagonist + " is chasing you!";
                if (CurrentPlan == null) return GameNames.Antagonist + " is thinking.";
                string doing = CurrentPlan.Tactic != null ? KarenNarrator.Tactic(CurrentPlan.Tactic.Id) : KarenNarrator.ActionName(CurrentPlan.Action);
                return GameNames.Antagonist + " is " + doing.TrimEnd('!') + ".";
            }
        }

        public bool IsChasing => chasing;

        void NarratePlan(PlanTree plan)
        {
            if (!ShiftActive || plan.Tactic == null) return;
            string id = plan.Tactic.Id;
            // Her rounds and small follow-ups would flood the story; say them only when they change.
            if (id == lastNarratedTactic && Time.time - lastPlanNarration < 20f) return;
            lastNarratedTactic = id;
            lastPlanNarration = Time.time;
            string where = PlainTarget(plan.Target);
            KarenNarrator.Say(StoryKind.Plan, $"{GameNames.Antagonist} is {KarenNarrator.Tactic(id).TrimEnd('!')}{(where != null ? " — " + where : "")}.");
        }

        string lastNarratedTactic;
        float lastPlanNarration = -99f;

        static string PlainTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || target == "close distance") return null;
            if (target.Contains("/") || target.StartsWith("Door")) return KarenNarrator.Place(target.Split(' ')[0].Contains("/") ? target.Split(' ')[0] : target);
            return target.Length <= 40 ? target : null;
        }

        int lastGuessRegion = -1;
        float lastGuessAt = -99f;

        // Every so often, and whenever her best guess moves somewhere new, say where she
        // thinks you are.
        void NarrateGuess()
        {
            if (!ShiftActive || Belief == null) return;
            int region = Belief.PeakRegion;
            float conf = Belief.Confidence;
            bool moved = region != lastGuessRegion && conf >= 0.3f;
            if (!moved && Time.time - lastGuessAt < 20f) return;
            if (region == lastGuessRegion && Time.time - lastGuessAt < 45f) return;   // nothing new to say
            lastGuessRegion = region;
            lastGuessAt = Time.time;
            if (conf < 0.12f)
                KarenNarrator.Say(StoryKind.Guess, GameNames.Antagonist + " has no idea where you are.");
            else
                KarenNarrator.Say(StoryKind.Guess, $"{GameNames.Antagonist} thinks you're {KarenNarrator.In(Map.RegionName(region))} ({KarenNarrator.Sureness(conf)}).", Belief.PeakPosition);
        }

        // ---- the thought log --------------------------------------------------------------

        public void Note(string kind, string text)
        {
            string line = ThoughtLog.Stamp(ShiftTime) + " " + (string.IsNullOrEmpty(kind) || text.StartsWith(kind) || text.StartsWith(" ") ? text : $"{kind.PadRight(8)} {text}");
            Log.Write(new ThoughtRecord
            {
                T = ShiftTime,
                Shift = Stats.Shift,
                Kind = string.IsNullOrEmpty(kind) ? "WHY" : kind,
                Text = line,
                Panic = Director != null ? Director.Panic : 0f,
                Target = Director != null ? Director.Setpoint : 0f,
                Tension = Director != null ? Director.Tension : 0f,
                Pressure = Director != null ? Director.Pressure : 0f,
                BodyPosition = Body != null ? Body.Position : Vector3.zero
            });
        }

        ThoughtRecord Record(string kind, string text, List<ThoughtOption> options, string chose, string because)
        {
            return new ThoughtRecord
            {
                T = ShiftTime,
                Shift = Stats.Shift,
                Kind = kind,
                Text = ThoughtLog.Stamp(ShiftTime) + " " + text,
                Peak = Map.RegionName(Belief.PeakRegion),
                Confidence = Belief.Confidence,
                Entropy = Belief.Entropy,
                Stale = Mathf.Min(999f, Belief.Staleness),
                RuledOut = Belief.RuledOut(),
                Options = options != null ? new List<ThoughtOption>(options) : null,
                Chose = chose,
                Because = because,
                Panic = Director.Panic,
                Target = Director.Setpoint,
                Tension = Director.Tension,
                Pressure = Director.Pressure,
                BeliefSnapshot = Belief.SnapshotRegions(),
                BodyPosition = Body.Position,
                PlayerPosition = KarenDirector.TruePlayerPosition
            };
        }
    }

    // One catch, and what led to it (0.4.0, build plan C1.2).
    public struct CatchCause
    {
        public float T;                 // seconds into the shift
        public string Action, Tactic;   // what she was doing
        public bool Chasing;
        public bool SeenWithin3s;       // her own eyes had you in the last 3 s
        public float ClockDistance;     // metres from the time clock; -1 with no clock
        public string Where;
        public bool Dash;               // she was lurking after a vanish

        // Within 10 m of the clock: the clock-camp guard's count (§5.6).
        public bool AtClock => ClockDistance >= 0f && ClockDistance <= 10f;

        public System.Collections.Generic.Dictionary<string, object> ToDictionary() => new System.Collections.Generic.Dictionary<string, object>
        {
            ["t"] = T, ["action"] = Action, ["tactic"] = Tactic, ["chasing"] = Chasing,
            ["seen_3s"] = SeenWithin3s, ["clock_m"] = ClockDistance, ["where"] = Where, ["dash"] = Dash
        };
    }

    // What the eval harness and the ablation runner read at the end of a shift.
    public sealed class KarenStats
    {
        public int Shift { get; private set; }
        public float FirstDetection { get; private set; } = -1f;
        public int Detections { get; private set; }
        public int Catches { get; private set; }
        public int Chases { get; private set; }
        public int Overtimes { get; private set; }
        public int BlinksSeen { get; private set; }
        public float ShiftSeconds { get; private set; }
        public readonly Dictionary<string, int> TacticCounts = new Dictionary<string, int>();
        public readonly Dictionary<string, List<float>> TacticRewards = new Dictionary<string, List<float>>();
        public readonly List<ChainStat> ChainsFound = new List<ChainStat>();      // the combo room, this shift
        public readonly List<float> PanicTrace = new List<float>();
        public readonly List<float> SetpointTrace = new List<float>();

        // 0.3.0's measurements (build plan §7): catches in her fog, catches at a chore she left
        // (Ambush waiting on your way to it), your answers and how long they took, and what her
        // tricks' places paid.
        public int CatchesInFog { get; private set; }
        public int CatchesAtChore { get; private set; }
        public readonly List<(string kind, float seconds)> Answers = new List<(string, float)>();
        public readonly List<CatchCause> CatchCauses = new List<CatchCause>();   // 0.4.0 (C1.2)
        public void CaughtInFog() => CatchesInFog++;

        // How often each trick landed (its plan's first effect), against TacticCounts' starts.
        public readonly Dictionary<string, int> TacticLanded = new Dictionary<string, int>();
        public void Landed(Tactic t) => TacticLanded[t.Id] = (TacticLanded.TryGetValue(t.Id, out int n) ? n : 0) + 1;
        public void CaughtAtChore() => CatchesAtChore++;

        float pacingSquaredError;
        int pacingSamples;

        // The single healthiest metric (Karen.md §13): how well panic tracks its target.
        public float SetpointRmse => pacingSamples > 0 ? Mathf.Sqrt(pacingSquaredError / pacingSamples) : 0f;

        public void SamplePacing(float panic, float setpoint)
        {
            pacingSquaredError += (panic - setpoint) * (panic - setpoint);
            pacingSamples++;
        }

        public void BeginShift(int shift)
        {
            Shift = shift;
            pacingSquaredError = 0f;
            pacingSamples = 0;
            FirstDetection = -1f;
            Detections = Catches = Chases = Overtimes = BlinksSeen = 0;
            CatchesInFog = CatchesAtChore = 0;
            Answers.Clear();
            CatchCauses.Clear();
            CctvCamera.SeenByHers = 0f;
            TacticCounts.Clear();
            TacticLanded.Clear();
            TacticRewards.Clear();
            ChainsFound.Clear();
            PanicTrace.Clear();
            SetpointTrace.Clear();
        }

        public void Detected(float at)
        {
            Detections++;
            if (FirstDetection < 0f) FirstDetection = at;
        }

        public void Caught() => Catches++;
        public void Chased() => Chases++;
        public void ClockStopped() => Overtimes++;   // the overtime: times she stopped the clock
        public void BlinkSeen() => BlinksSeen++;

        public void Used(Tactic t)
        {
            TacticCounts.TryGetValue(t.Id, out int n);
            TacticCounts[t.Id] = n + 1;
        }

        public void ChainFound(ChainStat chain) =>
            ChainsFound.Add(new ChainStat { first = chain.first, then = chain.then, q = chain.q, n = chain.n, found = chain.found, alone = chain.alone });

        public void Rewarded(Tactic t, float delta)
        {
            if (!TacticRewards.TryGetValue(t.Id, out List<float> list)) TacticRewards[t.Id] = list = new List<float>();
            list.Add(delta);
        }

        public void EndShift(KarenBrain brain)
        {
            ShiftSeconds = brain.ShiftTime;
            foreach (var h in brain.Director.History)
            {
                PanicTrace.Add(h.panic);
            }
        }

        // Shannon entropy of the tactic distribution, in bits — high means varied, low means
        // she found one trick and camped on it.
        public float TacticEntropy
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, int> p in TacticCounts)
                {
                    Tactic t = TacticLibrary.Get(p.Key);
                    if (t != null && t.Tier > 0) total += p.Value;
                }
                if (total == 0) return 0f;
                float h = 0f;
                foreach (KeyValuePair<string, int> p in TacticCounts)
                {
                    Tactic t = TacticLibrary.Get(p.Key);
                    if (t == null || t.Tier == 0) continue;
                    float q = (float)p.Value / total;
                    h -= q * Mathf.Log(q, 2f);
                }
                return h;
            }
        }
    }
}
