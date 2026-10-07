using System.Collections.Generic;
using UnityEngine;

// The rest of a facing. A slot holds one item — the thing the player picks up and puts back —
// but a stocked shelf shows a block of the product, several across and a few deep, pulled up
// to the front. This draws that block around the slot's item as a single mesh, shared by every
// facing of the same product and size, and hides it whenever the slot is empty, so a gap on
// the shelf still means "restock me".
//
// The block is scenery: no colliders, no shadows, and the item itself stands in one of its
// cells (ItemCell), so what the player takes is visibly part of it.
[DisallowMultipleComponent]
public class Backstock : MonoBehaviour
{
    // How the block is cut. Wider and deeper than this is more mesh than anyone will notice:
    // at three deep and 24 units the shop's blocks came to ten million vertices, and doubled
    // what an aisle cost to draw.
    public const int MaxAcross = 6;
    public const int MaxDeep = 2;
    public const int MaxUnits = 12;

    // Under this much of the screen's height (about 20 m off for a slot's block) it isn't drawn.
    public const float CullBelow = 0.02f;
    public const float Gap = 0.012f;          // between neighbouring packs
    public const float StackLimit = 0.24f;    // flat packs stack, up to this high

    // A slot's share of its board when nothing narrower is known: half a metre-wide pillar
    // board across, half a two-sided board deep.
    public static readonly Vector2 DefaultFootprint = new Vector2(0.46f, 0.44f);

    public readonly struct Block
    {
        public readonly int Across, Deep, Stack;
        public readonly Vector3 Pitch;          // centre to centre, x across, y up, z deep
        public readonly Vector3 ItemCell;       // the cell the slot's own item stands in

        public Block(int across, int deep, int stack, Vector3 pitch)
        {
            Across = across;
            Deep = deep;
            Stack = stack;
            Pitch = pitch;
            ItemCell = Cell(across, deep, pitch, across / 2, deep / 2, 0);
        }

        public int Units => Across * Deep * Stack;

        // The centre of one unit, relative to the middle of the slot, before any mess.
        public static Vector3 Cell(int across, int deep, Vector3 pitch, int i, int j, int k) =>
            new Vector3((i - (across - 1) * 0.5f) * pitch.x, k * pitch.y, (j - (deep - 1) * 0.5f) * pitch.z);

        public Vector3 CellAt(int i, int j, int k) => Cell(Across, Deep, Pitch, i, j, k);

        public Vector3 Size => new Vector3((Across - 1) * Pitch.x, (Stack - 1) * Pitch.y, (Deep - 1) * Pitch.z);
    }

    // How many of a pack fit in a slot's footprint (x across the shelf, y into it).
    public static Block Fit(Vector3 packSize, Vector2 footprint)
    {
        var pitch = new Vector3(packSize.x + Gap, packSize.y + 0.002f, packSize.z + Gap);
        int across = Mathf.Clamp(Mathf.FloorToInt((footprint.x + Gap) / pitch.x), 1, MaxAcross);
        int deep = Mathf.Clamp(Mathf.FloorToInt((footprint.y + Gap) / pitch.z), 1, MaxDeep);
        int stack = Mathf.Clamp(Mathf.FloorToInt(StackLimit / Mathf.Max(0.01f, pitch.y)), 1, 3);
        if (packSize.y > 0.08f) stack = 1;      // only tins, bars and flat packs are stacked

        // Too many: lose rows from the back first (nobody sees them), then layers, then width.
        while (across * deep * stack > MaxUnits)
        {
            if (deep > 1) deep--;
            else if (stack > 1) stack--;
            else across--;
        }
        return new Block(across, deep, stack, pitch);
    }

    // ------------------------------------------------------------------ shared meshes

    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMeshes() => meshes.Clear();

    // One combined mesh per product and block shape: every unit except the item's own cell,
    // each nudged a centimetre and turned a few degrees so the block looks hand-faced.
    static Mesh MeshFor(string productId, ProductLook.Look look, Block block)
    {
        string key = $"{productId}/{block.Across}x{block.Deep}x{block.Stack}";
        if (meshes.TryGetValue(key, out Mesh cached)) return cached;

        Mesh source = look.Mesh;
        if (!source.isReadable) { meshes[key] = null; return null; }

        float rest = look.RestHeight;
        var random = new System.Random(Hash(productId));
        var cells = new List<Matrix4x4>();
        for (int k = 0; k < block.Stack; k++)
            for (int j = 0; j < block.Deep; j++)
                for (int i = 0; i < block.Across; i++)
                {
                    if (k == 0 && i == block.Across / 2 && j == block.Deep / 2) continue;   // the item's cell
                    Vector3 c = block.CellAt(i, j, k);
                    c.x += Range(random, -0.6f, 0.6f) * Gap;
                    c.z += Range(random, -0.6f, 0.6f) * Gap;
                    c.y += rest;
                    float yaw = Range(random, -4f, 4f);
                    cells.Add(Matrix4x4.TRS(c, Quaternion.Euler(0f, yaw, 0f), Vector3.one));
                }

        Mesh combined = null;
        if (cells.Count > 0)
        {
            // Combine per material first, then stack those as submeshes, so the block keeps
            // the product's materials one-to-one.
            var perMaterial = new CombineInstance[source.subMeshCount];
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var parts = new CombineInstance[cells.Count];
                for (int n = 0; n < cells.Count; n++)
                    parts[n] = new CombineInstance { mesh = source, subMeshIndex = s, transform = cells[n] };
                var merged = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                merged.CombineMeshes(parts, true, true);
                perMaterial[s] = new CombineInstance { mesh = merged, transform = Matrix4x4.identity };
            }
            combined = new Mesh { name = "Backstock_" + key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            combined.CombineMeshes(perMaterial, false, false);
            foreach (CombineInstance part in perMaterial) Destroy(part.mesh);
            combined.UploadMeshData(true);
        }
        meshes[key] = combined;
        return combined;
    }

    static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static int Hash(string s)
    {
        unchecked
        {
            int h = (int)2166136261;
            foreach (char ch in s) h = (h ^ ch) * 16777619;
            return h;
        }
    }

    // ------------------------------------------------------------------ per slot

    MeshFilter filter;
    MeshRenderer meshRenderer;
    LODGroup lod;

    public static Backstock On(ShelfSlot slot)
    {
        Transform existing = slot.transform.Find("Backstock");
        if (existing != null && existing.TryGetComponent(out Backstock found)) return found;

        var go = new GameObject("Backstock");
        go.transform.SetParent(slot.transform, false);
        var stock = go.AddComponent<Backstock>();
        stock.filter = go.AddComponent<MeshFilter>();
        stock.meshRenderer = go.AddComponent<MeshRenderer>();
        stock.meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        stock.meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        stock.meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        stock.lod = go.AddComponent<LODGroup>();
        stock.lod.SetLODs(new[] { new LOD(CullBelow, new Renderer[] { stock.meshRenderer }) });
        return stock;
    }

    // Shows the block for a product, or hides it. Returns where the slot's own item should
    // stand, or zero when there is no block.
    public Vector3 Show(string productId, Vector2 footprint, bool visible)
    {
        ProductLook.Look? look = ProductLook.For(productId);
        if (look == null)
        {
            meshRenderer.enabled = false;
            return Vector3.zero;
        }

        Block block = Fit(look.Value.Mesh.bounds.size, footprint);
        Mesh mesh = MeshFor(productId, look.Value, block);
        if (filter.sharedMesh != mesh)
        {
            filter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = look.Value.Materials;
            if (lod != null && mesh != null) lod.RecalculateBounds();
        }
        meshRenderer.enabled = visible && mesh != null;
        return block.ItemCell;
    }

    public void Hide()
    {
        if (meshRenderer != null) meshRenderer.enabled = false;
    }
}
