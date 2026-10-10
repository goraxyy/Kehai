using Kehai.Karen;
using UnityEngine;

// A bagged-up sack of rubbish pulled out of a bin. Carried like any other item, and only
// counted as dealt with once it has been dropped into the container out back.
public class TrashBag : MonoBehaviour
{
    // Bags still waiting to be taken out — the trash task isn't done while any exist.
    public static int ActiveCount { get; private set; }

    // A bag in the skip is done with, but it stays in the world as a physical object.
    public bool IsDisposed { get; private set; }

    bool counted;
    Item item;
    float nextRustle;

    void Awake() => item = GetComponent<Item>();

    // A carried bag rustles as you walk — a moving noise she can follow by ear alone.
    void Update()
    {
        if (IsDisposed || item == null || !item.isCarried || Time.time < nextRustle) return;
        nextRustle = Time.time + 0.6f;
        NoiseBus.Emit(transform.position, 0.3f, NoiseKind.TrashRustle, NoiseAuthor.Player);
    }

    void OnEnable()
    {
        if (IsDisposed) return;      // already settled in the skip; don't re-count it
        counted = true;
        ActiveCount++;
        TaskManager.NotifyWorldChanged();
    }

    void OnDisable()
    {
        if (!counted) return;
        counted = false;
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
        TaskManager.NotifyWorldChanged();
    }

    // Dropped into the container. It stops counting toward the task and stops being
    // pickable, but the sack itself is left sitting in the skip rather than vanishing.
    public void MarkDisposed()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        GameEvents.RaiseBagDisposed(this);

        if (counted)
        {
            counted = false;
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            TaskManager.NotifyWorldChanged();
        }

        // Off the Interactable layer so the crosshair passes straight over it, and the
        // pickup component removed so nothing can hand it back.
        var pickup = GetComponent<PickupInteractable>();
        if (pickup != null) Destroy(pickup);

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = 0;   // Default
    }
}
