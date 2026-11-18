using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // A hit stops the body and brain, never adds knowledge about whoever threw the item.
    // One item can hit once per release; a burst of hits has one bounded stun window.
    public sealed class KarenStun : MonoBehaviour
    {
        public const float MinimumSpeed = 1.5f;
        public const float MaximumDuration = 3f;
        public const float RecoveryGrace = .5f;
        readonly Dictionary<Item, int> releases = new Dictionary<Item, int>();
        KarenBody body;
        float remaining, episodeRemaining, recovery;
        int impactFrame = -1;
        public bool IsStunned => remaining > 0f;
        public float Remaining => remaining;
        public bool HitThisFrame => impactFrame == Time.frameCount;

        void Awake() => body = GetComponent<KarenBody>();

        public static float DurationFor(float closingSpeed, float mass)
        {
            if (!Finite(closingSpeed) || !Finite(mass) || closingSpeed < MinimumSpeed || mass <= 0f) return 0f;
            // Closing speed is m/s; mass is kg. This authored score is speed * sqrt(kg),
            // bounded to 20 m/s and .05–5 kg. A score below 2 is a harmless graze.
            float strength = Mathf.Clamp(closingSpeed, 0f, 20f) * Mathf.Sqrt(Mathf.Clamp(mass, .05f, 5f));
            return strength < 2f ? 0f : Mathf.Clamp(.25f + (strength - 2f) * .18f, .25f, MaximumDuration);
        }

        public bool TryImpact(Item item, float closingSpeed, float itemSpeed)
        {
            if (!isActiveAndEnabled || GamePause.Paused || item == null || !item.isActiveAndEnabled
                || item.isCarried || item.isOnShelf) return false;
            Rigidbody rigid = item.GetComponent<Rigidbody>();
            if (rigid == null || rigid.isKinematic) return false;
            if (releases.TryGetValue(item, out int release) && release == item.ReleaseVersion) return false;
            // Any touch uses up this release, a stunning one or not: clutter she walks into, a
            // graze, or a hit during the grace period can't come back off a wall or keep
            // bouncing against her and stun her later. Only picking it up again re-arms it.
            releases[item] = item.ReleaseVersion;
            if (item.lastAuthor == NoiseAuthor.Karen || !Finite(itemSpeed) || itemSpeed < MinimumSpeed) return false;
            float duration = DurationFor(closingSpeed, rigid.mass);
            if (duration <= 0f) return false;
            if (recovery > 0f) return false;
            if (!IsStunned) episodeRemaining = MaximumDuration;
            remaining = Mathf.Min(episodeRemaining, Mathf.Max(remaining, duration));
            impactFrame = Time.frameCount;
            body?.InterruptForStun();
            return true;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        void OnCollisionEnter(Collision collision)
        {
            Item item = collision.collider.GetComponentInParent<Item>();
            if (item == null) return;
            float closing = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                closing = Mathf.Max(closing, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            // Pre-solver velocity is captured by Item.FixedUpdate, before a bounce slows it down.
            TryImpact(item, closing, item.PhysicsSpeed);
        }

        void Update() => Step(Time.deltaTime);
        public void Step(float dt)
        {
            if (GamePause.Paused || !Finite(dt) || dt <= 0f) return;
            if (!IsStunned) { recovery = Mathf.Max(0f, recovery - dt); return; }
            remaining = Mathf.Max(0f, remaining - dt);
            episodeRemaining = Mathf.Max(0f, episodeRemaining - dt);
            if (!IsStunned) recovery = RecoveryGrace;
        }

        public void ResetState()
        {
            remaining = episodeRemaining = recovery = 0f;
            impactFrame = -1;
            releases.Clear();
        }

        void OnDisable() => ResetState();
    }
}
