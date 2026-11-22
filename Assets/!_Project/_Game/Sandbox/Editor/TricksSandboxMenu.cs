using Kehai.Playtest;
using Kehai.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Only the Play-mode instance of the private store contains the stalls. Unity restores the
// open editing scenes when Play ends, including unsaved work in other scenes.
[InitializeOnLoad]
public static class TricksSandboxMenu
{
    const string Menu = "Kehai/Sandbox/Tricks";
    const string Key = TricksSandbox.SessionKey;

    static TricksSandboxMenu() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem(Menu, true)]
    static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(Menu)]
    public static void Open()
    {
        if (!CanOpen()) return;
        var store = AssetDatabase.LoadAssetAtPath<SceneAsset>(TricksSandbox.StoreScene);
        if (store == null)
        {
            Debug.LogError("Tricks sandbox needs the private store at " + TricksSandbox.StoreScene);
            return;
        }
        if (PlaytestConfig.Active)
        {
            Debug.LogError("Turn off playtest-session recording before opening the private tricks sandbox.");
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
        // A recompile during the session would discard the private career override.
        if (state == PlayModeStateChange.EnteredPlayMode) EditorApplication.LockReloadAssemblies();
        if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.UnlockReloadAssemblies();
        if (state != PlayModeStateChange.EnteredEditMode) return;
        TricksSandbox.EndSession();
        string previous = SessionState.GetString(Key + ".scene", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        EditorUtility.audioMasterMute = SessionState.GetBool(Key + ".mute", true);
        SessionState.EraseBool(Key);
        SessionState.EraseString(Key + ".scene");
        SessionState.EraseBool(Key + ".mute");
    }
}
