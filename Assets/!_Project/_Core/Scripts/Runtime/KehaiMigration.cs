using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace Kehai
{
    // The names have changed twice, and the save data follows them. Each launch copies any
    // data still under an old name across to the name the game uses now. It copies and never
    // moves: the old files stay as they were.
    //
    //   - Until 2026-09-29 the game was called Karoshi. Unity keeps a game's data in a folder
    //     named after the product, and its PlayerPrefs in a file named after it, so that
    //     rename moved both: the Karoshi folder and settings are copied into Kehai's.
    //   - From 2026-09-29 to 2026-10-10 she was called Aiko, and her files and settings in
    //     Kehai's own folder were named after her (aiko_ledger.json, aiko_logs, aiko.blink.…).
    //     They're copied to Karen's names beside them.
    //
    // This file and its test are the only code that still spells the old names.
    public static class KehaiMigration
    {
        public const string OldProduct = "Karoshi";

        // What the game keeps in its data folder: the name it has now, and the names it can
        // have in the Karoshi folder, newest first: Aiko's, when the game ran as Kehai before
        // the product name changed, then the Karoshi one.
        static readonly (string now, string[] before)[] Entries =
        {
            ("karen_ledger.json", new[] { "aiko_ledger.json", "karen_ledger.json" }),
            ("karen_logs", new[] { "aiko_logs", "karen_logs" }),
            ("kehai_eval", new[] { "kehai_eval", "karoshi_eval" }),
            ("shift_records", new[] { "shift_records" }),
        };

        // Her files under the name she had from 2026-09-29 to 2026-10-10, in Kehai's folder.
        static readonly (string now, string aiko)[] AikoFiles =
        {
            ("karen_ledger.json", "aiko_ledger.json"),
            ("karen_logs", "aiko_logs"),
        };

        const string PrefsDone = "kehai.migrated";
        const string AikoPrefsDone = "kehai.migrated.karen";

        // Before anything reads a setting: the sound volumes load at BeforeSceneLoad.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void OnLaunch()
        {
            if (Application.productName == OldProduct) return;
            try
            {
                string now = Application.persistentDataPath;
                string before = Path.Combine(Path.GetDirectoryName(now), OldProduct);
                MigrateData(before, now);
                CopyPrefs();
                CopyAikoPrefs();
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't bring the old save data across: " + e.Message);
            }
        }

        // Aiko's files first: then Kehai's folder has its data, and the Karoshi copy leaves it
        // alone instead of bringing back an older career.
        public static void MigrateData(string before, string now)
        {
            int renamed = CopyAikoData(now);
            if (renamed > 0) Debug.Log($"Copied {renamed} of her files from Aiko's names to Karen's, in {now}.");
            int copied = CopyData(before, now);
            if (copied > 0) Debug.Log($"Brought {copied} kinds of save data across from {before}.");
        }

        // Copies her Aiko-era files to Karen's names in the same folder, unless Karen's are
        // there already. Returns how many it copied.
        public static int CopyAikoData(string folder)
        {
            if (!Directory.Exists(folder)) return 0;
            int copied = 0;
            foreach (var (now, aiko) in AikoFiles)
            {
                string to = Path.Combine(folder, now), from = Path.Combine(folder, aiko);
                if (File.Exists(to) || Directory.Exists(to)) continue;
                if (File.Exists(from)) File.Copy(from, to);
                else if (Directory.Exists(from)) CopyFolder(from, to);
                else continue;
                copied++;
            }
            return copied;
        }

        // ---- the data folder ---------------------------------------------------------------

        // True if the folder holds any of the game's data, under Karen's names or Aiko's (an
        // empty folder doesn't count).
        public static bool HasData(string folder)
        {
            foreach (var (now, _) in Entries)
                if (Holds(Path.Combine(folder, now))) return true;
            foreach (var (_, aiko) in AikoFiles)
                if (Holds(Path.Combine(folder, aiko))) return true;
            return false;
        }

        static bool Holds(string path) =>
            File.Exists(path) || (Directory.Exists(path) && Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length > 0);

        // Copies each kind of data from the old folder to the new one, under its new name.
        // Does nothing if there is no old folder, or if the new one already has data.
        // Returns how many kinds it copied.
        public static int CopyData(string before, string now)
        {
            if (!Directory.Exists(before) || SameFolder(before, now) || HasData(now)) return 0;
            int copied = 0;
            foreach (var (target, sources) in Entries)
            {
                string to = Path.Combine(now, target);
                foreach (string name in sources)
                {
                    string from = Path.Combine(before, name);
                    if (File.Exists(from))
                    {
                        Directory.CreateDirectory(now);
                        File.Copy(from, to, true);
                    }
                    else if (Directory.Exists(from)) CopyFolder(from, to);
                    else continue;
                    copied++;
                    break;
                }
            }
            return copied;
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(file));
                if (file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                    File.WriteAllText(dest, RenameEvalKeys(File.ReadAllText(file)));
                else
                    File.Copy(file, dest, true);
            }
            foreach (string dir in Directory.GetDirectories(from))
                CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        // Eval runs recorded her numbers as "karen_catches" and so on, then for a while as
        // "aiko_catches"; now they're "karen_…" again.
        public static string RenameEvalKeys(string jsonl) => jsonl.Replace("\"aiko_", "\"karen_");

        static bool SameFolder(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                          Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

        // ---- PlayerPrefs -------------------------------------------------------------------

        public enum PrefKind { Int, Float, Text }

        public struct Pref
        {
            public string Key;
            public PrefKind Kind;
            public int Int;
            public float Float;
            public string Text;
        }

        // On a Mac, PlayerPrefs live in ~/Library/Preferences/unity.<company>.<product>.plist.
        // No Windows or Linux build was ever made under the old name, so there is nothing to
        // bring across there.
        static void CopyPrefs()
        {
            if (PlayerPrefs.GetInt(PrefsDone, 0) == 1) return;
            bool mac = Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer;
            string plist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                                        "Library", "Preferences", $"unity.{Application.companyName}.{OldProduct}.plist");
            if (mac && File.Exists(plist))
            {
                foreach (Pref p in RenamePrefs(ParsePlist(PlistAsXml(plist))))
                {
                    if (PlayerPrefs.HasKey(p.Key)) continue;
                    switch (p.Kind)
                    {
                        case PrefKind.Int: PlayerPrefs.SetInt(p.Key, p.Int); break;
                        case PrefKind.Float: PlayerPrefs.SetFloat(p.Key, p.Float); break;
                        default: PlayerPrefs.SetString(p.Key, p.Text); break;
                    }
                }
            }
            PlayerPrefs.SetInt(PrefsDone, 1);
            PlayerPrefs.Save();
        }

        // The game's own settings, under their new names; Unity's bookkeeping stays behind.
        public static List<Pref> RenamePrefs(IEnumerable<Pref> old)
        {
            var renamed = new List<Pref>();
            foreach (Pref p in old)
            {
                string key = RenameKey(p.Key);
                if (key == PrefsDone) continue;
                if (!key.StartsWith("Kehai.", StringComparison.Ordinal) && !key.StartsWith("kehai.", StringComparison.Ordinal) &&
                    !key.StartsWith("karen.", StringComparison.Ordinal)) continue;
                Pref q = p;
                q.Key = key;
                renamed.Add(q);
            }
            return renamed;
        }

        // "Karoshi.Volume.Karen" → "Kehai.Volume.Karen", "aiko.blink.consent" → "karen.blink.consent".
        public static string RenameKey(string key) =>
            key.Replace("Karoshi", "Kehai").Replace("karoshi", "kehai").Replace("Aiko", "Karen").Replace("aiko", "karen");

        // Her settings under Aiko's names, in Kehai's own PlayerPrefs, with the name each has
        // now. PlayerPrefs can't list its keys on every platform, so they're listed here: her
        // volume, her floor cone, and the webcam's consent and calibration, kept per helper
        // (the Mac's Vision one, the Python one's methods, and before the first packet).
        static readonly string[] BlinkSources = { "vision", "ear", "blendshapes", "cnn", "webcam", "none" };

        public static List<(string aiko, string now, PrefKind kind)> AikoPrefKeys()
        {
            var keys = new List<(string, string, PrefKind)>
            {
                ("Kehai.Volume.Aiko", "Kehai.Volume.Karen", PrefKind.Float),
                ("Kehai.AikoCone", "Kehai.KarenCone", PrefKind.Int),
                ("aiko.blink.consent", "karen.blink.consent", PrefKind.Int),
            };
            foreach (string src in BlinkSources)
                foreach (string end in new[] { ".open", ".closed" })
                    keys.Add(($"aiko.blink.cal.{src}{end}", $"karen.blink.cal.{src}{end}", PrefKind.Float));
            return keys;
        }

        static void CopyAikoPrefs()
        {
            if (PlayerPrefs.GetInt(AikoPrefsDone, 0) == 1) return;
            foreach (var (aiko, now, kind) in AikoPrefKeys())
            {
                if (!PlayerPrefs.HasKey(aiko) || PlayerPrefs.HasKey(now)) continue;
                if (kind == PrefKind.Int) PlayerPrefs.SetInt(now, PlayerPrefs.GetInt(aiko));
                else PlayerPrefs.SetFloat(now, PlayerPrefs.GetFloat(aiko));
            }
            PlayerPrefs.SetInt(AikoPrefsDone, 1);
            PlayerPrefs.Save();
        }

        // A plist's top-level dictionary, in the XML form `plutil -convert xml1` writes.
        public static List<Pref> ParsePlist(string xml)
        {
            var prefs = new List<Pref>();
            if (string.IsNullOrWhiteSpace(xml)) return prefs;
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                doc.Load(reader);
            XmlNode dict = doc.SelectSingleNode("/plist/dict");
            if (dict == null) return prefs;

            string key = null;
            foreach (XmlNode node in dict.ChildNodes)
            {
                if (node.NodeType != XmlNodeType.Element) continue;
                if (node.Name == "key") { key = node.InnerText; continue; }
                if (key == null) continue;
                var p = new Pref { Key = key };
                key = null;
                switch (node.Name)
                {
                    case "integer":
                        if (!long.TryParse(node.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) continue;
                        p.Kind = PrefKind.Int;
                        p.Int = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, i));
                        break;
                    case "real":
                        if (!double.TryParse(node.InnerText, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) continue;
                        p.Kind = PrefKind.Float;
                        p.Float = (float)d;
                        break;
                    case "string":
                        p.Kind = PrefKind.Text;
                        p.Text = node.InnerText;
                        break;
                    default:
                        continue;
                }
                prefs.Add(p);
            }
            return prefs;
        }

        // The plist on disk is usually binary; plutil prints it as XML.
        static string PlistAsXml(string path)
        {
            var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/plutil")
            {
                Arguments = $"-convert xml1 -o - \"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(info))
            {
                string xml = process.StandardOutput.ReadToEnd();
                return process.WaitForExit(5000) && process.ExitCode == 0 ? xml : "";
            }
        }
    }
}
