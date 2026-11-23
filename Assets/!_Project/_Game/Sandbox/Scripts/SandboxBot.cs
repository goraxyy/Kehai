using System.Collections;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The bot that plays a sandbox's checks (0.5.2, C11.6): it drives the player through the same
    // input the game's own test bots use (PlayerMotor.externalInput), so it walks, crouches and
    // hides with the player's real body, speeds and collisions.
    public sealed class SandboxBot : IMotorInput
    {
        public Vector3 MoveWorld { get; set; }
        public bool Sprint { get; set; }
        public bool Crouch { get; set; }
        public float YawDelta { get; set; }
        public float PitchDelta { get; set; }
        public bool Jump { get; set; }

        readonly PlayerMotor motor;
        readonly CharacterController controller;

        public SandboxBot(PlayerMotor motor)
        {
            this.motor = motor;
            controller = motor.GetComponent<CharacterController>();
            motor.externalInput = this;
        }

        // Every frame of a check is 1/60 s of game time, as KehaiEnv does for the ablations. A
        // batch Unity runs frames uncapped (hundreds a second): each frame's step is then shorter
        // than the CharacterController's least move, so it's dropped and the bot crawls at a tenth
        // of walking pace. A fixed step also makes the checks the same on every computer.
        public const float FrameSeconds = 1f / 60f;

        public static void FixFrames() => Time.captureDeltaTime = FrameSeconds;
        public static void FreeFrames() => Time.captureDeltaTime = 0f;

        // The bottom of the player's body, whichever player it is: the sandbox's own has its origin
        // at its feet, the store's at the middle of its body (the island, C11.11).
        Vector3 FeetOffset => controller != null ? controller.center - Vector3.up * (controller.height * 0.5f) : Vector3.zero;
        public Vector3 Feet => motor.transform.position + FeetOffset;

        // Put the body somewhere at once (between checks, never during one).
        public void Teleport(Vector3 feet)
        {
            MoveWorld = Vector3.zero;
            controller.enabled = false;
            motor.transform.position = feet - FeetOffset + Vector3.up * 0.02f;
            controller.enabled = true;
            Physics.SyncTransforms();
        }

        // Walk to `target` on the floor, at walking pace, giving up after `seconds`.
        public IEnumerator WalkTo(Vector3 target, float seconds = 6f)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                Vector3 d = target - Feet;
                d.y = 0f;
                if (d.magnitude < 0.12f) break;
                MoveWorld = d.normalized * Mathf.Clamp01(d.magnitude / 0.5f + 0.2f);
                yield return null;
            }
            MoveWorld = Vector3.zero;
        }

        // Crouch or stand, and wait until the body has done it.
        public IEnumerator SetCrouch(bool crouched, float seconds = 1f)
        {
            Crouch = crouched;
            float until = Time.time + seconds;
            while (motor.IsCrouching != crouched && Time.time < until) yield return null;
        }
    }
}
