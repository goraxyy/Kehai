using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// 0.5.1, item 7: the Mac camera helper builds itself. The 0.5.0 playtest found "helper missing" in
// a project folder where nobody had run tools/blink/mac/build.sh. Now, when the editor opens on a
// Mac and the helper is missing or older than its source, it is compiled in the background
// (a few seconds with the Xcode command line tools). Compiling never opens the camera: the
// camera starts only after the player turns webcam blinking on and consents in the game.
// Skipped in batch mode, so test and ablation runs don't pay for it.
[InitializeOnLoad]
public static class BlinkHelperAutoBuild
{
    const string Once = "Kehai.BlinkHelperAutoBuild.Tried";

    static BlinkHelperAutoBuild()
    {
        if (Application.isBatchMode || Application.platform != RuntimePlatform.OSXEditor) return;
        if (SessionState.GetBool(Once, false)) return;
        SessionState.SetBool(Once, true);
        EditorApplication.delayCall += BuildIfNeeded;
    }

    static void BuildIfNeeded()
    {
        if (KehaiBuild.BlinkHelperUpToDate() || !System.IO.File.Exists(KehaiBuild.BlinkHelperScript)) return;
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("/bin/sh", "\"" + KehaiBuild.BlinkHelperScript + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                },
                EnableRaisingEvents = true,
            };
            process.Exited += (_, __) =>
            {
                string error = process.StandardError.ReadToEnd();
                if (process.ExitCode == 0) Debug.Log("Kehai: the camera helper for webcam blinking is built.");
                else Debug.LogWarning("Kehai: couldn't build the camera helper (install the Xcode command line tools: xcode-select --install). " + error.Trim());
                process.Dispose();
            };
            process.Start();
        }
        catch (System.Exception e) { Debug.LogWarning("Kehai: couldn't build the camera helper: " + e.Message); }
    }
}
