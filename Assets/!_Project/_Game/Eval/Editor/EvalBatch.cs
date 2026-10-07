using System.Linq;
using System.Text;
using Kehai.Eval;
using Kehai.Store;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// The eval harness without a window — for runs from a terminal, CI, or an agent:
//
//   Unity -batchmode -projectPath . -executeMethod EvalBatch.Play -kehai-ablation \
//         -ablation-careers 2 -ablation-shifts 4 -eval-timeout-min 90 -logFile eval.log
//
// Play opens the store (or -evalScene <path>) and enters play mode; the -kehai-* flags
// are then picked up by EvalCommandLine exactly as they are in a built player.
[InitializeOnLoad]
public static class EvalBatch
{
    static double nextReport;

    static EvalBatch()
    {
        if (!Application.isBatchMode) return;
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-eval-timeout-min");
        double limit = i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], out double m) ? m * 60.0 : 0.0;

        EditorApplication.update += () =>
        {
            if (limit > 0 && EditorApplication.timeSinceStartup > limit)
            {
                Debug.LogError($"EvalBatch: timed out after {limit / 60:0} min");
                EditorApplication.Exit(2);
            }
            if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup > nextReport)
            {
                nextReport = EditorApplication.timeSinceStartup + 30.0;
                AblationRunner runner = Object.FindAnyObjectByType<AblationRunner>();
                if (runner != null) Debug.Log("EvalBatch progress: " + runner.Progress);
            }
        };
    }

    public static void Play()
    {
        string scene = Arg("-evalScene") ?? EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
        if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        Debug.Log("EvalBatch: entering play mode in " + EditorSceneManager.GetActiveScene().path);
        EditorApplication.EnterPlaymode();
    }

    // Opens each scene given with -scenes a.unity;b.unity and logs what's in it, so two
    // versions of the store can be compared without opening them by hand.
    public static void Inspect()
    {
        var sb = new StringBuilder("EvalBatch scene inspection\n");
        foreach (string path in (Arg("-scenes") ?? string.Empty).Split(';').Where(p => p.Length > 0))
        {
            var s = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            sb.AppendLine($"== {path}");
            sb.AppendLine($"   roots {s.rootCount}, game objects {all.Length}");
            sb.AppendLine($"   shelf units {Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include).Length}, " +
                          $"shelf slots {StoreLayout.BuildPreview()}, " +
                          $"items {Object.FindObjectsByType<Item>(FindObjectsInactive.Include).Length}, " +
                          $"lights {Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Length}, " +
                          $"renderers {Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include).Length}");
            sb.AppendLine($"   customer spawners {Object.FindObjectsByType<CustomerSpawner>(FindObjectsInactive.Include).Length}, " +
                          $"trashcans {Object.FindObjectsByType<Trashcan>(FindObjectsInactive.Include).Length}, " +
                          $"doors {Object.FindObjectsByType<HingeDoor>(FindObjectsInactive.Include).Length}+" +
                          $"{Object.FindObjectsByType<AutoDoubleDoor>(FindObjectsInactive.Include).Length}, " +
                          $"navmesh triangles {NavMesh.CalculateTriangulation().indices.Length / 3}");
            sb.AppendLine("   root objects: " + string.Join(", ", s.GetRootGameObjects().Select(g => $"{g.name}({g.GetComponentsInChildren<Transform>(true).Length})")));
            try { sb.AppendLine("   map: " + StoreMap.Rebuild().Summary().Split('\n')[0]); }
            catch (System.Exception e) { sb.AppendLine("   map failed: " + e.Message); }
        }
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    // Every door in the store: where it is, what it's made of, and whether a body fits.
    public static void Doors()
    {
        string scene = Arg("-evalScene") ?? EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        var sb = new StringBuilder("EvalBatch doors\n");
        foreach (Transform group in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Where(t => t.name.Contains("Door") && t.parent != null && t.parent.name == "Layout_15x15"))
        {
            sb.AppendLine($"== {group.name} ({group.childCount} children)");
            foreach (Transform door in group)
            {
                var hinge = door.GetComponentInChildren<HingeDoor>(true);
                var auto = door.GetComponentInChildren<AutoDoubleDoor>(true);
                sb.AppendLine($"  {door.name} at {door.position} rot {door.eulerAngles.y:0} hinge={(hinge != null ? hinge.name : "-")} auto={(auto != null ? auto.name : "-")}");
                foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
                {
                    string owner = c.GetComponentInParent<HingeDoor>() != null ? " [moves with hinge]" : "";
                    sb.AppendLine($"     {c.name} {c.GetType().Name} trigger={c.isTrigger} centre {c.bounds.center} size {c.bounds.size}{owner}");
                }
            }
        }
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    // The NavMesh surfaces and what they collect, and which layers the moving things are on.
    public static void Surfaces()
    {
        string scene = Arg("-evalScene") ?? EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path;
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        var sb = new StringBuilder("EvalBatch surfaces\n");
        foreach (var s in Object.FindObjectsByType<Unity.AI.Navigation.NavMeshSurface>(FindObjectsInactive.Include))
            sb.AppendLine($"  {s.name}: collect={s.collectObjects} geometry={s.useGeometry} layers={s.layerMask.value} agent={s.agentTypeID} " +
                          $"size={s.size} centre={s.center} data={(s.navMeshData != null ? s.navMeshData.sourceBounds.ToString() : "none")}");
        var spawner = Object.FindAnyObjectByType<CustomerSpawner>();
        if (spawner != null && spawner.customerPrefab != null)
            sb.AppendLine($"  customer prefab layer {LayerMask.LayerToName(spawner.customerPrefab.layer)}");
        foreach (var i in Object.FindObjectsByType<Item>(FindObjectsInactive.Include).Take(3))
            sb.AppendLine($"  item {i.name} layer {LayerMask.LayerToName(i.gameObject.layer)}");
        foreach (var u in Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Include).Take(2))
            sb.AppendLine($"  bay {u.name} layer {LayerMask.LayerToName(u.gameObject.layer)} static={u.gameObject.isStatic} colliders={u.GetComponentsInChildren<Collider>().Length}");
        Debug.Log(sb.ToString());
        EditorApplication.Exit(0);
    }

    static string Arg(string key)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
        return null;
    }
}
