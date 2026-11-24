using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using Kehai.Sandbox;
using Kehai.Store;
using UnityEditor;
using UnityEngine;

// The mini store sandbox, opened for real (0.5.2, C11.17): Kehai → Sandbox → Mini store in
// Play, then a look the way you would: you start inside, her map has its three rooms with their
// names, the copied shelves are stocked, each room's lamps light that room, every door opens
// with E from where you'd stand, and Karen, started, walks its rooms and stays in them.
// Screenshots go next to the log when Unity may render:
//   tools/run/kehai-run unity --graphics -- -executeMethod MiniStoreCheck.Run
// Exit 0 when all hold, 1 when one doesn't, 2 if Play never got there.
[InitializeOnLoad]
public static class MiniStoreCheck
{
    const double Limit = 900;
    static IEnumerator steps;
    static int frame = -1;
    static readonly List<string> failures = new List<string>();

    static MiniStoreCheck()
    {
        if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "MiniStoreCheck.Run") < 0) return;
        EditorApplication.update += Tick;
    }

    public static void Run() => MiniStoreMenu.Open();

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > Limit) { Debug.LogError("MiniStoreCheck: timed out"); EditorApplication.Exit(2); return; }
        if (!EditorApplication.isPlaying) return;
        if (frame == Time.frameCount) return;   // one step a frame
        frame = Time.frameCount;
        if (steps == null) steps = Steps();
        bool more;
        try { more = steps.MoveNext(); }
        catch (Exception e) { Debug.LogError("MiniStoreCheck: " + e); EditorApplication.Exit(1); return; }
        if (more) return;
        EditorApplication.update -= Tick;
        Debug.Log(failures.Count == 0 ? "MiniStoreCheck: all held" : "MiniStoreCheck: failed:\n  " + string.Join("\n  ", failures));
        EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
    }

    static void Expect(bool ok, string what)
    {
        Debug.Log($"MiniStoreCheck: {(ok ? "ok  " : "FAIL")} {what}");
        if (!ok) failures.Add(what);
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    static IEnumerator Steps()
    {
        yield return Wait(4f);
        GameObject root = MiniStore.Root;
        Expect(root != null, "the mini store is built");
        if (root == null) yield break;
        Debug.Log("MiniStoreCheck: build report\n" + MiniStore.Report);
        Expect(!MiniStore.Report.Contains("MISSING") && !MiniStore.Report.Contains("missing"), "every model and material was found");

        // You start inside.
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Expect(player != null && MiniStore.Contains(player.transform.position), $"you start inside (at {player?.transform.position})");

        // Her map: three rooms with the store's names, and nothing else.
        StoreMap map = StoreMap.Current;
        Debug.Log($"MiniStoreCheck: her map {map.CellCount} cells, rooms [{string.Join(", ", map.Rooms)}]");
        foreach (string room in new[] { "Sales floor", "Stockroom", "Cold storage" })
            Expect(map.Rooms.Contains(room), $"her map has a room called {room}");
        Expect(map.Rooms.Count == 3, $"her map has three rooms (has {map.Rooms.Count})");
        foreach (var (name, local) in new[] { ("Sales floor", new Vector3(0f, 0f, 5f)), ("Stockroom", new Vector3(-8f, 0f, -7f)), ("Cold storage", new Vector3(9f, 0f, -7f)) })
            Expect(StoreMap.AreaAt(MiniStore.World(local)) == name, $"the middle of its {name} reads as {name} ({StoreMap.AreaAt(MiniStore.World(local))})");

        // The copied shelves are stocked, and sell more than one thing.
        var bays = root.GetComponentsInChildren<ShelfUnit>().Where(u => u.GetComponent<ColdFixture>() == null).ToArray();
        int stocked = 0;
        var sections = new HashSet<string>();
        var slots = new List<ShelfSlot>();
        foreach (ShelfUnit bay in bays)
        {
            slots.Clear();
            ShelfStock.Current.Near(bay.transform.position, 3f, slots);
            int mine = slots.Count(s => s.isFilled && Vector3.Distance(s.Position, bay.transform.position) < 3f);
            if (mine > 0) stocked++;
            if (!string.IsNullOrEmpty(bay.section)) sections.Add(bay.section);
        }
        Expect(bays.Length >= 8 && stocked == bays.Length, $"every copied shelf is stocked ({stocked} of {bays.Length})");
        Expect(sections.Count >= 2, $"the shelves sell more than one thing ({string.Join(", ", sections)})");

        // Each room's lamps light that room: a lamp and the floor under it share a layer.
        foreach (Light light in root.GetComponentsInChildren<Light>())
        {
            var data = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            uint lightLayers = data != null ? data.renderingLayers : 1u;
            Vector3 below = light.transform.position; below.y = 0.5f;
            Renderer near = root.GetComponentsInChildren<Renderer>()
                .Where(r => r.bounds.size.y < 3f && r.bounds.Contains(new Vector3(below.x, r.bounds.center.y, below.z)))
                .OrderBy(r => Mathf.Abs(r.bounds.center.y - 1f)).FirstOrDefault();
            if (near != null && (near.renderingLayerMask & lightLayers) == 0)
                Expect(false, $"{light.name} in {StoreMap.AreaAt(light.transform.position)} lights {near.name} under it (layers {lightLayers} vs {near.renderingLayerMask})");
        }

        // Every hinged door opens with E from where you'd stand.
        var interact = player != null ? player.GetComponent<PlayerInteract>() : null;
        foreach (HingeDoor door in root.GetComponentsInChildren<HingeDoor>())
        {
            Bounds b = MiniStore.Footprint(door.gameObject);
            bool alongZ = b.size.z > b.size.x;
            Vector3 face = alongZ ? Vector3.left : Vector3.forward;
            var ray = new Ray(new Vector3(b.center.x, 1.5f, b.center.z) + face * 1.4f, -face);
            IInteractable target = interact != null ? interact.FindAimedTarget(ray) : null;
            Expect(target != null, $"{door.transform.parent?.name ?? door.name}: you can aim at it");
            if (target == null) continue;
            target.Interact(interact);
            yield return Wait(1f);
            Expect(door.IsOpen, $"{door.transform.parent?.name ?? door.name}: E opens it");
        }

        yield return Shots("built");

        // Karen: inside, and started, she walks its rooms and stays in them.
        KarenBrain brain = UnityEngine.Object.FindAnyObjectByType<KarenBrain>();
        Expect(brain != null && brain.Body != null, "Karen is here");
        if (brain == null || brain.Body == null) yield break;
        Vector3 start = brain.Body.transform.position;
        Expect(MiniStore.Contains(start), $"she starts inside, in the {StoreMap.AreaAt(start)} (at {start})");
        if (brain.Shift != null && !brain.Shift.IsShiftActive) brain.Shift.StartShift();
        brain.enabled = true;
        var seen = new HashSet<string>();
        float moved = 0f, outside = 0f;
        Vector3 last = start;
        for (float t = 0f; t < 45f; t += Time.deltaTime)
        {
            Vector3 at = brain.Body.transform.position;
            moved += Vector3.Distance(at, last);
            last = at;
            seen.Add(StoreMap.AreaAt(at));
            if (!MiniStore.Contains(at)) outside += Time.deltaTime;
            yield return null;
        }
        Debug.Log($"MiniStoreCheck: Karen walked {moved:0.0} m through [{string.Join(", ", seen)}]; plan {brain.CurrentPlan?.Name}");
        Expect(moved > 8f, $"she walks when started ({moved:0.0} m in 45 s)");
        Expect(outside == 0f, $"she stays inside ({outside:0.0} s outside)");
        yield return Shots("karen");

        // Every trick: would she use it here now, and if not, why (her gate, then the trick's own
        // conditions). With all tricks allowed for this look; the panel's choice is put back.
        var before = MiniStoreSettings.Allowed;
        MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.All;
        MiniStoreSettings.ApplyTo(brain.config);
        var table = new List<string>();
        foreach (string id in TricksSandbox.Catalogue)
            table.Add($"{id,-16} {MiniStorePanel.Readiness(brain, id)}");
        MiniStoreSettings.Allowed = before;
        MiniStoreSettings.ApplyTo(brain.config);
        Debug.Log("MiniStoreCheck: tricks here now\n  " + string.Join("\n  ", table));

        // The panel's limit holds for her own choices: with none allowed, every trick is refused.
        MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.None;
        MiniStoreSettings.ApplyTo(brain.config);
        int open = 0;
        foreach (string id in TricksSandbox.Catalogue)
        {
            Tactic t = TacticLibrary.Get(id);
            if (t != null && HtnPlanner.Gated(t, brain.Ctx, out _)) open++;
        }
        Expect(open == 0, $"with no tricks allowed, none passes her gate ({open} did)");
        MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.OnlyOne;
        MiniStoreSettings.Chosen = "spill";
        MiniStoreSettings.ApplyTo(brain.config);
        var passing = TricksSandbox.Catalogue.Where(id => TacticLibrary.Get(id) is Tactic t && HtnPlanner.Gated(t, brain.Ctx, out _)).ToList();
        Expect(passing.All(id => id == "spill"), $"with only Spill allowed, only Spill passes her gate ([{string.Join(", ", passing)}])");
        MiniStoreSettings.Allowed = before;
        MiniStoreSettings.ApplyTo(brain.config);
    }

    // Screenshots from where you'd stand in each room, when Unity renders.
    static IEnumerator Shots(string tag)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        string dir = OutDir();
        if (dir == null) yield break;
        Camera main = Camera.main;
        if (main == null) yield break;
        var go = new GameObject("MiniStoreCheck camera");
        var cam = go.AddComponent<Camera>();
        cam.CopyFrom(main);
        var views = new (string name, Vector3 at, float yaw)[]
        {
            ("start", MiniStore.PlayerStart + Vector3.up * 1.6f, 180f),
            ("sales", new Vector3(13f, 1.7f, 11.5f), 235f),
            ("stockroom", new Vector3(1.5f, 1.7f, -4f), 250f),
            ("cold", new Vector3(3.5f, 1.7f, -6f), 70f),
        };
        foreach (var v in views)
        {
            go.transform.SetPositionAndRotation(MiniStore.World(v.at), Quaternion.Euler(8f, v.yaw, 0f));
            yield return null;
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(Path.Combine(dir, $"mini_{tag}_{v.name}.png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
        }
        UnityEngine.Object.Destroy(go);
        Debug.Log($"MiniStoreCheck: screenshots ({tag}) in {dir}");
    }

    // The run's folder: the one its log is written to.
    static string OutDir()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-logFile");
        if (i < 0 || i + 1 >= args.Length) return null;
        string dir = Path.GetDirectoryName(args[i + 1]);
        return Directory.Exists(dir) ? dir : null;
    }
}
