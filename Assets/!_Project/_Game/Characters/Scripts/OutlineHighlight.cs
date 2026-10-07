using System.Collections.Generic;
using UnityEngine;

// Draws a yellow inverted-hull outline, toggled on while the player is looking at the object.
// Built lazily on first use, because most shelves in a level are never highlighted and every
// outline duplicates geometry.
public class OutlineHighlight : MonoBehaviour
{
    public enum OutlineShape
    {
        PerMesh,      // one shell per mesh — follows the silhouette, good for compact objects
        BoundingBox   // one shell around the whole object — right for thin, spindly assemblies
    }

    [Header("Outline")]
    public Material outlineMaterial;
    public Color outlineColor = new Color(1f, 0.85f, 0.1f, 1f);
    [Range(0f, 0.2f)] public float outlineWidth = 0.035f;

    [Tooltip("Shelves are built from 2cm-thin boards; a per-mesh hull balloons around them, " +
             "so those use a single box around the whole unit instead.")]
    public OutlineShape shape = OutlineShape.PerMesh;

    [Tooltip("Outline stock sitting on this object too. Off means a shelf outlines its own " +
             "frame rather than the products standing on it.")]
    public bool includeItems = false;

    // The Item on this object, if it is one. Stock belonging to something else is what
    // the filter below is for; an item outlining its own mesh is not.
    Item ownItem;

    readonly List<GameObject> outlineObjects = new List<GameObject>();
    bool isHighlighted;
    bool built;

    void EnsureBuilt()
    {
        if (built) return;
        built = true;

        ownItem = GetComponent<Item>();

        if (outlineMaterial == null)
        {
            Debug.LogWarning($"{name}: OutlineHighlight has no outlineMaterial assigned.", this);
            return;
        }

        if (shape == OutlineShape.BoundingBox) BuildBoundingBox();
        else BuildPerMesh();
    }

    Material CreateMaterial(float width)
    {
        Material material = new Material(outlineMaterial);
        material.SetColor("_OutlineColor", outlineColor);
        material.SetFloat("_OutlineWidth", width);
        return material;
    }

    void BuildPerMesh()
    {
        // Snapshot first — we add children while iterating otherwise.
        MeshFilter[] sourceFilters = GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter filter in sourceFilters)
        {
            if (filter.sharedMesh == null) continue;
            if (filter.GetComponent<MeshRenderer>() == null) continue;

            // Products on a shelf are their own objects — highlight the fixture, not the
            // stock. An item highlighting itself is a different case, so only skip meshes
            // that belong to some *other* Item.
            if (!includeItems && BelongsToAnotherItem(filter.transform)) continue;

            // A hull wider than the part itself swallows it, so cap the expansion at a
            // quarter of the thinnest world-space dimension of this particular piece.
            Vector3 worldSize = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
            float thinnest = Mathf.Min(worldSize.x, Mathf.Min(worldSize.y, worldSize.z));
            float width = Mathf.Min(outlineWidth, Mathf.Max(0.002f, thinnest * 0.25f));

            CreateShell(filter.transform, filter.sharedMesh, Vector3.zero, Quaternion.identity, Vector3.one,
                        filter.gameObject.layer, CreateMaterial(width));
        }
    }

    void BuildBoundingBox()
    {
        // Collect every renderer's bounds in this object's local space.
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool any = false;
        Bounds local = new Bounds();

        foreach (Renderer renderer in renderers)
        {
            if (!includeItems && BelongsToAnotherItem(renderer.transform)) continue;
            if (renderer.GetComponent<MeshFilter>() == null) continue;

            Bounds b = renderer.bounds;   // world space
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 world = b.center + Vector3.Scale(b.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                Vector3 point = transform.InverseTransformPoint(world);

                if (!any) { local = new Bounds(point, Vector3.zero); any = true; }
                else local.Encapsulate(point);
            }
        }

        if (!any) return;

        Mesh cube = CubeMesh();
        if (cube == null) return;

        CreateShell(transform, cube, local.center, Quaternion.identity, local.size,
                    gameObject.layer, CreateMaterial(outlineWidth));
    }

    bool BelongsToAnotherItem(Transform part)
    {
        Item item = part.GetComponentInParent<Item>();
        return item != null && item != ownItem;
    }

    void CreateShell(Transform parent, Mesh mesh, Vector3 localPosition, Quaternion localRotation,
                     Vector3 localScale, int layer, Material material)
    {
        GameObject shell = new GameObject("__Outline");
        shell.transform.SetParent(parent, false);
        shell.transform.localPosition = localPosition;
        shell.transform.localRotation = localRotation;
        shell.transform.localScale = localScale;
        shell.layer = layer;

        shell.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = shell.AddComponent<MeshRenderer>();
        // One per submesh, so a product made of several materials is outlined all round.
        var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // New GameObjects start active. Match the current state instead, otherwise a
        // freshly built outline shows until the first hover toggles it off.
        shell.SetActive(isHighlighted);

        outlineObjects.Add(shell);
    }

    static Mesh cachedCube;
    static Mesh CubeMesh()
    {
        if (cachedCube == null)
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cachedCube = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);
        }
        return cachedCube;
    }

    // The mesh underneath changed — a placeholder box dressed as its product — so the shells
    // built for the old one are thrown away and the next highlight builds new ones.
    public void Rebuild()
    {
        foreach (GameObject shell in outlineObjects)
            if (shell != null) Destroy(shell);
        outlineObjects.Clear();
        built = false;

        if (!isHighlighted) return;
        EnsureBuilt();
        foreach (GameObject shell in outlineObjects)
            if (shell != null) shell.SetActive(true);
    }

    public void SetHighlighted(bool on)
    {
        if (isHighlighted == on) return;
        isHighlighted = on;

        if (on) EnsureBuilt();   // turning off before anything was built has nothing to hide

        foreach (GameObject shell in outlineObjects)
        {
            if (shell != null)
                shell.SetActive(on);
        }
    }
}
