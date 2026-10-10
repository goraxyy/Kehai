using System.IO;
using System.Linq;
using Kehai.Karen;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Kehai.Replay
{
    // Kehai → Replay: watch a recorded shift in the editor.
    public static class ReplayMenu
    {
        [MenuItem("Kehai/Replay/Open Latest Shift")]
        static void OpenLatest()
        {
            string latest = Directory.Exists(ShiftRecorder.Folder)
                ? Directory.GetFiles(ShiftRecorder.Folder, "*.krec").OrderBy(File.GetLastWriteTimeUtc).LastOrDefault()
                : null;
            if (latest == null)
            {
                EditorUtility.DisplayDialog("No recorded shifts", "Play a shift first: every shift is recorded to\n" + ShiftRecorder.Folder, "OK");
                return;
            }
            Watch(latest);
        }

        [MenuItem("Kehai/Replay/Open Shift…")]
        static void Open()
        {
            string path = EditorUtility.OpenFilePanel("Watch a recorded shift", ShiftRecorder.Folder, "krec");
            if (!string.IsNullOrEmpty(path)) Watch(path);
        }

        [MenuItem("Kehai/Replay/Show Recorded Shifts")]
        static void Reveal()
        {
            Directory.CreateDirectory(ShiftRecorder.Folder);
            EditorUtility.RevealInFinder(ShiftRecorder.Folder);
        }

        static void Watch(string krec)
        {
            if (EditorApplication.isPlaying)
            {
                ReplayMode.Open(krec);
                return;
            }
            string scene = ReplayMode.SceneOf(krec);
            if (!string.IsNullOrEmpty(scene) && File.Exists(scene) && EditorSceneManager.GetActiveScene().path != scene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            }
            SessionState.SetString(ReplayMode.SessionKey, krec);
            EditorApplication.EnterPlaymode();
        }
    }
}
