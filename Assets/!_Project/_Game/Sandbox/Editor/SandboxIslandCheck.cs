using System;
using System.Collections;
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
// SandboxIslandCheck.Walk drives the real store player from the lobby through the island rooms.
// SandboxIslandCheck.Interactions plays the interaction checks in the island's interactions room.
[InitializeOnLoad]
public static class SandboxIslandCheck
{
    const double Limit = 900;
    static double playingSince = -1;
    static Vector3 startedAt;
    static bool started;
    static readonly bool withWalk;
    static IEnumerator walk;
    static int walkFrame;

    static SandboxIslandCheck()
    {
        string[] args = Environment.GetCommandLineArgs();
        withWalk = Array.IndexOf(args, "SandboxIslandCheck.Walk") >= 0;
        if (!Application.isBatchMode || !withWalk && Array.IndexOf(args, "SandboxIslandCheck.Run") < 0 &&
            Array.IndexOf(args, "SandboxIslandCheck.Pursuit") < 0 && Array.IndexOf(args, "SandboxIslandCheck.Interactions") < 0) return;
        EditorApplication.update += Tick;
    }

    public static void Run() => SandboxIslandMenu.Open();
    public static void Walk() => SandboxIslandMenu.Open();

    public static void Pursuit()
    {
        SessionState.SetBool(SandboxIsland.PursuitChecksKey, true);
        SandboxIslandMenu.Open();
    }

    // The interaction checks played in the island's interactions room (C11.9 on the island).
    public static void Interactions()
    {
        SessionState.SetBool(SandboxIsland.InteractionChecksKey, true);
        SandboxIslandMenu.Open();
    }

    static bool WithPursuitChecks => SessionState.GetBool(SandboxIsland.PursuitChecksKey, false);
    static bool WithInteractionChecks => SessionState.GetBool(SandboxIsland.InteractionChecksKey, false);

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > Limit) { Debug.LogError("SandboxIslandCheck: timed out"); EditorApplication.Exit(2); return; }
        if (!EditorApplication.isPlaying) return;
        if (walk != null)
        {
            if (walkFrame == Time.frameCount) return;
            walkFrame = Time.frameCount;
            walk.MoveNext();
            return;
        }
        if (playingSince < 0) playingSince = EditorApplication.timeSinceStartup;
        double t = EditorApplication.timeSinceStartup - playingSince;
        GameObject island = GameObject.Find(SandboxIsland.RootName);
        SightSandbox sight = island == null ? null : Array.Find(island.GetComponentsInChildren<SightSandbox>(),
            room => room.GetComponent<InteractionSandbox>() == null);
        if (t > 8 && !started && sight != null && sight.Karen != null)
        {
            startedAt = sight.Karen.Body.transform.position;
            sight.Press(KeyCode.F5);
            started = true;
        }
        if (t < 14) return;
        if (WithPursuitChecks && !PursuitChecks.Done) return;
        if (WithInteractionChecks && !InteractionChecks.Done) return;

        var fails = new List<string>();
        void Expect(bool ok, string what) { Debug.Log($"SandboxIslandCheck: {(ok ? "PASS" : "FAIL")} {what}"); if (!ok) fails.Add(what); }
        Expect(island != null, "the sandbox island is built");
        GameObject models = GameObject.Find(SandboxIsland.ModelsIslandName);
        float floor = float.MaxValue;   // its contents move; its root holds the store's NavMesh volume and stays (C8.1)
        if (models != null) foreach (Collider c in models.GetComponentsInChildren<Collider>()) floor = Mathf.Min(floor, c.bounds.min.z);
        Expect(models != null && floor < -40f, $"the models island moved onto the road's edge (its floor starts at z {floor:0.0})");
        int brains = 0;
        foreach (KarenBrain b in UnityEngine.Object.FindObjectsByType<KarenBrain>())
            if (island == null || !b.transform.IsChildOf(island.transform)) brains++;   // the island's own Karens don't count
        Expect(brains == 0, $"the store is quiet: no store Karen ({brains} off the island)");
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
        InteractionSandbox interactions = UnityEngine.Object.FindAnyObjectByType<InteractionSandbox>();
        Expect(interactions != null && interactions.Ready, "the interactions area stands on the island with its camera and shopper");
        if (WithPursuitChecks)
        {
            PursuitSandboxChecks.LogMapGrid("SandboxIslandCheck");
            Vector3 grid = map.Bounds.min - PursuitSandbox.Centre;
            Expect(Mathf.Abs(SandboxIsland.GridNudge(grid.x, SandboxIsland.PursuitGrid.x)) < 0.01f &&
                   Mathf.Abs(SandboxIsland.GridNudge(grid.z, SandboxIsland.PursuitGrid.y)) < 0.01f,
                $"her island map is cut on the standalone pursuit room's grid ({grid.x:0.00}, {grid.z:0.00})");
            Expect(PursuitChecks.ExitCode == 0, $"the pursuit checks pass on the island (report {PursuitChecks.ReportPath})");
            SessionState.EraseBool(SandboxIsland.PursuitChecksKey);
        }
        if (WithInteractionChecks)
        {
            Expect(InteractionChecks.ExitCode == 0, $"the interaction checks pass on the island (report {InteractionChecks.ReportPath})");
            SessionState.EraseBool(SandboxIsland.InteractionChecksKey);
        }
        if (withWalk && fails.Count == 0)
        {
            PlayerMotor motor = PlayerPresence.Current.GetComponent<PlayerMotor>();
            SandboxDoor pursuitDoor = chase.transform.Find("Door")?.GetComponent<SandboxDoor>();
            if (motor != null && motor.GetComponent<CharacterController>() != null && pursuitDoor != null)
            {
                sight.Karen.Freeze();
                walk = WalkRoutes(motor, sight, interactions.Room, pursuitDoor);
                return;
            }
            Expect(false, "the walk needs the store player controller and pursuit room door");
        }
        Debug.Log($"SandboxIslandCheck: {(fails.Count == 0 ? "all passed" : fails.Count + " failed")}");
        EditorApplication.Exit(fails.Count == 0 ? 0 : 1);
    }

    static Vector3 At(float x, float z) => new Vector3(x, 0f, z);

    static IEnumerator WalkRoutes(PlayerMotor motor, SightSandbox sight, SightSandbox interactions, SandboxDoor pursuitDoor)
    {
        Vector3 sightDoor = sight.transform.TransformPoint(SightSandbox.PlayerStart);
        Vector3 interactionsDoor = interactions.transform.TransformPoint(SightSandbox.PlayerStart);
        Vector3 pursuitFront = PursuitSandbox.World(PursuitSandbox.PlayerStart);
        var routes = new (string name, Vector3[] waypoints)[]
        {
            ("lobby to public road", new[] { At(42.5f, -105f), At(42.5f, -47f) }),
            ("road to the trick stalls' approach", new[] { At(-3f, -47f), At(-25f, -43f) }),
            ("sight room", new[] { At(sightDoor.x, -43f), sightDoor, sightDoor + Vector3.forward * 5f, sightDoor }),
            ("interactions room", new[] { At(-70f, sightDoor.z), At(-70f, interactionsDoor.z), interactionsDoor,
                interactionsDoor + Vector3.forward * 5f, interactionsDoor }),
            ("pursuit room", new[] { At(-49f, -10f), pursuitFront, pursuitFront + Vector3.forward * 5f, pursuitFront }),
            ("sandbox island to models island", new[] { At(-21f, -10f), At(-10f, -20f), At(10f, -20f) })
        };

        sight.Door.Set(true, instantly: true);
        interactions.Door.Set(true, instantly: true);
        pursuitDoor.Set(true, instantly: true);
        IMotorInput previousInput = motor.externalInput;
        float previousStep = Time.captureDeltaTime;
        var bot = new SandboxBot(motor);
        SandboxBot.FixFrames();
        bot.Teleport(At(42.5f, -114f));
        try
        {
            foreach (var (name, waypoints) in routes)
            {
                foreach (Vector3 waypoint in waypoints)
                {
                    float timeout = Vector3.Distance(bot.Feet, waypoint) / Mathf.Max(1f, motor.walkSpeed) + 5f;
                    IEnumerator leg = bot.WalkTo(waypoint, timeout);
                    while (leg.MoveNext() && bot.Feet.y > -1f) yield return null;
                    Vector3 error = bot.Feet - waypoint;
                    if (Mathf.Abs(error.y) > 0.6f || new Vector2(error.x, error.z).sqrMagnitude > 0.25f * 0.25f)
                    {
                        Debug.LogError($"SandboxIslandCheck: FAIL {name}: stopped at {bot.Feet} on the way to {waypoint}");
                        EditorApplication.Exit(1);
                        yield break;
                    }
                }
                Debug.Log($"SandboxIslandCheck: PASS walked {name}");
            }
            EditorApplication.Exit(0);
        }
        finally
        {
            motor.externalInput = previousInput;
            Time.captureDeltaTime = previousStep;
        }
    }
}
