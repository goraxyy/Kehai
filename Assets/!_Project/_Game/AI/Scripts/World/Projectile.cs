using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What Karen can throw (0.2.0, Throw), and what each does if it hits.
    //
    // The rule is that an item alone never makes a catch certain: after its effect you must
    // still be at least SafeGap metres ahead of her if you sprint straight on. At her 7.5 m/s
    // against your 6.5, the closest she may throw from is gap + (7.5 − your speed during the
    // effect) × its length — which is how each item's MinRange was set:
    //
    //   flour  blurs you 4 s, no slower:     4 + (7.5 − 6.5) × 4     = 8 m
    //   oil    25% slower for 2 s:           4 + (7.5 − 4.875) × 2   = 9.25 → 10 m
    //   a tin  stuns you 1 s:                4 + 7.5 × 1              = 11.5 → 15 m (as the plan set it)
    public enum ThrownItem { Flour, Oil, Tin }

    public static class ThrownItems
    {
        public const float SafeGap = 4f;

        public static float MinRange(ThrownItem item) => item == ThrownItem.Flour ? 8f : item == ThrownItem.Oil ? 10f : 15f;
        public static float MaxRange(ThrownItem item) => item == ThrownItem.Tin ? 20f : 15f;
        public static float Seconds(ThrownItem item) => item == ThrownItem.Flour ? 4f : item == ThrownItem.Oil ? 2f : 1f;
        public static float Slow(ThrownItem item) => item == ThrownItem.Oil ? 0.25f : item == ThrownItem.Tin ? 1f : 0f;

        public static void Apply(ThrownItem item, PlayerEffects on)
        {
            switch (item)
            {
                case ThrownItem.Flour: on.Blur(Seconds(item)); break;
                case ThrownItem.Oil: on.Slow(Slow(item), Seconds(item)); break;
                case ThrownItem.Tin: on.Stun(Seconds(item)); break;
            }
        }

        // How far ahead of her you'd be once the effect is over, sprinting straight on from `range`.
        public static float GapAfter(ThrownItem item, float range, float herSpeed, float yourSprint) =>
            range - (herSpeed - yourSprint * (1f - Slow(item))) * Seconds(item);
    }

    public struct ThrowEvent
    {
        public ThrownItem Item;
        public Vector3 From, Aim, At;     // At: where it came down
        public float Flight;              // seconds it was meant to fly
        public bool Hit;                  // it hit you
        public Projectile Projectile;     // follow its transform in flight
    }

    // For the second builder's visuals (ThrownItemVisual): a throw leaving her hand, and landing.
    public static class ThrowEvents
    {
        public static event System.Action<ThrowEvent> Thrown, Landed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Thrown = null; Landed = null; }

        internal static void RaiseThrown(ThrowEvent e) => Thrown?.Invoke(e);
        internal static void RaiseLanded(ThrowEvent e) => Landed?.Invoke(e);
    }

    // A thrown item in flight: an arc under gravity, stopped by the first solid thing in its way.
    // It finds you only by hitting you — a body's collider, as her touch does — never by knowing
    // where you are. Walls block it, and stepping aside makes it miss.
    public sealed class Projectile : MonoBehaviour, IMapTransient
    {
        const float Radius = 0.15f;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        ThrowEvent e;
        Vector3 velocity;
        Transform thrower;
        float born;

        public static Projectile Launch(ThrownItem item, Vector3 from, Vector3 aim, float flight, Transform thrower)
        {
            var go = new GameObject("KAREN_Thrown_" + item);
            go.transform.position = from;
            var p = go.AddComponent<Projectile>();
            p.thrower = thrower;
            p.born = Time.time;
            flight = Mathf.Max(0.2f, flight);
            // The arc that lands on `aim` after `flight` seconds.
            p.velocity = (aim - from) / flight - 0.5f * Physics.gravity * flight;
            p.e = new ThrowEvent { Item = item, From = from, Aim = aim, Flight = flight, Projectile = p };
            ThrowEvents.RaiseThrown(p.e);
            return p;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            velocity += Physics.gravity * dt;
            Vector3 step = velocity * dt;
            float distance = step.magnitude;

            if (distance > 1e-5f)
            {
                int n = Physics.SphereCastNonAlloc(from, Radius, step / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                Collider hitCollider = null;
                Vector3 point = from + step;
                for (int i = 0; i < n; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null || thrower != null && col.transform.IsChildOf(thrower)) continue;
                    if (hits[i].distance < nearest) { nearest = hits[i].distance; hitCollider = col; point = hits[i].point; }
                }
                if (hitCollider != null)
                {
                    PlayerEffects body = hitCollider is CharacterController ? hitCollider.GetComponentInParent<PlayerEffects>() : null;
                    if (body != null) ThrownItems.Apply(e.Item, body);
                    Land(point, body != null);
                    return;
                }
            }
            transform.position = from + step;
            if (Time.time - born > 4f || transform.position.y < -10f) Land(transform.position, false);
        }

        void Land(Vector3 at, bool hit)
        {
            e.At = at;
            e.Hit = hit;
            ThrowEvents.RaiseLanded(e);
            Destroy(gameObject);
        }
    }
}
