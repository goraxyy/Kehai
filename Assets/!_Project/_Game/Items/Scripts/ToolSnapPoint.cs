using UnityEngine;

// The spot a tool belongs in — the mop rack, the stock pallet.
// Look at it and press E to send the tool back, whether it's in your hands or lying
// somewhere on the floor. A marker shows the empty spot while the tool is away.
public class ToolSnapPoint : HighlightInteractable
{
    [Tooltip("The tool that lives here.")]
    public Item tool;

    [Tooltip("Optional visual shown only while the tool is missing from this spot.")]
    public GameObject emptyMarker;

    [Tooltip("How close the tool has to be to count as 'home'.")]
    public float homeRadius = 0.35f;

    public bool IsToolHome
    {
        get
        {
            if (tool == null) return true;
            if (tool.isCarried) return false;
            return Vector3.Distance(tool.transform.position, transform.position) <= homeRadius;
        }
    }

    Collider zone;

    protected override void Awake()
    {
        base.Awake();
        zone = GetComponent<Collider>();
    }

    void Update()
    {
        // Two of these exist in the level, so a per-frame check costs nothing.
        bool home = IsToolHome;

        if (emptyMarker != null && emptyMarker.activeSelf == home)
            emptyMarker.SetActive(!home);

        // The recall zone surrounds the tool, so while the tool is sitting here the zone
        // would swallow the interaction ray and block picking the tool up. Only exist
        // when there is actually something to recall.
        if (zone != null && zone.enabled == home)
            zone.enabled = !home;
    }

    public override void Interact(PlayerInteract player)
    {
        if (tool == null) return;
        if (IsToolHome) return;

        // If it's in the inventory, take it out of the slot first.
        if (player.carrySlot != null && player.carrySlot.Contains(tool))
            player.carrySlot.Remove(tool);

        ItemHome home = tool.GetComponent<ItemHome>();
        if (home != null) home.ReturnHome();
        else tool.transform.SetPositionAndRotation(transform.position, transform.rotation);
    }

    public override string GetPrompt()
    {
        if (tool == null) return string.Empty;
        if (IsToolHome) return string.Empty;
        string what = tool.TryGetComponent(out ProductBox box) ? box.ProductName + " box" : ProductCatalog.SectionName(tool.type);
        return $"Put the {what} back";
    }
}
