using Kehai;
using Kehai.Karen;
using UnityEngine;
using UnityEngine.AI;

public class HingeDoor : MonoBehaviour
{
    [Header("Settings")]
    public float openAngle = 90f;  // How far the door swings open
    public float openSpeed = 3f;   // Swing speed
    public float interactRange = 3f; // Max distance to interact
    [Tooltip("Use the aimed interaction component instead of the nearby-E shortcut.")]
    public bool aimedInteraction;
    [Tooltip("Use the signed openAngle as an authored swing instead of choosing a side.")]
    public bool fixedSwingDirection;

    [Header("Audio")]
    public AudioClip openCreak;
    public AudioClip closeCreak;
    [Range(0f, 1f)] public float creakVolume = 0.8f;

    [Tooltip("A door left open this long counts as propped: its sensor stops reporting and " +
             "it no longer tells " + GameNames.Antagonist + " who passes (Karen.md §3.5).")]
    public float proppedAfter = 30f;

    private Quaternion closedRot;
    private Quaternion openRot;
    private bool isOpen = false;
    private bool isMoving = false;
    private Transform player;
    private float openedAt;
    private NavMeshObstacle lockCarve;

    public bool IsOpen => isOpen;
    public bool IsMoving => isMoving;
    bool locked;
    public DoorBlock Blocker { get; private set; }
    public bool Locked => locked || (Blocker != null && Blocker.IsBlocking);
    public BoxCollider Panel => panel;
    public bool Propped => isOpen && Time.time - openedAt > proppedAfter;

    // Every door in the loaded scene, for anything that has to open one on its way through.
    static readonly System.Collections.Generic.List<HingeDoor> all = new System.Collections.Generic.List<HingeDoor>();
    public static System.Collections.Generic.IReadOnlyList<HingeDoor> All => all;
    void OnEnable() => all.Add(this);
    void OnDisable()
    {
        all.Remove(this);
        if (Blocker != null) Blocker.Clear();
    }

    void Start()
    {
        closedRot = transform.rotation;
        var playerObject = GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null ? playerObject.transform : null;

        panel = FindPanel();
        closedCentre = panel != null ? panel.bounds.center : transform.position;
        if (HasWrapper()) wrapped = true;

        // An open door is an obstacle like any other: carve the panel out of the NavMesh
        // while it stands open, so paths — shoppers', Karen's, the eval agent's — go round it.
        if (panel != null)
        {
            openCarve = panel.gameObject.AddComponent<NavMeshObstacle>();
            openCarve.shape = NavMeshObstacleShape.Box;
            openCarve.center = panel.center;
            Vector3 size = panel.size;
            Vector3 scale = panel.transform.lossyScale;
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(size[i] * scale[i]) < 0.1f && Mathf.Abs(scale[i]) > 1e-5f) size[i] = 0.1f / Mathf.Abs(scale[i]);
            openCarve.size = size;
            openCarve.carving = true;
            openCarve.carveOnlyStationary = true;
            openCarve.enabled = false;
        }
    }

    BoxCollider panel;
    Vector3 closedCentre;
    NavMeshObstacle openCarve;

    // The widest solid collider that swings with the hinge is available during fixture
    // installation, which can run before Start initializes the motion state.
    internal BoxCollider FindPanel()
    {
        BoxCollider widest = null;
        foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>())
            if (box.enabled && !box.isTrigger && (widest == null || box.bounds.size.sqrMagnitude > widest.bounds.size.sqrMagnitude))
                widest = box;
        return widest;
    }

    void Update()
    {
        if (ListensForE && player != null)
        {
            // Check distance & E key press. Not while E is aimed at something else, or one
            // press would also work whatever is under the crosshair.
            float dist = Vector3.Distance(transform.position, player.position);
            bool aimedElsewhere = PlayerInteract.Local != null && PlayerInteract.Local.CurrentTarget != null;
            if (Input.GetKeyDown(KeyCode.E) && dist <= interactRange && !GamePause.Paused && !aimedElsewhere)
                Use(player.position, NoiseAuthor.Player);
        }
        StepSwing(Time.deltaTime);
    }

    // 0.5.1: the cold room's and stockroom's doors never opened. They carry a FurnitureDoor
    // (the aimed E target) and also listened for E themselves, so one press reached Use twice
    // in a frame: the first started the swing, the second turned it round (0.4.1 reversal).
    // A door with an aimed wrapper never listens for E itself, whatever the scene says.
    bool wrapped;
    public bool ListensForE => !aimedInteraction && !wrapped;
    public void MarkWrapped() => wrapped = true;

    bool HasWrapper()
    {
        foreach (FurnitureDoor use in GetComponentsInChildren<FurnitureDoor>(true))
            if (use != null && use.roomDoor == this) return true;
        foreach (FurnitureDoor use in GetComponentsInParent<FurnitureDoor>(true))
            if (use != null && use.roomDoor == this) return true;
        return false;
    }

    // Opens or closes the door for whoever stands at `from` — the E key, the eval's agent,
    // and Karen all come through here. Returns false if it's locked, or swinging for anyone
    // but the player: E on a swinging door turns it round (0.4.1), as the fridge and locker
    // doors do, so a door you opened by mistake can be shut again at once.
    public bool Use(Vector3 from, NoiseAuthor author)
    {
        // One press is one use. A second player Use in the same frame, with no swing step in
        // between, is the same press arriving by a second path: ignore it, or it reverses the
        // swing the first one started.
        if (author == NoiseAuthor.Player)
        {
            if (Time.frameCount == playerUseFrame && !steppedSinceUse) return lastPlayerUse;
            playerUseFrame = Time.frameCount;
            steppedSinceUse = false;
            lastPlayerUse = UseNow(from, author);
            return lastPlayerUse;
        }
        return UseNow(from, author);
    }

    int playerUseFrame = -1;
    bool steppedSinceUse = true;
    bool lastPlayerUse;

    bool UseNow(Vector3 from, NoiseAuthor author)
    {
        if (Blocker != null) Blocker.Refresh();
        if (isMoving)
        {
            if (author != NoiseAuthor.Player || Locked) return false;
            Reverse(author);
            return true;
        }
        if (Locked)
        {
            // Rattles against the lock or the box behind it.
            OneShotAudio.PlayAt(ProceduralAudio.Tell(TellKind.Clunk), transform.position, 0.6f);
            NoiseBus.Emit(transform.position, 0.45f, NoiseKind.Door, author);
            return false;
        }

        Toggle(from);

        // The door sensor reports the employee going through a door that isn't propped.
        // Karen's own passage isn't news to her.
        NoiseBus.Emit(transform.position, 0.4f, NoiseKind.Door, author);
        if (author == NoiseAuthor.Player && !Propped) GameEvents.RaiseDoorUsed(this, isOpen);
        return true;
    }

    public bool OpenFor(Vector3 from, NoiseAuthor author) => !isOpen && Use(from, author);

    // The nearest closed door within `range` that lies ahead along `heading` — what someone
    // walking that way is about to bump into.
    public static HingeDoor ClosedAhead(Vector3 position, Vector3 heading, float range)
    {
        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-4f) return null;
        heading.Normalize();
        HingeDoor best = null;
        float bestSqr = range * range;
        foreach (HingeDoor d in all)
        {
            if (d == null || d.isOpen || d.isMoving) continue;
            Vector3 to = d.closedCentre - position;
            to.y = 0f;
            float sqr = to.sqrMagnitude;
            if (sqr > bestSqr || Vector3.Dot(to.normalized, heading) < 0.2f) continue;
            best = d;
            bestSqr = sqr;
        }
        return best;
    }

    // The middle of the doorway (where the panel sits when shut), not the hinge.
    public Vector3 DoorCentre => closedCentre;

    void Toggle(Vector3 from)
    {
        if (!isOpen)
        {
            float angle = SwingAngleFor(from);
            openRot = Quaternion.Euler(closedRot.eulerAngles + new Vector3(0, angle, 0));
            openedAt = Time.time;
        }

        isOpen = !isOpen;
        BeginSwing(isOpen ? openRot : closedRot);

        // isOpen has already flipped, so it now describes the swing that just started.
        OneShotAudio.PlayAt(isOpen ? openCreak : closeCreak, transform.position, creakVolume);
    }

    // Mid-swing: the door turns round and goes back the way it came. An opening door closes;
    // a closing one opens again to the side it was already open on.
    void Reverse(NoiseAuthor author)
    {
        isOpen = !isOpen;
        if (isOpen) openedAt = Time.time;
        BeginSwing(isOpen ? openRot : closedRot);
        OneShotAudio.PlayAt(isOpen ? openCreak : closeCreak, transform.position, creakVolume);
        NoiseBus.Emit(transform.position, 0.4f, NoiseKind.Door, author);
        if (author == NoiseAuthor.Player && !Propped) GameEvents.RaiseDoorUsed(this, isOpen);
    }

    // The signed swing for someone opening the door from `from`. Away from them: of the two
    // ways the panel can swing, the one that ends up further from the opener. (The transform's
    // forward runs along the panel on these prefabs, so it can't say which side someone is on.)
    // If that way would leave the open leaf in a wall or fixed furniture and the other way is
    // clear, it opens the other way. The check only picks a side; it never refuses to open, and
    // the swing itself is not stopped by static geometry (as before 0.5.1). An authored
    // fixedSwingDirection keeps the signed openAngle; the store clears it at load
    // (KarenFixtures.TwoWayDoors), so every store door opens both ways.
    public float SwingAngleFor(Vector3 from)
    {
        if (fixedSwingDirection) return openAngle;
        Vector3 along = closedCentre - transform.position;
        along.y = 0f;
        Vector3 toOpener = from - closedCentre;
        toOpener.y = 0f;
        float plus = Vector3.Dot(Quaternion.Euler(0f, openAngle, 0f) * along, toOpener);
        float minus = Vector3.Dot(Quaternion.Euler(0f, -openAngle, 0f) * along, toOpener);
        float angle = plus < minus ? openAngle : -openAngle;
        if (!isOpen && !isMoving && SideBlocked(angle) && !SideBlocked(-angle)) angle = -angle;
        return angle;
    }

    const float PoseInset = 0.03f;      // all round: flush floor or ceiling contact is not a block
    const float HingeInset = 0.15f;     // the hinge end: the jamb and the wall it stands in
    readonly Collider[] poseOverlaps = new Collider[64];
    readonly System.Collections.Generic.HashSet<Collider> frame = new System.Collections.Generic.HashSet<Collider>();

    // Half-way and fully open: the sweep, not only where it ends.
    bool SideBlocked(float angle) => OpenPoseBlocked(angle * 0.5f) || OpenPoseBlocked(angle);

    // Would the leaf, swung to `angle`, sit in something fixed? Only valid while shut.
    public bool OpenPoseBlocked(float angle)
    {
        if (panel == null) return false;
        Vector3 hinge = transform.position;
        Vector3 centre = panel.transform.TransformPoint(panel.center);
        Quaternion rotation = panel.transform.rotation;
        Vector3 half = Vector3.Scale(panel.size, panel.transform.lossyScale);
        for (int i = 0; i < 3; i++) half[i] = Mathf.Abs(half[i]) * 0.5f;

        // The frame is whatever the shut leaf already touches: jambs, header, the wall it is
        // set in, the floor. Those belong to the doorway and never pick the side.
        frame.Clear();
        int touching = Physics.OverlapBoxNonAlloc(centre, half + Vector3.one * 0.02f, poseOverlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < touching; i++) frame.Add(poseOverlaps[i]);

        Quaternion delta = Quaternion.Euler(closedRot.eulerAngles + new Vector3(0f, angle, 0f)) * Quaternion.Inverse(transform.rotation);
        Vector3 openCentre = hinge + delta * (centre - hinge);
        Quaternion openRotation = delta * rotation;

        // Pull the box in all round, and further at the hinge end.
        Vector3 along = centre - hinge;
        along.y = 0f;
        int axis = 0;
        float best = -1f;
        for (int i = 0; i < 3; i++)
        {
            float d = along.sqrMagnitude < 1e-6f ? 0f : Mathf.Abs(Vector3.Dot(rotation * Unit(i), along.normalized));
            if (d > best) { best = d; axis = i; }
        }
        for (int i = 0; i < 3; i++) half[i] = Mathf.Max(0.001f, half[i] - PoseInset);
        float outward = Mathf.Sign(Vector3.Dot(rotation * Unit(axis), along));
        float trim = Mathf.Min(HingeInset, half[axis] - 0.001f) * 0.5f;
        half[axis] -= trim;
        openCentre += openRotation * Unit(axis) * (outward * trim);

        int count = Physics.OverlapBoxNonAlloc(openCentre, half, poseOverlaps, openRotation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider other = poseOverlaps[i];
            if (other == null || other.isTrigger || frame.Contains(other) || other.transform.IsChildOf(transform)) continue;
            if (Movable(other)) continue;
            return true;
        }
        return false;
    }

    static Vector3 Unit(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

    // People, carried or loose stock and other doors' leaves don't choose a side: the swing
    // goes round or into them as before.
    static bool Movable(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body != null && !body.isKinematic) return true;
        return other.GetComponentInParent<Item>() != null
            || other.GetComponentInParent<CharacterController>() != null
            || other.GetComponentInParent<NavMeshAgent>() != null
            || other.GetComponentInParent<HingeDoor>() != null;
    }

    void BeginSwing(Quaternion target)
    {
        swingTarget = target;
        isMoving = true;
        if (openCarve != null) openCarve.enabled = false;
    }

    Quaternion swingTarget;

    // One step of the swing. Update drives it; public so a test can. Static geometry doesn't
    // stop it (the door's own jambs touch the leaf at every angle); the side is chosen so the
    // open leaf is clear, and locks and door blocks stop it before it starts.
    public void StepSwing(float dt)
    {
        // A step separates two presses. In play mode only a later frame does, because Unity
        // runs this door's Update and PlayerInteract's in no fixed order.
        if (!Application.isPlaying || Time.frameCount != playerUseFrame) steppedSinceUse = true;
        if (!isMoving || GamePause.Paused) return;
        transform.rotation = Quaternion.Lerp(transform.rotation, swingTarget, openSpeed * dt);
        if (Quaternion.Angle(transform.rotation, swingTarget) > 0.1f) return;
        transform.rotation = swingTarget;
        isMoving = false;
        if (openCarve != null) openCarve.enabled = isOpen;
    }

    // A physical block has its own lifetime. Removing it must not cancel a separate lock.
    public bool AttachBlock(DoorBlock block)
    {
        if (block == null || Locked || isOpen || isMoving || panel == null) return false;
        Blocker = block;
        if (openCarve != null) openCarve.enabled = true;
        return true;
    }

    public void DetachBlock(DoorBlock block)
    {
        if (Blocker != block) return;
        Blocker = null;
        if (openCarve != null) openCarve.enabled = isOpen && !isMoving;
    }

    // Kept for legacy timed effects. Block door uses AttachBlock instead.
    public void SetLocked(bool locked)
    {
        if (this.locked == locked) return;
        this.locked = locked;

        if (locked && isOpen && !isMoving) Toggle(transform.position + transform.forward);

        if (locked)
        {
            if (lockCarve == null)
            {
                lockCarve = gameObject.AddComponent<NavMeshObstacle>();
                lockCarve.shape = NavMeshObstacleShape.Box;
                lockCarve.size = new Vector3(2f, 2.2f, 0.6f);
                lockCarve.center = new Vector3(0f, 1.1f, 0f);
                lockCarve.carving = true;
            }
            lockCarve.enabled = true;
        }
        else if (lockCarve != null)
        {
            lockCarve.enabled = false;
        }
    }

}
