using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authoring front end for StoreLayout, which is where the plan itself lives.
//
// The game stocks the shop at load, so neither of these is needed to play. They are here
// for working on the layout: "Report" prints what the plan makes of the scene as it stands,
// and "Apply" bakes it in so the Inspector shows each bay's section and each facing's
// product instead of the placeholder cereal they were built with.
//
// Baking is optional and costs something — every facing becomes a prefab override in a
// scene file that is already a megabyte — so reach for the report first.
//
// "Stock the Maze with Product Prefabs" goes further and bakes the stocked shop itself: every
// board cut into ShelfGrid's slots with a product prefab in each, the aisle signs and the lamps.
// That is what the game does by itself at load; baked, the editor shows it too.
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

        Debug.Log($"Baked the store layout into the scene: {facings} facings.\n\n" +
                  StoreLayout.Describe());
    }

    const string LevelMaterials = "Assets/!_Project/_Game/Level/Materials";

    [MenuItem("Kehai/Store/Stock the Maze with Product Prefabs")]
    public static void StockWithProductsMenu() => Debug.Log(StockWithProducts());

    // Bakes the whole stocked shop into the scene: every bay's boards cut into ShelfGrid's
    // slots, each slot holding its product's prefab, then the aisle signs and the lamps. The
    // game builds all of this by itself at load when the scene doesn't have it; baked, the
    // editor shows it. Rerunning replaces the last bake. It is not undoable (tens of thousands
    // of objects): save before, and reopen the scene to throw it away.
    public static string StockWithProducts()
    {
        int bays = 0, slots = 0, items = 0, cleared = 0;
        foreach (ShelfUnit unit in Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include))
        {
            Transform bay = unit.transform;
            StoreLayout.Zone zone = StoreLayout.ZoneAt(bay.position);

            // The prefab's own six-to-a-board slots: drop the product prefabs a previous bake
            // put on them, then switch them off (one override per bay).
            Transform old = bay.Find("Slots");
            if (old != null)
            {
                foreach (Item item in old.GetComponentsInChildren<Item>(true))
                    if (PrefabUtility.IsAddedGameObjectOverride(item.gameObject)) { Object.DestroyImmediate(item.gameObject); cleared++; }
                if (old.gameObject.activeSelf)
                {
                    old.gameObject.SetActive(false);
                    if (PrefabUtility.IsPartOfPrefabInstance(old.gameObject))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(old.gameObject);
                }
            }

            Transform grid = bay.Find(ShelfGrid.RootName);
            if (grid != null) Object.DestroyImmediate(grid.gameObject);

            int seed = Mathf.Abs(Mathf.RoundToInt(bay.position.x) * 73856093 ^ Mathf.RoundToInt(bay.position.z) * 19349663);
            bool endCap = Planogram.IsEndCap(bay);
            foreach (ShelfGrid.Facing facing in ShelfGrid.Facings(bay))
            {
                string id = Planogram.ProductFor(zone.Section, seed, facing.Back, Planogram.BoardAt(facing.Height), endCap);
                ProductDef product = ProductCatalog.Get(id);
                ItemType section = product != null ? product.Category : zone.Section;
                Transform group = ShelfGrid.Build(bay, facing, product, section,
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                foreach (ShelfSlot slot in group.GetComponentsInChildren<ShelfSlot>(true))
                {
                    slots++;
                    if (slot.storedItem != null) items++;
                }
            }
            bays++;
        }

        // Ids and each bay's section, through the usual pass.
        ApplyLayout();

        int signs = AisleSigns.Build(Asset("M_SignBoard", AisleSigns.BoardColour, 0.2f, false),
                                     Asset("M_SignBadge", AisleSigns.BadgeColour, 0.2f, false),
                                     Asset("M_SignWire", AisleSigns.WireColour, 0.3f, false),
                                     TMP_Settings.defaultFontAsset, japanese: false);
        CeilingLamps lamps = CeilingLamps.Build(Asset("M_LampBox", new Color(0.015f, 0.015f, 0.018f), 0.25f, false),
                                                Asset("M_SignWire", AisleSigns.WireColour, 0.3f, false),
                                                Asset("M_LampPanelOn", new Color(1f, 0.98f, 0.92f), 0f, true),
                                                Asset("M_LampPanelOff", new Color(0.12f, 0.12f, 0.12f), 0.4f, false));

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return $"Stocked the maze: {bays} bays, {slots} slots, {items} items" +
               (cleared > 0 ? $" ({cleared} products from the last bake cleared)" : "") +
               $"; {signs} aisle signs and {lamps.lamps.Count} lamps hung. Not saved yet.";
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
