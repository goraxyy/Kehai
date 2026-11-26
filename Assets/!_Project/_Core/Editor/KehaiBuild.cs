using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public static KehaiEdition? BuildingEdition { get; private set; }
    public static string[] EditionDefines(KehaiEdition edition) => new[] { Edition.Define(edition) };

    // Unity -executeMethod KehaiBuild.Build -edition training|testing|gameplay -platform windows|macos
    // Use -channel dev|testing|prod -release-version 0.5.2 [-beta 1] to stamp a release label.
    public static void Build()
    {
        string platform = (Arg("-platform") ?? "macos").ToLowerInvariant();
        if (platform != "windows" && platform != "macos")
            throw new ArgumentException("-platform must be windows or macos");
        FromCommandLine(platform == "windows" ? BuildTarget.StandaloneWindows64 : BuildTarget.StandaloneOSX);
    }
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
        var edition = string.IsNullOrEmpty(playtest) ? KehaiEdition.Gameplay : KehaiEdition.Testing;
        string editionArg = Arg("-edition");
        if (editionArg != null && !Edition.TryParse(editionArg, out edition))
            throw new ArgumentException("-edition must be training, testing or gameplay");
        string channel = Arg("-channel"), releaseVersion = Arg("-release-version"), beta = Arg("-beta");
        if (channel != null)
        {
            KehaiEdition expectedEdition = ChannelEdition(channel);
            if (editionArg != null && edition != expectedEdition)
                throw new ArgumentException($"{channel} uses the {expectedEdition} edition.");
            edition = expectedEdition;
        }
        else if (releaseVersion != null || beta != null)
            throw new ArgumentException("-release-version and -beta require -channel.");
        BuildStamp.Data stamp = channel == null ? null : Stamp(edition, channel, releaseVersion, beta);
        string output = Arg("-kehai-build-out");
        string previousProduct = PlayerSettings.productName;
        bool disposableClone = previousProduct.StartsWith("Kehai-run-", StringComparison.Ordinal) &&
            Environment.GetEnvironmentVariable("KEHAI_RUN_CODE_COMMIT") != null;
        if (disposableClone)
        {
            if (string.IsNullOrEmpty(output) || !Path.IsPathFullyQualified(output))
                throw new ArgumentException("A build on a temporary clone needs an absolute -kehai-build-out outside the clone.");
            string relative = Path.GetRelativePath(ProjectRoot, output);
            if (!Path.IsPathFullyQualified(relative) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("A build on a temporary clone must write outside the clone.");
        }
        // A verification player (0.5.2, C6.6) gets its own product name, so it writes its data to its
        // own folder, never the real game's. Only on a temporary clone, and only names that say so.
        string verifyProduct = VerifyProduct(Arg("-kehai-product"), disposableClone);
        BuildReport report;
        try
        {
            if (disposableClone) PlayerSettings.productName = verifyProduct ?? GameNames.Game;
            output ??= edition == KehaiEdition.Training ? KehaiRl.TrainingPath(target) : DefaultPath(target, playtest);
            report = Build(target, output, Environment.GetCommandLineArgs().Contains("-kehai-build-dev"),
                playtest, edition, stamp, releaseVersion);
        }
        finally
        {
            if (disposableClone) PlayerSettings.productName = previousProduct;
        }
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

    public static BuildReport Build(BuildTarget target, string appPath, bool development, string playtest = null,
        KehaiEdition? edition = null) => Build(target, appPath, development, playtest, edition, null, null);

    static BuildReport Build(BuildTarget target, string appPath, bool development, string playtest, KehaiEdition? edition,
        BuildStamp.Data stamp, string releaseVersion)
    {
        KehaiEdition selected = edition ?? (string.IsNullOrEmpty(playtest) ? KehaiEdition.Gameplay : KehaiEdition.Testing);
        if (!string.IsNullOrEmpty(playtest) && selected != KehaiEdition.Testing)
            throw new ArgumentException("Only Testing builds may include playtest configuration.");
        if (selected == KehaiEdition.Gameplay && development)
            throw new ArgumentException("Gameplay builds cannot enable development tools.");
        if (selected == KehaiEdition.Training && !KehaiRl.Ready)
            throw new InvalidOperationException("Training requires the ML-Agents package and KAREN_MLAGENTS define. Run Kehai → RL → Set Up ML-Agents first.");
        string[] configured = PlayerSettings.GetScriptingDefineSymbols(UnityEditor.Build.NamedBuildTarget.Standalone).Split(';');
        if (configured.Any(d => d.StartsWith("KEHAI_", StringComparison.Ordinal) &&
            new[] { "KEHAI_TRAINING", "KEHAI_TESTING", "KEHAI_GAMEPLAY" }.Contains(d) && d != Edition.Define(selected)))
            throw new InvalidOperationException("Remove conflicting KEHAI edition defines from Player Settings; the build stamps its edition automatically.");
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

        if (target == BuildTarget.StandaloneOSX && selected != KehaiEdition.Training) EnsureBlinkHelper();
        if (selected != KehaiEdition.Training) IncludeRuntimeShaders();
        Directory.CreateDirectory(Path.GetDirectoryName(appPath));
        var watch = Stopwatch.StartNew();
        BuildReport report;
        string previousVersion = PlayerSettings.bundleVersion;
        BuildingEdition = selected;
        try
        {
            if (releaseVersion != null) PlayerSettings.bundleVersion = releaseVersion;
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = appPath,
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
                extraScriptingDefines = EditionDefines(selected),
            });
        }
        finally
        {
            if (releaseVersion != null) PlayerSettings.bundleVersion = previousVersion;
            BuildingEdition = null;
        }
        BuildSummary s = report.summary;
        Debug.Log($"Kehai build: {s.result} in {watch.Elapsed:mm\\:ss}: {appPath}, {s.totalSize / 1048576f:0} MB, " +
                  $"{s.totalErrors} errors, {s.totalWarnings} warnings");
        if (s.result != BuildResult.Succeeded) return report;

        stamp ??= Stamp(selected);
        Kehai.Playtest.PlaytestConfig config = string.IsNullOrEmpty(playtest) ? null : WritePlaytestConfig(target, appPath, playtest, stamp);
        // An output folder can be reused; never inherit another edition's upload settings.
        string streaming = StreamingAssetsOf(target, appPath);
        Directory.CreateDirectory(streaming);
        File.WriteAllText(Path.Combine(streaming, "edition.txt"), selected.ToString().ToLowerInvariant());
        // The stamp (0.5.2, C10.2): what this build is, for Settings and every shift record.
        File.WriteAllText(Path.Combine(streaming, BuildStamp.FileName), UnityEngine.JsonUtility.ToJson(stamp, true));
        if (selected != KehaiEdition.Testing)
        {
            string stale = Path.Combine(streaming, Kehai.Playtest.PlaytestConfig.FileName);
            if (File.Exists(stale)) File.Delete(stale);
        }
        if (target == BuildTarget.StandaloneOSX)
        {
            if (selected != KehaiEdition.Training)
            {
                AddCameraReason(appPath);
                CopyBlinkHelper(appPath);
            }
            Run("codesign", $"--force --deep --sign - \"{appPath}\"", "re-signing the app after its Info.plist changed");
        }
        WriteBuildInfo(appPath, target, development, selected, stamp, config);
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

    [MenuItem("Kehai/Blink/Build Camera Helper")]
    public static void EnsureBlinkHelper()
    {
        string folder = Path.Combine(ProjectRoot, "tools", "blink", "mac");
        string helper = Path.Combine(folder, "build", "BlinkVision");
        string source = Path.Combine(folder, "BlinkVision.swift");
        string script = Path.Combine(folder, "build.sh");
        if (!File.Exists(source) || !File.Exists(script))
            throw new InvalidOperationException("Blink helper sources are missing from tools/blink/mac.");
        if (BlinkHelperUpToDate()) return;
        if (UnityEngine.Application.platform != UnityEngine.RuntimePlatform.OSXEditor)
            throw new InvalidOperationException("Build the macOS blink helper on a Mac before packaging it.");
        if (Run("/bin/sh", "\"" + script + "\"", "building the camera helper") == null || !File.Exists(helper))
            throw new InvalidOperationException("Camera helper compilation failed. Install Xcode command line tools and use Kehai → Blink → Build Camera Helper.");
        Debug.Log("Camera helper built. It starts only when webcam blinking is enabled with consent.");
    }

    // Built and newer than its source and build script: nothing to do.
    public static bool BlinkHelperUpToDate()
    {
        string folder = Path.Combine(ProjectRoot, "tools", "blink", "mac");
        string helper = Path.Combine(folder, "build", "BlinkVision");
        string source = Path.Combine(folder, "BlinkVision.swift");
        string script = Path.Combine(folder, "build.sh");
        return File.Exists(helper) && File.Exists(source) && File.Exists(script)
            && File.GetLastWriteTimeUtc(helper) >= File.GetLastWriteTimeUtc(source)
            && File.GetLastWriteTimeUtc(helper) >= File.GetLastWriteTimeUtc(script);
    }

    public static string BlinkHelperScript => Path.Combine(ProjectRoot, "tools", "blink", "mac", "build.sh");

    static void CopyBlinkHelper(string appPath)
    {
        EnsureBlinkHelper();
        string helper = Path.Combine(ProjectRoot, "tools", "blink", "mac", "build", "BlinkVision");
        // Keep the helper with the app when a player moves it out of the download folder.
        string embedded = Path.Combine(appPath, "Contents", "Helpers", "BlinkVision");
        Directory.CreateDirectory(Path.GetDirectoryName(embedded));
        File.Copy(helper, embedded, overwrite: true);
        if (Run("/bin/chmod", $"+x \"{embedded}\"", "making the blink helper runnable") == null)
            throw new InvalidOperationException("Could not make the packaged camera helper executable.");
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

    internal static KehaiEdition ChannelEdition(string channel) => channel switch
    {
        "dev" => KehaiEdition.Training,
        "testing" => KehaiEdition.Testing,
        "prod" => KehaiEdition.Gameplay,
        _ => throw new ArgumentException("-channel must be dev, testing or prod"),
    };

    internal static string ChannelLabel(string channel, string version, string beta, string code)
    {
        ChannelEdition(channel);
        if (!Regex.IsMatch(version ?? "", @"\A(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2}\z"))
            throw new ArgumentException("-release-version must be MAJOR.MINOR.PATCH, for example 0.5.2");
        if (channel == "testing")
        {
            if (!Regex.IsMatch(beta ?? "", @"\A[1-9][0-9]*\z"))
                throw new ArgumentException("The testing channel requires -beta followed by a positive number.");
            return $"{version}-beta.{beta}";
        }
        if (beta != null) throw new ArgumentException("Only the testing channel takes -beta.");
        if (channel == "prod") return version;
        if (!Regex.IsMatch(code ?? "", @"\A[0-9a-f]{7,40}(?:\+changes)?\z"))
            throw new ArgumentException("The dev channel needs a known source commit.");
        return $"{version}-dev+{code.Replace("+changes", ".changes")}";
    }

    internal static string VerifyProduct(string name, bool disposableClone)
    {
        if (name == null) return null;
        if (!disposableClone) throw new ArgumentException("-kehai-product is only for a build on a temporary kehai-run clone.");
        if (!Regex.IsMatch(name, @"\AKehai-verify(?:-[a-z0-9]+)*\z"))
            throw new ArgumentException("-kehai-product must be Kehai-verify or Kehai-verify-<word>, so it never names the real game.");
        return name;
    }

    internal static string ArtFromRunner(string value) =>
        value != null && Regex.IsMatch(value, @"\A(?:[0-9a-f]{7,40}|unversioned)\z") ? value : null;

    static string FullCodeCommit => Run("git", $"-C \"{ProjectRoot}\" rev-parse HEAD", null)?.Trim()
        ?? Environment.GetEnvironmentVariable("KEHAI_RUN_CODE_COMMIT");

    // What goes into build.json: the version, edition, code and art commits.
    public static BuildStamp.Data Stamp(KehaiEdition edition, string channel = null, string releaseVersion = null, string beta = null)
    {
        string root = ProjectRoot;
        string fullCode = FullCodeCommit;
        string code = Regex.IsMatch(fullCode ?? "", @"\A[0-9a-f]{40}\z") ? fullCode.Substring(0, 7) : "unknown";
        string dirty = Run("git", $"-C \"{root}\" status --porcelain --untracked-files=no", null)?.Trim();
        string assets = Path.Combine(root, ".git-assets");
        string art = Directory.Exists(assets) ? Run("git", $"--git-dir=\"{assets}\" --work-tree=\"{root}\" rev-parse --short HEAD", null)?.Trim() : null;
        // A release rebuild in a kehai-run clone (no .git-assets there): the art it laid, or "unversioned".
        if (string.IsNullOrEmpty(art)) art = ArtFromRunner(Environment.GetEnvironmentVariable("KEHAI_RUN_ART_COMMIT"));
        if (!string.IsNullOrEmpty(dirty)) code += "+changes";
        var stamp = new BuildStamp.Data
        {
            version = channel == null ? Edition.VersionLabel(PlayerSettings.bundleVersion, edition) : ChannelLabel(channel, releaseVersion, beta, code),
            edition = edition.ToString().ToLowerInvariant(),
            channel = channel,
            code = code,
            art = string.IsNullOrEmpty(art) ? "none" : art,
        };
        BuildStamp.IncludeBundledCards(stamp);
        return stamp;
    }

    // The built game's StreamingAssets folder.
    public static string StreamingAssetsOf(BuildTarget target, string appPath) => target == BuildTarget.StandaloneOSX
        ? Path.Combine(appPath, "Contents", "Resources", "Data", "StreamingAssets")
        : Path.Combine(Path.GetDirectoryName(appPath), Path.GetFileNameWithoutExtension(appPath) + "_Data", "StreamingAssets");

    static Kehai.Playtest.PlaytestConfig WritePlaytestConfig(BuildTarget target, string appPath, string round, BuildStamp.Data stamp)
    {
        Kehai.Playtest.PlaytestConfig config = PlaytestSettings(round);
        config.build = $"{(stamp.channel == null ? PlayerSettings.bundleVersion : stamp.version)} {stamp.code}";
        string folder = StreamingAssetsOf(target, appPath);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Kehai.Playtest.PlaytestConfig.FileName), UnityEngine.JsonUtility.ToJson(config, true));
        Debug.Log($"Kehai build: playtest {config.round}, " + (config.Uploads ? "sending sessions to " + config.uploadUrl : "keeping sessions on the tester's computer (no KEHAI_PLAYTEST_UPLOAD_URL)") +
                  (string.IsNullOrEmpty(config.formUrl) ? ", no form link" : ", form " + config.formUrl));
        return config;
    }

    // <folder>.zip beside the build's folder, without the crash-debugging symbols.
    internal static void ZipForTesters(string appPath)
    {
        string folder = Path.GetDirectoryName(appPath);
        string zip = folder + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        string name = Path.GetFileName(folder);
        Run("/bin/sh", $"-c \"cd '{Path.GetDirectoryName(folder)}' && zip -q -r -y -X '{name}.zip' '{name}' -x '*_DoNotShip/*' '*.DS_Store'\"", "zipping the build for testers");
        if (File.Exists(zip)) Debug.Log($"Kehai build: {zip} ({new FileInfo(zip).Length / 1048576f:0} MB) is the one to send");
    }

    internal static void WriteBuildInfo(string appPath, BuildTarget target, bool development, KehaiEdition edition, BuildStamp.Data stamp, Kehai.Playtest.PlaytestConfig playtest = null)
    {
        string commit = stamp.code?.Split('+')[0];
        string branch = Run("git", $"-C \"{ProjectRoot}\" rev-parse --abbrev-ref HEAD", null)?.Trim();
        string changes = Run("git", $"-C \"{ProjectRoot}\" status --porcelain --untracked-files=no", null);
        string from = string.IsNullOrEmpty(commit) || commit == "unknown" ? "an unknown commit"
            : $"commit {commit}" + (string.IsNullOrEmpty(branch) || branch == "HEAD" ? "" : $" ({branch})") +
              (string.IsNullOrWhiteSpace(changes) ? "" : " with local changes");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(appPath), "build-provenance.json"), MiniJson.Serialize(new System.Collections.Generic.Dictionary<string,object>
        {
            ["commit"] = FullCodeCommit,
            ["dirty"] = !string.IsNullOrWhiteSpace(changes),
            ["edition"] = edition.ToString().ToLowerInvariant(),
            ["channel"] = stamp.channel,
            ["version"] = stamp.version,
        }));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(appPath), "build.txt"),
            $"{PlayerSettings.productName} {stamp.version} for {PlatformName(target)}{(development ? ", development build" : "")}\n" +
            $"Edition: {edition.ToString().ToLowerInvariant()}.\nBuilt {DateTime.Now:yyyy-MM-dd HH:mm} from {from}.\n" +
            BuildStamp.Describe(stamp) + "\n" +
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
            var outputRead = process.StandardOutput.ReadToEndAsync();
            var errorRead = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string output = outputRead.GetAwaiter().GetResult();
            string error = errorRead.GetAwaiter().GetResult();
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
        if (i < 0) return null;
        if (i + 1 == args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
            throw new ArgumentException($"{key} needs a value.");
        return args[i + 1];
    }
}
