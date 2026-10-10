using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// A patch of mess a customer left behind. Cleaned by holding E while carrying the mop;
// the patch visibly shrinks and fades as the mopping progresses.
public class Dirt : HighlightInteractable, IHoldInteractable
{
    [Header("Cleaning")]
    public float secondsToClean = 3f;
    [Range(0f, 1f)] public float minScaleWhenAlmostClean = 0.15f;

    // How many spills are on the floor right now — the mopping task reads this directly.
    public static int ActiveCount { get; private set; }

    // And which: Karen's footprint trail and her favour both need to find them.
    static readonly List<Dirt> all = new List<Dirt>();
    public static IReadOnlyList<Dirt> All => all;

    public static bool AnyWithin(Vector3 position, float radius)
    {
        float sqr = radius * radius;
        for (int i = 0; i < all.Count; i++)
        {
            Vector3 d = all[i].transform.position - position;
            d.y = 0f;
            if (d.sqrMagnitude <= sqr) return true;
        }
        return false;
    }

    public static Dirt Nearest(Vector3 position, float radius)
    {
        Dirt best = null;
        float bestSqr = radius * radius;
        for (int i = 0; i < all.Count; i++)
        {
            float sqr = (all[i].transform.position - position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = all[i]; }
        }
        return best;
    }

    float progress;
    float nextMopNoise;

    Vector3 fullScale;
    MaterialPropertyBlock propertyBlock;
    Renderer patchRenderer;
    Color baseColour;

    void OnEnable()
    {
        all.Add(this);
        ActiveCount++;
        TaskManager.NotifyWorldChanged();
    }

    void OnDisable()
    {
        all.Remove(this);
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
        TaskManager.NotifyWorldChanged();
    }

    protected override void Awake()
    {
        base.Awake();
        fullScale = transform.localScale;

        patchRenderer = GetComponentInChildren<Renderer>();
        if (patchRenderer != null)
        {
            propertyBlock = new MaterialPropertyBlock();
            baseColour = patchRenderer.sharedMaterial != null && patchRenderer.sharedMaterial.HasProperty("_BaseColor")
                ? patchRenderer.sharedMaterial.GetColor("_BaseColor")
                : Color.white;
        }
    }

    public float HoldDuration => secondsToClean;

    // Only moppable while the mop is the item currently in hand.
    public bool CanHold(PlayerInteract player)
    {
        if (player == null || player.carrySlot == null) return false;
        return player.carrySlot.IsCarrying && player.carrySlot.currentItem.type == ItemType.Mop;
    }

    public void OnHoldProgress(float normalised)
    {
        progress = normalised;
        ApplyProgress(normalised);

        // Mopping is sustained and stationary — three seconds of free ambush window.
        if (Time.time >= nextMopNoise)
        {
            nextMopNoise = Time.time + 0.5f;
            NoiseBus.Emit(transform.position, 0.5f, NoiseKind.Mopping, NoiseAuthor.Player);
        }
    }

    public void OnHoldCancelled()
    {
        if (progress > 0f) GameEvents.RaiseMoppingAbandoned(this, progress);
        progress = 0f;
        ApplyProgress(0f);
    }

    public void OnHoldComplete(PlayerInteract player)
    {
        GameEvents.RaiseSpillCleaned(this);
        // Nothing to tally — OnDisable drops the active count and refreshes the task list.
        Destroy(gameObject);
    }

    void ApplyProgress(float normalised)
    {
        float remaining = Mathf.Lerp(1f, minScaleWhenAlmostClean, normalised);
        transform.localScale = fullScale * remaining;

        if (patchRenderer == null || propertyBlock == null) return;

        // Fade out alongside the shrink so progress reads clearly on dark floors too.
        Color faded = baseColour;
        faded.a = Mathf.Lerp(baseColour.a, 0f, normalised);
        patchRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor("_BaseColor", faded);
        patchRenderer.SetPropertyBlock(propertyBlock);
    }

    public override void Interact(PlayerInteract player)
    {
        // Cleaning happens through the hold interface; a tap does nothing.
    }

    public override string GetPrompt() => "Hold E with the mop to clean";
}
