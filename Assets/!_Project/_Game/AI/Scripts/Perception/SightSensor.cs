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
    // 0.4.1: one rule for cover, eye to eye. An eye sees you only along a clear line from it to
    // your eyes (your camera). A low thing on the floor between you — a short shelf, a box —
    // doesn't hide you if she can look over it into your eyes; and with that line blocked — you're
    // under the stockroom racks, the boards between her eyes and yours — she doesn't see you,
    // however clear the floor is. Until now it was three rays (feet, chest, head) and any one
    // would do, so feet showing under a rack gave you away. How much of the rest of you shows
    // still counts, but only for how fast she notices (ExposureOf).
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
        }

        public void Tick(float dt)
        {
            PlayerPresence player = PlayerPresence.Current;
            float score = player != null ? Score(player) : 0f;
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

            // Eye to eye first (0.4.1): no clear line to your eyes, nothing seen. Then how much
            // of the rest of you shows, for how fast she notices.
            Exposure = ExposureTo(player.Head, player.Chest, player.Feet, player);
            if (Exposure <= 0f) return 0f;

            // Are they looking this way? A face turned toward you reads instantly.
            Vector3 back = Eye - player.Head;
            IsWatched = Vector3.Angle(player.Facing, back) < 30f;

            return Detect(angle, distance, player.LightLevel, player.MotionSalience, Exposure);
        }

        // How much of you an eye has, 0 to 1, from which of its three lines are clear: to your
        // eyes, your chest, your feet. Without the line to your eyes it has nothing (0.4.1); with
        // it, half, and a quarter more each for the chest and the feet. So a head over a short
        // shelf is seen, at half speed, and feet under a rack are not.
        public static float ExposureOf(bool eyes, bool chest, bool feet) =>
            !eyes ? 0f : 0.5f + (chest ? 0.25f : 0f) + (feet ? 0.25f : 0f);

        // The same, cast: this eye's lines to someone's eyes, chest and feet, through whatever
        // stands in the scene. `player` is whose body the lines may pass through (null in a test).
        public float ExposureTo(Vector3 eyes, Vector3 chest, Vector3 feet, PlayerPresence player = null)
        {
            if (!Clear(player, eyes)) return 0f;
            return ExposureOf(true, Clear(player, chest), Clear(player, feet));
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
