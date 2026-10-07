using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Aiko
{
    // The Director (Aiko.md §2.2, §7.3–7.4). Sees everything, controls nothing directly.
    //
    // It measures fear by its motor consequences — the Panic Index — and runs a PI
    // controller against a *setpoint*, not a maximum: quiet is something it schedules, not
    // something that happens when Aiko fails. Its levers are the tension budget, which
    // tiers of tactic are permitted, a tightly capped hint to the belief grid, and free
    // dread that costs the body nothing: a noise with no author, a flickering light.
    //
    // It may never move the body or hand it the player's position. That boundary is the
    // whole of fairness rule 2.
    public sealed class AikoDirector
    {
        public enum Phase { OffShift, Settle, Build, Spike, Recover, Crunch }

        public float Panic { get; private set; }
        public float Setpoint { get; private set; }
        public float Pressure { get; private set; }
        public float Integral { get; private set; }
        public float Tension { get; private set; } = 0.5f;
        public Phase CurrentPhase { get; private set; } = Phase.OffShift;
        public string PhaseName => CurrentPhase.ToString().ToUpperInvariant();
        public bool InRecovery => Time.time < recoveryUntil;
        public float RawPanic { get; private set; }
        public PanicIndex Index { get; }

        readonly AikoConfig config;
        readonly List<(float t, float panic)> history = new List<(float, float)>(4096);
        float recoveryUntil = -1f;
        float spikeStarted = -1f;
        float spikeEnded = -1f;
        float overSetpointSince = -1f;
        float suppressPursuitUntil = -1f;
        float lastEnvironmental;
        float biasThisMinute;
        float biasMinuteStarted;
        float shiftStarted;
        float shiftLength = 300f;
        int shiftNumber = 1;

        public AikoDirector(AikoConfig config, AikoLedger ledger)
        {
            this.config = config;
            Index = new PanicIndex(config, ledger);
        }

        public IReadOnlyList<(float t, float panic)> History => history;

        public void BeginShift(int shift, float length)
        {
            shiftNumber = Mathf.Max(1, shift);
            shiftLength = Mathf.Max(60f, length);
            shiftStarted = Time.time;
            Integral = 0f;
            Tension = 0.4f;
            recoveryUntil = -1f;
            spikeStarted = spikeEnded = -1f;
            overSetpointSince = -1f;
            history.Clear();
            Index.BeginShift(shift);
            CurrentPhase = Phase.Settle;
        }

        public void EndShift() => CurrentPhase = Phase.OffShift;

        public void Dispose() => Index.Detach();

        public float ShiftTime => Time.time - shiftStarted;

        // ---- the loop -------------------------------------------------------------------

        public void Tick(float dt, AikoContext c)
        {
            if (CurrentPhase == Phase.OffShift) return;

            RawPanic = Index.Sample(dt, c);
            // Fast attack, slow decay: fear spikes and drains slowly.
            float tau = RawPanic > Panic ? config.panicAttack : config.panicDecay;
            Panic += (RawPanic - Panic) * (1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
            Panic = Mathf.Clamp01(Panic);

            UpdatePhase(c);
            Setpoint = SetpointFor(CurrentPhase);

            float e = Setpoint - Panic;
            Integral = Mathf.Clamp(Integral + e * dt, -config.integralLimit, config.integralLimit);
            Pressure = Mathf.Clamp(config.kp * e + config.ki * Integral, -1f, 1f);

            Tension = Mathf.Min(1f, Tension + config.tensionRegenPerSecond * (0.5f + Mathf.Max(0f, Pressure)) * dt);

            if (history.Count >= 4000) history.RemoveRange(0, 1000);
            history.Add((Time.time, Panic));

            MaybeEnvironmental(c);
        }

        void UpdatePhase(AikoContext c)
        {
            Phase before = CurrentPhase;
            float t = ShiftTime;
            float scale = shiftLength / 300f;
            float settle = Mathf.Max(30f, 60f * scale);
            float remaining = c.Shift != null ? c.Shift.TimeRemaining : shiftLength - t;
            bool crunch = remaining < 90f * Mathf.Min(1f, scale) || c.A.TaskLoad > 0.75f;

            // Habituation guard (Scenario D): pinned far above target for too long means the
            // scares have stopped working. Buy real quiet, even though it costs.
            if (Panic > Setpoint + 0.25f && CurrentPhase != Phase.Recover)
            {
                if (overSetpointSince < 0f) overSetpointSince = Time.time;
                if (Time.time - overSetpointSince > 120f * scale)
                {
                    StartRecovery(90f, c, "habituation risk HIGH · gating tiers 2-4 · setpoint → 0.30");
                    overSetpointSince = -1f;
                }
            }
            else overSetpointSince = -1f;

            if (InRecovery) CurrentPhase = Phase.Recover;
            else if (t < settle) CurrentPhase = Phase.Settle;
            else if (spikeStarted > 0f && spikeEnded < 0f)
            {
                CurrentPhase = Phase.Spike;
                if (Time.time - spikeStarted > 30f * scale)
                {
                    spikeEnded = Time.time;
                    StartRecovery(config.recoverySeconds, c, "spike over");
                }
            }
            else if (crunch) CurrentPhase = Phase.Crunch;
            else
            {
                CurrentPhase = Phase.Build;
                // The spike comes when the integral says the player has been coasting, or
                // halfway through regardless — a calm player gets a bigger one.
                bool due = spikeStarted < 0f && (Integral > config.integralLimit * 0.5f || t > shiftLength * 0.5f);
                if (due)
                {
                    spikeStarted = Time.time;
                    spikeEnded = -1f;
                    CurrentPhase = Phase.Spike;
                }
            }

            if (CurrentPhase != before)
            {
                c.Think("DIRECTOR", $"phase {before.ToString().ToUpperInvariant()} → {PhaseName} (panic {Panic:0.00}, setpoint {SetpointFor(CurrentPhase):0.00}, pressure {Pressure:+0.00;-0.00})");
                AikoNarrator.Say(StoryKind.Mood, AikoNarrator.Phase(CurrentPhase));
            }
        }

        float SetpointFor(Phase phase)
        {
            float career = Mathf.Min(0.2f, config.careerSetpointRise * (shiftNumber - 1));
            switch (phase)
            {
                case Phase.Settle: return 0.15f;
                case Phase.Build: return Mathf.Lerp(0.3f, 0.5f, Mathf.Clamp01(ShiftTime / shiftLength * 2f)) + career;
                case Phase.Spike: return 0.72f + career * 0.5f;
                case Phase.Recover: return 0.25f;
                case Phase.Crunch: return 0.55f + career;
                default: return 0f;
            }
        }

        // ---- permissions ----------------------------------------------------------------

        public bool PermitsTier(int tier, AikoContext c)
        {
            if (tier <= 0) return true;
            if (CurrentPhase == Phase.OffShift) return false;
            if (InRecovery || CurrentPhase == Phase.Settle) return tier <= 1;
            switch (tier)
            {
                case 1: return true;
                case 2: return Pressure > -0.05f || CurrentPhase == Phase.Crunch;
                case 3: return Pressure > 0.1f || CurrentPhase == Phase.Spike;
                default: return (Pressure > 0.2f || CurrentPhase == Phase.Spike) && Tension >= 0.55f;
            }
        }

        public float GoalPermission(GoalId goal, AikoContext c)
        {
            switch (goal)
            {
                case GoalId.Pursue:
                    return InRecovery || Time.time < suppressPursuitUntil ? 0f : 1f;
                case GoalId.Ambush:
                case GoalId.Herd:
                    return InRecovery ? 0f : 1f;
                case GoalId.Withdraw:
                case GoalId.Assist:
                case GoalId.Patrol:
                case GoalId.Investigate:
                case GoalId.Sweep:
                    return 1f;
                default:
                    return CurrentPhase == Phase.OffShift ? 0f : 1f;
            }
        }

        public void SuppressPursuit(float seconds) => suppressPursuitUntil = Mathf.Max(suppressPursuitUntil, Time.time + seconds);

        public bool TrySpend(float cost)
        {
            if (cost <= 0f) return true;
            if (Tension + 1e-4f < cost) return false;
            Tension -= cost;
            return true;
        }

        // Fairness rule 4: after a chase or a catch, at least recoverySeconds of lowered
        // setpoint with no major tactic and no ambush.
        public void StartRecovery(float seconds, AikoContext c, string why)
        {
            recoveryUntil = Mathf.Max(recoveryUntil, Time.time + seconds);
            Integral = Mathf.Min(Integral, 0f);
            c.Think("DIRECTOR", $"recovery window {seconds:0}s — {why}");
        }

        public float PanicAt(float t)
        {
            for (int i = history.Count - 1; i >= 0; i--)
                if (history[i].t <= t) return history[i].panic;
            return history.Count > 0 ? history[0].panic : Panic;
        }

        public float MeanPanic(float from, float to)
        {
            float sum = 0f;
            int n = 0;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                float t = history[i].t;
                if (t < from) break;
                if (t > to) continue;
                sum += history[i].panic;
                n++;
            }
            return n > 0 ? sum / n : Panic;
        }

        // ---- the bounded hint (fairness rule 2) -------------------------------------------

        // When the employee has been unthreatened for too long, nudge belief toward the truth
        // — at most 0.15 of the mass per minute, never past half of it, and never while the
        // employee is in line of sight of the body's current path.
        public void MaybeBias(AikoContext c, float dt)
        {
            if (CurrentPhase == Phase.OffShift || InRecovery) return;
            if (c.A.Staleness < 45f || Pressure < 0.1f) return;

            if (Time.time - biasMinuteStarted > 60f)
            {
                biasMinuteStarted = Time.time;
                biasThisMinute = 0f;
            }

            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return;
            int trueRegion = c.Map.RegionAt(player.Position);
            if (trueRegion < 0) return;
            if (c.Belief.RegionMass[trueRegion] >= config.searchBiasMaxMass) return;
            if (FairnessGuard.PlayerSeesPath(c.Body, player)) return;

            float allowed = config.searchBiasPerMinute - biasThisMinute;
            float step = Mathf.Min(allowed, config.searchBiasPerMinute / 60f * dt);
            if (step <= 0f) return;

            float room = config.searchBiasMaxMass - c.Belief.RegionMass[trueRegion];
            step = Mathf.Min(step, room);
            c.Belief.Bias(trueRegion, step);
            biasThisMinute += step;
            BiasApplied += step;
        }

        public float BiasApplied { get; private set; }

        // The employee's true position — for debug views and replays only. Nothing that
        // plans may call this; the fairness test checks it.
        public static Vector3 TruePlayerPosition => PlayerPresence.Current != null ? PlayerPresence.Current.Position : Vector3.zero;

        // ---- feeding the Ledger ----------------------------------------------------------

        float stillFor;
        float lastThreat = -999f;

        // The player model is built from the truth, which is the Director's to see: where
        // the employee spends time, where they hide, how they move (Aiko.md §7.1).
        public void ObserveForLedger(AikoContext c, float dt)
        {
            PlayerPresence player = PlayerPresence.Current;
            if (player == null || CurrentPhase == Phase.OffShift) return;

            int region = c.Map.RegionAt(player.Position);
            if (c.A.Awareness > 0.3f || c.Brain.CurrentPlan?.Goal == GoalId.Pursue) lastThreat = Time.time;

            MotionState motion = player.Motion;
            stillFor = motion == MotionState.Still || motion == MotionState.Crouching ? stillFor + dt : 0f;

            // Hiding: gone still or low after a threat, out of her sight, and she's near.
            bool hiding = stillFor > 3f && Time.time - lastThreat < 30f && c.A.Awareness < 0.1f
                          && Vector3.Distance(player.Position, c.Body.Position) < 30f;

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            c.Ledger.ObservePlayer(c.Map, region, dt, hiding, motion, motor != null ? motor.PlanarSpeed : 0f);
        }

        // ---- free dread -------------------------------------------------------------------

        void MaybeEnvironmental(AikoContext c)
        {
            if (InRecovery || CurrentPhase == Phase.Settle) return;
            if (Pressure < 0.15f || Time.time - lastEnvironmental < 25f) return;
            if (c.Rng.Value > 0.02f) return;   // ~ once every 10 s of eligible time
            lastEnvironmental = Time.time;

            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return;
            Vector3 near = player.Position;

            switch (c.Rng.Range(0, 3))
            {
                case 0:
                    // A noise with no author, somewhere between you and nothing.
                    Vector3 away = near + new Vector3(c.Rng.Range(-14f, 14f), 0f, c.Rng.Range(-14f, 14f));
                    c.World.Pa.PlayNear(away, ProceduralAudio.Tell(c.Rng.Chance(0.5f) ? TellKind.Scrape : TellKind.SilenceFalls), 0.8f);
                    NoiseBus.Emit(away, 0.5f, NoiseKind.Environmental, NoiseAuthor.Director);
                    c.Think("DIRECTOR", "environmental: a noise with no author near " + c.Where(away));
                    break;
                case 1:
                    Light light = LightProbe.NearestOn(near, 10f);
                    if (light != null) c.Brain.StartCoroutine(c.World.Lights.Flicker(light, 1.2f));
                    c.Think("DIRECTOR", "environmental: a light flickers");
                    break;
                default:
                    AutoDoubleDoor door = Object.FindAnyObjectByType<AutoDoubleDoor>();
                    if (door != null) OneShotAudio.PlayAt(door.openChime, door.transform.position, 0.6f);
                    c.Think("DIRECTOR", "environmental: the door chime, and nobody there");
                    break;
            }
        }
    }

    // The Panic Index (Aiko.md §7.3): fear measured by what it does to your hands.
    public sealed class PanicIndex
    {
        public const int Features = 8;
        public static readonly string[] Names =
            { "sprint bursts", "yaw jitter", "path inefficiency", "task abandonment", "freeze", "drops", "burnout slope", "blink rate" };
        static readonly float[] Weights = { 0.20f, 0.20f, 0.15f, 0.15f, 0.10f, 0.10f, 0.10f, 0.10f };
        static readonly float[] DefaultMean = { 1f, 35f, 1.5f, 0f, 0f, 0f, 1f, 16f };
        static readonly float[] DefaultStd = { 2f, 35f, 0.8f, 0.6f, 0.35f, 0.6f, 0.6f, 8f };

        public readonly float[] Value = new float[Features];
        public readonly float[] Z = new float[Features];

        readonly AikoConfig config;
        readonly AikoLedger ledger;
        readonly Queue<float> sprintStarts = new Queue<float>();
        readonly Queue<float> abandons = new Queue<float>();
        readonly Queue<float> drops = new Queue<float>();
        readonly Queue<float> blinks = new Queue<float>();
        readonly Queue<(float t, float yawRate)> yaw = new Queue<(float, float)>();
        readonly Queue<(float t, Vector3 p)> trail = new Queue<(float, Vector3)>();
        readonly float[] baselineSum = new float[Features];
        readonly float[] baselineSq = new float[Features];
        int baselineSamples;
        bool measuringBaseline;
        float shiftStart;
        bool wasSprinting;
        float stillFor;
        float lastEnergy = -1f;

        public PanicIndex(AikoConfig config, AikoLedger ledger)
        {
            this.config = config;
            this.ledger = ledger;
            GameEvents.MoppingAbandoned += OnMoppingAbandoned;
            GameEvents.PlayerDroppedItem += OnDropped;
        }

        public void Detach()
        {
            GameEvents.MoppingAbandoned -= OnMoppingAbandoned;
            GameEvents.PlayerDroppedItem -= OnDropped;
        }

        void OnMoppingAbandoned(Dirt d, float progress) { if (progress > 0.15f) abandons.Enqueue(Time.time); }
        void OnDropped(Item item, Vector3 at) { if (!NearSnapPoint(at)) drops.Enqueue(Time.time); }

        public void NoteBlink() => blinks.Enqueue(Time.time);

        static bool NearSnapPoint(Vector3 at)
        {
            foreach (ToolSnapPoint s in Object.FindObjectsByType<ToolSnapPoint>())
                if (Vector3.Distance(s.transform.position, at) < 2f) return true;
            return false;
        }

        public bool BaselineReady => ledger.Data.baselineSet;

        public void BeginShift(int shift)
        {
            shiftStart = Time.time;
            sprintStarts.Clear(); abandons.Clear(); drops.Clear(); yaw.Clear(); trail.Clear(); blinks.Clear();
            // The baseline is measured once, in the first calm minute of the career —
            // some people just play twitchy.
            measuringBaseline = !ledger.Data.baselineSet;
            System.Array.Clear(baselineSum, 0, Features);
            System.Array.Clear(baselineSq, 0, Features);
            baselineSamples = 0;
            lastEnergy = -1f;
        }

        public float Sample(float dt, AikoContext c)
        {
            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return 0f;
            float now = Time.time;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();

            bool sprinting = player.Motion == MotionState.Sprinting;
            if (sprinting && !wasSprinting) sprintStarts.Enqueue(now);
            wasSprinting = sprinting;

            if (motor != null && dt > 0f) yaw.Enqueue((now, Mathf.Abs(motor.YawThisFrame) / Mathf.Max(dt, 0.001f)));
            trail.Enqueue((now, player.Position));

            Trim(sprintStarts, now - 20f);
            Trim(abandons, now - 30f);
            Trim(drops, now - 30f);
            Trim(blinks, now - 30f);
            while (yaw.Count > 0 && yaw.Peek().t < now - 2f) yaw.Dequeue();
            while (trail.Count > 0 && trail.Peek().t < now - 10f) trail.Dequeue();

            // Freeze: standing still without doing anything for over two seconds.
            if (player.Motion == MotionState.Still) stillFor += dt; else stillFor = 0f;

            BurnoutSystem burnout = c.Brain.Burnout;
            float slope = 1f;
            if (burnout != null)
            {
                if (lastEnergy >= 0f && dt > 0f)
                {
                    float drain = (lastEnergy - burnout.Energy01) / dt;
                    float baseDrain = burnout.drainPerMinute / 60f;
                    slope = baseDrain > 0f ? Mathf.Clamp(drain / baseDrain, 0f, 5f) : 1f;
                }
                lastEnergy = burnout.Energy01;
            }

            Value[0] = sprintStarts.Count * 3f;                 // per minute
            Value[1] = YawJitter();
            Value[2] = Inefficiency();
            Value[3] = abandons.Count;
            // A freeze is two to twenty seconds of stillness; longer than that is someone who
            // has put the controller down, not someone holding their breath.
            Value[4] = stillFor > 20f ? 0f : Mathf.Clamp01((stillFor - 2f) / 6f);
            Value[5] = drops.Count;
            Value[6] = slope;
            Value[7] = blinks.Count * 2f;                        // per minute

            if (measuringBaseline)
            {
                for (int i = 0; i < Features; i++) { baselineSum[i] += Value[i]; baselineSq[i] += Value[i] * Value[i]; }
                baselineSamples++;
                if (now - shiftStart > config.baselineSeconds && baselineSamples > 20)
                {
                    for (int i = 0; i < Features; i++)
                    {
                        float mean = baselineSum[i] / baselineSamples;
                        float var = Mathf.Max(0f, baselineSq[i] / baselineSamples - mean * mean);
                        ledger.Data.baselineMean[i] = mean;
                        ledger.Data.baselineStd[i] = Mathf.Max(DefaultStd[i] * 0.5f, Mathf.Sqrt(var));
                    }
                    ledger.Data.baselineSet = true;
                    measuringBaseline = false;
                    c.Think("DIRECTOR", "baseline panic measured over the first calm minute");
                }
            }

            float raw = 0f, weight = 0f;
            bool blinkLive = c.Brain.BlinkPhysiological;
            for (int i = 0; i < Features; i++)
            {
                if (i == 7 && !blinkLive) continue;
                float mean = ledger.Data.baselineSet ? ledger.Data.baselineMean[i] : DefaultMean[i];
                float std = ledger.Data.baselineSet ? ledger.Data.baselineStd[i] : DefaultStd[i];
                Z[i] = (Value[i] - mean) / Mathf.Max(0.001f, std);
                raw += Weights[i] * Mathf.Clamp01(Z[i] / 3f);
                weight += Weights[i];
            }
            return Mathf.Clamp01(raw / Mathf.Max(0.001f, weight) * 1.6f);
        }

        float YawJitter()
        {
            if (yaw.Count < 3) return 0f;
            float mean = 0f;
            foreach (var y in yaw) mean += y.yawRate;
            mean /= yaw.Count;
            float var = 0f;
            foreach (var y in yaw) var += (y.yawRate - mean) * (y.yawRate - mean);
            return Mathf.Sqrt(var / yaw.Count);
        }

        float Inefficiency()
        {
            if (trail.Count < 5) return 1f;
            float length = 0f;
            Vector3 first = default, previous = default;
            bool started = false;
            foreach (var p in trail)
            {
                if (!started) { first = previous = p.p; started = true; continue; }
                length += Vector3.Distance(previous, p.p);
                previous = p.p;
            }
            float net = Vector3.Distance(first, previous);
            if (length < 2f) return 1f;
            return Mathf.Clamp(length / Mathf.Max(0.5f, net), 1f, 6f);
        }

        static void Trim(Queue<float> q, float before)
        {
            while (q.Count > 0 && q.Peek() < before) q.Dequeue();
        }
    }
}
