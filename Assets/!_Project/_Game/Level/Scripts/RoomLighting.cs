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
// So each part of the building gets a rendering layer (the rooms StoreMap.AreaAt names), and:
//   - a ceiling light lights only its own room's layer,
//   - the sun lights only outside,
//   - a still thing (a wall, a shelf, a fridge) is on the layers of the rooms it stands in, so
//     a wall between two rooms is lit from both sides and nothing reaches through it,
//   - anything that moves (the player, shoppers, Karen, stock on the move) keeps the default
//     layer, which every light still lights, so it is never left in the dark by changing room.
// The floor and the roof are one mesh each across the whole building, so each room gets its
// own patch of floor and ceiling, drawn over them on the room's layer.
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
    public const int AllRooms = Outside | SalesFloor | Lobby | Stockroom | StaffRoom;
    public const uint Moving = 1;                       // the default layer
    public const uint Everything = 0xFFFFFFFF;
    public const string PatchesName = "RoomPatches";

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

    public static int BitAt(Vector3 p) => BitFor(StoreMap.AreaAt(p));

    // The rooms a box stands in: its middle and its four corners, widened a little so a wall
    // between two rooms counts for both.
    public static int MaskFor(Bounds b)
    {
        Vector3 c = b.center, e = b.extents + new Vector3(0.3f, 0f, 0.3f);
        return BitAt(c) | BitAt(c + new Vector3(e.x, 0f, e.z)) | BitAt(c + new Vector3(-e.x, 0f, e.z))
             | BitAt(c + new Vector3(e.x, 0f, -e.z)) | BitAt(c + new Vector3(-e.x, 0f, -e.z));
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
        foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
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
            r.renderingLayerMask = (uint)MaskFor(r.bounds);
            still++;
        }

        Patch(floor, up: true);
        Patch(roof, up: false);
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

    // The floor (or the roof's underside) inside each room, as its own quad on the room's layer,
    // with the same material and texture placement; the big mesh is left to outside light.
    static void Patch(Renderer whole, bool up)
    {
        if (whole == null) return;
        var filter = whole.GetComponent<MeshFilter>();
        // If the mesh can't be read, it stays lit by every room rather than by none.
        whole.renderingLayerMask = (uint)AllRooms;
        if (filter == null || filter.sharedMesh == null) return;
        if (!FitUv(filter, up, out Vector3 o, out Vector3 du, out Vector3 dv, out float y))
        {
            Debug.LogWarning($"Room lighting: can't read {whole.name}'s mesh, so it's lit from every room.");
            return;
        }
        whole.renderingLayerMask = (uint)Outside;

        Transform root = GameObject.Find(PatchesName)?.transform;
        if (root == null) root = new GameObject(PatchesName).transform;

        foreach (var (area, bit) in Rooms)
        {
            if (!RectOf(area, whole.bounds, out Rect rect)) continue;
            var mesh = new Mesh { name = (up ? "Floor_" : "Ceiling_") + area };
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

    // The part of the building StoreMap calls `area`: its rooms are rectangles, so the box
    // round the points that land in it.
    static bool RectOf(string area, Bounds within, out Rect rect)
    {
        float xMin = float.MaxValue, xMax = float.MinValue, zMin = float.MaxValue, zMax = float.MinValue;
        const float step = 0.5f;
        for (float x = within.min.x; x <= within.max.x; x += step)
            for (float z = within.min.z; z <= within.max.z; z += step)
            {
                if (StoreMap.AreaAt(new Vector3(x, 0f, z)) != area) continue;
                xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
                zMin = Mathf.Min(zMin, z); zMax = Mathf.Max(zMax, z);
            }
        rect = Rect.MinMaxRect(xMin - step * 0.5f, zMin - step * 0.5f, xMax + step * 0.5f, zMax + step * 0.5f);
        return xMin <= xMax;
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
