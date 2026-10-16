using System.Collections.Generic;
using UnityEngine;

// Fills up as customers use it. Once there's anything in it, interacting pulls out a
// bagged-up sack of rubbish that the player carries to the container out back.
public class Trashcan : HighlightInteractable
{
    // Registry so customers can find the nearest bin without scanning the scene.
    static readonly List<Trashcan> all = new List<Trashcan>();
    public static IReadOnlyList<Trashcan> All => all;

    [Header("Capacity")]
    public int capacity = 5;
    public WasteStream stream;
    [SerializeField] int usageCount;

    [Header("Bagging")]
    [Tooltip("Spawned into the player's hands when the bin is emptied.")]
    public GameObject trashBagPrefab;

    [Header("Fill Feedback")]
    [Tooltip("How much the bin visually swells as it fills, as a fraction of its size.")]
    public float fullBulge = 0.12f;

    [Header("Customer access")]
    public bool customersAllowed = true;
    [Tooltip("An explicit clear standing position, for bins against a wall.")]
    public Transform customerStandPoint;

    public bool IsFull => usageCount >= capacity;
    public int UsageCount => usageCount;

    Vector3 baseScale;

    protected override void Awake()
    {
        base.Awake();
        baseScale = transform.localScale;
        ApplyFillVisual();
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // Called by a customer that has finished using the bin.
    public void RegisterUse()
    {
        usageCount = Mathf.Min(capacity, usageCount + 1);
        ApplyFillVisual();
        TaskManager.NotifyWorldChanged();
    }

    public override void Interact(PlayerInteract player)
    {
        if (usageCount < 1) return;                       // nothing to bag up yet
        if (player.carrySlot == null) return;
        if (player.carrySlot.IsFull()) { Debug.Log("Inventory full!"); return; }

        if (trashBagPrefab == null)
        {
            Debug.LogWarning("Trashcan has no trashBagPrefab assigned.", this);
            return;
        }

        GameObject bag = Instantiate(trashBagPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);
        var rubbish=bag.GetComponent<TrashBag>();if(rubbish!=null)rubbish.SetStream(stream);
        Item bagItem = bag.GetComponent<Item>();
        if (bagItem == null) { Destroy(bag); return; }

        if (!player.carrySlot.TryPickup(bagItem))
        {
            Destroy(bag);
            return;
        }

        Empty();
        GameEvents.RaiseBinBagged(this);
    }

    public void Empty()
    {
        usageCount = 0;
        ApplyFillVisual();
        TaskManager.NotifyWorldChanged();
    }

    void ApplyFillVisual()
    {
        // Capture lazily and never trust a zero: if this ever runs before Awake (an editor
        // script poking at it, say) a zero baseScale would shrink the bin out of existence.
        if (baseScale.sqrMagnitude < 0.0001f)
        {
            baseScale = transform.localScale;
            if (baseScale.sqrMagnitude < 0.0001f) baseScale = Vector3.one;
        }

        float fill = capacity > 0 ? (float)usageCount / capacity : 0f;
        transform.localScale = baseScale * (1f + fullBulge * fill);
    }

    public override string GetPrompt()
    {
        if (usageCount >= 1) return $"Collect {WasteStreams.Label(stream)} bag ({usageCount}/{capacity})";
        return WasteStreams.Label(stream)+" bin is empty";
    }
}

// Anything the player can put back exactly where it started.
public interface IHomeReturnable
{
    void ReturnHome();
}
