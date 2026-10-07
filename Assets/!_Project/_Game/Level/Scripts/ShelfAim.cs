using System.Collections.Generic;
using UnityEngine;

// Which shelf slot a ray is pointing at. Slots have no colliders (there are 16,500 of them),
// so the ray first finds the nearest solid thing in reach (a board, a bay's back, a wall),
// then is tested against the boxes of the slots near it that stand in front of that.
public static class ShelfAim
{
    static readonly List<ShelfSlot> near = new List<ShelfSlot>();
    static int solid = -1;

    // Everything but the things that are aimed at (items, shoppers) and the triggers.
    static int Solid
    {
        get
        {
            if (solid == -1)
                solid = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Interactable", "Ignore Raycast");
            return solid;
        }
    }

    public static ShelfSlot Find(Ray ray, float range, out float distance) =>
        Find(ShelfStock.Current, ray, range, out distance);

    public static ShelfSlot Find(ShelfStock stock, Ray ray, float range, out float distance)
    {
        distance = float.PositiveInfinity;
        if (stock == null || stock.Count == 0) return null;

        // A slot's box can start a little behind the surface the ray hits first (the board's
        // front edge, the shelf's back), so it gets a hair of slack.
        float limit = range;
        if (Physics.Raycast(ray, out RaycastHit wall, range, Solid, QueryTriggerInteraction.Ignore))
            limit = Mathf.Min(range, wall.distance + 0.05f);

        stock.Near(ray.origin + ray.direction * (range * 0.5f), range * 0.5f + 1f, near);
        ShelfSlot best = null;
        foreach (ShelfSlot slot in near)
        {
            if (!slot.RayHit(ray, out float d) || d > limit || d >= distance) continue;
            best = slot;
            distance = d;
        }
        return best;
    }
}
