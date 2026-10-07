using System.Collections.Generic;

// What a shopper came in for. Real baskets aren't random: milk, bread, fruit and drinks are
// bought far more often than plasters or hamster bedding, and within a section the best sellers
// win. So a list is drawn by how often each section is shopped and by each product's share of
// its section's facings (Planogram.Share), then put in the order the shop is walked — fresh food
// by the door, the aisles, the back wall — so a shopper crosses the store once, the way people do.
public static class ShoppingList
{
    // How often a basket includes something from each section.
    public static float SectionFootfall(ItemType section)
    {
        switch (section)
        {
            case ItemType.Dairy: return 1.6f;          // milk is the most bought thing in the shop
            case ItemType.Produce: return 1.4f;
            case ItemType.Bakery: return 1.3f;
            case ItemType.SoftDrinks: return 1.3f;
            case ItemType.Snacks: return 1.1f;
            case ItemType.Noodles: return 1.0f;
            case ItemType.Cereal: return 0.9f;
            case ItemType.Frozen: return 0.9f;
            case ItemType.Canned: return 0.8f;
            case ItemType.Confectionery: return 0.6f;  // mostly grabbed at the till instead
            case ItemType.Household: return 0.6f;
            case ItemType.PersonalCare: return 0.6f;
            case ItemType.PetFood: return 0.5f;
            default: return 0f;
        }
    }

    public static float Weight(ProductDef product) =>
        SectionFootfall(product.Category) * (1 + Planogram.Share(product.Id)) / 3f;

    // `count` different products, in walking order.
    public static List<ProductDef> Pick(int count, System.Random random)
    {
        var pool = new List<ProductDef>();
        var weights = new List<float>();
        foreach (ProductDef p in ProductCatalog.All)
        {
            float w = Weight(p);
            if (w <= 0f) continue;
            pool.Add(p);
            weights.Add(w);
        }

        var picked = new List<ProductDef>();
        while (picked.Count < count && pool.Count > 0)
        {
            float total = 0f;
            foreach (float w in weights) total += w;
            float roll = (float)random.NextDouble() * total;
            int i = 0;
            while (i < pool.Count - 1 && roll >= weights[i]) roll -= weights[i++];
            picked.Add(pool[i]);
            pool.RemoveAt(i);
            weights.RemoveAt(i);
        }

        picked.Sort((a, b) =>
        {
            int order = StoreLayout.WalkOrder(a.Category).CompareTo(StoreLayout.WalkOrder(b.Category));
            return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
        });
        return picked;
    }
}
