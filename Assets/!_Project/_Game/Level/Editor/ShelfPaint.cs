using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai/Shelves/4: every shelf one plain grey. The shelf pack came in the grid prototype
// materials, white bays and grey pillars; this swaps both for one grey material on the shelf
// prefabs (the models first, so the ready variants and the scene's bays follow them), and on
// whatever in the open scene still has them: a bay with its own override, or the shelf models
// standing on Models_Island. The price strips go grey too, until the stock puts each one's
// price on it (PriceTags). The scene is saved only if it had no unsaved changes of its own.
public static class ShelfPaint
{
    const string ShelvesFolder = "Assets/!_Project/_Game/Level/Prefabs/Shelves";
    const string MaterialPath = "Assets/!_Project/_Game/Level/Materials/M_ShelfGrey.mat";
    static readonly string[] Replaced = { "White_Prototype", "Gray_Prototype" };
    public static readonly Color Grey = new Color(0.5f, 0.5f, 0.52f);

    [MenuItem("Kehai/Shelves/4. Paint Shelves Grey")]
    static void PaintMenu() => Debug.Log(Paint());

    public static string Paint()
    {
        using var hierarchy = SceneHierarchyOrganizer.BuilderScope();
        Material grey = GreyMaterial();
        bool clean = !EditorSceneManager.GetActiveScene().isDirty;

        // The models first: the ready variants and the scene's bays take their materials from them.
        List<string> paths = AssetDatabase.FindAssets("t:Prefab", new[] { ShelvesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p.Contains("/Models/") ? 0 : 1).ThenBy(p => p)
            .ToList();
        int prefabs = 0, parts = 0;
        foreach (string path in paths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            int n = PaintAll(root, grey, false);
            if (n > 0)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                prefabs++;
                parts += n;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        int inScene = 0;
        foreach (ShelfUnit unit in Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include))
            inScene += PaintAll(unit.gameObject, grey, true);
        GameObject island = GameObject.Find("Models_Island");
        if (island != null)
            foreach (Transform t in island.transform)
                if (t.name.StartsWith("Shelf")) inScene += PaintAll(t.gameObject, grey, true);

        AssetDatabase.SaveAssets();
        string saved = "";
        if (inScene > 0)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            if (clean) EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            saved = clean ? "; scene saved" : "; the scene had unsaved changes already, so it's left for you to save";
        }
        return $"Shelves painted grey: {parts} parts in {prefabs} prefabs, {inScene} more in the scene{saved}.";
    }

    // Swaps the prototype materials under `root` for the grey; returns how many renderers changed.
    static int PaintAll(GameObject root, Material grey, bool inScene)
    {
        int changed = 0;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool any = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && Replaced.Contains(mats[i].name)) { mats[i] = grey; any = true; }
            if (!any) continue;
            if (inScene) Undo.RecordObject(r, "Paint shelves grey");
            r.sharedMaterials = mats;
            if (inScene && PrefabUtility.IsPartOfPrefabInstance(r)) PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            changed++;
        }
        return changed;
    }

    static Material GreyMaterial()
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (m == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, MaterialPath);
        }
        m.color = Grey;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Grey);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.25f);
        EditorUtility.SetDirty(m);
        return m;
    }
}
