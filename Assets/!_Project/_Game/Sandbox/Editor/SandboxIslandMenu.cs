using Kehai.Playtest;
using Kehai.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kehai → Sandbox → Island (0.5.2, C11.11): Play in the store scene with a quiet store and the
// sandbox island's areas. Muted, as every Play of ours is; the editor's scenes come back as they
// were when Play ends.
[InitializeOnLoad]
public static class SandboxIslandMenu
{
    const string Menu = "Kehai/Sandbox/Island";
    const string Key = SandboxIsland.SessionKey;

    static SandboxIslandMenu() => EditorApplication.playModeStateChanged += Changed;

    // Out of the menu since the mini store replaced the island (24 November); its checks still open it.
    static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

    public static void Open()
    {
        if (!CanOpen()) return;
        var store = AssetDatabase.LoadAssetAtPath<SceneAsset>(SandboxIsland.StoreScene);
        if (store == null)
        {
            Debug.LogError("The sandbox island needs the store scene at " + SandboxIsland.StoreScene);
            return;
        }
        if (PlaytestConfig.Active)
        {
            Debug.LogError("Turn off playtest-session recording before opening the sandbox island.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetString(Key + ".scene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key + ".mute", EditorUtility.audioMasterMute);
        SessionState.SetBool(Key, true);
        EditorSceneManager.playModeStartScene = store;
        EditorUtility.audioMasterMute = true;
        EditorApplication.EnterPlaymode();
    }

    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state != PlayModeStateChange.EnteredEditMode) return;
        SandboxIsland.EndSession();
        string previous = SessionState.GetString(Key + ".scene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        EditorUtility.audioMasterMute = SessionState.GetBool(Key + ".mute", true);
        SessionState.EraseBool(Key);
        SessionState.EraseString(Key + ".scene");
        SessionState.EraseBool(Key + ".mute");
    }
}
