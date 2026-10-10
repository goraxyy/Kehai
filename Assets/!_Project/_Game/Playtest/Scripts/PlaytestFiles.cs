using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kehai.Playtest
{
    // Where a playtest keeps its files on the tester's computer:
    //   <persistent data>/playtest/sessions/<launch time>/session.jsonl   the session as it happens
    //   <persistent data>/playtest/outbox/<round>_<code>_<launch time>.zip  packed, waiting to be sent
    public static class PlaytestPaths
    {
        public static string Root => Path.Combine(Application.persistentDataPath, "playtest");
        public static string Sessions => Path.Combine(Root, "sessions");
        public static string Outbox => Path.Combine(Root, "outbox");
        public static string LogName = "session.jsonl";
    }

    // The session, one JSON object per line, so a crash loses at most the last couple of
    // seconds. Every line has "t" (seconds since the launch, real time) and "k" (what it is).
    public sealed class SessionLog : IDisposable
    {
        readonly StreamWriter writer;
        public string FilePath { get; }

        public SessionLog(string path)
        {
            FilePath = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        }

        public void Write(float t, string kind, Dictionary<string, object> fields = null)
        {
            var line = new Dictionary<string, object> { ["t"] = Mathf.Round(t * 100f) / 100f, ["k"] = kind };
            if (fields != null) foreach (KeyValuePair<string, object> f in fields) line[f.Key] = f.Value;
            writer.WriteLine(MiniJson.Serialize(line));
        }

        public void Flush() => writer.Flush();

        public void Dispose()
        {
            writer.Flush();
            writer.Dispose();
        }

        // The first line of a session's log: when it started (UTC, ISO 8601), if it says.
        public static DateTime? StartedUtc(string logPath)
        {
            try
            {
                using var reader = new StreamReader(new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                Dictionary<string, object> first = MiniJson.ParseObject(reader.ReadLine() ?? "");
                string utc = first?.GetString("utc");
                return utc != null && DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d) ? d.ToUniversalTime() : (DateTime?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    // A finished session, packed for sending: its log, the shifts and in-between stretches it
    // recorded (data and 3D replay; the HTML reports are rebuilt on the developer's side),
    // Karen's thought logs and ledger, and the game's own log (the one before it too, after a crash).
    public static class PlaytestPackage
    {
        public static string ZipName(string round, string code, string stamp) => $"{Safe(round)}_{Safe(code)}_{stamp}.zip";

        static string Safe(string s) => string.IsNullOrEmpty(s) ? "none" : new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '-').ToArray());

        // The files a session made, from the folders the game writes to.
        public static List<(string path, string entry)> Collect(string sessionFolder, DateTime startedUtc, string shiftFolder, string thoughtFolder, string ledgerPath, string playerLog, bool previousLogToo)
        {
            var files = new List<(string, string)>();
            string log = Path.Combine(sessionFolder, PlaytestPaths.LogName);
            if (File.Exists(log)) files.Add((log, PlaytestPaths.LogName));

            DateTime since = startedUtc.AddSeconds(-5);
            if (Directory.Exists(shiftFolder))
                foreach (string f in Directory.GetFiles(shiftFolder))
                {
                    string name = Path.GetFileName(f);
                    bool ours = name.StartsWith("shift_") || name.StartsWith("interlude_");
                    // data, clip markers, replay; .krec.part is a replay a crash cut short
                    bool wanted = name.EndsWith(".krec") || name.EndsWith(".json") || name.EndsWith(".krec.part");
                    if (ours && wanted && File.GetLastWriteTimeUtc(f) >= since) files.Add((f, "shifts/" + name));
                }
            if (thoughtFolder != null && Directory.Exists(thoughtFolder))
                foreach (string f in Directory.GetFiles(thoughtFolder))
                    if (File.GetLastWriteTimeUtc(f) >= since) files.Add((f, "karen_logs/" + Path.GetFileName(f)));
            if (ledgerPath != null && File.Exists(ledgerPath)) files.Add((ledgerPath, "karen_ledger.json"));
            if (!string.IsNullOrEmpty(playerLog) && File.Exists(playerLog)) files.Add((playerLog, "Player.log"));
            if (previousLogToo && !string.IsNullOrEmpty(playerLog))
            {
                string prev = Path.Combine(Path.GetDirectoryName(playerLog), "Player-prev.log");
                if (File.Exists(prev)) files.Add((prev, "Player-prev.log"));
            }
            return files;
        }

        // Zips them into the outbox and returns the zip. Files still open for writing (the
        // game's log) are read as they are.
        public static string Pack(string zipPath, IEnumerable<(string path, string entry)> files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(zipPath));
            string part = zipPath + ".part";
            using (var stream = new FileStream(part, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var seen = new HashSet<string>();
                foreach ((string path, string entry) in files)
                {
                    if (!seen.Add(entry)) continue;
                    try
                    {
                        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using Stream target = zip.CreateEntry(entry, System.IO.Compression.CompressionLevel.Optimal).Open();
                        source.CopyTo(target);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Playtest: left {entry} out of the package: {e.Message}");
                    }
                }
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(part, zipPath);
            return zipPath;
        }
    }

    // Sends the outbox to the upload service, oldest first; a zip leaves the outbox once it's
    // been received. One that can't be sent stays for the next launch.
    //
    //   PUT <uploadUrl>/sessions/<round>/<code>/<launch time>.zip
    //   X-Kehai-Key: <uploadKey>        Content-Type: application/zip
    public static class PlaytestUploader
    {
        public static bool Busy { get; private set; }
        public static float Progress { get; private set; }
        public static string LastError { get; private set; }

        static UnityWebRequest current;

        // Gives up on the send in progress (the tester didn't want to wait); what's left stays queued.
        public static void Abandon()
        {
            if (current != null) current.Abort();
            current = null;
            Busy = false;
        }

        public static string[] Waiting() =>
            Directory.Exists(PlaytestPaths.Outbox)
                ? Directory.GetFiles(PlaytestPaths.Outbox, "*.zip").OrderBy(File.GetLastWriteTimeUtc).ToArray()
                : new string[0];

        // round_code_yyyyMMdd_HHmmss.zip → its address on the upload service.
        public static string UrlFor(string baseUrl, string zipPath)
        {
            string name = Path.GetFileNameWithoutExtension(zipPath);
            string[] parts = name.Split('_');
            if (parts.Length < 4) return null;
            string stamp = parts[parts.Length - 2] + "_" + parts[parts.Length - 1];
            string code = parts[parts.Length - 3];
            string round = string.Join("_", parts.Take(parts.Length - 3));
            return $"{baseUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(round)}/{Uri.EscapeDataString(code)}/{stamp}.zip";
        }

        public static IEnumerator SendAll(PlaytestConfig config, Action<int, int> done = null)
        {
            if (Busy || config == null || !config.Uploads) { done?.Invoke(0, Waiting().Length); yield break; }
            Busy = true;
            LastError = null;
            string[] zips = Waiting();
            int sent = 0;
            for (int i = 0; i < zips.Length; i++)
            {
                string url = UrlFor(config.uploadUrl, zips[i]);
                if (url == null) continue;
                byte[] body;
                try { body = File.ReadAllBytes(zips[i]); }
                catch (Exception e) { LastError = e.Message; continue; }

                using UnityWebRequest request = UnityWebRequest.Put(url, body);
                current = request;
                request.SetRequestHeader("Content-Type", "application/zip");
                request.SetRequestHeader("X-Kehai-Key", config.uploadKey ?? "");
                request.timeout = 120;
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    Progress = (i + request.uploadProgress) / zips.Length;
                    yield return null;
                }
                if (request.result == UnityWebRequest.Result.Success)
                {
                    sent++;
                    try { File.Delete(zips[i]); } catch (Exception) { }
                }
                else
                {
                    LastError = $"{request.responseCode} {request.error}";
                    Debug.LogWarning($"Playtest: couldn't send {Path.GetFileName(zips[i])}: {LastError}");
                    break;   // the network is probably down: try the rest next time
                }
            }
            current = null;
            Progress = 1f;
            Busy = false;
            done?.Invoke(sent, Waiting().Length);
        }
    }
}
