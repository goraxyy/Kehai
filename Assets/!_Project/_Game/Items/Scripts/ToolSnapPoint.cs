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

    // Karen took the tool and hid it (0.3.0, Tool theft). Until it's carried back by hand the
    // rack won't recall it, and the task list says "Return the …". Cleared once it's home.
    public bool Taken { get; private set; }

    static readonly System.Collections.Generic.List<ToolSnapPoint> taken = new System.Collections.Generic.List<ToolSnapPoint>();
    public static int TakenCount { get { Prune(); return taken.Count; } }
    public static System.Collections.Generic.IReadOnlyList<ToolSnapPoint> TakenRacks { get { Prune(); return taken; } }

    // A rack destroyed without OnDestroy (in the editor, never woken) leaves a dead entry.
    static void Prune() => taken.RemoveAll(r => r == null);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => taken.Clear();

    public string ToolName
    {
        get
        {
            if (tool == null) return "tool";
            if (tool.TryGetComponent(out ProductBox box)) return box.ProductName + " box";
            string section = ProductCatalog.SectionName(tool.type);
            return string.IsNullOrEmpty(place) ? section.ToLowerInvariant() : place + " " + section.ToLowerInvariant();
        }
    }

    [Tooltip("Where this rack is, for the task list: \"WC\" makes the job read \"Return the WC mop\".")]
    public string place = string.Empty;

    public void MarkTaken(bool isTaken)
    {
        if (Taken == isTaken) return;
        Taken = isTaken;
        if (isTaken) { if (!taken.Contains(this)) taken.Add(this); }
        else taken.Remove(this);
        TaskManager.NotifyWorldChanged();
    }

    Collider zone;

    protected override void Awake()
    {
        base.Awake();
        zone = GetComponent<Collider>();
    }

    void OnDestroy() => taken.Remove(this);

    void Update()
    {
        // Two of these exist in the level, so a per-frame check costs nothing.
        bool home = IsToolHome;
        if (home && Taken && (tool == null || !tool.isCarried)) MarkTaken(false);

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

        // She hid it: no recall. Carried back by hand, it goes home as normal.
        bool inHand = player.carrySlot != null && player.carrySlot.Contains(tool);
        if (Taken && !inHand) return;

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
        if (Taken && !tool.isCarried) return "She moved it. Find it.";
        string what = tool.TryGetComponent(out ProductBox box) ? box.ProductName + " box" : ProductCatalog.SectionName(tool.type);
        return $"Put the {what} back";
    }
}
