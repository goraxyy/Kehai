using System;
using System.IO;
using Kehai.Karen;
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
        public string channel;        // dev, testing or prod for a channel build
        public string satori, oni;    // bundled card versions; absent in older build.json files
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

    static string CardVersion(BrainCard card)
    {
        if (card == null || !card.Trained) return "untrained";
        return string.IsNullOrEmpty(card.Version) ? "unknown version" : card.Version;
    }

    public static void IncludeBundledCards(Data stamp)
    {
        if (string.IsNullOrEmpty(stamp.satori))
            stamp.satori = CardVersion(BrainNets.FindBuiltIn("KarenBrainBasic", out _)?.Card);
        if (string.IsNullOrEmpty(stamp.oni))
            stamp.oni = CardVersion(BrainNets.FindBuiltIn("KarenBrainVital", out _)?.Card);
    }

    public static string Describe(Data stamp, ModelPack pack = null)
    {
        var basic = pack?.Brain("KarenBrainBasic")?.Card;
        var vital = pack?.Brain("KarenBrainVital")?.Card;
        string satori = basic != null ? CardVersion(basic) : stamp.satori;
        string oni = vital != null ? CardVersion(vital) : stamp.oni;
        string source = pack == null ? "" : $" (model pack {pack.PackVersion})";
        return $"Kehai {stamp.version} · code {stamp.code} · art {stamp.art} · Karen: Satori {satori}, Oni {oni}{source}";
    }

    public static string Line => Describe(Current, ModelPack.Current);

    static Data Read()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, FileName);
            if (!File.Exists(path)) return null;
            Data d = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            if (string.IsNullOrEmpty(d?.version)) return null;
            IncludeBundledCards(d);
            return d;
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
        IncludeBundledCards(d);
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
