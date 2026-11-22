using System;
using System.Collections.Generic;
using Kehai.Karen;
using Kehai.Sandbox;
using Kehai.Store;
using UnityEditor;
using UnityEngine;

// The sandbox island, opened for real (0.5.2, C11.11): Kehai → Sandbox → Island in Play, then a
// look: the store is quiet (no store Karen, no shift), the island and its sight area are there
// with her body on its NavMesh, she walks when started, and the player is in the store.
//   tools/run/kehai-run unity -- -executeMethod SandboxIslandCheck.Run
// Exit 0 when all hold, 1 when one doesn't, 2 if Play never got there.
// SandboxIslandCheck.Pursuit does the same and then plays the pursuit checks on the island's
// pursuit area, on her test map (C11.11, C11.13).
[InitializeOnLoad]
public static class SandboxIslandCheck
{
    const double Limit = 900;
    static double playingSince = -1;
    static Vector3 startedAt;
    static bool started;

    static SandboxIslandCheck()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!Application.isBatchMode || Array.IndexOf(args, "SandboxIslandCheck.Run") < 0 && Array.IndexOf(args, "SandboxIslandCheck.Pursuit") < 0) return;
        EditorApplication.update += Tick;
    }

    public static void Run() => SandboxIslandMenu.Open();

    public static void Pursuit()
    {
        SessionState.SetBool(SandboxIsland.PursuitChecksKey, true);
        SandboxIslandMenu.Open();
    }

    static bool WithPursuitChecks => SessionState.GetBool(SandboxIsland.PursuitChecksKey, false);

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > Limit) { Debug.LogError("SandboxIslandCheck: timed out"); EditorApplication.Exit(2); return; }
        if (!EditorApplication.isPlaying) return;
        if (playingSince < 0) playingSince = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - playingSince;
        SightSandbox sight = UnityEngine.Object.FindAnyObjectByType<SightSandbox>();
        if (t > 8 && !started && sight != null && sight.Karen != null)
        {
            startedAt = sight.Karen.Body.transform.position;
            sight.Press(KeyCode.F5);
            started = true;
        }
        if (t < 14) return;
        if (WithPursuitChecks && !PursuitChecks.Done) return;

        var fails = new List<string>();
        void Expect(bool ok, string what) { Debug.Log($"SandboxIslandCheck: {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) fails.Add(what); }
        Expect(GameObject.Find(SandboxIsland.RootName) != null, "the sandbox island is built");
        GameObject models = GameObject.Find(SandboxIsland.ModelsIslandName);
        Expect(models != null && models.transform.position.z < -37.32f - 40f, "the models island moved onto the road's edge");
        int brains = 0;
        foreach (KarenBrain b in UnityEngine.Object.FindObjectsByType<KarenBrain>(FindObjectsSortMode.None)) brains++;
        Expect(brains == 0, $"the store is quiet: no store Karen ({brains} brains)");
        ShiftManager shift = UnityEngine.Object.FindAnyObjectByType<ShiftManager>();
        Expect(shift != null && !shift.IsShiftActive, "the store is quiet: no shift running");
        Expect(sight != null && sight.Karen != null, "the sight area stands on the island with its Karen");
        if (sight != null && sight.Karen != null)
        {
            Vector3 at = sight.Karen.Body.transform.position;
            Expect(Vector3.Distance(new Vector3(at.x, 0f, at.z), SandboxIsland.SightAt) < 12f, $"her body is in the sight area ({at})");
            Expect(sight.Karen.Body.Agent != null && sight.Karen.Body.Agent.isOnNavMesh, "her body is on the area's NavMesh");
            Expect(Vector3.Distance(startedAt, at) > 0.5f, $"F5 sets her walking ({Vector3.Distance(startedAt, at):0.0} m)");
        }
        Expect(PlayerPresence.Current != null, "the store's player is there for her to see");
        StoreMap map = StoreMap.Current;
        bool allOnIsland = map.CellCount > 0;
        for (int c = 0; c < map.CellCount && allOnIsland; c++) allOnIsland = SandboxIsland.MapBounds.Contains(new Vector3(map.CellPosition[c].x, 2f, map.CellPosition[c].z));
        Expect(StoreMap.TestArea != null && allOnIsland, $"her test map covers the island only ({map.CellCount} cells)");
        PursuitSandbox chase = UnityEngine.Object.FindAnyObjectByType<PursuitSandbox>();
        Expect(chase != null && chase.Ready && chase.Brain != null && chase.Brain.Body.Agent.isOnNavMesh, "the pursuit area stands on the island with her brain on its NavMesh");
        if (WithPursuitChecks)
        {
            Expect(PursuitChecks.ExitCode == 0, $"the pursuit checks pass on the island (report {PursuitChecks.ReportPath})");
            SessionState.EraseBool(SandboxIsland.PursuitChecksKey);
        }
        Debug.Log($"SandboxIslandCheck: {(fails.Count == 0 ? "all passed" : fails.Count + " failed")}");
        EditorApplication.Exit(fails.Count == 0 ? 0 : 1);
    }
}
