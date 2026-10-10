using Kehai.Karen;
using UnityEngine;

// The stock crate. Carried in the inventory like any other item; while it's the item in
// hand, looking at any slot of a shelf and pressing E refills that entire shelf.
// It is never used up — one crate restocks the whole store.
public class StockCrate : MonoBehaviour
{
    [Tooltip("The box an item is made from when its product hasn't been imported.")]
    public GameObject itemPrefab;

    void Awake()
    {
        if (ShelfStock.Placeholder == null) ShelfStock.Placeholder = itemPrefab;
    }

    // Returns how many slots were filled.
    public int StockShelf(ShelfSlot slot, PlayerInteract player)
    {
        if (slot == null) return 0;

        // ShelfUnit refreshes its highlight and the task list as the slots fill.
        int filled = slot.owner != null ? slot.owner.FillAll() : (slot.Fill() ? 1 : 0);

        if (filled > 0)
        {
            NoiseBus.Emit(slot.Position, 0.45f, NoiseKind.Stocking, NoiseAuthor.Player);
            GameEvents.RaiseShelfRestocked(slot.owner, filled);
        }
        return filled;
    }
}
