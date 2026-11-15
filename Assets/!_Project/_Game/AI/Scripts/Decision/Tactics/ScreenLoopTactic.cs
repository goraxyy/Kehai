using UnityEngine;

namespace Kehai.Karen
{
    public sealed class ScreenLoopTactic : Tactic
    {
        public override string Id => "screen_loop";
        public override string Title => "Screen loop";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 3;
        public override float TensionCost => .1f;
        public override float Cooldown => 180f;
        public override float PanicPrior => .08f;
        public override TellKind Tell => TellKind.FrozenClock;
        public override bool IsHandsOn => true;
        public override bool LeavesChore => true;
        public override string Chore => "reset the frozen camera screen at the camera box, backstreet (20 s)";
        public override string Attacks => "sight,trust";
        public override bool Available(KarenContext c, out string why)
        {
            var cams = StoreCameras.Instance;
            why = FairnessGuard.DoorsClosed(c) ? FairnessGuard.ClosedWhy :
                cams == null || cams.Wall == null || cams.Box == null || cams.Cameras.Count == 0 ? "no screens or reset box" :
                cams.Broken || cams.Looped ? "the screens already need resetting" : null;
            return why == null;
        }
        public override bool EffectRunning(KarenContext c) => StoreCameras.Instance != null && StoreCameras.Instance.Looped;
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            var cams = StoreCameras.Instance;
            at = cams != null && cams.Box != null ? cams.Box.transform.position : default;
            return EffectRunning(c) && cams.Box != null;
        }
        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            var cams = StoreCameras.Instance;
            if (cams == null || cams.Wall == null || cams.Cameras.Count == 0) return;
            // The chosen feed depends on her belief, never the employee's real room.
            StoreCamera target = cams.Cameras[0];
            float best = float.MaxValue;
            Vector3 believed = c.Belief != null && c.Map != null ? TacticHelpers.Believed(c) : target.transform.position;
            foreach (StoreCamera camera in cams.Cameras)
            {
                float d = FairnessGuard.Flat(camera.Spot.LookAt, believed);
                if (d < best) { best = d; target = camera; }
            }
            Vector3 screens = cams.Wall.transform.position;
            plan.Target = target.Spot.Name + " screen, office";
            // Work at the front face, not the desk's usual watching distance (rule 9).
            Vector3 handSpot = screens + cams.Wall.Front * .25f;
            Vector3 stand = TacticHelpers.OnHerFloor(handSpot + cams.Wall.Front * .6f, 2.5f);
            plan.Go(stand, KarenBody.Pace.Walk, "go to the office screens", .35f);
            plan.Add(new HandsOn("freeze the empty camera feed; its clock stops", _ => handSpot, k =>
            {
                if (!FairnessGuard.DoorsClosed(k) && cams != null)
                {
                    k.Brain?.RecordTell(TellKind.FrozenClock, screens, 0f);
                    cams.Loop(target);
                }
            }));
        }
    }
}
