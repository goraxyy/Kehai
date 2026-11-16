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
    public bool IsFullyOpen => roomDoor != null ? roomDoor.IsOpen : Mathf.Abs(angle - chosenOpenAngle) < 2f;

    Quaternion closed;
    float angle;
    float chosenOpenAngle;
    bool wantsOpen;
    readonly Collider[] overlaps = new Collider[32];

    protected override void Awake()
    {
        base.Awake();
        closed = transform.localRotation;
        chosenOpenAngle = openAngle;
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
        else
        {
            if (GetComponent<FreezerAlarm>() is FreezerAlarm alarm && alarm.Ringing) alarm.Close();
            else UseFrom(player.transform.position);
        }
    }
    public override string GetPrompt() =>
        roomDoor != null && roomDoor.Blocker != null && roomDoor.Blocker.IsBlocking ? roomDoor.Blocker.PromptFor(PlayerInteract.Local)
        : roomDoor != null && roomDoor.Locked ? "Locked" : (IsOpen ? "Close " : "Open ") + label;
    public void SetOpen(bool open) { if(roomDoor==null)wantsOpen=open; }

    public void UseFrom(Vector3 from)
    {
        if (!wantsOpen && Mathf.Abs(angle) < .1f && panel != null)
        {
            Vector3 along = panel.bounds.center - transform.position;
            Vector3 toOpener = from - panel.bounds.center;
            float magnitude = Mathf.Abs(openAngle);
            chosenOpenAngle = Vector3.Dot(Quaternion.AngleAxis(magnitude, transform.up) * along, toOpener)
                < Vector3.Dot(Quaternion.AngleAxis(-magnitude, transform.up) * along, toOpener) ? magnitude : -magnitude;
        }
        wantsOpen = !wantsOpen;
    }

    void Update() => StepSwing(Time.deltaTime);

    public void StepSwing(float dt)
    {
        if (roomDoor != null || GamePause.Paused || panel == null) return;
        float target = wantsOpen ? chosenOpenAngle : 0f;
        float remaining = Mathf.Min(Mathf.Abs(target - angle), degreesPerSecond * dt);
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
        for (int i = 0; i < 3; i++) size[i] = Mathf.Max(.001f, Mathf.Abs(size[i]) * .5f - .005f);
        Quaternion panelRotation = worldRotation * Quaternion.Inverse(transform.rotation) * panel.transform.rotation;
        // Inset the query: a leaf resting flush above the floor must not block itself forever.
        int count = Physics.OverlapBoxNonAlloc(center, size,
            overlaps, panelRotation, ~0, QueryTriggerInteraction.Ignore);
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
