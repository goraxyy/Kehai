using UnityEngine;

// Where a point stands in the store's own terms (0.5.2, C11.17). The store's tables read world
// positions: which room a point is in (StoreMap.AreaAt), what a shelf sells (StoreLayout.ZoneAt),
// which breaker runs a lamp (LightControl.CircuitOf). A sandbox built elsewhere, the mini store,
// sets a map from its own floor to the matching part of the store, so those tables name its
// rooms, stock its shelves and wire its lamps as the store's. With no map, a point is itself.
public static class StoreFrame
{
    public static System.Func<Vector3, Vector3> Map { get; private set; }
    public static bool Active => Map != null;

    public static void Use(System.Func<Vector3, Vector3> map) => Map = map;

    public static Vector3 ToStore(Vector3 p) => Map != null ? Map(p) : p;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Map = null;
}
