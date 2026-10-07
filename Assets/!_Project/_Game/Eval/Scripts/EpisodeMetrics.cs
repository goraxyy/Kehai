using System.Collections.Generic;
using System.Linq;
using Kehai.Aiko;
using UnityEngine;

namespace Kehai.Eval
{
    // Everything measured about one shift (IDEAS.md §1 "Metrics"), plus the action trace the
    // failure taxonomy reads.
    public sealed class EpisodeMetrics
    {
        public int Seed;
        public int Shift;
        public string Agent = "unknown";
        public string Rung = "";

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

        // Aiko's side, copied from her stats at the end.
        public float FirstDetection = -1f;
        public int Detections, Catches, Chases, Overtimes;
        public float TacticEntropy;
        public float MeanPanic, PanicSetpointRmse;
        public int TacticsUsed;
        public string TopTactics = "";
        public int FairnessViolations;
        int violationsAtStart;

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
            started = Time.time;
            violationsAtStart = FairnessGuard.Violations;
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
        public void Tick(TaskManager tasks, BurnoutSystem burnout)
        {
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

            AikoBrain brain = AikoBrain.Instance;
            if (brain != null)
            {
                AikoStats s = brain.Stats;
                FirstDetection = s.FirstDetection;
                Detections = s.Detections;
                Catches = s.Catches;
                Chases = s.Chases;
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
            Failures = FailureTaxonomy.Classify(this);
            Unsubscribe();
        }

        public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["seed"] = Seed, ["shift"] = Shift, ["agent"] = Agent, ["rung"] = Rung,
            ["clocked_out"] = ClockedOut, ["timed_out"] = TimedOut,
            ["sim_seconds"] = SimSeconds, ["wall_seconds"] = WallSeconds, ["steps"] = Steps,
            ["spills_mopped"] = SpillsMopped, ["shelves_restocked"] = ShelvesRestocked, ["bins_bagged"] = BinsBagged,
            ["bags_disposed"] = BagsDisposed, ["customers_served"] = CustomersServed, ["directions_given"] = DirectionsGiven,
            ["coffees"] = Coffees, ["spills_left_standing"] = SpillsLeftStanding, ["customers_lost"] = CustomersLost,
            ["mean_wait_s"] = MeanWait, ["longest_wait_s"] = LongestWait, ["energy_at_end"] = EnergyAtEnd, ["min_energy"] = MinEnergy,
            ["spurious_clock_outs"] = SpuriousClockOuts,
            ["aiko_first_detection_s"] = FirstDetection, ["aiko_detections"] = Detections, ["aiko_catches"] = Catches,
            ["aiko_chases"] = Chases, ["aiko_overtimes"] = Overtimes, ["aiko_tactic_entropy_bits"] = TacticEntropy,
            ["aiko_tactics_used"] = TacticsUsed, ["aiko_top_tactics"] = TopTactics,
            ["mean_panic"] = MeanPanic, ["panic_setpoint_rmse"] = PanicSetpointRmse,
            ["aiko_fairness_violations"] = FairnessViolations,
            ["failures"] = Failures
        };
    }

    // IDEAS.md §1 "The artifact": a failure taxonomy. Each episode can carry several labels.
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
