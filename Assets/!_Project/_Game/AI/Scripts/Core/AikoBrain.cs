using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // Aiko — the orchestrator (Aiko.md §2, §6.1).
    //
    // Owns the three minds and runs them at their own rates:
    //
    //   Sense     every frame      the eye integrates; the plan's current primitive runs
    //   Believe   10 Hz            sensorium → belief grid, then predict
    //   Appraise   5 Hz            grid + director → the handful of scalars decisions use
    //   Director   5 Hz            panic, setpoint, tension, permissions
    //   Decide     2 Hz or event   goal by utility → tactic → plan (HTN, anytime)
    //
    // The rung (IDEAS.md §2) decides how much of this is switched on. A and B are the old
    // patrol-and-chase guard, kept as the ablation floor; C and up are Aiko.
    [DefaultExecutionOrder(100)]
    public sealed class AikoBrain : MonoBehaviour
    {
        public static AikoBrain Instance { get; private set; }

        public AikoConfig config = new AikoConfig();

        [Tooltip("Every tactic regardless of the career table — for testing.")]
        public bool careerOverride;

        // ---- the parts ----------------------------------------------------------------
        public StoreMap Map { get; private set; }
        public BeliefGrid Belief { get; private set; }
        public AikoBody Body { get; private set; }
        public AikoDirector Director { get; private set; }
        public AikoLedger Ledger { get; private set; }
        public AikoWorld World { get; private set; }
        public ThoughtLog Log { get; } = new ThoughtLog();
        public AikoRng Rng { get; private set; }
        public Sensorium Sensorium { get; private set; }
        public EnergyBelief Energy { get; } = new EnergyBelief();
        public GoalSet Goals { get; } = new GoalSet();
        public AikoContext Ctx { get; private set; }
        public PerformanceReview Review { get; private set; }
        public AikoStats Stats { get; } = new AikoStats();

        public TaskManager Tasks { get; private set; }
        public ShiftManager Shift { get; private set; }
        public BurnoutSystem Burnout { get; private set; }

        public PlanTree CurrentPlan { get; private set; }
        public bool ShiftActive { get; private set; }
        public float ShiftTime => ShiftActive ? Time.time - shiftStartedAt : 0f;
        public bool CareerOverride => careerOverride;
        public bool FinalShiftException { get; set; }

        float shiftStartedAt;
        float believeTimer, appraiseTimer, decideTimer, directorTimer, desireTimer;
        bool decideNow;
        float planStartedAt;
        bool chasing;

        readonly Dictionary<int, float> lastVisited = new Dictionary<int, float>();
        readonly Dictionary<int, float> closedUntil = new Dictionary<int, float>();
        readonly HashSet<int> closedNow = new HashSet<int>();
        readonly List<(float t, Vector3 p)> heard = new List<(float, Vector3)>();
        readonly List<(Tactic tactic, float t0, float p0)> pendingRewards = new List<(Tactic, float, float)>();
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
            Map = StoreMap.Current;
            Rng = new AikoRng(config.seed);
            Ledger = new AikoLedger(config) { Persistent = config.Features.Persistent };
            Ledger.Load();
            Director = new AikoDirector(config, Ledger);
            World = gameObject.GetOrAdd<AikoWorld>();
            Review = new PerformanceReview(this);

            Landmark? home = Map.FindLandmark(LandmarkKind.StockCrateHome);
            Body = AikoBody.Build(transform, config, home.HasValue ? home.Value.Position : transform.position);
            Body.Touched += OnTouched;

            RebuildBelief();
            Log.EchoToConsole = config.logToConsole;

            Ctx = new AikoContext
            {
                Brain = this, Body = Body, Map = Map, Belief = Belief, Director = Director, Ledger = Ledger,
                World = World, Log = Log, Rng = Rng, Config = config, Tasks = Tasks, Shift = Shift
            };

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

            if (Shift != null)
            {
                Shift.ShiftStateChanged += OnShiftStateChanged;
                Shift.ClockOutGuard = ShouldRefuseClockOut;
                if (Shift.IsShiftActive) BeginShift();
            }

            Note("SYSTEM", $"{GameNames.Antagonist} online — rung {config.rung} ({config.Features}), seed {Rng.Seed}. Map: {Map.Summary()}");
        }

        // Routes end at a job: the Ledger learns the paths taken between them.
        void OnSpillRoute(Dirt d) => Ledger.CompleteRoute("spill");
        void OnBinRoute(Trashcan b) => Ledger.CompleteRoute("bin");
        void OnSkipRoute(TrashBag b) => Ledger.CompleteRoute("skip");
        void OnCoffeeRoute(Vector3 p) => Ledger.CompleteRoute("coffee");
        void OnClockRoute(bool accepted) => Ledger.CompleteRoute("time clock");

        // The events are static and outlive the scene; an eval reset reloads it, so every
        // subscription is undone here or the old Aiko would go on listening.
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
            Director?.Dispose();
            if (Shift != null) Shift.ShiftStateChanged -= OnShiftStateChanged;
            Sensorium?.Dispose();
            if (Instance == this) Instance = null;
        }

        void RebuildBelief()
        {
            Sensorium?.Dispose();
            Belief = new BeliefGrid(Map);
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
        }

        // Swap rung mid-session (the ablation runner does this between episodes).
        public void SetRung(AikoRung rung)
        {
            config.rung = rung;
            Ledger.Persistent = config.Features.Persistent;
            ApplyRungToSenses();
            Note("SYSTEM", $"rung → {rung} ({config.Features})");
        }

        // ---- shifts ---------------------------------------------------------------------

        void OnShiftStateChanged()
        {
            if (Shift == null) return;
            if (Shift.IsShiftActive && !ShiftActive) BeginShift();
            else if (!Shift.IsShiftActive && ShiftActive) EndShift(true);
        }

        public void BeginShift()
        {
            int number = Shift != null ? Mathf.Max(1, Shift.ShiftNumber) : 1;
            if (config.seed != 0) Rng.Reseed(config.seed * 7919 + number);

            Log.Clear();
            Stats.BeginShift(number);
            TacticLibrary.ResetShift();
            TraceRegistry.Clear();
            HudFeed.Clear();
            closedUntil.Clear();
            closedNow.Clear();
            pendingRewards.Clear();
            sabotaged.Clear();
            peakOutstanding.Clear();
            OvertimeArmed = false;

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
            Goals.Reset();
            AbortPlan();
            Body.Sight.ResetAwareness();
            Landmark? home = Map.FindLandmark(LandmarkKind.StockCrateHome);
            if (home.HasValue) Body.Warp(home.Value.Position);
            Body.SetMood(AikoBody.Mood.Calm);

            ShiftActive = true;
            shiftStartedAt = Time.time;
            decideNow = true;

            Note("SYSTEM", $"shift {number} begins — employee {Ledger.PlayerName}, shift {number} of the career");
        }

        public void EndShift(bool clockedOut)
        {
            if (!ShiftActive) return;
            ShiftActive = false;
            AbortPlan();
            Director.EndShift();
            float energy = Burnout != null ? Burnout.Energy01 : 1f;
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

            Body.Sight.Tick(dt);
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
            if (believeTimer >= 1f / config.believeHz) { Believe(believeTimer); believeTimer = 0f; }

            appraiseTimer += dt;
            if (appraiseTimer >= 1f / config.appraiseHz) { Appraise(); appraiseTimer = 0f; }

            TickDirector(dt);

            decideTimer += dt;
            if (decideNow || decideTimer >= 1f / config.decideHz)
            {
                bool forced = decideNow;
                decideNow = false;
                decideTimer = 0f;
                Decide(forced);
            }

            RunPlan(dt);
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
            Director.ObserveForLedger(Ctx, step);
            if (ShiftActive) Stats.SamplePacing(Director.Panic, Director.Setpoint);
            SettleRewards();
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
                if (ShiftActive) AikoNarrator.Say(StoryKind.Seen, $"{GameNames.Antagonist} spotted you in {AikoNarrator.Place(Body.Sight.LastSeenPosition)}!", Body.Sight.LastSeenPosition);
                lostSightAt = -1f;
            }
            else if (!seeing && wasSeeing) lostSightAt = Time.time;
            if (lostSightAt > 0f && Time.time - lostSightAt > 3f)
            {
                lostSightAt = -1f;
                if (ShiftActive) AikoNarrator.Say(StoryKind.Seen, GameNames.Antagonist + " lost sight of you.", Body.Sight.LastSeenPosition);
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
            if (Sensorium.LoudNoiseNearby && CurrentPlan != null && CurrentPlan.Goal != GoalId.Pursue) decideNow = true;
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
                    AikoNarrator.Say(StoryKind.Heard, $"{GameNames.Antagonist} heard {AikoNarrator.Evidence(o.Label)} near {AikoNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Testimony:
                    AikoNarrator.Say(StoryKind.Heard, $"A customer told {GameNames.Antagonist} they saw you near {AikoNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Trace:
                    AikoNarrator.Say(StoryKind.Heard, $"{GameNames.Antagonist} found a trace of you — {AikoNarrator.Evidence(o.Label)} — near {AikoNarrator.Place(o.Position)}.", o.Position); break;
                case SenseChannel.Infrastructure:
                {
                    string what = AikoNarrator.Evidence(o.Label), where = AikoNarrator.Place(o.Position);
                    AikoNarrator.Say(StoryKind.Heard, what.Contains("door") && where.Contains("door")
                        ? $"{GameNames.Antagonist}'s door sensors saw someone use {where}."
                        : $"{GameNames.Antagonist}'s sensors picked up {what} {AikoNarrator.In(o.Position)}.", o.Position);
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
        // names objects in the store, not the employee, so Aiko may use it: she wrote it.
        static IEnumerable<KeyValuePair<string, Vector3>> FairnessGuardTargets() => FairnessGuard.RequiredTargets(StoreMap.Current);

        public int LikelyNextJobRegion
        {
            get
            {
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

        void Decide(bool forced)
        {
            Ctx.A = Appraised();
            GoalId goal = Goals.Choose(Ctx, forced, out string because);

            bool keep = CurrentPlan != null && CurrentPlan.Goal == goal && !forced;
            if (keep) return;

            WriteBelief();
            Log.Write(Record("GOAL", $"GOAL     {HtnPlanner.Describe(Goals.LastOptions)}", Goals.LastOptions, goal.ToString(), because));
            Note("", $"  why    {because}");

            AbortPlan();
            PlanTree plan = HtnPlanner.Plan(goal, Ctx, Interrupt, out Tactic tactic, out List<ThoughtOption> options, out string why);
            if (plan == null)
            {
                Note("PLAN", $"PLAN     {goal}: nothing workable ({why}) → patrol");
                plan = HtnPlanner.Plan(GoalId.Patrol, Ctx, Interrupt, out tactic, out options, out why);
                if (plan == null) return;
            }

            if (tactic.TensionCost > 0f && !Director.TrySpend(tactic.TensionCost))
            {
                Note("PLAN", $"PLAN     {tactic.Id} unaffordable at the last moment → patrol");
                return;
            }

            tactic.LastUsed = Time.time;
            tactic.UsesThisShift++;
            Ledger.MarkUsed(tactic, Time.time);
            if (tactic.Learnable) pendingRewards.Add((tactic, Time.time, Director.Panic));
            Stats.Used(tactic);

            CurrentPlan = plan;
            planStartedAt = Time.time;
            Log.Write(Record("PLAN", $"PLAN     {tactic.Id} → {why}", options, tactic.Id, why));
            NarratePlan(plan);
        }

        void WriteBelief()
        {
            Log.Write(Record("BELIEF",
                $"BELIEF   peak={Map.RegionName(Belief.PeakRegion)} p={Belief.Confidence:0.00} H={Belief.Entropy:0.00} stale={Mathf.Min(999f, Belief.Staleness):0.0}s",
                null, null, null));
        }

        // The global interrupt branch of every plan (§6.5).
        bool Interrupt(AikoContext c)
        {
            if (CurrentPlan == null) return false;
            GoalId g = CurrentPlan.Goal;
            bool exempt = g == GoalId.Pursue || g == GoalId.Stalk || g == GoalId.Ambush || g == GoalId.Assist || g == GoalId.Withdraw;
            if (!exempt && c.A.Awareness >= Body.Sight.seeAt && Director.GoalPermission(GoalId.Pursue, c) > 0f)
            {
                Note("", "  interrupt: SeeingPlayerAtRange");
                return true;
            }
            if (g != GoalId.Pursue && Sensorium.LoudNoiseNearby)
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

            if (s == Status.Failure && !CurrentPlan.Interrupted && CurrentPlan.Plan.ViolatedGuard != null)
                Note("PLAN", $"PLAN     {CurrentPlan.Name} aborted — precondition violated: {CurrentPlan.Plan.ViolatedGuard}");
            else if (s == Status.Failure && !CurrentPlan.Interrupted)
                Note("PLAN", $"PLAN     {CurrentPlan.Name} failed at step {CurrentPlan.Plan.Index + 1}/{CurrentPlan.Plan.Count}");

            CurrentPlan = null;
            decideNow = true;
        }

        void AbortPlan()
        {
            if (CurrentPlan == null) return;
            CurrentPlan.Abort(Ctx);
            CurrentPlan = null;
            if (chasing) SetChasing(false);
            Body.Silent = false;
            Body.SweepHead(0f);
        }

        // ---- learning -----------------------------------------------------------------------

        // Each tactic's reward is the change in panic over the window after it ran (§7.2).
        void SettleRewards()
        {
            for (int i = pendingRewards.Count - 1; i >= 0; i--)
            {
                var (tactic, t0, p0) = pendingRewards[i];
                if (Time.time - t0 < config.rewardWindow) continue;
                pendingRewards.RemoveAt(i);
                float delta = Director.MeanPanic(t0, t0 + config.rewardWindow) - p0;
                if (config.Features.Bandit)
                {
                    Ledger.Reward(tactic.Id, delta);
                    Note("LEARN", $"LEARN    {tactic.Id} Δpanic {delta:+0.00;-0.00} → Q̂ {Ledger.ExpectedPanicDelta(tactic, Ctx):+0.00;-0.00}");
                    if (Mathf.Abs(delta) >= 0.08f)
                        AikoNarrator.Say(StoryKind.Learned, delta > 0f
                            ? $"{GameNames.Antagonist} noticed that {AikoNarrator.Tactic(tactic.Id).TrimEnd('!')} rattled you. She'll remember."
                            : $"{GameNames.Antagonist} noticed that {AikoNarrator.Tactic(tactic.Id).TrimEnd('!')} didn't bother you.");
                }
                Stats.Rewarded(tactic, delta);
            }
        }

        // ---- tells and effects (fairness rule 3) --------------------------------------------

        float planTellAt = -999f;

        // A PA line is its own tell (the chime); what the plan does after it has been told.
        public void NotePlanTell() => planTellAt = Time.time;

        // `ofPlan`: a Tell primitive in the running plan, as opposed to a PA chime or the
        // time clock's buzz, which carry their own effects.
        // Told each time she gives a warning: what, where, and how long before the trick.
        public event System.Action<TellKind, Vector3, float> Told;

        public void RecordTell(TellKind kind, Vector3 at, float lead, bool ofPlan = false)
        {
            FairnessGuard.NoteTell(kind, lead);
            Told?.Invoke(kind, at, lead);
            if (ofPlan) planTellAt = Time.time;
            if (ShiftActive && kind != TellKind.PaChime && kind != TellKind.Footsteps)
                AikoNarrator.Say(StoryKind.Warning, $"Warning: {AikoNarrator.Tell(kind)} near {AikoNarrator.Place(at)} — something's about to happen.", at);
            Note("TELL", $"TELL     {kind} at {Map.Describe(at)} ({lead:0.0}s lead)");
        }

        public void RecordEffect(string label)
        {
            if (CurrentPlan?.Tactic != null && CurrentPlan.Tactic.Tier > 0)
                FairnessGuard.CheckEffect(CurrentPlan.Tactic.Id, planStartedAt, planTellAt, config.minTellLead);
            Note("EFFECT", "EFFECT   " + label);
        }

        // An effect that carries its own tell — PA speech, which always follows its chime.
        // Checked against that tell rather than against the plan that queued it, which may
        // have finished by the time the words play.
        void RecordToldEffect(string label, string by, float toldAt)
        {
            FairnessGuard.CheckToldEffect(label, Time.time - toldAt, config.minTellLead);
            Note("EFFECT", $"EFFECT   {label} ({by} {Time.time - toldAt:0.0}s before)");
        }

        void OnPaChime(PaAnnouncement a) => RecordTell(TellKind.PaChime, Body.Position, PaSystem.ChimeSeconds);   // storewide; logged where she is
        void OnPaSpeech(PaAnnouncement a)
        {
            RecordToldEffect("PA: " + a.Text, "chime", a.Started);
            AikoNarrator.Say(StoryKind.Store, $"{GameNames.Antagonist} over the speakers: \"{a.Text}\"");
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

            // Even the simplest guard lets you go after a lecture (§9: the recovery window
            // is a rule of the game, not a feature of the smarter rungs).
            if (Time.time < releasedUntil) sees = false;

            if (sees)
            {
                if (!chasing) SetChasing(true);
                Body.MoveTo(sight.LastSeenPosition, AikoBody.Pace.Run);
                searchUntil = Time.time + 6f;
                return;
            }

            if (chasing && Time.time - sight.LastSeenTime > 4f) SetChasing(false);

            if (config.rung == AikoRung.B_ScriptedPatrol && Time.time < searchUntil)
            {
                if (!Body.Arrived(1f)) return;
                Body.SweepHead(Mathf.Sin(Time.time * 2f) * 60f);
                return;
            }

            if (config.rung == AikoRung.B_ScriptedPatrol && Rng.Value < 0.002f)
            {
                // The old random sabotage.
                ShelfSlot slot = ShelfSlot.All.Where(s => s.isFilled)
                    .OrderBy(s => (s.transform.position - Body.Position).sqrMagnitude).FirstOrDefault();
                if (slot != null && Vector3.Distance(slot.transform.position, Body.Position) < 3f) slot.Eject();
            }

            if (!guardHasTarget || Body.Arrived(1.5f))
            {
                guardTarget = config.rung == AikoRung.A_RandomPatrol ? RandomFloor() : NextOnTour();
                guardHasTarget = true;
                Body.MoveTo(guardTarget, AikoBody.Pace.Walk);
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

        void OnTouched(Collider other)
        {
            if (!ShiftActive || Consequences.LectureRunning || Time.time < releasedUntil) return;

            // Contact is the one certain observation she ever gets.
            if (Belief != null) Belief.Apply(new Observation(SenseChannel.Touch, Body.Position, 1f, 1f, "touch"));

            if (chasing) Caught();
            else decideNow = true;
        }

        float releasedUntil = -1f;

        void Caught()
        {
            Stats.Caught();
            Ledger.Data.caughtCount++;
            Ledger.Data.warnings++;
            AbortPlan();
            Body.Stop();
            Body.SetMood(AikoBody.Mood.Calm);
            SetChasing(false);

            Log.Write(Record("CAUGHT", $"CAUGHT   written warning #{Ledger.Data.warnings} at {Map.Describe(Body.Position)}", null, null,
                                "caught during a chase — lecture, overtime, then a guaranteed recovery window"));
            StartCoroutine(Consequences.Lecture(this, Ledger.Data.warnings, config.lectureSeconds, config.overtimePerCatch));
            AikoNarrator.Say(StoryKind.Chase, $"{GameNames.Antagonist} caught you! Written warning #{Ledger.Data.warnings}: a {config.lectureSeconds:0}-second lecture and {config.overtimePerCatch:0} seconds of overtime. After it, she has to leave you alone for a while.", Body.Position);
            releasedUntil = Time.time + config.lectureSeconds + 15f;   // no second catch until you've had a chance to walk away
            if (config.Features.Goals) Director.StartRecovery(Mathf.Max(config.recoverySeconds, config.lectureSeconds + 15f), Ctx, "caught");
            CustomerMemory.MakeNearbyJumpy(Body.Position, 20f, 90f);
        }

        public void SetChasing(bool on)
        {
            if (chasing == on) return;
            chasing = on;
            Burnout?.SetChaseState(on);
            if (ShiftActive) AikoNarrator.Say(StoryKind.Chase, on ? GameNames.Antagonist + " is chasing you — run!" : GameNames.Antagonist + " gave up the chase.", Body.Position);
            Body.SetMood(on ? AikoBody.Mood.Hunt : AikoBody.Mood.Calm);
            if (on) Stats.Chased();
            else if (config.Features.Goals && ShiftActive) Director.StartRecovery(config.recoverySeconds, Ctx, "after a chase");
        }

        public void SuppressPursuit(float seconds) => Director.SuppressPursuit(seconds);

        // ---- the overtime (§8.2) -------------------------------------------------------------

        public bool OvertimeArmed { get; private set; }

        public void ArmOvertime() => OvertimeArmed = true;

        // Asked by the time clock when the employee tries to leave with everything done.
        bool ShouldRefuseClockOut()
        {
            if (!OvertimeArmed || !ShiftActive || !config.Features.Goals) return false;
            OvertimeArmed = false;
            StartCoroutine(Overtime());
            return true;
        }

        System.Collections.IEnumerator Overtime()
        {
            Landmark? clock = Map.FindLandmark(LandmarkKind.TimeClock);
            Vector3 at = clock.HasValue ? clock.Value.Position : Body.Position;
            World.PlayTell(TellKind.PunchBuzz, at, 0.9f);
            RecordTell(TellKind.PunchBuzz, at, 0.9f);
            yield return new WaitForSeconds(0.9f);
            Consequences.AddOvertime(120f);
            World.Pa.Announce($"Thank you for your flexibility, {Ledger.PlayerName}. Your shift has been extended.");
            Note("EFFECT", "EFFECT   clock-out refused; +2:00 of shift");
            Stats.OvertimeApplied();
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

        public ICollection<int> ClosedRegions => closedNow;

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

        public void OnBlinkStarted()
        {
            Director?.Index.NoteBlink();
            if (!config.Features.Blink || !ShiftActive) return;
            Stats.BlinkSeen();
            if (CurrentPlan != null && CurrentPlan.Tactic != null && CurrentPlan.Tactic.Id == "blink_advance")
                AikoNarrator.Say(StoryKind.Blink, "You blinked — and " + GameNames.Antagonist + " moved.", Body.Position);
        }

        // ---- telling the player what she's doing ---------------------------------------------

        // What she's doing right now, in a sentence, for the F1 map.
        public string StatusLine
        {
            get
            {
                if (!ShiftActive) return GameNames.Antagonist + " is off duty until you clock in.";
                if (Consequences.LectureRunning) return GameNames.Antagonist + " is lecturing you.";
                if (chasing) return GameNames.Antagonist + " is chasing you!";
                if (CurrentPlan == null) return GameNames.Antagonist + " is thinking.";
                string doing = CurrentPlan.Tactic != null ? AikoNarrator.Tactic(CurrentPlan.Tactic.Id) : AikoNarrator.Goal(CurrentPlan.Goal);
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
            AikoNarrator.Say(StoryKind.Plan, $"{GameNames.Antagonist} is {AikoNarrator.Tactic(id).TrimEnd('!')}{(where != null ? " — " + where : "")}.");
        }

        string lastNarratedTactic;
        float lastPlanNarration = -99f;

        static string PlainTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || target == "close distance") return null;
            if (target.Contains("/") || target.StartsWith("Door")) return AikoNarrator.Place(target.Split(' ')[0].Contains("/") ? target.Split(' ')[0] : target);
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
                AikoNarrator.Say(StoryKind.Guess, GameNames.Antagonist + " has no idea where you are.");
            else
                AikoNarrator.Say(StoryKind.Guess, $"{GameNames.Antagonist} thinks you're {AikoNarrator.In(Map.RegionName(region))} ({AikoNarrator.Sureness(conf)}).", Belief.PeakPosition);
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
                PlayerPosition = AikoDirector.TruePlayerPosition
            };
        }
    }

    // What the eval harness and the ablation runner read at the end of a shift.
    public sealed class AikoStats
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
        public readonly List<float> PanicTrace = new List<float>();
        public readonly List<float> SetpointTrace = new List<float>();

        float pacingSquaredError;
        int pacingSamples;

        // The single healthiest metric (Aiko.md §13): how well panic tracks its target.
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
            TacticCounts.Clear();
            TacticRewards.Clear();
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
        public void OvertimeApplied() => Overtimes++;
        public void BlinkSeen() => BlinksSeen++;

        public void Used(Tactic t)
        {
            TacticCounts.TryGetValue(t.Id, out int n);
            TacticCounts[t.Id] = n + 1;
        }

        public void Rewarded(Tactic t, float delta)
        {
            if (!TacticRewards.TryGetValue(t.Id, out List<float> list)) TacticRewards[t.Id] = list = new List<float>();
            list.Add(delta);
        }

        public void EndShift(AikoBrain brain)
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
