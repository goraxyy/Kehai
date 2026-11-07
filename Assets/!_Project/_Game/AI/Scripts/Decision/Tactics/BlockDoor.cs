using System.Collections.Generic;
using System.Linq;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Block door (0.3.0): a restock box on a floor frame right behind a door, on the side away
    // from where she expects you to come. The door stays shut, from both sides, until someone
    // picks the box up: from your side you have to go round. No timer. She walks to that side
    // to put it there (rule 9), and only where you could still finish the shift (rule 6).
    // The saved tactic ID stays the same so old ledgers still read.
    public sealed class DoorLockTactic : Tactic
    {
        public override string Id => "door_lock";
        public override bool IsHandsOn => true;
        public override string Title => "Block door";
        public override ActionId[] Actions => new[] { ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.12f;
        public override TellKind Tell => TellKind.Clunk;
        public override string Chore => "go round to the box's side of the door and pick it up";
        public override string Attacks => "space";

        public const float MinWalk = 5f, MaxWalk = 40f;
        public const float StandBack = 1.3f;   // where she stands to put the box down: clear of the box

        // The door and side this plan blocked, for the placement memory's reward.
        public string LastKey { get; private set; }

        public override bool EffectRunning(KarenContext c)
        {
            foreach (HingeDoor d in HingeDoor.All)
                if (d != null && d.Blocker != null && d.Blocker.IsBlocking) return true;
            return false;
        }

        // You fix it at the box: on its side of the door, where it sits.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            foreach (HingeDoor d in HingeDoor.All)
                if (d != null && d.Blocker != null && d.Blocker.IsBlocking && d.Blocker.Box != null)
                {
                    at = d.Blocker.Box.transform.position;
                    return true;
                }
            return false;
        }

        // The side the box goes on: away from where she expects you to come. Pure.
        public static int FarSide(Vector3 doorCentre, Vector3 sideNormal, Vector3 you)
        {
            Vector3 d = you - doorCentre;
            d.y = 0f;
            return Vector3.Dot(d, sideNormal) >= 0f ? -1 : 1;
        }

        public struct DoorOption
        {
            public HingeDoor Door;
            public int Side;
            public int Region;
            public Vector3 Stand;
            public string Key;
        }

        public static string Key(KarenContext c, HingeDoor door, int side) =>
            $"{(c.Map != null ? c.Map.NameAt(door.DoorCentre) : door.name)} · side {(side > 0 ? "+" : "-")}";

        // Closed doors 5–40 m from you that she can block and keep the shift winnable, on the far
        // side from you; guessed best on the routes you take and nearer you.
        public static List<DoorOption> Doors(KarenContext c, ProductBox box, out List<PlaceCandidate> candidates)
        {
            var result = new List<DoorOption>();
            candidates = new List<PlaceCandidate>();
            if (box == null || c.Map == null) return result;
            Vector3 you = TacticHelpers.Believed(c);
            float routeTotal = c.Ledger != null ? c.Ledger.Data.routes.Sum(r => r.count) : 0f;
            foreach (HingeDoor d in Object.FindObjectsByType<HingeDoor>())
            {
                if (d == null || d.Locked || d.IsOpen || d.IsMoving) continue;
                int region = c.Map.RegionAt(d.transform.position);
                if (region < 0) continue;
                float walk = TacticHelpers.WalkFromBelief(c, d.DoorCentre);
                if (walk < MinWalk || walk > MaxWalk) continue;
                if (!DoorBlock.SideNormal(d, out Vector3 normal)) continue;
                int side = FarSide(d.DoorCentre, normal, you);
                if (!DoorBlock.TryPlacement(d, box, side, out _, out _)) continue;
                if (!Winnable(c, region)) continue;

                string key = c.Map.Regions[region].Key;
                float onRoutes = 0f;
                if (routeTotal > 0f)
                    foreach (RouteStat r in c.Ledger.Data.routes)
                        if (r.path.Contains(key)) onRoutes += r.count;
                float prior = 0.6f * (routeTotal > 0f ? onRoutes / routeTotal : 0f) + 0.4f * (1f - walk / MaxWalk);

                result.Add(new DoorOption
                {
                    Door = d, Side = side, Region = region,
                    Stand = TacticHelpers.OnFloor(d.DoorCentre + normal * side * StandBack),
                    Key = Key(c, d, side),
                });
                candidates.Add(new PlaceCandidate(result[result.Count - 1].Key, Mathf.Clamp01(prior)));
            }
            return result;
        }

        public override bool Available(KarenContext c, out string why)
        {
            ProductBox box = DoorBlock.FindBox();
            if (box == null) { why = "no resting stock box available"; return false; }
            bool any = Doors(c, box, out _).Count > 0;
            why = any ? null : "no clear door she can block and keep the shift winnable";
            return any;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            ProductBox stock = DoorBlock.FindBox();
            List<DoorOption> doors = Doors(c, stock, out List<PlaceCandidate> candidates);
            int pick = c.Ledger != null ? c.Ledger.PickPlace(c, this, candidates) : (doors.Count > 0 ? 0 : -1);
            if (pick < 0) return;
            DoorOption o = doors[pick];
            HingeDoor target = o.Door;
            plan.Target = c.RegionName(o.Region) + (o.Side > 0 ? ", far side +" : ", far side -");
            plan.Go(o.Stand, KarenBody.Pace.Walk, "go round to the far side of " + c.RegionName(o.Region), 1f);
            plan.Add(new Tell(TellKind.Clunk, _ => target != null ? target.DoorCentre : Vector3.zero, 0.9f));
            plan.Add(new PlaceBox(target, stock, o.Region, o.Side, o.Stand, "block " + plan.Target, o.Key, this));
        }

        static bool Winnable(KarenContext c, int region)
        {
            var closed = new HashSet<int> { region };
            DoorBlock.AddClosedRegions(closed);
            return Herding.Winnable(c, closed, out _);
        }

        // Recheck after the tell: the player may have opened the door or taken the box, and
        // she has to be at the door's side (rule 9). Report failure instead of rewarding a plan
        // that placed nothing.
        sealed class PlaceBox : Primitive
        {
            readonly HingeDoor target;
            readonly ProductBox stock;
            readonly int region, side;
            readonly Vector3 stand;
            readonly string key;
            readonly DoorLockTactic owner;

            public PlaceBox(HingeDoor target, ProductBox stock, int region, int side, Vector3 stand, string label, string key, DoorLockTactic owner)
            {
                this.target = target; this.stock = stock; this.region = region; this.side = side;
                this.stand = stand; this.key = key; this.owner = owner; Label = label;
            }

            protected override Status Tick(KarenContext c, float dt)
            {
                float reach = FairnessGuard.Flat(c.Body.Position, stand);
                if (reach > FairnessGuard.HandsOnReach)
                {
                    c.Think("CHECK", $"rule 9: {Label} is {reach:0.0} m from her hands, out of reach");
                    return Status.Failure;
                }
                if (!Winnable(c, region)) { c.Think("CHECK", "blocking it now would seal a job off"); return Status.Failure; }
                DoorBlock block = DoorBlock.Spawn(target, stock, region, side);
                if (block == null) return Status.Failure;
                FairnessGuard.CheckHandsOn("block door", c.Body, block.transform.position);
                owner.LastKey = key;
                // Paid by how long it held you up: moved at once, 0; a minute and a half, 1.
                c.Brain?.PlaceWatch.Watch(owner, key, c.Now, t =>
                {
                    if (block != null && block.IsBlocking) return null;
                    c.Brain?.Answered("box_moved", t, "the box moved off the door");
                    return PlaceRewards.ByTime(t, 90f);
                }, _ => 1f);
                c.Brain.RecordEffect(Label);
                return Status.Success;
            }
        }
    }
}
