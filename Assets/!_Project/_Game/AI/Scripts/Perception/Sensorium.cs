using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Where the building's own sensors report: CCTV, door sensors, the till, the breaker
    // panel. World objects post here without holding a reference to Karen.
    public static class InfrastructureFeed
    {
        public static event System.Action<Observation> Reported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reported = null;

        public static void Report(in Observation observation) => Reported?.Invoke(observation);
    }

    // What Karen believes about your energy — not the bar itself, which is yours. She
    // knows the store's policy (how fast a shift wears people down, how much a coffee
    // gives back) and she counts what she hears: sprinting, the coffee machine. Believing
    // you're spent shrinks the area she searches (Karen.md §4.2).
    public sealed class EnergyBelief
    {
        public float Energy { get; private set; } = 1f;
        public bool CanSprint => Energy > 0.02f;
        public float SpeedEstimate => CanSprint ? 3.1f : 2.0f;

        float drainPerSecond = 0.2f / 60f;
        float coffee = 0.35f;
        float sprintMultiplier = 2f;

        public void BeginShift(int shiftIndex, BurnoutSystem policy)
        {
            if (policy != null)
            {
                drainPerSecond = policy.drainPerMinute / 60f;
                coffee = policy.coffeeRefill;
                sprintMultiplier = policy.sprintDrainMultiplier;
                Energy = Mathf.Clamp01(1f - policy.startingEnergyLossPerShift * Mathf.Max(0, shiftIndex));
            }
            else
            {
                Energy = 1f;
            }
        }

        public void Tick(float dt) => Energy = Mathf.Clamp01(Energy - drainPerSecond * dt);
        public void HeardSprinting(float seconds) => Energy = Mathf.Clamp01(Energy - drainPerSecond * (sprintMultiplier - 1f) * seconds);
        public void HeardCoffee() => Energy = Mathf.Clamp01(Energy + coffee);
    }

    // Fuses every channel into one stream of Observations for the belief grid, plus the
    // "nothing here" sweep. Owned by the brain; reads the world through Karen's eyes and
    // ears, which are wherever her body is standing.
    public sealed class Sensorium
    {
        public readonly List<Observation> Positive = new List<Observation>();
        public readonly List<int> SweptCells = new List<int>();
        public readonly List<float> SweptProbability = new List<float>();

        // Set when something warrants deciding again right now.
        public bool LoudNoiseNearby { get; private set; }
        public NoiseEvent LastHeard { get; private set; }
        public int HeardThisTick { get; private set; }

        public bool UseHearing = true;
        public bool UseTraces = true;
        public bool UseTestimony = true;
        public bool UseInfrastructure = true;
        public bool UseNegative = true;

        public int SweepCellsPerTick = 120;

        // The sweep's cost (0.2.0): cells inside her cone, and rays cast, on the last sweep.
        public int SweepConeCells { get; private set; }
        public int SweepRays { get; private set; }

        readonly StoreMap map;
        readonly EnergyBelief energy;
        readonly List<NoiseEvent> noises = new List<NoiseEvent>();
        readonly List<Observation> infrastructure = new List<Observation>();
        readonly HashSet<int> tracesRead = new HashSet<int>();
        readonly Dictionary<CustomerMemory, float> polled = new Dictionary<CustomerMemory, float>();
        readonly List<int> cone = new List<int>();
        float[] hearingDistance;
        long noiseCursor;
        int sweepOffset;
        float sprintHeardUntil;

        public Sensorium(StoreMap map, EnergyBelief energy)
        {
            this.map = map;
            this.energy = energy;
            noiseCursor = NoiseBus.Latest;
            InfrastructureFeed.Reported += OnInfrastructure;
        }

        public void Dispose() => InfrastructureFeed.Reported -= OnInfrastructure;

        void OnInfrastructure(Observation o)
        {
            if (UseInfrastructure) infrastructure.Add(o);
        }

        public void ForgetShift()
        {
            tracesRead.Clear();
            polled.Clear();
            infrastructure.Clear();
            noiseCursor = NoiseBus.Latest;
        }

        public void Collect(Vector3 bodyPosition, SightSensor sight, Transform bodyRoot)
        {
            Positive.Clear();
            SweptCells.Clear();
            SweptProbability.Clear();
            LoudNoiseNearby = false;
            HeardThisTick = 0;

            CollectSight(sight);
            if (UseHearing) CollectHearing(bodyPosition);
            else NoiseBus.ReadSince(ref noiseCursor, noises);
            if (UseTraces) CollectTraces(sight, bodyRoot);
            if (UseTestimony) CollectTestimony(bodyPosition);
            if (UseNegative && !sight.SeesNow) CollectSweep(sight, bodyRoot);

            Positive.AddRange(infrastructure);
            infrastructure.Clear();
        }

        void CollectSight(SightSensor sight)
        {
            if (!sight.SeesNow || sight.Awareness < sight.glimpseAt) return;

            // The more certain the look, the tighter the bump.
            float sigma = Mathf.Lerp(3f, 0.8f, sight.Awareness);
            Positive.Add(new Observation(SenseChannel.Sight, sight.LastSeenPosition, sight.Awareness, sigma,
                                         $"sight({sight.Band}, exposure {sight.Exposure:0.00})"));
        }

        void CollectHearing(Vector3 bodyPosition)
        {
            if (NoiseBus.ReadSince(ref noiseCursor, noises) == 0) return;

            bool anyPlayer = false;
            foreach (NoiseEvent n in noises)
            {
                if (n.Author == NoiseAuthor.Player) { anyPlayer = true; break; }
            }
            if (!anyPlayer) return;

            int here = map.CellAt(bodyPosition);
            if (here < 0) return;
            hearingDistance = map.Distances(here, NoiseBus.CarryPerUnit * 1.05f, hearingDistance);

            foreach (NoiseEvent n in noises)
            {
                if (n.Author != NoiseAuthor.Player) continue;

                int source = map.CellAt(n.Position);
                if (source < 0) continue;
                float d = hearingDistance[source];
                if (float.IsInfinity(d) || d > n.Carry) continue;

                float confidence = Mathf.Sqrt(1f - d / n.Carry) * 0.9f;
                float sigma = 1f + d * 0.12f;
                Positive.Add(new Observation(SenseChannel.Hearing, n.Position, confidence, sigma,
                                             $"noise({n.Kind}, {n.Loudness:0.0}, {d:0}m)"));
                HeardThisTick++;
                LastHeard = n;

                if (n.Kind == NoiseKind.Sprint)
                {
                    // Count sprinting once per second of it, not once per footstep.
                    if (Time.time > sprintHeardUntil)
                    {
                        energy.HeardSprinting(1f);
                        sprintHeardUntil = Time.time + 1f;
                    }
                }
                else if (n.Kind == NoiseKind.Coffee)
                {
                    energy.HeardCoffee();
                }

                if (n.Loudness >= 0.7f && confidence > 0.5f) LoudNoiseNearby = true;
            }
        }

        void CollectTraces(SightSensor sight, Transform bodyRoot)
        {
            TraceRegistry.Prune();
            foreach (Trace t in TraceRegistry.All)
            {
                if (tracesRead.Contains(t.Id)) continue;
                if ((t.Position - sight.Eye).sqrMagnitude > 14f * 14f) continue;
                if (!sight.CanSee(t.Position + Vector3.up * 0.15f, bodyRoot)) continue;

                tracesRead.Add(t.Id);

                float confidence, sigma;
                switch (t.Kind)
                {
                    case TraceKind.WetFootprint: confidence = 0.55f * t.Freshness; sigma = 3f; break;
                    case TraceKind.UndoneSabotage: confidence = 0.6f; sigma = 6f + t.Age * 0.1f; break;
                    case TraceKind.DroppedItem: confidence = 0.45f; sigma = 5f + t.Age * 0.1f; break;
                    case TraceKind.DoorOpened: confidence = 0.5f * t.Freshness; sigma = 4f; break;
                    case TraceKind.MopAway: confidence = 0.4f; sigma = 6f; break;
                    case TraceKind.BaggedBin: confidence = 0.35f * t.Freshness; sigma = 8f; break;
                    default: confidence = 0.3f; sigma = 7f + t.Age * 0.1f; break;
                }
                if (confidence < 0.05f) continue;

                Positive.Add(new Observation(SenseChannel.Trace, t.Position, confidence, sigma,
                                             $"trace({t.Kind}, {t.Age:0}s)", t.Heading));
            }
        }

        void CollectTestimony(Vector3 bodyPosition)
        {
            foreach (CustomerMemory m in CustomerMemory.All)
            {
                if (m == null || !m.HasSighting) continue;
                if ((m.transform.position - bodyPosition).sqrMagnitude > 2.2f * 2.2f && !m.possessed) continue;
                if (polled.TryGetValue(m, out float when) && when >= m.LastSeenTime) continue;

                polled[m] = m.LastSeenTime;
                Positive.Add(m.ToObservation());
            }
        }

        // Absence (Karen.md §3.6): cells in view that turned out empty. Round-robin over the
        // cells inside her cone so a sweep costs a fixed number of raycasts per tick, and each
        // cell's chance is her own eye's (SightSensor.Visibility) for someone standing there in
        // the open.
        void CollectSweep(SightSensor sight, Transform bodyRoot)
        {
            ConeCells(map, sight, cone);
            SweepConeCells = cone.Count;
            SweepRays = 0;
            if (cone.Count == 0) return;

            int budget = Mathf.Min(SweepCellsPerTick, cone.Count);
            for (int k = 0; k < budget; k++)
            {
                int cell = cone[(sweepOffset + k) % cone.Count];
                Vector3 p = map.CellPosition[cell] + Vector3.up * 1f;
                Vector3 d = p - sight.Eye;
                float p0 = Mathf.Clamp01(sight.Visibility(Vector3.Angle(sight.Forward, d), d.magnitude, LightProbe.LevelAt(map.CellPosition[cell])) * 0.85f);
                if (p0 < 0.03f) continue;
                SweepRays++;
                if (!sight.CanSee(p, bodyRoot)) continue;

                SweptCells.Add(cell);
                SweptProbability.Add(p0);
            }
            sweepOffset = (sweepOffset + budget) % Mathf.Max(1, cone.Count);
        }

        // The cells inside an eye's cone, at chest height: in range and within its half angle.
        public static void ConeCells(StoreMap map, SightSensor sight, List<int> into)
        {
            map.CellsWithin(sight.Eye, sight.range, into);
            int kept = 0;
            for (int i = 0; i < into.Count; i++)
            {
                Vector3 d = map.CellPosition[into[i]] + Vector3.up * 1f - sight.Eye;
                if (Vector3.Angle(sight.Forward, d) <= sight.HalfAngle) into[kept++] = into[i];
            }
            into.RemoveRange(kept, into.Count - kept);
        }
    }
}
