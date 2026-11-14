using UnityEngine;

namespace Kehai.Karen
{
    public sealed class FreezerAlarmTactic : Tactic
    {
        public override string Id => "freezer_alarm";
        public override string Title => "Freezer alarm";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Corner };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => .06f;
        public override float Cooldown => 120f;
        public override float PanicPrior => .06f;
        public override TellKind Tell => TellKind.FreezerAlarm;
        public override bool IsHandsOn => true;
        public override bool LeavesChore => true;
        public override string Chore => "close the freezer door in cold storage (press E)";
        public override string Attacks => "sound";
        FreezerAlarm left;
        static FreezerAlarm Pick()
        {
            foreach (var alarm in FreezerAlarm.All) if (alarm != null && !alarm.Ringing && alarm.Door != null) return alarm;
            return null;
        }
        public override bool Available(KarenContext c, out string why)
        {
            why = FairnessGuard.DoorsClosed(c) ? FairnessGuard.ClosedWhy : Pick() == null ? "no freezer door to prop" : null;
            return why == null;
        }
        public override bool EffectRunning(KarenContext c) => left != null && left.Ringing;
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = left != null ? left.transform.position : default;
            return EffectRunning(c);
        }
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var alarm = Pick();
            if (alarm == null) return;
            Vector3 at = alarm.transform.position;
            plan.Target = "freezer door, cold storage";
            plan.Go(TacticHelpers.OnHerFloor(at, 2.5f), KarenBody.Pace.Walk, "walk to the cold-room freezer", .35f);
            plan.Add(new HandsOn("prop open the freezer: alarm masks both sets of footsteps", _ => at, k =>
            {
                if (FairnessGuard.DoorsClosed(k) || alarm == null) return;
                // The alarm itself is the tell, made right here at her hands.
                k.Brain?.RecordTell(TellKind.FreezerAlarm, at, .35f);
                if (alarm.StartAlarm()) left = alarm;
            }));
        }
    }
}
