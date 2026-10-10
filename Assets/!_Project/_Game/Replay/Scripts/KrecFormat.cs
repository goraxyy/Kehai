using System.IO;
using UnityEngine;

namespace Kehai.Replay
{
    // A .krec file is a shift recorded for replay: one gzip stream of tagged records, written
    // as the shift happens and read back in order. Snapshots, not a re-simulation: everything
    // that moves is sampled 30 times a second (the player's view 60 times), positions in
    // millimetres as deltas from the entity's last sample, rotations as three 16-bit numbers,
    // and only what changed. Karen's belief map is added twice a second, on a 3 m grid.
    //
    //   header   magic, version, the shift, the scene, Karen's seed and rung, the maze's moves,
    //            the belief grid, and the store as it was: shelf units, shelf slots, lights
    //   Tick     t                                    starts a 30 Hz sample
    //   Spawn    id, kind, key, label, pose, state, visible
    //   Pose     id, what changed: position delta, rotation, state, visible
    //   Despawn  id
    //   Camera   t, what changed: position delta, rotation, FOV, eyelids, what's in hand
    //   Event    t, type, payload (a sound, a tell, the PA, the lights, a thought, a shelf slot…)
    //   Belief   t, her guess, how sure, the grid as bytes XOR'd with the previous one
    //   End      length, clocked out
    public enum KrecKind : byte
    {
        Player = 0, Karen = 1, Customer = 2, Understudy = 3, Item = 4, Door = 5, AutoDoorPanel = 6,
        ShelfUnit = 7, CrateWall = 8, Fog = 9, Cctv = 10, Coffee = 11, Footprint = 12, Spill = 13,
        Bin = 14, Bag = 15
    }

    public enum KrecTag : byte { Tick = 1, Spawn = 2, Despawn = 3, Pose = 4, Camera = 5, Event = 6, Belief = 7, End = 255 }

    public enum KrecEvent : byte
    {
        Noise = 1,      // kind, author, position, loudness
        Tell = 2,       // tell kind, position, lead
        PaChime = 3,    // text
        PaSpeech = 4,   // text
        Circuits = 5,   // bits: mains, three breakers, PA jammed
        Thought = 6,    // thought-log kind, text
        Story = 7,      // narrator kind, text
        Slot = 8,       // slot index, filled, product id
        Lights = 9,     // changed ceiling lights: index, on
    }

    // Bits of a Pose or Spawn's state for the kinds that have one.
    public static class KrecState
    {
        // Player: motion (0 still, 1 crouching, 2 walking, 3 sprinting) | 4 carrying | 8 holding a tool
        public const int PlayerCarrying = 4, PlayerHoldingTool = 8;
        // Karen: mood (0 calm, 1 alert, 2 hunt, 3 kind) | 4 sees you | 8 chasing
        public const int KarenSees = 4, KarenChasing = 8;
        // Item (tools are items): 0 loose, 1 in the player's hand, 2 held by someone else (Karen
        // with the mop), 3 on a shelf | 8 switched on (the torch)
        public const int ItemLoose = 0, ItemInHand = 1, ItemHeld = 2, ItemOnShelf = 3, ItemLit = 8;
        // Door: 1 locked. Bin: how full. Bag: 1 disposed. Cctv: 1 bolted on | 2 dead.
        // CrateWall: crates across. Fog: radius in decimetres. Coffee: 1 the last one.
    }

    public static class Krec
    {
        public const string Magic = "KREC";
        public const ushort Version = 1;
        public const float TickRate = 30f;
        public const float CameraRate = 60f;
        public const float BeliefRate = 2f;
        public const float PositionScale = 1000f;     // millimetres
        public const float BeliefCell = 3f;           // metres; two of the map's 1.5 m cells
        public const float DiscoverySeconds = 0.5f;   // how often to look for new things in the store
        public const float SlotSeconds = 0.1f;        // how often to check the shelf slots

        public static int Q(float metres) => Mathf.RoundToInt(metres * PositionScale);
        public static float Metres(int q) => q / PositionScale;

        public static void WritePosition(BinaryWriter w, Vector3Int q)
        {
            w.Write(q.x);
            w.Write(q.y);
            w.Write(q.z);
        }

        public static Vector3Int ReadPosition(BinaryReader r) => new Vector3Int(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());

        public static Vector3Int Quantise(Vector3 p) => new Vector3Int(Q(p.x), Q(p.y), Q(p.z));
        public static Vector3 Position(Vector3Int q) => new Vector3(Metres(q.x), Metres(q.y), Metres(q.z));

        // ---- rotations: the three smallest components of the quaternion, 16 bits each ----------

        const float Root2Over2 = 0.70710678f;

        public struct Rotation : System.IEquatable<Rotation>
        {
            public byte Largest;
            public short A, B, C;
            public bool Equals(Rotation o) => Largest == o.Largest && A == o.A && B == o.B && C == o.C;
        }

        public static Rotation Compress(Quaternion q)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float n = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (n < 1e-6f) { x = y = z = 0f; w = 1f; n = 1f; }
            x /= n; y /= n; z /= n; w /= n;

            int largest = 0;
            float max = Mathf.Abs(x);
            if (Mathf.Abs(y) > max) { largest = 1; max = Mathf.Abs(y); }
            if (Mathf.Abs(z) > max) { largest = 2; max = Mathf.Abs(z); }
            if (Mathf.Abs(w) > max) largest = 3;

            float a, b, c, big;
            switch (largest)
            {
                case 0: big = x; a = y; b = z; c = w; break;
                case 1: big = y; a = x; b = z; c = w; break;
                case 2: big = z; a = x; b = y; c = w; break;
                default: big = w; a = x; b = y; c = z; break;
            }
            float sign = big < 0f ? -1f : 1f;
            return new Rotation { Largest = (byte)largest, A = Small(a * sign), B = Small(b * sign), C = Small(c * sign) };
        }

        static short Small(float v) => (short)Mathf.Clamp(Mathf.RoundToInt(v / Root2Over2 * 32767f), -32767, 32767);

        public static Quaternion Expand(Rotation r)
        {
            float a = r.A / 32767f * Root2Over2, b = r.B / 32767f * Root2Over2, c = r.C / 32767f * Root2Over2;
            float big = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b - c * c));
            switch (r.Largest)
            {
                case 0: return new Quaternion(big, a, b, c);
                case 1: return new Quaternion(a, big, b, c);
                case 2: return new Quaternion(a, b, big, c);
                default: return new Quaternion(a, b, c, big);
            }
        }

        public static void WriteRotation(BinaryWriter w, Rotation r)
        {
            w.Write(r.Largest);
            w.Write(r.A);
            w.Write(r.B);
            w.Write(r.C);
        }

        public static Rotation ReadRotation(BinaryReader r) =>
            new Rotation { Largest = r.ReadByte(), A = r.ReadInt16(), B = r.ReadInt16(), C = r.ReadInt16() };

        // ---- variable-length integers (zigzag, so small negatives stay small) ------------------

        public static void WriteVarint(BinaryWriter w, int value)
        {
            uint v = (uint)((value << 1) ^ (value >> 31));
            while (v >= 0x80)
            {
                w.Write((byte)(v | 0x80));
                v >>= 7;
            }
            w.Write((byte)v);
        }

        public static int ReadVarint(BinaryReader r)
        {
            uint v = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                v |= (uint)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
                if (shift > 35) throw new KrecFormatException("varint too long");
            }
            return (int)(v >> 1) ^ -(int)(v & 1);
        }
    }
}
