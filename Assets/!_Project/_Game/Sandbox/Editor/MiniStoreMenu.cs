using Kehai.Sandbox;
using UnityEditor;

// Kehai → Sandbox → Mini store (0.5.2, C11.17): Play in the store scene on a private shift four
// (the tricks sandbox's: your save is never read or written, a catch freezes everything), with
// the mini store built beside the store and you inside it. Muted; the editor's scenes and sound
// come back as they were when Play ends.
[InitializeOnLoad]
public static class MiniStoreMenu
{
    const string Menu = "Kehai/Sandbox/Mini store";

    static MiniStoreMenu() => EditorApplication.playModeStateChanged += Changed;

    [MenuItem(Menu, true)]
    static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem(Menu, false, -10)]
    public static void Open()
    {
        if (!CanOpen()) return;
        SessionState.SetBool(MiniStore.SessionKey, true);
        TricksSandboxMenu.Open();
        // Cancelled (a scene not saved, say): no session.
        if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.EraseBool(MiniStore.SessionKey);
    }

    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(MiniStore.SessionKey, false)) return;
        MiniStore.EndSession();
        SessionState.EraseBool(MiniStore.SessionKey);
    }
}
