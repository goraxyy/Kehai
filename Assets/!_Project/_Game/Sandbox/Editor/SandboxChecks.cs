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
        if (!Application.isBatchMode || Array.IndexOf(Environment.GetCommandLineArgs(), "SandboxChecks.Sight") < 0) return;
        EditorApplication.update += () =>
        {
            if (SightChecks.Done)
            {
                Debug.Log("SandboxChecks: finished, report " + SightChecks.ReportPath);
                EditorApplication.Exit(SightChecks.ExitCode);
            }
            else if (EditorApplication.timeSinceStartup > TimeLimitSeconds)
            {
                Debug.LogError("SandboxChecks: timed out");
                EditorApplication.Exit(2);
            }
        };
    }

    [MenuItem("Kehai/Sandbox/Run Sight Checks")]
    public static void Sight()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Sight sandbox (checks)");
        go.AddComponent<SightSandbox>();
        go.AddComponent<SightChecks>();
        EditorApplication.EnterPlaymode();
    }
}
