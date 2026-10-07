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
// "Stock the Maze with Product Prefabs" goes further and puts the real thing on every shelf:
// each facing's placeholder box is replaced by its product's prefab, stood in its cell of the
// block the game draws round it, and the aisle signs are hung. That is what the game does by
// itself at load; baked, the editor shows the stocked shop too.
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

    const string SignMaterials = "Assets/!_Project/_Game/Items/Products/Materials";

    [MenuItem("Kehai/Store/Stock the Maze with Product Prefabs")]
    public static void StockWithProductsMenu() => Debug.Log(StockWithProducts());

    public static string StockWithProducts()
    {
        ApplyLayout();

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Stock the maze");
        int swapped = 0, already = 0, noModel = 0, hidden = 0;

        foreach (ShelfSlot slot in Object.FindObjectsByType<ShelfSlot>(FindObjectsInactive.Include))
        {
            GameObject prefab = ProductLook.Prefab(slot.productId);
            if (prefab == null) { noModel++; continue; }

            Item old = slot.storedItem;
            if (old != null && PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject) == prefab)
            {
                already++;
                continue;
            }

            Transform snap = slot.snapPoint != null ? slot.snapPoint : slot.transform;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, snap);
            Undo.RegisterCreatedObjectUndo(go, "Stock the maze");

            // Stood the way the game stands it: on the board, in its cell of the block.
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            float rest = mesh.bounds.extents.y - mesh.bounds.center.y;
            Vector3 cell = Backstock.Fit(mesh.bounds.size, slot.footprint).ItemCell;
            go.transform.localPosition = cell + Vector3.up * (rest - Item.SnapHeight);
            go.transform.localRotation = Quaternion.Euler(slot.snapRotationOffset);

            // Shelved stock is kinematic with its colliders off, like ShelfPrefabBuilder's.
            var item = go.GetComponent<Item>();
            item.isOnShelf = true;
            item.isCarried = false;
            var body = go.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.useGravity = false; }
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;

            Undo.RecordObject(slot, "Stock the maze");
            slot.storedItem = item;
            slot.isFilled = true;
            if (PrefabUtility.IsPartOfPrefabInstance(slot)) PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
            else EditorUtility.SetDirty(slot);

            if (old != null)
            {
                // A placeholder inside a shelf prefab is removed as an override; if this
                // editor won't remove it, it is switched off instead.
                GameObject placeholder = old.gameObject;
                try { Undo.DestroyObjectImmediate(placeholder); }
                catch (System.Exception) { }
                if (placeholder != null)
                {
                    Undo.RecordObject(placeholder, "Stock the maze");
                    placeholder.SetActive(false);
                    hidden++;
                }
            }
            swapped++;
        }

        int signs = AisleSigns.Build(SignMaterial("M_SignBoard", AisleSigns.BoardColour, 0.35f),
                                     SignMaterial("M_SignBadge", AisleSigns.BadgeColour, 0.4f),
                                     SignMaterial("M_SignWire", AisleSigns.WireColour, 0.5f),
                                     TMP_Settings.defaultFontAsset, japanese: false);
        var root = GameObject.Find(AisleSigns.RootName);
        if (root != null) Undo.RegisterCreatedObjectUndo(root, "Stock the maze");

        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        return $"Stocked the maze: {swapped} facings now hold their product's prefab ({already} already did, " +
               $"{noModel} without a model{(hidden > 0 ? $", {hidden} placeholders switched off rather than removed" : "")}); " +
               $"{signs} aisle signs hung.";
    }

    // The signs' materials as assets, so the scene can keep them.
    static Material SignMaterial(string name, Color color, float smoothness)
    {
        string path = $"{SignMaterials}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            if (!AssetDatabase.IsValidFolder(SignMaterials)) return null;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }
}
