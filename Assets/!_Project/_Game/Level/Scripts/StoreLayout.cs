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
// reviewed or shared; and 16,500 slots are a lot of scene to carry for something worked out
// in a few milliseconds. The stock it makes is data (ShelfStock), drawn by ShelfDrawer, which
// also shows it in the editor. Kehai/Store/Apply Layout writes each bay's aisle into the
// scene when you want the Inspector to show it.
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
    // boards are cut into ShelfGrid's slots and filled from the planogram, as data. Then the
    // drawer that shows the stock, the aisle signs and the lamps go up, and each room's
    // lights are kept in their room. Scenes loaded later (the eval harness reloads the store
    // for every episode) get the same.
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
        ShelfDrawer.Ensure();
        AisleSigns.Build();
        CeilingLamps.Ensure();
        RoomLighting.Apply();
    }

    // Stocks the shop (ShelfStock.Current) and writes each bay's aisle onto it. Optional
    // hooks let the editor pass wrap each write in an Undo record and register the prefab
    // override; at runtime both are null and this is a plain assignment.
    public static int ApplyToScene(System.Action<Object> beforeWrite = null,
                                   System.Action<Object> afterWrite = null) =>
        Stock(true, beforeWrite, afterWrite);

    // The stock the editor shows: the same, without touching the scene.
    public static int BuildPreview() => Stock(false, null, null);

    static int Stock(bool writeScene, System.Action<Object> beforeWrite, System.Action<Object> afterWrite)
    {
        Planogram.Clear();
        ShelfStock stock = ShelfStock.Current;
        stock.Clear();
        int slots = Walk(writeScene, stock, beforeWrite, afterWrite, null, null, null, null);
        stock.Finish();
        return slots;
    }

    // One walk of the shop, used both to stock it and to count it (`stock` null). Counting
    // takes the same path as stocking so a report can never describe a store the game
    // doesn't build.
    static int Walk(bool writeScene,
                    ShelfStock stock,
                    System.Action<Object> beforeWrite,
                    System.Action<Object> afterWrite,
                    Dictionary<string, int> facingsPerProduct,
                    Dictionary<ItemType, int> baysPerSection,
                    Dictionary<ItemType, int> facingsPerSection,
                    Dictionary<ItemType, int> slotsPerSection)
    {
        int slots = 0;

        // Every bay is planned first, so that a product whose boards its section doesn't have
        // can be given one (Planogram.GiveEveryProductAFacing). In position order, so the plan
        // comes out the same whatever order Unity finds the bays in.
        var plans = new List<BayPlan>();
        foreach (ShelfUnit unit in Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include))
            plans.Add(PlanBay(unit, ZoneAt(unit.transform.position)));
        plans.Sort((a, b) => ComparePositions(a.Unit.transform.position, b.Unit.transform.position));
        foreach (ItemType section in ProductCatalog.StockSections) FillGaps(plans, section);

        foreach (BayPlan plan in plans)
        {
            ShelfUnit unit = plan.Unit;
            Zone zone = plan.Zone;

            if (writeScene)
            {
                beforeWrite?.Invoke(unit);
                unit.section = zone.Sign;
                unit.category = zone.Section;
                afterWrite?.Invoke(unit);
            }

            int n = StockBay(plan, writeScene, stock, facingsPerProduct, null);
            slots += n;

            Tally(baysPerSection, zone.Section, 1);
            Tally(facingsPerSection, zone.Section, plan.Facings.Count);
            Tally(slotsPerSection, zone.Section, n);
        }

        // A facing stands loose in the scene rather than on a bay: the one on the till counter.
        // It carries the impulse buy every till in the world has: mints.
        ProductDef counter = ProductCatalog.Get(Planogram.CounterProduct);
        foreach (CounterFacing marker in Object.FindObjectsByType<CounterFacing>(FindObjectsInactive.Exclude))
        {
            if (counter == null) break;
            Tally(facingsPerProduct, counter.Id, 1);
            Tally(facingsPerSection, counter.Category, 1);
            Tally(slotsPerSection, counter.Category, 1);
            slots++;
            if (stock == null) continue;
            var slot = new ShelfSlot(null, marker.transform, Vector3.zero, false, marker.size,
                                     ShelfGrid.PackSize(counter).y + 0.04f);
            slot.lightMask = RoomMask(slot.Position);
            stock.Add(slot);
            slot.Stock(counter.Category, counter.Id, filled: true);
        }

        return slots;
    }

    // One bay's facings and the product each sells. A facing is one board on one side of the
    // bay — the whole of the top board, front side, is Pipisi and nothing else — which is how
    // a real planogram is blocked out.
    sealed class BayPlan
    {
        public ShelfUnit Unit;
        public Zone Zone;
        public List<ShelfGrid.Facing> Facings;
        public string[] Ids;
    }

    // Planogram picks the product for each board from its height (eye, waist or stoop level)
    // and side.
    static BayPlan PlanBay(ShelfUnit unit, Zone zone)
    {
        Transform bay = unit.transform;

        // Which of the section's layouts this bay gets. Derived from its position so the plan
        // is the same every run, and so neighbouring bays don't all look alike.
        int seed = Mathf.Abs(Mathf.RoundToInt(bay.position.x) * 73856093 ^
                             Mathf.RoundToInt(bay.position.z) * 19349663);
        bool endCap = Planogram.IsEndCap(bay);

        List<ShelfGrid.Facing> facings = ShelfGrid.Facings(bay);
        var ids = new string[facings.Count];
        for (int i = 0; i < facings.Count; i++)
            ids[i] = Planogram.ProductFor(zone.Section, seed, facings[i].Back, Planogram.BoardAt(facings[i].Height), endCap);
        return new BayPlan { Unit = unit, Zone = zone, Facings = facings, Ids = ids };
    }

    // A section's facings across all its bays, handed to the planogram to fill any product
    // that has none.
    static void FillGaps(List<BayPlan> plans, ItemType section)
    {
        var ids = new List<string>();
        var boards = new List<Planogram.Board>();
        foreach (BayPlan plan in plans)
        {
            if (plan.Zone.Section != section) continue;
            ids.AddRange(plan.Ids);
            foreach (ShelfGrid.Facing facing in plan.Facings) boards.Add(Planogram.BoardAt(facing.Height));
        }
        Planogram.GiveEveryProductAFacing(section, ids, boards);

        int k = 0;
        foreach (BayPlan plan in plans)
        {
            if (plan.Zone.Section != section) continue;
            for (int i = 0; i < plan.Ids.Length; i++) plan.Ids[i] = ids[k++];
        }
    }

    // What stands on a slot in the store is lit by that room's lights only, like the shelf.
    static uint RoomMask(Vector3 p) => RoomLighting.LayersDefined ? (uint)RoomLighting.BitAt(p) : RoomLighting.Moving;

    // Stocks bays that aren't the store's: the endless maze's, a chunk at a time, every bay
    // selling `section` under the sign `sign`. Their slots go into `stock`, lit by every light.
    public static List<ShelfSlot> StockBays(IReadOnlyList<ShelfUnit> units, ItemType section, string sign, ShelfStock stock)
    {
        var zone = new Zone { Sign = sign, Section = section };
        var plans = new List<BayPlan>();
        foreach (ShelfUnit unit in units) plans.Add(PlanBay(unit, zone));
        plans.Sort((a, b) => ComparePositions(a.Unit.transform.position, b.Unit.transform.position));
        FillGaps(plans, section);

        var made = new List<ShelfSlot>();
        foreach (BayPlan plan in plans)
        {
            plan.Unit.section = sign;
            plan.Unit.category = section;
            StockBay(plan, false, stock, null, RoomLighting.Moving);
            made.AddRange(plan.Unit.Slots);
        }
        return made;
    }

    static int ComparePositions(Vector3 a, Vector3 b)
    {
        int x = a.x.CompareTo(b.x);
        return x != 0 ? x : a.z.CompareTo(b.z);
    }

    // Stocks one bay from its plan: ShelfGrid cuts each board into slots for its product, and
    // each slot starts full. With no `stock` it only counts them.
    // `lightMask`: what lights it, if not the room it stands in.
    static int StockBay(BayPlan plan, bool writeScene, ShelfStock stock, Dictionary<string, int> facingsPerProduct,
                        uint? lightMask)
    {
        Transform bay = plan.Unit.transform;
        ShelfUnit unit = plan.Unit;
        Zone zone = plan.Zone;
        var made = stock != null ? new List<ShelfSlot>() : null;

        int slots = 0;
        for (int f = 0; f < plan.Facings.Count; f++)
        {
            ShelfGrid.Facing facing = plan.Facings[f];
            string id = plan.Ids[f];
            ProductDef product = ProductCatalog.Get(id);
            // A cross-merchandised product keeps its own section: cola on the crisps' end cap
            // still only takes cola.
            ItemType section = product != null ? product.Category : zone.Section;
            float height = product != null ? ShelfGrid.PackSize(product).y + 0.04f : 0.4f;

            if (!string.IsNullOrEmpty(id)) Tally(facingsPerProduct, id, 1);
            foreach (ShelfGrid.Cell cell in ShelfGrid.CellsOf(facing, product))
            {
                slots++;
                if (stock == null) continue;
                var slot = new ShelfSlot(unit, bay, cell.Centre, cell.Back, cell.Size, height);
                slot.lightMask = lightMask ?? RoomMask(slot.Position);
                stock.Add(slot);
                slot.Stock(section, id, filled: true);
                made.Add(slot);
            }
        }
        if (made != null) unit.SetSlots(made);

        // A scene from before stock was data still has the shelf prefab's own slots, six to a
        // board, and the slots and items the old bake left: both are superseded (Kehai/Store/
        // Migrate to Data Shelves clears them out).
        if (writeScene && Application.isPlaying)
            foreach (string legacy in new[] { "Slots", ShelfGrid.LegacyRootName })
            {
                Transform old = bay.Find(legacy);
                if (old != null && old.gameObject.activeSelf) old.gameObject.SetActive(false);
            }
        return slots;
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
        int total = Walk(false, null, null, null, perProduct, bays, facings, slots);

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
