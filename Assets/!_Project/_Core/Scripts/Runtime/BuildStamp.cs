using System;
using System.IO;
using UnityEngine;

// Where this copy of the game came from (0.5.2, C10.2; the versions page, section 6): the game's
// version with its edition label, the code commit, the art commit, and the Karens' numbers. A
// build carries it in StreamingAssets/build.json (KehaiBuild writes it); the editor works it out
// from git. It's shown in Settings and on the main menu, and written into every shift record, so
// a bug report, a tester's session or a training record says exactly what made it.
public static class BuildStamp
{
    public const string FileName = "build.json";

    [Serializable]
    public sealed class Data
    {
        public string version = "";   // e.g. 0.5.2-testing
        public string edition = "";
        public string code = "";      // the public repo's commit, short; "+changes" when built from a dirty tree
        public string art = "";       // the private project's commit (kehai-project), or "none" before it exists
    }

    static Data cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cached = null;

    public static Data Current
    {
        get
        {
            if (cached != null) return cached;
            cached = Read() ?? Live();
            return cached;
        }
    }

    // Her numbers today: a model pack's version, or the build's own (C10.3 gives each Karen her own
    // MAJOR.MINOR.PATCH and her name: Satori for Basic, Oni for Vital).
    public static string Karen
    {
        get
        {
            var pack = Kehai.Karen.ModelPack.Current;
            return pack != null ? $"model pack {pack.PackVersion}" : "built-in numbers";
        }
    }

    // One line, for Settings, reports and records.
    public static string Line
    {
        get
        {
            Data d = Current;
            return $"Kehai {d.version} · code {d.code} · art {d.art} · Karen: {Karen}";
        }
    }

    static Data Read()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (!File.Exists(path)) return null;
            Data d = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            return string.IsNullOrEmpty(d?.version) ? null : d;
        }
        catch (Exception) { return null; }
    }

    // No stamp file: the editor (or an old build). The commits come from git in the editor only.
    static Data Live()
    {
        var d = new Data
        {
            version = Edition.VersionLabel(Application.version, Edition.Current),
            edition = Edition.Current.ToString().ToLowerInvariant(),
            code = "unknown",
            art = "none",
        };
#if UNITY_EDITOR
        string root = Path.GetDirectoryName(Application.dataPath);
        d.code = Git(root, "rev-parse --short HEAD") ?? "unknown";
        if (!string.IsNullOrEmpty(Git(root, "status --porcelain --untracked-files=no"))) d.code += "+changes";
        string assets = Path.Combine(root, ".git-assets");
        if (Directory.Exists(assets)) d.art = Git(root, $"--git-dir=\"{assets}\" --work-tree=\"{root}\" rev-parse --short HEAD") ?? "none";
#endif
        return d;
    }

#if UNITY_EDITOR
    public static string Git(string root, string args)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", $"-C \"{root}\" {args}")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(info))
            {
                string output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(5000);
                return p.ExitCode == 0 ? output : null;
            }
        }
        catch (Exception) { return null; }
    }
#endif
}
