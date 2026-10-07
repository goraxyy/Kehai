using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Draws the stock: what stands on every shelf slot, with no GameObject for any of it
// (IDEAS.md, "Scaling", step 2). The floor is cut into 10 m cells. Each cell keeps, for each
// product and room, where its items stand. For each camera, the cells in its view (and near
// enough that anyone would make out a tin) put their lists together by product, and each
// product is drawn with one instanced call per part of its mesh, lit only by its room's
// lights (RoomLighting). Drawing each cell's lists separately took three times the calls.
//
// It draws in the editor too, from the planogram, so the shelves look stocked there with
// nothing baked into the scene.
[ExecuteAlways]
public class ShelfDrawer : MonoBehaviour
{
    public const string ObjectName = "Shelf Stock";
    public const float CellSize = 10f;
    public const float DrawDistance = 60f;
    const int MaxPerCall = 1023;

    sealed class Batch
    {
        public Mesh Mesh;
        public Material[] Materials;
        public ShadowCastingMode Shadows;
        public bool ReceiveShadows;
        public int Layer;
        public uint Mask;
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
    }

    sealed class Cell
    {
        public Bounds Bounds;
        public bool Dirty = true;
        public readonly List<ShelfSlot> Slots = new List<ShelfSlot>();
        public readonly Dictionary<(string id, uint mask), Batch> Batches = new Dictionary<(string, uint), Batch>();
    }

    readonly Dictionary<Vector2Int, Cell> cells = new Dictionary<Vector2Int, Cell>();

    // One camera's view of the stock: every visible cell's items of a product, in one list.
    sealed class Gathered
    {
        public Batch Look;
        public Bounds Bounds;
        public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
    }

    readonly Dictionary<(string id, uint mask), Gathered> gathered = new Dictionary<(string, uint), Gathered>();
    readonly Dictionary<ShelfSlot, Cell> cellOf = new Dictionary<ShelfSlot, Cell>();
    readonly Plane[] planes = new Plane[6];
    ShelfStock watching;
    int builtVersion = -1;

    // How many instances went out last frame, for measuring.
    public int LastDrawn { get; private set; }

    public static ShelfDrawer Ensure()
    {
        ShelfDrawer existing = FindAnyObjectByType<ShelfDrawer>();
        if (existing != null) return existing;
        return new GameObject(ObjectName).AddComponent<ShelfDrawer>();
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += Draw;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.hierarchyChanged += PreviewIsStale;
        previewStale = true;
#endif
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Draw;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.hierarchyChanged -= PreviewIsStale;
#endif
        Watch(null);
        cells.Clear();
        cellOf.Clear();
    }

#if UNITY_EDITOR
    // Out of play, the stock is the planogram's: built here, and again when bays are added or
    // removed (at most twice a second while the hierarchy is being edited).
    static bool previewStale;
    static double previewBuiltAt;

    static void PreviewIsStale() => previewStale = true;

    static void KeepPreview()
    {
        if (Application.isPlaying || !previewStale) return;
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        if (now - previewBuiltAt < 0.5) return;
        previewStale = false;
        previewBuiltAt = now;
        StoreLayout.BuildPreview();
    }
#endif

    void Draw(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
#if UNITY_EDITOR
        KeepPreview();
#endif
        ShelfStock stock = ShelfStock.Current;
        stock.SyncMoved();
        if (stock != watching || stock.Version != builtVersion) Rebuild(stock);

        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 eye = cam.transform.position;
        float far = DrawDistance * DrawDistance;
        foreach (Gathered g in gathered.Values) g.Matrices.Clear();
        foreach (Cell c in cells.Values)
        {
            if (c.Bounds.SqrDistance(eye) > far) continue;
            if (!GeometryUtility.TestPlanesAABB(planes, c.Bounds)) continue;
            if (c.Dirty) Refill(c);
            foreach (KeyValuePair<(string, uint), Batch> kv in c.Batches)
            {
                if (kv.Value.Matrices.Count == 0) continue;
                if (!gathered.TryGetValue(kv.Key, out Gathered g))
                    gathered[kv.Key] = g = new Gathered { Look = kv.Value };
                if (g.Matrices.Count == 0) g.Bounds = c.Bounds;
                else g.Bounds.Encapsulate(c.Bounds);
                g.Matrices.AddRange(kv.Value.Matrices);
            }
        }

        int drawn = 0;
        foreach (Gathered g in gathered.Values) drawn += Submit(g.Look, g.Matrices, g.Bounds, cam);
        LastDrawn = drawn;
    }

    int Submit(Batch b, List<Matrix4x4> matrices, Bounds bounds, Camera cam)
    {
        int count = matrices.Count;
        if (count == 0) return 0;
        for (int sub = 0; sub < b.Mesh.subMeshCount; sub++)
        {
            Material material = b.Materials[Mathf.Min(sub, b.Materials.Length - 1)];
            if (material == null) continue;
            var rp = new RenderParams(material)
            {
                camera = cam,
                worldBounds = bounds,
                renderingLayerMask = b.Mask,
                shadowCastingMode = b.Shadows,
                receiveShadows = b.ReceiveShadows,
                layer = b.Layer,
                lightProbeUsage = LightProbeUsage.BlendProbes,
            };
            for (int start = 0; start < count; start += MaxPerCall)
                Graphics.RenderMeshInstanced(rp, b.Mesh, sub, matrices, Mathf.Min(MaxPerCall, count - start), start);
        }
        return count;
    }

    void Watch(ShelfStock stock)
    {
        if (watching != null) watching.Changed -= OnChanged;
        watching = stock;
        if (watching != null) watching.Changed += OnChanged;
        gathered.Clear();
    }

    void OnChanged(ShelfSlot slot)
    {
        if (cellOf.TryGetValue(slot, out Cell c)) c.Dirty = true;
    }

    // Which cell each slot is in, and each cell's bounds. What's drawn in a cell is worked out
    // when it's first seen, and again only after one of its slots changes.
    void Rebuild(ShelfStock stock)
    {
        Watch(stock);
        builtVersion = stock.Version;
        cells.Clear();
        cellOf.Clear();
        foreach (ShelfSlot slot in stock.Slots)
        {
            Vector3 p = slot.Position;
            var key = new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
            var box = new Bounds(p + Vector3.up * (slot.height * 0.5f), new Vector3(0.6f, slot.height + 0.1f, 0.6f));
            if (!cells.TryGetValue(key, out Cell c))
            {
                cells[key] = c = new Cell { Bounds = box };
            }
            else c.Bounds.Encapsulate(box);
            c.Slots.Add(slot);
            cellOf[slot] = c;
        }
    }

    void Refill(Cell c)
    {
        foreach (Batch b in c.Batches.Values) b.Matrices.Clear();
        bool rooms = RoomLighting.LayersDefined;
        foreach (ShelfSlot slot in c.Slots)
        {
            if (!slot.isFilled) continue;
            // Lit by its own room's lights only, as the shelves under it are.
            uint mask = rooms ? (uint)RoomLighting.BitAt(slot.Position) : RoomLighting.Moving;
            Batch b = BatchFor(c, slot.StockedId, mask);
            if (b == null) continue;
            slot.Pose(slot.StockedId, out Vector3 position, out Quaternion rotation);
            b.Matrices.Add(Matrix4x4.TRS(position, rotation, Vector3.one));
        }
        c.Dirty = false;
    }

    static Batch BatchFor(Cell c, string id, uint mask)
    {
        if (c.Batches.TryGetValue((id, mask), out Batch b)) return b;

        ProductLook.Look? look = ProductLook.For(id);
        GameObject prefab = ProductLook.Prefab(id);
        if (look == null || prefab == null) return null;
        MeshRenderer shown = prefab.GetComponentInChildren<MeshRenderer>();
        foreach (Material m in look.Value.Materials)
            if (m != null && !m.enableInstancing) m.enableInstancing = true;

        b = new Batch
        {
            Mesh = look.Value.Mesh,
            Materials = look.Value.Materials,
            Shadows = shown != null ? shown.shadowCastingMode : ShadowCastingMode.Off,
            ReceiveShadows = shown == null || shown.receiveShadows,
            Layer = prefab.layer,
            Mask = mask,
        };
        c.Batches[(id, mask)] = b;
        return b;
    }
}
