using UnityEngine;

namespace Kehai.Sandbox
{
    // A plain hinged door for the sandboxes: E opens or closes it. The leaf is a solid box, so a
    // closed door blocks her eye like a wall; it swings over a fraction of a second.
    public sealed class SandboxDoor : HighlightInteractable, Kehai.Karen.IHidingDoor
    {
        public Transform leaf;
        public float openAngle = 95f;
        public float seconds = 0.35f;

        public bool IsOpen { get; private set; }
        // Karen's hide checks open it to look inside (0.5.2).
        public bool IsHidingDoor => true;
        public Vector3 DoorPoint => leaf != null ? leaf.position : transform.position;
        public void SetOpenForCheck(bool open) => Set(open);
        float t;   // 0 shut, 1 open

        public void Set(bool open, bool instantly = false)
        {
            IsOpen = open;
            if (instantly) { t = open ? 1f : 0f; Apply(); }
        }

        public override void Interact(PlayerInteract player) => Set(!IsOpen);
        public override string GetPrompt() => IsOpen ? "Close the door" : "Open the door";

        void Update()
        {
            float target = IsOpen ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.deltaTime / Mathf.Max(0.01f, seconds));
            Apply();
        }

        void Apply() => transform.localRotation = Quaternion.Euler(0f, openAngle * t, 0f) * Quaternion.identity;
    }
}
