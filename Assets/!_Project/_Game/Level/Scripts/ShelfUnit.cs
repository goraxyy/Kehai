using UnityEngine;

// Sits on a stocked shelf and highlights the whole unit while any of its slots are empty,
// so the player can spot what needs restocking from across the room.
// Purely event driven — slots report in when they change, nothing polls.
[RequireComponent(typeof(OutlineHighlight))]
public class ShelfUnit : MonoBehaviour
{
    [Tooltip("Highlight the shelf whenever at least one slot is empty.")]
    public bool highlightWhenNotFull = true;

    [Header("Planogram")]
    [Tooltip("Which part of the shop floor this bay stands in — \"Aisle 3 · Cereal & " +
             "Breakfast\". Written by Kehai/Store/Apply Layout; shown on signage and in " +
             "the customer's directions.")]
    public string section;

    [Tooltip("The section every slot on this bay belongs to. Kept in step by the layout " +
             "pass; read it instead of digging through the slots.")]
    public ItemType category = ItemType.Cereal;

    // How many shelves in the level currently need restocking — the stocking task reads this.
    public static int NotFullCount { get; private set; }

    OutlineHighlight outline;
    int emptyCount;
    bool initialised;
    bool countedAsNotFull;

    public bool IsFull => emptyCount == 0;

    void Awake()
    {
        outline = GetComponent<OutlineHighlight>();

        // Claim every slot underneath us and take an initial census.
        ShelfSlot[] slots = GetComponentsInChildren<ShelfSlot>(true);
        emptyCount = 0;
        foreach (ShelfSlot slot in slots)
        {
            slot.owner = this;
            if (!slot.isFilled) emptyCount++;
        }

        initialised = true;
        Refresh();
    }

    // ShelfGrid replaced this bay's slots: claim the new ones and take the census again.
    public void Rebind()
    {
        emptyCount = 0;
        foreach (ShelfSlot slot in GetComponentsInChildren<ShelfSlot>(false))
        {
            slot.owner = this;
            if (!slot.isFilled) emptyCount++;
        }
        initialised = true;
        Refresh();
    }

    public void OnSlotFilled()
    {
        emptyCount = Mathf.Max(0, emptyCount - 1);
        Refresh();
    }

    public void OnSlotEmptied()
    {
        emptyCount++;
        Refresh();
    }

    void Refresh()
    {
        if (!initialised) return;

        if (outline != null)
            outline.SetHighlighted(highlightWhenNotFull && emptyCount > 0);

        // Keep the level-wide tally in step, and let the task list know when it flips.
        bool notFull = emptyCount > 0;
        if (notFull != countedAsNotFull)
        {
            countedAsNotFull = notFull;
            NotFullCount = Mathf.Max(0, NotFullCount + (notFull ? 1 : -1));
            TaskManager.NotifyWorldChanged();
        }
    }

    void OnDisable()
    {
        // Don't leave a destroyed shelf counted as outstanding work.
        if (countedAsNotFull)
        {
            countedAsNotFull = false;
            NotFullCount = Mathf.Max(0, NotFullCount - 1);
        }
    }

    // Fills every empty slot in this shelf. Used by the stock crate.
    public int FillAll(GameObject itemPrefab)
    {
        int filled = 0;
        foreach (ShelfSlot slot in GetComponentsInChildren<ShelfSlot>(false))
        {
            if (slot.isFilled) continue;
            if (slot.FillWithNewItem(itemPrefab)) filled++;
        }
        return filled;
    }
}
