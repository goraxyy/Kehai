using Kehai.Karen;
using UnityEngine;
using UnityEngine.SceneManagement;

// Repeatable support for scenes built before hiding volumes were part of the recipes.
// Only known hiding furniture qualifies; ordinary shop shelves never get this override.
//
// What hides you (0.5.1, item 1; the scene-checked list is Kehai051HidingInTheStoreTests):
//   - ColdHideInterior carriers, each with its own interior: the cold hiding cabinets, the
//     staff lockers (a 1.08 m opening built for the 1 m-wide player) and the WC's empty
//     hiding cupboard. The stocked cleaning cupboard is full of shelves and is not one.
//   - The WC toilet cabins and the staff room's changing cabins, by name: standing inside.
//   - The stockroom's storage racks, by name: crouched under the lowest board.
// Names are matched on the object's original name and its organised display name both
// (`Changing cabin 01` and `Changing_Cabin_01`), so a scene before or after the organiser works.
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
            if (Is(t, "wccabin", "toiletcabin", "toiletcabinopeninterior")) HidingPlace.Author(t.gameObject, StallInterior);
            else if (Is(t, "changingcabin")) HidingPlace.Author(t.gameObject, ChangingCabinInterior);
            else if (IsRack(t) && !HasRackChild(t)) AuthorRack(t);
        }
    }

    // PublicInteriorModels.Cabin: 2.65 x 3.4 m gross, 2.49 x 3.24 m inside, 2.7 m high.
    public static Bounds StallInterior => new Bounds(new Vector3(0f, 1.35f, 0f), new Vector3(2.49f, 2.7f, 3.24f));
    // StaffRoomModels.Cabin: sides at x ±1.26 and front/back at z ±1.31 (8 cm boards), 2.6 m high.
    public static Bounds ChangingCabinInterior => new Bounds(new Vector3(0f, 1.3f, 0f), new Vector3(2.44f, 2.6f, 2.54f));

    // The organiser also names the racks' container "Storage_Rack_01" (it starts "storagerack"),
    // so a rack is one with no rack inside it.
    public static bool IsRack(Transform t) =>
        SceneObjectNames.Original(t).StartsWith("StorageRack", System.StringComparison.Ordinal) ||
        t.name.StartsWith("Storage_Rack_", System.StringComparison.Ordinal);
    static bool HasRackChild(Transform t)
    {
        foreach (Transform child in t) if (IsRack(child)) return true;
        return false;
    }

    static bool Is(Transform t, params string[] keys)
    {
        string original = Key(SceneObjectNames.Original(t)), display = Key(t.name);
        foreach (string key in keys) if (original == key || display == key) return true;
        return false;
    }

    // Lower-case letters and digits, without a trailing number: "WC_Cabin_01" -> "wccabin".
    public static string Key(string name)
    {
        int end = name.Length;
        while (end > 0 && (char.IsDigit(name[end - 1]) || !char.IsLetterOrDigit(name[end - 1]))) end--;
        var key = new System.Text.StringBuilder(end);
        for (int i = 0; i < end; i++) if (char.IsLetterOrDigit(name[i])) key.Append(char.ToLowerInvariant(name[i]));
        return key.ToString();
    }

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
