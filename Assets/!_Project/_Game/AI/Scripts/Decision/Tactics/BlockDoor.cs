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
        public override bool LeavesChore => true;
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
        public const float StandBack = 1.5f;   // where she stands to put the box down: clear of the box
        public const float StandArrive = 0.35f; // and how close to that spot she stops: not on the box's floor

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
            public Vector3 BoxAt;
            public string Key;
        }

        public static string Key(KarenContext c, HingeDoor door, int side) =>
            $"{(c.Map != null ? c.Map.NameAt(door.DoorCentre) : door.name)} · side {(side > 0 ? "+" : "-")}";

        // Closed doors 5–40 m from you that she can block and keep the shift winnable, on the far
        // side from you; guessed best on the routes you take and nearer you.
        public static List<DoorOption> Doors(KarenContext c, ProductBox box, out List<PlaceCandidate> candidates) =>
            Doors(c, box, out candidates, null);

        // `refused`, when given, collects why each door was passed over, for the thought log.
        public static List<DoorOption> Doors(KarenContext c, ProductBox box, out List<PlaceCandidate> candidates, List<string> refused)
        {
            var result = new List<DoorOption>();
            candidates = new List<PlaceCandidate>();
            if (box == null || c.Map == null) return result;
            Vector3 you = TacticHelpers.Believed(c);
            float routeTotal = c.Ledger != null ? c.Ledger.Data.routes.Sum(r => r.count) : 0f;
            foreach (HingeDoor d in Object.FindObjectsByType<HingeDoor>())
            {
                if (d == null) continue;
                string name = d.name;
                if (d.Locked || d.IsOpen || d.IsMoving) { refused?.Add(name + ": open, moving or locked"); continue; }
                int region = c.Map.RegionAt(d.transform.position);
                if (region < 0) { refused?.Add(name + ": off her map"); continue; }
                name = c.RegionName(region);
                float walk = TacticHelpers.WalkFromBelief(c, d.DoorCentre);
                if (walk < MinWalk || walk > MaxWalk) { refused?.Add($"{name}: {walk:0} m from you"); continue; }
                if (!DoorBlock.SideNormal(d, out Vector3 normal)) { refused?.Add(name + ": no sides"); continue; }
                int side = FarSide(d.DoorCentre, normal, you);
                if (!DoorBlock.TryPlacement(d, box, side, out Vector3 boxAt, out _)) { refused?.Add(name + ": no floor for the box on the far side"); continue; }
                // Where she stands to put it down: just beyond the box, on her floor, within reach of it.
                Vector3 stand = TacticHelpers.OnHerFloor(boxAt + normal * side * StandBack);
                if (FairnessGuard.Flat(stand, boxAt) > FairnessGuard.HandsOnReach - 0.2f || !TacticHelpers.CanReach(c, stand, 1.2f))
                { refused?.Add(name + ": she can't get round to the far side"); continue; }
                if (!Winnable(c, region)) { refused?.Add(name + ": would seal a job off (rule 6)"); continue; }

                string key = c.Map.Regions[region].Key;
                float onRoutes = 0f;
                if (routeTotal > 0f)
                    foreach (RouteStat r in c.Ledger.Data.routes)
                        if (r.path.Contains(key)) onRoutes += r.count;
                float prior = 0.6f * (routeTotal > 0f ? onRoutes / routeTotal : 0f) + 0.4f * (1f - walk / MaxWalk);

                result.Add(new DoorOption { Door = d, Side = side, Region = region, Stand = stand, BoxAt = boxAt, Key = Key(c, d, side) });
                candidates.Add(new PlaceCandidate(result[result.Count - 1].Key, Mathf.Clamp01(prior)));
            }
            return result;
        }

        public override bool Available(KarenContext c, out string why)
        {
            ProductBox box = DoorBlock.FindBox();
            if (box == null) { why = "no resting stock box available"; return false; }
            var refused = new List<string>();
            bool any = Doors(c, box, out _, refused).Count > 0;
            why = any ? null : "no door she can block: " + (refused.Count > 0 ? string.Join("; ", refused.Take(4)) : "none in the store");
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
            // Going round can be a long walk: allow it twice a plain walk's time.
            plan.Add(new MoveTo(_ => o.Stand, KarenBody.Pace.Hurry, "go round to the far side of " + c.RegionName(o.Region), StandArrive, timeout: 90f));
            // If her way round was through this very door, it's standing open behind her: she stops
            // (walking, her body opens doors in her way) and shuts it, and waits until it's shut.
            plan.Add(new Process("shut " + c.RegionName(o.Region) + " behind her", (k, dt) =>
            {
                k.Body.Stop();
                if (target == null) return true;
                if (target.IsMoving) return false;
                if (target.IsOpen) { target.Use(k.Body.Position, NoiseAuthor.Karen); return false; }
                return true;
            }, 6f));
            plan.Add(new Tell(TellKind.Clunk, _ => target != null ? target.DoorCentre : Vector3.zero, 0.9f));
            plan.Add(new PlaceBox(target, stock, o.Region, o.Side, o.BoxAt, "block " + plan.Target, o.Key, this));
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
            readonly Vector3 boxAt;
            readonly string key;
            readonly DoorLockTactic owner;

            public PlaceBox(HingeDoor target, ProductBox stock, int region, int side, Vector3 boxAt, string label, string key, DoorLockTactic owner)
            {
                this.target = target; this.stock = stock; this.region = region; this.side = side;
                this.boxAt = boxAt; this.key = key; this.owner = owner; Label = label;
            }

            protected override Status Tick(KarenContext c, float dt)
            {
                float reach = FairnessGuard.Flat(c.Body.Position, boxAt);
                if (reach > FairnessGuard.HandsOnReach)
                {
                    c.Think("CHECK", $"rule 9: {Label} is {reach:0.0} m from her hands, out of reach");
                    return Status.Failure;
                }
                if (!Winnable(c, region)) { c.Think("CHECK", "blocking it now would seal a job off"); return Status.Failure; }
                DoorBlock block = DoorBlock.Spawn(target, stock, region, side);
                if (block == null)
                {
                    string why = target == null ? "the door's gone" : target.IsOpen || target.IsMoving ? "the door's open"
                               : !DoorBlock.TryPlacement(target, stock, side, out _, out _) ? "something's in the box's way, or the box isn't home"
                               : "the door wouldn't take it";
                    c.Think("CHECK", $"{Label}: {why}");
                    return Status.Failure;
                }
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
