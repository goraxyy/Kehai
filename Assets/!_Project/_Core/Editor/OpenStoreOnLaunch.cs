using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// When the editor starts on an empty, unsaved scene, it opens the store instead. The editor
// remembers the open scene in Library/, and forgot it when the project moved on 2026-09-30:
// from then on it started on "Untitled", where Play shows nothing but the sky. Once per
// editor session, so a new scene made on purpose stays open.
[InitializeOnLoad]
static class OpenStoreOnLaunch
{
    const string DoneKey = "Kehai.OpenStoreOnLaunch.Done";

    static OpenStoreOnLaunch()
    {
        if (Application.isBatchMode || SessionState.GetBool(DoneKey, false)) return;
        SessionState.SetBool(DoneKey, true);
        EditorApplication.delayCall += OpenIfEmpty;
    }

    static void OpenIfEmpty()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.sceneCount != 1) return;
        Scene scene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(scene.path) || scene.isDirty) return;

        string store = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).FirstOrDefault();
        if (store == null) return;
        EditorSceneManager.OpenScene(store, OpenSceneMode.Single);
        Debug.Log($"Opened {store}: the editor had started on an empty, unsaved scene.");
    }
}
