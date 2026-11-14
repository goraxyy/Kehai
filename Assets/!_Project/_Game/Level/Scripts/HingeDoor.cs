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

        // The panel is the widest solid collider that swings with the hinge.
        foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>())
            if (!box.isTrigger && (panel == null || box.bounds.size.sqrMagnitude > panel.bounds.size.sqrMagnitude)) panel = box;
        closedCentre = panel != null ? panel.bounds.center : transform.position;

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

    void Update()
    {
        if (!aimedInteraction && player != null)
        {
            // Check distance & E key press
            float dist = Vector3.Distance(transform.position, player.position);
            if (Input.GetKeyDown(KeyCode.E) && dist <= interactRange && !GamePause.Paused)
                Use(player.position, NoiseAuthor.Player);
        }
        StepSwing(Time.deltaTime);
    }

    // Opens or closes the door for whoever stands at `from` — the E key, the eval's agent,
    // and Karen all come through here. Returns false if it's locked, or swinging for anyone
    // but the player: E on a swinging door turns it round (0.4.1), as the fridge and locker
    // doors do, so a door you opened by mistake can be shut again at once.
    public bool Use(Vector3 from, NoiseAuthor author)
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
            // Open away from them: of the two ways the panel can swing, take the one that
            // ends up further from the opener. (The transform's forward runs along the panel
            // on these prefabs, so it can't say which side of the door someone is on.)
            Vector3 along = closedCentre - transform.position;
            along.y = 0f;
            Vector3 toOpener = from - closedCentre;
            toOpener.y = 0f;
            float plus = Vector3.Dot(Quaternion.Euler(0f, openAngle, 0f) * along, toOpener);
            float minus = Vector3.Dot(Quaternion.Euler(0f, -openAngle, 0f) * along, toOpener);
            float angle = fixedSwingDirection ? openAngle : plus < minus ? openAngle : -openAngle;
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

    void BeginSwing(Quaternion target)
    {
        swingTarget = target;
        isMoving = true;
        if (openCarve != null) openCarve.enabled = false;
    }

    Quaternion swingTarget;

    // One step of the swing. Update drives it; public so a test can.
    public void StepSwing(float dt)
    {
        if (!isMoving) return;
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
