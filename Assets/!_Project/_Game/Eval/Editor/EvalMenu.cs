using System.IO;
using System.Linq;
using Kehai;
using Kehai.Eval;
using UnityEditor;
using UnityEngine;

// The eval harness from the editor (IDEAS.md §1–2). Each command enters play mode if
// needed, then starts the server or the ablation once the store has loaded.
[InitializeOnLoad]
public static class EvalMenu
{
    const string PendingKey = "Kehai.Eval.Pending";

    static EvalMenu()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            string pending = SessionState.GetString(PendingKey, string.Empty);
            SessionState.EraseString(PendingKey);
            if (pending.Length > 0) Launch(pending);
        };
    }

    [MenuItem("Kehai/Eval/Start Env Server (port 5555)")]
    static void StartServer() => Run("server");

    [MenuItem("Kehai/Eval/Run Quick Ablation (1 career, 2 shifts, 90 s)")]
    static void QuickAblation() => Run("quick");

    [MenuItem("Kehai/Eval/Run Full Ablation (2 careers, 4 shifts, 150 s)")]
    static void FullAblation() => Run("full");

    [MenuItem("Kehai/Eval/Watch One Bot Shift (rung F, efficient)")]
    static void WatchBot() => Run("watch");

    [MenuItem("Kehai/Eval/Open Eval Folder")]
    static void OpenFolder()
    {
        Directory.CreateDirectory(KehaiEnv.EvalDirectory);
        EditorUtility.RevealInFinder(KehaiEnv.EvalDirectory);
    }

    static void Run(string what)
    {
        if (EditorApplication.isPlaying) { Launch(what); return; }
        SessionState.SetString(PendingKey, what);
        EditorApplication.isPlaying = true;
    }

    static void Launch(string what)
    {
        switch (what)
        {
            case "server":
                EnvServer.Start(5555, false);
                break;
            case "quick":
                Report(AblationRunner.Run(new AblationPlan { careers = 1, shiftsPerCareer = 2, shiftSeconds = 90f }));
                break;
            case "full":
                Report(AblationRunner.Run(new AblationPlan { careers = 2, shiftsPerCareer = 4, shiftSeconds = 150f }));
                break;
            case "watch":
                KehaiEnv env = KehaiEnv.Ensure();
                env.StartCoroutine(WatchOne(env));
                break;
        }
    }

    static System.Collections.IEnumerator WatchOne(KehaiEnv env)
    {
        yield return env.ResetEpisode(new EnvConfig { seed = 1, rung = "F", shiftSeconds = 120f, fps = 30, render = true, agent = "efficient" });
        yield return new SimulatedPlayer(env, PlayerProfile.Efficient, 1).PlayShift();
        Debug.Log("Bot shift finished:\n" + MiniJson.Serialize(env.Metrics.ToDictionary()));
    }

    static void Report(AblationRunner runner)
    {
        runner.Completed = r => Debug.Log($"Ablation finished — {r.Progress}\n{r.MarkdownPath}\n\n{r.Summarise()}");
        Debug.Log("Ablation running. Progress: Kehai/Eval/Log Ablation Progress");
    }

    [MenuItem("Kehai/Eval/Log Ablation Progress")]
    static void Progress()
    {
        AblationRunner runner = Object.FindAnyObjectByType<AblationRunner>();
        Debug.Log(runner != null ? runner.Progress : "No ablation running.");
    }

    // A player build that the headless commands run against:
    //   Builds/KehaiEval/Kehai.app/Contents/MacOS/Kehai -batchmode -nographics -kehai-env 5555
    [MenuItem("Kehai/Eval/Build Eval Player (macOS)")]
    static void Build()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/KehaiEval/Kehai.app",
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"Eval build: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB → {options.locationPathName}");
    }
}
