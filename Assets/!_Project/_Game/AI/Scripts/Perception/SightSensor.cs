using UnityEngine;

namespace Kehai.Aiko
{
    // Graded sight (AIKO.md §3.1). Not "can I see the player" but a detection score that
    // builds while you're visible and drains while you aren't, so a glimpse through a gap
    // between two bays doesn't become a chase:
    //
    //     detect = angular · distance · light · motion · exposure
    //
    // The same component serves Aiko's own eyes, the CCTV cameras (lower gain, narrower
    // cone), and the possessed customers — each is just an eye with different numbers.
    public class SightSensor : MonoBehaviour
    {
        [Header("Cone")]
        public float range = 18f;
        [Range(10f, 180f)] public float fieldOfView = 120f;

        [Header("Integration")]
        [Tooltip("Awareness gained per second at a detection score of 1.")]
        public float gain = 2.2f;
        [Tooltip("Awareness lost per second while nothing is seen.")]
        public float decay = 0.45f;
        [Tooltip("How well it sees in the dark, as a floor under the light term. " + GameNames.Antagonist + "'s " +
                 "eyes are not a person's; darkness hides you less than you'd like.")]
        [Range(0f, 1f)] public float darkVision = 0.35f;

        [Header("Thresholds")]
        public float glimpseAt = 0.2f;
        public float seeAt = 0.5f;
        public float confirmAt = 0.85f;

        // Colliders on the owner that must never block its own view.
        [System.NonSerialized] public Transform ignoreRoot;

        public float Awareness { get; private set; }
        public float DetectNow { get; private set; }
        public float Exposure { get; private set; }
        public bool SeesNow => DetectNow > 0.02f;
        public Vector3 LastSeenPosition { get; private set; }
        public Vector3 LastSeenVelocity { get; private set; }
        public float LastSeenTime { get; private set; } = -999f;

        // Whether the employee is looking back at this eye — used by mimicry (a possessed
        // shopper turns to face you when you look at it) and by the blink channel.
        public bool IsWatched { get; private set; }

        public Vector3 Eye => transform.position;
        public Vector3 Forward => transform.forward;
        public float HalfAngle => fieldOfView * 0.5f;

        static readonly RaycastHit[] hits = new RaycastHit[8];
        Vector3 previousSeen;
        float previousSeenTime;

        public void ResetAwareness()
        {
            Awareness = 0f;
            DetectNow = 0f;
        }

        public void Tick(float dt)
        {
            PlayerPresence player = PlayerPresence.Current;
            DetectNow = player != null ? Score(player) : 0f;

            if (DetectNow > 0.02f)
            {
                Awareness = Mathf.Min(1f, Awareness + DetectNow * gain * dt);

                Vector3 seen = player.Position;
                if (Time.time - previousSeenTime < 1f && Time.time > previousSeenTime)
                    LastSeenVelocity = (seen - previousSeen) / Mathf.Max(0.02f, Time.time - previousSeenTime);
                previousSeen = seen;
                previousSeenTime = Time.time;

                LastSeenPosition = seen;
                LastSeenTime = Time.time;
            }
            else
            {
                Awareness = Mathf.Max(0f, Awareness - decay * dt);
            }
        }

        float Score(PlayerPresence player)
        {
            Vector3 toPlayer = player.Chest - Eye;
            float distance = toPlayer.magnitude;
            IsWatched = false;
            Exposure = 0f;
            if (distance > range || distance < 0.01f) return 0f;

            float angle = Vector3.Angle(Forward, toPlayer);
            bool close = distance < 2.2f;
            if (angle > HalfAngle && !close) return 0f;

            // Three rays, not one — a shoulder showing past a shelf end is still a shoulder.
            int visible = 0;
            if (Clear(player, player.Feet)) visible++;
            if (Clear(player, player.Chest)) visible++;
            if (Clear(player, player.Head)) visible++;
            Exposure = visible / 3f;
            if (visible == 0) return 0f;

            float t = Mathf.Clamp01(angle / HalfAngle);
            float angular = close ? 1f : 1f - t * t;
            float distanceFalloff = 1f / (1f + (distance / 8f) * (distance / 8f));
            float light = Mathf.Lerp(darkVision, 1f, player.LightLevel);
            float motion = 0.3f + 0.7f * player.MotionSalience;

            // Are they looking this way? A face turned toward you reads instantly.
            Vector3 back = Eye - player.Head;
            IsWatched = Vector3.Angle(player.Facing, back) < 30f;

            float score = angular * distanceFalloff * light * motion * Exposure;

            // At arm's length nobody is hidden by standing still in the dark.
            if (close) score = Mathf.Max(score, 0.9f * Exposure);
            return Mathf.Clamp01(score);
        }

        bool Clear(PlayerPresence player, Vector3 target)
        {
            Vector3 d = target - Eye;
            float distance = d.magnitude;
            if (distance < 0.01f) return true;

            int count = Physics.RaycastNonAlloc(Eye, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (player.Owns(c)) continue;
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
