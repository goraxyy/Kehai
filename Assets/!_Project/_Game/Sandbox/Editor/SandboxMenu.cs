using Kehai.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai → Sandbox (0.5.2, Phase 11): opens a sandbox in a new, unsaved scene and enters Play.
// The scene holds one object; everything else is built by code when Play starts, so nothing is
// saved and nothing needs to go into git but this code. Your store scene is offered for saving
// first, and reopens as usual when you open it again.
public static class SandboxMenu
{
    [MenuItem("Kehai/Sandbox/Sight")]
    public static void OpenSight()
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Sight sandbox").AddComponent<SightSandbox>();
        EditorApplication.EnterPlaymode();
    }
}
