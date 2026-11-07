using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Fog (0.3.0 build plan C1.6): which rooms Karen has flooded from their fridges, and how far
    // anyone can see in them. It blinds everyone the same — her eye, her cameras, Kenji, and you
    // — so every sight check asks SightLimit, and the screen's fog (FogVisuals) reads Visibility.
    //
    // A "room" is a StoreMap area (StoreMap.AreaAt): the sales floor, cold storage, the staff
    // room. The fog rolls
    // in over `spreadSeconds` (sight closes from open to the visibility) and thins over
    // `thinSeconds` once someone resets the fridge control box.
    public static class FogState
    {
        public const float Open = 60f;            // what "no fog" means for a sight limit, in metres
        public const float SpreadSeconds = 8f;
        public const float ThinSeconds = 3f;

        sealed class Room
        {
            public float visibility;               // metres, once it's thick
            public float startedAt, clearedAt = -1f;
            public float spread, thin;
        }

        static readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();

        // Which room a point is in. The map's areas, unless a test says otherwise.
        public static System.Func<Vector3, string> RoomOf = DefaultRoomOf;
        // The clock it runs on. The game's, unless a test says otherwise.
        public static System.Func<float> Clock = () => Time.time;

        public static event System.Action<string> Filled;
        public static event System.Action<string> Cleared;

        // The fridge control box in the backstreet, where you reset it: set by the box itself
        // (FogBox). Fog's chore is fixed there (Tactic.FixAt); Ambush waits on your way to it.
        public static Transform ResetBox;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            rooms.Clear();
            ResetBox = null;
            Filled = null;
            Cleared = null;
            RoomOf = DefaultRoomOf;
            Clock = () => Time.time;
        }

        // StoreMap's own table, so a doorway belongs to the room it opens on to.
        static string DefaultRoomOf(Vector3 p) => StoreMap.AreaAt(p);

        // Anything at all to check: false keeps every sight check as cheap as before fog existed.
        public static bool Active => rooms.Count > 0;

        // A room fogged and not yet reset, rolling in or thick (a reset one still thinning isn't).
        public static bool Any
        {
            get
            {
                foreach (KeyValuePair<string, Room> pair in rooms) if (pair.Value.clearedAt < 0f) return true;
                return false;
            }
        }

        public static IEnumerable<string> Rooms
        {
            get { foreach (KeyValuePair<string, Room> pair in rooms) if (pair.Value.clearedAt < 0f) yield return pair.Key; }
        }

        // Flood a room. Refilling one that's already fogged only changes its thickness.
        public static void Fill(string room, float visibility, float spreadSeconds = SpreadSeconds)
        {
            if (string.IsNullOrEmpty(room)) return;
            float now = Clock();
            bool fresh = !rooms.TryGetValue(room, out Room r) || r.clearedAt >= 0f;
            if (fresh)
            {
                r = new Room { startedAt = now, spread = Mathf.Max(0.01f, spreadSeconds) };
                rooms[room] = r;
            }
            r.visibility = Mathf.Max(0.5f, visibility);
            if (fresh) Filled?.Invoke(room);
        }

        // Thin it out: one room, or every room (null).
        public static void Clear(string room = null, float thinSeconds = ThinSeconds)
        {
            float now = Clock();
            var cleared = new List<string>();
            foreach (KeyValuePair<string, Room> pair in rooms)
            {
                if (room != null && pair.Key != room) continue;
                if (pair.Value.clearedAt >= 0f) continue;
                pair.Value.clearedAt = now;
                pair.Value.thin = Mathf.Max(0.01f, thinSeconds);
                cleared.Add(pair.Key);
            }
            foreach (string r in cleared) Cleared?.Invoke(r);
        }

        // Every room clear at once, with nothing to watch: the shift's end.
        public static void Reset()
        {
            var had = new List<string>(rooms.Keys);
            rooms.Clear();
            foreach (string r in had) Cleared?.Invoke(r);
        }

        // How thick it is once it's in: the visibility in metres, or 0 for a clear room.
        public static float VisibilityOf(string room) =>
            room != null && rooms.TryGetValue(room, out Room r) && r.clearedAt < 0f ? r.visibility : 0f;

        // When a room filled (the clock's time), or -1.
        public static float FilledAt(string room) =>
            room != null && rooms.TryGetValue(room, out Room r) ? r.startedAt : -1f;

        // Fogged: filled and not yet reset (it may still be thinning or rolling in).
        public static bool IsFogged(string room) =>
            room != null && rooms.TryGetValue(room, out Room r) && r.clearedAt < 0f;

        // 0 = clear, 1 = as thick as it gets.
        public static float Thickness(string room) =>
            room != null && rooms.TryGetValue(room, out Room r) ? Thick(r) : 0f;

        static float Thick(Room r)
        {
            float now = Clock();
            float rolling = Mathf.Clamp01((now - r.startedAt) / r.spread);
            if (r.clearedAt < 0f) return rolling;
            float thinning = 1f - Mathf.Clamp01((now - r.clearedAt) / r.thin);
            return Mathf.Min(rolling, thinning);
        }

        // How far you can see from a point: the room's visibility, eased in and out with the fog.
        public static float VisibilityAt(Vector3 p)
        {
            if (rooms.Count == 0) return Open;
            string room = RoomOf(p);
            if (room == null || !rooms.TryGetValue(room, out Room r)) return Open;
            float thick = Thick(r);
            if (thick <= 0f)
            {
                if (r.clearedAt >= 0f) rooms.Remove(room);   // fully thinned: forget it
                return Open;
            }
            return Mathf.Lerp(Open, r.visibility, thick);
        }

        // The furthest an eye at `eye` can see something at `target`: fog at either end limits it.
        public static float SightLimit(Vector3 eye, Vector3 target) =>
            rooms.Count == 0 ? Open : Mathf.Min(VisibilityAt(eye), VisibilityAt(target));
    }
}
