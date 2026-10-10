using System.Collections.Generic;
using UnityEngine;

// Every slot in the shop, and the indexes over them. Stock is data (IDEAS.md, "Scaling",
// step 2): a slot is a ShelfSlot record, ShelfDrawer draws what stands on them, and an item is
// a GameObject only once it leaves a shelf.
//
//   - by place: a 2 m grid of the floor, for "what's within reach of here";
//   - by bay: so a bay that moves (Karen rolls them between shifts) takes its slots with it;
//   - by change: the drawer and the replay recorder hear about each slot that changes.
//
// StoreLayout fills Current at load. Tests make their own.
public sealed class ShelfStock
{
    public const float GridSize = 2f;

    public static ShelfStock Current { get; private set; } = new ShelfStock();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCurrent()
    {
        Current = new ShelfStock();
        Placeholder = null;
    }

    // The box a slot is restocked with when its product hasn't been imported: the stock
    // crate's item (StockCrate hands it over).
    public static GameObject Placeholder;

    readonly List<ShelfSlot> slots = new List<ShelfSlot>();
    readonly Dictionary<Vector2Int, List<ShelfSlot>> grid = new Dictionary<Vector2Int, List<ShelfSlot>>();
    readonly Dictionary<Transform, List<ShelfSlot>> byFrame = new Dictionary<Transform, List<ShelfSlot>>();
    readonly List<Transform> frames = new List<Transform>();

    public IReadOnlyList<ShelfSlot> Slots => slots;
    public int Count => slots.Count;

    // Bumped whenever the whole stock is rebuilt, or a bay's slots move: whatever draws or
    // records it starts again. Slots added (Finish) and removed (Remove) are told instead.
    public int Version { get; private set; }

    public event System.Action<ShelfSlot> Changed;
    public event System.Action<IReadOnlyList<ShelfSlot>> Moved;
    public event System.Action<IReadOnlyList<ShelfSlot>> Added;
    public event System.Action<IReadOnlyList<ShelfSlot>> Removed;

    readonly List<ShelfSlot> pending = new List<ShelfSlot>();

    public void Clear()
    {
        slots.Clear();
        grid.Clear();
        byFrame.Clear();
        frames.Clear();
        pending.Clear();
        Version++;
    }

    public ShelfSlot Add(ShelfSlot slot)
    {
        slot.Attach(this, slots.Count);
        slots.Add(slot);
        if (!byFrame.TryGetValue(slot.frame, out List<ShelfSlot> own))
        {
            byFrame[slot.frame] = own = new List<ShelfSlot>();
            frames.Add(slot.frame);
            slot.frame.hasChanged = false;
        }
        own.Add(slot);
        Index(slot);
        pending.Add(slot);
        return slot;
    }

    // Done adding: whatever draws the stock hears about the new slots.
    public void Finish()
    {
        foreach (Transform frame in frames) if (frame != null) frame.hasChanged = false;
        if (pending.Count == 0) return;
        var added = new List<ShelfSlot>(pending);
        pending.Clear();
        Added?.Invoke(added);
    }

    // Takes bays' slots out (the endless maze, unloading a chunk's forty or so bays at once).
    // Each gap is filled by the last slot, so only the slots taken out and those moved into
    // their places are touched, not the hundred thousand others; a moved slot's Index changes.
    public void Remove(IEnumerable<Transform> bays)
    {
        var gone = new List<ShelfSlot>();
        foreach (Transform frame in bays)
        {
            if (frame == null || !byFrame.TryGetValue(frame, out List<ShelfSlot> own)) continue;
            foreach (ShelfSlot slot in own) Unindex(slot);
            byFrame.Remove(frame);
            frames.Remove(frame);
            gone.AddRange(own);
        }
        if (gone.Count == 0) return;
        Planogram.Forget(gone);

        gone.Sort((a, b) => b.Index.CompareTo(a.Index));
        foreach (ShelfSlot slot in gone)
        {
            int at = slot.Index, last = slots.Count - 1;
            if (at < 0 || at > last || slots[at] != slot) continue;
            if (at != last)
            {
                slots[at] = slots[last];
                slots[at].Attach(this, at);
            }
            slots.RemoveAt(last);
            slot.Attach(null, -1);
        }
        if (pending.Count > 0)
        {
            var set = new HashSet<ShelfSlot>(gone);
            pending.RemoveAll(set.Contains);
        }
        Removed?.Invoke(gone);
    }

    internal void NotifyChanged(ShelfSlot slot) => Changed?.Invoke(slot);

    // Bays that have moved since last asked: their slots are re-filed under where they are
    // now. Cheap enough to call before every lookup and every frame (a bool per bay).
    public void SyncMoved()
    {
        for (int f = 0; f < frames.Count; f++)
        {
            Transform frame = frames[f];
            if (frame == null || !frame.hasChanged) continue;
            frame.hasChanged = false;
            List<ShelfSlot> own = byFrame[frame];
            foreach (ShelfSlot slot in own) Unindex(slot);
            foreach (ShelfSlot slot in own) Index(slot);
            Version++;
            Moved?.Invoke(own);
        }
    }

    public IReadOnlyList<ShelfSlot> SlotsOn(Transform frame) =>
        frame != null && byFrame.TryGetValue(frame, out List<ShelfSlot> own) ? own : (IReadOnlyList<ShelfSlot>)System.Array.Empty<ShelfSlot>();

    // Every slot whose point is within `radius` of `p` (on the floor plan), in no order.
    public void Near(Vector3 p, float radius, List<ShelfSlot> into)
    {
        into.Clear();
        SyncMoved();
        float sqr = radius * radius;
        Vector2Int min = Key(p - new Vector3(radius, 0f, radius)), max = Key(p + new Vector3(radius, 0f, radius));
        for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                if (!grid.TryGetValue(new Vector2Int(x, z), out List<ShelfSlot> cell)) continue;
                foreach (ShelfSlot slot in cell)
                    if ((slot.Position - p).sqrMagnitude <= sqr) into.Add(slot);
            }
    }

    readonly List<ShelfSlot> scratch = new List<ShelfSlot>();

    // The closest slot within `radius` that `wanted` accepts, or null.
    public ShelfSlot Nearest(Vector3 p, float radius, System.Predicate<ShelfSlot> wanted = null)
    {
        Near(p, radius, scratch);
        ShelfSlot best = null;
        float bestSqr = float.MaxValue;
        foreach (ShelfSlot slot in scratch)
        {
            if (wanted != null && !wanted(slot)) continue;
            float sqr = (slot.Position - p).sqrMagnitude;
            if (sqr < bestSqr) { best = slot; bestSqr = sqr; }
        }
        return best;
    }

    static Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / GridSize), Mathf.FloorToInt(p.z / GridSize));

    void Index(ShelfSlot slot)
    {
        Vector2Int key = Key(slot.Position);
        if (!grid.TryGetValue(key, out List<ShelfSlot> cell)) grid[key] = cell = new List<ShelfSlot>();
        cell.Add(slot);
        slot.gridKey = key;
    }

    void Unindex(ShelfSlot slot)
    {
        if (grid.TryGetValue(slot.gridKey, out List<ShelfSlot> cell)) cell.Remove(slot);
    }
}
