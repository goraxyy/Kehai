using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // The blink dash (IDEAS.md, the blink mechanic): when you blink, she moves. It isn't a
    // rung; it's one of the Director's instruments, on every rung that has a Director (C up).
    //
    //   Close in   she's in your view and far away: you blink, and she is suddenly much
    //              nearer — a jump along her path to you, never ending closer than dashKeepAway.
    //   Vanish     she's in your view: you blink, and she's gone. She is somewhere you didn't
    //              expect, out of your sight near where you're going (or behind you), standing
    //              silent. Come within revealRange and she steps out; that is the wave's peak.
    //
    // Wave dramaturgy. The Director's phases are the waves (KarenDirector.UpdatePhase):
    //   Settle, Recover   no dashes at all: the tension really drops between waves.
    //   Build             close-ins, at most dashCloseInsPerWave: dread that keeps rising.
    //                     If she's already near you, the wave's one vanish instead.
    //   Spike             the wave's one vanish; close-ins if it's spent and she's far.
    //   Crunch            close-ins, and a vanish if the wave hasn't had one.
    // Stepping out after a vanish ends the wave: the Director starts a recovery.
    //
    // Fair by the same contract as every trick (Karen.md §9): each wave is told — her eye
    // flicker, at least minTellLead before any dash — and a dash only happens while she can
    // see you, never during a chase, never to within dashKeepAway, and never in recovery.
    public sealed class BlinkDash
    {
        public enum Kind { None, CloseIn, Vanish }

        public int Wave { get; private set; }
        public bool Lurking { get; private set; }
        public int CloseIns { get; private set; }
        public int Vanishes { get; private set; }
        public int Reveals { get; private set; }

        int closeInsThisWave;
        bool vanishedThisWave;
        float armedAt = -999f, lastDashAt = -999f, lurkUntil;
        KarenDirector.Phase lastPhase = KarenDirector.Phase.OffShift;

        public void BeginShift()
        {
            Wave = 0;
            CloseIns = Vanishes = Reveals = 0;
            closeInsThisWave = 0;
            vanishedThisWave = false;
            armedAt = lastDashAt = -999f;
            lastPhase = KarenDirector.Phase.OffShift;
            Lurking = false;
        }

        // ---- the waves --------------------------------------------------------------------

        static bool Calm(KarenDirector.Phase p) =>
            p == KarenDirector.Phase.OffShift || p == KarenDirector.Phase.Settle || p == KarenDirector.Phase.Recover;

        // Every Director tick: a wave starts when the Director leaves the calm, and the spike
        // within it is told again.
        public void Tick(KarenContext c)
        {
            if (!c.Features.Goals) return;
            KarenDirector.Phase phase = c.Director.CurrentPhase;
            if (phase == lastPhase) return;
            bool fromCalm = Calm(lastPhase);
            lastPhase = phase;

            if (Calm(phase))
            {
                armedAt = -999f;
                return;
            }
            if (fromCalm)
            {
                Wave++;
                closeInsThisWave = 0;
                vanishedThisWave = false;
            }
            if (fromCalm || phase == KarenDirector.Phase.Spike)
            {
                armedAt = Time.time;
                float lead = Mathf.Max(1f, c.Config.minTellLead);
                c.World.PlayTell(TellKind.EyeFlicker, c.Body.Position, lead);
                c.Brain.RecordTell(TellKind.EyeFlicker, c.Body.Position, lead);
                c.Think("DASH", $"DASH     wave {Wave} {(phase == KarenDirector.Phase.Spike ? "peaks" : "begins")} — she's watching your blinks");
            }
        }

        // What a blink right now would let her do. Pure, so the rules can be tested.
        public static Kind Choose(KarenDirector.Phase phase, bool inRecovery, bool chasing, bool seesYou, bool watched,
                                  float gap, int closeInsThisWave, bool vanishedThisWave,
                                  float sinceArmed, float sinceLastDash, KarenConfig config)
        {
            if (Calm(phase) || inRecovery || chasing) return Kind.None;
            if (sinceArmed < config.minTellLead || sinceLastDash < config.dashCooldown) return Kind.None;
            if (!seesYou || !watched) return Kind.None;          // "in view": she sees you looking at her

            bool far = gap >= config.dashCloseInMinRange;
            bool closeInLeft = closeInsThisWave < config.dashCloseInsPerWave;
            bool canVanish = !vanishedThisWave && gap >= 4f;

            switch (phase)
            {
                case KarenDirector.Phase.Build:
                    if (far) return closeInLeft ? Kind.CloseIn : Kind.None;
                    return canVanish ? Kind.Vanish : Kind.None;
                case KarenDirector.Phase.Spike:
                    if (canVanish) return Kind.Vanish;
                    return far && closeInLeft ? Kind.CloseIn : Kind.None;
                case KarenDirector.Phase.Crunch:
                    if (far && closeInLeft) return Kind.CloseIn;
                    return canVanish ? Kind.Vanish : Kind.None;
                default:
                    return Kind.None;
            }
        }

        // ---- a blink ------------------------------------------------------------------------

        public Kind OnBlink(KarenContext c)
        {
            if (!c.Features.Goals || Lurking) return Kind.None;
            SightSensor sight = c.Body.Sight;
            Vector3 you = sight.LastSeenPosition;
            float gap = Flat(c.Body.Position, you);
            Kind kind = Choose(c.Director.CurrentPhase, c.Director.InRecovery, c.Brain.IsChasing, sight.SeesNow, sight.IsWatched,
                               gap, closeInsThisWave, vanishedThisWave, Time.time - armedAt, Time.time - lastDashAt, c.Config);
            if (kind == Kind.None) return kind;

            bool done = kind == Kind.CloseIn ? CloseIn(c, you, gap) : Vanish(c, you, sight.LastSeenVelocity);
            if (!done) return Kind.None;

            lastDashAt = Time.time;
            FairnessGuard.CheckToldEffect($"blink dash ({kind})", Time.time - armedAt, c.Config.minTellLead);
            return kind;
        }

        bool CloseIn(KarenContext c, Vector3 you, float gap)
        {
            float step = Mathf.Min(Mathf.Clamp(gap * c.Config.dashStepFraction, c.Config.dashMinStep, c.Config.dashMaxStep),
                                   gap - c.Config.dashKeepAway);
            if (step < c.Config.dashMinStep) return false;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(c.Body.Position, you, NavMesh.AllAreas, path) || path.corners.Length < 2) return false;
            Vector3 to = AlongPath(path.corners, step);
            if (!NavMesh.SamplePosition(to, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) return false;
            if (Flat(hit.position, you) < c.Config.dashKeepAway - 0.5f) return false;

            Vector3 from = c.Body.Position;
            c.Body.Warp(hit.position);
            c.Body.TurnToward(you);
            closeInsThisWave++;
            CloseIns++;
            c.Think("DASH", $"DASH     close in {Flat(from, hit.position):0.0} m while you blinked ({gap:0.0} → {Flat(hit.position, you):0.0} m)");
            KarenNarrator.Say(StoryKind.Blink, $"You blinked — and {GameNames.Antagonist} was suddenly much closer.", hit.position);
            return true;
        }

        bool Vanish(KarenContext c, Vector3 you, Vector3 velocity)
        {
            Vector3 spot;
            if (!FindHidingSpot(c, you, velocity, out spot)) return false;

            Vector3 from = c.Body.Position;
            c.Brain.HandOverToDash();
            c.Body.Warp(spot);
            c.Body.Stop();
            c.Body.Silent = true;
            c.Body.TurnToward(you);
            Lurking = true;
            lurkUntil = Time.time + c.Config.lurkSeconds;
            vanishedThisWave = true;
            Vanishes++;
            c.Think("DASH", $"DASH     vanish: gone from {c.Where(from)}, waiting silent at {c.Where(spot)} ({Flat(spot, you):0.0} m from you, out of your sight)");
            KarenNarrator.Say(StoryKind.Blink, $"You blinked — and {GameNames.Antagonist} was gone.", from);
            return true;
        }

        // ---- lurking, after a vanish ---------------------------------------------------------

        // While she waits she does nothing else: no plan, no footsteps. She steps out when
        // you come close enough for her to see, and leaves quietly if you never do.
        public void TickLurk(KarenContext c)
        {
            if (!Lurking) return;
            SightSensor sight = c.Body.Sight;
            c.Body.Stop();
            if (sight.SeesNow)
            {
                c.Body.TurnToward(sight.LastSeenPosition);
                float gap = Flat(c.Body.Position, sight.LastSeenPosition);
                if (gap <= c.Config.revealRange)
                {
                    Reveal(c, sight.LastSeenPosition, gap);
                    return;
                }
            }
            if (Time.time > lurkUntil)
            {
                EndLurk(c);
                c.Think("DASH", "DASH     nobody came — she moves on");
            }
        }

        void Reveal(KarenContext c, Vector3 you, float gap)
        {
            EndLurk(c);
            Reveals++;
            c.World.PlayTell(TellKind.RevealSting, c.Body.Position, 0f);
            c.Body.SetMood(KarenBody.Mood.Hunt);
            if (gap > 3.5f)
                c.Body.BurstToward(you, c.Config.runSpeed * 1.4f, 0.3f, Mathf.Min(1.5f, gap - 2.5f));
            c.Think("DASH", $"DASH     she steps out at {gap:0.0} m — the wave breaks");
            KarenNarrator.Say(StoryKind.Blink, $"{GameNames.Antagonist} was waiting for you.", c.Body.Position);
            c.Director.StartRecovery(c.Config.recoverySeconds, c, "she stepped out: the wave breaks");
        }

        void EndLurk(KarenContext c)
        {
            Lurking = false;
            c.Body.Silent = false;
            c.Brain.ResumeDeciding();
        }

        public void Cancel(KarenContext c)
        {
            if (Lurking) EndLurk(c);
        }

        // ---- where to go ---------------------------------------------------------------------

        // A walkable spot dashVanishMin–dashVanishMax from where she last saw you, that you can't
        // see from there, and that you can reach without leaving the area. Ahead of where you
        // were heading, or — dashBehindChance of the time, or if you stood still — beyond you
        // from where she was: behind you, as you were looking at her.
        bool FindHidingSpot(KarenContext c, Vector3 you, Vector3 velocity, out Vector3 spot)
        {
            spot = default;
            if (c.Map == null || c.Map.CellCount == 0) return false;

            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 beyond = Flatten(you - c.Body.Position).normalized;
            Vector3 aim = flatVelocity.magnitude > 0.5f ? flatVelocity.normalized : beyond;
            if (c.Rng.Chance(c.Config.dashBehindChance)) aim = flatVelocity.magnitude > 0.5f ? -aim : beyond;

            Vector3 eye = you + Vector3.up * 1.6f;
            Transform karen = c.Body.transform;
            var scored = new List<(Vector3 at, float score)>();
            for (int cell = 0; cell < c.Map.CellCount; cell++)
            {
                Vector3 p = c.Map.CellPosition[cell];
                float d = Flat(p, you);
                if (d < c.Config.dashVanishMin || d > c.Config.dashVanishMax) continue;
                if (Mathf.Abs(p.y - you.y) > 1.5f) continue;                       // same floor
                if (!Hidden(eye, p + Vector3.up * 1.4f, karen)) continue;          // you can't see it
                float along = Vector3.Dot(Flatten(p - you).normalized, aim);
                scored.Add((p, along + c.Rng.Range(0f, 0.35f)));
            }
            scored.Sort((a, b) => b.score.CompareTo(a.score));

            var path = new NavMeshPath();
            int tries = 0;
            foreach (var (at, _) in scored)
            {
                if (++tries > 12) break;
                if (!NavMesh.CalculatePath(you, at, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                if (PathLength(path.corners) > c.Config.dashVanishMax * 1.8f) continue;     // near by foot, not through a wall
                spot = at;
                return true;
            }
            return false;
        }

        // Anything solid between the two points, other than her and the shoppers. (The ray
        // starts inside your own capsule, so that never counts.)
        static readonly RaycastHit[] hits = new RaycastHit[12];

        static bool Hidden(Vector3 from, Vector3 to, Transform karen)
        {
            Vector3 d = to - from;
            float distance = d.magnitude;
            if (distance < 0.01f) return false;
            int count = Physics.RaycastNonAlloc(from, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = hits[i].collider;
                if (hit == null) continue;
                if (karen != null && hit.transform.IsChildOf(karen)) continue;
                if (hit.GetComponentInParent<NavMeshAgent>() != null) continue;
                return true;
            }
            return false;
        }

        // ---- geometry ---------------------------------------------------------------------

        public static Vector3 AlongPath(Vector3[] corners, float distance)
        {
            for (int i = 1; i < corners.Length; i++)
            {
                float leg = Vector3.Distance(corners[i - 1], corners[i]);
                if (distance <= leg) return Vector3.Lerp(corners[i - 1], corners[i], leg > 0f ? distance / leg : 0f);
                distance -= leg;
            }
            return corners[corners.Length - 1];
        }

        static float PathLength(Vector3[] corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float Flat(Vector3 a, Vector3 b) => Flatten(a - b).magnitude;
    }
}
