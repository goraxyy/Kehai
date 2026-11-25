using UnityEngine;
using Kehai.Karen;

// Aimed interaction: opening one locker never toggles the lockers beside it.
// Door leaves remain solid and stop if a player or other object is in their sweep.
public sealed class FurnitureDoor : HighlightInteractable, IHidingDoor
{
    public string label = "locker";
    public float openAngle = 105f;
    public float degreesPerSecond = 130f;
    public Transform assembly;
    public BoxCollider panel;
    public HingeDoor roomDoor;
    public bool IsOpen => roomDoor != null ? roomDoor.IsOpen : wantsOpen;
    public bool IsFullyOpen => roomDoor != null ? roomDoor.IsOpen : Mathf.Abs(angle - chosenOpenAngle) < 2f;
    // Karen's hide checks open it to look inside (0.5.2).
    public bool IsHidingDoor => roomDoor == null;
    public Vector3 DoorPoint => panel != null ? panel.bounds.center : transform.position;
    public float LeafWidth => panel != null ? Mathf.Max(panel.bounds.size.x, panel.bounds.size.z) : 0.6f;
    public float OpenFraction => roomDoor != null ? (roomDoor.IsOpen ? 1f : 0f)
                               : Mathf.Abs(chosenOpenAngle) > 1f ? Mathf.Clamp01(Mathf.Abs(angle / chosenOpenAngle)) : 0f;
    public void SetOpenForCheck(bool open) => SetOpen(open);

    Quaternion closed;
    float angle;
    float chosenOpenAngle;
    bool wantsOpen;
    readonly Collider[] overlaps = new Collider[32];

    protected override void Awake()
    {
        base.Awake();
        EnsureReady();
        if (roomDoor != null) roomDoor.MarkWrapped();   // the room door hears E through this, only
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

    bool ready;
    void EnsureReady()
    {
        if (ready) return;
        ready = true;
        closed = transform.localRotation;
        chosenOpenAngle = openAngle;
        if (assembly == null) assembly = transform.parent;
    }

    public override void Interact(PlayerInteract player)
    {
        if (roomDoor != null)
        {
            roomDoor.MarkWrapped();
            roomDoor.Use(player.transform.position, NoiseAuthor.Player);
        }
        else
        {
            if (GetComponent<FreezerAlarm>() is FreezerAlarm alarm && alarm.Ringing) alarm.Close();
            else UseFrom(player.transform.position);
        }
    }
    public override string GetPrompt() =>
        roomDoor != null && roomDoor.Blocker != null && roomDoor.Blocker.IsBlocking ? roomDoor.Blocker.PromptFor(PlayerInteract.Local)
        : roomDoor != null && roomDoor.Locked ? "Locked" : (IsOpen ? "Close " : "Open ") + label;
    public void SetOpen(bool open) { if (roomDoor == null) { EnsureReady(); wantsOpen = open; } }

    // E on a furniture leaf. It opens outward, the way its openAngle was authored, whichever
    // side the opener stands: away from the opener would swing a cabinet, fridge or locker leaf
    // into its own cabinet. Only if the outward pose is blocked (a wall, a neighbour) and the
    // inward one is clear does it open the other way. A leaf whose inward side is its own
    // cabinet has no two-way clearance, so it stays one-way: that is the geometry, not an
    // exception (0.5.1 item 11). E mid-swing turns it round: opening -> closing, and closing ->
    // opening again to the same side, from the angle it has reached.
    public void UseFrom(Vector3 from)
    {
        EnsureReady();
        if (!wantsOpen && Mathf.Abs(angle) < .1f && panel != null) chosenOpenAngle = ChooseSide();
        wantsOpen = !wantsOpen;
    }

    float ChooseSide()
    {
        float outward = openAngle;
        if (SideBlocked(outward) && !SideBlocked(-outward)) return -outward;
        return outward;
    }

    // Half-way and fully open: a leaf hinged at its cabinet's corner ends a 105-degree inward
    // swing outside the cabinet, but passes through it on the way.
    bool SideBlocked(float degrees) => PoseBlocked(degrees * 0.5f) || PoseBlocked(degrees);

    const float PoseInset = 0.03f, HingeInset = 0.1f;

    // Would the leaf, fully open at `degrees`, sit in something fixed? Unlike the swing check
    // below, its own cabinet counts here: a pose inside the cabinet's bounds is blocked even
    // where the cabinet is hollow (the cold hiding cabinets).
    bool PoseBlocked(float degrees)
    {
        Quaternion parent = transform.parent == null ? Quaternion.identity : transform.parent.rotation;
        Quaternion shutWorld = parent * closed;
        Quaternion fromShut = parent * closed * Quaternion.Euler(0f, degrees, 0f) * Quaternion.Inverse(shutWorld);
        Quaternion toShut = shutWorld * Quaternion.Inverse(transform.rotation);   // the leaf may be part-open now
        Vector3 hinge = transform.position;
        Vector3 shut = hinge + toShut * (panel.transform.TransformPoint(panel.center) - hinge);
        Quaternion shutRotation = toShut * panel.transform.rotation;

        Vector3 half = Vector3.Scale(panel.size, panel.transform.lossyScale);
        for (int i = 0; i < 3; i++) half[i] = Mathf.Abs(half[i]) * 0.5f;
        Vector3 along = shut - hinge;
        int axis = 0;
        float best = -1f;
        for (int i = 0; i < 3; i++)
        {
            float d = along.sqrMagnitude < 1e-6f ? 0f : Mathf.Abs(Vector3.Dot(shutRotation * Unit(i), along.normalized));
            if (d > best) { best = d; axis = i; }
        }
        // Pulled in all round, and further at the hinge end: flush contact is not a block.
        for (int i = 0; i < 3; i++) half[i] = Mathf.Max(.001f, half[i] - PoseInset);
        float outward = Mathf.Sign(Vector3.Dot(shutRotation * Unit(axis), along));
        float trim = Mathf.Min(HingeInset, half[axis] - .001f) * 0.5f;
        half[axis] -= trim;

        Quaternion openRotation = fromShut * shutRotation;
        Vector3 openCentre = hinge + fromShut * (shut - hinge) + openRotation * Unit(axis) * (outward * trim);

        if (assembly != null && AssemblyBounds(out Bounds cabinet) && cabinet.Contains(openCentre)) return true;

        int count = Physics.OverlapBoxNonAlloc(openCentre, half, overlaps, openRotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider other = overlaps[i];
            if (other == null || other.isTrigger || other == panel || other.transform.IsChildOf(transform)) continue;
            Rigidbody body = other.attachedRigidbody;
            if (body != null && !body.isKinematic) continue;
            if (other.GetComponentInParent<Item>() != null || other.GetComponentInParent<CharacterController>() != null
                || other.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) continue;
            return true;
        }
        return false;
    }

    // The cabinet's own solid body, without this leaf.
    bool AssemblyBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Collider c in assembly.GetComponentsInChildren<Collider>())
        {
            if (c == null || c.isTrigger || !c.enabled || c == panel || c.transform.IsChildOf(transform)) continue;
            if (any) bounds.Encapsulate(c.bounds); else { bounds = c.bounds; any = true; }
        }
        return any;
    }

    static Vector3 Unit(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    void Update() => StepSwing(Time.deltaTime);

    public void StepSwing(float dt)
    {
        if (roomDoor != null || GamePause.Paused || panel == null) return;
        EnsureReady();
        AdvanceSwing(degreesPerSecond * dt);
    }

#if UNITY_EDITOR
    // Measure the stock with the leaf in the pose its ordinary swing can reach. The audit
    // runs in a paused store. Restore even a partly open leaf before measuring the next bay.
    public void InspectOpenPose(Vector3 opener, System.Action measure)
    {
        EnsureReady();
        var rotation = transform.localRotation;
        float priorAngle = angle, priorTarget = chosenOpenAngle;
        bool priorOpen = wantsOpen;
        try
        {
            if (!wantsOpen) UseFrom(opener);
            if (degreesPerSecond > 0f) AdvanceSwing(Mathf.Abs(chosenOpenAngle - angle));
            Physics.SyncTransforms();
            measure();
        }
        finally
        {
            angle = priorAngle;
            chosenOpenAngle = priorTarget;
            wantsOpen = priorOpen;
            transform.localRotation = rotation;
            Physics.SyncTransforms();
        }
    }
#endif

    void AdvanceSwing(float degrees)
    {
        float target = wantsOpen ? chosenOpenAngle : 0f;
        float remaining = Mathf.Min(Mathf.Abs(target - angle), degrees);
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
