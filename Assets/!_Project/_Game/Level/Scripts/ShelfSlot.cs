using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

// One slot on a shelf: the place one item stands, as data. A full shop has 16,500 of them;
// when each was a GameObject, and the item on it another, the stock alone was 33,000 objects.
// Now ShelfDrawer draws what stands on the slots, and an item becomes a GameObject only when
// it leaves one: taken by the player or a shopper, or knocked off by Karen (IDEAS.md,
// "Scaling", step 2).
//
// A slot is kept relative to the bay it's on (its frame), so a bay that's moved takes its
// stock along. The player aims at one through ShelfAim, which tests the ray against the
// slots' boxes instead of a collider each.
public sealed class ShelfSlot : IInteractable
{
    // Every slot in the shop. A slot's place in this list is its number in a replay.
    public static IReadOnlyList<ShelfSlot> All => ShelfStock.Current.Slots;

    // The bay it's on, which counts its empty slots; null for the till counter's.
    public readonly ShelfUnit owner;

    // What it's placed relative to: its bay, or the counter's marker.
    public readonly Transform frame;

    // On the board, in the frame's space: the middle of the cell, at the board's surface.
    public readonly Vector3 local;

    // Faces the frame's back (+Z) aisle rather than its front (-Z).
    public readonly bool facesBack;

    // The cell's floor, in the frame's space: x along the board, y into it.
    public readonly Vector2 cell;

    // How tall what stands here is, for aiming at it.
    public readonly float height;

    // The rendering layers of the room it's in (RoomLighting), so what stands on it is lit by
    // that room's lights. The default layer, lit by every light, unless whoever made it says.
    public uint lightMask = RoomLighting.Moving;

    // The section this facing belongs to. Only items from the same section fit.
    public ItemType requiredType;

    // The planogram: what this facing should hold, and what a customer asks for by name.
    public string productId;

    public bool isFilled { get; private set; }

    // What's actually standing here: the planogram's product, or whatever of its section the
    // player put back. Null when empty.
    public string StockedId { get; private set; }

    public int Index { get; private set; } = -1;
    internal Vector2Int gridKey;
    internal bool claimed;      // its bay has counted it, and hears when it empties or fills
    ShelfStock stock;
    string registeredFor;

    public ShelfSlot(ShelfUnit owner, Transform frame, Vector3 local, bool facesBack, Vector2 cell, float height)
    {
        this.owner = owner;
        this.frame = frame;
        this.local = local;
        this.facesBack = facesBack;
        this.cell = cell;
        this.height = height;
    }

    internal void Attach(ShelfStock to, int index)
    {
        stock = to;
        Index = index;
    }

    // On the board, in the world.
    public Vector3 Position => frame.TransformPoint(local);

    // Which way what stands here faces: products are made with their label to -Z.
    public Quaternion Rotation => facesBack ? frame.rotation * Quaternion.Euler(0f, 180f, 0f) : frame.rotation;

    // Toward the aisle it faces.
    public Vector3 Outward => Rotation * Vector3.back;

    // The SKU stocked here, or null on a facing that has only been given a section.
    public ProductDef Product => ProductCatalog.Get(productId);

    // What to call whatever belongs here: the product if there is one, the section if not.
    public string Label => ProductCatalog.Label(requiredType, productId);

    // Gives the slot its section and product, from the planogram, full or empty. What stands
    // on it becomes that product too, so the player never picks cereal off the drinks shelf.
    public void Stock(ItemType section, string id, bool filled)
    {
        requiredType = section;
        productId = id;
        if (isFilled != filled || (filled && StockedId != id)) Set(filled, filled ? id : null);
        if (registeredFor != id)
        {
            registeredFor = id;
            Planogram.Register(this);
        }
    }

    // ---------------------------------------------------------------- the player

    public void Interact(PlayerInteract player)
    {
        // Holding the stock crate restocks the whole shelf in one go.
        if (player.carrySlot != null && player.carrySlot.IsCarrying)
        {
            StockCrate crate = player.carrySlot.currentItem.GetComponent<StockCrate>();
            if (crate != null)
            {
                crate.StockShelf(this, player);
                return;
            }
        }

        if (isFilled)
        {
            if (player.carrySlot.IsFull())
            {
                Debug.Log("Inventory full!");
                return;
            }

            Item taken = TakeItem();
            if (taken == null) return;
            player.carrySlot.TryPickup(taken);
            GameEvents.RaisePlayerTookFromShelf(this, taken);
        }
        else
        {
            if (!player.carrySlot.IsCarrying) return;

            Item held = player.carrySlot.currentItem;
            if (held.type != requiredType)
            {
                Debug.Log("Wrong item type!");
                return;
            }

            player.carrySlot.Drop();
            Put(held);

            NoiseBus.Emit(Position, 0.45f, NoiseKind.Stocking, NoiseAuthor.Player);
            GameEvents.RaisePlayerShelvedItem(this, held);
            if (owner != null && owner.IsFull) GameEvents.RaiseShelfRestocked(owner, 1);
        }
    }

    public string GetPrompt() =>
        isFilled ? "Pick up " + ProductCatalog.Label(requiredType, StockedId) : "Place " + Label;

    // ---------------------------------------------------------------- stock coming and going

    // The item standing here, made real: a GameObject of the product, where it stood, and
    // the slot empty. For the player's hands and shoppers' baskets.
    public Item TakeItem()
    {
        if (!isFilled) return null;
        Item item = Spawn(StockedId);
        if (item == null) return null;
        Set(false, null);
        return item;
    }

    // Puts an item on the slot. It stops being a GameObject: the slot records what it was.
    public bool Put(Item item)
    {
        if (isFilled || item == null) return false;
        string id = string.IsNullOrEmpty(item.productId) ? productId : item.productId;
        if (Application.isPlaying) Object.Destroy(item.gameObject);
        else Object.DestroyImmediate(item.gameObject);
        Set(true, id);
        return true;
    }

    // Restocks it with the planogram's product, from the crate.
    public bool Fill()
    {
        if (isFilled) return false;
        Set(true, productId);
        return true;
    }

    // Karen's shelf sweep: the item tumbles off into the aisle.
    public void Eject()
    {
        if (!isFilled) return;
        Item item = Spawn(StockedId);
        Set(false, null);
        if (item == null) return;

        item.SetCarried(false, null);
        item.lastAuthor = NoiseAuthor.Karen;
        if (item.TryGetComponent(out Rigidbody body))
            body.AddForce(Outward * 2f + Vector3.up * 0.5f, ForceMode.Impulse);
    }

    // Puts back a state kept from before (a chunk of the endless maze coming back): full of
    // `id`, or empty.
    public void Load(bool filled, string id) => Set(filled, filled ? id : null);

    // A replay showing the shelf as it was: the look only, nobody told.
    public void Show(bool filled, string product)
    {
        isFilled = filled;
        StockedId = filled ? (string.IsNullOrEmpty(product) ? productId : product) : null;
        stock?.NotifyChanged(this);
    }

    void Set(bool filled, string id)
    {
        bool was = isFilled;
        isFilled = filled;
        StockedId = filled ? id : null;
        if (owner != null && claimed && was != filled)
        {
            if (filled) owner.OnSlotFilled();
            else owner.OnSlotEmptied();
        }
        stock?.NotifyChanged(this);
    }

    // Where an item of `id` stands here: its origin is the middle of its box, so it's lifted
    // by its rest height off the board.
    public void Pose(string id, out Vector3 position, out Quaternion rotation)
    {
        rotation = Rotation;
        ProductLook.Look? look = ProductLook.For(id);
        float rest = look != null ? look.Value.RestHeight : Item.SnapHeight;
        position = Position + rotation * (Vector3.up * rest);
    }

    Item Spawn(string id)
    {
        GameObject prefab = ProductLook.Prefab(id);
        if (prefab == null) prefab = ShelfStock.Placeholder;
        if (prefab == null) return null;

        Pose(id, out Vector3 position, out Quaternion rotation);
        GameObject go = Object.Instantiate(prefab, position, rotation);
        if (!go.TryGetComponent(out Item item))
        {
            Object.Destroy(go);
            return null;
        }
        ProductDef product = ProductCatalog.Get(id);
        item.SetProduct(product != null ? product.Category : requiredType, id);
        item.isOnShelf = false;
        return item;
    }

    // ---------------------------------------------------------------- aiming

    // Where a ray first enters this slot's box (the cell, as tall as what stands in it), if
    // it does.
    public bool RayHit(Ray ray, out float distance)
    {
        distance = 0f;
        Vector3 o = frame.InverseTransformPoint(ray.origin) - local - new Vector3(0f, height * 0.5f, 0f);
        Vector3 d = frame.InverseTransformDirection(ray.direction);
        var half = new Vector3(cell.x * 0.5f - 0.005f, height * 0.5f, cell.y * 0.5f - 0.005f);

        float near = float.NegativeInfinity, far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = o[axis], da = d[axis], h = half[axis];
            if (Mathf.Abs(da) < 1e-6f)
            {
                if (oa < -h || oa > h) return false;
                continue;
            }
            float t1 = (-h - oa) / da, t2 = (h - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Mathf.Max(near, t1);
            far = Mathf.Min(far, t2);
            if (near > far || far < 0f) return false;
        }
        distance = Mathf.Max(0f, near);
        return true;
    }
}
