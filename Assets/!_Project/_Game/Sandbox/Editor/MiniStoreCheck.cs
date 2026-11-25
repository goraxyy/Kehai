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
public static partial class MiniStoreCheck
{
    // Long enough for each trick alone (-mini-tricks): 24 restarts and up to a minute each.
    static double Limit => Array.IndexOf(Environment.GetCommandLineArgs(), "-mini-tricks") >= 0 ? 3600 : 900;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool begun;
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
        if (!begun) { steps.Push(Steps()); begun = true; }
        // One step a frame, nested steps (waits, screenshots) run in place.
        try
        {
            while (steps.Count > 0)
            {
                IEnumerator top = steps.Peek();
                if (!top.MoveNext()) { steps.Pop(); continue; }
                if (top.Current is IEnumerator inner) { steps.Push(inner); continue; }
                return;
            }
        }
        catch (Exception e) { Debug.LogError("MiniStoreCheck: " + e); EditorApplication.Exit(1); return; }
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

        // Her map: every room of the store by its name (the backstreet and the pavement are
        // "Outside" to her, as in the store), and each room's middle reads as that room.
        StoreMap map = StoreMap.Current;
        Debug.Log($"MiniStoreCheck: her map {map.CellCount} cells, rooms [{string.Join(", ", map.Rooms)}]");
        foreach (var (name, store, outdoors) in MiniStore.Rooms)
        {
            Vector3 middle = MiniStore.Half(new Vector3(store.center.x, 0f, store.center.y));
            Expect(StoreMap.AreaAt(middle) == name, $"the middle of its {name} reads as {name} ({StoreMap.AreaAt(middle)})");
            if (!outdoors) Expect(map.Rooms.Contains(name), $"her map has a room called {name}");
        }
        Expect(map.Rooms.Exists(r => r.StartsWith("Outside")), "her map has the outside (backstreet, pavement)");

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

        // Every key thing her tricks and your interactions need is here.
        yield return Wait(2f);
        KeyThings();

        // What floats or sits in a wall (the owner saw a sweet floating by the till, and something
        // in the staff room's wall right of the camera controls): logged with where it is.
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>())
        {
            if (r == null || !MiniStore.Contains(r.bounds.center) || r.bounds.size.magnitude > 0.6f || r.bounds.min.y < 0.3f) continue;
            if (r.GetComponentInParent<Light>() != null || r.name.StartsWith("Lamp")) continue;
            bool held = Physics.Raycast(r.bounds.center, Vector3.down, out RaycastHit below, r.bounds.extents.y + 0.12f, ~0, QueryTriggerInteraction.Ignore);
            bool onWall = Physics.CheckBox(r.bounds.center, r.bounds.extents * 0.9f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (!held && !onWall) Debug.Log($"MiniStoreCheck: floating {PathOf(r.transform)} at {r.bounds.center:F2} in the {StoreMap.AreaAt(r.bounds.center)}");
        }
        foreach (Transform shellPart in MiniStore.Root.transform.Find("Shell"))
        {
            if (!shellPart.name.StartsWith("Wall") || shellPart.name.Contains("plinth")) continue;
            Bounds w = shellPart.GetComponent<Renderer>().bounds;
            foreach (Collider c in Physics.OverlapBox(w.center, w.extents * 0.98f, Quaternion.identity, ~0, QueryTriggerInteraction.Collide))
                if (c != null && !c.transform.IsChildOf(MiniStore.Root.transform.Find("Shell")) && c.GetComponentInParent<HingeDoor>() == null && c.GetComponentInParent<AutoDoubleDoor>() == null)
                    Debug.Log($"MiniStoreCheck: in a wall: {PathOf(c.transform)} at {c.bounds.center:F2} ({StoreMap.AreaAt(c.bounds.center)})");
        }

        // Every hinged door opens with E from where you'd stand, on one side or the other.
        var interact = player != null ? player.GetComponent<PlayerInteract>() : null;
        foreach (HingeDoor door in root.GetComponentsInChildren<HingeDoor>())
        {
            Bounds b = MiniStore.Footprint(door.gameObject);
            bool alongZ = b.size.z > b.size.x;
            IInteractable target = null;
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 face = (alongZ ? Vector3.left : Vector3.forward) * side;
                var ray = new Ray(new Vector3(b.center.x, 1.5f, b.center.z) + face * 1.4f, -face);
                target = interact != null ? interact.FindAimedTarget(ray) : null;
                if (target != null) break;
            }
            string label = door.GetComponentInParent<Transform>().name;
            for (Transform t = door.transform; t != null; t = t.parent) if (t.name.StartsWith("Mini ")) { label = t.name; break; }
            Expect(target != null, $"{label} ({StoreMap.AreaAt(b.center)}): you can aim at it");
            if (target == null) continue;
            target.Interact(interact);
            yield return Wait(1f);
            Expect(door.IsOpen, $"{label}: E opens it");
            // Away from you, and E again while it swings reverses it. You stand where you'd use it.
            Vector3 eye = Stand(player, b, alongZ);
            target.Interact(interact);
            yield return Wait(0.15f);
            Expect(!door.IsOpen, $"{label}: E while it moves turns it back");
            // Shut all the way before the next try (a door turned back mid-swing keeps its side).
            for (float t = 0f; t < 5f && door.IsMoving; t += Time.deltaTime) yield return null;
            yield return Wait(0.3f);
            target.Interact(interact);
            float near = Vector3.Distance(MiniStore.Footprint(door.gameObject).center, eye);
            yield return Wait(0.4f);
            float after = Vector3.Distance(MiniStore.Footprint(door.gameObject).center, eye);
            Expect(after >= near - 0.02f, $"{label}: opens away from you (leaf {near:0.00} m, then {after:0.00} m)");
            target.Interact(interact);   // and shut again
            yield return Wait(1.2f);
        }

        yield return Shots("built");

        // Karen: inside, and started, she walks its rooms and stays in them.
        KarenBrain brain = UnityEngine.Object.FindAnyObjectByType<KarenBrain>();
        Expect(brain != null && brain.Body != null, "Karen is here");
        TricksSandbox sandbox = UnityEngine.Object.FindAnyObjectByType<TricksSandbox>();
        Expect(sandbox != null && sandbox.HasPanel, "the mini store's panel is up");
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

        // Shoppers: in by the front, around its shelves, to its till; none heads for the store's.
        int shoppers = 0, atTill = 0, wandered = 0;
        CustomerSpawner shop = UnityEngine.Object.FindAnyObjectByType<CustomerSpawner>();
        var seenShoppers = new HashSet<CustomerNPC>();
        for (float t = 0f; t < 90f; t += Time.deltaTime)
        {
            foreach (CustomerNPC npc in UnityEngine.Object.FindObjectsByType<CustomerNPC>())
            {
                if (npc == null || npc.GetComponent<Understudy>() != null) continue;
                if (seenShoppers.Add(npc)) shoppers++;
                var agent = npc.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent != null && agent.hasPath && !MiniStore.Contains(agent.destination)) wandered++;
                if (shop != null && shop.cashierPoint != null && Vector3.Distance(npc.transform.position, shop.cashierPoint.position) < 2.5f) atTill++;
            }
            yield return null;
        }
        Debug.Log($"MiniStoreCheck: shoppers {shoppers}, frames at the till {atTill}, frames heading outside it {wandered}");
        Expect(shoppers > 0, $"shoppers come in ({shoppers} in 90 s)");
        Expect(wandered == 0, "no shopper heads for the store's own shelves");
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

        // M5: each trick on its own. Only it allowed, a fresh shift, she starts: does she use it
        // herself within the limit? The tester hides in a cold room chiller, door shut, as you
        // might; for the throws, which need her to see you from 8-20 m, they stand in the sales
        // floor's far corner instead. For The overtime the shift is short enough that its last two
        // minutes have begun.
        // Phase 9's interactions with the real Karen (MiniStoreCheck.Interactions.cs).
        yield return Interactions();

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mini-tricks") < 0) yield break;
        var results = new List<string>();
        int used = 0;
        foreach (string id in TricksSandbox.Catalogue)
        {
            MiniStoreSettings.Allowed = MiniStoreSettings.Tricks.OnlyOne;
            MiniStoreSettings.Chosen = id;
            UnityEngine.Object.FindAnyObjectByType<TricksSandbox>()?.RestartNow();
            yield return Wait(3f);
            KarenBrain her = null;
            for (float t = 0f; t < 20f && (her == null || her.Body == null || her.Ctx == null); t += Time.deltaTime)
            {
                her = UnityEngine.Object.FindAnyObjectByType<KarenBrain>();
                yield return null;
            }
            if (her == null || her.Body == null) { results.Add($"{id,-16} no Karen after the restart"); continue; }
            if (her.Shift != null && !her.Shift.IsShiftActive)
            {
                if (id == "overtime") her.Shift.shiftDurationSeconds = 115f;
                her.Shift.StartShift();
            }
            Tester(id.StartsWith("throw_"));
            her.enabled = true;
            string seenWhy = "";
            bool started = false;
            float limit = 60f, at = 0f;
            for (; at < limit; at += Time.deltaTime)
            {
                if (her.CurrentPlan != null && her.CurrentPlan.Tactic != null && her.CurrentPlan.Tactic.Id == id) { started = true; break; }
                if (her.KilledThisShift) { seenWhy = "she caught you first"; break; }
                seenWhy = MiniStorePanel.Readiness(her, id);
                yield return null;
            }
            if (started) used++;
            results.Add(started ? $"{id,-16} used it after {at:0} s" : $"{id,-16} not in {limit:0} s: {seenWhy}");
            Debug.Log("MiniStoreCheck: alone: " + results[results.Count - 1]);
        }
        Debug.Log($"MiniStoreCheck: each trick alone ({used} of {TricksSandbox.Catalogue.Count} used)\n  " + string.Join("\n  ", results));
        MiniStoreSettings.Allowed = before;
    }

    // Where the tester waits for a trick: in a cold room chiller you can hide in, its door shut;
    // or, to be seen from afar, in the sales floor's far corner.
    static void Tester(bool inView)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null || MiniStore.Root == null) return;
        Vector3 to = MiniStore.Half(new Vector3(82f, 0f, -123f));   // the sales floor's far front corner
        FurnitureDoor shut = null;
        if (!inView)
        {
            foreach (HidingPlace place in MiniStore.Root.GetComponentsInChildren<HidingPlace>())
            {
                Vector3 centre = place.transform.TransformPoint(place.interior.center);
                if (StoreMap.AreaAt(centre) != "Cold storage") continue;
                to = new Vector3(centre.x, place.transform.TransformPoint(place.interior.min).y, centre.z);
                shut = place.GetComponentInParent<ColdFixture>()?.GetComponentInChildren<FurnitureDoor>();
                break;
            }
        }
        var controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = to + Vector3.up * 0.05f;
        if (controller != null) controller.enabled = true;
        if (shut != null) shut.SetOpen(false);
        Physics.SyncTransforms();
    }

    // The things her tricks and your interactions need, each inside the mini store.
    static void KeyThings()
    {
        bool In(Component c) => c != null && MiniStore.Contains(c.transform.position);
        int Count<T>() where T : Component => UnityEngine.Object.FindObjectsByType<T>().Count(In);
        Expect(Count<ElectricBox>() == 1 && BreakerPanel.Instance != null, "the power box and its breakers (Blackout)");
        Expect(Count<FogBox>() >= 1, "the fog box (Fog)");
        Expect(Count<PaBox>() >= 1 && Count<ClosingMic>() >= 1, "the PA box and its microphone (Phantom chime, the closing call)");
        Expect(Count<Puncher>() == 1, "the time clock (The overtime)");
        Expect(Count<MusicBox>() == 1, "the radio");
        Expect(Count<CoffeeMachine>() == 1, "the coffee machine");
        Expect(UnityEngine.Object.FindObjectsByType<Item>().Count(i => In(i) && i.type == ItemType.Flashlight) >= 1, "the torch");
        Expect(Count<RoomLightSwitch>() >= 5, $"a light switch per room (Room lights): {Count<RoomLightSwitch>()}");
        StoreCameras cams = StoreCameras.Instance;
        Expect(cams != null && cams.Cameras.Count(In) >= 4 && In(cams.Wall), $"the cameras and their screens (Camera watch, break, Screen loop): {cams?.Cameras.Count(In)} cameras");
        Expect(UnityEngine.Object.FindObjectsByType<ToolSnapPoint>().Count(In) >= 2, "the mops' racks (Tool theft, spills)");
        Expect(Count<TrashContainer>() >= 1 && Count<Trashcan>() >= 3, "the bins and the yard's containers (Bin tamper)");
        Expect(Count<FreezerAlarm>() >= 1, $"freezer alarms (Freezer alarm): {Count<FreezerAlarm>()}");
        Expect(Count<Item>() >= 3 && UnityEngine.Object.FindObjectsByType<Item>().Any(i => In(i) && i.GetComponent<ItemHome>() != null && i.GetComponent<StockCrate>() == null), "the staff mop, on its rack");
        Expect(Count<StockCrate>() >= 1 && Count<ProductBox>() >= 3, $"the stock crate and boxes on the racks (Block door): {Count<StockCrate>()} crates, {Count<ProductBox>()} boxes");
        CustomerSpawner spawner = UnityEngine.Object.FindAnyObjectByType<CustomerSpawner>();
        Expect(spawner != null && In(spawner.entrancePoint) && In(spawner.cashierPoint), "shoppers come in at its front and pay at its till (Ask the customers, Flash sale, The complaint)");
        Expect(Count<AutoDoubleDoor>() == 4, "both pairs of automatic doors (Phantom chime)");
        TimeClockDisplay clock = UnityEngine.Object.FindObjectsByType<TimeClockDisplay>().FirstOrDefault(In);
        Expect(clock != null && clock.Shown.Contains(":"), $"the time clock shows the time ({clock?.Shown})");
        Expect(SceneObjectNames.Find(TimeClockDisplay.WallClockAbove) == null, "the wall clock above the store's time clock is gone");
        // The copied doors keep the store's paint; the bays have a solid back.
        var doors = MiniStore.Root.GetComponentsInChildren<HingeDoor>();
        Expect(doors.All(d => d.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m == null || !m.name.Contains("Prototype")))),
            "the doors wear the store's paint, not the placeholder grid");
        var bays = MiniStore.Root.GetComponentsInChildren<ShelfUnit>().Where(u => u.GetComponent<ColdFixture>() == null && u.name.Contains("shelf")).ToArray();
        Expect(bays.Length > 0 && bays.All(b => b.transform.Find("Back panel") != null || b.GetComponentsInChildren<Transform>().Any(t => t.name == "Back panel")),
            $"every shelf bay has a solid back ({bays.Length} bays)");
        Expect(bays.Length == 23, $"four runs of shelves: 18 behind, 5 in front ({bays.Length})");
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
        // From where you'd stand: your start, and in each room from a corner toward its middle.
        var views = new List<(string name, Vector3 at, Vector3 look)>
        {
            ("start", MiniStore.World(MiniStore.PlayerStart) + Vector3.up * 1.6f, MiniStore.World(MiniStore.PlayerStart) + new Vector3(0f, 1.4f, -6f)),
        };
        foreach (var (name, store, _) in MiniStore.Rooms)
        {
            Vector3 corner = MiniStore.Half(new Vector3(store.xMax - 1.2f, 0f, store.yMax - 1.2f));
            Vector3 middle = MiniStore.Half(new Vector3(store.center.x, 0f, store.center.y));
            views.Add((name.ToLowerInvariant().Replace(' ', '_'), corner + Vector3.up * 1.7f, middle + Vector3.up * 1.2f));
        }
        foreach (var v in views)
        {
            go.transform.SetPositionAndRotation(v.at, Quaternion.LookRotation(v.look - v.at));
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

    // The player put 1.2 m in front of a door, on the side the check aimed from.
    static Vector3 Stand(GameObject player, Bounds door, bool alongZ)
    {
        Vector3 at = new Vector3(door.center.x, 0.1f, door.center.z) + (alongZ ? Vector3.left : Vector3.forward) * 1.2f;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.transform.position = at;
        if (cc != null) cc.enabled = true;
        Physics.SyncTransforms();
        return at;
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null && p.parent != null; p = p.parent) path = p.name + "/" + path;
        return path;
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
