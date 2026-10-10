using System.IO;
using UnityEngine;

namespace Kehai.Replay
{
    // How a recorded shift is opened for watching. Every way in loads the store with a replay
    // pending, and KarenBootstrap then puts a ReplayPlayer into it instead of Karen:
    //   -replay <file.krec>      on the command line (a build, or the editor in batch mode)
    //   -krec <file.krec> …      the same, rendering a shot unattended (ReplayRender)
    //   [R] on the review screen after a shift, for the shift just played
    //   Kehai → Replay in the editor's menu
    // Leaving (Backspace) loads the store again as a game, with the career where it was.
    public static class ReplayMode
    {
        // The editor menu hands the file over through SessionState: statics don't survive
        // entering play mode.
        public const string SessionKey = "Kehai.Replay.Pending";

        static string pending;
        static bool commandLineRead;
        static int returnTo = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pending = null;
            commandLineRead = false;
            returnTo = -1;
        }

        // True when this run renders a shot and quits (`-krec` on the command line).
        public static bool Rendering => Arg("-krec") != null;

        // The recording the store should open as, if any. Asking takes it: the next load of the
        // store is a game again.
        public static string TakePending()
        {
            if (!commandLineRead)
            {
                commandLineRead = true;
                string file = Arg("-krec") ?? Arg("-replay");
                if (!string.IsNullOrEmpty(file)) pending = file;
            }
#if UNITY_EDITOR
            string handed = UnityEditor.SessionState.GetString(SessionKey, "");
            if (handed.Length > 0)
            {
                UnityEditor.SessionState.EraseString(SessionKey);
                pending = handed;
            }
#endif
            string path = pending;
            pending = null;
            return path;
        }

        // Loads the recorded shift's store and watches it.
        public static void Open(string krec)
        {
            if (string.IsNullOrEmpty(krec) || !File.Exists(krec))
            {
                Debug.LogWarning("No replay to open: " + krec);
                return;
            }
            returnTo = ShiftRestart.CompletedShifts();
            pending = krec;
            ShiftRestart.Reload(-1, SceneOf(krec));
        }

        // Back to the game, with as many shifts done as there were before the replay.
        public static void Leave()
        {
            int back = returnTo;
            returnTo = -1;
            ShiftRestart.Reload(back);
        }

        // The scene the shift was recorded in, if the file says and that scene can be loaded.
        public static string SceneOf(string krec)
        {
            try
            {
                string scene = KrecReader.LoadHeader(krec).Scene;
                return string.IsNullOrEmpty(scene) ? null : scene;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Couldn't read {krec}: {e.Message}");
                return null;
            }
        }

        public static string Arg(string key)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }

        public static bool Flag(string key) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), key) >= 0;
    }
}
