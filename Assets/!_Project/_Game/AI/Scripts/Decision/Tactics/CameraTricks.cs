using UnityEngine;

namespace Kehai.Karen
{
    // The store's cameras, and her two tricks with them (0.4.1; StoreCameras has the cameras,
    // the office screens and the camera box).
    //
    // The cameras belong to whoever is at the screens in the office. You can go and look: six
    // rooms at once, and her in them. So can she.

    // Camera watch: she walks to the office and stands at the screens for a while. While she's
    // there, each camera's eye runs, and a camera that sees you tells her where, as a report on
    // her infrastructure channel (the same one her own bolt-on cameras use): it lands on her
    // belief grid like any other evidence. Rooms without a camera, and anywhere a camera can't
    // see, tell her nothing. She has to be there in person: it's hands-on, and while she's at
    // the screens she isn't on the floor. It leaves you no chore.
    public sealed class CameraWatchTactic : Tactic
    {
        public override string Id => "camera_watch";
        public override bool IsHandsOn => true;
        public override string Title => "Camera watch";
        public override ActionId[] Actions => new[] { ActionId.Search };
        public override int Tier => 1;
        public override int IntroducedShift => 3;
        public override float TensionCost => 0.05f;
        public override float Cooldown => 120f;
        public override float PanicPrior => 0.03f;
        public override TellKind Tell => TellKind.MonitorHum;
        public override string Chore => "nothing to fix: keep out of the cameras' rooms, or out of their view, while she's at the screens";
        public override string Attacks => "sight";

        public const float WatchSeconds = 25f;   // how long she stays at the screens
        public const float StandWithin = 1.2f;
        public const float WatchReach = 3.5f;    // she watches from the desk: this near the wall of screens

        // What the last watch saw, for the log and the tests.
        public int LastSightings { get; private set; }

        public override bool Available(KarenContext c, out string why)
        {
            StoreCameras cams = StoreCameras.Instance;
            if (cams == null || cams.Cameras.Count == 0) { why = "the store has no cameras"; return false; }
            if (cams.Wall == null) { why = "no screens to watch them on"; return false; }
            if (cams.Broken) { why = "the cameras are broken"; return false; }
            if (cams.Watching) { why = "she's already at the screens"; return false; }
            why = null;
            return true;
        }

        public override bool EffectRunning(KarenContext c) => StoreCameras.Instance != null && StoreCameras.Instance.Watching;

        // Sitting with her back to the room: she's easy to walk up on, and easy to see.
        public override float ExposureRisk(KarenContext c) => 0.2f;

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            StoreCameras cams = StoreCameras.Instance;
            if (cams == null || cams.Wall == null) return;
            Vector3 screens = cams.Wall.transform.position;
            Vector3 stand = TacticHelpers.OnHerFloor(cams.Wall.StandPoint, 2.5f);
            plan.Target = "the office screens";
            plan.Go(stand, KarenBody.Pace.Walk, "walk to the screens in the office", StandWithin);
            plan.Add(new Tell(TellKind.MonitorHum, _ => screens, TellLead));
            plan.Add(new WatchCameras(this, _ => screens, WatchSeconds));
            plan.Go(TacticHelpers.Retreat(c), KarenBody.Pace.Walk, "leave the office", 2.5f);
        }

        // The watch itself: on while she stands at the screens, off the moment she leaves them,
        // however the step ends (done, cut short by a hunt, the shift ending).
        public sealed class WatchCameras : Primitive
        {
            readonly CameraWatchTactic owner;
            readonly System.Func<KarenContext, Vector3> screens;
            readonly float seconds;
            float t;
            int before;
            bool on;

            // Where her hands are. Her body, unless a test says otherwise.
            public System.Func<KarenContext, Vector3> Hands = k => k.Body.Position;

            public WatchCameras(CameraWatchTactic owner, System.Func<KarenContext, Vector3> screens, float seconds)
            {
                this.owner = owner;
                this.screens = screens;
                this.seconds = seconds;
                Label = $"watch the store's cameras for {seconds:0} s";
            }

            protected override void Begin(KarenContext c)
            {
                StoreCameras cams = StoreCameras.Instance;
                if (cams == null) return;
                // Rule 9's spirit: she watches from the desk, not from across the store.
                float d = FairnessGuard.Flat(Hands(c), screens(c));
                if (d > WatchReach + 0.05f)
                {
                    c.Think("CHECK", $"{d:0.0} m from the screens: too far to watch them");
                    return;
                }
                before = cams.Sightings;
                cams.SetWatching(true);
                on = cams.Watching;
                if (!on) return;
                c.Brain?.RecordEffect("watch the store's cameras from the office");
                if (c.Body != null) c.Body.TurnToward(screens(c));
            }

            protected override Status Tick(KarenContext c, float dt)
            {
                StoreCameras cams = StoreCameras.Instance;
                if (!on || cams == null || !cams.Watching) return Status.Failure;   // out of reach, or the cameras went dark
                t += dt;
                return t >= seconds ? Status.Success : Status.Running;
            }

            protected override void End(KarenContext c, bool aborted)
            {
                StoreCameras cams = StoreCameras.Instance;
                if (!on || cams == null) return;
                on = false;
                if (owner != null) owner.LastSightings = cams.Sightings - before;
                cams.SetWatching(false);
                c.Think("CHECK", $"the cameras showed you {cams.Sightings - before} time{(cams.Sightings - before == 1 ? "" : "s")} in {t:0} s");
            }
        }
    }

    // Camera break: she is the building, and she shorts the cameras out. All six go dark at
    // once: the office screens show static, for you and for her (no Camera watch until they're
    // fixed). You fix them at the camera box in the backstreet, by the breaker box: hold E for
    // twenty seconds. It's a chore like the fog's, so she doesn't start it once the doors have
    // closed, and Ambush can wait on your way to the box.
    public sealed class CameraBreakTactic : Tactic
    {
        public override string Id => "camera_break";
        public override bool LeavesChore => true;
        public override string Title => "Camera break";
        public override ActionId[] Actions => new[] { ActionId.Disrupt, ActionId.Corner };
        public override int Tier => 2;
        public override int IntroducedShift => 5;
        public override float TensionCost => 0.15f;
        public override float Cooldown => 240f;
        public override float PanicPrior => 0.1f;
        public override TellKind Tell => TellKind.CameraFizz;
        public override string Chore => "hold the camera box in the backstreet for 20 s";
        public override string Attacks => "sight";

        public override bool Available(KarenContext c, out string why)
        {
            StoreCameras cams = StoreCameras.Instance;
            if (cams == null || cams.Cameras.Count == 0) { why = "the store has no cameras"; return false; }
            if (cams.Broken) { why = "the cameras are already broken"; return false; }
            if (cams.Watching) { why = "she's watching them"; return false; }
            if (cams.Box == null) { why = "no camera box to fix them at"; return false; }
            why = null;
            return true;
        }

        public override bool EffectRunning(KarenContext c) => StoreCameras.Instance != null && StoreCameras.Instance.Broken;

        // You fix it at the camera box.
        public override bool FixAt(KarenContext c, out Vector3 at)
        {
            at = default;
            StoreCameras cams = StoreCameras.Instance;
            if (cams == null || !cams.Broken || cams.Box == null) return false;
            at = cams.Box.transform.position;
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            StoreCameras cams = StoreCameras.Instance;
            if (cams == null || cams.Cameras.Count == 0) return;
            // It fizzes at the camera nearest where she thinks you are: that's the one you'd hear.
            Vector3 believed = c.Belief != null && c.Map != null ? TacticHelpers.Believed(c) : cams.Cameras[0].transform.position;
            Vector3 fizz = cams.Cameras[0].transform.position;
            float best = float.MaxValue;
            foreach (StoreCamera cam in cams.Cameras)
            {
                if (cam == null) continue;
                float d = FairnessGuard.Flat(cam.transform.position, believed);
                if (d < best) { best = d; fizz = cam.transform.position; }
            }

            plan.Target = "the store's cameras";
            plan.Add(new Tell(TellKind.CameraFizz, _ => fizz, TellLead));
            plan.Add(new Effect("break the store's cameras", k =>
            {
                StoreCameras now = StoreCameras.Instance;
                if (now == null || !now.Break()) { k.Think("CHECK", "the cameras were already dark"); return; }
                if (k.Tasks != null) k.Tasks.Hint(TaskManager.TaskKind.FixCameras, string.Empty);
            }));
        }
    }
}
