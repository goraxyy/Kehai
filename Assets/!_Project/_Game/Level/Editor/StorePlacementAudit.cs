using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kehai.Karen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

// tools/run/kehai-run unity -- -noaudio -executeMethod StorePlacementAudit.Run
// Runs before clock-in on the safe clone. The batch exits after writing its TSV to kehai_eval.
[InitializeOnLoad]
public static class StorePlacementAudit
{
    const string Pending = "Kehai.StorePlacementAudit";
    const string StoreScene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";

    static StreamWriter report;
    static string reportPath;
    static string[] layouts;
    static string layout;
    static int nextLayout, readyFrame, rows, findings;
    static bool missingEvidence;

    static StorePlacementAudit()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                layout = null;
                nextLayout = rows = findings = 0;
                missingEvidence = false;
                readyFrame = Time.frameCount + 2; // Let Start install doors and other runtime fixtures.
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
                Finish(new InvalidOperationException("Play mode ended before the placement audit finished."));
        };
    }

    public static void Run()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run placement checks in a batch editor with the safe clone.");
        try
        {
            EditorUtility.audioMasterMute = true;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StoreScene) == null)
                throw new FileNotFoundException("Private store scene missing", StoreScene);
            EditorSceneManager.OpenScene(StoreScene, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying || Time.frameCount < readyFrame) return;
            if (report == null)
            {
                OpenReport();
                GamePause.Set(true);
                var shift = Object.FindAnyObjectByType<ShiftManager>();
                if (shift == null || shift.IsShiftActive || StoreLayoutRuntime.CurrentId != "today")
                    throw new InvalidOperationException("Expected the authored store before clock-in.");
                layouts = new[] { "today" }.Concat(StoreLayouts.All.Select(item => item.Id)).ToArray();
                // -placement-layouts today,rings: only those (for iterating on one layout).
                string[] only = LayoutsArg();
                if (only != null) layouts = layouts.Where(only.Contains).ToArray();
            }

            if (layout == null)
            {
                if (nextLayout == layouts.Length)
                {
                    Finish(null);
                    return;
                }
                layout = layouts[nextLayout++];
                StoreLayoutRuntime.PreparePlacementAudit(layout);
                readyFrame = Time.frameCount + 2; // Let old bays be destroyed and carving catch up.
                return;
            }

            InspectLayout();
            report.Flush();
            layout = null;
        }
        catch (Exception error) { Finish(error); }
    }

    static string[] LayoutsArg()
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-placement-layouts");
        return i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
    }

    static void OpenReport()
    {
        string folder = Path.Combine(Application.persistentDataPath, "kehai_eval");
        Directory.CreateDirectory(folder);
        reportPath = Path.Combine(folder, $"placement_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.tsv");
        report = new StreamWriter(reportPath);
        report.WriteLine("layout\ttarget\ttype\theight_m\theight_status\tstanding_approach\tstanding_xyz\tprompt_status\tprompt\tevidence");
    }

    static void InspectLayout()
    {
        var player = Object.FindAnyObjectByType<PlayerInteract>();
        var surface = KarenWorld.StoreSurface();
        var probe = new PlacementProbe(player, surface);
        if (!probe.HasNavigation)
            Missing("Store", "No player, capsule, or walkable NavMesh at the player spawn");

        var behaviours = Object.FindObjectsByType<MonoBehaviour>();
        var targets = behaviours.Where(b => b.isActiveAndEnabled && b is IInteractable)
                     .OrderBy(b => PathOf(b.transform), StringComparer.Ordinal)
                     .ThenBy(b => b.GetType().Name, StringComparer.Ordinal);
        foreach (var group in targets.GroupBy(StorageCabinet))
        {
            FurnitureDoor door = group.Key != null ? group.Key.GetComponentInChildren<FurnitureDoor>() : null;
            if (TryOpen(door, probe, evidence => WriteTargets(group, probe, evidence))) continue;
            WriteTargets(group, probe, group.Key == null ? null : "Measured as found; no reachable cabinet door was established.");
        }

        foreach (var door in behaviours.OfType<AutoDoubleDoor>().Where(d => d.isActiveAndEnabled))
            foreach (var handle in door.Handles)
                if (handle != null && !handle.gameObject.activeSelf)
                    Write(PathOf(handle.transform), nameof(AutoDoorHandle), probe.Check(handle));

        foreach (var door in behaviours.OfType<HingeDoor>().Where(d => d.isActiveAndEnabled && d.ListensForE))
            Write(PathOf(door.transform), nameof(HingeDoor), probe.CheckNearbyDoor(door));

        foreach (var bay in ShelfSlot.All.GroupBy(slot => slot.owner))
        {
            var cold = bay.Key != null ? bay.Key.GetComponent<ColdFixture>() : null;
            var doors = cold != null && cold.retail ? cold.GetComponentsInChildren<FurnitureDoor>() : Array.Empty<FurnitureDoor>();
            // A chiller has a glass door per section: each slot is measured with its own door open,
            // as you'd open it (opening the first one only had it swing across its neighbours').
            foreach (var behind in bay.GroupBy(slot => DoorFor(slot, doors)))
                if (!TryOpen(behind.Key, probe, evidence => WriteSlots(behind, probe, evidence)))
                    WriteSlots(behind, probe, cold != null && cold.retail
                        ? "Measured as found; no reachable chiller door was established." : null);
        }

        foreach (Type type in new[] { typeof(ElectricBox), typeof(BreakerSwitch), typeof(FogBox), typeof(CameraBox),
                     typeof(PaBox), typeof(ClosingMic), typeof(Puncher), typeof(RoomLightSwitch), typeof(MusicBox),
                     typeof(CoffeeMachine), typeof(Trashcan), typeof(ToolSnapPoint) })
        {
            int count = behaviours.Count(b => b.isActiveAndEnabled && type.IsInstanceOfType(b));
            int expected = type == typeof(BreakerSwitch) ? 3 : 1;
            if (count < expected) Missing(type.Name, $"Expected at least {expected} active, found {count}");
        }
        if (ShelfSlot.All.Count == 0) Missing(nameof(ShelfSlot), "The planogram produced no stock slots");
        if (behaviours.OfType<AutoDoubleDoor>().Any(d => d.isActiveAndEnabled && d.Handles.Count == 0))
            Missing(nameof(AutoDoorHandle), "A sliding door produced no hand controls");

        Debug.Log($"Placement audit {layout}: {rows} rows and {findings} findings so far.");
    }

    static ColdFixture StorageCabinet(MonoBehaviour target) => target switch
    {
        PickupInteractable pickup => pickup.GetComponent<ProductBox>()?.cabinet,
        ToolSnapPoint snap when snap.tool != null => snap.tool.GetComponent<ProductBox>()?.cabinet,
        _ => null
    };

    static FurnitureDoor DoorFor(ShelfSlot slot, FurnitureDoor[] doors)
    {
        FurnitureDoor best = null;
        float bestDistance = float.MaxValue;
        Vector3 at = slot.Position;
        foreach (var door in doors)
        {
            if (door == null || door.panel == null) continue;
            Vector3 d = door.panel.bounds.center - at;
            d.y = 0f;
            if (d.sqrMagnitude < bestDistance) { bestDistance = d.sqrMagnitude; best = door; }
        }
        return best;
    }

    static bool TryOpen(FurnitureDoor door, PlacementProbe probe, Action<string> inspect)
    {
        if (door == null || !door.isActiveAndEnabled || door.panel == null || door.roomDoor != null ||
            probe.Check(door).Standing is not Vector3 opener) return false;
        door.InspectOpenPose(opener, () => inspect(door.IsFullyOpen
            ? "Measured after opening the reachable cabinet door."
            : "Measured with the reachable cabinet door at its swing limit."));
        return true;
    }

    static void WriteTargets(IEnumerable<MonoBehaviour> targets, PlacementProbe probe, string evidence)
    {
        foreach (var target in targets)
        {
            var result = probe.Check(target);
            if (evidence != null)
                result.Evidence = string.IsNullOrEmpty(result.Evidence) ? evidence : evidence + " " + result.Evidence;
            Write(PathOf(target.transform), target.GetType().Name, result);
        }
    }

    static void WriteSlots(IEnumerable<ShelfSlot> slots, PlacementProbe probe, string doorEvidence)
    {
        foreach (var slot in slots)
        {
            var result = probe.CheckSlot(slot);
            if (result.Approach != "reachable" && slot.frame != null)
                result.Evidence = (string.IsNullOrEmpty(result.Evidence) ? "" : result.Evidence + " ") +
                    $"Slot at {slot.Position.ToString("F2")} facing {slot.Outward.ToString("F2")} ({slot.side}).";
            if (doorEvidence != null)
                result.Evidence = string.IsNullOrEmpty(result.Evidence) ? doorEvidence : doorEvidence + " " + result.Evidence;
            Write($"{PathOf(slot.frame)}/slot {slot.Index}", nameof(ShelfSlot), result);
        }
    }

    static void Missing(string target, string detail) => Write(target, "", new PlacementProbe.Result
    {
        Evidence = detail,
        PromptStatus = "unmeasured"
    });

    static void Write(string target, string type, PlacementProbe.Result result)
    {
        rows++;
        if (result.HasFinding) findings++;
        missingEvidence |= result.MissingEvidence;
        var standing = result.Standing;
        report.WriteLine(string.Join("\t", layout, Cell(target), type,
            result.Height?.ToString("0.0000", CultureInfo.InvariantCulture) ?? "",
            result.HeightStatus, result.Approach ?? "unmeasured",
            standing.HasValue ? standing.Value.ToString("F3") : "",
            result.PromptStatus, Cell(result.Prompt), Cell(result.Evidence)));
    }

    static string PathOf(Transform at)
    {
        if (at == null) return "(missing transform)";
        return at.parent == null ? at.name : PathOf(at.parent) + "/" + at.name;
    }

    static string Cell(string value) => value?.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ") ?? "";

    static void Finish(Exception error)
    {
        SessionState.EraseBool(Pending);
        EditorApplication.update -= Tick;
        try
        {
            if (error != null)
            {
                if (report == null) OpenReport();
                Missing("Audit", error.Message);
            }
        }
        finally
        {
            report?.Dispose();
            report = null;
            if (error != null) Debug.LogException(error);
            Debug.Log($"Placement audit: {rows} rows, {findings} findings, report {reportPath}");
            EditorApplication.Exit(error != null || missingEvidence ? 2 : findings > 0 ? 1 : 0);
        }
    }
}
