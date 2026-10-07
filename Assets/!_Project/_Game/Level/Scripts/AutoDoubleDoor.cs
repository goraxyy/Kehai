using System.Collections;
using Kehai.Aiko;
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
    private int playersInside = 0;

    public bool IsOpen => isOpen;
    public Transform LeftPanel => leftDoor;     // the sliding panels, for the replay recorder
    public Transform RightPanel => rightDoor;

    void Start()
    {
        // Auto-find doors by name — no Inspector dragging needed
        leftDoor = transform.Find("LeftDoor");
        rightDoor = transform.Find("RightDoor");

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

        Vector3 leftTarget = isOpen ? leftOpen : leftClosed;
        Vector3 rightTarget = isOpen ? rightOpen : rightClosed;

        leftDoor.position = Vector3.MoveTowards(leftDoor.position, leftTarget, slideSpeed * Time.deltaTime);
        rightDoor.position = Vector3.MoveTowards(rightDoor.position, rightTarget, slideSpeed * Time.deltaTime);
    }

    void OnTriggerEnter(Collider other)
    {
        playersInside++;

        // Only on the closed -> open transition: a second body walking in behind the
        // first shouldn't set the chime off again while the doors are already open.
        if (!isOpen)
        {
            isOpen = true;
            OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);

            bool employee = other is CharacterController;
            NoiseBus.Emit(transform.position, 0.55f, NoiseKind.AutoDoor, employee ? NoiseAuthor.Player : NoiseAuthor.Customer);

            // The door sensor tells the store's management who came through (§3.5).
            if (employee) GameEvents.RaiseDoorUsed(this, true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        playersInside = Mathf.Max(0, playersInside - 1);
        if (playersInside == 0)
            StartCoroutine(CloseAfterDelay());
    }

    IEnumerator CloseAfterDelay()
    {
        yield return new WaitForSeconds(closeDelay);
        if (playersInside == 0) isOpen = false;
    }

    // The phantom chime (AIKO.md §8.3): the doors cycle with nobody there.
    public void PhantomCycle()
    {
        if (isOpen) return;
        isOpen = true;
        OneShotAudio.PlayAt(openChime, transform.position, chimeVolume);
        StartCoroutine(CloseAfterDelay());
    }
}
