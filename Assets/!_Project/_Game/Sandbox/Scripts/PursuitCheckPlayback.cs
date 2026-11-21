using Kehai.Replay;
using UnityEngine;

namespace Kehai.Sandbox
{
    // Supplies the generated room to the standard replay player and shot renderer.
    public sealed class PursuitCheckPlayback : MonoBehaviour
    {
        public const string SceneId = "Pursuit sandbox";

        public string recording;

        void Awake()
        {
            transform.position = PursuitSandbox.Centre;
            PursuitSandbox.BuildRoom(transform, out var fridge, out _);
            SandboxDoor door = fridge.transform.parent.GetComponentInChildren<SandboxDoor>();
            // ReplayStage binds its Door entities to HingeDoors by their recorded position.
            // Attach an inactive pose target to the hinge without enabling game interaction.
            door.gameObject.AddComponent<HingeDoor>().enabled = false;
            ReplayPlayer.Install(recording);
        }
    }
}
