using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // A code-authored empty interior, not the bounds of the solid furniture. Coverage is
    // the fraction of the current controller's body envelope inside it. It is independent
    // of the observer: an open cabinet or a ray under a rack does not defeat concealment.
    [DisallowMultipleComponent]
    public sealed class HidingPlace : MonoBehaviour
    {
        public const float CoveredThreshold = 0.8f;
        public Bounds interior;
        public bool requiresCrouch;
        static readonly List<HidingPlace> places = new List<HidingPlace>();
        void OnEnable() { if (!places.Contains(this)) places.Add(this); }
        void OnDisable() { places.Remove(this); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => places.Clear();

        public static bool Conceals(float coverage) => coverage >= CoveredThreshold;
        public static float OverlapFraction(Bounds body, Bounds space)
        {
            Vector3 overlap = Vector3.Max(Vector3.zero, Vector3.Min(body.max, space.max) - Vector3.Max(body.min, space.min));
            Vector3 size = body.size;
            float volume = size.x * size.y * size.z;
            return volume > 0f ? Mathf.Clamp01(overlap.x * overlap.y * overlap.z / volume) : 0f;
        }

        public bool Conceals(PlayerPresence player, float coverage) =>
            (!requiresCrouch || player.IsCrouching) && Conceals(coverage);

        public float Coverage(PlayerPresence player)
        {
            return OverlapFraction(player.BodyEnvelopeIn(transform), interior);
        }

        public float DistanceTo(Vector3 worldPoint) =>
            Vector3.Distance(worldPoint, transform.TransformPoint(interior.ClosestPoint(transform.InverseTransformPoint(worldPoint))));

        public static HidingPlace At(PlayerPresence player, out float coverage)
        {
            coverage = 0f;
            HidingPlace best = null;
            foreach (var place in places)
            {
                if (place == null || !place.isActiveAndEnabled) continue;
                float value = place.Coverage(player);
                bool conceals = place.Conceals(player, value);
                bool bestConceals = best != null && best.Conceals(player, coverage);
                if (bestConceals && !conceals || conceals == bestConceals && value <= coverage) continue;
                coverage = value;
                best = place;
            }
            return best;
        }

        public static HidingPlace Author(GameObject owner, Bounds interior, bool crouch = false)
        {
            var place = owner.GetComponent<HidingPlace>() ?? owner.AddComponent<HidingPlace>();
            place.interior = interior;
            place.requiresCrouch = crouch;
            return place;
        }
    }

    // Only perceptual evidence starts this memory. Occupancy can invalidate a catch, never
    // start one or update the destination. Escape keeps the fixed search destination but
    // permanently revokes the catch, including if the player returns unseen.
    public sealed class WitnessedHide
    {
        public const float CatchDistance = 3f;
        public const float Lifetime = 60f;
        public bool Active { get; private set; }
        public Vector3 Position { get; private set; }
        public HidingPlace Place { get; private set; }
        public bool CatchEligible { get; private set; }
        float expires;
        public void Remember(HidingPlace place, Vector3 observedPosition, float now)
        {
            Place = place; Position = observedPosition; expires = now + Lifetime;
            Active = CatchEligible = true;
        }
        public void UpdateOccupancy(HidingPlace occupied, bool concealed, float now)
        {
            if (!Active) return;
            if (Place == null || !Place.isActiveAndEnabled || now >= expires) { Clear(); return; }
            if (occupied != Place || !concealed) CatchEligible = false;
        }
        public bool CanCatch(Vector3 observer, Vector3 occupant) =>
            Active && CatchEligible && Vector3.Distance(observer, occupant) <= CatchDistance;
        public void Clear() { Active = CatchEligible = false; Place = null; }
    }
}
