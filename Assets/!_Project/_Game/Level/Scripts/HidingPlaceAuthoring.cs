using Kehai.Karen;
using UnityEngine;
using UnityEngine.SceneManagement;

// Repeatable support for scenes built before hiding volumes were part of the recipes.
// Only known hiding furniture qualifies; ordinary shop shelves never get this override.
public static class HidingPlaceAuthoring
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
        Apply(SceneManager.GetActiveScene());
    }
    static void Loaded(Scene scene, LoadSceneMode mode) => Apply(scene);
    public static void Apply(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var cold = t.GetComponent<ColdHideInterior>();
            if (cold != null) { HidingPlace.Author(t.gameObject, cold.interior).enabled = cold.enabled; continue; }
            if (t.GetComponent<HidingPlace>() != null) continue;
            string original = SceneObjectNames.Original(t);
            if (original.StartsWith("WC cabin ") || original == "Toilet cabin - open interior" || original == "ToiletCabin")
                HidingPlace.Author(t.gameObject, StallInterior);
            else if (original.StartsWith("StorageRack") || t.name.StartsWith("Storage_Rack_")) AuthorRack(t);
        }
    }

    public static Bounds StallInterior => new Bounds(new Vector3(0f, 1.35f, 0f), new Vector3(2.49f, 2.7f, 3.24f));

    public static bool AuthorRack(Transform rack)
    {
        var mesh = rack.GetComponentInChildren<MeshFilter>();
        var colliders = rack.GetComponentsInChildren<Collider>();
        if (mesh == null || mesh.sharedMesh == null || colliders.Length == 0) return false;
        // The imported mesh can have a rotated/scaled child. Keep the volume upright in
        // the furniture's frame rather than treating the mesh's local Y as world height.
        Bounds raw = mesh.sharedMesh.bounds;
        Bounds bounds = new Bounds(rack.InverseTransformPoint(mesh.transform.TransformPoint(raw.min)), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            bounds.Encapsulate(rack.InverseTransformPoint(mesh.transform.TransformPoint(new Vector3(
                (corner & 1) == 0 ? raw.min.x : raw.max.x,
                (corner & 2) == 0 ? raw.min.y : raw.max.y,
                (corner & 4) == 0 ? raw.min.z : raw.max.z))));
        Vector3 floor = rack.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        // Probe from above a low plinth, below the first usable overhead board. The actual
        // underside sets the ceiling; using the whole rack's bounds would hide standing people.
        float ceiling = float.PositiveInfinity;
        Vector3 from = floor + Vector3.up * 0.45f;
        foreach (var collider in colliders)
        {
            if (!collider.enabled || collider.isTrigger) continue;
            if (collider.Raycast(new Ray(from, Vector3.up), out RaycastHit hit, 2f))
                ceiling = Mathf.Min(ceiling, hit.point.y);
        }
        if (float.IsInfinity(ceiling) || ceiling - floor.y < 0.6f) return false;
        float top = rack.InverseTransformPoint(new Vector3(floor.x, ceiling, floor.z)).y;
        var interior = new Bounds(new Vector3(bounds.center.x, (bounds.min.y + top) * 0.5f, bounds.center.z),
            new Vector3(bounds.size.x, top - bounds.min.y, bounds.size.z));
        HidingPlace.Author(rack.gameObject, interior, true);
        return true;
    }
}
