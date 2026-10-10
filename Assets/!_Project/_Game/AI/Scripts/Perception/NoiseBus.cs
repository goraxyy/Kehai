using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    public enum NoiseKind
    {
        Footstep,
        CrouchStep,
        Sprint,
        DroppedItem,
        Impact,
        KickedItem,
        Mopping,
        Stocking,
        TrashRustle,
        Coffee,
        AutoDoor,
        Door,
        TimeClock,
        Serve,
        CrateClearing,
        Unplugging,
        KarenStep,
        KarenVoice,
        Tell,
        Environmental
    }

    // Who made it. Karen ignores her own noises and the Director's, the way a person
    // doesn't startle at their own footsteps — but the player hears all of them.
    public enum NoiseAuthor { Player, Customer, Karen, Director, World }

    public struct NoiseEvent
    {
        public long Id;
        public Vector3 Position;
        public float Loudness;      // metres of nominal carry, divided by NoiseBus.CarryPerUnit
        public NoiseKind Kind;
        public NoiseAuthor Author;
        public float Time;

        public float Carry => Loudness * NoiseBus.CarryPerUnit;
    }

    // Everything noisy in the store announces itself here. One static ring buffer, no
    // per-frame scanning: listeners keep a cursor and read what's new since they last looked.
    //
    // Loudness follows Karen.md §3.2 — sprint 0.9, walk 0.35, crouch 0.1 and so on — and
    // one unit carries CarryPerUnit metres *along the floor*: the listener measures the
    // walking distance to the source, not the straight line, so a noise two aisles over
    // around a corner is quieter than one straight down the aisle.
    public static class NoiseBus
    {
        public const float CarryPerUnit = 30f;
        const int Capacity = 512;

        static readonly NoiseEvent[] ring = new NoiseEvent[Capacity];
        static long next;

        // Raised as each noise is made — for anything that must react *now* (Karen
        // re-deciding on a crash) rather than on its next scheduled tick.
        public static event System.Action<NoiseEvent> Emitted;

        public static long Latest => next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            next = 0;
            Emitted = null;
        }

        public static void Emit(Vector3 position, float loudness, NoiseKind kind, NoiseAuthor author)
        {
            if (loudness <= 0f) return;

            var e = new NoiseEvent
            {
                Id = next,
                Position = position,
                Loudness = loudness,
                Kind = kind,
                Author = author,
                Time = UnityEngine.Time.time
            };
            ring[next % Capacity] = e;
            next++;
            Emitted?.Invoke(e);
        }

        // Copies every event after `cursor` into `into` and moves the cursor on. A listener
        // that fell more than a buffer behind simply misses the oldest — the same thing that
        // happens to anyone not paying attention.
        public static int ReadSince(ref long cursor, List<NoiseEvent> into)
        {
            into.Clear();
            if (cursor < next - Capacity) cursor = next - Capacity;
            for (long i = cursor; i < next; i++) into.Add(ring[i % Capacity]);
            cursor = next;
            return into.Count;
        }
    }
}
