using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // What a shopper is doing, as one symbol on the map.
    public enum CustomerMark : byte
    {
        Shopping, UsingBin, HeadingToTill, Queueing, Leaving, LookingAround,
        Asking, Talking, Following, LostTheGuide, Possessed, Fake
    }

    public struct PersonState
    {
        public int Id;
        public Vector2 At;
        public float Yaw;
        public CustomerMark State;
        public float Wait;      // seconds at the till, when queueing
        public int Bay;         // the shelf they're looking for, when asking or following; -1 otherwise
    }

    public enum PropKind : byte { Crates, Fog, Camera, Coffee }

    // Everything on the map at one moment.
    public sealed class ShiftFrame
    {
        public float T;                     // seconds into the shift
        public Vector2 Player;
        public float PlayerYaw;
        public byte PlayerMotion;           // MotionState
        public bool PlayerHeld;             // frozen by a lecture
        public float Energy;

        public bool KarenPresent;
        public Vector2 Karen;
        public float KarenYaw;
        public byte KarenMood;              // KarenBody.Mood
        public bool KarenSees;
        public bool Chasing;
        public Vector2 Guess;               // where she thinks you are
        public float GuessConfidence;

        public readonly List<PersonState> Customers = new List<PersonState>();
        public readonly List<Vector2> Spills = new List<Vector2>();
        public readonly List<int> EmptyBays = new List<int>();
        public readonly List<Vector4> Bins = new List<Vector4>();       // x, z, fill, capacity
        public readonly List<Vector2> Bags = new List<Vector2>();
        public bool Power = true;
        public readonly List<Vector2> LockedDoors = new List<Vector2>();
        public readonly List<Vector4> Props = new List<Vector4>();      // kind, x, z, size
    }

    // Something that happened: a sound, a line of the story, a finished job.
    public struct ShiftEvent
    {
        public float T;                 // seconds into the shift
        public float WallTime;          // Time.time, for the live map's fading rings
        public string Kind;             // "sound", "job", "customer", "store", or a StoryKind name
        public Vector2 At;
        public bool HasPlace;
        public string Who;              // "you", "Karen", "a customer", "the store"
        public float Radius;            // for sounds: how far it carried, metres
        public string Text;
    }

    public sealed class ShiftRecording
    {
        public int ShiftNumber;
        public string PlayerName = "";
        public string KarenRung = "";
        public string StartedAt = "";   // wall-clock date and time
        public float Length;
        public bool ClockedOut;
        public readonly List<ShiftFrame> Frames = new List<ShiftFrame>();
        public readonly List<ShiftEvent> Events = new List<ShiftEvent>();
        public readonly Dictionary<int, string> CustomerWants = new Dictionary<int, string>();

        // Moments worth a clip: markers as they happen, the chases, and at the end the moments.
        public readonly List<ClipMarker> Markers = new List<ClipMarker>();
        public readonly List<Vector2> ChaseSpans = new List<Vector2>();     // start, end
        public List<ClipMoment> Moments;

        public ShiftFrame At(float t)
        {
            if (Frames.Count == 0) return null;
            int lo = 0, hi = Frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Frames[mid].T <= t) lo = mid; else hi = mid - 1;
            }
            return Frames[lo];
        }

        // ---- capture --------------------------------------------------------------------

        public static void Capture(ShiftFrame f, float t, BurnoutSystem burnout, System.Func<CustomerNPC, int> idOf, System.Func<CustomerNPC, int> bayOf)
        {
            f.T = t;
            f.Customers.Clear(); f.Spills.Clear(); f.EmptyBays.Clear(); f.Bins.Clear(); f.Bags.Clear();
            f.LockedDoors.Clear(); f.Props.Clear();

            PlayerPresence me = PlayerPresence.Current;
            if (me != null)
            {
                f.Player = StoreFloorPlan.Flat(me.Position);
                Vector3 look = me.Facing;
                f.PlayerYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                f.PlayerMotion = (byte)me.Motion;
            }
            f.PlayerHeld = Consequences.LectureRunning;
            f.Energy = burnout != null ? burnout.Energy01 : 1f;

            KarenBrain brain = KarenBrain.Instance;
            f.KarenPresent = brain != null && brain.Body != null;
            if (f.KarenPresent)
            {
                f.Karen = StoreFloorPlan.Flat(brain.Body.Position);
                Vector3 fwd = brain.Body.Forward;
                f.KarenYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                f.KarenMood = (byte)brain.Body.CurrentMood;
                f.KarenSees = brain.Body.Sight.Awareness >= brain.Body.Sight.seeAt;
                f.Chasing = brain.IsChasing;
                if (brain.Belief != null)
                {
                    f.Guess = StoreFloorPlan.Flat(brain.Belief.PeakPosition);
                    f.GuessConfidence = brain.Belief.Confidence;
                }
            }

            foreach (CustomerNPC c in CustomerNPC.All)
            {
                if (c == null) continue;
                var p = new PersonState { Id = idOf(c), At = StoreFloorPlan.Flat(c.transform.position), Yaw = c.transform.eulerAngles.y, Bay = -1 };
                p.State = MarkOf(c);
                if (p.State == CustomerMark.Queueing && c.QueueingSince >= 0f) p.Wait = Time.time - c.QueueingSince;
                if (p.State == CustomerMark.Asking || p.State == CustomerMark.Talking || p.State == CustomerMark.Following || p.State == CustomerMark.LostTheGuide)
                    p.Bay = bayOf(c);
                f.Customers.Add(p);
            }
            foreach (Understudy u in Object.FindObjectsByType<Understudy>())
                f.Customers.Add(new PersonState { Id = -1 - (u.GetHashCode() & 0xffff), At = StoreFloorPlan.Flat(u.transform.position), Yaw = u.transform.eulerAngles.y, State = CustomerMark.Fake, Bay = -1 });

            foreach (Dirt d in Dirt.All) if (d != null) f.Spills.Add(StoreFloorPlan.Flat(d.transform.position));

            StoreMap map = StoreMap.Current;
            for (int i = 0; i < map.Bays.Count; i++)
                if (map.Bays[i].Unit != null && !map.Bays[i].Unit.IsFull) f.EmptyBays.Add(i);

            foreach (Trashcan bin in Trashcan.All)
                if (bin != null) f.Bins.Add(new Vector4(bin.transform.position.x, bin.transform.position.z, bin.UsageCount, bin.capacity));
            foreach (TrashBag bag in Object.FindObjectsByType<TrashBag>())
                if (bag != null && !bag.IsDisposed) f.Bags.Add(StoreFloorPlan.Flat(bag.transform.position));

            f.Power = PowerSystem.PowerOn;
            foreach (HingeDoor door in HingeDoor.All)
                if (door != null && door.Locked) f.LockedDoors.Add(StoreFloorPlan.Flat(door.DoorCentre));

            foreach (CrateWall w in Object.FindObjectsByType<CrateWall>())
                f.Props.Add(new Vector4((float)PropKind.Crates, w.transform.position.x, w.transform.position.z, 2f));
            foreach (FogCloud fog in Object.FindObjectsByType<FogCloud>())
                f.Props.Add(new Vector4((float)PropKind.Fog, fog.transform.position.x, fog.transform.position.z, 4f));
            foreach (CctvCamera cam in CctvCamera.All)
                if (cam != null && cam.BoltedOn && !cam.Dead) f.Props.Add(new Vector4((float)PropKind.Camera, cam.transform.position.x, cam.transform.position.z, 1f));
            foreach (CoffeeCup cup in Object.FindObjectsByType<CoffeeCup>())
                f.Props.Add(new Vector4((float)PropKind.Coffee, cup.transform.position.x, cup.transform.position.z, 0.5f));
        }

        public static CustomerMark MarkOf(CustomerNPC c)
        {
            var possessed = c.GetComponent<Possession>();
            if (possessed != null && possessed.Active) return CustomerMark.Possessed;

            var req = c.GetComponent<CustomerRequest>();
            if (req != null)
            {
                switch (req.CurrentStage)
                {
                    case CustomerRequest.Stage.Asking: return CustomerMark.Asking;
                    case CustomerRequest.Stage.Talking: return CustomerMark.Talking;
                    case CustomerRequest.Stage.Escorting: return CustomerMark.Following;
                    case CustomerRequest.Stage.Returning: return CustomerMark.LostTheGuide;
                }
            }
            switch (c.CurrentActivity)
            {
                case CustomerNPC.Activity.UsingBin: return CustomerMark.UsingBin;
                case CustomerNPC.Activity.HeadingToTill: return CustomerMark.HeadingToTill;
                case CustomerNPC.Activity.Queueing: return CustomerMark.Queueing;
                case CustomerNPC.Activity.Leaving: return CustomerMark.Leaving;
                case CustomerNPC.Activity.LookingAround: return CustomerMark.LookingAround;
                default: return CustomerMark.Shopping;
            }
        }

        public static string Words(CustomerMark m)
        {
            switch (m)
            {
                case CustomerMark.Shopping: return "shopping";
                case CustomerMark.UsingBin: return "using a bin";
                case CustomerMark.HeadingToTill: return "heading to the till";
                case CustomerMark.Queueing: return "waiting at the till";
                case CustomerMark.Leaving: return "leaving";
                case CustomerMark.LookingAround: return "looking around (sent by " + GameNames.Antagonist + ")";
                case CustomerMark.Asking: return "needs directions";
                case CustomerMark.Talking: return "talking to you";
                case CustomerMark.Following: return "following you to a shelf";
                case CustomerMark.LostTheGuide: return "lost you — waiting";
                case CustomerMark.Possessed: return "taken over by " + GameNames.Antagonist;
                case CustomerMark.Fake: return "not a real customer";
                default: return m.ToString();
            }
        }

        // ---- JSON ------------------------------------------------------------------------

        public string ToJson(StoreFloorPlan plan, ShiftAnalysis analysis)
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"shift\":").Append(ShiftNumber)
              .Append(",\"player\":\"").Append(MiniJson.EscapeInner(PlayerName))
              .Append("\",\"rung\":\"").Append(MiniJson.EscapeInner(KarenRung))
              .Append("\",\"started\":\"").Append(MiniJson.EscapeInner(StartedAt))
              .Append("\",\"length\":").Append(N(Length))
              .Append(",\"clockedOut\":").Append(ClockedOut ? "true" : "false");
            if (plan != null) sb.Append(",\"plan\":").Append(plan.ToJson());
            if (analysis != null) sb.Append(",\"analysis\":").Append(analysis.ToJson());

            sb.Append(",\"wants\":{");
            bool first = true;
            foreach (var pair in CustomerWants)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(pair.Key).Append("\":\"").Append(MiniJson.EscapeInner(pair.Value)).Append('"');
            }
            sb.Append('}');

            // Frames as arrays of numbers, to keep a ten-minute shift to a few hundred KB:
            // [t, px, pz, pyaw, motion, held, energy, karen?, kx, kz, kyaw, mood, sees, chase, gx, gz, gconf, power,
            //  customers[[id,x,z,yaw,state,wait,bay]], spills[[x,z]], emptyBays[i], bins[[x,z,fill,cap]], bags[[x,z]], locked[[x,z]], props[[kind,x,z,size]]]
            sb.Append(",\"frames\":[");
            for (int i = 0; i < Frames.Count; i++)
            {
                if (i > 0) sb.Append(',');
                ShiftFrame f = Frames[i];
                sb.Append('[').Append(N(f.T)).Append(',').Append(N(f.Player.x)).Append(',').Append(N(f.Player.y)).Append(',').Append(Mathf.RoundToInt(f.PlayerYaw))
                  .Append(',').Append(f.PlayerMotion).Append(',').Append(f.PlayerHeld ? 1 : 0).Append(',').Append(N(f.Energy))
                  .Append(',').Append(f.KarenPresent ? 1 : 0).Append(',').Append(N(f.Karen.x)).Append(',').Append(N(f.Karen.y)).Append(',').Append(Mathf.RoundToInt(f.KarenYaw))
                  .Append(',').Append(f.KarenMood).Append(',').Append(f.KarenSees ? 1 : 0).Append(',').Append(f.Chasing ? 1 : 0)
                  .Append(',').Append(N(f.Guess.x)).Append(',').Append(N(f.Guess.y)).Append(',').Append(N(f.GuessConfidence)).Append(',').Append(f.Power ? 1 : 0);
                sb.Append(",[");
                for (int j = 0; j < f.Customers.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    PersonState c = f.Customers[j];
                    sb.Append('[').Append(c.Id).Append(',').Append(N(c.At.x)).Append(',').Append(N(c.At.y)).Append(',').Append(Mathf.RoundToInt(c.Yaw))
                      .Append(',').Append((int)c.State).Append(',').Append(Mathf.RoundToInt(c.Wait)).Append(',').Append(c.Bay).Append(']');
                }
                sb.Append("],");
                Points(sb, f.Spills);
                sb.Append(",[");
                for (int j = 0; j < f.EmptyBays.Count; j++) { if (j > 0) sb.Append(','); sb.Append(f.EmptyBays[j]); }
                sb.Append("],[");
                for (int j = 0; j < f.Bins.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    Vector4 b = f.Bins[j];
                    sb.Append('[').Append(N(b.x)).Append(',').Append(N(b.y)).Append(',').Append((int)b.z).Append(',').Append((int)b.w).Append(']');
                }
                sb.Append("],");
                Points(sb, f.Bags);
                sb.Append(',');
                Points(sb, f.LockedDoors);
                sb.Append(",[");
                for (int j = 0; j < f.Props.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    Vector4 p = f.Props[j];
                    sb.Append('[').Append((int)p.x).Append(',').Append(N(p.y)).Append(',').Append(N(p.z)).Append(',').Append(N(p.w)).Append(']');
                }
                sb.Append("]]");
            }
            sb.Append("],\"events\":[");
            for (int i = 0; i < Events.Count; i++)
            {
                if (i > 0) sb.Append(',');
                ShiftEvent e = Events[i];
                sb.Append("{\"t\":").Append(N(e.T)).Append(",\"k\":\"").Append(e.Kind).Append('"');
                if (e.HasPlace) sb.Append(",\"x\":").Append(N(e.At.x)).Append(",\"y\":").Append(N(e.At.y));
                if (!string.IsNullOrEmpty(e.Who)) sb.Append(",\"w\":\"").Append(MiniJson.EscapeInner(e.Who)).Append('"');
                if (e.Radius > 0f) sb.Append(",\"r\":").Append(N(e.Radius));
                if (!string.IsNullOrEmpty(e.Text)) sb.Append(",\"s\":\"").Append(MiniJson.EscapeInner(e.Text)).Append('"');
                sb.Append('}');
            }
            sb.Append("],\"markers\":");
            ClipMoments.WriteMarkers(sb, Markers);
            sb.Append(",\"moments\":");
            ClipMoments.WriteMoments(sb, Moments);
            sb.Append('}');
            return sb.ToString();
        }

        static void Points(StringBuilder sb, List<Vector2> points)
        {
            sb.Append('[');
            for (int j = 0; j < points.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append('[').Append(N(points[j].x)).Append(',').Append(N(points[j].y)).Append(']');
            }
            sb.Append(']');
        }

        // JSON has no NaN or Infinity; a broken number becomes 0 rather than a broken file.
        static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : (Mathf.Round(v * 100f) / 100f).ToString(CultureInfo.InvariantCulture);
    }
}
