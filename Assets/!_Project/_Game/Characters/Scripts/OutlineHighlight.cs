using System.Collections.Generic;
using UnityEngine;

// A yellow outline round the object while the player looks at it (or while a guide points at
// it). Since 28 November it is drawn on the screen (HighlightOutlines): a line of even width
// traced from the object's own shape, in place of the inverted-hull shells that stood out from
// thin boards and split at sharp corners. This component only says which renderers to trace.
public class OutlineHighlight : MonoBehaviour
{
    public enum OutlineShape
    {
        PerMesh,      // the object's meshes
        BoundingBox   // the same since the screen outline: it never balloons round thin parts
    }

    [Header("Outline")]
    [Tooltip("Not used since the screen outline (28 November); kept so scenes and the code that " +
             "copies it from one object to another still load.")]
    public Material outlineMaterial;
    public Color outlineColor = new Color(1f, 0.85f, 0.1f, 1f);
    [Tooltip("Not used since the screen outline: its width is HighlightOutlines.PixelsAt1080.")]
    [Range(0f, 0.2f)] public float outlineWidth = 0.035f;

    [Tooltip("Kept for scenes and callers; both shapes trace the meshes' own outline now.")]
    public OutlineShape shape = OutlineShape.PerMesh;

    [Tooltip("Outline stock sitting on this object too. Off means a shelf outlines its own " +
             "frame rather than the products standing on it.")]
    public bool includeItems = false;

    [Tooltip("Optional exact meshes to outline, e.g. a refrigerator frame without its glass.")]
    public MeshFilter[] targetMeshes;

    // The Item on this object, if it is one. Stock belonging to something else is what
    // the filter below is for; an item outlining its own mesh is not.
    Item ownItem;

    readonly List<Renderer> targets = new List<Renderer>();
    bool isHighlighted;
    bool hovered, guided;
    bool built;

    // The renderers the outline traces.
    public IReadOnlyList<Renderer> Targets { get { EnsureBuilt(); return targets; } }
    public bool Showing => isHighlighted;

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        ownItem = GetComponent<Item>();
        targets.Clear();

        if (targetMeshes != null && targetMeshes.Length > 0)
        {
            foreach (MeshFilter filter in targetMeshes)
                if (filter != null && filter.TryGetComponent(out MeshRenderer r)) Add(r);
            return;
        }
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r is MeshRenderer || r is SkinnedMeshRenderer) Add(r);
    }

    void Add(Renderer r)
    {
        // Products on a shelf are their own objects: highlight the fixture, not the stock. An
        // item highlighting itself is a different case, so only skip meshes that belong to some
        // *other* Item. Old hull shells, if a scene still carries one, are never traced.
        if (!includeItems && BelongsToAnotherItem(r.transform)) return;
        if (r.gameObject.name == "__Outline") return;
        targets.Add(r);
    }

    bool BelongsToAnotherItem(Transform part)
    {
        Item item = part.GetComponentInParent<Item>();
        return item != null && item != ownItem;
    }

    // The meshes underneath changed (a placeholder box dressed as its product, art attached to a
    // fixture): the renderers are looked up again.
    public void Rebuild() => built = false;

    public void SetGuided(bool on) { guided = on; ApplyHighlight(); }
    public void SetHighlighted(bool on) { hovered = on; ApplyHighlight(); }
    void Update() { if (guided) ApplyHighlight(); }

    void ApplyHighlight()
    {
        bool on = Edition.ShowUI && (hovered || (guided && Mathf.Repeat(Time.unscaledTime, 1.4f) < 1f));
        if (isHighlighted == on) return;
        isHighlighted = on;
        if (on) { EnsureBuilt(); HighlightOutlines.Show(this); }
        else HighlightOutlines.Hide(this);
    }

    void OnDisable()
    {
        HighlightOutlines.Hide(this);
        isHighlighted = false;
    }
}
