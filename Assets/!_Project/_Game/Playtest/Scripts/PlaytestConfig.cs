using System;
using System.IO;
using UnityEngine;

namespace Kehai.Playtest
{
    // What makes a build a playtest build: StreamingAssets/playtest.json, written into the built
    // game by KehaiBuild -playtest <round>. Without it the game is the game, and none of the
    // playtest code runs. PLAYTEST.md has the whole loop.
    //
    //   -playtest-config <file>   use this file instead (testing a build, or the editor in batch mode)
    //   Kehai → Playtest → Simulate a Playtest Build    the same in the editor, with no upload
    [Serializable]
    public sealed class PlaytestConfig
    {
        public string round = "";
        public string uploadUrl = "";     // the upload service; empty keeps sessions on this computer
        public string uploadKey = "";     // lets the build upload, nothing else
        public string formUrl = "";       // the longer questions; {code} becomes the tester code
        public string build = "";         // version and commit
        public int suggestFinishAfter = 3;

        public const string FileName = "playtest.json";

        static PlaytestConfig current;
        static bool loaded;

        public static bool Active => Current != null;

        public static PlaytestConfig Current
        {
            get
            {
                if (loaded) return current;
                loaded = true;
                string path = Arg("-playtest-config") ?? Path.Combine(Application.streamingAssetsPath, FileName);
                try { if (File.Exists(path)) current = Parse(File.ReadAllText(path)); }
                catch (Exception e) { Debug.LogWarning("Playtest: couldn't read " + path + ": " + e.Message); }
#if UNITY_EDITOR
                if (current == null && UnityEditor.EditorPrefs.GetBool(SimulateKey, false))
                    current = new PlaytestConfig { round = "editor", build = "editor" };
#endif
                return current;
            }
        }

        // Null unless it names a round.
        public static PlaytestConfig Parse(string json)
        {
            try
            {
                PlaytestConfig c = JsonUtility.FromJson<PlaytestConfig>(json);
                if (c == null || string.IsNullOrWhiteSpace(c.round)) return null;
                c.round = c.round.Trim();
                c.uploadUrl = (c.uploadUrl ?? "").Trim().TrimEnd('/');
                if (c.suggestFinishAfter < 1) c.suggestFinishAfter = 3;
                return c;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool Uploads => !string.IsNullOrEmpty(uploadUrl);

        public string FormFor(string code) =>
            string.IsNullOrEmpty(formUrl) ? null : formUrl.Replace("{code}", Uri.EscapeDataString(code ?? ""));

        public static string Arg(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            loaded = false;
        }

#if UNITY_EDITOR
        const string SimulateKey = "Kehai.Playtest.SimulateInEditor";
        const string SimulateItem = "Kehai/Playtest/Simulate a Playtest Build in the Editor";

        [UnityEditor.MenuItem(SimulateItem)]
        static void ToggleSimulate() => UnityEditor.EditorPrefs.SetBool(SimulateKey, !UnityEditor.EditorPrefs.GetBool(SimulateKey, false));

        [UnityEditor.MenuItem(SimulateItem, true)]
        static bool ShowSimulate()
        {
            UnityEditor.Menu.SetChecked(SimulateItem, UnityEditor.EditorPrefs.GetBool(SimulateKey, false));
            return true;
        }

        [UnityEditor.MenuItem("Kehai/Playtest/Forget the Tester on This Computer")]
        static void Forget() => TesterState.Forget();
#endif
    }

    // A tester code, as the developer hands them out: T07, LAF-3. Letters, digits and dashes,
    // 2 to 16 of them; typed any case, kept upper case.
    public static class TesterCode
    {
        public static string Normalize(string raw)
        {
            if (raw == null) return null;
            string code = raw.Trim().ToUpperInvariant();
            if (code.Length < 2 || code.Length > 16) return null;
            foreach (char c in code)
                if (!(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-') return null;
            return code;
        }
    }

    // What this computer remembers about its tester, between launches.
    public static class TesterState
    {
        const string CodeKey = "Kehai.Playtest.Code";
        const string ConsentKey = "Kehai.Playtest.Consent";       // 1 send, 2 keep here
        const string CareerKey = "Kehai.Playtest.CareerOf";       // whose career Aiko's ledger holds
        const string AnsweredKey = "Kehai.Playtest.Answered";     // the code that answered the questions
        const string SuggestedKey = "Kehai.Playtest.Suggested";   // the code we suggested stopping to
        const string RunningKey = "Kehai.Playtest.Running";       // the session in progress, until a clean quit

        public static string Code
        {
            get => PlayerPrefs.GetString(CodeKey, "");
            set { PlayerPrefs.SetString(CodeKey, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool ConsentAsked => PlayerPrefs.GetInt(ConsentKey, 0) != 0;
        public static bool Sends => PlayerPrefs.GetInt(ConsentKey, 0) == 1;
        public static void SetConsent(bool send) { PlayerPrefs.SetInt(ConsentKey, send ? 1 : 2); PlayerPrefs.Save(); }

        public static string CareerOf
        {
            get => PlayerPrefs.GetString(CareerKey, "");
            set { PlayerPrefs.SetString(CareerKey, value ?? ""); PlayerPrefs.Save(); }
        }

        public static bool Answered => Code.Length > 0 && PlayerPrefs.GetString(AnsweredKey, "") == Code;
        public static void MarkAnswered() { PlayerPrefs.SetString(AnsweredKey, Code); PlayerPrefs.Save(); }

        public static bool Suggested => Code.Length > 0 && PlayerPrefs.GetString(SuggestedKey, "") == Code;
        public static void MarkSuggested() { PlayerPrefs.SetString(SuggestedKey, Code); PlayerPrefs.Save(); }

        public static string Running
        {
            get => PlayerPrefs.GetString(RunningKey, "");
            set { PlayerPrefs.SetString(RunningKey, value ?? ""); PlayerPrefs.Save(); }
        }

        // A new tester gets a new employee: Aiko starts from nothing.
        public static bool NeedsFreshCareer(string code) => !string.IsNullOrEmpty(code) && CareerOf != code;

        public static void Forget()
        {
            foreach (string key in new[] { CodeKey, ConsentKey, CareerKey, AnsweredKey, SuggestedKey, RunningKey }) PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
