using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Board = Planogram.Board;

// The shop is merchandised the way a real supermarket is (Planogram), its boards cut into
// slots that fit what they sell (ShelfGrid), divided into numbered aisles (StoreLayout), and
// shopped by people with lists who ask for things the way people do (ShoppingList,
// CustomerQuestion).
public class MerchandisingTests
{
    static IEnumerable<(ItemType section, Planogram.Bay bay)> AllLayouts() =>
        ProductCatalog.StockSections.SelectMany(s => Planogram.LayoutsFor(s).Select(b => (s, b)));

    static IEnumerable<(string id, bool back, Board board)> Boards(Planogram.Bay bay)
    {
        foreach (Board board in new[] { Board.Stoop, Board.Waist, Board.Eye })
        {
            yield return (bay.At(false, board), false, board);
            yield return (bay.At(true, board), true, board);
        }
    }

    // ------------------------------------------------------------------ planogram

    [Test]
    public void EveryProduct_HasABoardInItsSection()
    {
        foreach (ProductDef p in ProductCatalog.All)
            Assert.Greater(Planogram.Share(p.Id), 0, $"{p.Id} is in none of {p.Category}'s layouts");
    }

    [Test]
    public void Layouts_OnlyNameRealProductsOfTheirOwnSection()
    {
        foreach (var (section, bay) in AllLayouts())
            foreach (var (id, _, _) in Boards(bay))
            {
                ProductDef p = ProductCatalog.Get(id);
                Assert.IsNotNull(p, $"{section} layout names unknown product {id}");
                Assert.AreEqual(section, p.Category, $"{id} is laid out in {section}");
            }
    }

    [Test]
    public void NothingHeavy_GoesOnTheTopBoard()
    {
        foreach (var (section, bay) in AllLayouts())
            foreach (var (id, _, board) in Boards(bay))
                if (board == Board.Eye)
                    Assert.Less(ProductCatalog.Get(id).Mass, Planogram.HeavyKg, $"{id} ({section}) at eye level");
    }

    [Test]
    public void ChildrensLines_SitBelowEyeLevel()
    {
        foreach (var (_, bay) in AllLayouts())
            foreach (var (id, _, board) in Boards(bay))
                if (Planogram.KidsProducts.Contains(id))
                    Assert.AreNotEqual(Board.Eye, board, $"{id} at eye level");
    }

    [Test]
    public void TheHeaviestOfEachSection_IsOnTheBottomBoard()
    {
        // Rice, kibble, the litre of water, the laundry box...
        foreach (ItemType section in ProductCatalog.StockSections)
        {
            ProductDef heaviest = ProductCatalog.InSection(section).OrderByDescending(p => p.Mass).First();
            if (heaviest.Mass < Planogram.HeavyKg) continue;
            foreach (Planogram.Bay bay in Planogram.LayoutsFor(section))
                foreach (var (id, _, board) in Boards(bay))
                    if (id == heaviest.Id)
                        Assert.AreNotEqual(Board.Eye, board, $"{id}, {section}'s heaviest, at eye level");
        }
    }

    [Test]
    public void ABrandsLines_ShareOneFaceOfABay()
    {
        // Produce is merchandised by kind, not brand: the store's own label covers it all.
        foreach (var (section, bay) in AllLayouts())
        {
            if (section == ItemType.Produce) continue;
            var faceOf = new Dictionary<string, bool>();
            foreach (var (id, back, _) in Boards(bay))
            {
                if (bay.Front.Contains(id) && bay.Back.Contains(id)) continue;   // a best seller on both faces
                string brand = ProductCatalog.Get(id).Brand;
                if (faceOf.TryGetValue(brand, out bool face))
                    Assert.AreEqual(face, back, $"{brand} is split across a {section} bay");
                else faceOf[brand] = back;
            }
        }
    }

    [Test]
    public void BoardHeights_ReadAsEyeWaistAndStoop()
    {
        Assert.AreEqual(Board.Eye, Planogram.BoardAt(1.42f));
        Assert.AreEqual(Board.Waist, Planogram.BoardAt(0.83f));
        Assert.AreEqual(Board.Waist, Planogram.BoardAt(0.75f), "a short bay's top board");
        Assert.AreEqual(Board.Stoop, Planogram.BoardAt(0.24f));
    }

    [Test]
    public void EverySection_HasAnEndCap_AndTheCounterSellsSweets()
    {
        foreach (ItemType section in ProductCatalog.StockSections)
        {
            IReadOnlyList<string> caps = Planogram.EndCapsFor(section);
            Assert.IsNotEmpty(caps, $"{section} has no end cap");
            foreach (string id in caps) Assert.IsNotNull(ProductCatalog.Get(id), $"end cap names unknown {id}");
        }
        Assert.AreEqual(ItemType.Confectionery, ProductCatalog.Get(Planogram.CounterProduct).Category);
    }

    [Test]
    public void CrispsAndCola_AreCrossMerchandised()
    {
        Assert.Contains("drink_pipisi", Planogram.EndCapsFor(ItemType.Snacks).ToList());
        Assert.Contains("snack_krunchos", Planogram.EndCapsFor(ItemType.SoftDrinks).ToList());
    }

    [Test]
    public void ProductFor_AlternatesLayouts_AndUsesEndCapsOnEnds()
    {
        string a = Planogram.ProductFor(ItemType.Snacks, 0, false, Board.Eye, false);
        string b = Planogram.ProductFor(ItemType.Snacks, 1, false, Board.Eye, false);
        Assert.AreNotEqual(a, b, "neighbouring layouts look the same");
        Assert.Contains(Planogram.ProductFor(ItemType.Snacks, 0, false, Board.Eye, true),
                        Planogram.EndCapsFor(ItemType.Snacks).ToList());
    }

    // The sweets are on one-sided bays, where Mochi Bites' back faces never show, and health
    // and beauty on short bays without the eye-level board sanitiser is on: neither was on sale.
    [Test]
    public void EveryProduct_GetsAFacing_OnTheBaysItsSectionHas()
    {
        var oneSided = new[] { Board.Stoop, Board.Waist, Board.Eye };
        var shortBay = new[] { Board.Stoop, Board.Waist };
        foreach (ItemType section in ProductCatalog.StockSections)
            foreach (Board[] shape in new[] { oneSided, shortBay })
            {
                var ids = new List<string>();
                var boards = new List<Board>();
                for (int seed = 0; seed < 6; seed++)
                    foreach (Board board in shape)
                    {
                        ids.Add(Planogram.ProductFor(section, seed, false, board, false));
                        boards.Add(board);
                    }
                Planogram.GiveEveryProductAFacing(section, ids, boards);

                string bays = shape == oneSided ? "one-sided bays" : "short bays";
                foreach (ProductDef p in ProductCatalog.InSection(section))
                    Assert.Contains(p.Id, ids, $"{p.Id} isn't on sale on {bays}");
                for (int i = 0; i < ids.Count; i++)
                    Assert.IsTrue(Planogram.MayGoOn(ProductCatalog.Get(ids[i]), boards[i]), $"{ids[i]} on the {boards[i]} board");
            }
    }

    // ------------------------------------------------------------------ the shelf grid

    static List<ShelfGrid.Facing> FacingsOfBoard(float width, float depth, float z = 0f)
    {
        var bay = new GameObject("TestBay");
        try
        {
            var board = new GameObject("Polka1");
            board.transform.SetParent(bay.transform, false);
            board.transform.localPosition = new Vector3(0f, 0.82f, z);
            board.transform.localScale = new Vector3(width, 0.02f, depth);
            return ShelfGrid.Facings(bay.transform);
        }
        finally { Object.DestroyImmediate(bay); }
    }

    [Test]
    public void Boards_AreCutIntoHalfMetreSquares()
    {
        // A two-sided 4 m board: 8 along each face, 16 in all.
        List<ShelfGrid.Facing> two = FacingsOfBoard(4f, 1f);
        Assert.AreEqual(2, two.Count, "a face each way");
        Assert.AreEqual(16, two.Sum(f => f.Squares.Count));
        Assert.IsTrue(two.All(f => f.Squares.Count == 8));
        Assert.AreEqual(8, FacingsOfBoard(4f, 0.5f, -0.25f).Sum(f => f.Squares.Count), "one-sided");
        Assert.AreEqual(4, FacingsOfBoard(1f, 1f).Sum(f => f.Squares.Count), "a pillar");
        Assert.AreEqual(2, FacingsOfBoard(1f, 0.5f, -0.25f).Sum(f => f.Squares.Count), "a b pillar");
        Assert.AreEqual(1, FacingsOfBoard(0.5f, 0.5f, -0.25f).Sum(f => f.Squares.Count), "a tail");
    }

    // A pillar's board sits off its origin (the 1 m pillars' at z = -2). Its two rows face the
    // two aisles, each with its own facing, not both the front.
    [Test]
    public void APillarsBoard_FacesBothAisles_WhereverItSits()
    {
        List<ShelfGrid.Facing> pillar = FacingsOfBoard(1f, 1f, -2f);
        Assert.AreEqual(1, pillar.Count(f => !f.Back), "a front facing");
        Assert.AreEqual(1, pillar.Count(f => f.Back), "and a back one");
        Assert.AreEqual(2, pillar.First(f => f.Back).Squares.Count);
        // One square deep, a board faces the front, wherever it sits.
        Assert.IsTrue(FacingsOfBoard(1f, 0.5f, -2.25f).All(f => !f.Back), "a b pillar");
        Assert.IsTrue(FacingsOfBoard(4f, 0.5f, -0.25f).All(f => !f.Back), "one-sided");
    }

    // A pillar board with price strips on some of its edges: those are the sides it sells to.
    static List<ShelfGrid.Square> SquaresWithStrips(float width, float depth, float z, params ShelfGrid.Side[] sides)
    {
        var bay = new GameObject("TestPillar");
        try
        {
            var board = new GameObject("Polka1");
            board.transform.SetParent(bay.transform, false);
            board.transform.localPosition = new Vector3(2.5f, 0.82f, z);
            board.transform.localScale = new Vector3(width, 0.02f, depth);
            foreach (ShelfGrid.Side side in sides)
            {
                GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Pricetag " + side;
                strip.transform.SetParent(bay.transform, false);
                bool alongX = side == ShelfGrid.Side.Front || side == ShelfGrid.Side.Back;
                float x = side == ShelfGrid.Side.Left ? 2.5f - width * 0.5f : side == ShelfGrid.Side.Right ? 2.5f + width * 0.5f : 2.5f;
                float edgeZ = side == ShelfGrid.Side.Front ? z - depth * 0.5f : side == ShelfGrid.Side.Back ? z + depth * 0.5f : z;
                strip.transform.localPosition = new Vector3(x, 0.79f, edgeZ);
                strip.transform.localScale = alongX ? new Vector3(width, 0.15f, 0.04f) : new Vector3(0.04f, 0.15f, depth);
            }
            List<ShelfGrid.Board> boards = ShelfGrid.Boards(bay.transform);
            return ShelfGrid.SquaresOf(boards[0], ShelfGrid.Strips(bay.transform, boards)).ToList();
        }
        finally { Object.DestroyImmediate(bay); }
    }

    // The shelf models' price strips are on the sides that sell: where a perpendicular bay
    // covers a pillar's side, there's none, and nothing faces that way.
    [Test]
    public void APillar_SellsOnlyToTheSidesItsPriceStripsAreOn()
    {
        var front = ShelfGrid.Side.Front;
        var back = ShelfGrid.Side.Back;
        var left = ShelfGrid.Side.Left;
        var right = ShelfGrid.Side.Right;
        Assert.IsTrue(SquaresWithStrips(1f, 1f, -2f, front, back).Count(q => q.Side == front) == 2, "both aisles: a row each");
        Assert.IsTrue(SquaresWithStrips(1f, 1f, -2f, front).All(q => q.Side == front), "a T's stem covers its back");
        Assert.IsTrue(SquaresWithStrips(1f, 0.5f, -2.25f, back).All(q => q.Side == back), "a b pillar sells from its back");
        List<ShelfGrid.Square> sideways = SquaresWithStrips(1f, 0.5f, -1.75f, left, right);
        Assert.AreEqual(1, sideways.Count(q => q.Side == left), "one square to each side");
        Assert.AreEqual(1, sideways.Count(q => q.Side == right));
        // An L: the front row faces front, the back corner on the open side faces it, and the
        // square inside faces front behind the row in front of it.
        List<ShelfGrid.Square> l = SquaresWithStrips(1f, 1f, -2f, front, right);
        Assert.AreEqual(3, l.Count(q => q.Side == front));
        Assert.AreEqual(1, l.Count(q => q.Side == right));
        Assert.IsTrue(l.Single(q => q.Side == right).Centre.z > -2f, "the corner behind the front row");
    }

    // A square that sells sideways has its packs turned a quarter: a long pack's length runs
    // along the bay, not across it.
    [Test]
    public void ASidewaysSquare_TurnsItsPacks()
    {
        ProductDef bread = ProductCatalog.Get("bakery_shokupan");
        var facing = new ShelfGrid.Facing(0.82f, ShelfGrid.Side.Right);
        facing.Squares.Add(new ShelfGrid.Square(new Vector3(2.75f, 0.82f, -1.75f), new Vector2(0.5f, 0.5f), ShelfGrid.Side.Right));
        var front = new ShelfGrid.Facing(0.82f, ShelfGrid.Side.Front);
        front.Squares.Add(new ShelfGrid.Square(new Vector3(2.75f, 0.82f, -1.75f), new Vector2(0.5f, 0.5f), ShelfGrid.Side.Front));
        ShelfGrid.Cell[] turned = ShelfGrid.CellsOf(facing, bread).ToArray();
        ShelfGrid.Cell[] straight = ShelfGrid.CellsOf(front, bread).ToArray();
        Assert.AreEqual(straight.Length, turned.Length);
        Assert.AreEqual(straight[0].Size.x, turned[0].Size.y, 1e-4f);
        Assert.AreEqual(straight[0].Size.y, turned[0].Size.x, 1e-4f);
        Assert.IsTrue(turned.All(c => c.Side == ShelfGrid.Side.Right));
    }

    // One side of an aisle is planned as one run, whichever way each bay along it was turned:
    // the section's brand columns run along it in blocks.
    [Test]
    public void AnAislesSide_RunsInBlocks_WhicheverWayEachBayFaces()
    {
        var made = new List<GameObject>();
        try
        {
            var units = new List<ShelfUnit>();
            for (int i = 0; i < 3; i++)
            {
                var bay = new GameObject("ShelfTwoside_test " + i);
                made.Add(bay);
                bay.transform.position = new Vector3(i * 4f, 0f, 0f);
                // The middle bay was placed the other way round: its back faces this aisle.
                bay.transform.rotation = Quaternion.Euler(0f, i == 1 ? 180f : 0f, 0f);
                // Three boards, so every product of the section has one and none is moved.
                foreach (float height in new[] { 0.23f, 0.82f, 1.41f })
                {
                    var board = new GameObject("Polka");
                    board.transform.SetParent(bay.transform, false);
                    board.transform.localPosition = new Vector3(0f, height, 0f);
                    board.transform.localScale = new Vector3(4f, 0.02f, 1f);
                }
                units.Add(bay.AddComponent<ShelfUnit>());
            }
            var stock = new ShelfStock();
            StoreLayout.StockBays(units, ItemType.Bakery, "Aisle test", stock);

            // What each bay's eye-level board sells to the aisle on -Z.
            string SouthOf(ShelfUnit u) => u.Slots.First(s => s.Outward.z < -0.5f && s.local.y > 1.2f).productId;
            List<string[]> columns = Planogram.ColumnsFor(ItemType.Bakery);
            int eye = (int)Board.Eye;
            Assert.AreEqual(columns[1][eye], SouthOf(units[0]), "the first block");
            Assert.AreEqual(columns[2][eye], SouthOf(units[1]), "the turned bay takes its block's product");
            Assert.AreEqual(SouthOf(units[1]), SouthOf(units[2]), "and so does the next");
        }
        finally { foreach (GameObject go in made) Object.DestroyImmediate(go); }
    }

    [Test]
    public void ThickBases_AreNotBoards()
    {
        var bay = new GameObject("TestBay");
        try
        {
            var block = new GameObject("Shelf_tail_polka2");
            block.transform.SetParent(bay.transform, false);
            block.transform.localScale = new Vector3(1f, 0.69f, 0.5f);
            Assert.IsEmpty(ShelfGrid.Boards(bay.transform));
        }
        finally { Object.DestroyImmediate(bay); }
    }

    static Vector2Int Cells(string id) =>
        ShelfGrid.CellsFor(ProductCatalog.Get(id), ShelfGrid.PackSize(ProductCatalog.Get(id)), new Vector2(0.5f, 0.5f));

    [Test]
    public void SmallPacks_GoFourToASquare()
    {
        Assert.AreEqual(new Vector2Int(2, 2), Cells("canned_tuna"));
        Assert.AreEqual(new Vector2Int(2, 2), Cells("sweet_pokki"));
    }

    [Test]
    public void LongPacks_GoTwoToASquare_TheWayTheyFit()
    {
        Assert.AreEqual(new Vector2Int(2, 1), Cells("bakery_sourdough"), "a loaf runs into the shelf");
        Assert.AreEqual(new Vector2Int(1, 2), Cells("cereal_chocoloops"), "a cereal box is wide and shallow");
    }

    [Test]
    public void Drinks_FillTheirSquare_ByWhatTheyTakeUp()
    {
        Vector2Int can = Cells("drink_kokakora");
        Assert.Greater(can.x * can.y, 4, "cans pack tighter than four to a square");
        Assert.LessOrEqual(can.x, ShelfGrid.MaxDrinksAcross);
        Vector3 water = ShelfGrid.PackSize(ProductCatalog.Get("drink_aquapura"));
        Vector2Int bottles = Cells("drink_aquapura");
        Assert.LessOrEqual(bottles.x * (water.x + ShelfGrid.Gap), 0.5f + ShelfGrid.Gap, "the litre bottles fit across");
    }

    [Test]
    public void EveryProduct_FitsItsCell_AndUnderTheBoardAbove()
    {
        var square = new Vector2(0.5f, 0.5f);
        foreach (ProductDef p in ProductCatalog.All)
        {
            Vector3 size = ShelfGrid.PackSize(p);
            Vector2Int cells = ShelfGrid.CellsFor(p, size, square);
            Assert.GreaterOrEqual(cells.x * cells.y, 1, p.Id);
            if (cells != Vector2Int.one)
            {
                Assert.LessOrEqual(size.x, square.x / cells.x + 0.01f, $"{p.Id} too wide for {cells}");
                Assert.LessOrEqual(size.z, square.y / cells.y + 0.01f, $"{p.Id} too deep for {cells}");
            }
            Assert.LessOrEqual(size.y, ProductLook.MaxDisplayHeight + 0.01f, $"{p.Id} pokes into the board above");
        }
    }

    [Test]
    public void Products_AreShownBigger_ButTheTallestFit()
    {
        Assert.AreEqual(ProductLook.DisplayScale, ProductLook.ScaleFor(ProductCatalog.Get("drink_kokakora")), 1e-4f);
        ProductDef daikon = ProductCatalog.Get("produce_daikon");
        Assert.Less(ProductLook.ScaleFor(daikon), ProductLook.DisplayScale);
        Assert.LessOrEqual(daikon.Size.y * ProductLook.ScaleFor(daikon), ProductLook.MaxDisplayHeight + 1e-4f);
    }

    // An item's rest height (from its origin down to its base) is measured from its mesh when
    // first asked for, so it's right even where Awake hasn't run, as in the editor.
    [Test]
    public void AnItem_KnowsHowFarItStandsOffWhatItsOn_EvenInTheEditor()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            go.transform.localScale = new Vector3(1f, 0.3f, 1f);
            var item = go.AddComponent<Item>();
            Assert.AreEqual(0.15f, item.RestHeight, 1e-4f);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // ------------------------------------------------------------------ aisles

    [Test]
    public void TheWholeShop_IsAisles1To13_InWalkingOrder()
    {
        var aisles = StoreLayout.Zones.Select(z => z.Aisle).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, StoreLayout.Zones.Length).ToArray(), aisles);
        foreach (StoreLayout.Zone z in StoreLayout.Zones)
        {
            Assert.IsFalse(string.IsNullOrEmpty(z.Name), z.Sign);
            Assert.IsFalse(string.IsNullOrEmpty(z.Japanese), z.Sign);
            StringAssert.StartsWith("the ", z.Spoken, z.Sign);
            StringAssert.StartsWith($"Aisle {z.Aisle} ", z.Sign);
            Assert.AreEqual($"AISLE {z.Aisle}", AisleSigns.Title(z));
            Assert.AreEqual(z.Aisle, StoreLayout.WalkOrder(z.Section));
        }
        Assert.AreEqual(1, StoreLayout.ZoneOf(ItemType.Produce).Aisle, "fresh food by the door");
        Assert.AreEqual(StoreLayout.Zones.Length, StoreLayout.ZoneOf(ItemType.Confectionery).Aisle, "sweets at the tills");
    }

    // ------------------------------------------------------------------ customers

    [Test]
    public void AisleQuestions_NameTheAisle()
    {
        foreach (StoreLayout.Zone zone in StoreLayout.Zones)
        {
            ProductDef p = ProductCatalog.InSection(zone.Section)[0];
            for (int pick = 0; pick < 3; pick++)
                StringAssert.Contains($"aisle {zone.Aisle}", CustomerQuestion.Ask(CustomerQuestion.Kind.Aisle, p, zone, pick).ToLowerInvariant());
            StringAssert.Contains($"Aisle {zone.Aisle}", CustomerQuestion.Thanks(CustomerQuestion.Kind.Aisle, p, zone));
        }
    }

    [Test]
    public void ProductQuestions_AndThanks_NameTheProduct()
    {
        ProductDef p = ProductCatalog.Get("drink_pipisi_zero");
        StoreLayout.Zone z = StoreLayout.ZoneOf(p.Category);
        foreach (CustomerQuestion.Kind kind in new[] { CustomerQuestion.Kind.Product, CustomerQuestion.Kind.WhichAisle, CustomerQuestion.Kind.Missing })
            for (int pick = 0; pick < 5; pick++)
                StringAssert.Contains("Pipisi Zero", CustomerQuestion.Ask(kind, p, z, pick));
        StringAssert.Contains("Pipisi Zero", CustomerQuestion.Thanks(CustomerQuestion.Kind.Product, p, z));
        StringAssert.Contains("Aisle 3", CustomerQuestion.Thanks(CustomerQuestion.Kind.Aisle, p, z));
    }

    [Test]
    public void EveryKindOfQuestion_GetsAsked()
    {
        StoreLayout.Zone dairy = StoreLayout.ZoneOf(ItemType.Dairy);
        var kinds = new HashSet<CustomerQuestion.Kind>();
        for (float roll = 0f; roll < 1f; roll += 0.05f) kinds.Add(CustomerQuestion.Choose(roll, false, dairy));
        CollectionAssert.AreEquivalent(new[] { CustomerQuestion.Kind.Product, CustomerQuestion.Kind.WhichAisle, CustomerQuestion.Kind.Aisle }, kinds);
        Assert.AreEqual(CustomerQuestion.Kind.Missing, CustomerQuestion.Choose(0.1f, true, dairy));
    }

    [Test]
    public void AShoppingList_IsDistinct_AndWalkedInStoreOrder()
    {
        var random = new System.Random(7);
        for (int run = 0; run < 50; run++)
        {
            List<ProductDef> list = ShoppingList.Pick(4, random);
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(4, list.Select(p => p.Id).Distinct().Count());
            for (int i = 1; i < list.Count; i++)
                Assert.LessOrEqual(StoreLayout.WalkOrder(list[i - 1].Category), StoreLayout.WalkOrder(list[i].Category));
        }
    }

    [Test]
    public void Milk_IsBoughtMoreOftenThanHamsterBedding()
    {
        var random = new System.Random(11);
        int milk = 0, hamster = 0;
        for (int run = 0; run < 2000; run++)
            foreach (ProductDef p in ShoppingList.Pick(3, random))
            {
                if (p.Id == "dairy_milk_whole") milk++;
                if (p.Id == "pet_hamster_bed") hamster++;
            }
        Assert.Greater(milk, hamster * 2, $"milk {milk}, hamster bedding {hamster}");
    }
}
