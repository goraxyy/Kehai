using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Shows the stock: what stands on every shelf slot (IDEAS.md, "Scaling", step 2). The slots
// are the truth; this is only how they look. Each stocked slot gets a bare render object: a
// mesh and its materials, with no collider and no script, hidden and never saved. Copies come
// from one template per product and room, so each is lit by its room's lights only
// (RoomLighting), and the GPU Resident Drawer draws them. It culls them one by one, including
// those hidden behind shelves. Over four test views the stock costs 2.4 ms that way (4.8 ms
// with it, 2.4 without), where one instanced call per product cost 5 to 7 ms. The copies are
// made one at a time with Instantiate: copies made by InstantiateAsync are never taken up by
// the GPU Resident Drawer, and cost 17 ms drawn the ordinary way.
//
// A slot that empties or fills swaps its object through a pool, and a full rebuild (the shop
// restocked, a bay moved) reuses the objects it has, so it's quick in the editor as well,
// where it shows the planogram's stock with nothing baked into the scene.
[ExecuteAlways]
public class ShelfDrawer : MonoBehaviour
{
    public const string ObjectName = "Shelf Stock";
    const HideFlags Hidden = HideFlags.HideAndDontSave;

    Transform root;
    readonly Dictionary<(string id, uint mask), GameObject> templates = new Dictionary<(string, uint), GameObject>();
    readonly Dictionary<(string id, uint mask), Stack<GameObject>> pool = new Dictionary<(string, uint), Stack<GameObject>>();
    readonly Dictionary<ShelfSlot, (GameObject go, (string id, uint mask) key)> shown =
        new Dictionary<ShelfSlot, (GameObject, (string, uint))>();
    readonly HashSet<ShelfSlot> dirty = new HashSet<ShelfSlot>();
    ShelfStock watching;
    int builtVersion = -1;

    // How many items are shown, for measuring.
    public int ShownCount => shown.Count;

    public static ShelfDrawer Ensure()
    {
        ShelfDrawer existing = FindAnyObjectByType<ShelfDrawer>();
        if (existing != null) return existing;
        return new GameObject(ObjectName).AddComponent<ShelfDrawer>();
    }

    void OnEnable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.hierarchyChanged += PreviewIsStale;
        UnityEditor.EditorApplication.update += EditorTick;
        previewStale = true;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.hierarchyChanged -= PreviewIsStale;
        UnityEditor.EditorApplication.update -= EditorTick;
#endif
        Watch(null);
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }
        root = null;
        templates.Clear();
        pool.Clear();
        shown.Clear();
        dirty.Clear();
        builtVersion = -1;
    }

    void LateUpdate()
    {
        if (Application.isPlaying) Sync();
    }

#if UNITY_EDITOR
    // Out of play, the stock is the planogram's: built here, and again when bays are added or
    // removed (at most twice a second while the hierarchy is being edited).
    static bool previewStale;
    static double previewBuiltAt;

    static void PreviewIsStale() => previewStale = true;

    void EditorTick()
    {
        if (Application.isPlaying || this == null) return;
        if (previewStale)
        {
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - previewBuiltAt >= 0.5)
            {
                previewStale = false;
                previewBuiltAt = now;
                StoreLayout.BuildPreview();
            }
        }
        Sync();
    }
#endif

    // Brings what's shown in line with the stock: everything again when it has been rebuilt
    // or a bay has moved, otherwise just the slots that changed.
    void Sync()
    {
        ShelfStock stock = ShelfStock.Current;
        stock.SyncMoved();
        if (stock != watching || stock.Version != builtVersion) Rebuild(stock);
        if (dirty.Count == 0) return;
        foreach (ShelfSlot slot in dirty) Refresh(slot);
        dirty.Clear();
    }

    void Watch(ShelfStock stock)
    {
        if (watching != null)
        {
            watching.Changed -= OnChanged;
            watching.Added -= OnAdded;
            watching.Removed -= OnRemoved;
        }
        watching = stock;
        if (watching != null)
        {
            watching.Changed += OnChanged;
            watching.Added += OnAdded;
            watching.Removed += OnRemoved;
        }
    }

    void OnChanged(ShelfSlot slot) => dirty.Add(slot);

    // Slots come and go a bay at a time (the endless maze's chunks), and only theirs change.
    void OnAdded(IReadOnlyList<ShelfSlot> added)
    {
        if (watching == null || watching.Version != builtVersion) return;   // all of it is redone anyway
        foreach (ShelfSlot slot in added) dirty.Add(slot);
    }

    void OnRemoved(IReadOnlyList<ShelfSlot> removed)
    {
        foreach (ShelfSlot slot in removed)
        {
            dirty.Remove(slot);
            if (!shown.TryGetValue(slot, out (GameObject go, (string id, uint mask) key) current)) continue;
            Give(current.key, current.go);
            shown.Remove(slot);
        }
    }

    void Rebuild(ShelfStock stock)
    {
        Watch(stock);
        builtVersion = stock.Version;
        dirty.Clear();
        foreach (var kv in shown) Give(kv.Value.key, kv.Value.go);
        shown.Clear();

        var wanted = new Dictionary<(string, uint), List<ShelfSlot>>();
        foreach (ShelfSlot slot in stock.Slots)
        {
            if (!slot.isFilled) continue;
            var key = KeyOf(slot);
            if (!wanted.TryGetValue(key, out List<ShelfSlot> list)) wanted[key] = list = new List<ShelfSlot>();
            list.Add(slot);
        }
        foreach (var kv in wanted) Place(kv.Key, kv.Value);
    }

    // Shows `slots`, all of one product in one room: pooled objects first, then copies of the
    // template (Instantiate, not InstantiateAsync: see the top).
    void Place((string id, uint mask) key, List<ShelfSlot> slots)
    {
        Stack<GameObject> free = PoolOf(key);
        int i = 0;
        for (; i < slots.Count && free.Count > 0; i++) Show(slots[i], free.Pop(), key);
        if (i == slots.Count) return;

        GameObject template = TemplateFor(key);
        if (template == null) return;
        for (; i < slots.Count; i++)
        {
            slots[i].Pose(key.id, out Vector3 position, out Quaternion rotation);
            GameObject copy = Instantiate(template, position, rotation, Root);
            copy.hideFlags = Hidden;
            copy.SetActive(true);
            shown[slots[i]] = (copy, key);
        }
    }

    void Refresh(ShelfSlot slot)
    {
        bool had = shown.TryGetValue(slot, out (GameObject go, (string id, uint mask) key) current);
        if (!slot.isFilled)
        {
            if (!had) return;
            Give(current.key, current.go);
            shown.Remove(slot);
            return;
        }

        var key = KeyOf(slot);
        if (had)
        {
            if (current.key.Equals(key)) return;
            Give(current.key, current.go);
            shown.Remove(slot);
        }
        Stack<GameObject> free = PoolOf(key);
        if (free.Count > 0)
        {
            Show(slot, free.Pop(), key);
            return;
        }
        GameObject template = TemplateFor(key);
        if (template == null) return;
        GameObject go = Instantiate(template, Root);
        go.hideFlags = Hidden;
        Show(slot, go, key);
    }

    void Show(ShelfSlot slot, GameObject go, (string id, uint mask) key)
    {
        slot.Pose(key.id, out Vector3 position, out Quaternion rotation);
        go.transform.SetPositionAndRotation(position, rotation);
        go.SetActive(true);
        shown[slot] = (go, key);
    }

    void Give((string id, uint mask) key, GameObject go)
    {
        if (go == null) return;
        go.SetActive(false);
        PoolOf(key).Push(go);
    }

    Stack<GameObject> PoolOf((string id, uint mask) key)
    {
        if (!pool.TryGetValue(key, out Stack<GameObject> free)) pool[key] = free = new Stack<GameObject>();
        return free;
    }

    // Lit by its own room's lights only, as the shelves under it are (the slot's lightMask).
    static (string id, uint mask) KeyOf(ShelfSlot slot) => (slot.StockedId, slot.lightMask);

    Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("Stock (drawn)") { hideFlags = Hidden }.transform;
                root.SetParent(transform, false);
            }
            return root;
        }
    }

    // A product as a bare render object: its mesh and materials, switched off, to copy.
    GameObject TemplateFor((string id, uint mask) key)
    {
        if (templates.TryGetValue(key, out GameObject template)) return template;

        ProductLook.Look? look = ProductLook.For(key.id);
        GameObject prefab = ProductLook.Prefab(key.id);
        if (look == null || prefab == null)
        {
            templates[key] = null;
            return null;
        }
        MeshRenderer like = prefab.GetComponentInChildren<MeshRenderer>();
        template = new GameObject(key.id) { hideFlags = Hidden, layer = prefab.layer };
        template.SetActive(false);
        template.transform.SetParent(Root, false);
        template.AddComponent<MeshFilter>().sharedMesh = look.Value.Mesh;
        MeshRenderer r = template.AddComponent<MeshRenderer>();
        r.sharedMaterials = look.Value.Materials;
        r.shadowCastingMode = like != null ? like.shadowCastingMode : ShadowCastingMode.Off;
        r.receiveShadows = like == null || like.receiveShadows;
        r.reflectionProbeUsage = like != null ? like.reflectionProbeUsage : ReflectionProbeUsage.Off;
        r.motionVectorGenerationMode = like != null ? like.motionVectorGenerationMode : MotionVectorGenerationMode.ForceNoMotion;
        r.renderingLayerMask = key.mask;
        templates[key] = template;
        return template;
    }
}
