using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kehai.Playtest
{
    // Playtest v2's game side, the parts that talk to the upload service (tools/playtest/worker),
    // kept apart from the screens so EditMode tests can check them.

    // C2.2: what a tester agreed to. Version 2 adds "…and may be used to train Karen"; one click
    // ("Play and send") covers sending and training. A tester who agreed to v1 is asked again.
    public static class PlaytestConsent
    {
        public const string Version = "v2";
        public const string TrainingLine = "and may be used to train " + GameNames.Antagonist + ".";   // the line v2 adds
        public const string DeletionEmail = "tokenlimitlabs@gmail.com";   // Q7
    }

    // What travels with each packed zip, in a sidecar beside it (<zip>.json): the consent it was
    // packed under, the build, and the summary's schema. A zip with no sidecar was packed by an
    // older build: it goes up without consent headers, so the service counts it as v1 and it
    // never trains her.
    [Serializable]
    public sealed class OutboxMeta
    {
        public string consent = "";
        public bool training;
        public string build = "";
        public int schema = PlaytestSummary.Schema;

        public static string PathFor(string zip) => zip + ".json";
        public static string SummaryPathFor(string zip) => Path.ChangeExtension(zip, null) + ".summary.json";

        public static OutboxMeta Read(string zip)
        {
            try { return File.Exists(PathFor(zip)) ? JsonUtility.FromJson<OutboxMeta>(File.ReadAllText(PathFor(zip))) : null; }
            catch (Exception) { return null; }
        }

        public void Write(string zip) => File.WriteAllText(PathFor(zip), JsonUtility.ToJson(this));
    }

    // C2.10: what a send's answer means for the zip (or summary, or events) that was sent.
    public enum SendResult { Sent, Queued, Rejected, Gone }

    public static class SendAnswer
    {
        // 2xx: received (200 is a retry of what it already has). 410: the tester or session was
        // deleted: stop, and drop the local copy. 400, 409, 413: it will never be accepted, so it
        // stays on this computer instead of being retried forever. Anything else (no network, a
        // timeout, 401 from a misconfigured build, 5xx): try again next time.
        public static SendResult Of(long status, bool networkError)
        {
            if (networkError || status == 0) return SendResult.Queued;
            if (status >= 200 && status < 300) return SendResult.Sent;
            if (status == 410) return SendResult.Gone;
            if (status == 400 || status == 409 || status == 413) return SendResult.Rejected;
            return SendResult.Queued;
        }
    }

    // C2.5: the funnel. Events made before the consent page is answered wait in memory: kept if
    // the tester agrees, thrown away if not. After agreeing, each goes to <outbox>/events.jsonl at
    // once, so one made offline is sent on a later launch.
    public static class PlaytestEvents
    {
        public static readonly string[] Kinds = { "launch", "consent", "walkthrough", "first_shift", "shift", "end", "sent" };
        public const int Batch = 100;

        static readonly List<string> beforeConsent = new List<string>();
        public static string QueuePath => Path.Combine(PlaytestPaths.Outbox, "events.jsonl");
        public static int Buffered => beforeConsent.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => beforeConsent.Clear();

        public static string Line(string kind, string tester, string session, string round, string build, DateTime utc) =>
            MiniJson.Serialize(new Dictionary<string, object>
            {
                ["id"] = Guid.NewGuid().ToString("N"), ["tester"] = tester, ["session"] = session, ["round"] = round,
                ["build"] = build ?? "", ["kind"] = kind, ["at"] = utc.ToString("o", CultureInfo.InvariantCulture),
            });

        // `consent`: null while the page hasn't been answered, else whether they agreed.
        public static void Record(string kind, bool? consent, string tester, string session, string round, string build)
        {
            if (Array.IndexOf(Kinds, kind) < 0 || consent == false) return;
            string line = Line(kind, tester, session, round, build, DateTime.UtcNow);
            if (consent == null) { beforeConsent.Add(line); return; }
            Append(new[] { line });
        }

        public static void Answered(bool agreed)
        {
            if (agreed) Append(beforeConsent);
            beforeConsent.Clear();
        }

        static void Append(IEnumerable<string> lines)
        {
            try
            {
                Directory.CreateDirectory(PlaytestPaths.Outbox);
                File.AppendAllLines(QueuePath, lines, new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("Playtest: couldn't queue an event: " + e.Message); }
        }

        public static List<string> Queued()
        {
            try { return File.Exists(QueuePath) ? File.ReadAllLines(QueuePath).Where(l => l.Trim().Length > 0).ToList() : new List<string>(); }
            catch (Exception) { return new List<string>(); }
        }

        // The first `count` queued lines were received: they leave the queue.
        public static void Drop(int count)
        {
            List<string> left = Queued().Skip(count).ToList();
            try
            {
                if (left.Count == 0) File.Delete(QueuePath);
                else File.WriteAllLines(QueuePath, left, new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("Playtest: couldn't update the event queue: " + e.Message); }
        }

        public static string Body(IEnumerable<string> lines) => "{\"events\":[" + string.Join(",", lines) + "]}";
    }

    // C2.6: a session's numbers, for POST /summaries beside its zip: the computer, the frame rate,
    // each shift (from ShiftAnalysis), the answers and the bug notes.
    public sealed class PlaytestSummary
    {
        public const int Schema = 2;

        public string Round, Tester, Launch, Build, Computer, Ended;
        public bool Training;
        public float Fps, Minutes;
        public readonly List<Dictionary<string, object>> Shifts = new List<Dictionary<string, object>>();
        public readonly List<Dictionary<string, object>> Moments = new List<Dictionary<string, object>>();
        public readonly Dictionary<string, object> Answers = new Dictionary<string, object>();

        public static Dictionary<string, object> ShiftRow(int n, Kehai.Karen.ShiftAnalysis a) => new Dictionary<string, object>
        {
            ["n"] = n, ["seconds"] = Round1(a.Length), ["clocked_out"] = a.ClockedOut, ["caught"] = a.Catches, ["spotted"] = a.Spotted,
            ["closest_m"] = a.ClosestDistance < float.MaxValue ? (object)Round1(a.ClosestDistance) : null,
            ["trick_sounds"] = a.Tricks.Values.Sum(), ["lowest_energy"] = Mathf.Round(a.LowestEnergy * 100f) / 100f,
        };

        public void Moment(int shift, float replaySeconds, string note) => Moments.Add(new Dictionary<string, object>
        {
            ["shift"] = shift, ["replay_s"] = Round1(replaySeconds), ["note"] = note ?? "",
        });

        public string ToJson()
        {
            var taps = PlaytestScreens.Questions.Select(q => Answers.TryGetValue(q.id, out object v) ? v as string : null).ToList<object>();
            var answers = new Dictionary<string, object> { ["taps"] = taps };
            for (int i = 0; i < PlaytestScreens.TypedQuestions.Length; i++)
                answers["a" + (i + 1)] = Answers.TryGetValue("a" + (i + 1), out object v) ? v : "";
            answers["nickname"] = Answers.TryGetValue("nickname", out object nick) ? nick : "";
            return MiniJson.Serialize(new Dictionary<string, object>
            {
                ["round"] = Round, ["tester"] = Tester, ["launch"] = Launch, ["build"] = Build ?? "", ["schema"] = Schema,
                ["consent"] = new Dictionary<string, object> { ["version"] = PlaytestConsent.Version, ["training"] = Training },
                ["computer"] = Computer ?? "", ["fps"] = Round1(Fps), ["minutes"] = Round1(Minutes), ["ended"] = Ended ?? "",
                ["shifts"] = Shifts.Cast<object>().ToList(), ["answers"] = answers, ["moments"] = Moments.Cast<object>().ToList(),
            });
        }

        static float Round1(float v) => Mathf.Round(v * 10f) / 10f;
    }

    // C2.7 (Q6): is a newer build out? GET <upload>/latest says the current one.
    public static class PlaytestUpdate
    {
        public static string Latest { get; private set; }     // the newer version, or null
        public static string DownloadUrl { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Latest = null; DownloadUrl = null; }

        public static void Saw(string latestJson, string ours, string uploadUrl, RuntimePlatform platform)
        {
            Dictionary<string, object> latest = MiniJson.ParseObject(latestJson ?? "");
            string version = latest?.GetString("version");
            if (string.IsNullOrEmpty(version) || !IsNewer(version, ours)) return;
            Latest = version;
            string which = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor ? "windows" : "mac";
            DownloadUrl = uploadUrl.TrimEnd('/') + "/download/" + which;
        }

        // 0.5.3 is newer than 0.5.2 and than 0.5.2-beta.4; 0.5.2 is newer than 0.5.2-beta.4.
        // Ours may carry its commit after a space ("0.5.2-beta.4 85f67f4").
        public static bool IsNewer(string latest, string ours)
        {
            ours = (ours ?? "").Split(' ')[0];
            if (string.IsNullOrEmpty(ours)) return false;
            (int[] a, string pa) = Parse(latest);
            (int[] b, string pb) = Parse(ours);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            if (pa == pb) return false;
            if (pa.Length == 0) return true;       // a release is newer than its pre-releases
            if (pb.Length == 0) return false;
            return ComparePre(pa, pb) > 0;
        }

        static (int[] numbers, string pre) Parse(string v)
        {
            string core = v, pre = "";
            int dash = v.IndexOf('-');
            if (dash >= 0) { core = v.Substring(0, dash); pre = v.Substring(dash + 1); }
            int plus = pre.IndexOf('+');
            if (plus >= 0) pre = pre.Substring(0, plus);
            string[] parts = core.Split('.');
            var n = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out n[i]);
            return (n, pre);
        }

        static int ComparePre(string a, string b)
        {
            string[] x = a.Split('.'), y = b.Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                if (i >= x.Length) return -1;
                if (i >= y.Length) return 1;
                bool nx = int.TryParse(x[i], out int ix), ny = int.TryParse(y[i], out int iy);
                int c = nx && ny ? ix.CompareTo(iy) : string.CompareOrdinal(x[i], y[i]);
                if (c != 0) return c;
            }
            return 0;
        }
    }
}
