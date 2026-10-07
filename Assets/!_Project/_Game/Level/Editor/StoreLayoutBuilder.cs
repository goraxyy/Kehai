using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authoring front end for StoreLayout, which is where the plan itself lives.
//
// The game stocks the shop at load, so none of these is needed to play:
//   - "Report Layout" prints what the plan makes of the scene as it stands;
//   - "Apply Layout" writes each bay's aisle into the scene, so the Inspector shows it (every
//     bay becomes a prefab override, so reach for the report first);
//   - "Hang Signs and Lamps" bakes the aisle signs and the ceiling lamps in, so the editor
//     shows them too (the game hangs its own at load either way);
//   - "Migrate to Data Shelves" clears out what the shop held before stock was data.
//
// The stock itself isn't baked into anything: ShelfDrawer draws it in the editor from the
// planogram, as the game does.
public static class StoreLayoutBuilder
{
    [MenuItem("Kehai/Store/Report Layout")]
    public static void ReportLayout()
    {
        Debug.Log(StoreLayout.Describe());
    }

    [MenuItem("Kehai/Store/Apply Layout")]
    public static void ApplyLayout()
    {
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply store layout");

        int facings = StoreLayout.ApplyToScene(
            beforeWrite: o => Undo.RecordObject(o, "Apply store layout"),
            afterWrite: o =>
            {
                // Bays are prefab instances, so a changed field only sticks once it has
                // been registered as an override.
                if (PrefabUtility.IsPartOfPrefabInstance(o))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(o);
                else
                    EditorUtility.SetDirty(o);
            });

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        Debug.Log($"Wrote the store layout into the scene ({facings} slots).\n\n" +
                  StoreLayout.Describe());
    }

    const string LevelMaterials = "Assets/!_Project/_Game/Level/Materials";

    [MenuItem("Kehai/Store/Hang Signs and Lamps")]
    public static void HangSignsAndLampsMenu() => Debug.Log(HangSignsAndLamps());

    // The aisle signs over the bays and a lamp over every ceiling light, with their materials
    // as assets so the scene can keep them. English only: the Japanese needs the computer's
    // fonts, which the game picks up at load. Rerunning replaces the last ones.
    public static string HangSignsAndLamps()
    {
        int signs = AisleSigns.Build(Asset("M_SignBoard", AisleSigns.BoardColour, 0.2f, false),
                                     Asset("M_SignBadge", AisleSigns.BadgeColour, 0.2f, false),
                                     Asset("M_SignWire", AisleSigns.WireColour, 0.3f, false),
                                     TMP_Settings.defaultFontAsset, japanese: false);
        CeilingLamps lamps = CeilingLamps.Build(Asset("M_LampBox", new Color(0.015f, 0.015f, 0.018f), 0.25f, false),
                                                Asset("M_SignWire", AisleSigns.WireColour, 0.3f, false),
                                                Asset("M_LampPanelOn", new Color(1f, 0.98f, 0.92f), 0f, true),
                                                Asset("M_LampPanelOff", new Color(0.12f, 0.12f, 0.12f), 0.4f, false));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return $"Hung {signs} aisle signs and {lamps.lamps.Count} lamps. Not saved yet.";
    }

    // ---------------------------------------------------------------- migration

    [MenuItem("Kehai/Store/Migrate to Data Shelves")]
    public static void MigrateMenu() => Debug.Log(MigrateToDataShelves());

    // Stock used to be GameObjects: a slot with a trigger for every place an item stood, and
    // the item on it another. This clears what that left in the open scene and the prefabs:
    //   - each bay's baked GridSlots (the old Kehai/Store/Stock the Maze);
    //   - the shelf prefabs' own Slots, six to a board, which nothing has used since the grid;
    //   - the till counter's slot, which becomes a CounterFacing standing where its item stood;
    // and puts in the drawer that shows the stock in the editor. Save the scene after.
    public static string MigrateToDataShelves()
    {
        int baked = 0, prefabs = 0, counters = 0, stripped = 0;
        var units = Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include);
        var prefabPaths = new HashSet<string>();
        foreach (ShelfUnit unit in units)
        {
            Transform grid = unit.transform.Find(ShelfGrid.LegacyRootName);
            if (grid != null)
            {
                Object.DestroyImmediate(grid.gameObject);
                baked++;
            }
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(unit.gameObject);
            if (!string.IsNullOrEmpty(path)) prefabPaths.Add(path);
        }

        // The till counter's slot: a dead component now (ShelfSlot isn't a component any more,
        // and Unity doesn't count that as a missing script), with the SnapPoint its item stood
        // on. The facing stands on the counter's top.
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null || t.GetComponentInParent<ShelfUnit>(true) != null) continue;
            Transform snap = t.Find("SnapPoint");
            if (snap == null || System.Array.IndexOf(t.GetComponents<Component>(), null) < 0) continue;

            var marker = new GameObject("Counter Facing");
            marker.transform.SetPositionAndRotation(snap.position - Vector3.up * Item.SnapHeight, t.rotation);
            if (t.parent != null) marker.transform.SetParent(t.parent, true);
            marker.AddComponent<CounterFacing>();
            Undo.RegisterCreatedObjectUndo(marker, "Migrate to data shelves");

            // Unity can't strip that dead component from the old slot's prefab, so the instance
            // goes rather than carrying it.
            if (PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject) is GameObject own && own != t.gameObject)
                t.gameObject.SetActive(false);
            else
                Undo.DestroyObjectImmediate(t.gameObject);
            counters++;
        }

        foreach (string path in prefabPaths)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            Transform old = root.transform.Find("Slots");
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
                changed = true;
            }
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                stripped += n;
                changed |= n > 0;
            }
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                prefabs++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Overrides on what the prefabs no longer have.
        foreach (ShelfUnit unit in units)
        {
            GameObject instance = PrefabUtility.GetOutermostPrefabInstanceRoot(unit.gameObject);
            if (instance != null)
                PrefabUtility.RemoveUnusedOverrides(new[] { instance }, InteractionMode.AutomatedAction);
        }

        ShelfDrawer drawer = ShelfDrawer.Ensure();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return $"Migrated to data shelves: {baked} baked bays cleared, {prefabs} prefabs cleaned " +
               $"({stripped} missing scripts stripped), {counters} counter facing{(counters == 1 ? "" : "s")} made, " +
               $"drawer '{drawer.name}' in the scene. Save the scene.";
    }

    // A material as an asset, so the scene can keep it. `unlit` for the lamps' glowing panels.
    static Material Asset(string name, Color color, float smoothness, bool unlit)
    {
        string path = $"{LevelMaterials}/{name}.mat";
        Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder(LevelMaterials)) AssetDatabase.CreateFolder("Assets/!_Project/_Game/Level", "Materials");
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader) m.shader = shader;
        m.SetColor("_BaseColor", color);
        if (!unlit) m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }
}
