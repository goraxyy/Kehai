using UnityEngine;
using Kehai.Karen;

// Aimed interaction: opening one locker never toggles the lockers beside it.
// Door leaves remain solid and stop if a player or other object is in their sweep.
public sealed class FurnitureDoor : HighlightInteractable
{
    public string label = "locker";
    public float openAngle = 105f;
    public float degreesPerSecond = 130f;
    public Transform assembly;
    public BoxCollider panel;
    public HingeDoor roomDoor;
    public bool IsOpen => roomDoor != null ? roomDoor.IsOpen : wantsOpen;

    Quaternion closed;
    float angle;
    bool wantsOpen;
    readonly Collider[] overlaps = new Collider[32];

    protected override void Awake()
    {
        base.Awake();
        closed = transform.localRotation;
        if (assembly == null) assembly = transform.parent;
        if (roomDoor == null && panel != null)
        {
            var obstacle = panel.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.center = panel.center;
            obstacle.size = new Vector3(panel.size.x, panel.size.y, Mathf.Max(.1f, panel.size.z));
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }
    }

    public override void Interact(PlayerInteract player)
    {
        if (roomDoor != null) roomDoor.Use(player.transform.position, NoiseAuthor.Player);
        else wantsOpen = !wantsOpen;
    }
    public override string GetPrompt() => roomDoor != null && roomDoor.Locked ? "Locked" : (IsOpen ? "Close " : "Open ") + label;

    void Update()
    {
        if (roomDoor != null || GamePause.Paused || panel == null) return;
        float target = wantsOpen ? openAngle : 0f;
        float remaining = Mathf.Min(Mathf.Abs(target - angle), degreesPerSecond * Time.deltaTime);
        // Subdivide even after a slow frame so a leaf cannot skip through the player.
        while (remaining > 0.001f)
        {
            float step = Mathf.Min(remaining, 2f);
            float candidate = Mathf.MoveTowards(angle, target, step);
            Quaternion rotation = closed * Quaternion.Euler(0f, candidate, 0f);
            if (Blocked(rotation)) break;
            angle = candidate;
            transform.localRotation = rotation;
            remaining -= step;
        }
    }

    bool Blocked(Quaternion localRotation)
    {
        Quaternion worldRotation = transform.parent == null ? localRotation : transform.parent.rotation * localRotation;
        Vector3 scale = transform.lossyScale;
        Vector3 centerInHinge = transform.InverseTransformPoint(panel.transform.TransformPoint(panel.center));
        Vector3 center = transform.position + worldRotation * Vector3.Scale(centerInHinge, scale);
        Vector3 size = Vector3.Scale(panel.size, panel.transform.lossyScale);
        int count = Physics.OverlapBoxNonAlloc(center, size * 0.5f + Vector3.one * 0.008f,
            overlaps, worldRotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return true;
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (other == panel || other.transform.IsChildOf(transform)) continue;
            if (assembly != null && other.transform.IsChildOf(assembly)) continue;
            return true;
        }
        return false;
    }
}
