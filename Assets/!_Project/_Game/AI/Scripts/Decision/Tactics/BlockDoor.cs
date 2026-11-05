using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Block door: she puts a box behind a door so it won't open (0.2.0, Corner). A draft until
    // its box, slot and highlight are rebuilt; this file is the second builder's (B2.1).
    public sealed class DoorLockTactic : Tactic
    {
        public override string Id => "door_lock";
        public override string Title => "Block door";
        public override ActionId[] Actions => new[] { ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 7;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.12f;
        public override TellKind Tell => TellKind.Clunk;
        public override string Chore => "find another way round";
        public override string Attacks => "space";

        HingeDoor door;
        int doorRegion = -1;

        public override bool Available(KarenContext c, out string why)
        {
            door = null;
            float best = float.MaxValue;
            foreach (HingeDoor d in Object.FindObjectsByType<HingeDoor>())
            {
                if (c.World.IsLocked(d)) continue;
                int region = c.Map.RegionAt(d.transform.position);
                if (region < 0) continue;
                float walk = TacticHelpers.WalkFromBelief(c, d.transform.position);
                if (walk < 5f || walk > 40f) continue;
                if (!Herding.Winnable(c, new[] { region }, out string check)) continue;
                if (walk < best) { best = walk; door = d; doorRegion = region; }
            }
            why = door == null ? "no door she can lock and keep the shift winnable" : null;
            return door != null;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            HingeDoor target = door;
            int region = doorRegion;
            plan.Target = c.RegionName(region);
            plan.Add(new Tell(TellKind.Clunk, _ => target.transform.position, 0.9f));
            plan.Add(new Effect("lock " + plan.Target, k =>
            {
                k.World.LockDoor(target, 75f);
                k.Brain.Close(region, 75f);
            }));
        }
    }
}
