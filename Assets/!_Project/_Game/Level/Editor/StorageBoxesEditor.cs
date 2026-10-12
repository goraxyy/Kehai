using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The back rooms' boxes are built when the game starts (StorageBoxes), so an edited scene has
// none. This puts the same boxes in the same places while the scene is open in the editor, to
// look at only: they're never saved, can't be picked in the Scene view (a click lands on the
// rack), and are cleared before Play builds the working ones. Kehai/Storage turns them off.
[InitializeOnLoad]
public static class StorageBoxesPreview
{
    const string ShowKey = "Kehai.StorageBoxes.Preview";
    const string ShowMenu = "Kehai/Storage/Show Boxes in the Editor";
    static StorageBoxes.Parts parts;

    static bool Show
    {
        get => EditorPrefs.GetBool(ShowKey, true);
        set => EditorPrefs.SetBool(ShowKey, value);
    }

    static StorageBoxesPreview()
    {
        EditorApplication.delayCall += () => Refresh(false);
        EditorSceneManager.sceneOpened += (scene, mode) => Refresh(false);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Clear();
            else if (state == PlayModeStateChange.EnteredEditMode) Refresh(false);
        };
    }

    [MenuItem(ShowMenu, priority = 1)]
    static void Toggle()
    {
        Show = !Show;
        Refresh(true);
    }

    [MenuItem(ShowMenu, true)]
    static bool ToggleCheck()
    {
        Menu.SetChecked(ShowMenu, Show);
        return true;
    }

    [MenuItem("Kehai/Storage/Rebuild the Boxes in the Editor", priority = 2)]
    static void RebuildMenu() => Refresh(true);

    // Builds the preview in every open scene that has a back room and none yet; `rebuild` throws
    // away what's there first (after the racks have moved, say).
    public static void Refresh(bool rebuild)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (rebuild || !Show) Clear();
        if (!Show) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || Find(scene) != null) continue;
            parts ??= new StorageBoxes.Parts();
            GameObject root = StorageBoxes.Preview(scene, parts);
            if (root == null) continue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            SceneVisibilityManager.instance.DisablePicking(root, true);
        }
    }

    public static void Clear()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            GameObject root = Find(SceneManager.GetSceneAt(i));
            if (root != null) Object.DestroyImmediate(root);
        }
    }

    static GameObject Find(Scene scene)
    {
        if (!scene.isLoaded) return null;
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == StorageBoxes.PreviewName) return go;
        return null;
    }
}

// Kehai/Products/3: a box of every product on the floor of Models_Island, beside the products
// themselves, to look at and judge: one row per section in the order a customer walks the store,
// the section's name at the head of the row. Only the model and its materials, no collider, no
// physics, no scripts. The materials are kept as assets so the scene saves with them. The scene
// is saved too, unless it had unsaved changes of its own before: those are left for you to save.
public static class StorageBoxesShowcase
{
    public const string ShowcaseName = "Boxes_Showcase";
    const string MaterialsFolder = "Assets/!_Project/_Game/Items/Materials/Boxes";
    const float BoxPitch = 2f;        // along a row: the widest box (1.8) and a gap
    const float RowPitch = 1.6f;      // between rows: a box's depth and room to walk
    const float SignRoom = 2.8f;      // at the head of a row, for the section's name
    const float Clearance = 1f;       // from whatever already stands on the island

    [MenuItem("Kehai/Products/3. Lay Out a Box of Every Product on Models_Island")]
    static void ShowcaseMenu() => Debug.Log(Build());

    public static string Build()
    {
        var island = GameObject.Find("Models_Island");
        if (island == null) return "No Models_Island in the open scene.";
        bool clean = !island.scene.isDirty;

        Transform old = island.transform.Find(ShowcaseName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        // East of everything already on the island, from the south edge of its floor.
        Transform plane = island.transform.Find("Plane_test");
        Bounds floor = plane != null && plane.GetComponent<Renderer>() != null
            ? plane.GetComponent<Renderer>().bounds
            : new Bounds(island.transform.position, new Vector3(30f, 0f, 30f));
        float occupied = floor.min.x;
        foreach (Renderer r in island.GetComponentsInChildren<Renderer>())
            if (plane == null || r.transform != plane) occupied = Mathf.Max(occupied, r.bounds.max.x);

        var root = new GameObject(ShowcaseName);
        Undo.RegisterCreatedObjectUndo(root, "Box showcase");
        root.transform.SetParent(island.transform, false);

        ItemType[] order = ProductCatalog.StockSections;
        float headX = occupied + Clearance + SignRoom;
        float southZ = floor.min.z + 1.4f;
        var parts = new StorageBoxes.Parts(Keep);
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        int placed = 0, longest = 0;

        for (int row = 0; row < order.Length; row++)
        {
            ItemType section = order[row];
            IReadOnlyList<ProductDef> range = ProductCatalog.InSection(section);
            var group = new GameObject(section.ToString());
            group.transform.SetParent(root.transform, false);
            group.transform.position = new Vector3(headX, floor.max.y, southZ + row * RowPitch);
            FloorSign(group.transform, ProductCatalog.SectionName(section).ToUpperInvariant(), font);

            for (int i = 0; i < range.Count; i++)
            {
                ProductDef p = range[i];
                bool cold = p.Storage == StorageKind.Cold;
                Vector3 size = cold ? StorageBoxes.ColdBox : StorageBoxes.RackBox;
                // The cold room's boxes are labelled both sides, like there.
                GameObject box = StorageBoxes.Look(p, size, cold, cold, group.transform, parts);
                // A box's front faces +Z; turn it to face south, where you walk up from.
                box.transform.SetLocalPositionAndRotation(
                    new Vector3((i + 0.5f) * BoxPitch, size.y * 0.5f + 0.001f, 0f), Quaternion.Euler(0f, 180f, 0f));
                placed++;
            }
            longest = Mathf.Max(longest, range.Count);
        }

        string grew = plane != null ? ExtendFloorEast(plane, headX + longest * BoxPitch + 1f) : "";
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(island.scene);
        if (clean) EditorSceneManager.SaveScene(island.scene);
        return $"Box showcase: {placed} boxes on Models_Island in {order.Length} rows from x {headX:0.0}, " +
               $"z {southZ:0.0} to {southZ + (order.Length - 1) * RowPitch:0.0}{grew}; " +
               (clean ? "scene saved." : "the scene had unsaved changes already, so it's left for you to save.");
    }

    // Keeps a box material as an asset, updating the one already there.
    static Material Keep(Material made)
    {
        string path = $"{MaterialsFolder}/{made.name}.mat";
        var kept = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (kept != null)
        {
            kept.CopyPropertiesFromMaterial(made);
            EditorUtility.SetDirty(kept);
            Object.DestroyImmediate(made);
            return kept;
        }
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            AssetDatabase.CreateFolder("Assets/!_Project/_Game/Items/Materials", "Boxes");
        AssetDatabase.CreateAsset(made, path);
        return made;
    }

    // Stretches the test floor east until it reaches `eastX`, keeping its west edge.
    static string ExtendFloorEast(Transform plane, float eastX)
    {
        Bounds b = plane.GetComponent<Renderer>().bounds;
        if (b.max.x >= eastX) return "";
        Undo.RecordObject(plane, "Extend the island floor");
        float width = eastX - b.min.x;
        Vector3 s = plane.localScale;
        float perUnit = b.size.x / s.x;                 // a built-in plane is 10 units across
        plane.localScale = new Vector3(width / perUnit, s.y, s.z);
        Vector3 c = plane.position;
        plane.position = new Vector3(b.min.x + width * 0.5f, c.y, c.z);
        return $" (floor extended east to x {eastX:0.0})";
    }

    // The section's name on the floor at the head of its row, reading from the south.
    static void FloorSign(Transform row, string text, TMP_FontAsset font)
    {
        var go = new GameObject("Sign");
        go.transform.SetParent(row, false);
        go.transform.SetLocalPositionAndRotation(new Vector3(-0.3f, 0.003f, 0f), Quaternion.Euler(90f, 0f, 0f));
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 1.2f;
        tmp.alignment = TextAlignmentOptions.Right;
        tmp.color = new Color(0.55f, 0.08f, 0.06f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(SignRoom - 0.4f, 0.24f);
        tmp.rectTransform.pivot = new Vector2(1f, 0.5f);
    }
}
