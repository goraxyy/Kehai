using UnityEngine;

// The skip out back. A trash bag dropped into it stops counting against the trash task,
// and is left lying in the skip rather than deleted.
[RequireComponent(typeof(Collider))]
public class TrashContainer : MonoBehaviour
{
    public AudioClip disposeSound;
    public WasteStream stream;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        TryDispose(other);
    }

    // A bag dropped from standing height can pass through in a single physics step,
    // so also accept anything resting inside the volume.
    void OnTriggerStay(Collider other)
    {
        TryDispose(other);
    }

    void TryDispose(Collider other)
    {
        TrashBag bag = other.GetComponentInParent<TrashBag>();
        if (bag == null || bag.IsDisposed || bag.stream != stream) return;

        // Ignore a bag still in the player's hands hovering over the skip.
        Item item = bag.GetComponent<Item>();
        if (item != null && item.isCarried) return;

        OneShotAudio.PlayAt(disposeSound, transform.position);
        bag.MarkDisposed();   // refreshes the task list; the sack stays in the skip
    }
}
