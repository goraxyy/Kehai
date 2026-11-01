using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// This pass changes only names, parents and sibling order. Original object references and
// builder ownership are retained in SceneObjectNames, including inactive prototypes.
public static class SceneHierarchyOrganizer
{
    const string UndoLabel = "Organize supermarket hierarchy";
    static int builderDepth;

    [MenuItem("Kehai/Scene/Organize by Area and Name Objects")]
    public static void Organize()
    {
        RequireEditMode();
        Scene scene = SceneManager.GetActiveScene();
        if (SceneObjectNames.Find("Layout_15x15") == null)
            throw new InvalidOperationException("Open the supermarket scene first.");
        string backup = Backup(scene);
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);
        try
        {
            var map = SceneObjectNames.ForScene(scene);
            if (map == null)
            {
                var go = new GameObject("Scene_Name_Index");
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                map = Undo.AddComponent<SceneObjectNames>(go);
            }
            var before = Snapshot.Take(scene, map);
            Restore(map);
            Apply(map);
            before.RequireUnchanged();
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log($"Hierarchy organized: {map.entries.Count} objects; transforms, active states, components, prefab links and serialized references preserved. Backup: {backup}");
        }
        catch
        {
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(undoGroup);
            SceneObjectNames.Invalidate();
            throw;
        }
    }

    [MenuItem("Kehai/Scene/Restore Original Names and Hierarchy")]
    public static void RestoreMenu()
    {
        RequireEditMode();
        var scene = SceneManager.GetActiveScene();
        var map = SceneObjectNames.ForScene(scene);
        if (map == null) return;
        Backup(scene);
        Restore(map);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // Existing builders own parts of the legacy hierarchy. Restore that view for the
    // duration of an explicit builder command, then organize its result, even on failure.
    // There is no automatic hierarchy watcher and no runtime reparenting.
    public static IDisposable BuilderScope() => new BuilderView();

    sealed class BuilderView : IDisposable
    {
        readonly SceneObjectNames map;
        bool disposed;
        public BuilderView()
        {
            if (Application.isPlaying) return;
            map = builderDepth == 0 ? SceneObjectNames.ForScene(SceneManager.GetActiveScene()) : null;
            builderDepth++;
            try
            {
                if (map != null && map.organized) Restore(map);
                else map = null;
            }
            catch { builderDepth--; throw; }
        }
        public void Dispose()
        {
            if (disposed || Application.isPlaying) return;
            disposed = true;
            builderDepth--;
            if (map != null)
            {
                Apply(map);
                EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            }
        }
    }

    static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Organize only in edit mode after compilation finishes.");
    }

    static string Backup(Scene scene)
    {
        const string folder = "Library/Kehai/SceneBackups";
        Directory.CreateDirectory(folder);
        string path = folder + "/Before-hierarchy-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
        if (!EditorSceneManager.SaveScene(scene, path, true))
            throw new IOException("Scene backup failed; nothing was reorganized.");
        return path;
    }

    static bool Persistent(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if ((p.gameObject.hideFlags & HideFlags.DontSave) != 0) return false;
        return true;
    }

    static List<Transform> Objects(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(Persistent).ToList();

    static void Restore(SceneObjectNames map)
    {
        if (!map.organized) return;
        Undo.RecordObject(map, UndoLabel);
        // Capture order is parent-before-child. Assemblies remain intact throughout.
        foreach (var entry in map.entries)
        {
            if (entry.target == null) continue;
            Move(entry.target, entry.originalParent);
            Rename(entry.target, entry.originalName);
        }
        foreach (var entry in map.entries)
            if (entry.target != null)
            {
                Undo.RecordObject(entry.target, UndoLabel);
                entry.target.SetSiblingIndex(entry.originalSibling);
            }
        map.organized = false;
        SceneObjectNames.Invalidate();
    }

    static void Apply(SceneObjectNames map)
    {
        var folders = new HashSet<Transform>(map.folders.Where(t => t != null));
        var content = Objects(map.gameObject.scene).Where(t => t != map.transform && !folders.Contains(t)).ToList();
        Undo.RecordObject(map, UndoLabel);
        map.entries = content.Select(t => new SceneObjectNames.Entry
        {
            target = t, originalName = t.name, originalParent = t.parent,
            originalSibling = t.GetSiblingIndex(), originalPath = PathOf(t)
        }).ToList();
        SceneObjectNames.Invalidate();
        var layout = new Layout(map);
        layout.Arrange();
        foreach (var siblings in content.Where(t => t != null).GroupBy(t => t.parent))
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in siblings)
            {
                string stem = DisplayName(t);
                // Existing mechanic/animation child paths inside animated assemblies are
                // deliberately left untouched. None are currently present in SampleScene.
                if (t.GetComponentInParent<Animator>() != null || t.GetComponentInParent<Animation>() != null) continue;
                string name = stem;
                if (stem != "Models_Island" && stem != "Player")
                {
                    int next = counts.TryGetValue(stem, out int count) ? count + 1 : 1;
                    do { name = stem + "_" + next.ToString("00"); next++; } while (!used.Add(name));
                    counts[stem] = next - 1;
                }
                else used.Add(name);
                Rename(t, name);
            }
        }
        Move(map.transform, layout.Folder("Game_Systems"));
        var originalParents = new HashSet<Transform>(map.entries.Select(e => e.originalParent));
        foreach (var folder in map.folders.Where(t => t != null).OrderByDescending(t => PathOf(t).Count(c => c == '/')).ToArray())
        {
            if (folder.childCount != 0 || originalParents.Contains(folder)) continue;
            if (folder.parent != null && (folder.parent.name == "Supermarket" || folder.parent.name == "Exterior")) continue;
            map.folders.Remove(folder);
            Undo.DestroyObjectImmediate(folder.gameObject);
        }
        // Sorting is restricted to organizational folders, never furniture/door children.
        foreach (var folder in map.folders.Where(t => t != null))
        {
            var children = folder.Cast<Transform>().OrderBy(t => t.name, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < children.Length; i++)
            { Undo.RecordObject(children[i], UndoLabel); children[i].SetSiblingIndex(i); }
        }
        map.organized = true;
        SceneObjectNames.Invalidate();
        EditorUtility.SetDirty(map);
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static void Move(Transform t, Transform parent)
    {
        if (t == null || t.parent == parent) return;
        if (PrefabUtility.IsPartOfPrefabInstance(t) && !PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
            throw new InvalidOperationException("Cannot move a prefab's internal part: " + PathOf(t));
        Undo.SetTransformParent(t, parent, UndoLabel);
    }

    static void Rename(Transform t, string name)
    {
        if (t.name == name) return;
        Undo.RecordObject(t.gameObject, UndoLabel);
        t.name = name;
        if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
            PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
    }

    static string DisplayName(Transform t)
    {
        string n = SceneObjectNames.Original(t);
        string lower = n.ToLowerInvariant();
        var cold = t.GetComponent<ColdFixture>();
        if (cold != null) return cold.hidingSpace ? "Cold_Hiding_Cabinet" :
            (cold.retail ? "Retail_" : "Storage_") + (cold.frozen ? "Freezer" : "Chiller");
        if (lower.StartsWith("prop_fridge")) return "Legacy_Fridge_Type" + Regex.Match(n, @"\d+").Value;
        if (lower.StartsWith("storagerack")) return "Storage_Rack";
        if (lower.StartsWith("shelfpillar"))
        {
            string variant = n.Split('_').Length > 1 ? n.Split('_')[1] : "Standard";
            switch (variant)
            {
                case "E": variant = "Endcap"; break;
                case "onetwoL": variant = "Transition_L"; break;
                case "onetwoT": variant = "Transition_T"; break;
                case "onetwoTwp": variant = "Transition_T_Wide"; break;
                default: variant = variant.ToUpperInvariant(); break;
            }
            return "Shelf_Pillar_" + variant + (lower.Contains("short") ? "_Low" : "_Tall");
        }
        if (lower.StartsWith("shelftwoside")) return "Shelf_Double_Sided" + (lower.Contains("short") ? "_Low" : "_Tall");
        if (lower.StartsWith("shelfoneside")) return "Shelf_Single_Sided" + (lower.Contains("short") ? "_Low" : "_Tall");
        if (lower.Contains("polka") || lower.StartsWith("shelf deck"))
            return t.localScale.y < .1f ? "Shelf_Deck" : "Shelf_Base";
        if (lower.StartsWith(ShelfGrid.StripPrefix.ToLowerInvariant())) return "Shelf_Price_Rail";
        switch (n)
        {
            case "Models_Island": return n;
            case "Player": return n;
            case "Stock_def": return "Stock_Test_Crate";
            case "Puncher": return "Time_Clock";
            case "MusicBox": return "Staff_Radio";
            case "RetailChiller": return "Retail_Chiller";
            case "StorageChiller": return "Storage_Chiller";
            case "HideChiller": return "Cold_Hiding_Cabinet";
            case "Fridge": case "Staff fridge": return "Staff_Fridge";
            case "Plane_test": return "Catalogue_Floor";
            case "Products_Showcase": return "Products";
            case "Boxes_Showcase": return "Restocking_Boxes";
            case "Shop_Fixtures_Showcase": return "Retail_Fixtures";
            case "Staff_Furniture_Showcase": return "Staff_Room";
            case "Office_Furniture_Showcase": return "Office";
            case "WC_Furniture_Showcase": return "WC";
            case "Lobby_Furniture_Showcase": return "Lobby";
            case "Store_Furniture_Showcase": return "Exterior_And_Tools";
            case "Island_Layout": return "Catalogue_Labels";
            case "Plane": return "Original_Floor";
            case "Roof": return "Original_Roof";
            case "Maze": return "Maze_Shelves";
            case "SingleDoors": return "Hinged_Doors";
            case "DoubleDoors": return "Automatic_Doors";
            case "Fridges": return "Legacy_Refrigeration_Source";
            case "Storageracks": return "Storage_Racks";
            case "Cashierdesks": return "Checkout_Collision_Models";
            case "CeilingLights": return "Ceiling_Light_Sources";
            case "CeilingLamps": return "Ceiling_Lamp_Models";
            case "Shelf Stock": return "Shelf_Product_Drawer";
            case "Rooftop plant": return "Rooftop_HVAC";
            case "Canvas": return t.Find("Crosshair") != null ? "HUD_Crosshair_And_Tasks" : "HUD_Inventory_And_Burnout";
        }
        return Clean(n);
    }

    static string Clean(string value)
    {
        value = Regex.Replace(value, @"\s*\(\d+\)$", "");
        value = Regex.Replace(value, @"([a-z])([A-Z])", "$1_$2");
        value = Regex.Replace(value, @"[^A-Za-z0-9]+", "_").Trim('_');
        value = Regex.Replace(value, @"_\d+$", "");
        var words = value.Split('_').Where(s => s.Length > 0).Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1));
        value = string.Join("_", words);
        if (value.Length > 72) value = value.Substring(0, 72).TrimEnd('_');
        return value.Length > 0 ? value : "Object";
    }

    sealed class Layout
    {
        readonly SceneObjectNames map;
        readonly Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        readonly Dictionary<string, Transform> source;
        public Layout(SceneObjectNames map)
        {
            this.map = map;
            source = map.entries.GroupBy(e => e.originalPath).ToDictionary(g => g.Key, g => g.First().target);
            foreach (var f in map.folders.Where(f => f != null)) groups[PathOf(f)] = f;
        }

        public Transform Folder(string path)
        {
            if (groups.TryGetValue(path, out var found) && found != null) return found;
            int slash = path.LastIndexOf('/');
            var parent = slash < 0 ? null : Folder(path.Substring(0, slash));
            var go = new GameObject(slash < 0 ? path : path.Substring(slash + 1));
            SceneManager.MoveGameObjectToScene(go, map.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
            go.transform.SetParent(parent, false);
            map.folders.Add(go.transform);
            return groups[path] = go.transform;
        }

        void Put(string old, string folder) { if (source.TryGetValue(old, out var t)) Move(t, Folder(folder)); }
        void Split(string old, Func<Transform, string> destination)
        {
            if (!source.TryGetValue(old, out var root)) return;
            // Never split a physical assembly or a component-owned child collection.
            if (root.GetComponents<Component>().Length != 1 || PrefabUtility.IsPartOfPrefabInstance(root))
                throw new InvalidOperationException("Expected a plain source container: " + old);
            foreach (Transform t in root.Cast<Transform>().ToArray()) Move(t, Folder(destination(t)));
            Move(root, Folder("Authoring_References/Builder_Containers"));
        }

        void Shelves()
        {
            const string path = "Layout_15x15/Maze";
            if (!source.TryGetValue(path, out var maze)) return;
            // The maze's NavMeshModifier must still be an ancestor of every shelf.
            Put(path, "Supermarket/Sales_Floor/Shelving");
            foreach (var entry in map.entries.Where(e => e.originalParent == maze))
            {
                string name = ShelfGroup(entry.target);
                var group = map.folders.Find(f => f != null && f.parent == maze && f.name == name);
                if (group == null)
                {
                    var go = new GameObject(name);
                    Undo.RegisterCreatedObjectUndo(go, UndoLabel);
                    go.transform.SetParent(maze, false);
                    group = go.transform;
                    map.folders.Add(group);
                }
                Move(entry.target, group);
            }
        }

        public void Arrange()
        {
            foreach (string room in new[] { "Sales_Floor", "Staff_Room", "Office", "WC", "Lobby", "Storage", "Cold_Room" })
                Folder("Supermarket/" + room);
            foreach (string area in new[] { "Storefront", "Service_Yard", "Parking" }) Folder("Exterior/" + area);
            foreach (string name in new[] { "GameSystems", "EventSystem", "Player", "Directional Light", "Global Volume" }) Put(name, "Game_Systems");
            foreach (var e in map.entries.Where(e => e.originalParent == null && e.originalName == "Canvas")) Move(e.target, Folder("Game_Systems/UI"));
            foreach (string name in new[] { "EntrancePoint", "ExitPoint", "CustomerSpawner", "CashierPoint" }) Put(name, "Game_Systems/Customer_Routing");
            foreach (string name in new[] { "Stock_def", "Mop", "Puncher", "CoffeeMachine", "MusicBox", "Flashlight", "ToolSnapPoints" }) Put(name, "Supermarket/Staff_Room/Gameplay_Tools");
            foreach (string name in new[] { "Trashcan", "Trashcan (1)" }) Put(name, "Authoring_References/Inactive_Prototypes");
            foreach (string name in new[] { "Shelf Stock", "RoomPatches", "RoomReflections" }) Put(name, "Generated_Content");
            Put("AisleSigns", "Supermarket/Sales_Floor/Signage");
            Put("CeilingLamps", "Supermarket/Shared_Building/Lighting");
            Split("Staff Room - Furnished", t => "Supermarket/Staff_Room/" + Category(t));
            Split("Office - Furnished", t => "Supermarket/Office/" + Category(t));
            Split("WC - Furnished", t => "Supermarket/WC/" + Category(t));
            Split("Lobby - Furnished", t => "Supermarket/Lobby/" + Category(t));
            Shelves();
            Put("Layout_15x15/Storageracks", "Supermarket/Storage/Shelving");
            Put("Layout_15x15/Cashierdesks", "Supermarket/Sales_Floor/Checkouts");
            Put("Layout_15x15/Counter Facing", "Supermarket/Sales_Floor/Checkouts");
            Put("Layout_15x15/Fridges", "Authoring_References/Legacy_Fixtures");
            Put("Layout_15x15/Cardboards", "Supermarket/Storage/Boxes_And_Props");
            Put("Layout_15x15/Backstreet", "Exterior/Service_Yard/Utilities");
            Put("Layout_15x15/Walls", "Supermarket/Shared_Building/Original_Walls");
            foreach (string name in new[] { "Plane", "Roof" }) Put("Layout_15x15/" + name, "Supermarket/Shared_Building/Original_Surfaces");
            foreach (string name in new[] { "CeilingLights", "MallSpeakers" }) Put("Layout_15x15/" + name, "Supermarket/Shared_Building/Lighting_And_Audio");
            foreach (string name in new[] { "SingleDoors", "DoubleDoors" }) Put("Layout_15x15/" + name, "Supermarket/Shared_Building/Doors");
            Put("Layout_15x15", "Authoring_References/Builder_Containers");
            Put("Cold Chain - Retail and Storage/Retail Chillers", "Supermarket/Sales_Floor/Refrigeration");
            Put("Cold Chain - Retail and Storage/Cold Storage Cabinets", "Supermarket/Cold_Room/Refrigeration");
            Split("Cold Chain - Retail and Storage", t => "Supermarket/Cold_Room/" + Category(t));
            Split("Parking - Finished", t => "Exterior/Parking/" + ParkingCategory(t));
            Split("Store - Finished", t => SceneObjectNames.Original(t) == "Rooftop plant"
                ? "Supermarket/Shared_Building/Rooftop_Equipment" : Area(Centre(t)) + "/" + Category(t));
            Split("Environment - Refined", t => Area(Centre(t)) + "/Doors_And_Wall_Finishes");
            Split("Environment - Fit corrections", t => "Supermarket/Shared_Building/Wall_Finishes");
        }

        static string ShelfGroup(Transform t)
        {
            string name = SceneObjectNames.Original(t).ToLowerInvariant();
            return name.Contains("pillar") ? "Pillars_And_Endcaps" : name.Contains("oneside") ? "Single_Sided" : "Double_Sided";
        }

        static Vector3 Centre(Transform t)
        {
            var renderer = t.GetComponent<Renderer>();
            return renderer != null ? renderer.bounds.center : t.position;
        }

        static string Area(Vector3 p)
        {
            if (p.z > -110.2f) return "Exterior/Storefront";
            if (p.x < 9.8f || p.x > 85.2f || p.z < -190.2f) return "Exterior/Service_Yard";
            if (p.x < 25.01f && p.z < -150f && p.z > -175f) return "Exterior/Service_Yard";
            if (p.z < -175f) return p.x < 55f ? "Supermarket/Storage" : "Supermarket/Cold_Room";
            if (p.x < 25.01f)
                return p.z < -136.6667f ? "Supermarket/Staff_Room" : p.z < -123.3333f ? "Supermarket/Office" : "Supermarket/WC";
            return p.z > -120f ? "Supermarket/Lobby" : "Supermarket/Sales_Floor";
        }

        static string Category(Transform t)
        {
            string n = SceneObjectNames.Original(t).ToLowerInvariant();
            if (!t.gameObject.activeSelf) return "Inactive_Originals";
            if (n.Contains("door") || n.Contains("entrance") || n.Contains("connection") || n.Contains("portal")) return "Doors";
            if (n.Contains("floor") || n.Contains("wall") || n.Contains("lining") || n.Contains("ceiling") || n.Contains("partition") || n.Contains("cladding") || n.Contains("fascia") || n.Contains("parapet") || n.Contains("rib") || n.Contains("plinth") || n.Contains("infill")) return "Architecture";
            if (n.Contains("sign") || n.Contains("label") || n.Contains("notice") || n.Contains("whiteboard") || n.Contains("clock")) return "Signs_And_Notices";
            if (n.Contains("plant") || n.Contains("greenery")) return "Greenery";
            if (n.Contains("recycling") || n.Contains(" bin") || n.StartsWith("bin") || n.Contains("collection container")) return "Recycling";
            if (n.Contains("lamp") || n.Contains("light") || n.Contains("pendant")) return "Lighting";
            if (n.Contains("locker") || n.Contains("cabin") || n.Contains("cabinet") || n.Contains("shelf") || n.Contains("shelves")) return "Cabinets_And_Storage";
            if (n.Contains("checkout")) return "Checkouts";
            if (n.Contains("evaporator") || n.Contains("riser") || n.Contains("drain") || n.Contains("grille") || n.Contains("extract") || n.Contains("power") || n.Contains("pipe")) return "Utilities";
            return "Furniture_And_Props";
        }

        static string ParkingCategory(Transform t)
        {
            string n = SceneObjectNames.Original(t).ToLowerInvariant();
            if (n.Contains("lighting")) return "Lighting";
            if (n.Contains("tree") || n.Contains("green") || n.Contains("plant")) return "Greenery";
            if (n.Contains("car ") || n.Contains("parked")) return "Vehicles";
            if (n.Contains("bay") || n.Contains("line") || n.Contains("wheel stop")) return "Bays_And_Markings";
            return "Roads_And_Furniture";
        }
    }

    sealed class Snapshot
    {
        sealed class State
        {
            public Transform target;
            public Matrix4x4 matrix;
            public bool active, activeInHierarchy;
            public int layer, flags;
            public Component[] components;
            public MonoBehaviour[] behavioursAbove;
            public Object prefab;
            public string tag;
        }
        readonly List<State> states = new List<State>();
        readonly List<(Component component, string path, Object value)> references = new List<(Component, string, Object)>();

        public static Snapshot Take(Scene scene, SceneObjectNames map)
        {
            var result = new Snapshot();
            foreach (var t in Objects(scene))
            {
                if (t == map.transform || map.folders.Contains(t)) continue;
                result.states.Add(new State { target = t, matrix = t.localToWorldMatrix,
                    active = t.gameObject.activeSelf, activeInHierarchy = t.gameObject.activeInHierarchy,
                    layer = t.gameObject.layer, tag = t.tag, flags = (int)GameObjectUtility.GetStaticEditorFlags(t.gameObject),
                    components = t.GetComponents<Component>(), behavioursAbove = t.GetComponentsInParent<MonoBehaviour>(true),
                    prefab = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) });
                foreach (var component in t.GetComponents<MonoBehaviour>())
                {
                    if (component == null) continue;
                    using var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                            result.references.Add((component, property.propertyPath, property.objectReferenceValue));
                }
            }
            return result;
        }

        public void RequireUnchanged()
        {
            foreach (var state in states)
            {
                var t = state.target;
                if (t == null) throw new InvalidOperationException("Organization removed an existing object.");
                for (int i = 0; i < 16; i++)
                    if (Mathf.Abs(t.localToWorldMatrix[i] - state.matrix[i]) > .0005f)
                        throw new InvalidOperationException("Organization moved or distorted " + t.name);
                if (t.gameObject.activeSelf != state.active || t.gameObject.activeInHierarchy != state.activeInHierarchy ||
                    t.gameObject.layer != state.layer || t.tag != state.tag ||
                    (int)GameObjectUtility.GetStaticEditorFlags(t.gameObject) != state.flags ||
                    !t.GetComponents<Component>().SequenceEqual(state.components) ||
                    !t.GetComponentsInParent<MonoBehaviour>(true).SequenceEqual(state.behavioursAbove) ||
                    PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != state.prefab)
                    throw new InvalidOperationException("Organization changed object behaviour or its prefab link: " + t.name);
            }
            foreach (var group in references.GroupBy(r => r.component))
            {
                using var serialized = new SerializedObject(group.Key);
                foreach (var reference in group)
                {
                    var property = serialized.FindProperty(reference.path);
                    if (property == null || property.objectReferenceValue != reference.value)
                        throw new InvalidOperationException("Organization changed a serialized reference: " + group.Key.name + "/" + reference.path);
                }
            }
        }
    }
}
