using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // Testimony (Karen.md §3.4). Every shopper keeps a tiny memory of the last time it saw
    // the employee. Karen can't read minds: her body has to walk up to a customer to "ask",
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

        public bool HasSpokenToKaren { get; private set; }
        public void ClearConversation() => HasSpokenToKaren = false;

        public bool HasSighting => lastSeenTime > -900f && Time.time - lastSeenTime < memorySeconds;
        public bool SawHidingEntry { get; private set; }
        bool lastLookWasVisible;
        public Vector3 LastSeenAt { get; private set; }
        public float LastSeenTime => lastSeenTime;
        public float Age => Time.time - lastSeenTime;
        public float SightingConfidence { get; private set; }

        // Somebody near a scare is a jumpy witness: they notice more and remember harder.
        public bool IsJumpy => Time.time < jumpyUntil;

        // Set by Karen when she takes this shopper over (mimicry); its reports are then
        // near certain, and it stops shopping.
        [System.NonSerialized] public bool possessed;

        float lastSeenTime = -999f;
        const float LookInterval = 0.5f;
        float nextLook;
        float jumpyUntil = -1f;
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

        void Update() => TickObservation();

        internal void TickObservation()
        {
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + LookInterval;
                Look();
            }
        }

        internal void ResetObservation()
        {
            nextLook = Time.time;
            lastSeenTime = -999f;
            lastLookWasVisible = SawHidingEntry = HasSpokenToKaren = false;
            LastSeenAt = Vector3.zero;
            SightingConfidence = 0f;
        }

        void Look()
        {
            PlayerPresence player = PlayerPresence.Current;
            bool sawPreviousLook = lastLookWasVisible;
            lastLookWasVisible = false;
            if (player == null) return;
            // Hidden in a fridge, closet, stall or under a rack: shoppers can't see you either,
            // even with a clear line in (0.5.1, item 1; the same rule as every SightSensor).
            HidingPlace place = HidingPlace.At(player, out float coverage);
            if (place != null && place.Conceals(player, coverage))
            {
                if (sawPreviousLook && WitnessedHide.Witnessed(place, LastSeenAt, lastSeenTime, player.Position, Time.time, LookInterval + 0.1f))
                {
                    // The location stays at the last visible sample. Her conversation with
                    // this witness carries a fading lead, never the right to catch at it.
                    SawHidingEntry = true;
                    lastSeenTime = Time.time;
                }
                return;
            }

            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 d = player.Chest - eye;
            float distance = d.magnitude;
            float range = IsJumpy ? sightRange * 1.4f : sightRange;
            range = Mathf.Min(range, FogState.SightLimit(eye, player.Chest));   // fog blinds shoppers too (0.3.0)
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

            lastLookWasVisible = true;
            SawHidingEntry = false;
            lastSeenTime = Time.time;
            LastSeenAt = player.Position;
            float confidence = 1f - distance / range * 0.6f;
            if (IsJumpy) confidence = Mathf.Min(1f, confidence + 0.15f);
            if (possessed) confidence = 0.95f;
            SightingConfidence = confidence;
        }

        public void MakeJumpy(float seconds) => jumpyUntil = Mathf.Max(jumpyUntil, Time.time + seconds);

        // A witness report fades with age: sure of the aisle a moment ago, vague a minute later.
        public Observation ToObservation(SenseModel senses = null)
        {
            HasSpokenToKaren = true; // a report was actually taken at talking distance
            float decayed = SightingConfidence * Mathf.Clamp01(1f - Age / memorySeconds);
            float sigma = (senses ?? SenseModel.Hand()).TestimonySigma(Age);
            return new Observation(SenseChannel.Testimony, LastSeenAt, decayed, sigma,
                                   $"testimony({name}, {(SawHidingEntry ? "saw entry into hiding, " : "")}{Age:0}s old)");
        }

        public static void MakeNearbyJumpy(Vector3 position, float radius, float seconds)
        {
            float sqr = radius * radius;
            foreach (CustomerMemory m in all)
                if ((m.transform.position - position).sqrMagnitude <= sqr) m.MakeJumpy(seconds);
        }
    }
}
