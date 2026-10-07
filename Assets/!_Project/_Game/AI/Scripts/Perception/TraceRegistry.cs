using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Aiko
{
    // AIKO.md §3.3.
    public enum TraceKind
    {
        WetFootprint,       // walked through a spill — has a heading
        ShelfGap,           // the employee took something off a shelf
        UndoneSabotage,     // a shelf Aiko stripped has been filled back up
        DroppedItem,        // stock left on the floor
        MopAway,            // the mop is lying somewhere that isn't its rack
        BaggedBin,          // a bin was just emptied
        DoorOpened          // a door was opened in a hurry
    }

    public sealed class Trace
    {
        public int Id;
        public TraceKind Kind;
        public Vector3 Position;
        public Vector3 Heading;
        public float Created;
        public float Lifetime;          // seconds; +infinity = until resolved
        public bool Resolved;
        public Transform Follow;        // a dropped item can be kicked about
        public Object Source;

        public float Age => Time.time - Created;
        public bool Expired => Resolved || Age > Lifetime;

        // Fading traces carry less as they age: footprints are sharp for a few seconds
        // and ghosts after forty.
        public float Freshness => float.IsInfinity(Lifetime) ? 1f : Mathf.Clamp01(1f - Age / Lifetime);
    }

    // Physical evidence with a decay clock. The store remembers you even when nothing is
    // watching: Aiko walks into an aisle, finds a bagged bin and wet footprints heading
    // north, and has a hard prior on half the building.
    public static class TraceRegistry
    {
        static readonly List<Trace> traces = new List<Trace>();
        static int nextId = 1;

        public static IReadOnlyList<Trace> All => traces;
        public static event System.Action<Trace> Added;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            traces.Clear();
            nextId = 1;
            Added = null;
        }

        public static Trace Add(TraceKind kind, Vector3 position, float lifetime,
                                Vector3 heading = default, Object source = null, Transform follow = null)
        {
            var trace = new Trace
            {
                Id = nextId++,
                Kind = kind,
                Position = position,
                Heading = heading,
                Created = Time.time,
                Lifetime = lifetime,
                Source = source,
                Follow = follow
            };
            traces.Add(trace);
            Added?.Invoke(trace);
            return trace;
        }

        // Clears every live trace of a kind that came from `source` — the dropped item
        // picked back up, the gap refilled.
        public static void Resolve(TraceKind kind, Object source)
        {
            for (int i = 0; i < traces.Count; i++)
                if (traces[i].Kind == kind && traces[i].Source == source) traces[i].Resolved = true;
        }

        public static void ResolveNear(TraceKind kind, Vector3 position, float radius)
        {
            float sqr = radius * radius;
            for (int i = 0; i < traces.Count; i++)
                if (traces[i].Kind == kind && (traces[i].Position - position).sqrMagnitude <= sqr)
                    traces[i].Resolved = true;
        }

        public static void Prune()
        {
            for (int i = traces.Count - 1; i >= 0; i--)
            {
                Trace t = traces[i];
                if (t.Follow != null) t.Position = t.Follow.position;
                if (t.Expired || (t.Follow == null && t.Source == null && t.Kind == TraceKind.DroppedItem))
                    traces.RemoveAt(i);
            }
        }

        public static void Clear() => traces.Clear();

        // Wire the gameplay events that leave evidence behind. Footprints are laid by the
        // footprint system itself, since they need the player's movement.
        public static void Listen()
        {
            GameEvents.PlayerDroppedItem -= OnDropped;
            GameEvents.PlayerDroppedItem += OnDropped;
            GameEvents.PlayerPickedUp -= OnPickedUp;
            GameEvents.PlayerPickedUp += OnPickedUp;
            GameEvents.PlayerTookFromShelf -= OnTook;
            GameEvents.PlayerTookFromShelf += OnTook;
            GameEvents.PlayerShelvedItem -= OnShelved;
            GameEvents.PlayerShelvedItem += OnShelved;
            GameEvents.BinBagged -= OnBagged;
            GameEvents.BinBagged += OnBagged;
            GameEvents.DoorUsed -= OnDoor;
            GameEvents.DoorUsed += OnDoor;
        }

        static void OnDropped(Item item, Vector3 at)
        {
            if (item == null) return;
            Add(TraceKind.DroppedItem, at, float.PositiveInfinity, default, item, item.transform);
        }

        static void OnPickedUp(Item item) => Resolve(TraceKind.DroppedItem, item);

        static void OnTook(ShelfSlot slot, Item item)
        {
            if (slot != null) Add(TraceKind.ShelfGap, slot.transform.position, float.PositiveInfinity, default, slot);
        }

        static void OnShelved(ShelfSlot slot, Item item)
        {
            if (slot != null) Resolve(TraceKind.ShelfGap, slot);
        }

        static void OnBagged(Trashcan can)
        {
            if (can != null) Add(TraceKind.BaggedBin, can.transform.position, 240f, default, can);
        }

        static void OnDoor(Component door, bool opened)
        {
            if (door != null && opened) Add(TraceKind.DoorOpened, door.transform.position, 8f, default, door);
        }
    }
}
