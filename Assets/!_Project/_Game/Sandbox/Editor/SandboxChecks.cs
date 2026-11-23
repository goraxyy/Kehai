using System;
using Kehai.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs a sandbox's bot checks (0.5.2, C11.6): Kehai → Sandbox → Run Sight Checks, or headless:
//   Unity -batchmode -nographics -projectPath <clone> -executeMethod SandboxChecks.Sight
// (tools/run/kehai-run unity -- -executeMethod SandboxChecks.Sight does it on a safe clone).
// Headless, the editor quits when the checks finish: exit 0 if all passed, 1 if any failed, 2 if
// they never finished. Entering Play reloads the scripts, so the wait is set up again here.
[InitializeOnLoad]
public static class SandboxChecks
{
    const double TimeLimitSeconds = 600;

    static SandboxChecks()
    {
        if (!Application.isBatchMode) return;
        string[] args = Environment.GetCommandLineArgs();
        bool interactions = Array.IndexOf(args, "SandboxChecks.Interactions") >= 0;
        if (!interactions && Array.IndexOf(args, "SandboxChecks.Sight") < 0) return;
        EditorApplication.update += () =>
        {
            if (interactions ? InteractionChecks.Done : SightChecks.Done)
            {
                Debug.Log("SandboxChecks: finished, report " + (interactions ? InteractionChecks.ReportPath : SightChecks.ReportPath));
                EditorApplication.Exit(interactions ? InteractionChecks.ExitCode : SightChecks.ExitCode);
            }
            else if (EditorApplication.timeSinceStartup > TimeLimitSeconds)
            {
                Debug.LogError("SandboxChecks: timed out");
                EditorApplication.Exit(2);
            }
        };
    }

    [MenuItem("Kehai/Sandbox/Run Sight Checks")]
    public static void Sight() => Open<SightChecks>("Sight sandbox (checks)");

    [MenuItem("Kehai/Sandbox/Run Interaction Checks")]
    public static void Interactions() => Open<InteractionChecks>("Interactions sandbox (checks)");

    static void Open<T>(string name) where T : Component
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorUtility.audioMasterMute = true;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject(name).AddComponent<T>();
        EditorApplication.EnterPlaymode();
    }
}
