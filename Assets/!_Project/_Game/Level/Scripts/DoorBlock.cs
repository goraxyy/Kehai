using System.Collections.Generic;
using UnityEngine;

// Borrows a real restock box, keeping its pickup component, materials and home slot.
// The marker stays in world space; the borrowed box never becomes its child.
public sealed class DoorBlock : MonoBehaviour, IInteractable, IHoverable
{
    public HingeDoor Door { get; private set; }
    public Item Box { get; private set; }
    public int Region { get; private set; }
    public int Side { get; private set; }   // which side of the door the box is on (SideOf)
    public bool IsBlocking => !cleared && Box != null && Box.gameObject.activeInHierarchy &&
        !Box.isCarried && (Box.transform.position - boxPosition).sqrMagnitude < .04f;

    static readonly List<DoorBlock> active = new List<DoorBlock>();
    static readonly Collider[] overlaps = new Collider[32];
    Vector3 boxPosition;
    PickupInteractable pickup;
    OutlineHighlight boxOutline, slotOutline;
    ItemHome home;
    ShiftManager shift;
    bool cleared, hovered, highlighting;

    public static void AddClosedRegions(ICollection<int> regions)
    {
        foreach (DoorBlock block in active)
            if (block != null && block.IsBlocking && block.Region >= 0) regions.Add(block.Region);
    }

    // For explicit world resets (including eval resets without a ShiftManager).
    public static void ClearAll()
    {
        for (int i = active.Count - 1; i >= 0; i--)
            if (active[i] != null) active[i].Clear();
    }

    public static ProductBox FindBox()
    {
        foreach (Item item in Item.All)
            if (item != null && item.TryGetComponent(out ProductBox box) && Available(box)) return box;
        return null;
    }

    static bool Available(ProductBox box)
    {
        if (box == null || !box.isActiveAndEnabled || !box.AtHome) return false;
        Item item = box.GetComponent<Item>();
        ItemHome home = box.GetComponent<ItemHome>();
        return item != null && !item.isCarried && item.enabled &&
            home != null && home.snapPoint != null && box.GetComponent<PickupInteractable>() != null &&
            box.TryGetComponent(out BoxCollider collider) && collider.enabled && !collider.isTrigger;
    }

    // Which way the door's two sides face: +1 along this, -1 against it (0.3.0).
    public static bool SideNormal(HingeDoor door, out Vector3 normal)
    {
        normal = default;
        if (door == null) return false;
        Vector3 along = door.DoorCentre - door.transform.position;
        along.y = 0f;
        if (along.sqrMagnitude < .01f) return false;
        normal = Vector3.Cross(along.normalized, Vector3.up);
        return true;
    }

    // The side of a door a point is on: +1 or -1, as SideNormal.
    public static int SideOf(HingeDoor door, Vector3 point)
    {
        if (!SideNormal(door, out Vector3 normal)) return 1;
        Vector3 d = point - door.DoorCentre;
        d.y = 0f;
        return Vector3.Dot(d, normal) >= 0f ? 1 : -1;
    }

    // Only a closed, still door with floor space on one side is eligible. All dimensions
    // come from the existing colliders, in metres; no rescaling of stock or hinge prefabs.
    // Either side (the first that fits), or the one Karen asks for (0.3.0: side = +1 or -1).
    public static bool TryPlacement(HingeDoor door, ProductBox box, out Vector3 position, out Quaternion rotation) =>
        TryPlacement(door, box, 0, out position, out rotation);

    public static bool TryPlacement(HingeDoor door, ProductBox box, int wanted, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (door == null || !door.isActiveAndEnabled || door.Locked || door.IsOpen || door.IsMoving ||
            door.Panel == null || !Available(box)) return false;
        if (!SideNormal(door, out Vector3 normal)) return false;
        BoxCollider collider = box.GetComponent<BoxCollider>();
        Vector3 size = WorldSize(collider);
        Bounds panel = door.Panel.bounds;
        BoxCollider leaf = door.Panel;
        float thickness = Mathf.Abs(Vector3.Dot(normal, leaf.transform.TransformVector(Vector3.right * leaf.size.x * .5f))) +
            Mathf.Abs(Vector3.Dot(normal, leaf.transform.TransformVector(Vector3.up * leaf.size.y * .5f))) +
            Mathf.Abs(Vector3.Dot(normal, leaf.transform.TransformVector(Vector3.forward * leaf.size.z * .5f)));
        for (int side = 1; side >= -1; side -= 2)
        {
            if (wanted != 0 && side != wanted) continue;
            Vector3 outward = normal * side;
            rotation = Quaternion.LookRotation(outward);
            Vector3 centre = door.DoorCentre + outward * (thickness + size.z * .5f + .04f);
            if (!Physics.Raycast(centre, Vector3.down, out RaycastHit floor, panel.size.y + 1f,
                    ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < .9f ||
                Mathf.Abs(floor.point.y - panel.min.y) > .3f) continue;
            centre.y = floor.point.y + size.y * .5f + .015f;
            int count = Physics.OverlapBoxNonAlloc(centre, size * .5f - Vector3.one * .005f,
                overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) continue; // A full buffer cannot prove clearance.
            bool clear = true;
            for (int i = 0; i < count; i++)
                if (!overlaps[i].transform.IsChildOf(box.transform)) { clear = false; break; }
            if (!clear) continue;
            position = centre - rotation * Vector3.Scale(collider.center, box.transform.lossyScale);
            return true;
        }
        return false;
    }

    static Vector3 WorldSize(BoxCollider collider)
    {
        Vector3 size = Vector3.Scale(collider.size, collider.transform.lossyScale);
        return new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    public static DoorBlock Spawn(HingeDoor door, ProductBox box, int region) => Spawn(door, box, region, 0);

    // On the side asked for (+1 or -1; 0 for whichever fits).
    public static DoorBlock Spawn(HingeDoor door, ProductBox box, int region, int side)
    {
        if (!TryPlacement(door, box, side, out Vector3 at, out Quaternion rotation)) return null;
        var root = new GameObject("Door box slot");
        root.layer = box.gameObject.layer;
        root.transform.SetPositionAndRotation(at, rotation);
        var block = root.AddComponent<DoorBlock>();
        block.Side = SideOf(door, at);
        block.Door = door;
        block.Box = box.GetComponent<Item>();
        block.Region = region;
        block.boxPosition = at;
        block.home = box.GetComponent<ItemHome>();
        block.pickup = box.GetComponent<PickupInteractable>();
        block.boxOutline = box.GetComponent<OutlineHighlight>();
        if (!door.AttachBlock(block)) { Remove(root); return null; }

        // Keep the original parent/scale and ItemHome. The stockroom's recall still works.
        box.transform.SetPositionAndRotation(at, rotation);
        Rigidbody body = box.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        block.BuildSlot(WorldSize(box.GetComponent<BoxCollider>()));
        active.Add(block);
        GameEvents.PlayerPickedUp += block.PickedUp;
        block.shift = FindAnyObjectByType<ShiftManager>();
        if (block.shift != null) block.shift.ShiftStateChanged += block.ShiftChanged;
        return block;
    }

    void BuildSlot(Vector3 size)
    {
        // A low rectangular floor frame, using the restock slot's existing material.
        // It has a trigger around the box, so aiming at either uses the same pickup path.
        ToolSnapPoint source = home.snapPoint.GetComponent<ToolSnapPoint>();
        Renderer marker = source != null && source.emptyMarker != null
            ? source.emptyMarker.GetComponentInChildren<Renderer>(true) : null;
        Renderer boxRenderer = Box.GetComponentInChildren<Renderer>();
        Material material = marker != null ? marker.sharedMaterial : boxRenderer != null ? boxRenderer.sharedMaterial : null;
        Vector3 offset = transform.InverseTransformVector(Box.transform.TransformVector(Box.GetComponent<BoxCollider>().center));
        for (int i = 0; i < 4; i++)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "Slot edge";
            edge.layer = gameObject.layer;
            edge.transform.SetParent(transform, false);
            bool across = i < 2;
            float sign = (i & 1) == 0 ? -1f : 1f;
            edge.transform.localPosition = offset + new Vector3(across ? 0f : sign * (size.x * .5f + .03f),
                -size.y * .5f, across ? sign * (size.z * .5f + .03f) : 0f);
            edge.transform.localScale = across ? new Vector3(size.x + .1f, .02f, .04f) : new Vector3(.04f, .02f, size.z + .1f);
            Collider solid = edge.GetComponent<Collider>();
            solid.enabled = false;
            Remove(solid);
            if (material != null) edge.GetComponent<Renderer>().sharedMaterial = material;
        }
        var zone = gameObject.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.center = offset;
        zone.size = size + Vector3.one * .08f;
        if (boxOutline != null && boxOutline.outlineMaterial != null)
        {
            slotOutline = gameObject.AddComponent<OutlineHighlight>();
            slotOutline.outlineMaterial = boxOutline.outlineMaterial;
            slotOutline.shape = OutlineHighlight.OutlineShape.BoundingBox;
        }
    }

    public void Interact(PlayerInteract player) { if (IsBlocking) pickup.Interact(player); }
    public string GetPrompt() => IsBlocking ? PickUpPrompt : string.Empty;

    // What the door says (0.3.0): from the box's side, how to clear it; from the other, that it's
    // held from beyond (and it rattles, HingeDoor.Use), so you know to go round.
    public const string PickUpPrompt = "Pick up the box to unblock the door";
    public const string OtherSidePrompt = "Blocked from the other side";

    public string PromptFor(PlayerInteract player)
    {
        if (!IsBlocking) return string.Empty;
        if (player == null || Door == null) return PickUpPrompt;
        return SideOf(Door, player.transform.position) == Side ? PickUpPrompt : OtherSidePrompt;
    }
    public void OnHoverEnter() { hovered = true; Highlight(true); }
    public void OnHoverExit() { hovered = false; Highlight(false); }

    void LateUpdate()
    {
        Refresh();
        if (cleared) return;
        IInteractable target = PlayerInteract.Local != null ? PlayerInteract.Local.CurrentTarget : null;
        Highlight(hovered || ReferenceEquals(target, this) || ReferenceEquals(target, pickup) ||
            (target is FurnitureDoor furniture && furniture.roomDoor == Door));
    }

    void Highlight(bool on)
    {
        if (highlighting == on) return;
        highlighting = on;
        if (boxOutline != null) boxOutline.SetHighlighted(on);
        if (slotOutline != null) slotOutline.SetHighlighted(on);
    }

    // The pickup event also covers an item put straight into an inactive inventory slot.
    void PickedUp(Item item) { if (item == Box) Clear(); }
    void ShiftChanged() { if (shift != null && !shift.IsShiftActive) Clear(); }
    public void Refresh() { if (!IsBlocking || Door == null) Clear(); }

    public void Clear()
    {
        Release();
        Remove(gameObject);
    }

    static void Remove(Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }

    void Release()
    {
        if (cleared) return;
        bool returnHome = IsBlocking;
        cleared = true;
        active.Remove(this);
        GameEvents.PlayerPickedUp -= PickedUp;
        if (shift != null) shift.ShiftStateChanged -= ShiftChanged;
        Highlight(false);
        if (Door != null) Door.DetachBlock(this);
        // A carried, stowed or moved box belongs to the player. Never recall it from them.
        if (returnHome && home != null) home.ReturnHome();
    }

    void OnDisable() => Release();
    void OnDestroy() => Release();
}
