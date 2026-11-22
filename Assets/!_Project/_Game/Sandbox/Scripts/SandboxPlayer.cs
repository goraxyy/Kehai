using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The player, built in code for a sandbox: the game's own movement (PlayerMotor: walk,
    // sprint, crouch, look), the game's own presence (PlayerPresence: what her senses read) and
    // the game's own hands (PlayerInteract and CarrySlot: E on the buttons and doors). Sizes are
    // the store's: a body 1 m wide and 2 m tall, eyes at 1.6 m, 0.8 m crouched.
    public static class SandboxPlayer
    {
        public const float Radius = 0.5f, Height = 2f;

        public static PlayerPresence Build(Transform parent, Vector3 feet, float yaw)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(parent, false);
            go.transform.position = feet + Vector3.up * 0.02f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.tag = "Player";
            // Inactive while it's put together, so every Awake sees its parts.
            go.SetActive(false);

            var controller = go.AddComponent<CharacterController>();
            controller.radius = Radius;
            controller.height = Height;
            controller.center = Vector3.up * Height * 0.5f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.08f;

            var cameraRoot = new GameObject("CameraRoot");
            cameraRoot.transform.SetParent(go.transform, false);
            var camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            camera.transform.SetParent(cameraRoot.transform, false);
            Camera view = camera.AddComponent<Camera>();
            view.nearClipPlane = 0.05f;
            view.fieldOfView = 70f;
            camera.AddComponent<AudioListener>();

            var hold = new GameObject("HoldPoint");
            hold.transform.SetParent(camera.transform, false);
            hold.transform.localPosition = new Vector3(0.35f, -0.3f, 0.6f);
            var stash = new GameObject("StashPoint");
            stash.transform.SetParent(go.transform, false);
            stash.SetActive(false);

            var motor = go.AddComponent<PlayerMotor>();
            motor.cameraRoot = cameraRoot.transform;
            cameraRoot.transform.localPosition = new Vector3(0f, motor.standCameraY, 0f);

            var carry = go.AddComponent<CarrySlot>();
            carry.holdPoint = hold.transform;
            carry.stashPoint = stash.transform;

            var interact = go.AddComponent<PlayerInteract>();
            interact.rayOrigin = camera.transform;
            interact.interactLayer = ~0;

            var presence = go.AddComponent<PlayerPresence>();
            go.SetActive(true);
            return presence;
        }
    }
}
