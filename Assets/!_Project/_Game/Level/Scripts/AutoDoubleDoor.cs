using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

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
    void OnEnable() => all.Add(this);

    // How far ahead the sensor reaches for someone moving at `speed`: far enough that the panels
    // (which take slideDistance / slideSpeed seconds) are open by the time they arrive. Karen
    // hunts at 7.5 m/s, and the sensor box alone gives her a third of a second (0.4.1).
    public float ReachFor(float speed) => Mathf.Max(SensorReach, speed * (slideDistance / Mathf.Max(0.1f, slideSpeed) + 0.15f));
    public const float SensorReach = 2.5f;      // the sensor box: 2.5 m either side of the panels

    // How open the panels are, 0 shut to 1 fully open.
    public float Openness => leftDoor == null || slideDistance <= 0f ? (isOpen ? 1f : 0f)
        : Mathf.Clamp01(Vector3.Distance(leftDoor.position, leftClosed) / slideDistance);

    // The nearest automatic door someone at `position`, moving along `velocity`, is about to
    // reach: within its reach for that speed, and ahead of them.
    public static AutoDoubleDoor Ahead(Vector3 position, Vector3 velocity)
    {
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
    // authored hers, so her own ears don't take it for you. False if they were already open.
    public bool OpenForKaren()
    {
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
    }

    void Update()
    {
        if (leftDoor == null || rightDoor == null) return;
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

        leftDoor.position = Vector3.MoveTowards(leftDoor.position, leftTarget, slideSpeed * Time.deltaTime);
        rightDoor.position = Vector3.MoveTowards(rightDoor.position, rightTarget, slideSpeed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
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
            || other.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null;
    }

    void OnDisable() { all.Remove(this); occupants.Clear(); isOpen = false; closeAt = float.PositiveInfinity; karenUntil = -1f; }

    // The phantom chime (Karen.md §8.3): the doors cycle with nobody there.
    public void PhantomCycle()
    {
        if (isOpen) return;
        isOpen = true;
        OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);
        closeAt = Time.time + closeDelay;
    }
}
