using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Where you might be going (0.4.0, build plan §8.3, C4.3): about a dozen places at a time, all
    // things she knows. Built only from her own knowledge: the jobs the fairness guard lists (she
    // wrote the list), her own chores (Tactic.FixAt), the store's landmarks, her exits, your hiding
    // spots from your Ledger or everyone's, and "staying here" and "turned back".
    public enum DestinationKind { Job, Queue, Chore, Breakers, Coffee, Clock, Exit, Hide, Stay, TurnBack }

    public struct Destination
    {
        public DestinationKind Kind;
        public string Name;          // "a spill", "fog", "Stockroom/C", "out by Pavement/A"
        public Vector3 Position;
        public int Cell;             // -1 for Stay and TurnBack (no place of their own)
        public float Waited;         // seconds she has seen it waiting (jobs and chores)
        public string AnswerKind;    // her chore's answer in the Ledger ("fog_reset"), for P3

        public string Label => Kind == DestinationKind.Job || Kind == DestinationKind.Chore ? Kind + ":" + Name : Kind.ToString();
    }

    public sealed class Destinations
    {
        public const int MaxPlaces = 12;

        // A chore's answer in the Ledger, by the trick that left it (KarenBrain.Answered's kinds).
        static readonly Dictionary<string, string> answerOf = new Dictionary<string, string>
        {
            ["fog"] = "fog_reset", ["overtime"] = "clock_restarted", ["understudy"] = "understudy_sent_away",
            ["camera"] = "camera_unplugged", ["block_door"] = "box_moved", ["tool_theft"] = "mop_returned"
        };

        readonly Dictionary<string, float> firstSeen = new Dictionary<string, float>();
        public readonly List<Destination> All = new List<Destination>();

        public void BeginShift() => firstSeen.Clear();

        // The list, now. `from` is where her belief puts you (its peak): of each kind of job, the
        // nearest one to it is kept.
        public List<Destination> Build(KarenContext c, Vector3 from, float now)
        {
            All.Clear();
            StoreMap map = c.Map;
            if (map == null) return All;

            var nearest = new Dictionary<string, Destination>();
            foreach (KeyValuePair<string, Vector3> t in FairnessGuard.RequiredTargets(map))
            {
                DestinationKind kind = t.Key == "the time clock" ? DestinationKind.Clock
                    : t.Key.StartsWith("Checkout") || t.Key.StartsWith("checkout") ? DestinationKind.Queue
                    : t.Key.EndsWith("'s rack") ? DestinationKind.Chore : DestinationKind.Job;
                Keep(nearest, Place(map, kind, t.Key, t.Value, now), from);
            }
            if (c.Brain != null || c.Ledger != null)
                foreach (Tactic t in TacticLibrary.Live)
                    if (t.LeavesChore && SafeRunning(t, c) && t.FixAt(c, out Vector3 at))
                    {
                        Destination d = Place(map, DestinationKind.Chore, t.Id, at, now);
                        d.AnswerKind = answerOf.TryGetValue(t.Id, out string a) ? a : t.Id;
                        Keep(nearest, d, from);
                    }
            if (!PowerSystem.PowerOn)
            {
                Landmark? box = map.FindLandmark(LandmarkKind.BreakerBox);
                if (box.HasValue) Keep(nearest, Place(map, DestinationKind.Breakers, "the breaker box", box.Value.Position, now), from);
            }
            Landmark? coffee = map.FindLandmark(LandmarkKind.CoffeeMachine);
            if (coffee.HasValue) Keep(nearest, Place(map, DestinationKind.Coffee, "the coffee machine", coffee.Value.Position, now), from);
            if (c.Belief != null)
                for (int k = 0; k < c.Belief.ExitCount; k++)
                {
                    Vector3 p = c.Belief.ExitPosition(k);
                    Keep(nearest, Place(map, DestinationKind.Exit, "out by " + map.NameAt(p), p, now), from, key: "exit" + k);
                }
            foreach (string region in HidingSpots(c).Take(3))
            {
                Region r = map.Regions.FirstOrDefault(x => x.Name == region || x.Key == region);
                if (r != null) Keep(nearest, Place(map, DestinationKind.Hide, r.Name, r.Centroid, now), from, key: "hide:" + r.Name);
            }

            // The places, nearest first, at most MaxPlaces − 2; then staying, and turning back.
            All.AddRange(nearest.Values.OrderBy(d => (d.Position - from).sqrMagnitude).ThenBy(d => d.Name, System.StringComparer.Ordinal).Take(MaxPlaces - 2));
            All.Add(new Destination { Kind = DestinationKind.Stay, Name = "staying here", Cell = -1 });
            All.Add(new Destination { Kind = DestinationKind.TurnBack, Name = "turned back", Cell = -1 });
            return All;
        }

        // Your hiding spots: yours from the Ledger first, then everyone's.
        public static IEnumerable<string> HidingSpots(KarenContext c)
        {
            if (c.Ledger != null && c.Map != null)
                foreach (int r in c.Ledger.ConcealmentRegions(c, 2)) yield return c.Map.Regions[r].Name;
            foreach (KeyValuePair<string, float> h in LedgerStart.Current.Hides) yield return h.Key;
        }

        static bool SafeRunning(Tactic t, KarenContext c)
        {
            try { return t.EffectRunning(c); }
            catch (System.NullReferenceException) { return false; }   // a context with no world (tests)
        }

        Destination Place(StoreMap map, DestinationKind kind, string name, Vector3 at, float now)
        {
            string key = kind + ":" + name + ":" + Mathf.RoundToInt(at.x) + "," + Mathf.RoundToInt(at.z);
            if (!firstSeen.TryGetValue(key, out float seen)) firstSeen[key] = seen = now;
            return new Destination { Kind = kind, Name = name, Position = at, Cell = map.CellAt(at), Waited = now - seen };
        }

        static void Keep(Dictionary<string, Destination> nearest, Destination d, Vector3 from, string key = null)
        {
            if (d.Cell < 0) return;
            key = key ?? d.Kind + ":" + d.Name;
            if (!nearest.TryGetValue(key, out Destination have) || (d.Position - from).sqrMagnitude < (have.Position - from).sqrMagnitude)
                nearest[key] = d;
        }
    }
}
