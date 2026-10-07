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
