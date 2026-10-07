using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Board = Planogram.Board;

// The shop is merchandised the way a real supermarket is (Planogram), stocked in blocks that
// fit their shelves (Backstock), divided into numbered aisles and named departments
// (StoreLayout), and shopped by people with lists who ask for things the way people do
// (ShoppingList, CustomerQuestion).
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

    // ------------------------------------------------------------------ backstock blocks

    [Test]
    public void EveryProduct_FitsItsBlock_InsideTheSlot()
    {
        var footprints = new[] { new Vector2(0.62f, 0.44f), Backstock.DefaultFootprint, new Vector2(0.3f, 0.44f) };
        foreach (ProductDef p in ProductCatalog.All)
            foreach (Vector2 f in footprints)
            {
                Backstock.Block block = Backstock.Fit(p.Size, f);
                Assert.GreaterOrEqual(block.Units, 1, p.Id);
                Assert.LessOrEqual(block.Units, Backstock.MaxUnits, p.Id);
                // Only a pack wider than the slot itself may overhang it.
                if (p.Size.x <= f.x) Assert.LessOrEqual(block.Size.x + p.Size.x, f.x + 0.02f, $"{p.Id} too wide for {f}");
                if (p.Size.z <= f.y) Assert.LessOrEqual(block.Size.z + p.Size.z, f.y + 0.02f, $"{p.Id} too deep for {f}");
                Assert.LessOrEqual(block.Size.y + p.Size.y, 0.45f, $"{p.Id} stacked into the board above");
            }
    }

    [Test]
    public void SmallTins_AreStacked_TallBottlesAreNot()
    {
        Assert.Greater(Backstock.Fit(ProductCatalog.Get("canned_tuna").Size, Backstock.DefaultFootprint).Stack, 1);
        Assert.AreEqual(1, Backstock.Fit(ProductCatalog.Get("drink_aquapura").Size, Backstock.DefaultFootprint).Stack);
    }

    [Test]
    public void TheItemsOwnCell_IsPartOfTheBlock()
    {
        Backstock.Block block = Backstock.Fit(ProductCatalog.Get("drink_kokakora").Size, new Vector2(0.62f, 0.44f));
        Assert.AreEqual(block.CellAt(block.Across / 2, block.Deep / 2, 0), block.ItemCell);
        Assert.LessOrEqual(Mathf.Abs(block.ItemCell.x), block.Pitch.x);
    }

    // ------------------------------------------------------------------ aisles

    [Test]
    public void TheMiddleOfTheShop_IsAisles1To7_AndTheEdgeIsNamedDepartments()
    {
        var aisles = StoreLayout.Zones.Where(z => z.IsAisle).Select(z => z.Aisle).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, aisles);
        foreach (StoreLayout.Zone z in StoreLayout.Zones)
        {
            Assert.IsFalse(string.IsNullOrEmpty(z.Name), z.Sign);
            Assert.IsFalse(string.IsNullOrEmpty(z.Japanese), z.Sign);
            StringAssert.StartsWith("the ", z.Spoken, z.Sign);
            if (z.IsAisle) StringAssert.StartsWith($"Aisle {z.Aisle} ", z.Sign);
            Assert.AreEqual(z.IsAisle ? $"AISLE {z.Aisle}" : z.Name.ToUpperInvariant(), AisleSigns.Title(z));
        }
    }

    // ------------------------------------------------------------------ customers

    [Test]
    public void AisleQuestions_NameTheAisle_DepartmentQuestionsDont()
    {
        ProductDef tuna = ProductCatalog.Get("canned_tuna");
        StoreLayout.Zone tins = StoreLayout.ZoneOf(ItemType.Canned);
        for (int pick = 0; pick < 3; pick++)
            StringAssert.Contains("aisle 3", CustomerQuestion.Ask(CustomerQuestion.Kind.Aisle, tuna, tins, pick).ToLowerInvariant());

        ProductDef bread = ProductCatalog.Get("bakery_shokupan");
        StoreLayout.Zone bakery = StoreLayout.ZoneOf(ItemType.Bakery);
        for (int pick = 0; pick < 3; pick++)
        {
            string line = CustomerQuestion.Ask(CustomerQuestion.Kind.Department, bread, bakery, pick);
            StringAssert.Contains("the bakery", line);
            StringAssert.DoesNotContain("aisle", line.ToLowerInvariant());
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
        StringAssert.Contains("Aisle 1", CustomerQuestion.Thanks(CustomerQuestion.Kind.Aisle, p, z));
    }

    [Test]
    public void ADepartment_IsNeverAskedForByNumber()
    {
        StoreLayout.Zone dairy = StoreLayout.ZoneOf(ItemType.Dairy);
        for (float roll = 0f; roll < 1f; roll += 0.05f)
        {
            CustomerQuestion.Kind kind = CustomerQuestion.Choose(roll, false, dairy);
            Assert.AreNotEqual(CustomerQuestion.Kind.Aisle, kind);
            Assert.AreNotEqual(CustomerQuestion.Kind.WhichAisle, kind);
        }
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
