using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // Karen in a sandbox (0.5.2, Phase 11, C11.2): her real body and her real eye, with the
    // game's own settings (KarenBody.Build, which calls KarenBody.ConfigureEye), and no brain.
    // In "walk and see" she has no tricks, no hunt, no Director and no catch: she walks her
    // route, or stands and turns, and her eye works exactly as it does in the store. Nothing
    // here reads where the player is; the meters read her eye.
    public sealed class SandboxKaren : MonoBehaviour
    {
        public enum State { Frozen, Moving }
        public enum Route { Loop, StandAndTurn }

        public KarenBody Body { get; private set; }
        public KarenConfig Config { get; private set; }
        public State Current { get; private set; } = State.Frozen;
        public Route CurrentRoute { get; private set; } = Route.Loop;
        public Vector3 Spot { get; private set; }
        public float SpotYaw { get; private set; }
        public Vector3[] Loop { get; private set; } = new Vector3[0];
        public int NextWaypoint { get; private set; }

        // Stand and turn: a quarter turn every few seconds, so she sweeps the room.
        public const float TurnEvery = 3f;
        float turnTimer;
        int facing;

        public static SandboxKaren Build(Transform parent, Vector3 spot, float yaw, Vector3[] loop, KarenConfig config = null)
        {
            config = config ?? new KarenConfig();
            KarenBody body = KarenBody.Build(parent, config, spot);
            body.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var karen = body.gameObject.AddComponent<SandboxKaren>();
            karen.Body = body;
            karen.Config = config;
            karen.Spot = body.transform.position;
            karen.SpotYaw = yaw;
            karen.Loop = loop ?? new Vector3[0];
            karen.Freeze();
            return karen;
        }

        public void StartMoving()
        {
            Current = State.Moving;
            turnTimer = 0f;
            if (CurrentRoute == Route.Loop) GoToNext();
        }

        // She stops where she is. Her eye keeps working, so you can read the meters.
        public void Freeze()
        {
            Current = State.Frozen;
            if (Body != null) Body.Stop();
        }

        // Back to her spot and frozen, with her awareness and her memory of any hiding cleared.
        public void Restart()
        {
            Freeze();
            if (Body == null) return;
            if (Body.Agent != null && Body.Agent.isActiveAndEnabled) Body.Warp(Spot);
            else Body.transform.position = Spot;
            Face(SpotYaw);
            Body.Sight.ResetAwareness();
            NextWaypoint = 0;
            facing = 0;
            turnTimer = 0f;
        }

        // For the scripted checks: put her somewhere else, facing a given way, frozen and cleared.
        public void PlaceAt(Vector3 at, float yaw)
        {
            Freeze();
            if (Body == null) return;
            if (Body.Agent != null && Body.Agent.isActiveAndEnabled) Body.Warp(at);
            else Body.transform.position = at;
            Face(yaw);
            Body.Sight.ResetAwareness();
        }

        // Face `yaw`, and keep facing it: her body turns towards the last thing she turned to (her
        // personal-space turn, KarenBody.KeepPersonalSpace), so that is pointed this way too.
        void Face(float yaw)
        {
            Body.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Body.TurnToward(Body.transform.position + Body.transform.forward * 5f);
        }

        public void ToggleRoute()
        {
            CurrentRoute = CurrentRoute == Route.Loop ? Route.StandAndTurn : Route.Loop;
            if (Current == State.Moving) StartMoving();
        }

        void Update() => Step(Time.deltaTime);

        // One frame: move (if moving), then her eye. Public so the tests can step it.
        public void Step(float dt)
        {
            if (Body == null) return;
            if (Current == State.Moving)
            {
                if (CurrentRoute == Route.Loop) Walk();
                else Turn(dt);
            }
            Body.Sight.Tick(dt);
            ShowMood();
        }

        void Walk()
        {
            if (Loop.Length == 0) return;
            if (Body.Arrived(0.5f) || Body.PathFailed)
            {
                NextWaypoint = (NextWaypoint + 1) % Loop.Length;
                GoToNext();
            }
        }

        void GoToNext()
        {
            if (Loop.Length == 0) return;
            Body.MoveTo(Loop[NextWaypoint], KarenBody.Pace.Walk);
        }

        void Turn(float dt)
        {
            turnTimer += dt;
            if (turnTimer < TurnEvery) return;
            turnTimer = 0f;
            facing = (facing + 1) % 4;
            Vector3 dir = Quaternion.Euler(0f, SpotYaw + facing * 90f, 0f) * Vector3.forward;
            Body.TurnToward(Body.transform.position + dir * 5f);
        }

        // Her light, and the floor cone with it, shows what her eye is doing: calm, noticing, sure.
        void ShowMood()
        {
            SightSensor eye = Body.Sight;
            KarenBody.Mood mood = eye.Awareness >= eye.confirmAt ? KarenBody.Mood.Hunt
                : eye.Awareness >= eye.glimpseAt ? KarenBody.Mood.Alert : KarenBody.Mood.Calm;
            if (Body.CurrentMood != mood) Body.SetMood(mood);
        }
    }
}
