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

        // The numbered aisles run through the middle of the shop; the departments round the
        // edge (produce, bakery, the back wall, the tills) have names instead, as in any
        // supermarket. 0 for a department.
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
    // Aisle numbers climb with distance from the doors. First match wins, so the order
    // matters: the front strip is claimed before the aisles that run behind it.
    public static readonly Zone[] Zones =
    {
        // --- the front of the shop, everything north of z = -142 ------------------
        Z("Produce · Fruit & Veg",           ItemType.Produce,       58f,  Inf, -142f,  Inf,
          0, "Fruit & Veg", "青果", "the fruit and veg"),
        Z("Bakery",                          ItemType.Bakery,        42f,  58f, -142f,  Inf,
          0, "Bakery", "ベーカリー", "the bakery"),
        Z("Checkout · Sweets & Impulse",     ItemType.Confectionery, -Inf, 42f, -142f,  Inf,
          0, "Sweets", "お菓子", "the sweets"),

        // --- the aisles, working westward away from the doors ---------------------
        Z("Aisle 1 · Soft Drinks",           ItemType.SoftDrinks,    76f,  Inf, -163f, -142f,
          1, "Soft Drinks", "飲料", "the soft drinks"),
        Z("Aisle 2 · Snacks & Crisps",       ItemType.Snacks,        60f,  76f, -153f, -142f,
          2, "Snacks & Crisps", "スナック菓子", "the crisps"),
        Z("Aisle 3 · Tins & Jars",           ItemType.Canned,        60f,  76f, -163f, -153f,
          3, "Tins & Jars", "缶詰・瓶詰", "the tinned food"),
        Z("Aisle 4 · Cereal & Breakfast",    ItemType.Cereal,        44f,  60f, -153f, -142f,
          4, "Cereal & Breakfast", "シリアル・朝食", "the cereal"),
        Z("Aisle 5 · Noodles, Pasta & Rice", ItemType.Noodles,       44f,  60f, -163f, -153f,
          5, "Noodles, Pasta & Rice", "麺類・お米", "the noodles"),
        Z("Aisle 6 · Health & Beauty",       ItemType.PersonalCare, -Inf,  44f, -153f, -142f,
          6, "Health & Beauty", "ヘルス＆ビューティー", "the toiletries"),
        Z("Aisle 7 · Household & Cleaning",  ItemType.Household,    -Inf,  44f, -163f, -153f,
          7, "Household & Cleaning", "日用品", "the cleaning stuff"),

        // --- the back wall, along the chillers ------------------------------------
        Z("Back Wall · Dairy & Chilled",     ItemType.Dairy,         58f,  Inf, -Inf,  -163f,
          0, "Dairy & Chilled", "乳製品", "the dairy"),
        Z("Back Wall · Frozen",              ItemType.Frozen,        44f,  58f, -Inf,  -163f,
          0, "Frozen", "冷凍食品", "the frozen food"),
        Z("Back Wall · Pet",                 ItemType.PetFood,      -Inf,  44f, -Inf,  -163f,
          0, "Pet", "ペット用品", "the pet food")
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

    // The order a shopper meets the sections in: fresh food at the door, the aisles, the back
    // wall, and the sweets last, in the queue. A shopping list is walked in this order.
    public static int WalkOrder(ItemType section)
    {
        switch (section)
        {
            case ItemType.Produce: return 0;
            case ItemType.Bakery: return 1;
            case ItemType.SoftDrinks: return 2;
            case ItemType.Snacks: return 3;
            case ItemType.Canned: return 4;
            case ItemType.Cereal: return 5;
            case ItemType.Noodles: return 6;
            case ItemType.PersonalCare: return 7;
            case ItemType.Household: return 8;
            case ItemType.Dairy: return 9;
            case ItemType.Frozen: return 10;
            case ItemType.PetFood: return 11;
            case ItemType.Confectionery: return 12;
            default: return 13;
        }
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

    // Stocking the store is the first thing that happens once the scene is up: every bay
    // is still holding the placeholder cereal it was built with until this runs. Scenes
    // loaded later (the eval harness reloads the store for every episode) are stocked too.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StockOnLoad()
    {
        ApplyToScene();
        AisleSigns.Build();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToScene();
        AisleSigns.Build();
    }

    // Optional hooks so the editor pass can wrap each write in an Undo record and register
    // the prefab override. At runtime both are null and this is a plain assignment.
    public static int ApplyToScene(System.Action<Object> beforeWrite = null,
                                   System.Action<Object> afterWrite = null)
    {
        Planogram.Clear();
        return Walk(true, beforeWrite, afterWrite, null, null, null);
    }

    // One walk of the shop, used both to stock it and to count it. Counting takes the same
    // path as stocking so a report can never describe a store the game doesn't build.
    static int Walk(bool write,
                    System.Action<Object> beforeWrite,
                    System.Action<Object> afterWrite,
                    Dictionary<string, int> facingsPerProduct,
                    Dictionary<ItemType, int> baysPerSection,
                    Dictionary<ItemType, int> facingsPerSection)
    {
        int touched = 0;

        var units = Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include);
        foreach (ShelfUnit unit in units)
        {
            Zone zone = ZoneAt(unit.transform.position);
            ShelfSlot[] slots = unit.GetComponentsInChildren<ShelfSlot>(true);

            if (write)
            {
                beforeWrite?.Invoke(unit);
                unit.section = zone.Sign;
                unit.category = zone.Section;
                afterWrite?.Invoke(unit);
            }

            Stock(slots, zone, unit.transform, write, beforeWrite, afterWrite, facingsPerProduct);
            touched += slots.Length;

            Tally(baysPerSection, zone.Section, 1);
            Tally(facingsPerSection, zone.Section, slots.Length);
        }

        // A facing sits loose in the scene rather than on a bay: the one on the till counter.
        // It carries the impulse buy every till in the world has: mints.
        var allSlots = Object.FindObjectsByType<ShelfSlot>(FindObjectsInactive.Include);
        foreach (ShelfSlot slot in allSlots)
        {
            if (slot.GetComponentInParent<ShelfUnit>() != null) continue;

            ProductDef counter = ProductCatalog.Get(Planogram.CounterProduct);
            if (counter != null)
            {
                Tally(facingsPerProduct, counter.Id, 1);
                if (write) Write(slot, counter.Category, counter.Id, Backstock.DefaultFootprint, beforeWrite, afterWrite);
            }
            touched++;

            Tally(facingsPerSection, counter != null ? counter.Category : ZoneAt(slot.transform.position).Section, 1);
        }

        return touched;
    }

    // Stocks one bay from the planogram. A facing is one board on one side of the bay — the
    // whole of the top board, front side, is Pipisi and nothing else — which is how a real
    // planogram is blocked out. Planogram picks the product for each board from its height
    // (eye, waist or stoop level) and side, so every rule about where things go lives there.
    static void Stock(IReadOnlyList<ShelfSlot> slots, Zone zone, Transform bay, bool write,
                      System.Action<Object> beforeWrite, System.Action<Object> afterWrite,
                      Dictionary<string, int> facingsPerProduct)
    {
        // Facings are keyed by height and by which side of the bay they face, then sorted,
        // so the order is the same however Unity happened to hand back the slots.
        var facings = new Dictionary<long, List<ShelfSlot>>();
        var keys = new List<long>();

        foreach (ShelfSlot slot in slots)
        {
            Vector3 local = bay.InverseTransformPoint(slot.transform.position);
            long key = (long)Mathf.RoundToInt(local.y * 100f) * 4L + (local.z >= 0f ? 1L : 0L);

            if (!facings.TryGetValue(key, out List<ShelfSlot> facing))
            {
                facing = new List<ShelfSlot>();
                facings[key] = facing;
                keys.Add(key);
            }
            facing.Add(slot);
        }

        keys.Sort();

        // Which of the section's layouts this bay gets. Derived from its position so the plan
        // is the same every run, and so neighbouring bays don't all look alike.
        int seed = Mathf.Abs(Mathf.RoundToInt(bay.position.x) * 73856093 ^
                             Mathf.RoundToInt(bay.position.z) * 19349663);
        bool endCap = Planogram.IsEndCap(bay);

        foreach (long key in keys)
        {
            float height = (key >> 2) / 100f;
            bool back = (key & 3L) == 1L;
            Planogram.Board board = Planogram.BoardAt(height);

            string id = Planogram.ProductFor(zone.Section, seed, back, board, endCap);
            ProductDef product = ProductCatalog.Get(id);
            // A cross-merchandised product keeps its own section: cola on the crisps' end cap
            // still only takes cola.
            ItemType section = product != null ? product.Category : zone.Section;

            if (!string.IsNullOrEmpty(id)) Tally(facingsPerProduct, id, 1);
            if (!write) continue;

            Vector2 footprint = FootprintOf(facings[key], bay);
            foreach (ShelfSlot slot in facings[key])
                Write(slot, section, id, footprint, beforeWrite, afterWrite);
        }
    }

    static void Write(ShelfSlot slot, ItemType section, string id, Vector2 footprint,
                      System.Action<Object> beforeWrite, System.Action<Object> afterWrite)
    {
        beforeWrite?.Invoke(slot);
        if (slot.storedItem != null) beforeWrite?.Invoke(slot.storedItem);

        // The item already on the facing has to agree with it, or the player picks cereal off
        // the drinks shelf and then can't put it back. Stock sees to that too.
        slot.Stock(section, id, footprint);

        afterWrite?.Invoke(slot);
        if (slot.storedItem != null) afterWrite?.Invoke(slot.storedItem);
    }

    // How much board one slot of a facing has: the spacing between its slots along the board,
    // less a little air. A facing with one slot (the pillar pieces) gets the default.
    static Vector2 FootprintOf(List<ShelfSlot> facing, Transform bay)
    {
        if (facing.Count < 2) return Backstock.DefaultFootprint;

        var xs = new List<float>(facing.Count);
        foreach (ShelfSlot slot in facing) xs.Add(bay.InverseTransformPoint(slot.transform.position).x);
        xs.Sort();

        float pitch = float.MaxValue;
        for (int i = 1; i < xs.Count; i++)
        {
            float d = xs[i] - xs[i - 1];
            if (d > 0.05f && d < pitch) pitch = d;
        }
        if (pitch == float.MaxValue) return Backstock.DefaultFootprint;
        return new Vector2(Mathf.Clamp(pitch - 0.05f, 0.3f, 0.62f), Backstock.DefaultFootprint.y);
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
        var perProduct = new Dictionary<string, int>();

        // Counting is the same walk as stocking, so this can't drift from what ships.
        int touched = Walk(false, null, null, perProduct, bays, facings);

        var sb = new StringBuilder();
        sb.AppendLine($"Store layout: {touched} facings across {Zones.Length} sections.");
        sb.AppendLine();
        sb.AppendLine("  section                              bays  facings  range");

        foreach (Zone zone in Zones)
        {
            bays.TryGetValue(zone.Section, out int bayCount);
            facings.TryGetValue(zone.Section, out int facingCount);
            sb.AppendLine($"  {zone.Sign,-36} {bayCount,4} {facingCount,8} {ProductCatalog.InSection(zone.Section).Count,6}");
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
