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
    // Out of the menu since the mini store (24 November): the automatic checks still open these.
    public static void OpenSight() => Open<SightSandbox>("Sight sandbox");

    public static void OpenPursuit() => Open<PursuitSandbox>("Pursuit sandbox");

    public static void OpenInteractions() => Open<InteractionSandbox>("Interactions sandbox");

    static void Open<T>(string name) where T : Component
    {
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject(name).AddComponent<T>();
        EditorApplication.EnterPlaymode();
    }
}
