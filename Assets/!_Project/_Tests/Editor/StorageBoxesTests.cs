using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// The back rooms: which products are kept cold, how the storage racks are filled, and what a
// product's box restocks.
public class StorageBoxesTests
{
    [Test]
    public void DairyAndFrozen_AreKeptCold_WithTheFewThatWouldSpoil()
    {
        foreach (ProductDef p in ProductCatalog.All)
            if (p.Category == ItemType.Dairy || p.Category == ItemType.Frozen)
                Assert.AreEqual(StorageKind.Cold, p.Storage, p.Id);
        foreach (string id in new[] { "produce_saladmix", "noodle_udon", "drink_chakra_tea", "drink_genki" })
            Assert.AreEqual(StorageKind.Cold, ProductCatalog.Get(id).Storage, id);
        Assert.AreEqual(StorageKind.Ambient, ProductCatalog.Get("canned_tuna").Storage);
        Assert.AreEqual(StorageKind.Ambient, ProductCatalog.Get("drink_pipisi").Storage);

        int kept = StorageBoxes.ProductsKept(StorageKind.Ambient).Count + StorageBoxes.ProductsKept(StorageKind.Cold).Count;
        Assert.AreEqual(ProductCatalog.All.Count(p => ProductCatalog.IsStockSection(p.Category)), kept, "every product is kept somewhere");
    }

    // Racks as the storage room has them: two boxes to a board, two boards in reach, one not.
    static List<StorageBoxes.Rack> Racks(int count)
    {
        var racks = new List<StorageBoxes.Rack>();
        for (int r = 0; r < count; r++)
        {
            var rack = new StorageBoxes.Rack();
            for (int board = 0; board < 3; board++)
                for (int i = 0; i < 2; i++)
                    rack.Spots.Add(new StorageBoxes.Spot(new Vector3(r * 5f + i * 2f, 1.5f + board * 1.4f, 0f), Quaternion.identity,
                                                         StorageBoxes.RackBox, board < 2, i, false));
            racks.Add(rack);
        }
        return racks;
    }

    // One section to a rack; every product of it in reach on one of its racks; the board out of
    // reach repeats the box below it.
    [Test]
    public void EachRack_HoldsOneSection_WithEveryProductInReach()
    {
        List<ProductDef> products = StorageBoxes.ProductsKept(StorageKind.Ambient);
        List<StorageBoxes.Rack> racks = Racks(28);
        ItemType?[] sections = StorageBoxes.SectionsFor(racks, products);

        Assert.IsTrue(sections.All(s => s != null), "every rack has a section");
        var inReach = new HashSet<string>();
        foreach (IGrouping<ItemType, int> group in Enumerable.Range(0, racks.Count).GroupBy(r => sections[r].Value))
        {
            List<ProductDef> own = products.FindAll(p => p.Category == group.Key);
            int index = 0, of = group.Count();
            foreach (int r in group)
            {
                List<StorageBoxes.Spot> spots = racks[r].Spots;
                string[] ids = StorageBoxes.Assign(spots, own, (index++, of));
                Assert.IsTrue(ids.All(id => own.Any(p => p.Id == id)), $"rack {r} holds only {group.Key}");
                for (int i = 0; i < spots.Count; i++)
                {
                    if (spots[i].Reachable) { inReach.Add(ids[i]); continue; }
                    int below = spots.FindLastIndex(s => s.Reachable && s.Column == spots[i].Column);
                    Assert.AreEqual(ids[below], ids[i], "a high box repeats the one below it");
                }
            }
        }
        foreach (ProductDef p in products) Assert.IsTrue(inReach.Contains(p.Id), p.Id + " has no box in reach");
    }

    // With the box in hand, a slot of its product fills; another product's slot doesn't.
    [Test]
    public void ABox_RestocksItsOwnProduct_AndNothingElse()
    {
        var bay = new GameObject("test bay");
        var boxObject = new GameObject("test box");
        try
        {
            var stock = new ShelfStock();
            var milk = new ShelfSlot(null, bay.transform, Vector3.zero, false, new Vector2(0.25f, 0.25f), 0.2f);
            var tuna = new ShelfSlot(null, bay.transform, Vector3.right, false, new Vector2(0.25f, 0.25f), 0.2f);
            stock.Add(milk);
            stock.Add(tuna);
            milk.Stock(ItemType.Dairy, "dairy_milk_whole", filled: false);
            tuna.Stock(ItemType.Canned, "canned_tuna", filled: false);
            var box = boxObject.AddComponent<ProductBox>();
            box.productId = "dairy_milk_whole";

            Assert.AreEqual(0, box.Restock(tuna));
            Assert.IsFalse(tuna.isFilled);
            StringAssert.StartsWith("Wrong box", box.PromptFor(tuna));

            Assert.AreEqual(1, box.Restock(milk));
            Assert.IsTrue(milk.isFilled);
            Assert.AreEqual("dairy_milk_whole", milk.StockedId);
            Assert.AreEqual("Restock Moo-Moo whole milk", box.PromptFor(milk));
        }
        finally
        {
            Object.DestroyImmediate(bay);
            Object.DestroyImmediate(boxObject);
        }
    }

    [Test]
    public void ABox_IsNamedForItsProduct()
    {
        Assert.AreEqual("Bag of Fuji apples", ProductBox.ShortName(ProductCatalog.Get("produce_apples")));
        Assert.AreEqual("Moo-Moo whole milk", ProductBox.ShortName(ProductCatalog.Get("dairy_milk_whole")));
    }
}
