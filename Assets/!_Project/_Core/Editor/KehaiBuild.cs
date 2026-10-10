using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Kehai;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

// Builds the game: Kehai → Build → macOS or Windows in the editor, or headless with the editor closed:
//
//   Unity -batchmode -nographics -projectPath . -executeMethod KehaiBuild.MacOS
//   Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiBuild.Windows
//         [-kehai-build-out <folder/Kehai.app or folder/Kehai.exe>] [-kehai-build-dev]
//
// The game goes to Builds/macOS/ or Builds/Windows/ unless told otherwise, and gets what the
// editor never needed:
//   - the shaders the game makes materials from by name (Shader.Find) but no material in the
//     project uses: a build leaves those out, so they're added to Always Included Shaders;
//   - build.txt beside it, with the version and the commit, for playtesters' bug reports;
// and on macOS:
//   - the webcam blink helper beside the app, where the game looks for it outside the editor
//     (tools/blink/mac/build/BlinkVision, if it has been built);
//   - a camera line in the app's Info.plist: the helper is the game's child process, so macOS
//     asks on the game's behalf, and stops an app that opens a camera without one.
// Windows builds need Windows Build Support (Mono) added to the editor in Unity Hub; they have
// no webcam helper yet, so blinking there is the keyboard's (B).
//
// Playtest builds (PLAYTEST.md): add -playtest <round>, or Kehai → Build → Playtest, which
// takes the round from tools/playtest/.env. They go to Builds/playtest-<round>/, carry
// StreamingAssets/playtest.json (the round, where to send sessions and the form link, from
// tools/playtest/.env or the environment), and come zipped for testers beside that folder.
public static class KehaiBuild
{
    // GuideMarker's rings and beacons, and Karen's fog.
    static readonly string[] RuntimeShaders = { "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Lit" };

    const string CameraReason = GameNames.Game + " can watch for your blinks through the webcam, if you turn that on " +
                                "in its settings. Nothing is recorded, and nothing leaves this computer.";

    [MenuItem("Kehai/Build/macOS")]
    static void MacOSFromMenu() => FromMenu(BuildTarget.StandaloneOSX);

    [MenuItem("Kehai/Build/Windows")]
    static void WindowsFromMenu() => FromMenu(BuildTarget.StandaloneWindows64);

    [MenuItem("Kehai/Build/Playtest/macOS")]
    static void PlaytestMacOSFromMenu() => FromMenu(BuildTarget.StandaloneOSX, PlaytestSettings().round);

    [MenuItem("Kehai/Build/Playtest/Windows")]
    static void PlaytestWindowsFromMenu() => FromMenu(BuildTarget.StandaloneWindows64, PlaytestSettings().round);

    static void FromMenu(BuildTarget target, string playtest = null)
    {
        BuildReport report = Build(target, DefaultPath(target, playtest), development: false, playtest);
        if (report != null && report.summary.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(report.summary.outputPath);
    }

    // The batch-mode entries. Each exits 0 when the build succeeded.
    public static void MacOS() => FromCommandLine(BuildTarget.StandaloneOSX);

    public static void Windows() => FromCommandLine(BuildTarget.StandaloneWindows64);

    static void FromCommandLine(BuildTarget target)
    {
        string playtest = Arg("-playtest");
        BuildReport report = Build(target, Arg("-kehai-build-out") ?? DefaultPath(target, playtest), Environment.GetCommandLineArgs().Contains("-kehai-build-dev"), playtest);
        EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    public static string ProjectRoot => Directory.GetParent(UnityEngine.Application.dataPath).FullName;

    public static string DefaultPath(BuildTarget target, string playtest = null)
    {
        string folder = string.IsNullOrEmpty(playtest)
            ? Path.Combine(ProjectRoot, "Builds", PlatformName(target))
            : Path.Combine(ProjectRoot, "Builds", "playtest-" + playtest, $"{PlayerSettings.productName}-playtest-{playtest}-{PlatformName(target)}");
        return Path.Combine(folder, PlayerSettings.productName + (target == BuildTarget.StandaloneOSX ? ".app" : ".exe"));
    }

    static string PlatformName(BuildTarget target) => target == BuildTarget.StandaloneOSX ? "macOS" : "Windows";

    // The scenes in File → Build Profiles, in order; the first is the one the game opens.
    public static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();

    public static BuildReport Build(BuildTarget target, string appPath, bool development, string playtest = null)
    {
        string[] scenes = Scenes;
        if (scenes.Length == 0)
        {
            Debug.LogError("Kehai build: no scenes in the build list (File → Build Profiles).");
            return null;
        }
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            Debug.LogError($"Kehai build: this editor can't build for {PlatformName(target)}. Add its Build Support module in Unity Hub (Installs → ⚙ → Add modules).");
            return null;
        }

        IncludeRuntimeShaders();
        Directory.CreateDirectory(Path.GetDirectoryName(appPath));
        var watch = Stopwatch.StartNew();
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = appPath,
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = development ? BuildOptions.Development : BuildOptions.None,
        });
        BuildSummary s = report.summary;
        Debug.Log($"Kehai build: {s.result} in {watch.Elapsed:mm\\:ss}: {appPath}, {s.totalSize / 1048576f:0} MB, " +
                  $"{s.totalErrors} errors, {s.totalWarnings} warnings");
        if (s.result != BuildResult.Succeeded) return report;

        Kehai.Playtest.PlaytestConfig config = string.IsNullOrEmpty(playtest) ? null : WritePlaytestConfig(target, appPath, playtest);
        if (target == BuildTarget.StandaloneOSX)
        {
            AddCameraReason(appPath);
            CopyBlinkHelper(appPath);
            Run("codesign", $"--force --deep --sign - \"{appPath}\"", "re-signing the app after its Info.plist changed");
        }
        WriteBuildInfo(appPath, target, development, config);
        if (config != null) ZipForTesters(appPath);
        return report;
    }

    public static void IncludeRuntimeShaders()
    {
        UnityEngine.Object settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
        if (settings == null) { Debug.LogWarning("Kehai build: GraphicsSettings not found; runtime shaders may be missing."); return; }
        var so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        var present = new HashSet<UnityEngine.Object>();
        for (int i = 0; i < list.arraySize; i++) present.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
        foreach (string name in RuntimeShaders)
        {
            UnityEngine.Shader shader = UnityEngine.Shader.Find(name);
            if (shader == null) { Debug.LogWarning("Kehai build: shader not found: " + name); continue; }
            if (present.Contains(shader)) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            Debug.Log("Kehai build: always including " + name);
        }
        if (so.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssets();
    }

    static void AddCameraReason(string appPath)
    {
        string plist = Path.Combine(appPath, "Contents", "Info.plist");
        if (!File.Exists(plist)) { Debug.LogWarning("Kehai build: no Info.plist in " + appPath); return; }
        if (File.ReadAllText(plist).Contains("NSCameraUsageDescription"))
            Run("/usr/libexec/PlistBuddy", $"-c \"Set :NSCameraUsageDescription {CameraReason}\" \"{plist}\"", "setting the camera line");
        else
            Run("/usr/libexec/PlistBuddy", $"-c \"Add :NSCameraUsageDescription string {CameraReason}\" \"{plist}\"", "adding the camera line");
    }

    static void CopyBlinkHelper(string appPath)
    {
        string helper = Path.Combine(ProjectRoot, "tools", "blink", "mac", "build", "BlinkVision");
        string beside = Path.Combine(Path.GetDirectoryName(appPath), "BlinkVision");
        if (!File.Exists(helper))
        {
            Debug.LogWarning("Kehai build: the blink helper isn't built (tools/blink/mac/build.sh), so the build has no webcam blinking.");
            return;
        }
        File.Copy(helper, beside, overwrite: true);
        Run("/bin/chmod", $"+x \"{beside}\"", "making the blink helper runnable");
    }

    // ---- playtest builds ----------------------------------------------------------------------

    // tools/playtest/.env (KEY=value lines, not in git), with the environment taking precedence.
    public static Kehai.Playtest.PlaytestConfig PlaytestSettings(string round = null)
    {
        var values = ReadEnvFile(Path.Combine(ProjectRoot, "tools", "playtest", ".env"));
        string Get(string key) => Environment.GetEnvironmentVariable(key) is string v && v.Length > 0 ? v : values.TryGetValue(key, out string f) ? f : "";
        return new Kehai.Playtest.PlaytestConfig
        {
            round = round ?? (Get("KEHAI_PLAYTEST_ROUND") is string r && r.Length > 0 ? r : "round1"),
            uploadUrl = Get("KEHAI_PLAYTEST_UPLOAD_URL").TrimEnd('/'),
            uploadKey = Get("KEHAI_PLAYTEST_UPLOAD_KEY"),
            formUrl = Get("KEHAI_PLAYTEST_FORM_URL"),
            suggestFinishAfter = int.TryParse(Get("KEHAI_PLAYTEST_SUGGEST_AFTER"), out int n) && n > 0 ? n : 3,
        };
    }

    public static Dictionary<string, string> ReadEnvFile(string path)
    {
        var values = new Dictionary<string, string>();
        if (!File.Exists(path)) return values;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line.StartsWith("#") || eq <= 0) continue;
            values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim().Trim('"', '\'');
        }
        return values;
    }

    // The built game's StreamingAssets folder.
    public static string StreamingAssetsOf(BuildTarget target, string appPath) => target == BuildTarget.StandaloneOSX
        ? Path.Combine(appPath, "Contents", "Resources", "Data", "StreamingAssets")
        : Path.Combine(Path.GetDirectoryName(appPath), Path.GetFileNameWithoutExtension(appPath) + "_Data", "StreamingAssets");

    static Kehai.Playtest.PlaytestConfig WritePlaytestConfig(BuildTarget target, string appPath, string round)
    {
        Kehai.Playtest.PlaytestConfig config = PlaytestSettings(round);
        string commit = Run("git", $"-C \"{ProjectRoot}\" rev-parse --short HEAD", null)?.Trim();
        config.build = $"{PlayerSettings.bundleVersion} {commit}".Trim();
        string folder = StreamingAssetsOf(target, appPath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Kehai.Playtest.PlaytestConfig.FileName), UnityEngine.JsonUtility.ToJson(config, true));
        Debug.Log($"Kehai build: playtest {config.round}, " + (config.Uploads ? "sending sessions to " + config.uploadUrl : "keeping sessions on the tester's computer (no KEHAI_PLAYTEST_UPLOAD_URL)") +
                  (string.IsNullOrEmpty(config.formUrl) ? ", no form link" : ", form " + config.formUrl));
        return config;
    }

    // <folder>.zip beside the build's folder, without the crash-debugging symbols.
    static void ZipForTesters(string appPath)
    {
        string folder = Path.GetDirectoryName(appPath);
        string zip = folder + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        string name = Path.GetFileName(folder);
        Run("/bin/sh", $"-c \"cd '{Path.GetDirectoryName(folder)}' && zip -q -r -y -X '{name}.zip' '{name}' -x '*_DoNotShip/*' '*.DS_Store'\"", "zipping the build for testers");
        if (File.Exists(zip)) Debug.Log($"Kehai build: {zip} ({new FileInfo(zip).Length / 1048576f:0} MB) is the one to send");
    }

    static void WriteBuildInfo(string appPath, BuildTarget target, bool development, Kehai.Playtest.PlaytestConfig playtest = null)
    {
        string commit = Run("git", $"-C \"{ProjectRoot}\" rev-parse --short HEAD", null)?.Trim();
        string branch = Run("git", $"-C \"{ProjectRoot}\" rev-parse --abbrev-ref HEAD", null)?.Trim();
        string changes = Run("git", $"-C \"{ProjectRoot}\" status --porcelain --untracked-files=no", null);
        string from = string.IsNullOrEmpty(commit) ? "an unknown commit"
            : $"commit {commit}" + (string.IsNullOrEmpty(branch) || branch == "HEAD" ? "" : $" ({branch})") +
              (string.IsNullOrWhiteSpace(changes) ? "" : " with local changes");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(appPath), "build.txt"),
            $"{PlayerSettings.productName} {PlayerSettings.bundleVersion} for {PlatformName(target)}{(development ? ", development build" : "")}\n" +
            $"Built {DateTime.Now:yyyy-MM-dd HH:mm} from {from}.\n" +
            (playtest == null ? "" : $"Playtest {playtest.round}: " + (playtest.Uploads ? "sends sessions after the tester agrees." : "keeps sessions on the tester's computer.") + "\n"));
    }

    // Runs a tool and returns what it printed, or null if it failed (with a warning, when `what` says what it was for).
    static string Run(string file, string arguments, string what)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            });
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return output;
            if (what != null) Debug.LogWarning($"Kehai build: {what} failed: {error.Trim()}");
        }
        catch (Exception e)
        {
            if (what != null) Debug.LogWarning($"Kehai build: {what} failed: {e.Message}");
        }
        return null;
    }

    static string Arg(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
