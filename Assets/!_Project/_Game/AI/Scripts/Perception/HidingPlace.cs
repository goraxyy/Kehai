using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // A door on a hiding place that Karen opens to look inside (0.5.2: the fridge and locker
    // checks, HideCheck). A locker's or cabinet's door (FurnitureDoor), a sandbox's fridge door.
    public interface IHidingDoor
    {
        bool IsHidingDoor { get; }   // false for a room door wrapped as furniture
        bool IsOpen { get; }
        Vector3 DoorPoint { get; }   // the middle of the closed leaf
        float LeafWidth { get; }     // how far the leaf sweeps out from its hinge
        float OpenFraction { get; }  // 0 shut, 1 fully open
        void SetOpenForCheck(bool open);
    }

    // A code-authored empty interior, not the bounds of the solid furniture. Coverage is
    // the fraction of the current controller's body envelope inside it. It is independent
    // of the observer: an open cabinet or a ray under a rack does not defeat concealment.
    //
    // A sense: SightSensor asks it where the player is hiding. The memory of a seen entry is
    // WitnessedHide, in its own file, which never reads the player.
    [DisallowMultipleComponent]
    public sealed class HidingPlace : MonoBehaviour
    {
        public const float CoveredThreshold = 0.8f;
        public Bounds interior;
        public bool requiresCrouch;
        static readonly List<HidingPlace> places = new List<HidingPlace>();

        // Registered on enable and when authored: in edit mode (tests, builders) OnEnable never
        // runs, so authoring registers too. Disabled or destroyed entries are skipped.
        void OnEnable() => Register(this);
        void OnDisable() => places.Remove(this);
        static void Register(HidingPlace place) { if (!places.Contains(place)) places.Add(place); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => places.Clear();

        // Not isActiveAndEnabled: that stays false in edit mode for a script that doesn't run there.
        public bool Usable => this != null && enabled && gameObject.activeInHierarchy;

        // Every registered hiding place (the store's furniture, which she knows; not who's in it).
        public static IReadOnlyList<HidingPlace> All
        {
            get { places.RemoveAll(p => p == null); return places; }
        }

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
            places.RemoveAll(p => p == null);
            foreach (var place in places)
            {
                if (!place.Usable) continue;
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
            if (!owner.TryGetComponent(out HidingPlace place)) place = owner.AddComponent<HidingPlace>();
            place.interior = interior;
            place.requiresCrouch = crouch;
            Register(place);
            return place;
        }
    }
}
