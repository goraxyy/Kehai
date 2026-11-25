using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // The hide checks (0.5.2, Phase 9): the fridge check (I8), the locker check (every other
    // closed hiding place: the staff lockers, the WC cupboard, the toilet and changing cabins)
    // and the kneel check (I9, under a stockroom rack).
    //
    // Which place she checks comes only from what she sensed (Q24, rule A): her belief right
    // beside the place, which already carries what she saw, heard, was told and the traces she
    // read, and her Ledger's habits for this employee (a hide she saw counts for more, I6). Never
    // from where you are: this file is under the "she never reads the player" scan.
    //
    // It's never certain she checks: a place with a reason is checked with a chance that grows
    // with the reason and never passes MaxChance; a place with no reason stays safe.
    public static class HideCheck
    {
        public const float Reach = 1.5f;        // Q24: from her, to the inside (open door) or to you (under a rack)
        public const float Near = 2.5f;         // her belief this close to a place is about that place
        public const float MinReason = 0.04f;   // the share of her belief beside it before she would look
        public const float MaxChance = 0.85f;
        public const float HabitReason = 0.05f; // a Ledger favourite, weighed as Search weighs it
        public const float ChancePerReason = 3f;
        public const int MaxPerPlan = 2;

        public static Vector3 Centre(HidingPlace place) => place.transform.TransformPoint(place.interior.center);

        // The room the place opens into: that of her map's nearest cell (the inside itself is
        // furniture, off her map). Belief through a wall isn't about this place.
        public static int RoomOf(StoreMap map, HidingPlace place)
        {
            int at = map.CellAt(Centre(place));
            return at >= 0 ? map.CellRoom[at] : -1;
        }

        // Her map's cells beside the place, in its room.
        public static List<int> CellsBeside(StoreMap map, HidingPlace place)
        {
            var cells = new List<int>();
            Vector3 centre = Centre(place);
            int room = RoomOf(map, place);
            for (int cell = 0; cell < map.CellCount; cell++)
                if (map.CellRoom[cell] == room && FairnessGuard.Flat(map.CellPosition[cell], centre) <= Near) cells.Add(cell);
            return cells;
        }

        // Why she'd look in `place`: her belief within Near of it, plus a Ledger favourite's weight.
        public static float Reason(KarenContext c, HidingPlace place, ICollection<int> habits = null)
        {
            if (place == null || !place.Usable || c.Map == null || c.Belief == null) return 0f;
            Vector3 centre = Centre(place);
            if (c.Belief.IsOutside(centre)) return 0f;
            List<int> beside = CellsBeside(c.Map, place);
            float mass = 0f;
            foreach (int cell in beside) mass += c.Belief[cell];
            if (habits != null && habits.Count > 0 && beside.Any(cell => habits.Contains(c.Map.CellRegion[cell])))
                mass += HabitReason;
            return mass;
        }

        public static float Chance(float reason) =>
            reason < MinReason ? 0f : Mathf.Min(MaxChance, reason * ChancePerReason);

        // The hiding place near `around` she has most reason to check, then a roll. Null (and no
        // roll, so a shift with nothing to suspect keeps her random stream) when none has a reason.
        public static HidingPlace Pick(KarenContext c, Vector3 around, float radius, ICollection<HidingPlace> skip)
        {
            List<int> habits = c.Ledger != null ? c.Ledger.ConcealmentRegions(c, 3) : null;
            HidingPlace best = null;
            float bestScore = 0f, bestReason = 0f;
            foreach (HidingPlace place in Object.FindObjectsByType<HidingPlace>()
                         .Where(p => p != null && p.Usable && (skip == null || !skip.Contains(p)))
                         .OrderBy(p => Centre(p).x).ThenBy(p => Centre(p).z))
            {
                Vector3 centre = Centre(place);
                if (FairnessGuard.Flat(centre, around) > radius) continue;
                float reason = Reason(c, place, habits);
                if (reason < MinReason) continue;
                float score = reason / (2f + FairnessGuard.Flat(centre, c.Body.Position));
                if (score <= bestScore) continue;
                bestScore = score;
                bestReason = reason;
                best = place;
            }
            if (best == null) return null;
            float chance = Chance(bestReason);
            bool check = c.Rng.Chance(chance);
            c.Think("SEARCH", $"{(check ? "will check" : "passes up")} {best.name}: {bestReason:P0} of her belief beside it, {chance:P0} chance");
            return check ? best : null;
        }

        // Up to one check near `around` added to `plan`, at most MaxPerPlan in a plan.
        public static bool AddTo(KarenContext c, PlanBuilder plan, Vector3 around, float radius, HashSet<HidingPlace> picked)
        {
            if (picked.Count >= MaxPerPlan) return false;
            HidingPlace place = Pick(c, around, radius, picked);
            if (place == null) return false;
            picked.Add(place);
            plan.Add(new CheckHiding(place));
            return true;
        }

        // What she opens to look inside: a cabinet's or locker's door (IHidingDoor), else a
        // cabin's hinged door. A rack has neither: she kneels.
        public static IHidingDoor DoorOf(HidingPlace place)
        {
            Vector3 centre = Centre(place);
            return place.GetComponentsInChildren<MonoBehaviour>(true)
                .Concat(place.GetComponentsInParent<MonoBehaviour>(true))
                .OfType<IHidingDoor>()
                .Where(d => d != null && d.IsHidingDoor)
                .OrderBy(d => (d.DoorPoint - centre).sqrMagnitude)
                .FirstOrDefault();
        }

        public static HingeDoor HingeDoorOf(HidingPlace place)
        {
            Vector3 centre = Centre(place);
            return place.GetComponentsInChildren<HingeDoor>(true)
                .Concat(place.GetComponentsInParent<HingeDoor>(true))
                .Where(d => d != null)
                .OrderBy(d => (d.DoorCentre - centre).sqrMagnitude)
                .FirstOrDefault();
        }

        // Where she stands to check: in front of the door, or beside the rack.
        public static Vector3 Front(KarenContext c, HidingPlace place)
        {
            Vector3 centre = Centre(place);
            Vector3 face = centre;
            IHidingDoor leaf = DoorOf(place);
            HingeDoor hinge = leaf == null ? HingeDoorOf(place) : null;
            Vector3? door = leaf != null ? leaf.DoorPoint : hinge != null ? hinge.DoorCentre : (Vector3?)null;
            if (door.HasValue)
            {
                Vector3 outward = door.Value - centre;
                outward.y = 0f;
                face = door.Value + (outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward) * 0.7f;
            }
            face.y = place.transform.TransformPoint(place.interior.min).y;
            return c.Body != null ? HidingApproach.ClosestReachable(c.Body.Agent, face) : face;
        }
    }

    // One hide check: walk to the place, open it (or kneel at a rack), look for about a second;
    // her eye says whether you're in there (KarenBrain.CatchInHiding). If you aren't, what she
    // looked at goes into her belief as empty. If she no longer has a reason when she sets out,
    // she skips it.
    public sealed class CheckHiding : Primitive
    {
        public const float LookSeconds = 1.2f, OpenSeconds = 0.4f, WalkTimeout = 25f;
        readonly HidingPlace place;
        IHidingDoor leaf;
        HingeDoor hinge;
        bool kneel, arrived, opened, skipped;
        Vector3 front;
        float t;

        public HidingPlace Place => place;
        public bool Looked { get; private set; }
        public bool Found { get; private set; }

        public CheckHiding(HidingPlace place)
        {
            this.place = place;
            Label = "check " + (place != null ? place.name : "a hiding place");
        }

        protected override void Begin(KarenContext c)
        {
            if (place == null || !place.Usable || HideCheck.Reason(c, place, c.Ledger?.ConcealmentRegions(c, 3)) < HideCheck.MinReason * 0.5f)
            {
                skipped = true;
                return;
            }
            leaf = HideCheck.DoorOf(place);
            hinge = leaf == null ? HideCheck.HingeDoorOf(place) : null;
            kneel = leaf == null && hinge == null;
            front = HideCheck.Front(c, place);
            c.Body.MoveTo(front, KarenBody.Pace.Walk);
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            if (skipped) { c.Think("SEARCH", $"skips {Label}: no reason left"); return Status.Success; }
            t += dt;
            if (!arrived)
            {
                if (c.Body.PathFailed || t > WalkTimeout) { c.Think("SEARCH", $"{Label}: couldn't get there"); return Status.Failure; }
                if (!c.Body.Arrived(0.5f)) return Status.Running;
                arrived = true;
                t = 0f;
                c.Body.Stop();
            }
            c.Body.TurnToward(HideCheck.Centre(place));
            if (!opened && t >= OpenSeconds * 0.5f)
            {
                opened = true;
                if (leaf != null) leaf.SetOpenForCheck(true);
                else if (hinge != null && !hinge.IsOpen) hinge.OpenFor(c.Body.Position, NoiseAuthor.Karen);
                else if (kneel) c.Body.Kneel(true);
                c.Think("SEARCH", kneel ? $"kneels at {place.name} and looks under it" : $"opens {place.name} and looks inside");
            }
            if (t < OpenSeconds + LookSeconds) return Status.Running;

            Looked = true;
            if (c.Brain != null && c.Brain.CatchInHiding(place, !kneel))
            {
                Found = true;
                return Status.Success;
            }
            SweepEmpty(c);
            c.Think("SEARCH", $"{place.name}: nobody");
            return Status.Success;
        }

        protected override void End(KarenContext c, bool aborted)
        {
            if (kneel) c.Body.Kneel(false);
            if (leaf != null && opened && !Found) leaf.SetOpenForCheck(false);
        }

        // Nobody there: her belief beside the place, where she looked, keeps only what she could
        // have missed (under a rack, past her reach).
        void SweepEmpty(KarenContext c)
        {
            if (c.Map == null || c.Belief == null) return;
            List<int> cells = HideCheck.CellsBeside(c.Map, place);
            var p = cells.Select(_ => kneel ? 0.6f : 0.9f).ToList();
            if (cells.Count > 0) c.Belief.ApplySweep(cells, p);
        }
    }
}
