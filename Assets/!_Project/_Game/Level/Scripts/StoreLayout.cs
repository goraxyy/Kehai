using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Where everything is sold.
//
// The sales floor is a maze, not a set of parallel aisles, so sections can't be read off
// the shelf runs — they are cut out of the floor plan instead. Every bay is classified by
// where it stands, which keeps a bay whole where the maze doubles back on itself and means
// the plan survives a shelf being nudged.
//
// It runs at load rather than being baked into the scene, for two reasons. The scene is a
// binary asset that isn't in version control, so a planogram stored in it could never be
// reviewed or shared; and 3,400 facings baked as prefab overrides is a lot of scene to
// carry for something a lookup answers in a millisecond. Kehai/Store/Apply Layout will
// still write it into the scene when you want the Inspector to show the truth.
//
// STORE_CATALOG.md is this file written out in prose.
public static class StoreLayout
{
    // A rectangle of floor and what is sold on it. Bounds are world XZ, half-open:
    // xMin <= x < xMax, zMin <= z < zMax. Z is negative across the whole building, so
    // zMin is the far (south) edge, by the chillers.
    public struct Zone
    {
        public string Sign;
        public ItemType Section;
        public float XMin, XMax, ZMin, ZMax;

        // Its number, in the order a customer walks the shop.
        public int Aisle;
        public string Name;       // "Tins & Jars", printed on the hanging sign
        public string Japanese;   // its second line
        public string Spoken;     // what a customer calls it: "the tinned food"

        public bool Contains(Vector3 p) => p.x >= XMin && p.x < XMax && p.z >= ZMin && p.z < ZMax;
        public bool IsAisle => Aisle > 0;
    }

    const float Inf = 100000f;

    // The plan follows the route a customer actually walks: in through the doors at the
    // north-east, round the floor, out through the checkouts at the north-west. So fresh
    // goods greet them at the door, milk is at the far wall with the length of the shop
    // between it and the entrance, and sweets are the last thing they pass.
    //
    // Every part of the shop is a numbered aisle, numbered in that walking order: 1 by the
    // doors, 13 at the tills. First match wins, so the order of this table matters: the front
    // strip is claimed before the aisles that run behind it.
    public static readonly Zone[] Zones =
    {
        // --- the front of the shop, everything north of z = -142 ------------------
        Z("Aisle 1 · Fruit & Veg",            ItemType.Produce,       58f,  Inf, -142f,  Inf,
          1, "Fruit & Veg", "青果", "the fruit and veg"),
        Z("Aisle 2 · Bakery",                 ItemType.Bakery,        42f,  58f, -142f,  Inf,
          2, "Bakery", "ベーカリー", "the bread"),
        Z("Aisle 13 · Sweets",                ItemType.Confectionery, -Inf, 42f, -142f,  Inf,
          13, "Sweets", "お菓子", "the sweets"),

        // --- the middle of the shop, working westward away from the doors ----------
        Z("Aisle 3 · Soft Drinks",            ItemType.SoftDrinks,    76f,  Inf, -163f, -142f,
          3, "Soft Drinks", "飲料", "the soft drinks"),
        Z("Aisle 4 · Snacks & Crisps",        ItemType.Snacks,        60f,  76f, -153f, -142f,
          4, "Snacks & Crisps", "スナック菓子", "the crisps"),
        Z("Aisle 5 · Tins & Jars",            ItemType.Canned,        60f,  76f, -163f, -153f,
          5, "Tins & Jars", "缶詰・瓶詰", "the tinned food"),
        Z("Aisle 6 · Cereal & Breakfast",     ItemType.Cereal,        44f,  60f, -153f, -142f,
          6, "Cereal & Breakfast", "シリアル・朝食", "the cereal"),
        Z("Aisle 7 · Noodles, Pasta & Rice",  ItemType.Noodles,       44f,  60f, -163f, -153f,
          7, "Noodles, Pasta & Rice", "麺類・お米", "the noodles"),
        Z("Aisle 8 · Health & Beauty",        ItemType.PersonalCare, -Inf,  44f, -153f, -142f,
          8, "Health & Beauty", "ヘルス＆ビューティー", "the toiletries"),
        Z("Aisle 9 · Household & Cleaning",   ItemType.Household,    -Inf,  44f, -163f, -153f,
          9, "Household & Cleaning", "日用品", "the cleaning stuff"),

        // --- the back wall, along the chillers ------------------------------------
        Z("Aisle 10 · Dairy & Chilled",       ItemType.Dairy,         58f,  Inf, -Inf,  -163f,
          10, "Dairy & Chilled", "乳製品", "the dairy"),
        Z("Aisle 11 · Frozen",                ItemType.Frozen,        44f,  58f, -Inf,  -163f,
          11, "Frozen", "冷凍食品", "the frozen food"),
        Z("Aisle 12 · Pet",                   ItemType.PetFood,      -Inf,  44f, -Inf,  -163f,
          12, "Pet", "ペット用品", "the pet food")
    };

    static Zone Z(string sign, ItemType section, float xMin, float xMax, float zMin, float zMax,
                  int aisle, string name, string japanese, string spoken)
    {
        return new Zone
        {
            Sign = sign, Section = section, XMin = xMin, XMax = xMax, ZMin = zMin, ZMax = zMax,
            Aisle = aisle, Name = name, Japanese = japanese, Spoken = spoken
        };
    }

    // The zone a section is sold in.
    public static Zone ZoneOf(ItemType section)
    {
        foreach (Zone zone in Zones)
            if (zone.Section == section) return zone;
        return Zones[0];
    }

    // The order a shopper meets the sections in, which is the aisle numbering: fresh food at
    // the door, the middle of the shop, the back wall, and the sweets last, in the queue. A
    // shopping list is walked in this order.
    public static int WalkOrder(ItemType section)
    {
        foreach (Zone zone in Zones)
            if (zone.Section == section) return zone.Aisle;
        return Zones.Length + 1;
    }

    public static Zone ZoneAt(Vector3 position)
    {
        foreach (Zone zone in Zones)
            if (zone.Contains(position)) return zone;

        // The table covers the whole plane, so this is unreachable — but a shelf dragged
        // out to nowhere should land somewhere sane rather than throw.
        return Zones[0];
    }

    public static ItemType SectionAt(Vector3 position) => ZoneAt(position).Section;
    public static string SignAt(Vector3 position) => ZoneAt(position).Sign;

    // Stocking the store is the first thing that happens once the scene is up: every bay's
    // boards are cut into ShelfGrid's slots and filled from the planogram (a scene that has
    // been baked with Kehai/Store/Stock the Maze already has them, and is only checked). Then
    // the aisle signs and the lamps go up, and each room's lights are kept in their room.
    // Scenes loaded later (the eval harness reloads the store for every episode) get the same.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StockOnLoad()
    {
        Dress();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Dress();

    static void Dress()
    {
        ApplyToScene();
        AisleSigns.Build();
        CeilingLamps.Ensure();
        RoomLighting.Apply();
    }

    // Optional hooks so the editor pass can wrap each write in an Undo record and register
    // the prefab override. At runtime both are null and this is a plain assignment.
    public static int ApplyToScene(System.Action<Object> beforeWrite = null,
                                   System.Action<Object> afterWrite = null)
    {
        Planogram.Clear();
        return Walk(true, beforeWrite, afterWrite, null, null, null, null);
    }

    // One walk of the shop, used both to stock it and to count it. Counting takes the same
    // path as stocking so a report can never describe a store the game doesn't build.
    static int Walk(bool write,
                    System.Action<Object> beforeWrite,
                    System.Action<Object> afterWrite,
                    Dictionary<string, int> facingsPerProduct,
                    Dictionary<ItemType, int> baysPerSection,
                    Dictionary<ItemType, int> facingsPerSection,
                    Dictionary<ItemType, int> slotsPerSection)
    {
        int slots = 0;

        var units = Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include);
        foreach (ShelfUnit unit in units)
        {
            Zone zone = ZoneAt(unit.transform.position);

            if (write)
            {
                beforeWrite?.Invoke(unit);
                unit.section = zone.Sign;
                unit.category = zone.Section;
                afterWrite?.Invoke(unit);
            }

            int facings;
            int n = StockBay(unit, zone, write, beforeWrite, afterWrite, facingsPerProduct, out facings);
            slots += n;

            Tally(baysPerSection, zone.Section, 1);
            Tally(facingsPerSection, zone.Section, facings);
            Tally(slotsPerSection, zone.Section, n);
        }

        // A facing sits loose in the scene rather than on a bay: the one on the till counter.
        // It carries the impulse buy every till in the world has: mints.
        var allSlots = Object.FindObjectsByType<ShelfSlot>(FindObjectsInactive.Include);
        foreach (ShelfSlot slot in allSlots)
        {
            if (slot.GetComponentInParent<ShelfUnit>(true) != null) continue;

            ProductDef counter = ProductCatalog.Get(Planogram.CounterProduct);
            if (counter == null) continue;
            Tally(facingsPerProduct, counter.Id, 1);
            Tally(facingsPerSection, counter.Category, 1);
            Tally(slotsPerSection, counter.Category, 1);
            if (write) Write(slot, counter.Category, counter.Id, beforeWrite, afterWrite);
            slots++;
        }

        return slots;
    }

    // Stocks one bay from the planogram. A facing is one board on one side of the bay — the
    // whole of the top board, front side, is Pipisi and nothing else — which is how a real
    // planogram is blocked out. Planogram picks the product for each board from its height
    // (eye, waist or stoop level) and side; ShelfGrid cuts the board into slots for it.
    static int StockBay(ShelfUnit unit, Zone zone, bool write,
                        System.Action<Object> beforeWrite, System.Action<Object> afterWrite,
                        Dictionary<string, int> facingsPerProduct, out int facingCount)
    {
        Transform bay = unit.transform;

        // Which of the section's layouts this bay gets. Derived from its position so the plan
        // is the same every run, and so neighbouring bays don't all look alike.
        int seed = Mathf.Abs(Mathf.RoundToInt(bay.position.x) * 73856093 ^
                             Mathf.RoundToInt(bay.position.z) * 19349663);
        bool endCap = Planogram.IsEndCap(bay);

        int slots = 0;
        List<ShelfGrid.Facing> facings = ShelfGrid.Facings(bay);
        facingCount = facings.Count;
        foreach (ShelfGrid.Facing facing in facings)
        {
            string id = Planogram.ProductFor(zone.Section, seed, facing.Back, Planogram.BoardAt(facing.Height), endCap);
            ProductDef product = ProductCatalog.Get(id);
            // A cross-merchandised product keeps its own section: cola on the crisps' end cap
            // still only takes cola.
            ItemType section = product != null ? product.Category : zone.Section;

            if (!string.IsNullOrEmpty(id)) Tally(facingsPerProduct, id, 1);
            slots += ShelfGrid.SlotsIn(facing, product);
            if (!write) continue;

            Transform group = bay.Find(ShelfGrid.RootName + "/Facing_" + facing.Key);
            ShelfFacing built = group != null ? group.GetComponent<ShelfFacing>() : null;
            if (built == null || built.productId != id)
            {
                // Not baked, or baked for another product: build it now. The editor's pass
                // only writes ids into what's there; Stock the Maze is what bakes.
                if (!Application.isPlaying) continue;
                group = ShelfGrid.Build(bay, facing, product, section, (prefab, parent) => Object.Instantiate(prefab, parent));
            }
            foreach (ShelfSlot slot in group.GetComponentsInChildren<ShelfSlot>(true))
                Write(slot, section, id, beforeWrite, afterWrite);
        }

        if (write && Application.isPlaying)
        {
            // The shelf prefab's own slots, six to a board, are superseded by the grid.
            Transform old = bay.Find("Slots");
            if (old != null && old.gameObject.activeSelf) old.gameObject.SetActive(false);
            unit.Rebind();
        }
        return slots;
    }

    static void Write(ShelfSlot slot, ItemType section, string id,
                      System.Action<Object> beforeWrite, System.Action<Object> afterWrite)
    {
        beforeWrite?.Invoke(slot);
        if (slot.storedItem != null) beforeWrite?.Invoke(slot.storedItem);

        // The item already on the slot has to agree with it, or the player picks cereal off
        // the drinks shelf and then can't put it back. Stock sees to that too.
        slot.Stock(section, id);

        afterWrite?.Invoke(slot);
        if (slot.storedItem != null) afterWrite?.Invoke(slot.storedItem);
    }

    static void Tally<T>(Dictionary<T, int> counts, T key, int amount)
    {
        if (counts == null) return;
        counts[key] = counts.TryGetValue(key, out int current) ? current + amount : amount;
    }

    // The plan as a table, for the console and for checking the shop against the document.
    public static string Describe()
    {
        var bays = new Dictionary<ItemType, int>();
        var facings = new Dictionary<ItemType, int>();
        var slots = new Dictionary<ItemType, int>();
        var perProduct = new Dictionary<string, int>();

        // Counting is the same walk as stocking, so this can't drift from what ships.
        int total = Walk(false, null, null, perProduct, bays, facings, slots);

        var sb = new StringBuilder();
        sb.AppendLine($"Store layout: {total} slots across {Zones.Length} aisles.");
        sb.AppendLine();
        sb.AppendLine("  aisle                                bays  facings  slots  range");

        foreach (Zone zone in Zones)
        {
            bays.TryGetValue(zone.Section, out int bayCount);
            facings.TryGetValue(zone.Section, out int facingCount);
            slots.TryGetValue(zone.Section, out int slotCount);
            sb.AppendLine($"  {zone.Sign,-36} {bayCount,4} {facingCount,8} {slotCount,6} {ProductCatalog.InSection(zone.Section).Count,6}");
        }

        var unstocked = new List<string>();
        foreach (ProductDef product in ProductCatalog.All)
            if (!perProduct.ContainsKey(product.Id)) unstocked.Add(product.Id);

        sb.AppendLine();
        sb.AppendLine($"  {perProduct.Count} of {ProductCatalog.All.Count} products have a facing.");
        if (unstocked.Count > 0)
            sb.AppendLine("  not stocked anywhere: " + string.Join(", ", unstocked));

        return sb.ToString();
    }
}
