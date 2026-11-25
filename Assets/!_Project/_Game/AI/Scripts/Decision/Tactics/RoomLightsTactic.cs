using System.Collections;
using UnityEngine;

namespace Kehai.Karen
{
    public sealed class RoomLightsTactic : Tactic
    {
        public override string Id => "room_lights";
        public override string Title => "Room lights";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Flush, ActionId.Corner };
        public override int Tier => 1;
        public override int IntroducedShift => 2;
        public override float TensionCost => .06f;
        public override float Cooldown => 90f;
        public override float PanicPrior => .06f;
        public override TellKind Tell => TellKind.RoomFlicker;
        public override bool LeavesChore => true;
        public override string Chore => "press the light switch by that room's door";
        public override string Attacks => "sight";
        RoomLightSwitch left;
        public override bool Available(KarenContext c, out string why)
        {
            why = FairnessGuard.DoorsClosed(c) ? FairnessGuard.ClosedWhy : Pick(c) == null ? "no lit room with a working switch" : null;
            return why == null;
        }
        static RoomLightSwitch Pick(KarenContext c)
        {
            RoomLightSwitch best = null;
            float distance = float.MaxValue;
            Vector3 believed = c.Belief != null && c.Map != null ? TacticHelpers.Believed(c) : Vector3.zero;
            foreach (var sw in RoomLightSwitch.All)
            {
                if (sw == null || sw.IsOff || sw.Warning || !sw.HasWorkingLights) continue;
                float d = FairnessGuard.Flat(sw.transform.position, believed);
                if (d < distance) { distance = d; best = sw; }
            }
            return best;
        }
        public override bool EffectRunning(KarenContext c) => left != null && left.IsOff;
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = left != null ? left.transform.position : default;
            return EffectRunning(c);
        }
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var sw = Pick(c);
            if (sw == null) return;
            plan.Target = sw.Room + " light switch";
            // At the switch first (25 November).
            TacticHelpers.GoToThing(plan, sw.transform.position, "the " + sw.Room + " light switch", 1f);
            plan.Add(new RoomFlicker(sw));
            TacticHelpers.AtThing(plan, this, sw.transform.position, "switch off the " + sw.Room + " lights");
            plan.Add(new Effect("switch off " + sw.Room + " lights", k =>
            {
                if (!FairnessGuard.DoorsClosed(k) && sw != null) { sw.SetOff(true); left = sw; }
            }));
        }
        sealed class RoomFlicker : Primitive
        {
            readonly RoomLightSwitch sw;
            IEnumerator routine;
            float until;
            public RoomFlicker(RoomLightSwitch sw) { this.sw = sw; Label = "two local light flickers"; }
            protected override void Begin(KarenContext c)
            {
                if (sw == null) return;
                routine = sw.FlickerTwice();
                c.Brain?.RecordTell(TellKind.RoomFlicker, sw.transform.position, .7f);
            }
            protected override Status Tick(KarenContext c, float dt)
            {
                if (sw == null || routine == null) return Status.Failure;
                until -= dt;
                if (until > 0f) return Status.Running;
                if (!routine.MoveNext()) return Status.Success;
                // The four waits in FlickerTwice are .15/.2/.15/.2. Driving it here also
                // makes abort disposal deterministic, rather than leaving a coroutine alive.
                until = step++ % 2 == 0 ? .15f : .2f;
                return Status.Running;
            }
            int step;
            protected override void End(KarenContext c, bool aborted) => (routine as System.IDisposable)?.Dispose();
        }
    }
}
