using UnityEngine;

namespace Kehai.Karen
{
    // Graded sight (Karen.md §3.1). Not "can I see the player" but a detection score that
    // builds while you're visible and drains while you aren't, so a glimpse through a gap
    // between two bays doesn't become a chase:
    //
    //     detect = angular · distance · light · motion · exposure
    //
    // The same component serves Karen's own eyes, the CCTV cameras (lower gain, narrower
    // cone), and the possessed customers — each is just an eye with different numbers.
    //
    // 0.2.0 gave her eyes their own numbers (KarenBody.ConfigureEye): a wider, longer cone
    // that works at half strength 16 m away, a higher floor so the best hiding still works,
    // more gain where her belief already expects you, and a short grace before the meter
    // drains. The cameras and shoppers keep the old ones.
    //
    // 0.3.0: fog blinds every eye the same (FogState). In a fogged room, or looking into one, an
    // eye sees no further than the fog's visibility — hers, a camera's, Kenji's, and yours.
    //
    // 0.5.1: solid cover still blocks the eyes. Clear eyes over a low shelf give full
    // exposure. Authored hiding interiors override that ray at 80% body-envelope coverage.
    // Witnessed entry is a separate, fixed-location memory; unseen occupancy is never evidence.
    //
    // Every ray starts at the eye (hers is at the top of her body, about your own eye height),
    // and stops at anything solid on any layer: walls, shelving, doors, people. Triggers, your
    // own body, the eye's own body and loose stock (things that can be knocked about) don't
    // block it.
    public class SightSensor : MonoBehaviour
    {
        public const float CloseRange = 2.2f;

        [Header("Cone")]
        public float range = 18f;
        [Range(10f, 180f)] public float fieldOfView = 120f;
        [Tooltip("How far away this eye works at half strength.")]
        public float halfStrengthAt = 8f;

        [Header("Integration")]
        [Tooltip("Awareness gained per second at a detection score of 1.")]
        public float gain = 2.2f;
        [Tooltip("Awareness lost per second while nothing is seen.")]
        public float decay = 0.45f;
        [Tooltip("How well it sees in the dark, as a floor under the light term. " + GameNames.Antagonist + "'s " +
                 "eyes are not a person's; darkness hides you less than you'd like.")]
        [Range(0f, 1f)] public float darkVision = 0.35f;
        [Tooltip("A score below this counts as nothing seen: the floor that keeps the best hiding safe.")]
        public float scoreFloor = 0.02f;
        [Tooltip("Seconds the meter holds after losing you, before it drains: a shelf end doesn't reset it.")]
        public float grace = 0f;
        [Tooltip("Up to this much more gain where her belief already expects you (1 doubles it).")]
        public float expectedBoost = 0f;

        // How much her belief expects you at a spot she's looking at: 0 (no more than
        // anywhere) to 1. Set by her brain from the belief grid (BeliefGrid.Expectation); read
        // only for a spot where this eye sees you.
        [System.NonSerialized] public System.Func<Vector3, float> Expectation;

        [Header("Thresholds")]
        public float glimpseAt = 0.2f;
        public float seeAt = 0.5f;
        public float confirmAt = 0.85f;

        // Colliders on the owner that must never block its own view.
        [System.NonSerialized] public Transform ignoreRoot;

        // What this eye saw go into a hiding place (WitnessedHide). Only her eye's is acted on.
        public WitnessedHide HiddenEntry { get; } = new WitnessedHide();
        // Emitted by this eye only. The Ledger subscribes to Karen’s own eye.
        public event System.Action<Vector3> SawHidingBoundary;
        bool hasLastVisible;
        Vector3 lastVisiblePosition;
        float lastVisibleAt;
        bool wasConcealed;
        HidingPlace wasConcealedIn;

        // The catch for a witnessed hide (0.5.1, item 2). The eye checks, as a sense, whether you
        // are still concealed in the place it saw you enter; the distance is from `observer` to
        // where it saw you, never to you. The brain gets only this yes or no.
        public bool CanCatchWitnessedHide(Vector3 observer)
        {
            var player = PlayerPresence.Current;
            HidingPlace place = null;
            bool concealed = false;
            if (player != null)
            {
                place = HidingPlace.At(player, out float coverage);
                concealed = place != null && place.Conceals(player, coverage);
            }
            HiddenEntry.UpdateOccupancy(place, concealed, Time.time);
            return HiddenEntry.CanCatch(observer);
        }

        // The look into a hiding place she has walked up to and opened, or knelt at (0.5.2: the
        // fridge, locker and kneel checks, I8 and I9). A sense she uses on purpose: the eye looks
        // into `place` from `observer` and finds you only if you are concealed in that place, and
        // within `reach`: of its inside once its door is open (`seesWholeInside`), or, when she
        // kneels and looks under a rack, from the rack's edge in front of her to you (Q24: 1.5 m,
        // past it you're safe). The brain picked the
        // place from what it sensed before (HideCheck) and gets only this yes or no.
        public bool FindsInHiding(HidingPlace place, Vector3 observer, float reach, bool seesWholeInside)
        {
            var player = PlayerPresence.Current;
            if (player == null || place == null || !place.Usable) return false;
            if (HidingPlace.At(player, out float coverage) != place || !place.Conceals(player, coverage)) return false;
            if (seesWholeInside) return place.DistanceTo(observer) <= reach;
            // Under a rack: from the rack's edge where she kneels (her body stands a little back
            // from it), along under the boards to you.
            Vector3 edge = place.transform.TransformPoint(place.interior.ClosestPoint(place.transform.InverseTransformPoint(observer)));
            Vector3 you = player.transform.position - edge;
            you.y = 0f;
            return you.magnitude <= reach;
        }

        public float Awareness { get; private set; }
        public float DetectNow { get; private set; }
        public float Exposure { get; private set; }
        public bool SeesNow => DetectNow > scoreFloor;
        public Vector3 LastSeenPosition { get; private set; }
        public Vector3 LastSeenVelocity { get; private set; }
        public float LastSeenTime { get; private set; } = -999f;

        // Whether the employee is looking back at this eye — used by mimicry (a possessed
        // shopper turns to face you when you look at it) and by the blink channel.
        public bool IsWatched { get; private set; }

        public Vector3 Eye => transform.position;
        public Vector3 Forward => transform.forward;
        public float HalfAngle => fieldOfView * 0.5f;

        // How far this eye can see a point right now: its range, or less in fog.
        public float ReachTo(Vector3 point) => FogState.Active ? Mathf.Min(range, FogState.SightLimit(Eye, point)) : range;

        static readonly RaycastHit[] hits = new RaycastHit[16];
        Vector3 previousSeen;
        float previousSeenTime;
        float unseenFor;

        public void ResetAwareness()
        {
            Awareness = 0f;
            DetectNow = 0f;
            unseenFor = 0f;
            HiddenEntry.Clear();
            hasLastVisible = false;
            wasConcealed = false;
            wasConcealedIn = null;
            LastSeenTime = -999f;
            LastSeenVelocity = Vector3.zero;
        }

        // A looped feed may still draw and score its ordinary view, but cannot supply the
        // approach for a newly witnessed entry when its live picture comes back.
        internal void ForgetVisibleApproach() => hasLastVisible = false;

        public void Tick(float dt)
        {
            PlayerPresence player = PlayerPresence.Current;
            float coverage = 0f;
            HidingPlace place = player != null ? HidingPlace.At(player, out coverage) : null;
            bool concealed = place != null && place.Conceals(player, coverage);
            float now = Time.time;
            HiddenEntry.UpdateOccupancy(place, concealed, now);
            // A witnessed entry: on the step into concealment (or into another place), the eye
            // saw you beside that place a moment ago (WitnessedHide.Witnessed). One sighting
            // witnesses one entry, so an unseen return can't re-arm the catch. A clear eye ray
            // into the hiding place now is not evidence: concealed, the score is 0.
            HidingPlace previousPlace = wasConcealedIn;
            if (concealed && (!wasConcealed || place != wasConcealedIn) && hasLastVisible &&
                WitnessedHide.Witnessed(place, lastVisiblePosition, lastVisibleAt, player.Position, now))
            {
                HiddenEntry.Remember(place, lastVisiblePosition, now);
                SawHidingBoundary?.Invoke(lastVisiblePosition);
                hasLastVisible = false;
            }
            wasConcealed = concealed;
            wasConcealedIn = concealed ? place : null;

            float score = player != null && !concealed ? Score(player) : 0f;
            if (concealed || player == null) { Exposure = 0f; IsWatched = false; }
            if (score > scoreFloor)
            {
                if (previousPlace != null && previousPlace.DistanceTo(player.Position) <= WitnessedHide.EntryReach)
                    SawHidingBoundary?.Invoke(player.Position);
                hasLastVisible = true;
                lastVisiblePosition = player.Position;
                lastVisibleAt = now;
                // Seeing you again supersedes the hiding-place search.
                if (HiddenEntry.Active) HiddenEntry.Clear();
            }
            float expected = score > scoreFloor && Expectation != null ? Expectation(player.Position) : 0f;
            Integrate(score, dt, expected);

            if (SeesNow)
            {
                Vector3 seen = player.Position;
                if (Time.time - previousSeenTime < 1f && Time.time > previousSeenTime)
                    LastSeenVelocity = (seen - previousSeen) / Mathf.Max(0.02f, Time.time - previousSeenTime);
                previousSeen = seen;
                previousSeenTime = Time.time;

                LastSeenPosition = seen;
                LastSeenTime = Time.time;
            }
        }

        // Stunned (0.5.2, C9.10, the owner's item 4): she sees nothing for the stun's length. Her
        // awareness can only drain, no sighting is remembered, and no entry into a hiding place
        // can be witnessed. The one check kept is the player's protection: leaving the hiding
        // place she saw you enter still takes her catch away (WitnessedHide.UpdateOccupancy),
        // which can never tell her anything.
        public void Blind(float dt)
        {
            PlayerPresence player = PlayerPresence.Current;
            float coverage = 0f;
            HidingPlace place = player != null ? HidingPlace.At(player, out coverage) : null;
            bool concealed = place != null && place.Conceals(player, coverage);
            HiddenEntry.UpdateOccupancy(place, concealed, Time.time);
            wasConcealed = concealed;
            wasConcealedIn = concealed ? place : null;
            hasLastVisible = false;
            Exposure = 0f;
            IsWatched = false;
            Integrate(0f, dt, 0f);
        }

        // One step of the meter, from a score and how much her belief expected you where it
        // came from (0–1). Above the floor it fills, faster where she expected you; below it,
        // it holds for the grace and then drains. Pure, so the walk-by set can drive it.
        public void Integrate(float score, float dt, float expected)
        {
            DetectNow = score;
            if (SeesNow)
            {
                unseenFor = 0f;
                float boost = 1f + expectedBoost * Mathf.Clamp01(expected);
                Awareness = Mathf.Min(1f, Awareness + score * gain * boost * dt);
                return;
            }
            unseenFor += dt;
            if (unseenFor > grace) Awareness = Mathf.Max(0f, Awareness - decay * dt);
        }

        float Score(PlayerPresence player)
        {
            Vector3 toPlayer = player.Chest - Eye;
            float distance = toPlayer.magnitude;
            IsWatched = false;
            Exposure = 0f;
            if (distance > ReachTo(player.Chest) || distance < 0.01f) return 0f;

            float angle = Vector3.Angle(Forward, toPlayer);
            bool close = distance < CloseRange;
            if (angle > HalfAngle && !close) return 0f;

            // Eye to eye (0.5.1): ordinary low cover never slows a clear eye sighting.
            Exposure = ExposureTo(player.Head, player.Chest, player.Feet, player);
            if (Exposure <= 0f) return 0f;

            // Are they looking this way? A face turned toward you reads instantly.
            Vector3 back = Eye - player.Head;
            IsWatched = Vector3.Angle(player.Facing, back) < 30f;

            return Detect(angle, distance, player.LightLevel, player.MotionSalience, Exposure);
        }

        // Clear eyes over ordinary low cover give full sight (0.5.1, item 1). 0.4.1 gave half for
        // the eyes alone and a quarter more each for the chest and feet, so a head over a short
        // shelf was noticed at half speed: that was the 0.5.0 playtest fault. Concealment is the
        // authored interiors' job (HidingPlace), checked before this ray. The chest and feet are
        // kept in the signature for the callers; they no longer count.
        public static float ExposureOf(bool eyes, bool chest, bool feet) =>
            eyes ? 1f : 0f;

        // The same, cast: this eye's line to someone's eyes through whatever stands in the scene
        // (only the eye line counts since 0.5.1). `player` is whose body the line may pass
        // through (null in a test).
        public float ExposureTo(Vector3 eyes, Vector3 chest, Vector3 feet, PlayerPresence player = null)
        {
            if (!Clear(player, eyes)) return 0f;
            return 1f;
        }

        // The score for someone `angle` degrees off this eye's forward and `distance` metres
        // away, lit 0–1, moving with a salience of 0–1, with 0–1 of them showing past cover.
        // No rays: Score casts those. Pure, so the walk-by set times it without a scene.
        public float Detect(float angle, float distance, float light, float salience, float exposure)
        {
            if (distance > range || exposure <= 0f) return 0f;
            bool close = distance < CloseRange;
            if (angle > HalfAngle && !close) return 0f;

            float score = (close ? Falloff(distance) * DarkVision(light) : Visibility(angle, distance, light))
                * (0.3f + 0.7f * salience) * exposure;

            // At arm's length nobody is hidden by standing still in the dark.
            if (close) score = Mathf.Max(score, 0.9f * exposure);
            return Mathf.Clamp01(score);
        }

        // The eye's part of the score: how well it sees a spot that far off-centre, that far
        // away, that lit. The "nothing here" sweep uses it too (Sensorium.CollectSweep).
        public float Visibility(float angle, float distance, float light)
        {
            if (angle > HalfAngle || distance > range) return 0f;
            float t = Mathf.Clamp01(angle / HalfAngle);
            return (1f - t * t) * Falloff(distance) * DarkVision(light);
        }

        public float Falloff(float distance) => 1f / (1f + (distance / halfStrengthAt) * (distance / halfStrengthAt));
        float DarkVision(float light) => Mathf.Lerp(darkVision, 1f, light);

        bool Clear(PlayerPresence player, Vector3 target)
        {
            Vector3 d = target - Eye;
            float distance = d.magnitude;
            if (distance < 0.01f) return true;

            int count = Physics.RaycastNonAlloc(Eye, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (player != null && player.Owns(c)) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;   // loose stock
                return false;
            }
            return true;
        }

        // An illuminated surface can give away a light even when its holder is concealed.
        // The ray ends at the first solid surface. Looking at the other side of a cupboard
        // door cannot reveal a beam that struck the inside.
        public bool TryHiddenTorchGlow(out Vector3 spot)
        {
            spot = default;
            PlayerPresence player = PlayerPresence.Current;
            Flashlight torch = player != null ? player.HeldTorch : null;
            if (torch == null || !torch.IsOn || torch.beam == null ||
                !torch.beam.isActiveAndEnabled || torch.beam.intensity <= 0f) return false;
            HidingPlace place = HidingPlace.At(player, out float coverage);
            if (place == null || !place.Conceals(player, coverage)) return false;
            Transform beam = torch.beam.transform;
            if (!Physics.Raycast(beam.position, beam.forward, out RaycastHit hit,
                                 torch.beam.range, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (player.Owns(hit.collider) || Vector3.Dot(Eye - hit.point, hit.normal) <= 0f)
                return false;
            spot = hit.point + hit.normal * 0.015f;
            return CanSee(spot);
        }

        // Line of sight from this eye to an arbitrary point — for sweeping cells as empty.
        public bool CanSee(Vector3 point, Transform ignore = null)
        {
            Vector3 d = point - Eye;
            float distance = d.magnitude;
            if (distance > range) return false;
            if (Vector3.Angle(Forward, d) > HalfAngle) return false;
            if (FogState.Active && distance > FogState.SightLimit(Eye, point)) return false;
            if (distance < 0.01f) return true;

            int count = Physics.RaycastNonAlloc(Eye, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                if (ignore != null && c.transform.IsChildOf(ignore)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (c is CharacterController) continue;
                return false;
            }
            return true;
        }

        public string Band
        {
            get
            {
                if (Awareness >= confirmAt) return "confirmed";
                if (Awareness >= seeAt) return "seen";
                if (Awareness >= glimpseAt) return "glimpse";
                return "none";
            }
        }
    }
}
