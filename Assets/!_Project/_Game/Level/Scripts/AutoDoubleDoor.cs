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
        if (occupants.Count == 0 && Time.time >= closeAt) isOpen = false;

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

    void OnDisable() { occupants.Clear(); isOpen = false; closeAt = float.PositiveInfinity; }

    // The phantom chime (Karen.md §8.3): the doors cycle with nobody there.
    public void PhantomCycle()
    {
        if (isOpen) return;
        isOpen = true;
        OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);
        closeAt = Time.time + closeDelay;
    }
}
