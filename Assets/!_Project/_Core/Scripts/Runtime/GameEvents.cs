using System;
using UnityEngine;

// The things the player does that other systems want to know about, raised where they
// happen. Karen reads them as evidence, the Director as stress signals, the eval harness
// as its action log — none of which the gameplay scripts need to know exist.
public static class GameEvents
{
    public static event Action<Item, Vector3> PlayerDroppedItem;
    public static event Action<ShelfSlot, Item> PlayerTookFromShelf;
    public static event Action<ShelfUnit, int> ShelfRestocked;          // unit, facings filled
    public static event Action<ShelfSlot, Item> PlayerShelvedItem;
    public static event Action<Dirt> SpillCleaned;
    public static event Action<Dirt, float> MoppingAbandoned;           // how far it had got, 0..1
    public static event Action<Trashcan> BinBagged;
    public static event Action<TrashBag> BagDisposed;
    public static event Action<Vector3> CoffeeDrunk;
    public static event Action<CustomerNPC> CustomerServed;
    public static event Action<CustomerNPC> DirectionsGiven;
    public static event Action<Component, bool> DoorUsed;               // door, opened (false = closed)
    public static event Action<bool> PunchAttempted;                    // accepted?
    public static event Action<Item> PlayerPickedUp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PlayerDroppedItem = null;
        PlayerTookFromShelf = null;
        ShelfRestocked = null;
        PlayerShelvedItem = null;
        SpillCleaned = null;
        MoppingAbandoned = null;
        BinBagged = null;
        BagDisposed = null;
        CoffeeDrunk = null;
        CustomerServed = null;
        DirectionsGiven = null;
        DoorUsed = null;
        PunchAttempted = null;
        PlayerPickedUp = null;
    }

    public static void RaisePlayerDroppedItem(Item item, Vector3 at) => PlayerDroppedItem?.Invoke(item, at);
    public static void RaisePlayerTookFromShelf(ShelfSlot slot, Item item) => PlayerTookFromShelf?.Invoke(slot, item);
    public static void RaiseShelfRestocked(ShelfUnit unit, int filled) => ShelfRestocked?.Invoke(unit, filled);
    public static void RaisePlayerShelvedItem(ShelfSlot slot, Item item) => PlayerShelvedItem?.Invoke(slot, item);
    public static void RaiseSpillCleaned(Dirt dirt) => SpillCleaned?.Invoke(dirt);
    public static void RaiseMoppingAbandoned(Dirt dirt, float progress) => MoppingAbandoned?.Invoke(dirt, progress);
    public static void RaiseBinBagged(Trashcan can) => BinBagged?.Invoke(can);
    public static void RaiseBagDisposed(TrashBag bag) => BagDisposed?.Invoke(bag);
    public static void RaiseCoffeeDrunk(Vector3 at) => CoffeeDrunk?.Invoke(at);
    public static void RaiseCustomerServed(CustomerNPC customer) => CustomerServed?.Invoke(customer);
    public static void RaiseDirectionsGiven(CustomerNPC customer) => DirectionsGiven?.Invoke(customer);
    public static void RaiseDoorUsed(Component door, bool opened) => DoorUsed?.Invoke(door, opened);
    public static void RaisePunchAttempted(bool accepted) => PunchAttempted?.Invoke(accepted);
    public static void RaisePlayerPickedUp(Item item) => PlayerPickedUp?.Invoke(item);
}
