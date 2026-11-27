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
    //   <persistent data>/playtest/outbox/<round>_<code>_<launch time>.zip  packed, waiting to be sent,
    //       with <zip>.json (its consent and build) and <name>.summary.json (its numbers) beside it
    //   <persistent data>/playtest/outbox/rejected/   what the service won't take: kept here
    //   <persistent data>/playtest/local/             sessions of a tester who didn't agree to send
    public static class PlaytestPaths
    {
        public static string RootOverride;   // EditMode tests: a temp folder instead of the real one
        public static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "playtest");
        public static string Sessions => Path.Combine(Root, "sessions");
        public static string Outbox => Path.Combine(Root, "outbox");
        public static string Local => Path.Combine(Root, "local");     // packed without consent to send: never sent
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
        public static string NewStamp(DateTime utc) =>
            utc.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");

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

    // Sends the outbox to the upload service, oldest first (playtest v2):
    //   each zip      PUT  <upload>/sessions/<round>/<tester>/<launch>.zip, with the consent it was packed under
    //   its summary   POST <upload>/summaries (C2.6), once the zip is in
    //   the funnel    POST <upload>/events, a hundred at a time (C2.5)
    // A zip leaves the outbox only once the service has it (C2.10). One the service will never take
    // (too big, or other bytes under its name) moves to outbox/rejected and stays on this computer;
    // one for a deleted tester is dropped. Anything else (offline, a timeout) waits for the next launch.
    // Nothing is sent unless the tester agreed.
    public static class PlaytestUploader
    {
        public static bool Busy { get; private set; }
        public static float Progress { get; private set; }
        public static string LastError { get; private set; }
        public static int LastSent { get; private set; }        // the last send's: received
        public static int LastRejected { get; private set; }    // the last send's: kept here for good

        public static string RejectedFolder => Path.Combine(PlaytestPaths.Outbox, "rejected");

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

        public static string[] Kept() =>
            Directory.Exists(RejectedFolder) ? Directory.GetFiles(RejectedFolder, "*.zip") : new string[0];

        // The last two underscore-delimited fields are the date and time, with an ID on new launches.
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

        // The headers a zip goes up with: its consent (none for a zip an older build packed, which
        // the service then counts as v1, never training her), its build and its summary's schema.
        public static Dictionary<string, string> HeadersFor(OutboxMeta meta, string uploadKey)
        {
            var headers = new Dictionary<string, string> { ["Content-Type"] = "application/zip", ["X-Kehai-Key"] = uploadKey ?? "" };
            if (meta == null || string.IsNullOrEmpty(meta.consent)) return headers;
            headers["X-Kehai-Consent"] = meta.consent;
            headers["X-Kehai-Training"] = meta.training ? "1" : "0";
            if (!string.IsNullOrEmpty(meta.build)) headers["X-Kehai-Build"] = meta.build;
            headers["X-Kehai-Schema"] = meta.schema.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return headers;
        }

        // A zip and its sidecars: moved to the rejected folder, or deleted.
        public static void Settle(string zip, SendResult result)
        {
            string[] files = { zip, OutboxMeta.PathFor(zip), OutboxMeta.SummaryPathFor(zip) };
            try
            {
                if (result == SendResult.Rejected) Directory.CreateDirectory(RejectedFolder);
                foreach (string f in files)
                {
                    if (!File.Exists(f)) continue;
                    if (result == SendResult.Rejected)
                    {
                        string to = Path.Combine(RejectedFolder, Path.GetFileName(f));
                        if (File.Exists(to)) File.Delete(to);
                        File.Move(f, to);
                    }
                    else File.Delete(f);
                }
            }
            catch (Exception e) { Debug.LogWarning($"Playtest: couldn't tidy {Path.GetFileName(zip)}: {e.Message}"); }
        }

        public static IEnumerator SendAll(PlaytestConfig config, Action<int, int> done = null)
        {
            if (Busy || config == null || !config.Uploads || !TesterState.Sends) { done?.Invoke(0, Waiting().Length); yield break; }
            Busy = true;
            LastError = null;
            LastSent = LastRejected = 0;
            string[] zips = Waiting();
            bool offline = false;
            for (int i = 0; i < zips.Length && !offline; i++)
            {
                string zip = zips[i], url = UrlFor(config.uploadUrl, zip);
                if (url == null) continue;
                byte[] body;
                try { body = File.ReadAllBytes(zip); }
                catch (Exception e) { LastError = e.Message; continue; }

                SendResult result = SendResult.Queued;
                int index = i;
                yield return Send(UnityWebRequest.Put(url, body), HeadersFor(OutboxMeta.Read(zip), config.uploadKey),
                    r => result = r, p => Progress = (index + p) / zips.Length);
                if (result == SendResult.Sent)
                {
                    string summary = OutboxMeta.SummaryPathFor(zip);
                    if (File.Exists(summary))
                    {
                        SendResult s = SendResult.Queued;
                        yield return Send(Post(config.uploadUrl + "/summaries", File.ReadAllBytes(summary)),
                            new Dictionary<string, string> { ["Content-Type"] = "application/json", ["X-Kehai-Key"] = config.uploadKey ?? "" },
                            r => s = r, null);
                        // The zip stays until its summary is in too: sent again, the zip answers 200.
                        if (s == SendResult.Queued) { offline = true; break; }
                    }
                    LastSent++;
                    Settle(zip, SendResult.Sent);
                }
                else if (result == SendResult.Rejected) { LastRejected++; Settle(zip, SendResult.Rejected); }
                else if (result == SendResult.Gone) Settle(zip, SendResult.Gone);
                else offline = true;   // the network is probably down: try the rest next time
            }
            if (!offline) yield return SendEvents(config);
            current = null;
            Progress = 1f;
            Busy = false;
            done?.Invoke(LastSent, Waiting().Length);
        }

        static IEnumerator SendEvents(PlaytestConfig config)
        {
            while (true)
            {
                List<string> queued = PlaytestEvents.Queued();
                if (queued.Count == 0) yield break;
                List<string> batch = queued.Take(PlaytestEvents.Batch).ToList();
                SendResult result = SendResult.Queued;
                yield return Send(Post(config.uploadUrl + "/events", Encoding.UTF8.GetBytes(PlaytestEvents.Body(batch))),
                    new Dictionary<string, string> { ["Content-Type"] = "application/json", ["X-Kehai-Key"] = config.uploadKey ?? "" },
                    r => result = r, null);
                if (result == SendResult.Queued) yield break;
                PlaytestEvents.Drop(batch.Count);   // received, or never to be accepted
            }
        }

        static UnityWebRequest Post(string url, byte[] body) =>
            new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST) { uploadHandler = new UploadHandlerRaw(body), downloadHandler = new DownloadHandlerBuffer() };

        static IEnumerator Send(UnityWebRequest request, Dictionary<string, string> headers, Action<SendResult> answer, Action<float> progress)
        {
            using (request)
            {
                current = request;
                foreach (KeyValuePair<string, string> h in headers) request.SetRequestHeader(h.Key, h.Value);
                request.timeout = 120;
                UnityWebRequestAsyncOperation op = request.SendWebRequest();
                while (!op.isDone)
                {
                    progress?.Invoke(request.uploadProgress);
                    yield return null;
                }
                bool network = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError;
                SendResult result = SendAnswer.Of(request.responseCode, network);
                if (result != SendResult.Sent)
                {
                    LastError = $"{request.responseCode} {request.error}";
                    Debug.LogWarning($"Playtest: {request.method} {Path.GetFileName(request.url)}: {LastError} ({result})");
                }
                answer(result);
            }
        }
    }
}
