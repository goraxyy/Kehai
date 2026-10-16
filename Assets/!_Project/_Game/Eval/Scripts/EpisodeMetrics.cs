using System.Collections.Generic;
using System.Linq;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Eval
{
    // Everything measured about one shift (IDEAS.md, the agent test environment), plus the action trace the
    // failure taxonomy reads.
    public sealed class EpisodeMetrics
    {
        public int Seed;
        public int Shift;
        public string Agent = "unknown";
        public string Rung = "";

        // Set by Begin, once the punch has taken. A shift that never started has nothing to say.
        public bool Started { get; private set; }

        public bool ClockedOut;
        public bool TimedOut;
        public float SimSeconds;
        public float WallSeconds;
        public int Steps;

        public int SpillsMopped, ShelvesRestocked, BinsBagged, BagsDisposed, CustomersServed, DirectionsGiven, Coffees;
        public int SpillsLeftStanding;
        public int CustomersLost;
        public int SpuriousClockOuts;
        public float EnergyAtEnd = 1f;
        public float MinEnergy = 1f;

        public readonly List<float> Waits = new List<float>();
        public float MeanWait => Waits.Count > 0 ? Waits.Average() : 0f;
        public float LongestWait { get; private set; }

        // Karen's side, copied from her stats at the end.
        public float FirstDetection = -1f;
        public int Detections, Catches, Chases, Overtimes;
        public int CloseIns, Vanishes, Reveals;   // the blink dash (BlinkDash): catches are kills now
        public float RewardMeanAbs;               // mean |Δpanic| over the tactics she was rewarded for: the signal learning gets

        // The simulated player's side (SimulatedPlayer, BotFear), set as its shift ends.
        public float BotMeanFear, BotPeakFear;
        public int BotStartles, BotHides, BotBackOffs;
        public bool BotReactive = true;
        public float TacticEntropy;
        public float MeanPanic, PanicSetpointRmse;
        public int TacticsUsed;
        public string TopTactics = "";
        public int FairnessViolations;
        int violationsAtStart;

        // How well she knew where the player was, scored against where they were.
        public readonly BeliefAccuracy Belief = new BeliefAccuracy();

        // The trace: what the agent tried, when, and at what.
        public struct Step { public float T; public string Verb; public string Kind; public bool Ok; }
        public readonly List<Step> Trace = new List<Step>();
        public readonly List<(float t, int outstanding, int waitingLong)> Pressure = new List<(float, int, int)>();

        readonly Dictionary<CustomerNPC, float> waitingSince = new Dictionary<CustomerNPC, float>();
        int lastAsking;
        int directionsThisTick;
        float started;

        public List<string> Failures = new List<string>();

        public void Begin(int seed, int shift, string agent, string rung)
        {
            Seed = seed;
            Shift = shift;
            Agent = agent;
            Rung = rung;
            Started = true;
            started = Time.time;
            violationsAtStart = FairnessGuard.Violations;
            Belief.Begin();
            Subscribe();
        }

        void Subscribe()
        {
            GameEvents.SpillCleaned += OnSpill;
            GameEvents.ShelfRestocked += OnShelf;
            GameEvents.BinBagged += OnBin;
            GameEvents.BagDisposed += OnBag;
            GameEvents.CustomerServed += OnServed;
            GameEvents.DirectionsGiven += OnDirections;
            GameEvents.CoffeeDrunk += OnCoffee;
            GameEvents.PunchAttempted += OnPunch;
        }

        public void Unsubscribe()
        {
            GameEvents.SpillCleaned -= OnSpill;
            GameEvents.ShelfRestocked -= OnShelf;
            GameEvents.BinBagged -= OnBin;
            GameEvents.BagDisposed -= OnBag;
            GameEvents.CustomerServed -= OnServed;
            GameEvents.DirectionsGiven -= OnDirections;
            GameEvents.CoffeeDrunk -= OnCoffee;
            GameEvents.PunchAttempted -= OnPunch;
        }

        void OnSpill(Dirt d) => SpillsMopped++;
        void OnShelf(ShelfUnit u, int n) => ShelvesRestocked++;
        void OnBin(Trashcan t) => BinsBagged++;
        void OnBag(TrashBag b) => BagsDisposed++;
        void OnCoffee(Vector3 at) => Coffees++;
        void OnDirections(CustomerNPC c) { DirectionsGiven++; directionsThisTick++; }

        void OnServed(CustomerNPC c)
        {
            CustomersServed++;
            if (c != null && waitingSince.TryGetValue(c, out float since))
            {
                float w = Time.time - since;
                Waits.Add(w);
                LongestWait = Mathf.Max(LongestWait, w);
                waitingSince.Remove(c);
            }
        }

        void OnPunch(bool accepted)
        {
            TaskManager tasks = Object.FindAnyObjectByType<TaskManager>();
            if (!accepted && tasks != null && !tasks.AllComplete) SpuriousClockOuts++;
        }

        public float WaitedFor(CustomerNPC c) => c != null && waitingSince.TryGetValue(c, out float since) ? Time.time - since : 0f;

        // Called every frame by the environment.
        public void Tick(TaskManager tasks, BurnoutSystem burnout, Transform player)
        {
            if (player != null) Belief.Tick(Time.deltaTime, player.position);

            foreach (CustomerNPC c in CustomerNPC.All)
            {
                if (c == null) continue;
                if (c.IsWaitingToBeServed && !waitingSince.ContainsKey(c)) waitingSince[c] = Time.time;
            }
            foreach (CustomerNPC gone in waitingSince.Keys.Where(k => k == null || !k.IsWaitingToBeServed).ToList())
                if (gone == null) waitingSince.Remove(gone);

            // A shopper who stops asking without being helped has given up.
            int asking = CustomerRequest.PendingCount;
            if (asking < lastAsking) CustomersLost += Mathf.Max(0, lastAsking - asking - directionsThisTick);
            lastAsking = asking;
            directionsThisTick = 0;

            if (burnout != null) MinEnergy = Mathf.Min(MinEnergy, burnout.Energy01);

            if (Time.frameCount % 15 == 0 && tasks != null)
            {
                int outstanding = tasks.Tasks.Count(t => !t.IsComplete);
                int waitingLong = waitingSince.Values.Count(s => Time.time - s > 60f);
                Pressure.Add((Time.time - started, outstanding, waitingLong));
            }
        }

        public void Record(string verb, string kind, bool ok)
        {
            Steps++;
            Trace.Add(new Step { T = Time.time - started, Verb = verb, Kind = kind, Ok = ok });
        }

        public void End(bool clockedOut, bool timedOut, BurnoutSystem burnout, float wallSeconds)
        {
            ClockedOut = clockedOut;
            TimedOut = timedOut;
            SimSeconds = Time.time - started;
            WallSeconds = wallSeconds;
            SpillsLeftStanding = Dirt.ActiveCount;
            CustomersLost += waitingSince.Count;           // still queued when it ended
            EnergyAtEnd = burnout != null ? burnout.Energy01 : 1f;

            KarenBrain brain = KarenBrain.Instance;
            if (brain != null)
            {
                KarenStats s = brain.Stats;
                FirstDetection = s.FirstDetection;
                Detections = s.Detections;
                Catches = s.Catches;
                Chases = s.Chases;
                var rewards = s.TacticRewards.Values.SelectMany(v => v).ToList();
                RewardMeanAbs = rewards.Count > 0 ? rewards.Average(r => Mathf.Abs(r)) : 0f;
                CloseIns = brain.Dash.CloseIns;
                Vanishes = brain.Dash.Vanishes;
                Reveals = brain.Dash.Reveals;
                Overtimes = s.Overtimes;
                TacticEntropy = s.TacticEntropy;
                TacticsUsed = s.TacticCounts.Where(p => TacticLibrary.Get(p.Key)?.Tier > 0).Sum(p => p.Value);
                TopTactics = string.Join(" ", s.TacticCounts.Where(p => TacticLibrary.Get(p.Key)?.Tier > 0)
                                                .OrderByDescending(p => p.Value).Take(4).Select(p => $"{p.Key}×{p.Value}"));
                var h = brain.Director.History;
                if (h.Count > 0)
                {
                    MeanPanic = h.Average(x => x.panic);
                }
                PanicSetpointRmse = brain.Stats.SetpointRmse;
            }

            FairnessViolations = FairnessGuard.Violations - violationsAtStart;
            Belief.End();
            Failures = FailureTaxonomy.Classify(this);
            Unsubscribe();
        }

        public Dictionary<string, object> ToDictionary()
        {
            var d = new Dictionary<string, object>
            {
                ["seed"] = Seed, ["shift"] = Shift, ["agent"] = Agent, ["rung"] = Rung,
                ["clocked_out"] = ClockedOut, ["timed_out"] = TimedOut,
                ["sim_seconds"] = SimSeconds, ["wall_seconds"] = WallSeconds, ["steps"] = Steps,
                ["spills_mopped"] = SpillsMopped, ["shelves_restocked"] = ShelvesRestocked, ["bins_bagged"] = BinsBagged,
                ["bags_disposed"] = BagsDisposed, ["customers_served"] = CustomersServed, ["directions_given"] = DirectionsGiven,
                ["coffees"] = Coffees, ["spills_left_standing"] = SpillsLeftStanding, ["customers_lost"] = CustomersLost,
                ["mean_wait_s"] = MeanWait, ["longest_wait_s"] = LongestWait, ["energy_at_end"] = EnergyAtEnd, ["min_energy"] = MinEnergy,
                ["spurious_clock_outs"] = SpuriousClockOuts,
                ["karen_first_detection_s"] = FirstDetection, ["karen_detections"] = Detections, ["karen_catches"] = Catches,
                ["karen_chases"] = Chases, ["karen_overtimes"] = Overtimes, ["karen_tactic_entropy_bits"] = TacticEntropy,
                ["karen_dash_close_ins"] = CloseIns, ["karen_dash_vanishes"] = Vanishes, ["karen_dash_reveals"] = Reveals,
                ["karen_reward_mean_abs"] = RewardMeanAbs,
                ["bot_reactive"] = BotReactive, ["bot_mean_fear"] = BotMeanFear, ["bot_peak_fear"] = BotPeakFear,
                ["bot_startles"] = BotStartles, ["bot_hides"] = BotHides, ["bot_back_offs"] = BotBackOffs,
                ["karen_tactics_used"] = TacticsUsed, ["karen_top_tactics"] = TopTactics,
                ["mean_panic"] = MeanPanic, ["panic_setpoint_rmse"] = PanicSetpointRmse,
                ["karen_fairness_violations"] = FairnessViolations,
                ["failures"] = Failures
            };
            Belief.AddTo(d);
            return d;
        }
    }

    // IDEAS.md, the agent test environment: a failure taxonomy. Each episode can carry several labels.
    public static class FailureTaxonomy
    {
        public const string Starvation = "starvation";
        public const string Thrashing = "thrashing";
        public const string InterferenceBlindness = "interference_blindness";
        public const string ClockBlindness = "clock_blindness";
        public const string ResourceMismanagement = "resource_mismanagement";
        public const string SpuriousCompletion = "spurious_completion";

        public static List<string> Classify(EpisodeMetrics m)
        {
            var labels = new List<string>();
            var work = m.Trace.Where(s => !string.IsNullOrEmpty(s.Kind)).ToList();

            // Starvation: one kind of job dominates while others sit outstanding.
            if (work.Count >= 6)
            {
                var top = work.GroupBy(s => s.Kind).OrderByDescending(g => g.Count()).First();
                float share = (float)top.Count() / work.Count;
                bool othersWaiting = m.Pressure.Count > 0 && m.Pressure.Count(p => p.outstanding >= 2) > m.Pressure.Count / 2;
                if (share >= 0.75f && othersWaiting) labels.Add(Starvation);
            }

            // Thrashing: switching job every few steps and finishing little.
            if (work.Count >= 8)
            {
                int switches = 0;
                for (int i = 1; i < work.Count; i++) if (work[i].Kind != work[i - 1].Kind) switches++;
                float perMinute = switches / Mathf.Max(1f, m.SimSeconds / 60f);
                int completions = m.SpillsMopped + m.ShelvesRestocked + m.BinsBagged + m.CustomersServed + m.BagsDisposed;
                if (perMinute > 6f && completions < 3) labels.Add(Thrashing);
            }

            // Interference blindness: a customer left waiting a minute, or given up on, while
            // the agent was busy with other jobs.
            bool neglected = m.LongestWait > 60f || m.CustomersLost > 0 || m.Pressure.Any(p => p.waitingLong > 0);
            if (neglected && work.Count(s => s.Kind != "Serve") >= 3) labels.Add(InterferenceBlindness);

            // Clock blindness: ran out the clock without getting out.
            if (m.TimedOut && !m.ClockedOut) labels.Add(ClockBlindness);

            // Resource mismanagement: energy run to empty.
            if (m.MinEnergy <= 0.01f) labels.Add(ResourceMismanagement);

            // Spurious completion: tried to clock out believing the jobs were done.
            if (m.SpuriousClockOuts > 0) labels.Add(SpuriousCompletion);

            return labels;
        }
    }
}
