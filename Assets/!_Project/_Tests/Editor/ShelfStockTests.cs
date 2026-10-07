using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// Stock is data (IDEAS.md, "Scaling", step 2): slots are ShelfSlot records in a ShelfStock,
// aimed at by ray against their boxes, and an item is a GameObject only off the shelf. These
// build their own stock, far from the shop, so the scene open in the editor isn't touched.
public class ShelfStockTests
{
    static readonly Vector3 Away = new Vector3(5000f, 0f, 5000f);

    readonly List<Object> made = new List<Object>();

    [TearDown]
    public void CleanUp()
    {
        foreach (Object o in made) if (o != null) Object.DestroyImmediate(o);
        made.Clear();
    }

    Transform Frame(Vector3 at, float yaw = 0f)
    {
        var go = new GameObject("test bay");
        made.Add(go);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        return go.transform;
    }

    static ShelfSlot Slot(ShelfStock stock, Transform frame, Vector3 local, string product = "canned_tuna", bool back = false)
    {
        var slot = new ShelfSlot(null, frame, local, back, new Vector2(0.25f, 0.25f), 0.2f);
        stock.Add(slot);
        ProductDef p = ProductCatalog.Get(product);
        slot.Stock(p.Category, p.Id, filled: true);
        return slot;
    }

    [Test]
    public void ASlot_StandsOnItsBay_AndMovesWithIt()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away, 90f);
        ShelfSlot slot = Slot(stock, bay, new Vector3(1f, 0.8f, 0f));
        stock.Finish();

        Assert.That(Vector3.Distance(slot.Position, Away + new Vector3(0f, 0.8f, -1f)), Is.LessThan(1e-4f));
        Assert.AreSame(slot, stock.Nearest(slot.Position, 0.5f));

        bay.position += new Vector3(20f, 0f, 0f);
        Assert.IsNull(stock.Nearest(Away + new Vector3(0f, 0.8f, -1f), 0.5f), "still filed where the bay was");
        Assert.AreSame(slot, stock.Nearest(slot.Position, 0.5f));
    }

    [Test]
    public void Near_FindsWhatsInReach_AndNothingElse()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away);
        var inReach = new List<ShelfSlot>();
        for (int i = 0; i < 10; i++)
        {
            ShelfSlot s = Slot(stock, bay, new Vector3(i * 0.5f, 1f, 0f));
            if (i * 0.5f <= 1.6f) inReach.Add(s);
        }
        stock.Finish();

        var found = new List<ShelfSlot>();
        stock.Near(Away + Vector3.up, 1.6f, found);
        CollectionAssert.AreEquivalent(inReach, found);
    }

    [Test]
    public void A_Ray_FindsTheBoxOfWhatStandsOnASlot()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away, 30f);
        ShelfSlot slot = Slot(stock, bay, new Vector3(0f, 1f, 0f));
        stock.Finish();

        Vector3 middle = slot.Position + Vector3.up * 0.1f;
        var straightAt = new Ray(middle - slot.Outward * -2f, -slot.Outward);   // from the aisle it faces
        Assert.IsTrue(slot.RayHit(straightAt, out float d));
        Assert.AreEqual(2f - 0.12f, d, 0.02f, "the box's near face is half a cell from the middle");

        var over = new Ray(middle + Vector3.up * 0.5f + slot.Outward * 2f, -slot.Outward);
        Assert.IsFalse(slot.RayHit(over, out _), "a ray over the top misses");

        var away = new Ray(middle + slot.Outward * 2f, slot.Outward);
        Assert.IsFalse(slot.RayHit(away, out _), "a ray pointing away misses");
    }

    [Test]
    public void Aiming_GoesNoFurtherThanTheFirstSolidThing()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away);
        ShelfSlot slot = Slot(stock, bay, new Vector3(0f, 1f, 0f));
        stock.Finish();

        var eye = new Ray(slot.Position + Vector3.up * 0.1f + slot.Outward * 2f, -slot.Outward);
        Assert.AreSame(slot, ShelfAim.Find(stock, eye, 2.5f, out _));

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        made.Add(wall);
        wall.transform.position = eye.origin + eye.direction;
        wall.transform.localScale = new Vector3(2f, 2f, 0.1f);
        Physics.SyncTransforms();
        Assert.IsNull(ShelfAim.Find(stock, eye, 2.5f, out _), "a slot behind a wall isn't aimed at");
    }

    [Test]
    public void AnItem_LeavesTheShelf_AsAGameObject_AndGoesBackAsData()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away);
        ShelfSlot slot = Slot(stock, bay, new Vector3(0f, 1f, 0f), "drink_pipisi");
        stock.Finish();
        var changed = new List<ShelfSlot>();
        stock.Changed += changed.Add;

        Item item = slot.TakeItem();
        Assume.That(item, Is.Not.Null, "drink_pipisi hasn't been imported");
        made.Add(item.gameObject);
        Assert.IsFalse(slot.isFilled);
        Assert.AreEqual("drink_pipisi", item.productId);
        Assert.IsFalse(item.isOnShelf);
        slot.Pose("drink_pipisi", out Vector3 stood, out _);
        Assert.That(Vector3.Distance(item.transform.position, stood), Is.LessThan(1e-4f), "it starts where it stood");
        Assert.IsNull(slot.TakeItem(), "an empty slot has nothing to give");

        Assert.IsTrue(slot.Put(item));
        Assert.IsTrue(item == null, "on the shelf it's data again");
        Assert.IsTrue(slot.isFilled);
        Assert.AreEqual("drink_pipisi", slot.StockedId);
        CollectionAssert.AreEqual(new[] { slot, slot }, changed);
    }

    [Test]
    public void ABay_CountsItsEmptySlots()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away);
        var unit = bay.gameObject.AddComponent<ShelfUnit>();
        var slots = new List<ShelfSlot>();
        for (int i = 0; i < 4; i++)
        {
            var s = new ShelfSlot(unit, bay, new Vector3(i * 0.3f, 1f, 0f), false, new Vector2(0.25f, 0.25f), 0.2f);
            stock.Add(s);
            s.Stock(ItemType.Canned, "canned_tuna", filled: true);
            slots.Add(s);
        }
        stock.Finish();
        unit.SetSlots(slots);
        Assert.IsTrue(unit.IsFull);

        slots[0].Eject();
        slots[1].Eject();
        foreach (Item i in Object.FindObjectsByType<Item>())
            if (Vector3.Distance(i.transform.position, Away) < 10f) made.Add(i.gameObject);
        Assert.AreEqual(2, unit.EmptyCount);

        Assert.AreEqual(2, unit.FillAll());
        Assert.IsTrue(unit.IsFull);
    }

    [Test]
    public void BackFacingSlots_FaceTheOtherAisle()
    {
        var stock = new ShelfStock();
        Transform bay = Frame(Away);
        ShelfSlot front = Slot(stock, bay, new Vector3(0f, 1f, -0.25f));
        ShelfSlot back = Slot(stock, bay, new Vector3(0f, 1f, 0.25f), back: true);
        Assert.That(Vector3.Dot(front.Outward, Vector3.back), Is.GreaterThan(0.99f));
        Assert.That(Vector3.Dot(back.Outward, Vector3.forward), Is.GreaterThan(0.99f));
    }
}
