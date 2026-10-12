using Kehai.Karen;
using UnityEngine;

// A box of one product, from the back rooms (StorageBoxes puts one for every product there).
// Carried like any item; with it in hand, E on a shelf slot of the same product refills every
// empty slot of that product on the bay. It never runs out, and dropped it goes straight back
// to the spot it was taken from, so the racks stay full.
public class ProductBox : MonoBehaviour
{
    public string productId;
    public bool cold;

    [System.NonSerialized] public Vector3 home;
    Item item;

    public ProductDef Product => ProductCatalog.Get(productId);
    public string ProductName => ShortName(Product);
    public string Label => ProductName + " (box)";

    // Still where it was put: the replay leaves boxes out until one is moved.
    public bool AtHome
    {
        get
        {
            if (item == null) item = GetComponent<Item>();
            return (item == null || !item.isCarried) && (transform.position - home).sqrMagnitude < 0.0004f;
        }
    }

    public bool Fits(ShelfSlot slot) => slot != null && slot.productId == productId;

    // Fills the bay's empty slots of this product; returns how many.
    public int Restock(ShelfSlot slot)
    {
        if (!Fits(slot)) return 0;
        int filled = slot.owner != null ? slot.owner.FillProduct(productId) : (slot.Fill() ? 1 : 0);
        if (filled > 0)
        {
            NoiseBus.Emit(slot.Position, 0.45f, NoiseKind.Stocking, NoiseAuthor.Player);
            GameEvents.RaiseShelfRestocked(slot.owner, filled);
        }
        return filled;
    }

    public string PromptFor(ShelfSlot slot) =>
        Fits(slot) ? "Restock " + ProductName : "Wrong box: this shelf takes " + ShortName(ProductCatalog.Get(slot.productId));

    // What's printed on the box: the product as a customer says it, without its "a".
    public static string ShortName(ProductDef product)
    {
        if (product == null) return "Stock";
        string name = product.Name;
        if (name.StartsWith("a ")) name = name.Substring(2);
        else if (name.StartsWith("an ")) name = name.Substring(3);
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1) : product.Id;
    }
}
