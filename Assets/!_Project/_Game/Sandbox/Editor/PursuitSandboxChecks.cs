using System;
using Kehai.Replay;
using Kehai.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// tools/run/kehai-run unity -- -executeMethod PursuitSandboxChecks.Run
[InitializeOnLoad]
public static class PursuitSandboxChecks
{
    const double TimeLimitSeconds = 600;
    const string RunMethod = "PursuitSandboxChecks.Run";

    static PursuitSandboxChecks()
    {
        if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), RunMethod) < 0) return;
        EditorApplication.update += WaitForChecks;
    }

    static void WaitForChecks()
    {
        if (EditorApplication.isPlaying && PursuitChecks.Done)
        {
            Debug.Log("PursuitSandboxChecks: finished, report " + PursuitChecks.ReportPath);
            EditorApplication.Exit(PursuitChecks.ExitCode);
        }
        else if (EditorApplication.timeSinceStartup > TimeLimitSeconds)
        {
            Debug.LogError("PursuitSandboxChecks: timed out before the report finished");
            EditorApplication.Exit(2);
        }
    }

    [MenuItem("Kehai/Sandbox/Run Pursuit Checks")]
    public static void Run()
    {
        if (!OpenEmptyScene()) return;
        new GameObject("Pursuit sandbox (checks)").AddComponent<PursuitChecks>();
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Kehai/Sandbox/Watch Pursuit Check Replay")]
    public static void Watch() => ChooseReplay(PursuitCheckPlayback.SceneId);

    [MenuItem("Kehai/Sandbox/Watch Sight Check Replay")]
    public static void WatchSight() => ChooseReplay(SightSandbox.ReplayScene);

    static void ChooseReplay(string scene)
    {
        string folder = System.IO.Path.Combine(Application.persistentDataPath, "sandbox_checks");
        string path = EditorUtility.OpenFilePanel("Open a " + scene + " replay", folder, "krec");
        if (path.Length > 0) OpenReplay(path, scene);
    }

    [MenuItem("Kehai/Sandbox/Run Pursuit Checks", true)]
    [MenuItem("Kehai/Sandbox/Watch Pursuit Check Replay", true)]
    [MenuItem("Kehai/Sandbox/Watch Sight Check Replay", true)]
    static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // Run with graphics enabled and the replay renderer's -krec/-from/-to/-out flags.
    public static void Render() => OpenReplay(ReplayMode.Arg("-krec"));

    static void OpenReplay(string path, string expected = null)
    {
        string scene = string.IsNullOrEmpty(path) ? null : ReplayMode.SceneOf(path);
        if ((scene != SightSandbox.ReplayScene && scene != PursuitCheckPlayback.SceneId) || (expected != null && scene != expected))
        {
            Debug.LogError("PursuitSandboxChecks: choose a recording from the requested sandbox");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }
        if (!OpenEmptyScene()) return;
        new GameObject(scene + " (replay)").AddComponent<PursuitCheckPlayback>().recording = path;
        EditorApplication.EnterPlaymode();
    }

    static bool OpenEmptyScene()
    {
        if (!CanOpen()) return false;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return true;
    }
}
