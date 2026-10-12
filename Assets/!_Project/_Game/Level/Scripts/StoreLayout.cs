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
        PlanAisles(plans);
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

            int n = StockBay(plan, writeScene, stock, facingsPerProduct, null, beforeWrite, afterWrite);
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

    // Each board's facings, with the product the bay's own layout would give them: what an end
    // cap keeps, and what any other facing starts from before PlanAisles plans it with its aisle.
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

    // ------------------------------------------------------------------ aisles

    // How long a block of one brand column runs along an aisle before the next takes over.
    const float ColumnRun = 6f;

    // One facing on one side of an aisle: where it is along the aisle, and where its edge is.
    struct AisleFacing
    {
        public BayPlan Plan;
        public int Index;
        public float At, From, To;
    }

    // Products by aisle, not by bay. The facings that face the same way from the same line in
    // one section are one side of one aisle: the two-sided bays, the pillars between them and
    // the one-sided bays along it, whichever way each was turned when it was placed. The
    // section's brand columns (Planogram.ColumnsFor) run along that side in blocks of about
    // ColumnRun metres, each a brand stacked eye over waist over stoop, and the two sides of an
    // aisle start on different columns. End caps keep their promotions.
    static void PlanAisles(List<BayPlan> plans)
    {
        var aisles = new Dictionary<(ItemType section, int dir, int line), List<AisleFacing>>();
        foreach (BayPlan plan in plans)
        {
            Transform bay = plan.Unit.transform;
            if (Planogram.IsEndCap(bay)) continue;
            for (int i = 0; i < plan.Facings.Count; i++)
            {
                ShelfGrid.Facing facing = plan.Facings[i];
                if (facing.Squares.Count == 0) continue;
                int dir = Heading(bay.TransformDirection(ShelfGrid.Outward(facing.Side)));
                Vector3 across = Axis(dir), along = dir % 2 == 0 ? Vector3.forward : Vector3.right;
                float line = 0f, from = float.MaxValue, to = float.MinValue, at = 0f;
                foreach (ShelfGrid.Square sq in facing.Squares)
                {
                    line += Vector3.Dot(bay.TransformPoint(sq.Edge), across);
                    float a = Vector3.Dot(bay.TransformPoint(sq.Centre), along);
                    at += a;
                    from = Mathf.Min(from, a - ShelfGrid.SquareSize * 0.5f);
                    to = Mathf.Max(to, a + ShelfGrid.SquareSize * 0.5f);
                }
                line /= facing.Squares.Count;
                at /= facing.Squares.Count;
                var key = (plan.Zone.Section, dir, Mathf.RoundToInt(line * 4f));
                if (!aisles.TryGetValue(key, out List<AisleFacing> side)) aisles[key] = side = new List<AisleFacing>();
                side.Add(new AisleFacing { Plan = plan, Index = i, At = at, From = from, To = to });
            }
        }

        foreach (var kv in aisles)
        {
            List<string[]> columns = Planogram.ColumnsFor(kv.Key.section);
            if (columns.Count == 0) continue;
            List<AisleFacing> side = kv.Value;
            float start = float.MaxValue, end = float.MinValue;
            foreach (AisleFacing f in side) { start = Mathf.Min(start, f.From); end = Mathf.Max(end, f.To); }
            float length = Mathf.Max(0.01f, end - start);
            int blocks = Mathf.Clamp(Mathf.RoundToInt(length / ColumnRun), 1, columns.Count);
            int first = kv.Key.dir < 2 ? 0 : 1;   // facing +X or +Z, or -X or -Z: the aisle's two sides
            foreach (AisleFacing f in side)
            {
                int block = Mathf.Clamp(Mathf.FloorToInt((f.At - start) / length * blocks), 0, blocks - 1);
                string[] column = columns[(first + block) % columns.Count];
                ShelfGrid.Facing facing = f.Plan.Facings[f.Index];
                string id = column[(int)Planogram.BoardAt(facing.Height)];
                if (!string.IsNullOrEmpty(id)) f.Plan.Ids[f.Index] = id;
            }
        }
    }

    // Which way a direction points, to the nearest of +X (0), +Z (1), -X (2), -Z (3).
    static int Heading(Vector3 d) =>
        Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? (d.x >= 0f ? 0 : 2) : (d.z >= 0f ? 1 : 3);

    static Vector3 Axis(int heading) => heading switch
    {
        0 => Vector3.right, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.back,
    };

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
        PlanAisles(plans);
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
                        uint? lightMask, System.Action<Object> beforeWrite = null, System.Action<Object> afterWrite = null)
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
                var slot = new ShelfSlot(unit, bay, cell.Centre, cell.Side, cell.Size, height);
                slot.lightMask = lightMask ?? RoomMask(slot.Position);
                stock.Add(slot);
                slot.Stock(section, id, filled: true);
                made.Add(slot);
            }
        }
        if (made != null) unit.SetSlots(made);
        if (stock != null) PriceStrips(plan, beforeWrite, afterWrite);

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

    // Each price strip along a board's edge shows the price of what that board sells on its
    // side (PriceTags), in the editor as in the game.
    static void PriceStrips(BayPlan plan, System.Action<Object> beforeWrite, System.Action<Object> afterWrite)
    {
        foreach (ShelfGrid.Strip strip in ShelfGrid.Strips(plan.Unit.transform))
        {
            int f = plan.Facings.FindIndex(x => x.Side == strip.Side && Mathf.Abs(x.Height - strip.Height) < 0.03f);
            if (f < 0) continue;
            Material price = PriceTags.MaterialFor(plan.Ids[f], strip.Length);
            if (price == null || strip.Renderer.sharedMaterial == price) continue;
            beforeWrite?.Invoke(strip.Renderer);
            strip.Renderer.sharedMaterial = price;
            afterWrite?.Invoke(strip.Renderer);
        }
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
