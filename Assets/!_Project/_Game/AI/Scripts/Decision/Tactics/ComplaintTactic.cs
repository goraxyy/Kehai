using UnityEngine;

namespace Kehai.Karen
{
    public sealed class ComplaintTactic : Tactic
    {
        public override string Id => "complaint";
        public override string Title => "The complaint";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Flush };
        public override int Tier => 2;
        public override int IntroducedShift => 3;
        public override float TensionCost => .12f;
        public override float Cooldown => 180f;
        public override float PanicPrior => .1f;
        public override TellKind Tell => TellKind.AngryWalk;
        public override bool IsHandsOn => true;
        public override bool LeavesChore => true;
        public override string Chore => "hear the angry shopper out at the till (hold E for 20 s)";
        public override string Attacks => "work,social";
        CustomerComplaint left;
        static CustomerNPC Pick()
        {
            foreach (var customer in CustomerNPC.All) if (CustomerComplaint.Eligible(customer)) return customer;
            return null;
        }
        public override bool Available(KarenContext c, out string why)
        {
            why = c.Shift == null || !c.Shift.IsShiftActive || FairnessGuard.DoorsClosed(c) ? FairnessGuard.ClosedWhy :
                Pick() == null ? "no shopper she has spoken to can reach the till" : null;
            return why == null;
        }
        public override bool EffectRunning(KarenContext c) => left != null && left.Active;
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = left != null ? left.FixPosition : default;
            return EffectRunning(c);
        }
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var customer = Pick();
            if (customer == null) return;
            plan.Target = "the shopper she spoke to, then the till";
            plan.Add(new MoveTo(k => customer != null ? customer.transform.position : k.Body.Position,
                KarenBody.Pace.Walk, "go to the shopper she spoke to", 1.3f, 30f, follow: true));
            plan.Add(new AtThing("talk the shopper into complaining", this, k => customer != null ? customer.transform.position : k.Body.Position));
            plan.Add(new HandsOn("send the angry shopper to complain at the till", k => customer != null ? customer.transform.position : k.Body.Position, k =>
            {
                if (customer == null || FairnessGuard.DoorsClosed(k)) return;
                k.Brain?.RecordTell(TellKind.AngryWalk, customer.transform.position, 1f);
                left = CustomerComplaint.Begin(customer, k.Shift);
            }));
        }
    }
}
