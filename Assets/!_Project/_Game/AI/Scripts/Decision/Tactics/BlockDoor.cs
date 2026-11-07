using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // A restock box holds a door shut until someone picks it up or recalls it home.
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
        public override string Chore => "move the box away from the door";
        public override string Attacks => "space";

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

        HingeDoor door;
        ProductBox box;
        int doorRegion = -1;

        public override bool Available(KarenContext c, out string why)
        {
            door = null;
            box = DoorBlock.FindBox();
            if (box == null) { why = "no resting stock box available"; return false; }
            float best = float.MaxValue;
            foreach (HingeDoor d in Object.FindObjectsByType<HingeDoor>())
            {
                if (d.Locked || d.IsOpen || d.IsMoving || c.World.IsLocked(d)) continue;
                int region = c.Map.RegionAt(d.transform.position);
                if (region < 0) continue;
                float walk = TacticHelpers.WalkFromBelief(c, d.transform.position);
                if (walk < 5f || walk > 40f) continue;
                if (!Winnable(c, region)) continue;
                if (!DoorBlock.TryPlacement(d, box, out _, out _)) continue;
                if (walk < best) { best = walk; door = d; doorRegion = region; }
            }
            why = door == null ? "no clear door she can block and keep the shift winnable" : null;
            return door != null;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            HingeDoor target = door;
            int region = doorRegion;
            ProductBox stock = box;
            plan.Target = c.RegionName(region);
            plan.Add(new Tell(TellKind.Clunk, _ => target != null ? target.DoorCentre : Vector3.zero, 0.9f));
            plan.Add(new PlaceBox(target, stock, region, "block " + plan.Target));
        }

        static bool Winnable(KarenContext c, int region)
        {
            var closed = new HashSet<int> { region };
            DoorBlock.AddClosedRegions(closed);
            return Herding.Winnable(c, closed, out _);
        }

        // Recheck after the tell: the player may have opened the door or taken the box.
        // Report failure instead of rewarding a plan that placed nothing.
        sealed class PlaceBox : Primitive
        {
            readonly HingeDoor target;
            readonly ProductBox stock;
            readonly int region;
            public PlaceBox(HingeDoor target, ProductBox stock, int region, string label)
            { this.target = target; this.stock = stock; this.region = region; Label = label; }
            protected override Status Tick(KarenContext c, float dt)
            {
                if (!Winnable(c, region) || DoorBlock.Spawn(target, stock, region) == null)
                    return Status.Failure;
                c.Brain.RecordEffect(Label);
                return Status.Success;
            }
        }
    }
}
