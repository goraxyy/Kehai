using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// A look at the store in Play, for building the mini store sandbox from its own models
// (0.5.2, C11.17): for each kind of fixture, how many there are, where the first few stand, how
// big they are and what they carry. Batch only:
//   tools/run/kehai-run unity -- -executeMethod MiniStoreSurvey.Run
[InitializeOnLoad]
public static class MiniStoreSurvey
{
    static double playingSince = -1;

    static readonly string[] Kinds =
    {
        "ShelfUnit", "HidingPlace", "ColdFixture", "ColdHideInterior", "HingeDoor", "AutoDoubleDoor", "FurnitureDoor",
        "Kehai.Karen.BreakerPanel", "Kehai.Karen.LightControl", "FogBox", "PaBox", "ClosingMic", "Puncher", "ToolSnapPoint",
        "TrashContainer", "Trashcan", "CustomerSpawner", "CctvCamera", "FreezerAlarm", "ElectricBox", "MusicBox",
        "CoffeeMachine", "RoomLightSwitch", "RoomLighting", "StorageBoxes", "StockCrate", "ProductBox", "CeilingLamps",
        "CounterFacing", "CashRegister", "Checkout", "WallClockHands", "Kehai.Karen.KarenWorld", "ShiftManager",
        "Unity.AI.Navigation.NavMeshSurface", "CameraWall", "CameraBox", "ScreenWall", "Kehai.Karen.PaSystem",
    };

    static MiniStoreSurvey()
    {
        if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "MiniStoreSurvey.Run") < 0) return;
        EditorApplication.update += Tick;
    }

    public static void Run() => TricksSandboxMenu.Open();

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > 600) { Debug.LogError("MiniStoreSurvey: timed out"); EditorApplication.Exit(2); return; }
        if (!EditorApplication.isPlaying) return;
        if (playingSince < 0) playingSince = EditorApplication.timeSinceStartup;
        if (EditorApplication.timeSinceStartup - playingSince < 10) return;
        EditorApplication.update -= Tick;
        try { Debug.Log(Survey()); }
        catch (Exception e) { Debug.LogError("MiniStoreSurvey: " + e); EditorApplication.Exit(1); return; }
        EditorApplication.Exit(0);
    }

    static Type Find(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(name, false);
            if (t != null) return t;
            t = asm.GetTypes().FirstOrDefault(x => x.Name == name && typeof(Component).IsAssignableFrom(x));
            if (t != null) return t;
        }
        return null;
    }

    static string Path(Transform t)
    {
        var parts = new List<string>();
        for (; t != null && parts.Count < 6; t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }

    static Bounds BoundsOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static string Describe(GameObject go)
    {
        Bounds b = BoundsOf(go);
        var comps = go.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).Where(n => n != "Transform");
        var mats = go.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct().Take(4);
        int colliders = go.GetComponentsInChildren<Collider>().Length;
        return $"{Path(go.transform)} | at {go.transform.position:F2} yaw {go.transform.eulerAngles.y:F0} | bounds {b.center:F2} size {b.size:F2} | " +
               $"children {go.GetComponentsInChildren<Transform>(true).Length - 1}, colliders {colliders}, layer {LayerMask.LayerToName(go.layer)} | " +
               $"comps [{string.Join(",", comps)}] | mats [{string.Join(",", mats)}]";
    }

    static string Survey()
    {
        var s = new StringBuilder("MiniStoreSurvey\n");
        foreach (string kind in Kinds)
        {
            Type t = Find(kind);
            if (t == null) { s.AppendLine($"## {kind}: no such type"); continue; }
            var all = UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None).Cast<Component>().ToArray();
            s.AppendLine($"## {kind}: {all.Length}");
            foreach (Component c in all.Take(kind == "ShelfUnit" || kind == "HidingPlace" ? 12 : 4))
                s.AppendLine("  - " + Describe(c.gameObject));
        }
        // Lamps, walls, floors and ceilings by name.
        foreach (string name in new[] { "CeilingLights", "Ceiling_Light_Sources", "Wall", "Floor", "Ceiling", "Cream_Wall_Panel", "Panel_Rib", "Charcoal_Plinth" })
        {
            var hits = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.name.StartsWith(name, StringComparison.Ordinal)).Take(4).ToArray();
            s.AppendLine($"## name {name}*: {hits.Length}+");
            foreach (var h in hits) s.AppendLine("  - " + Describe(h.gameObject));
        }
        var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        s.AppendLine($"## lights: {lights.Length}");
        foreach (var l in lights.Take(6)) s.AppendLine($"  - {Path(l.transform)} {l.type} range {l.range:F1} int {l.intensity:F2} at {l.transform.position:F1}");
        // Every room folder under the building, and the first objects in each.
        GameObject market = GameObject.Find("Supermarket");
        if (market != null)
            foreach (Transform room in market.transform)
            {
                s.AppendLine($"## room {room.name}");
                foreach (Transform group in room)
                {
                    s.Append($"  - {group.name} ({group.childCount}): ");
                    s.AppendLine(string.Join(", ", group.Cast<Transform>().Take(10).Select(c => c.name)));
                }
            }
        return s.ToString();
    }
}
