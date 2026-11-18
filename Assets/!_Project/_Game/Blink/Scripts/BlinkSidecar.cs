using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kehai.Blink
{
    // Starts and stops the camera helper that reads your eyes, so blinking works without
    // opening a terminal. On a Mac it's tools/blink/mac/build/BlinkVision (Apple Vision, no
    // downloads — build it once with tools/blink/mac/build.sh); anywhere, the Python helper
    // in tools/blink/.venv if it has been set up. Its output is kept for the F10 test panel.
    public static class BlinkSidecar
    {
        static Process process;
        static readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        static readonly List<string> lines = new List<string>();

        public static bool Running => process != null && !process.HasExited;
        public static string Which { get; private set; } = "";
        public static string Problem { get; private set; } = "";
        public static IReadOnlyList<string> Output { get { Drain(); return lines; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            Application.quitting -= Stop;
            Application.quitting += Stop;
        }

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        // Which camera the Mac helper opens; C in the F10 panel steps through them.
        public static int CameraIndex
        {
            get => PlayerPrefs.GetInt("Kehai.BlinkCamera", 0);
            set => PlayerPrefs.SetInt("Kehai.BlinkCamera", Mathf.Max(0, value));
        }

        static string[] cameras;

        // The cameras the Mac helper can see, as "0: FaceTime HD Camera". Asked once, then kept.
        public static IReadOnlyList<string> Cameras
        {
            get
            {
                if (cameras != null) return cameras;
                string exe = MacHelper;
                if (exe == null) return new string[0];   // not built yet; ask again later
                cameras = new string[0];
                try
                {
                    var info = new ProcessStartInfo(exe, "--list-cameras") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process p = Process.Start(info))
                    {
                        var stdout = p.StandardOutput.ReadToEndAsync();
                        var stderr = p.StandardError.ReadToEndAsync();
                        if (!p.WaitForExit(3000))
                        {
                            try { p.Kill(); } catch { }
                            Problem = "Camera enumeration timed out; retry after checking the camera connection.";
                            cameras = null;
                            return new string[0];
                        }
                        string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
                        cameras = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 2 && char.IsDigit(l[0]) && l.Contains(":")).ToArray();
                    }
                }
                catch (System.Exception) { }
                return cameras;
            }
        }

        public static string CameraName
        {
            get
            {
                IReadOnlyList<string> all = Cameras;
                if (all.Count == 0) return "";
                string line = all[Mathf.Clamp(CameraIndex, 0, all.Count - 1)];
                return line.Substring(line.IndexOf(':') + 1).Trim();
            }
        }

        // Next camera, and restart the helper on it.
        public static void NextCamera(int port)
        {
            int count = Cameras.Count;
            CameraIndex = count > 0 ? (CameraIndex + 1) % count : 0;
            Stop();
            Start(port);
        }

        static string MacHelper
        {
            get
            {
                if (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.OSXPlayer) return null;
                foreach (string path in new[] { Path.Combine(Application.dataPath, "Helpers/BlinkVision"), Path.Combine(ProjectRoot, "tools/blink/mac/build/BlinkVision"), Path.GetFullPath(Path.Combine(Application.dataPath, "../../BlinkVision")) })
                    if (File.Exists(path)) return path;
                return null;
            }
        }

        // The MediaPipe helper: Python in tools/blink/.venv, set up by tools/blink/setup_mediapipe.sh.
        static string Python => Path.Combine(ProjectRoot, Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer
            ? "tools/blink/.venv/Scripts/python.exe" : "tools/blink/.venv/bin/python3");
        static string Model => Path.Combine(ProjectRoot, "tools/blink/face_landmarker.task");
        public static bool MediaPipeReady => File.Exists(Python);
        public static bool VisionReady => MacHelper != null;

        // Which helper to use when both are there. MediaPipe's blink scores are the more
        // accurate, so once someone has set it up it's the default; M in the F10 panel switches.
        public static bool PreferMediaPipe
        {
            get => PlayerPrefs.GetInt("Kehai.BlinkMediaPipe", MediaPipeReady && File.Exists(Model) ? 1 : 0) == 1;
            set => PlayerPrefs.SetInt("Kehai.BlinkMediaPipe", value ? 1 : 0);
        }

        public static void SwitchHelper(int port)
        {
            PreferMediaPipe = !PreferMediaPipe;
            Stop();
            Start(port);
        }

        // Where a helper might be, in the order to try them.
        static IEnumerable<(string exe, string args, string name)> Candidates(int port)
        {
            string script = Path.Combine(ProjectRoot, "tools/blink/blink_server.py");
            string method = File.Exists(Model) ? $"--method blendshapes --model \"{Model}\"" : "--method ear";
            var mediaPipe = (Python, $"\"{script}\" {method} --port {port}", "MediaPipe (Python)");
            string mac = MacHelper;
            if (PreferMediaPipe) yield return mediaPipe;
            if (mac != null) yield return (mac, $"--port {port} --camera {CameraIndex}", "BlinkVision (Apple Vision)");
            if (!PreferMediaPipe) yield return mediaPipe;
        }

        public static bool Start(int port)
        {
            if (Running) return true;
            Stop(); // Dispose an exited helper before creating its replacement.
            Problem = "";
            foreach (var (exe, args, name) in Candidates(port))
            {
                if (!File.Exists(exe)) continue;
                try
                {
                    var info = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                    process = new Process { StartInfo = info, EnableRaisingEvents = true };
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) incoming.Enqueue(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) incoming.Enqueue(e.Data); };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    Which = name;
                    incoming.Enqueue($"started {name}");
                    return true;
                }
                catch (System.Exception e)
                {
                    Problem = $"couldn't start {name}: {e.Message}";
                    process = null;
                }
            }
            if (string.IsNullOrEmpty(Problem))
                Problem = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer
                    ? (Application.isEditor ? "The camera helper isn't built yet. It builds itself when the editor opens (needs the Xcode command line tools), or use Kehai → Blink → Build Camera Helper." : "The camera helper is missing from this app. Reinstall a complete macOS build.")
                    : "No camera helper found. Set up tools/blink (see tools/blink/README.md).";
            return false;
        }

        public static void Stop()
        {
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch { }
            process.Dispose();
            process = null;
        }

        static void Drain()
        {
            while (incoming.TryDequeue(out string line))
            {
                lines.Add(line);
                if (line.Contains("refused") || line.Contains("couldn't") || line.Contains("no camera")) Debug.LogWarning("Blink helper: " + line);
            }
            if (lines.Count > 40) lines.RemoveRange(0, lines.Count - 40);
            if (process != null && process.HasExited && string.IsNullOrEmpty(Problem))
                Problem = ProblemFor(process.ExitCode);
        }

        // What the helper's exit code means, in words a player can act on (0.5.1). The codes are
        // BlinkVision.swift's: 2 no camera, 3 the camera wouldn't start, 4 access refused or off.
        public static string ProblemFor(int exitCode)
        {
            switch (exitCode)
            {
                case 2: return "No camera found. Plug one in (or close apps using it) and try again; the keyboard still works.";
                case 3: return "The camera wouldn't start. Another app may be using it; close it and try again.";
                case 4: return "Camera access is off. Allow it in System Settings → Privacy & Security → Camera, then try again.";
                default: return $"the camera helper stopped (exit code {exitCode}) — see its last lines below";
            }
        }
    }
}
