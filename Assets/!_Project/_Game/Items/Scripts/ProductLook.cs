using System.Collections.Generic;
using UnityEngine;

// Dresses an item as the product it is. The shop is stocked with one placeholder prefab
// (Item_def, a 20 x 40 cm box) and told at load what each one holds; this swaps the box for
// the product's own model — mesh, materials, collider and weight — taken from its prefab in
// Resources/Products/<id>, which Kehai/Products/1. Import Product Models makes.
//
// The item stays the same object, so the slot holding it, the replay following it and
// anything else with a reference to it are none the wiser. Without an imported prefab the
// placeholder box stays as it is.
public static class ProductLook
{
    public const string ResourceFolder = "Products";

    public readonly struct Look
    {
        public readonly Mesh Mesh;
        public readonly Material[] Materials;
        public readonly float Mass;

        public Look(Mesh mesh, Material[] materials, float mass)
        {
            Mesh = mesh;
            Materials = materials;
            Mass = mass;
        }

        // From the centre of the box, which is the item's origin, down to the bottom of the mesh.
        public float RestHeight => Mesh.bounds.extents.y - Mesh.bounds.center.y;
    }

    static readonly Dictionary<string, Look?> cache = new Dictionary<string, Look?>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => cache.Clear();

    // The product's prefab, or null when it hasn't been imported.
    public static GameObject Prefab(string productId) =>
        string.IsNullOrEmpty(productId) ? null : Resources.Load<GameObject>(ResourceFolder + "/" + productId);

    public static Look? For(string productId)
    {
        if (string.IsNullOrEmpty(productId)) return null;
        if (cache.TryGetValue(productId, out Look? known)) return known;

        Look? look = null;
        GameObject prefab = Prefab(productId);
        if (prefab != null)
        {
            var filter = prefab.GetComponentInChildren<MeshFilter>();
            var renderer = prefab.GetComponentInChildren<MeshRenderer>();
            var body = prefab.GetComponent<Rigidbody>();
            if (filter != null && filter.sharedMesh != null && renderer != null)
                look = new Look(filter.sharedMesh, renderer.sharedMaterials, body != null ? body.mass : 0.3f);
        }
        cache[productId] = look;
        return look;
    }

    // Puts the product's model on an item. Play mode only: in the editor the layout pass
    // writes ids into the scene, and baking 3,400 mesh swaps into it would be a lot of scene.
    public static bool Apply(Item item)
    {
        if (item == null || !Application.isPlaying) return false;
        Look? found = For(item.productId);
        if (found == null) return false;
        Look look = found.Value;

        var filter = item.GetComponent<MeshFilter>();
        var renderer = item.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null) return false;
        if (filter.sharedMesh == look.Mesh) return true;

        item.transform.localScale = Vector3.one;
        filter.sharedMesh = look.Mesh;
        renderer.sharedMaterials = look.Materials;

        if (item.TryGetComponent(out BoxCollider box))
        {
            box.center = look.Mesh.bounds.center;
            box.size = look.Mesh.bounds.size;
        }
        if (item.TryGetComponent(out Rigidbody body)) body.mass = look.Mass;
        if (item.TryGetComponent(out OutlineHighlight outline)) outline.Rebuild();

        item.restHeight = look.RestHeight;
        if (item.isOnShelf) item.ApplyShelfTransform();
        return true;
    }
}
