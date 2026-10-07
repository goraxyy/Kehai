using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Aiko
{
    // Testimony (AIKO.md §3.4). Every shopper keeps a tiny memory of the last time it saw
    // the employee. Aiko can't read minds: her body has to walk up to a customer to "ask",
    // which costs her time and puts her in the open. A busy store is a dense sensor grid;
    // standing at the till puts you in front of the most reliable witness in the building.
    [DisallowMultipleComponent]
    public class CustomerMemory : MonoBehaviour
    {
        static readonly List<CustomerMemory> all = new List<CustomerMemory>();
        public static IReadOnlyList<CustomerMemory> All => all;

        [Tooltip("How far a shopper notices the employee.")]
        public float sightRange = 14f;
        [Range(30f, 220f)] public float fieldOfView = 150f;
        [Tooltip("Seconds a sighting stays worth reporting.")]
        public float memorySeconds = 75f;

        public bool HasSighting => lastSeenTime > -900f && Time.time - lastSeenTime < memorySeconds;
        public Vector3 LastSeenAt { get; private set; }
        public float LastSeenTime => lastSeenTime;
        public float Age => Time.time - lastSeenTime;
        public float SightingConfidence { get; private set; }

        // Somebody near a scare is a jumpy witness: they notice more and remember harder.
        public bool IsJumpy => Time.time < jumpyUntil;

        // Set by Aiko when she takes this shopper over (mimicry); its reports are then
        // near certain, and it stops shopping.
        [System.NonSerialized] public bool possessed;

        float lastSeenTime = -999f;
        float nextLook;
        float jumpyUntil = -1f;
        Vector3 lookTarget;
        float lookUntil = -1f;
        CustomerNPC npc;

        static readonly RaycastHit[] hits = new RaycastHit[8];

        void Awake()
        {
            npc = GetComponent<CustomerNPC>();
            // Stagger the looks so forty shoppers don't all raycast on the same frame.
            nextLook = Time.time + Random.Range(0f, 0.5f);
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Update()
        {
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 0.5f;
                Look();
            }

            // The PA announced the employee's position: everyone turns to stare.
            if (Time.time < lookUntil && npc != null)
                npc.FaceTowards(lookTarget);
        }

        void Look()
        {
            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return;

            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 d = player.Chest - eye;
            float distance = d.magnitude;
            float range = IsJumpy ? sightRange * 1.4f : sightRange;
            if (distance > range) return;
            if (Vector3.Angle(transform.forward, d) > fieldOfView * 0.5f && distance > 2f) return;

            int count = Physics.RaycastNonAlloc(eye, d / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (player.Owns(c) || c.transform.IsChildOf(transform)) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                return;   // something solid in the way
            }

            lastSeenTime = Time.time;
            LastSeenAt = player.Position;
            float confidence = 1f - distance / range * 0.6f;
            if (IsJumpy) confidence = Mathf.Min(1f, confidence + 0.15f);
            if (possessed) confidence = 0.95f;
            SightingConfidence = confidence;
        }

        public void MakeJumpy(float seconds) => jumpyUntil = Mathf.Max(jumpyUntil, Time.time + seconds);

        public void StareAt(Vector3 point, float seconds)
        {
            lookTarget = point;
            lookUntil = Time.time + seconds;
        }

        // A witness report fades with age: sure of the aisle a moment ago, vague a minute later.
        public Observation ToObservation()
        {
            float decayed = SightingConfidence * Mathf.Clamp01(1f - Age / memorySeconds);
            float sigma = 2.5f + Age * 0.25f;
            return new Observation(SenseChannel.Testimony, LastSeenAt, decayed, sigma,
                                   $"testimony({name}, {Age:0}s old)");
        }

        public static void MakeNearbyJumpy(Vector3 position, float radius, float seconds)
        {
            float sqr = radius * radius;
            foreach (CustomerMemory m in all)
                if ((m.transform.position - position).sqrMagnitude <= sqr) m.MakeJumpy(seconds);
        }
    }
}
