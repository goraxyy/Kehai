using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Kehai → RL: what rung F's training needs from the editor (tools/rl/README.md).
//
//   Set Up ML-Agents           adds com.unity.ml-agents to Packages/manifest.json and, once
//                              it's in, the KAREN_MLAGENTS define the bridge compiles under
//   Build Training Player      macOS or Windows, to Builds/KehaiTrain/<platform>/ (Windows
//                              is zipped beside it, to carry to another computer)
//
// Headless, with the editor closed:
//   Unity -batchmode -nographics -projectPath . -executeMethod KehaiRl.SetUp
//   Unity -batchmode -nographics -projectPath . -executeMethod KehaiRl.BuildMacOS
//   Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiRl.BuildWindows
//
// Neither the package nor the define is in the repository (Packages/ and ProjectSettings/
// aren't), so each copy of the project is set up once.
[InitializeOnLoad]
public static class KehaiRl
{
    public const string Package = "com.unity.ml-agents";
    public const string Version = "4.1.0";       // with mlagents 1.1.0 and Python 3.10.12 (tools/rl/README.md)
    public const string Define = "KAREN_MLAGENTS";
    const string DefinePending = "Kehai.Rl.DefinePending";

    static string Manifest => Path.Combine(KehaiBuild.ProjectRoot, "Packages", "manifest.json");

    // The define waits for the package: set before it, every script would fail to compile
    // until the download finished.
    static KehaiRl()
    {
        if (!SessionState.GetBool(DefinePending, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (!PackageInstalled()) return;
            SessionState.EraseBool(DefinePending);
            AddDefine();
        };
    }

    [MenuItem("Kehai/RL/Set Up ML-Agents")]
    static void SetUpFromMenu()
    {
        if (AddToManifest()) Debug.Log($"Kehai RL: added {Package} {Version} to Packages/manifest.json — the define follows once it's installed.");
        if (PackageInstalled()) AddDefine();
        else
        {
            SessionState.SetBool(DefinePending, true);
            UnityEditor.PackageManager.Client.Resolve();
        }
    }

    // Batch: both at once. The next launch resolves the package before it compiles anything.
    public static void SetUp()
    {
        AddToManifest();
        AddDefine();
        EditorApplication.Exit(0);
    }

    [MenuItem("Kehai/RL/Build Training Player/macOS")]
    static void MacOSFromMenu() => BuildTraining(BuildTarget.StandaloneOSX, reveal: true);

    [MenuItem("Kehai/RL/Build Training Player/Windows")]
    static void WindowsFromMenu() => BuildTraining(BuildTarget.StandaloneWindows64, reveal: true);

    public static void BuildMacOS() => EditorApplication.Exit(BuildTraining(BuildTarget.StandaloneOSX, false) ? 0 : 1);

    public static void BuildWindows() => EditorApplication.Exit(BuildTraining(BuildTarget.StandaloneWindows64, false) ? 0 : 1);

    public static string TrainingPath(BuildTarget target) =>
        Path.Combine(KehaiBuild.ProjectRoot, "Builds", "KehaiTrain", target == BuildTarget.StandaloneOSX ? "macOS" : "Windows",
                     PlayerSettings.productName + (target == BuildTarget.StandaloneOSX ? ".app" : ".exe"));

    static bool BuildTraining(BuildTarget target, bool reveal)
    {
        if (!DefineSet())
        {
            Debug.LogError($"Kehai RL: {Define} isn't defined, so the player couldn't talk to a trainer. Run Kehai → RL → Set Up ML-Agents first.");
            return false;
        }
        string path = TrainingPath(target);
        BuildReport report = KehaiBuild.Build(target, path, development: false);
        if (report == null || report.summary.result != BuildResult.Succeeded) return false;

        if (target == BuildTarget.StandaloneWindows64)
        {
            KehaiBuild.ZipForTesters(path);
            Debug.Log("Kehai RL: copy Builds/KehaiTrain/Windows.zip and tools/rl to the Windows machine (tools/rl/README.md).");
        }
        if (reveal) EditorUtility.RevealInFinder(path);
        return true;
    }

    // ---- package and define ------------------------------------------------------------------

    static bool PackageInstalled() => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + Package) != null;

    // A line in "dependencies", unless the package is already listed.
    static bool AddToManifest()
    {
        string json = File.ReadAllText(Manifest);
        if (json.Contains($"\"{Package}\"")) return false;
        const string key = "\"dependencies\": {";
        int at = json.IndexOf(key, System.StringComparison.Ordinal);
        if (at < 0)
        {
            Debug.LogError("Kehai RL: no \"dependencies\" in Packages/manifest.json — add " + Package + " " + Version + " by hand (Package Manager → + → Add by name).");
            return false;
        }
        at += key.Length;
        File.WriteAllText(Manifest, json.Insert(at, $"\n    \"{Package}\": \"{Version}\","));
        return true;
    }

    static NamedBuildTarget Standalone => NamedBuildTarget.Standalone;

    static bool DefineSet() => PlayerSettings.GetScriptingDefineSymbols(Standalone).Split(';').Contains(Define);

    static void AddDefine()
    {
        if (DefineSet()) return;
        string defines = PlayerSettings.GetScriptingDefineSymbols(Standalone);
        PlayerSettings.SetScriptingDefineSymbols(Standalone, string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
        Debug.Log($"Kehai RL: {Define} defined — rung F's ML-Agents bridge compiles in.");
    }
}
