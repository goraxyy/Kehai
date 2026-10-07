using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Regenerates the whole shelf pipeline:
//   Shelves/Models/  3-platform (2 for "short") model prefabs — geometry only
//   Shelves/Ready/   variants of those, with a ShelfUnit and a ShelfPoint for customers. Their
//                    slots aren't in the prefab: StoreLayout cuts the boards into ShelfGrid's
//                    slots at load, as data (ShelfSlot).
// Menu items are under Kehai/Shelves so this can be re-run after tweaking the constants.
public static class ShelfPrefabBuilder
{
    // Shelf models live in Shelves/, the corner/connector pieces in Pillars/ — both carry platforms.
    const string PrefabsRoot = "Assets/!_Project/_Game/Level/Prefabs";
    const string ShelvesFolder = PrefabsRoot + "/Shelves";
    const string ModelsFolder = ShelvesFolder + "/Models";
    const string ReadyFolder = ShelvesFolder + "/Ready";
    const string OutlineMaterialPath = "Assets/!_Project/_Game/Characters/Materials/CustomerOutline.mat";

    // Shelf heights: a full shelf's back wall spans 0..2, a "short" one 0..1.25.
    const float TallTop = 2f;
    const float ShortTop = 1.25f;
    const int TallPlatforms = 3;
    const int ShortPlatforms = 2;

    // Every shelf in the pack shares this bottom platform height. Levels are derived from it
    // rather than from each model's own lowest platform, so that pieces whose bottom bay is a
    // solid base block (the "b" pillars) still line up with the runs they connect to.
    const float StandardBottom = 0.225f;

    const float ThinPlatformMax = 0.1f;   // thicker "polka" pieces are structural bases, not shelves

    const float ShelfPointClearance = 0.9f; // how far in front of the shelf a customer stands

    [MenuItem("Kehai/Shelves/1. Build Model Prefabs")]
    public static void BuildModels()
    {
        EnsureFolder(ModelsFolder);
        var log = new StringBuilder("Model prefabs:\n");

        foreach (string path in SourcePrefabPaths())
        {
            string sourceName = Path.GetFileNameWithoutExtension(path);
            bool isShort = IsShort(sourceName);
            string outPath = $"{ModelsFolder}/{ModelName(sourceName)}.prefab";

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(path));
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = ModelName(sourceName);

            int removed = Restructure(root, isShort);
            StripRuntimeMaterialController(root);
            MarkStaticGeometry(root);
            MarkNonWalkable(root);

            PrefabUtility.SaveAsPrefabAsset(root, outPath);
            Object.DestroyImmediate(root);

            log.AppendLine($"  {sourceName} -> {ModelName(sourceName)} " +
                           $"({(isShort ? ShortPlatforms : TallPlatforms)} platforms, {removed} removed)");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    [MenuItem("Kehai/Shelves/2. Build Ready Prefabs")]
    public static void BuildReady()
    {
        EnsureFolder(ReadyFolder);
        var log = new StringBuilder("Ready prefabs:\n");

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ModelsFolder }))
        {
            string modelPath = AssetDatabase.GUIDToAssetPath(guid);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            string readyName = Path.GetFileNameWithoutExtension(modelPath) + "_Ready";

            // Instantiating the model and saving it back out yields a Prefab Variant,
            // so later edits to the model still flow through to the stocked version.
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = readyName;

            AddShelfPoint(root);
            AddRestockHighlight(root);

            PrefabUtility.SaveAsPrefabAsset(root, $"{ReadyFolder}/{readyName}.prefab");
            Object.DestroyImmediate(root);
            log.AppendLine($"  {readyName}");
        }

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    // Models_Island is the model showcase, so it gets plain models; everywhere else in the
    // level gets the stocked "Ready" variants.
    [MenuItem("Kehai/Shelves/3. Swap Scene Shelves")]
    public static void SwapSceneShelves()
    {
        const string ShowcaseRoot = "Models_Island";

        var targets = new List<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (!t.Cast<Transform>().Any(c => c.name.ToLower().Contains("polka"))) continue;
            if (PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == null) continue;
            targets.Add(t);
        }

        int swapped = 0, skipped = 0, missing = 0;
        var log = new StringBuilder();

        foreach (Transform old in targets)
        {
            if (old == null) continue;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject);
            string sourceName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
            string baseName = BaseModelName(sourceName);

            bool showcase = IsUnder(old, ShowcaseRoot);
            string wantedPath = showcase
                ? $"{ModelsFolder}/{baseName}.prefab"
                : $"{ReadyFolder}/{baseName}_Ready.prefab";

            if (AssetDatabase.GetAssetPath(source) == wantedPath) { skipped++; continue; }

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(wantedPath);
            if (asset == null) { missing++; log.AppendLine($"  missing target: {wantedPath}"); continue; }

            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(asset, old.parent);
            replacement.transform.SetPositionAndRotation(old.position, old.rotation);
            replacement.transform.localScale = old.localScale;
            replacement.transform.SetSiblingIndex(old.GetSiblingIndex());
            replacement.gameObject.SetActive(old.gameObject.activeSelf);
            Undo.RegisterCreatedObjectUndo(replacement, "Swap shelf");

            Object.DestroyImmediate(old.gameObject);
            swapped++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        log.Insert(0, $"Shelf swap: {swapped} replaced, {skipped} already current, {missing} missing targets\n");
        Debug.Log(log.ToString());
    }

    static bool IsUnder(Transform t, string ancestorName)
    {
        for (Transform c = t; c != null; c = c.parent)
            if (c.name == ancestorName) return true;
        return false;
    }

    // Resolves any shelf prefab name (original, model, or ready) to its model base name.
    static string BaseModelName(string sourceName)
    {
        if (sourceName.EndsWith("_Ready"))
            sourceName = sourceName.Substring(0, sourceName.Length - "_Ready".Length);

        if (sourceName.Contains("_3_grey") || sourceName.Contains("_2_grey"))
            return sourceName;

        return ModelName(sourceName);
    }

    // ---------------------------------------------------------------- geometry

    // Drops surplus platforms and spreads the rest evenly over the shelf's height,
    // keeping the bottom platform exactly where it was.
    static int Restructure(GameObject root, bool isShort)
    {
        float top = isShort ? ShortTop : TallTop;
        int target = isShort ? ShortPlatforms : TallPlatforms;

        List<Transform> thin = root.transform.Cast<Transform>()
            .Where(t => t.name.ToLower().Contains("polka") && t.localScale.y < ThinPlatformMax)
            .ToList();
        if (thin.Count == 0) return 0;

        List<float> oldLevels = thin.Select(t => Mathf.Round(t.localPosition.y * 1000f) / 1000f)
                                    .Distinct().OrderBy(y => y).ToList();

        float bottom = Mathf.Abs(oldLevels[0] - StandardBottom) < 0.05f ? oldLevels[0] : StandardBottom;
        float step = (top - bottom) / target;
        var newLevels = new List<float>();
        for (int i = 0; i < target; i++) newLevels.Add(bottom + i * step);

        // Map each existing level onto a new one. Models that already have fewer platforms
        // than the target (the "b" pieces, whose bottom bay is a solid base block) are aligned
        // to the TOP levels, so their shelves still line up with the runs they connect to.
        var mapping = new Dictionary<float, float>();
        var removed = new List<float>();
        if (oldLevels.Count >= target)
        {
            for (int i = 0; i < oldLevels.Count; i++)
            {
                if (i < target) mapping[oldLevels[i]] = newLevels[i];
                else removed.Add(oldLevels[i]);
            }
        }
        else
        {
            int offset = target - oldLevels.Count;
            for (int i = 0; i < oldLevels.Count; i++)
                mapping[oldLevels[i]] = newLevels[offset + i];
        }

        // Platforms and their pricetags move together; anything not sitting at a
        // platform height (walls, structural bases) is left alone.
        int deleted = 0;
        foreach (Transform child in root.transform.Cast<Transform>().ToList())
        {
            if (child.name == "Wall") continue;
            if (child.name.ToLower().Contains("polka") && child.localScale.y >= ThinPlatformMax) continue;

            float y = child.localPosition.y;
            float nearest = 0f, bestDistance = 0.12f;
            bool found = false;
            foreach (float level in oldLevels)
            {
                float d = Mathf.Abs(y - level);
                if (d < bestDistance) { bestDistance = d; nearest = level; found = true; }
            }
            if (!found) continue;

            if (removed.Contains(nearest))
            {
                Object.DestroyImmediate(child.gameObject);
                deleted++;
            }
            else
            {
                Vector3 p = child.localPosition;
                child.localPosition = new Vector3(p.x, p.y - nearest + mapping[nearest], p.z);
            }
        }

        return deleted;
    }

    // A spot in front of the shelf for customers to stand while browsing.
    static void AddShelfPoint(GameObject root)
    {
        var platforms = Platforms(root).ToList();
        if (platforms.Count == 0) return;

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (Transform p in platforms)
        {
            minX = Mathf.Min(minX, p.localPosition.x - p.localScale.x * 0.5f);
            maxX = Mathf.Max(maxX, p.localPosition.x + p.localScale.x * 0.5f);
            minZ = Mathf.Min(minZ, p.localPosition.z - p.localScale.z * 0.5f);
            maxZ = Mathf.Max(maxZ, p.localPosition.z + p.localScale.z * 0.5f);
        }

        var point = new GameObject("ShelfPoint");
        point.transform.SetParent(root.transform, false);
        point.transform.localPosition = new Vector3((minX + maxX) * 0.5f, 0f, minZ - ShelfPointClearance);
    }

    // Lets the shelf glow yellow while any of its slots are empty.
    static void AddRestockHighlight(GameObject root)
    {
        var outline = root.GetComponent<OutlineHighlight>() ?? root.AddComponent<OutlineHighlight>();
        outline.outlineMaterial = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
        outline.outlineColor = new Color(1f, 0.85f, 0.1f, 1f);
        outline.outlineWidth = 0.05f;
        // Shelf boards are 2cm thin, so a per-mesh hull balloons around them.
        outline.shape = OutlineHighlight.OutlineShape.BoundingBox;

        if (root.GetComponent<ShelfUnit>() == null)
            root.AddComponent<ShelfUnit>();
    }

    static IEnumerable<Transform> Platforms(GameObject root)
    {
        // Only the model's own platforms, not the structural bases.
        foreach (Transform child in root.transform)
            if (child.name.ToLower().Contains("polka") && child.localScale.y < ThinPlatformMax)
                yield return child;
    }

    // ---------------------------------------------------------------- helpers

    static void StripRuntimeMaterialController(GameObject root)
    {
        var controller = root.GetComponent<ParentMaterialController>();
        if (controller == null) return;

        if (controller.sharedMaterial != null)
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                renderer.sharedMaterial = controller.sharedMaterial;

        Object.DestroyImmediate(controller);
    }

    static void MarkStaticGeometry(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }
    }

    static void MarkNonWalkable(GameObject root)
    {
        var modifier = root.GetComponent<NavMeshModifier>() ?? root.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = 1; // Not Walkable
    }

    public static IEnumerable<string> SourcePrefabPaths()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(ModelsFolder) || path.StartsWith(ReadyFolder)) continue;

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Contains("_3_grey") || name.Contains("_2_grey")) continue; // previously generated

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool hasPlatform = go.transform.Cast<Transform>()
                .Any(t => t.name.ToLower().Contains("polka") && t.localScale.y < ThinPlatformMax);
            if (hasPlatform) yield return path;
        }
    }

    public static bool IsShort(string name) => name.ToLower().Contains("short");

    public static string ModelName(string sourceName)
    {
        string suffix = IsShort(sourceName) ? "_2_grey" : "_3_grey";
        string baseName = sourceName.EndsWith("_grey")
            ? sourceName.Substring(0, sourceName.Length - "_grey".Length)
            : sourceName;

        // Keep the two names that were specified explicitly.
        if (baseName == "Shelftwoside") baseName = "ShelfTwoside";
        else if (baseName == "Shelfoneside") baseName = "ShelfOneside";
        else if (baseName == "Shelftwoside_short") baseName = "ShelfTwoside_short";

        return baseName + suffix;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
