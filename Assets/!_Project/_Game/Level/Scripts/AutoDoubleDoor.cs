using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;
using UnityEngine.AI;

public class AutoDoubleDoor : MonoBehaviour
{
    public float slideDistance = 2f;
    public float slideSpeed = 3f;
    public float closeDelay = 2f;

    [Header("Audio")]
    [Tooltip("Shop chime, once each time the doors start opening.")]
    public AudioClip openChime;
    [Range(0f, 1f)] public float chimeVolume = 0.85f;

    private Transform leftDoor, rightDoor;
    private Vector3 leftClosed, rightClosed;
    private Vector3 leftOpen, rightOpen;
    private bool isOpen = false;
    readonly HashSet<Collider> occupants = new HashSet<Collider>();
    float closeAt = float.PositiveInfinity;

    public bool IsOpen => isOpen;

    // Every automatic door in the loaded scene, for whoever has to have one opened ahead of them.
    static readonly List<AutoDoubleDoor> all = new List<AutoDoubleDoor>();
    public static IReadOnlyList<AutoDoubleDoor> All => all;
    void OnEnable()
    {
        if (!all.Contains(this)) all.Add(this);
        PowerSystem.PowerChanged -= PowerChanged;
        PowerSystem.PowerChanged += PowerChanged;
    }

    // ---- power (0.5.1 item 6) ------------------------------------------------------------------
    //
    // The doors run on the mains. In a blackout nothing powers them: the sensor, Karen's ask
    // and the phantom cycle are all refused, and the panels stop where they are. Shut dead
    // panels are carved out of the NavMesh so shoppers and Karen path round them instead of
    // walking through glass; the carving goes when the power comes back. The player can slide
    // a dead pair open by hand (no chime, no sensor report), because the lobby and the WC have
    // no other way out: four dead doors would otherwise seal them away from the breaker
    // (fairness rule 6). On restoration the sensor is read again, since a trigger doesn't send
    // Enter twice for someone already standing in it.

    readonly List<NavMeshObstacle> powerObstacles = new List<NavMeshObstacle>();
    readonly List<AutoDoorHandle> handles = new List<AutoDoorHandle>();
    bool powerWasOn = true;
    bool handOpen;

    public const float HandSpeed = 0.8f;        // m/s, panels slid by hand
    public bool HandOpened => handOpen;
    public IReadOnlyList<AutoDoorHandle> Handles => handles;

    void PowerChanged(bool powered)
    {
        powerWasOn = powered;
        karenUntil = -1f;
        handOpen = false;
        foreach (var obstacle in powerObstacles) if (obstacle != null) obstacle.enabled = !powered;
        if (powered)
        {
            occupants.Clear();
            // Panels left part-open (stopped mid-slide, or slid by hand) close in the usual way.
            bool ajar = Openness > 0.01f;
            isOpen = ajar;
            closeAt = ajar ? Time.time + closeDelay : float.PositiveInfinity;
            if (TryGetComponent(out BoxCollider sensor))
            {
                Vector3 size = Vector3.Scale(sensor.size, sensor.transform.lossyScale);
                foreach (Collider other in Physics.OverlapBox(sensor.transform.TransformPoint(sensor.center),
                    size * .5f, sensor.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
                    Arrive(other);
            }
        }
        RefreshHandles();
    }

    void AddPowerObstacles(Transform leaf)
    {
        foreach (BoxCollider panel in leaf.GetComponentsInChildren<BoxCollider>())
        {
            if (panel.isTrigger) continue;
            var obstacle = panel.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = panel.center;
            Vector3 size = panel.size;
            Vector3 scale = panel.transform.lossyScale;
            for (int i = 0; i < 3; i++)
                if (Mathf.Abs(size[i] * scale[i]) < 0.1f && Mathf.Abs(scale[i]) > 1e-5f) size[i] = 0.1f / Mathf.Abs(scale[i]);
            obstacle.size = size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;   // follows panels slid by hand
            obstacle.enabled = !PowerSystem.PowerOn;
            powerObstacles.Add(obstacle);
        }
    }

    // An E target over each panel, there only while the power is off and the pair is shut.
    void AddHandles(Transform leaf)
    {
        int layer = LayerMask.NameToLayer("Interactable");
        foreach (BoxCollider panel in leaf.GetComponentsInChildren<BoxCollider>())
        {
            if (panel.isTrigger || panel.GetComponent<AutoDoorHandle>() != null) continue;
            var go = new GameObject("Hand push (no power)");
            if (layer >= 0) go.layer = layer;
            go.transform.SetParent(panel.transform, false);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;                  // the interact ray hits triggers; sight ignores them
            box.center = panel.center;
            Vector3 size = panel.size;
            Vector3 scale = panel.transform.lossyScale;
            // A little proud of the glass both sides, so the aim finds it before the panel.
            for (int i = 0; i < 3; i++) if (Mathf.Abs(scale[i]) > 1e-5f) size[i] += 0.08f / Mathf.Abs(scale[i]);
            box.size = size;
            var handle = go.AddComponent<AutoDoorHandle>();
            handle.Door = this;
            handles.Add(handle);
        }
    }

    void RefreshHandles()
    {
        bool usable = !PowerSystem.PowerOn && !handOpen && Openness < 0.99f;
        foreach (var handle in handles) if (handle != null) handle.gameObject.SetActive(usable);
    }

    // E on a dead pair: slide it open by hand. No power, so no chime and no sensor report
    // to the store, but glass in its runners is not silent. Open-only; it shuts again once the
    // power is back and nobody is in the way. False while powered, already pushed, or open.
    public bool PushByHand()
    {
        if (PowerSystem.PowerOn || leftDoor == null || rightDoor == null) return false;
        if (powerWasOn) PowerChanged(false);
        if (handOpen || Openness >= 0.99f) return false;
        handOpen = true;
        NoiseBus.Emit(transform.position, 0.3f, NoiseKind.Door, NoiseAuthor.Player);
        RefreshHandles();
        return true;
    }

    // How far ahead the sensor reaches for someone moving at `speed`: far enough that the panels
    // (which take slideDistance / slideSpeed seconds) are open by the time they arrive. Karen
    // hunts at 7.5 m/s, and the sensor box alone gives her a third of a second (0.4.1).
    public float ReachFor(float speed) => Mathf.Max(SensorReach, speed * (slideDistance / Mathf.Max(0.1f, slideSpeed) + 0.15f));
    public const float SensorReach = 2.5f;      // the sensor box: 2.5 m either side of the panels

    // How open the panels are, 0 shut to 1 fully open.
    public float Openness => leftDoor == null || slideDistance <= 0f ? (isOpen ? 1f : 0f)
        : Mathf.Clamp01(Vector3.Distance(leftDoor.position, leftClosed) / slideDistance);

    // The nearest automatic door someone at `position`, moving along `velocity`, is about to
    // reach: within its reach for that speed, and ahead of them. None in a blackout: a dead
    // door won't open for anyone, so there is nothing to ask or wait for (Karen used to stand
    // at the glass for good); where its panels are shut, the carving routes her round.
    public static AutoDoubleDoor Ahead(Vector3 position, Vector3 velocity)
    {
        if (!PowerSystem.PowerOn) return null;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        if (speed < 0.05f) return null;
        Vector3 heading = velocity / speed;
        AutoDoubleDoor best = null;
        float bestSqr = float.MaxValue;
        foreach (AutoDoubleDoor d in all)
        {
            if (d == null) continue;
            Vector3 to = d.transform.position - position;
            to.y = 0f;
            float sqr = to.sqrMagnitude;
            float reach = d.ReachFor(speed);
            if (sqr > reach * reach || sqr >= bestSqr) continue;
            if (sqr > 0.25f && Vector3.Dot(to.normalized, heading) < 0.2f) continue;
            best = d;
            bestSqr = sqr;
        }
        return best;
    }

    // Karen at the doors (0.4.1). She used to walk through the shut panels: her only collider
    // is her touch trigger, and the sensor ignores triggers. Now her body asks as it nears, the
    // doors open for her as for anyone, chime, and stay open until she's through. The noise is
    // authored hers, so her own ears don't take it for you. False if they were already open,
    // or the power is off.
    public bool OpenForKaren()
    {
        if (!PowerSystem.PowerOn) return false;
        karenUntil = Time.time + KarenHold;
        closeAt = float.PositiveInfinity;
        if (isOpen) return false;
        isOpen = true;
        OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);
        NoiseBus.Emit(transform.position, 0.55f, NoiseKind.AutoDoor, NoiseAuthor.Karen);
        return true;
    }

    public const float KarenHold = 0.6f;        // seconds the doors stay held after her last ask
    float karenUntil = -1f;
    bool HeldForKaren => Time.time < karenUntil;
    public Transform LeftPanel => leftDoor;     // the sliding panels, for the replay recorder
    public Transform RightPanel => rightDoor;

    void Start()
    {
        // A sensor must cover both approaches, including the space between the panels.
        if (TryGetComponent(out BoxCollider sensor))
        {
            sensor.isTrigger = true;
            sensor.center = new Vector3(0f, 1.25f, 0f);
            sensor.size = new Vector3(4f, 2.5f, 5f);
        }
        // Auto-find doors by name — no Inspector dragging needed
        leftDoor = SceneObjectNames.Child(transform, "LeftDoor");
        rightDoor = SceneObjectNames.Child(transform, "RightDoor");

        if (leftDoor == null || rightDoor == null)
        {
            Debug.LogError("LeftDoor or RightDoor not found! Check names in Hierarchy.", this);
            return;
        }

        // Use WORLD position, not local
        leftClosed = leftDoor.position;
        rightClosed = rightDoor.position;
        leftOpen = leftClosed + transform.right * slideDistance;
        rightOpen = rightClosed + transform.right * -slideDistance;
        AddPowerObstacles(leftDoor);
        AddPowerObstacles(rightDoor);
        AddHandles(leftDoor);
        AddHandles(rightDoor);
        powerWasOn = PowerSystem.PowerOn;
        RefreshHandles();
    }

    void Update() => StepMotion(Time.deltaTime);

    // One frame of the panels. Update drives it; public so a test can.
    public void StepMotion(float dt)
    {
        // The event can be missed (disabled at the time, or a test setting the flag), so the
        // flag itself is the authority.
        if (powerWasOn != PowerSystem.PowerOn) PowerChanged(PowerSystem.PowerOn);
        if (GamePause.Paused || leftDoor == null || rightDoor == null) return;
        if (!PowerSystem.PowerOn)
        {
            if (!handOpen) return;   // dead panels stay where they stopped
            leftDoor.position = Vector3.MoveTowards(leftDoor.position, leftOpen, HandSpeed * dt);
            rightDoor.position = Vector3.MoveTowards(rightDoor.position, rightOpen, HandSpeed * dt);
            return;
        }
        int before = occupants.Count;
        occupants.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
        if (before > 0 && occupants.Count == 0) closeAt = Time.time + closeDelay;
        if (karenUntil > 0f && !HeldForKaren)
        {
            // She's through: the doors close after the usual delay, unless someone else holds them.
            karenUntil = -1f;
            if (occupants.Count == 0) closeAt = Time.time + closeDelay;
        }
        if (occupants.Count == 0 && !HeldForKaren && Time.time >= closeAt) isOpen = false;

        Vector3 leftTarget = isOpen ? leftOpen : leftClosed;
        Vector3 rightTarget = isOpen ? rightOpen : rightClosed;

        leftDoor.position = Vector3.MoveTowards(leftDoor.position, leftTarget, slideSpeed * dt);
        rightDoor.position = Vector3.MoveTowards(rightDoor.position, rightTarget, slideSpeed * dt);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!PowerSystem.PowerOn) return;   // a dead sensor sees nobody
        Arrive(other);
    }

    void Arrive(Collider other)
    {
        if (!IsCharacter(other) || !occupants.Add(other)) return;
        closeAt = float.PositiveInfinity;

        // Only on the closed -> open transition: a second body walking in behind the
        // first shouldn't set the chime off again while the doors are already open.
        if (!isOpen)
        {
            isOpen = true;
            OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);

            bool employee = other.GetComponentInParent<CharacterController>() != null;
            NoiseBus.Emit(transform.position, 0.55f, NoiseKind.AutoDoor, employee ? NoiseAuthor.Player : NoiseAuthor.Customer);

            // The door sensor tells the store's management who came through (§3.5).
            if (employee) GameEvents.RaiseDoorUsed(this, true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (occupants.Remove(other) && occupants.Count == 0) closeAt = Time.time + closeDelay;
    }

    void OnTriggerStay(Collider other) => OnTriggerEnter(other);

    static bool IsCharacter(Collider other)
    {
        if (other == null || other.isTrigger || other.GetComponentInParent<Item>() != null) return false;
        return other.GetComponentInParent<CharacterController>() != null
            || other.GetComponentInParent<CustomerNPC>() != null
            || other.GetComponentInParent<NavMeshAgent>() != null;
    }

    void OnDisable() { PowerSystem.PowerChanged -= PowerChanged; all.Remove(this); occupants.Clear(); isOpen = false; closeAt = float.PositiveInfinity; karenUntil = -1f; handOpen = false; }

    // The phantom chime (Karen.md §8.3): the doors cycle with nobody there. Not in a blackout:
    // there's nothing to drive them. False when refused or already open, so a caller can
    // skip its own noise too.
    public bool PhantomCycle()
    {
        if (!PowerSystem.PowerOn || isOpen) return false;
        isOpen = true;
        OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);
        closeAt = Time.time + closeDelay;
        return true;
    }
}

// E on a dead automatic door (0.5.1 item 6): the panels slide open by hand. Added at runtime by
// AutoDoubleDoor, active only while the power is off and the pair is shut.
public sealed class AutoDoorHandle : MonoBehaviour, IInteractable
{
    public AutoDoubleDoor Door;
    public void Interact(PlayerInteract player) { if (Door != null) Door.PushByHand(); }
    public string GetPrompt() => "Slide the door open (no power)";
}
