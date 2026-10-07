using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Streams the endless maze around the player (IDEAS.md, "Scaling", step 3). The chunks within
// `radius` of the player's are built: their shelf runs and pillars, floor, ceiling, ceiling
// lights and lamps, and their stock, as data. Chunks further than `radius + 1` are taken down,
// keeping what changed on their shelves for when they come back, so a chunk is rebuilt just
// as it was left. Pieces and the floor, ceiling and lights are pooled, and moved from chunk to
// chunk. A NavMesh is built over the chunks as they come, off the main thread.
//
// It runs in a scene of its own (Kehai/Endless/Open an Endless Maze); the hand-built store is
// still the game's. How the two meet is a design decision for later (IDEAS.md).
public class EndlessMaze : MonoBehaviour
{
    [Header("World")]
    public int seed = 1;
    [Tooltip("Chunks this far from the player's, in each direction, are built.")]
    public int radius = 2;
    public Transform player;

    [Header("Pieces")]
    [Tooltip("A two-sided 4 m bay: the shelf run on a wall.")]
    public GameObject runPrefab;
    [Tooltip("A 1 m pillar: where walls meet.")]
    public GameObject pillarPrefab;
    public Material floorMaterial;
    public Material ceilingMaterial;
    public float ceilingHeight = 8f;

    [Header("Ceiling lights")]
    public float lightHeight = 7.6f;
    public Color lightColour = new Color(1f, 0.95f, 0.88f);
    public float lightIntensity = 40f;
    public float lightRange = 17f;
    public float lightAngle = 80f;

    sealed class Built
    {
        public GameObject Root;
        public GameObject Shell;
        public readonly List<(GameObject go, GameObject prefab)> Pieces = new List<(GameObject, GameObject)>();
        public readonly List<ShelfUnit> Bays = new List<ShelfUnit>();
        public List<ShelfSlot> Slots = new List<ShelfSlot>();
    }

    readonly Dictionary<Vector2Int, Built> built = new Dictionary<Vector2Int, Built>();

    // What changed on a chunk's shelves while it was loaded, by slot (in the order the chunk
    // makes them): what's on it now, or null for empty. Only differences from the plan.
    readonly Dictionary<Vector2Int, Dictionary<int, string>> kept = new Dictionary<Vector2Int, Dictionary<int, string>>();

    readonly Dictionary<GameObject, Stack<GameObject>> pool = new Dictionary<GameObject, Stack<GameObject>>();
    readonly Stack<GameObject> shells = new Stack<GameObject>();
    readonly Queue<Vector2Int> toBuild = new Queue<Vector2Int>();
    readonly HashSet<Vector2Int> queued = new HashSet<Vector2Int>();
    readonly Queue<Vector2Int> toTakeDown = new Queue<Vector2Int>();
    readonly HashSet<Vector2Int> leaving = new HashSet<Vector2Int>();
    MazeWorld world;
    Vector2Int centre = new Vector2Int(int.MinValue, int.MinValue);
    Material lampBox, lampPanel;

    NavMeshData navData;
    NavMeshDataInstance navInstance;
    AsyncOperation navBuilding;
    bool navDirty;
    readonly List<NavMeshBuildSource> navSources = new List<NavMeshBuildSource>();

    public int ChunksBuilt => built.Count;
    public MazeWorld World => world;
    public bool IsBuilt(Vector2Int chunk) => built.ContainsKey(chunk);

    void Awake()
    {
        world = new MazeWorld(seed);
        if (runPrefab != null) footprint[runPrefab] = Footprint(runPrefab);
        if (pillarPrefab != null) footprint[pillarPrefab] = Footprint(pillarPrefab);
        lampBox = AisleSigns.Flat(new Color(0.015f, 0.015f, 0.018f), 0.25f);
        lampPanel = CeilingLamps.Glow();
    }

    void OnEnable()
    {
        navData = new NavMeshData(0);
        navInstance = NavMesh.AddNavMeshData(navData);
    }

    void OnDisable()
    {
        navInstance.Remove();
        navBuilding = null;
    }

    void Update()
    {
        if (player == null) return;

        Vector2Int at = MazeWorld.ChunkAt(player.position);
        if (at != centre)
        {
            centre = at;
            Plan();
        }

        // One chunk a frame, so walking over a border doesn't stall a frame: the nearest missing
        // chunk is built, or when none is missing, one too far away is taken down.
        if (toBuild.Count > 0)
        {
            Vector2Int next = toBuild.Dequeue();
            queued.Remove(next);
            if (!built.ContainsKey(next) && Chebyshev(next, centre) <= radius)
            {
                Build(next);
                navDirty = true;
            }
        }
        else if (toTakeDown.Count > 0)
        {
            Vector2Int next = toTakeDown.Dequeue();
            leaving.Remove(next);
            if (Chebyshev(next, centre) > radius + 1)
            {
                TakeDown(next);
                navDirty = true;
            }
        }

        UpdateNavMesh();
    }

    static int Chebyshev(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    // What should be built around the player, nearest first, and what's too far to keep.
    void Plan()
    {
        foreach (Vector2Int k in built.Keys)
            if (Chebyshev(k, centre) > radius + 1 && leaving.Add(k)) toTakeDown.Enqueue(k);

        var wanted = new List<Vector2Int>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var k = new Vector2Int(centre.x + dx, centre.y + dz);
                if (!built.ContainsKey(k) && !queued.Contains(k)) wanted.Add(k);
            }
        wanted.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));
        foreach (Vector2Int k in wanted)
        {
            toBuild.Enqueue(k);
            queued.Add(k);
        }
        world.Forget(centre, radius + 2);
    }

    // Builds every chunk around the player now, and takes down those too far away, not one a
    // frame: for tests and for the start.
    public void BuildAllNow()
    {
        if (player == null) return;
        centre = MazeWorld.ChunkAt(player.position);
        Plan();
        while (toBuild.Count > 0)
        {
            Vector2Int k = toBuild.Dequeue();
            queued.Remove(k);
            if (!built.ContainsKey(k)) Build(k);
        }
        while (toTakeDown.Count > 0)
        {
            Vector2Int k = toTakeDown.Dequeue();
            leaving.Remove(k);
            if (Chebyshev(k, centre) > radius + 1) TakeDown(k);
        }
        navDirty = true;
    }

    // ---------------------------------------------------------------- a chunk

    void Build(Vector2Int k)
    {
        var b = new Built { Root = new GameObject($"Chunk {k.x},{k.y}") };
        b.Root.transform.SetParent(transform, false);
        MazeGenerator.Chunk chunk = world.Get(k.x, k.y);
        Vector3 origin = chunk.Origin;

        foreach (MazeGenerator.Piece piece in MazeGenerator.Pieces(world, k.x, k.y))
        {
            GameObject prefab = piece.Kind == MazeGenerator.PieceKind.Run ? runPrefab : pillarPrefab;
            if (prefab == null) continue;
            GameObject go = Take(prefab);
            go.transform.SetParent(b.Root.transform, false);
            Quaternion yaw = Quaternion.Euler(0f, piece.Yaw, 0f);
            Vector3 middle = footprint.TryGetValue(prefab, out Bounds f) ? f.center : Vector3.zero;
            go.transform.SetPositionAndRotation(piece.Position - yaw * new Vector3(middle.x, 0f, middle.z), yaw);
            b.Pieces.Add((go, prefab));
            if (go.TryGetComponent(out ShelfUnit unit)) b.Bays.Add(unit);
        }

        b.Shell = TakeShell();
        b.Shell.transform.SetParent(b.Root.transform, false);
        b.Shell.transform.position = origin;

        ItemType section = MazeGenerator.SectionOf(world.Seed, k.x, k.y);
        b.Slots = StoreLayout.StockBays(b.Bays, section, ProductCatalog.SectionName(section), ShelfStock.Current);
        if (kept.TryGetValue(k, out Dictionary<int, string> changes))
            foreach (KeyValuePair<int, string> change in changes)
                if (change.Key < b.Slots.Count) b.Slots[change.Key].Load(change.Value != null, change.Value);
        ShelfStock.Current.Finish();
        built[k] = b;
    }

    void TakeDown(Vector2Int k)
    {
        if (!built.TryGetValue(k, out Built b)) return;

        // What's changed on its shelves is kept against the plan, for when it comes back.
        var changes = new Dictionary<int, string>();
        for (int i = 0; i < b.Slots.Count; i++)
        {
            ShelfSlot s = b.Slots[i];
            if (!s.isFilled) changes[i] = null;
            else if (s.StockedId != s.productId) changes[i] = s.StockedId;
        }
        if (changes.Count > 0) kept[k] = changes;
        else kept.Remove(k);

        var bays = new List<Transform>(b.Bays.Count);
        foreach (ShelfUnit unit in b.Bays) bays.Add(unit.transform);
        ShelfStock.Current.Remove(bays);
        foreach ((GameObject go, GameObject prefab) in b.Pieces) Give(go, prefab);
        GiveShell(b.Shell);
        Destroy(b.Root);
        built.Remove(k);
    }

    // ---------------------------------------------------------------- a chunk's shell

    // A chunk's floor, ceiling, and ceiling lights with their lamps, from its south-west
    // corner: made once, then moved from chunk to chunk.
    GameObject TakeShell()
    {
        GameObject shell = shells.Count > 0 ? shells.Pop() : MakeShell();
        shell.SetActive(true);
        return shell;
    }

    void GiveShell(GameObject shell)
    {
        if (shell == null) return;
        shell.SetActive(false);
        shell.transform.SetParent(transform, false);
        shells.Push(shell);
    }

    GameObject MakeShell()
    {
        var shell = new GameObject("Shell");
        Surface(shell.transform, "Floor", 0f, true);
        Surface(shell.transform, "Ceiling", ceilingHeight, false);
        for (int i = 0; i < MazeGenerator.Cells; i++)
            for (int j = 0; j < MazeGenerator.Cells; j++)
                CeilingLight(shell.transform, new Vector3((i + 0.5f) * MazeGenerator.CellSize, 0f, (j + 0.5f) * MazeGenerator.CellSize));
        return shell;
    }

    void Surface(Transform shell, string name, float height, bool floor)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(shell, false);
        float half = MazeGenerator.ChunkSize * 0.5f;
        quad.transform.SetLocalPositionAndRotation(new Vector3(half, height, half), Quaternion.Euler(floor ? 90f : -90f, 0f, 0f));
        quad.transform.localScale = new Vector3(MazeGenerator.ChunkSize, MazeGenerator.ChunkSize, 1f);
        Material m = floor ? floorMaterial : ceilingMaterial;
        if (m != null) quad.GetComponent<MeshRenderer>().sharedMaterial = m;
        quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        if (!floor) return;
        // Something solid to stand on, and for the NavMesh to find.
        var solid = new GameObject("Floor collider");
        solid.transform.SetParent(shell, false);
        solid.transform.localPosition = new Vector3(half, -0.1f, half);
        solid.AddComponent<BoxCollider>().size = new Vector3(MazeGenerator.ChunkSize, 0.2f, MazeGenerator.ChunkSize);
    }

    void CeilingLight(Transform shell, Vector3 below)
    {
        var go = new GameObject("Ceiling light");
        go.transform.SetParent(shell, false);
        go.transform.SetLocalPositionAndRotation(below + Vector3.up * lightHeight, Quaternion.Euler(90f, 0f, 0f));
        Light light = go.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = lightColour;
        light.intensity = lightIntensity;
        light.range = lightRange;
        light.spotAngle = lightAngle;
        light.shadows = LightShadows.None;

        // The lamp it hangs in: a black box with a lit panel under it, like the store's.
        Lamp(go.transform, "Box", new Vector3(0f, 0f, -0.012f - CeilingLamps.BoxSize * 0.5f), Vector3.one * CeilingLamps.BoxSize, lampBox);
        Lamp(go.transform, "Panel", new Vector3(0f, 0f, 0.003f), new Vector3(CeilingLamps.PanelSize, CeilingLamps.PanelSize, 0.004f), lampPanel);
    }

    static void Lamp(Transform light, string name, Vector3 local, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(light, false);
        box.transform.localPosition = local;
        box.transform.localScale = size;
        MeshRenderer r = box.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Where a prefab's footprint is, from its own origin: a piece is placed so the middle of
    // what it stands on is on its wall, whatever its pivot.
    readonly Dictionary<GameObject, Bounds> footprint = new Dictionary<GameObject, Bounds>();

    public static Bounds Footprint(GameObject prefab)
    {
        bool any = false;
        var b = new Bounds();
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        foreach (MeshFilter f in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            Bounds m = f.sharedMesh.bounds;
            Matrix4x4 local = toRoot * f.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = m.center + Vector3.Scale(m.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = local.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ---------------------------------------------------------------- pooling

    GameObject Take(GameObject prefab)
    {
        if (pool.TryGetValue(prefab, out Stack<GameObject> free) && free.Count > 0)
        {
            GameObject go = free.Pop();
            go.SetActive(true);
            return go;
        }
        return Instantiate(prefab);
    }

    void Give(GameObject go, GameObject prefab)
    {
        if (go == null) return;
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        if (!pool.TryGetValue(prefab, out Stack<GameObject> free)) pool[prefab] = free = new Stack<GameObject>();
        free.Push(go);
    }

    // ---------------------------------------------------------------- the NavMesh

    // Over the chunks that are built, rebuilt off the main thread once chunks have finished
    // coming and going: what's on this thread (collecting the colliders, and their meshes) costs
    // 25 to 45 ms, so it's done once a border crossed, not once a chunk. One build at a time.
    void UpdateNavMesh()
    {
        if (navBuilding != null && !navBuilding.isDone) return;
        navBuilding = null;
        if (!navDirty || toBuild.Count > 0 || toTakeDown.Count > 0) return;
        navDirty = false;

        float size = (radius * 2 + 1) * MazeGenerator.ChunkSize;
        Vector3 middle = new Vector3((centre.x + 0.5f) * MazeGenerator.ChunkSize, 2f, (centre.y + 0.5f) * MazeGenerator.ChunkSize);
        var bounds = new Bounds(middle, new Vector3(size, 6f, size));
        NavMeshBuilder.CollectSources(bounds, ~LayerMask.GetMask("Interactable", "Ignore Raycast"),
            NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), navSources);
        navBuilding = NavMeshBuilder.UpdateNavMeshDataAsync(navData, NavMesh.GetSettingsByID(0), navSources, bounds);
    }

    public bool NavMeshBusy => navBuilding != null && !navBuilding.isDone;
}
