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
    // with the reason and never passes MaxChance, one she checked lately is left alone for a
    // while, and a place with no reason stays safe.
    public static class HideCheck
    {
        public const float Reach = 1.5f;          // Q24: from her, to the inside (door open) or to you (under a rack)
        public const float Near = 2.5f;           // her belief this close to a place's inside is about that place
        public const float MinReason = 0.04f;     // the share of her belief beside it before she'd look
        public const float MaxChance = 0.85f;
        public const float HabitReason = 0.05f;   // a Ledger favourite, weighed as Search weighs it
        public const float ChancePerReason = 3f;
        public const float RecheckSeconds = 45f;  // a place she just looked in is left alone this long
        public const float Around = 6f;           // how far from where she's searching she'd go to check
        public const int MaxPerPlan = 2;

        public static Vector3 Centre(HidingPlace place) => place.transform.TransformPoint(place.interior.center);
        static float FloorOf(HidingPlace place) => place.transform.TransformPoint(place.interior.min).y;

        // Her map's cells beside the place, in the room it opens into (the inside itself is
        // furniture, off her map; belief through a wall isn't about this place). Cached per map.
        static readonly Dictionary<HidingPlace, (StoreMap map, List<int> cells)> beside = new Dictionary<HidingPlace, (StoreMap, List<int>)>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => beside.Clear();

        public static List<int> CellsBeside(StoreMap map, HidingPlace place)
        {
            if (beside.TryGetValue(place, out var known) && known.map == map) return known.cells;
            var cells = new List<int>();
            Vector3 centre = Centre(place);
            int at = map.CellAt(centre);
            int room = at >= 0 ? map.CellRoom[at] : -1;
            if (room >= 0)
                for (int cell = 0; cell < map.CellCount; cell++)
                    if (map.CellRoom[cell] == room && place.DistanceTo(map.CellPosition[cell]) <= Near) cells.Add(cell);   // to its inside: a long rack's ends too
            beside[place] = (map, cells);
            return cells;
        }

        // Why she'd look in `place`: her belief beside it, plus a Ledger favourite's weight.
        public static float Reason(KarenContext c, HidingPlace place, ICollection<int> habits = null)
        {
            if (place == null || !place.Usable || c.Map == null || c.Belief == null) return 0f;
            if (c.Belief.IsOutside(Centre(place))) return 0f;
            List<int> cells = CellsBeside(c.Map, place);
            float mass = 0f;
            foreach (int cell in cells) mass += c.Belief[cell];
            if (habits != null && habits.Count > 0 && cells.Any(cell => habits.Contains(c.Map.CellRegion[cell])))
                mass += HabitReason;
            return mass;
        }

        public static float Chance(float reason) =>
            reason < MinReason ? 0f : Mathf.Min(MaxChance, reason * ChancePerReason);

        public static bool CheckedLately(KarenContext c, HidingPlace place) =>
            c.HideChecked.TryGetValue(place, out float at) && c.Now - at < RecheckSeconds;

        // The hiding place near `around` she has most reason to check, then a roll. Null, and no
        // roll (a shift with nothing to suspect keeps her random stream), when none has a reason.
        public static HidingPlace Pick(KarenContext c, Vector3 around, float radius, ICollection<HidingPlace> skip)
        {
            if (c.Map == null || c.Belief == null) return null;
            List<int> habits = c.Ledger != null ? c.Ledger.ConcealmentRegions(c, 3) : null;
            HidingPlace best = null;
            float bestScore = 0f, bestReason = 0f;
            Vector3 bestCentre = default;
            Vector3 from = c.Body != null ? c.Body.Position : around;
            foreach (HidingPlace place in HidingPlace.All)
            {
                if (!place.Usable || skip != null && skip.Contains(place) || CheckedLately(c, place)) continue;
                Vector3 centre = Centre(place);
                if (FairnessGuard.Flat(centre, around) > radius) continue;
                float reason = Reason(c, place, habits);
                if (reason < MinReason) continue;
                float score = reason / (2f + FairnessGuard.Flat(centre, from));
                // Ties go by position, so the pick never depends on the order places registered.
                if (score < bestScore || score == bestScore && best != null &&
                    (centre.x > bestCentre.x || centre.x == bestCentre.x && centre.z >= bestCentre.z)) continue;
                bestScore = score;
                bestReason = reason;
                bestCentre = centre;
                best = place;
            }
            if (best == null) return null;
            float chance = Chance(bestReason);
            bool check = c.Rng.Chance(chance);
            c.Think("SEARCH", $"{(check ? "will check" : "passes up")} {best.name}: {bestReason:P0} of her belief beside it, a {chance:P0} chance");
            return check ? best : null;
        }

        // One check near `around`, added to `plan`; at most MaxPerPlan in a plan.
        public static bool AddTo(KarenContext c, PlanBuilder plan, Vector3 around, float radius, HashSet<HidingPlace> picked)
        {
            if (picked.Count >= MaxPerPlan) return false;
            HidingPlace place = Pick(c, around, radius, picked);
            if (place == null) return false;
            int before = plan.Steps.Count;
            plan.Add(new CheckHiding(place));
            if (plan.Steps.Count == before) return false;   // dropped: over the planning budget
            picked.Add(place);
            return true;
        }

        // What she opens to look inside: a cabinet's, locker's or fridge's door (IHidingDoor), on
        // the place or beside it in its assembly; else a cabin's hinged door. A rack has neither.
        // The search goes up its assembly a few levels (a chiller's door can sit beside its inside,
        // under the fixture) and takes the nearest door that is close enough to be this place's.
        public static IHidingDoor DoorOf(HidingPlace place) =>
            Nearest(place, t => t.GetComponentsInChildren<MonoBehaviour>(true).OfType<IHidingDoor>().Where(d => d.IsHidingDoor), d => d.DoorPoint);

        public static HingeDoor HingeDoorOf(HidingPlace place) =>
            Nearest(place, t => t.GetComponentsInChildren<HingeDoor>(true), d => d.DoorCentre);

        static T Nearest<T>(HidingPlace place, System.Func<Transform, IEnumerable<T>> under, System.Func<T, Vector3> at) where T : class
        {
            Vector3 centre = Centre(place);
            float within = place.interior.extents.magnitude + 1f;
            Transform level = place.transform;
            for (int up = 0; up < 4 && level != null; up++, level = level.parent)
            {
                T best = under(level).Where(d => d != null && Vector3.Distance(at(d), centre) <= within)
                                     .OrderBy(d => (at(d) - centre).sqrMagnitude).FirstOrDefault();
                if (best != null) return best;
            }
            return null;
        }

        // Out of the way of the door's swing, and where she looks in once it's open.
        public static void DoorSpots(KarenContext c, HidingPlace place, Vector3 door, float leafWidth, out Vector3 clear, out Vector3 opening)
        {
            Vector3 outward = door - Centre(place);
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : place.transform.forward;
            // The leaf sweeps a quarter circle of its width from the hinge, half a width to her side.
            const float body = 0.45f;
            float clearDistance = Mathf.Sqrt(Mathf.Max(0.25f, (leafWidth + body) * (leafWidth + body) - leafWidth * leafWidth * 0.25f)) + 0.1f;
            Vector3 floor = new Vector3(0f, FloorOf(place) - door.y, 0f);
            clear = Reachable(c, door + floor + outward * clearDistance);
            opening = Reachable(c, door + floor + outward * 0.55f);
        }

        // Under a rack: where along it her belief beside it is heaviest, from the aisle.
        public static Vector3 KneelSpot(KarenContext c, HidingPlace place)
        {
            Vector3 centre = Centre(place);
            Vector3 sum = Vector3.zero;
            float mass = 0f;
            if (c.Map != null && c.Belief != null)
                foreach (int cell in CellsBeside(c.Map, place)) { sum += c.Map.CellPosition[cell] * c.Belief[cell]; mass += c.Belief[cell]; }
            Vector3 toward = mass > 1e-6f ? sum / mass : c.Body != null ? c.Body.Position : centre;
            Vector3 along = place.transform.TransformPoint(place.interior.ClosestPoint(place.transform.InverseTransformPoint(toward)));
            along.y = FloorOf(place);
            return Reachable(c, along);
        }

        static Vector3 Reachable(KarenContext c, Vector3 at) =>
            c.Body != null ? HidingApproach.ClosestReachable(c.Body.Agent, at) : at;
    }

    // One hide check, seen by anyone watching: she walks to the place; at a door she stands clear
    // of its swing, opens it, waits for it to open, steps up to the opening and looks in; at a
    // rack she kneels and looks under. Her eye then says whether you're in there
    // (KarenBrain.CatchInHiding): a yes is a catch, at once. If you aren't, what she looked at goes
    // into her belief as empty. If she has no reason left when she sets out, she skips it.
    public sealed class CheckHiding : Primitive
    {
        public const float LookSeconds = 1.2f, OpenTimeout = 2f, OpenEnough = 0.75f, WalkTimeout = 25f, StepTimeout = 3f;
        enum Phase { Walk, Open, Step, Look }

        readonly HidingPlace place;
        readonly bool needReason;
        IHidingDoor leaf;
        HingeDoor hinge;
        bool kneel, skipped, opened;
        Vector3 stand, opening;
        Phase phase;
        float t;

        public HidingPlace Place => place;
        public bool Kneels => kneel;
        public bool Looked { get; private set; }
        public bool Found { get; private set; }
        public bool DoorWasOpenWhenLooked { get; private set; }

        public CheckHiding(HidingPlace place, bool needReason = true)
        {
            this.place = place;
            this.needReason = needReason;
            Label = "check " + (place != null ? place.name : "a hiding place");
        }

        protected override void Begin(KarenContext c)
        {
            if (place == null || !place.Usable ||
                needReason && HideCheck.Reason(c, place, c.Ledger?.ConcealmentRegions(c, 3)) < HideCheck.MinReason * 0.5f)
            {
                skipped = true;
                return;
            }
            kneel = place.requiresCrouch;
            if (!kneel)
            {
                leaf = HideCheck.DoorOf(place);
                hinge = leaf == null ? HideCheck.HingeDoorOf(place) : null;
            }
            if (kneel) stand = HideCheck.KneelSpot(c, place);
            else if (leaf != null) HideCheck.DoorSpots(c, place, leaf.DoorPoint, leaf.LeafWidth, out stand, out opening);
            else if (hinge != null) HideCheck.DoorSpots(c, place, hinge.DoorCentre, 0.9f, out stand, out opening);
            else stand = opening = HideCheck.KneelSpot(c, place);   // an open-fronted place: look in from beside it
            c.Body.MoveTo(stand, KarenBody.Pace.Walk);
        }

        protected override Status Tick(KarenContext c, float dt)
        {
            if (skipped) { c.Think("SEARCH", $"skips {Label}: no reason left"); return Status.Success; }
            if (place == null) return Status.Failure;   // gone (a sandbox restart) while she was on her way
            t += dt;
            Vector3 centre = HideCheck.Centre(place);
            switch (phase)
            {
                case Phase.Walk:
                    if (c.Body.PathFailed || t > WalkTimeout) { c.Think("SEARCH", $"{Label}: couldn't get there"); return Status.Failure; }
                    if (!c.Body.Arrived(0.5f)) return Status.Running;
                    c.Body.Stop();
                    if (kneel || leaf == null && hinge == null) { Next(Phase.Look, c); return Status.Running; }
                    Next(Phase.Open, c);
                    if (leaf != null) leaf.SetOpenForCheck(true);
                    else if (!hinge.IsOpen) hinge.OpenFor(c.Body.Position, NoiseAuthor.Karen);
                    opened = true;
                    c.Think("SEARCH", $"opens {place.name}");
                    return Status.Running;

                case Phase.Open:
                    c.Body.TurnToward(centre);
                    if (!DoorOpen() && t < OpenTimeout) return Status.Running;
                    if (!DoorOpen())
                    {
                        Done(c);
                        c.Think("SEARCH", $"{place.name}: the door wouldn't open");
                        return Status.Success;
                    }
                    Next(Phase.Step, c);
                    c.Body.MoveTo(opening, KarenBody.Pace.Walk);
                    return Status.Running;

                case Phase.Step:
                    if (!c.Body.Arrived(0.35f) && t < StepTimeout) return Status.Running;
                    c.Body.Stop();
                    Next(Phase.Look, c);
                    return Status.Running;

                default:
                    c.Body.TurnToward(centre);
                    if (t < LookSeconds) return Status.Running;
                    Looked = true;
                    DoorWasOpenWhenLooked = kneel || leaf == null && hinge == null || DoorOpen();
                    Done(c);
                    if (DoorWasOpenWhenLooked && c.Brain != null && c.Brain.CatchInHiding(place, !kneel))
                    {
                        Found = true;
                        return Status.Success;
                    }
                    SweepEmpty(c);
                    c.Think("SEARCH", $"{place.name}: nobody");
                    return Status.Success;
            }
        }

        void Next(Phase next, KarenContext c)
        {
            phase = next;
            t = 0f;
            if (next != Phase.Look) return;
            if (kneel) c.Body.Kneel(true);
            c.Think("SEARCH", kneel ? $"kneels at {place.name} and looks under it" : $"looks into {place.name}");
        }

        bool DoorOpen() => leaf != null ? leaf.OpenFraction >= OpenEnough : hinge == null || hinge.IsOpen && !hinge.IsMoving;

        void Done(KarenContext c) { if (place != null) c.HideChecked[place] = c.Now; }

        protected override void End(KarenContext c, bool aborted)
        {
            if (kneel) c.Body.Kneel(false);
            if (leaf != null && opened && !Found) leaf.SetOpenForCheck(false);
        }

        // Nobody there: her belief beside the place keeps only what she could have missed (under a
        // rack, past her reach; inside an open cabinet, nothing).
        void SweepEmpty(KarenContext c)
        {
            if (c.Map == null || c.Belief == null) return;
            List<int> cells = HideCheck.CellsBeside(c.Map, place);
            if (cells.Count == 0) return;
            c.Belief.ApplySweep(new List<int>(cells), cells.Select(_ => kneel ? 0.6f : 0.9f).ToList());
        }
    }
}
