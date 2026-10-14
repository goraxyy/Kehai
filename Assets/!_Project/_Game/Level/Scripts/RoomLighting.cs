using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Keeps every light in its own room. The 240 ceiling lights are spots without shadows (shadows
// on that many would cost more than the rest of the frame), and a light without shadows goes
// straight through walls: the sales floor's lights lit the stockroom floor through the wall,
// and the sun lit the staff room through the roof.
//
// So each walled room gets a rendering layer, and:
//   - a ceiling light lights only its own room's layer,
//   - the sun lights only outside, and an outside wall is on the outside layer only if the
//     sun can reach its outer face: on the west and north walls it could only ever reach the
//     inside one, and lit the staff room's wall from within,
//   - each room has a reflection probe of its own dim interior, so nothing indoors reflects
//     the sky: with none, every wall mirrored the bright sky outside at a glancing angle,
//   - a still thing (a wall, a shelf, a fridge) is on the layers of the rooms beside it, so
//     a wall between two rooms is lit from both sides and nothing reaches through it,
//   - anything that moves (the player, shoppers, Karen, stock on the move) keeps the default
//     layer, which every light still lights, so it is never left in the dark by changing room.
// The floor and the roof are one mesh each across the whole building, so each room gets its
// own patch of floor and ceiling, drawn over them on the room's layer.
//
// The rooms are the walled spaces StoreMap finds, named for the part of the building
// (StoreMap.AreaAt) they're in. Where a part has two (the storage room and the cold room are
// both the stockroom, and the staff room has a second room behind it), the second takes one of
// the spare layers: on one layer, the cold room's lights lit the storage room through the wall.
//
// Built at load. A light made later lights the default layer only, so moving things but not
// the building: give it RoomLighting.Everything if it should.
//
// URP drops every bit of a light's layers that isn't a rendering layer named in Project
// Settings > Tags and Layers, so the rooms use layers 1-5, which URP names "Light Layer 1-5"
// out of the box. If they aren't named, nothing is split: a light through a wall beats a
// shop lit by nothing.
public static class RoomLighting
{
    public const int Outside = 1 << 1;
    public const int SalesFloor = 1 << 2;
    public const int Lobby = 1 << 3;
    public const int Stockroom = 1 << 4;
    public const int StaffRoom = 1 << 5;
    // For a second walled room in a part of the building that has two.
    public static readonly int[] Spare = { 1 << 6, 1 << 7, 1 << 8 };
    public const int AllRooms = Outside | SalesFloor | Lobby | Stockroom | StaffRoom | (1 << 6) | (1 << 7);
    public const uint Moving = 1;                       // the default layer
    public const uint Everything = 0xFFFFFFFF;
    public const string PatchesName = "RoomPatches";
    public const string ProbesName = "RoomReflections";

    // What a room reflects: a dim interior, not the sky.
    public static readonly Color InteriorReflection = new Color(0.05f, 0.052f, 0.058f);

    // Whether URP will keep the rooms' layers on a light.
    public static bool LayersDefined =>
        (RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue() & (uint)AllRooms) == (uint)AllRooms;

    static readonly (string area, int bit)[] Rooms =
    {
        ("Sales floor", SalesFloor), ("Lobby", Lobby), ("Stockroom", Stockroom), ("Staff room", StaffRoom),
    };

    public static int BitFor(string area)
    {
        foreach (var (name, bit) in Rooms) if (name == area) return bit;
        return Outside;                                 // the street, the alleys, the yard
    }

    // The layer of the room a point is in. In play, from the walled rooms (the map); out of
    // play, from the parts of the building alone, so the editor never builds a map for it.
    static StoreMap authoringMap;
    static StoreMap ActiveMap => authoringMap ?? (Application.isPlaying ? StoreMap.Current : null);
    public static int BitAt(Vector3 p) => BitAt(p, ActiveMap);

    public static int BitAt(Vector3 p, StoreMap map)
    {
        WalledRooms walled = RoomsOf(map);
        int room = walled != null ? walled.At(p) : -1;
        return room >= 0 ? walled.Bit[room] : BitFor(StoreMap.AreaAt(p));
    }

    // The rooms a box stands beside: points along both of its long faces, a little out from
    // them and in from its ends. So a wall between two rooms counts for both, and a wall that
    // runs up to the outside wall doesn't count as outside. In play, each point is in the room
    // of the nearest floor on that side of the box: the walls, not the area table, say where a
    // room ends (the west wall stands half a metre inside the table's line, and its inside face
    // was left to the sun).
    public static int MaskFor(Bounds b) => MaskFor(b, ActiveMap);

    // Toward the sun, while Apply works out the masks; zero when there's no sun to think of.
    static Vector3 towardSun;

    public static int MaskFor(Bounds b, StoreMap map)
    {
        WalledRooms walled = RoomsOf(map);
        int outdoorSides = 0, indoorSides = 0;
        bool sunOnOuterFace = false;
        Vector3 c = b.center;
        bool alongX = b.size.x >= b.size.z;
        float length = alongX ? b.size.x : b.size.z, half = alongX ? b.extents.z : b.extents.x;
        Vector3 out1 = alongX ? Vector3.forward : Vector3.right;
        float inset = Mathf.Min(0.6f, length * 0.25f);
        int steps = Mathf.Max(2, Mathf.CeilToInt((length - inset * 2f) / 3f));
        int mask = 0;
        for (int i = 0; i <= steps; i++)
        {
            float t = -length * 0.5f + inset + (length - inset * 2f) * i / steps;
            Vector3 at = c + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
            foreach (float sign in new[] { 1f, -1f })
            {
                Vector3 outward = out1 * sign, p = at + outward * (half + 0.3f);
                int room = walled != null ? walled.Beside(p, at, outward, half) : -1;
                int bit = room != -1 ? walled.BitOf(room) : BitAt(p, map);
                mask |= bit;
                if (bit == Outside) { outdoorSides |= sign > 0f ? 1 : 2; sunOnOuterFace |= Vector3.Dot(outward, towardSun) > 0.01f; }
                else indoorSides |= sign > 0f ? 1 : 2;
            }
        }
        // A wall with a room on one side and the outside on the other, whose outer face the sun
        // never reaches: the outside layer would only let the sun light it from inside.
        bool oneOfEach = (outdoorSides == 1 && indoorSides == 2) || (outdoorSides == 2 && indoorSides == 1);
        if (oneOfEach && towardSun != Vector3.zero && !sunOnOuterFace) mask &= ~Outside;
        return mask == 0 ? BitAt(c, map) : mask;
    }

    // ---- the walled rooms -----------------------------------------------------------------

    // StoreMap's rooms, each with its layer, and which of them a point is in.
    sealed class WalledRooms
    {
        public StoreMap Map;
        public int[] Bit;
        public string[] Area;
        readonly Dictionary<string, List<int>> ofArea = new Dictionary<string, List<int>>();
        readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();
        readonly Dictionary<Vector2Int, List<int>> allCells = new Dictionary<Vector2Int, List<int>>();
        const float Bucket = 2f;

        public WalledRooms(StoreMap map)
        {
            Map = map;
            int n = map.Rooms.Count;
            Bit = new int[n];
            Area = new string[n];
            int spare = 0;
            var taken = new HashSet<string>();
            for (int r = 0; r < n; r++)
            {
                Area[r] = AreaOf(map.Rooms[r]);
                int bit = BitFor(Area[r]);
                // The first room of a part keeps its layer; another takes a spare one, while any are left.
                if (bit != Outside && !taken.Add(Area[r]))
                {
                    uint defined=RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue();
                    while(spare<Spare.Length && (defined & (uint)Spare[spare])==0)spare++;
                    if(spare<Spare.Length)bit=Spare[spare++];
                }
                Bit[r] = bit;
                if (!ofArea.TryGetValue(Area[r], out List<int> list)) ofArea[Area[r]] = list = new List<int>();
                list.Add(r);
            }
            for (int c = 0; c < map.CellCount; c++)
            {
                int r = map.CellRoom[c];
                if (r < 0) continue;
                Vector2Int key = KeyOf(map.CellPosition[c]);
                if (!allCells.TryGetValue(key, out List<int> any)) allCells[key] = any = new List<int>();
                any.Add(c);
                if (ofArea[Area[r]].Count < 2) continue;   // only parts with two rooms are looked up by area
                if (!cells.TryGetValue(key, out List<int> here)) cells[key] = here = new List<int>();
                here.Add(c);
            }
        }

        // A room's layer: an outdoor one's is the outside's.
        public int BitOf(int room) => room == Outdoors ? Outside : Bit[room];
        public const int Outdoors = -2;

        // The room of the nearest floor to `p` within `radius` (Outdoors for the outside), only
        // counting floor on the far side of a plane through `on` facing `outward`, `half` thick.
        // -1 when there's no floor there.
        public int Beside(Vector3 p, Vector3 on, Vector3 outward, float half) => Nearest(p, 3f, on, outward, half);

        // The room whose floor a point on the floor is on: the nearest floor within 1.5 m.
        public int Floor(Vector3 p) => Nearest(p, 1.5f, p, Vector3.zero, 0f);

        int Nearest(Vector3 p, float radius, Vector3 on, Vector3 outward, float half)
        {
            int best = -1;
            float bestSqr = radius * radius;
            Vector2Int k = KeyOf(p);
            int reach = Mathf.CeilToInt(radius / Bucket);
            for (int dx = -reach; dx <= reach; dx++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    if (!allCells.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out List<int> here)) continue;
                    foreach (int c in here)
                    {
                        Vector3 cell = Map.CellPosition[c];
                        if (outward != Vector3.zero && Vector3.Dot(cell - on, outward) <= half) continue;
                        float sqr = (cell.x - p.x) * (cell.x - p.x) + (cell.z - p.z) * (cell.z - p.z);
                        if (sqr < bestSqr) { bestSqr = sqr; best = c; }
                    }
                }
            if (best < 0) return -1;
            int room = Map.CellRoom[best];
            return Bit[room] == Outside ? Outdoors : room;
        }

        // The room a point is in: the part of the building it's in, and where that part has two
        // rooms, the one whose floor is nearest. -1 outside, or in a part with no room.
        public int At(Vector3 p)
        {
            string area = StoreMap.AreaAt(p);
            if (BitFor(area) == Outside || !ofArea.TryGetValue(area, out List<int> rooms)) return -1;
            if (rooms.Count == 1) return rooms[0];
            int best = -1;
            float bestSqr = float.MaxValue;
            Vector2Int k = KeyOf(p);
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                {
                    if (!cells.TryGetValue(new Vector2Int(k.x + dx, k.y + dz), out List<int> here)) continue;
                    foreach (int c in here)
                    {
                        if (Area[Map.CellRoom[c]] != area) continue;
                        Vector3 d = Map.CellPosition[c] - p;
                        float sqr = d.x * d.x + d.z * d.z;
                        if (sqr < bestSqr) { bestSqr = sqr; best = Map.CellRoom[c]; }
                    }
                }
            return best >= 0 ? best : rooms[0];
        }

        static Vector2Int KeyOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.z / Bucket));

        // "Stockroom 2" is in the stockroom.
        static string AreaOf(string room)
        {
            int space = room.LastIndexOf(' ');
            return space > 0 && int.TryParse(room.Substring(space + 1), out _) ? room.Substring(0, space) : room;
        }
    }

    static WalledRooms walledRooms;

    static WalledRooms RoomsOf(StoreMap map)
    {
        if (map == null || map.Rooms.Count == 0) return null;
        if (walledRooms == null || walledRooms.Map != map) walledRooms = new WalledRooms(map);
        return walledRooms;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRooms() => walledRooms = null;

    // Builders have just changed the walls and navigation. Use those walled rooms in the
    // editor too, instead of treating all three west rooms as one coarse "Staff room" area.
    public static void Apply(StoreMap map)
    {
        authoringMap=map;
        try { Apply(); }
        finally { authoringMap=null; }
    }

    public static void Apply()
    {
        if (!LayersDefined)
        {
            Debug.LogWarning("Room lighting: rendering layers 1-5 aren't named in Tags and Layers, so URP " +
                             "would drop them and the rooms would go dark. Every light still lights every room.");
            return;
        }
        int lights = 0, still = 0;
        towardSun = Vector3.zero;
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type == LightType.Directional && light.isActiveAndEnabled) towardSun = -light.transform.forward;
            uint layers;
            if (light.type == LightType.Directional) layers = (uint)Outside | Moving;
            else if (light.shadows == LightShadows.None) layers = (uint)BitAt(light.transform.position) | Moving;
            else layers = Everything;                   // the torch, and anything else that casts shadows
            var data = light.GetUniversalAdditionalLightData();
            if (data != null) data.renderingLayers = layers;
            lights++;
        }

        Renderer floor = Find("Plane"), roof = Find("Roof");

        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (r == floor || r == roof || Moves(r)) continue;
            var anchor=r.GetComponentInParent<InteriorLightAnchor>();
            r.renderingLayerMask = (uint)(anchor!=null ? BitAt(anchor.Position) : MaskFor(r.bounds));
            still++;
        }

        towardSun = Vector3.zero;
        Patch(floor, up: true);
        Patch(roof, up: false);
        if (floor != null) Reflections(floor.bounds, roof != null ? roof.bounds.min.y : 8f);
        Debug.Log($"Room lighting: {lights} lights and {still} still renderers kept to their rooms.");
    }

    static bool Moves(Renderer r)
    {
        if (r.GetComponentInParent<Item>() != null) return true;
        if (r.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null) return true;
        if (r.GetComponentInParent<CharacterController>() != null) return true;
        Transform t = r.transform;
        while (t != null) { if (t.CompareTag("Player")) return true; t = t.parent; }
        return false;
    }

    static Renderer Find(string name)
    {
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            if (r.name == name && (r.bounds.size.x > 40f || r.bounds.size.z > 40f)) return r;
        return null;
    }

    // A reflection probe over each room, floor to roof, that reflects a dim interior: a few
    // pixels of one colour, so it costs no rendering.
    static void Reflections(Bounds building, float roofHeight)
    {
        if (GameObject.Find(ProbesName) != null) return;
        var root = new GameObject(ProbesName).transform;
        var cube = new Cubemap(8, TextureFormat.RGBA32, false) { name = "Interior reflection" };
        var pixels = new Color[8 * 8];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = InteriorReflection;
        for (int face = 0; face < 6; face++) cube.SetPixels(pixels, (CubemapFace)face);
        cube.Apply();
        foreach (var (name, bit, rect) in RoomRects(building))
        {
            var go = new GameObject("Reflections_" + name);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(rect.center.x, roofHeight * 0.5f, rect.center.y);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.customBakedTexture = cube;
            probe.size = new Vector3(rect.width, roofHeight, rect.height);
            probe.blendDistance = 0.5f;
            probe.importance = 2;
        }
    }

    // The floor (or the roof's underside) inside each room, as its own quad on the room's layer,
    // with the same material and texture placement; the big mesh is left to outside light.
    static void Patch(Renderer whole, bool up)
    {
        if (whole == null) return;
        var filter = whole.GetComponent<MeshFilter>();
        // If the mesh can't be read, it stays lit by every room rather than by none.
        whole.renderingLayerMask = (uint)AllRooms | (RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue() & (1u << 8));
        if (filter == null || filter.sharedMesh == null) return;
        if (!FitUv(filter, up, out Vector3 o, out Vector3 du, out Vector3 dv, out float y))
        {
            Debug.LogWarning($"Room lighting: can't read {whole.name}'s mesh, so it's lit from every room.");
            return;
        }
        whole.renderingLayerMask = (uint)Outside;

        Transform root = GameObject.Find(PatchesName)?.transform;
        if (root == null) root = new GameObject(PatchesName).transform;

        foreach (var (name, bit, rect) in RoomRects(whole.bounds))
        {
            var mesh = new Mesh { name = (up ? "Floor_" : "Ceiling_") + name };
            float h = y + (up ? 0.003f : -0.003f);
            var corners = new[]
            {
                new Vector3(rect.xMin, h, rect.yMin), new Vector3(rect.xMax, h, rect.yMin),
                new Vector3(rect.xMax, h, rect.yMax), new Vector3(rect.xMin, h, rect.yMax),
            };
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++) uvs[i] = new Vector2(Vector3.Dot(corners[i] - o, du), Vector3.Dot(corners[i] - o, dv));
            mesh.vertices = corners;
            mesh.uv = uvs;
            mesh.triangles = up ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(mesh.name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = whole.sharedMaterials;
            r.renderingLayerMask = (uint)bit;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    // Each room's floor: the rooms are rectangles, so the box round the points that land in it.
    // The walled rooms in play; out of it, the parts of the building.
    static List<(string name, int bit, Rect rect)> RoomRects(Bounds within)
    {
        WalledRooms walled = RoomsOf(ActiveMap);
        int count = walled != null ? walled.Bit.Length : Rooms.Length;
        var min = new Vector2[count];
        var max = new Vector2[count];
        for (int i = 0; i < count; i++) { min[i] = new Vector2(float.MaxValue, float.MaxValue); max[i] = -min[i]; }
        const float step = 0.5f;
        for (float x = within.min.x; x <= within.max.x; x += step)
            for (float z = within.min.z; z <= within.max.z; z += step)
            {
                var p = new Vector3(x, 0f, z);
                int i;
                if (walled == null) i = System.Array.FindIndex(Rooms, room => room.area == StoreMap.AreaAt(p));
                else
                {
                    // The floor's own room where there's floor near; the area table under walls and fittings.
                    i = walled.Floor(p);
                    if (i == -1) i = walled.At(p);
                }
                if (i < 0) continue;
                min[i] = Vector2.Min(min[i], new Vector2(x, z));
                max[i] = Vector2.Max(max[i], new Vector2(x, z));
            }
        var rects = new List<(string, int, Rect)>();
        for (int i = 0; i < count; i++)
        {
            if (min[i].x > max[i].x) continue;
            string name = walled != null ? walled.Map.Rooms[i] : Rooms[i].area;
            int bit = walled != null ? walled.Bit[i] : Rooms[i].bit;
            rects.Add((name, bit, Rect.MinMaxRect(min[i].x - step * 0.5f, min[i].y - step * 0.5f, max[i].x + step * 0.5f, max[i].y + step * 0.5f)));
        }
        return rects;
    }

    // How the mesh lays its texture over the horizontal face we're patching: a world origin and
    // the world directions of +u and +v, scaled so a dot product gives the uv.
    static bool FitUv(MeshFilter filter, bool up, out Vector3 origin, out Vector3 du, out Vector3 dv, out float height)
    {
        origin = du = dv = Vector3.zero;
        height = 0f;
        Mesh mesh = filter.sharedMesh;
        if (!mesh.isReadable) return false;
        Vector3[] v = mesh.vertices;
        Vector3[] n = mesh.normals;
        Vector2[] uv = mesh.uv;
        if (uv.Length != v.Length || n.Length != v.Length) return false;
        Matrix4x4 m = filter.transform.localToWorldMatrix;

        // Three corners of the face, not in a line.
        var picked = new List<int>();
        for (int i = 0; i < v.Length && picked.Count < 3; i++)
        {
            Vector3 normal = m.MultiplyVector(n[i]).normalized;
            if (up ? normal.y < 0.9f : normal.y > -0.9f) continue;
            if (picked.Count == 2)
            {
                Vector3 a = m.MultiplyPoint3x4(v[picked[0]]), b = m.MultiplyPoint3x4(v[picked[1]]), c = m.MultiplyPoint3x4(v[i]);
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-4f) continue;
            }
            else if (picked.Count == 1 && (m.MultiplyPoint3x4(v[i]) - m.MultiplyPoint3x4(v[picked[0]])).sqrMagnitude < 1e-4f) continue;
            picked.Add(i);
        }
        if (picked.Count < 3) return false;

        Vector3 p0 = m.MultiplyPoint3x4(v[picked[0]]), p1 = m.MultiplyPoint3x4(v[picked[1]]), p2 = m.MultiplyPoint3x4(v[picked[2]]);
        Vector2 t0 = uv[picked[0]], t1 = uv[picked[1]], t2 = uv[picked[2]];
        height = p0.y;

        // Solve uv = A * (x, z) + t0 relative to p0.
        Vector2 e1 = new Vector2(p1.x - p0.x, p1.z - p0.z), e2 = new Vector2(p2.x - p0.x, p2.z - p0.z);
        Vector2 f1 = t1 - t0, f2 = t2 - t0;
        float det = e1.x * e2.y - e1.y * e2.x;
        if (Mathf.Abs(det) < 1e-6f) return false;
        // Inverse of [e1 e2] applied to [f1 f2], per uv component.
        float a11 = e2.y / det, a12 = -e2.x / det, a21 = -e1.y / det, a22 = e1.x / det;
        var ux = new Vector2(f1.x * a11 + f2.x * a21, f1.x * a12 + f2.x * a22);   // du/dx, du/dz
        var vx = new Vector2(f1.y * a11 + f2.y * a21, f1.y * a12 + f2.y * a22);   // dv/dx, dv/dz
        du = new Vector3(ux.x, 0f, ux.y);
        dv = new Vector3(vx.x, 0f, vx.y);
        // Shift the origin so a dot product from it gives the uv itself.
        origin = p0 - (Vector3)SolveOffset(du, dv, t0);
        origin.y = p0.y;
        return true;
    }

    // The point (in xz, relative to which dot products give uv) whose uv is zero, given that
    // p0's uv is t0: p0 - q satisfies du·q = t0.x and dv·q = t0.y.
    static Vector3 SolveOffset(Vector3 du, Vector3 dv, Vector2 t0)
    {
        float det = du.x * dv.z - du.z * dv.x;
        if (Mathf.Abs(det) < 1e-9f) return Vector3.zero;
        float qx = (t0.x * dv.z - du.z * t0.y) / det;
        float qz = (du.x * t0.y - t0.x * dv.x) / det;
        return new Vector3(qx, 0f, qz);
    }
}
