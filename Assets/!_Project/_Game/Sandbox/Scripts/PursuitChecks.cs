using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    [RequireComponent(typeof(PursuitSandbox))]
    public sealed class PursuitChecks : MonoBehaviour
    {
        const float HuntSeconds = 60f;
        const float HuntStartLimit = 8f;
        static readonly Vector3 GapNorth = new Vector3(0f, 0f, 5.3f);
        static readonly Vector3 GapSouth = new Vector3(0f, 0f, 1.5f);
        static readonly Vector3 BehindShelf = new Vector3(-3.6f, 0f, 1.5f);

        [Serializable]
        sealed class Row
        {
            public string name, expected, observed, replay;
            public bool pass;
        }

        [Serializable]
        sealed class Report
        {
            public Row[] rows;
        }

        public static bool Done { get; private set; }
        public static int ExitCode { get; private set; }
        public static string ReportPath { get; private set; }

        readonly List<Row> rows = new List<Row>();
        PursuitSandbox sandbox;
        SandboxBot bot;
        PursuitCheckRecording recording;
        string folder;
        Row row;

        KarenBrain Brain => sandbox.Brain;
        SightSensor Eye => Brain.Body.Sight;
        PostSightPursuit Pursuit => Brain.Pursuit;
        bool Hunting => Brain.IsChasing && Pursuit.Active(Time.time);
        bool Confirmed => Hunting && Eye.SeesNow && Eye.Awareness >= Eye.seeAt;

        // Per-frame observations shared by the scenarios. None of these feed Karen's decisions.
        string trick;
        float runningOnMetres;
        Vector3 previousPosition;
        bool sawBlock, wasHunting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Done = false;
            ExitCode = 0;
            ReportPath = null;
        }

        IEnumerator Start()
        {
            sandbox = GetComponent<PursuitSandbox>();
            string stem = "pursuit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            folder = Path.Combine(Application.persistentDataPath, "sandbox_checks", stem);
            Directory.CreateDirectory(folder);

            float until = Time.realtimeSinceStartup + 10f;
            while (!sandbox.Ready && Time.realtimeSinceStartup < until) yield return null;
            if (!sandbox.Ready || sandbox.Player == null || !Brain.Body.Agent.isOnNavMesh)
            {
                rows.Add(new Row { name = "setup", expected = "A ready brain and player on the sandbox floor",
                    observed = "The sandbox did not become ready with a player and a usable NavMesh." });
                Finish();
                yield break;
            }

            Brain.Body.Silent = true;
            SandboxBot.FixFrames();
            bot = new SandboxBot(sandbox.Player.GetComponent<PlayerMotor>());
            var listener = sandbox.Player.GetComponentInChildren<AudioListener>();
            if (listener != null) listener.enabled = false;

            yield return Run("corner-search", "A tall shelf blocks sight. Karen runs on and searches an empty spot.",
                GapNorth, PursuitSandbox.KarenSpot, CornerSearch);
            yield return Run("fridge-expiry", "The unseen fridge entry holds. Karen searches and gives up 60 seconds after the last sighting.",
                new Vector3(8f, 0f, 1.4f), new Vector3(-8f, 0f, 1.4f), FridgeExpiry);
            yield return Run("reacquire", "A new confirmed sighting after the gap ends the search and renews the 60-second window.",
                GapNorth, PursuitSandbox.KarenSpot, Reacquire);
            yield return Run("catch-restart", "Contact freezes the sandbox with its shift active. Restart clears the catch, awareness and hunt.",
                PursuitSandbox.KarenSpot + Vector3.right * 4f, PursuitSandbox.KarenSpot, CatchAndRestart);
            Finish();
        }

        IEnumerator Run(string name, string expected, Vector3 playerAt, Vector3 karenAt, Func<IEnumerator> check)
        {
            sandbox.Restart();
            sandbox.FridgeDoor.Set(false, instantly: true);
            Brain.Body.Warp(PursuitSandbox.World(karenAt));
            Brain.Body.transform.rotation = Quaternion.Euler(0f, PursuitSandbox.KarenYaw, 0f);
            bot.Sprint = false;
            bot.Teleport(PursuitSandbox.World(playerAt));
            yield return null;

            trick = null;
            sawBlock = false;
            runningOnMetres = 0f;
            previousPosition = Brain.Body.Position;
            wasHunting = false;
            row = new Row { name = name, expected = expected, replay = name + ".krec" };
            recording = new PursuitCheckRecording(Path.Combine(folder, row.replay), sandbox);
            sandbox.StartRunning();
            yield return check();
            sandbox.Freeze();
            if (trick != null)
            {
                row.pass = false;
                row.observed += $" A trick ran: {trick}.";
            }
            recording.Mark("CHECK", row.observed);
            recording.Finish();
            recording = null;

            rows.Add(row);
            Debug.Log($"PursuitChecks: {(row.pass ? "PASS" : "FAIL")} {name}. {row.observed}");
        }

        void LateUpdate()
        {
            if (recording == null) return;
            Tactic tactic = Brain.CurrentPlan?.Tactic;
            if (tactic != null && tactic.Tier > 0) trick = tactic.Id;

            Vector3 position = Brain.Body.Position;
            bool hunting = Hunting;
            if ((hunting || wasHunting) && !Eye.SeesNow && SightBlocked())
            {
                sawBlock = true;
                if (hunting && !Pursuit.Searching && Brain.Body.CurrentPace == KarenBody.Pace.Run)
                    runningOnMetres += Vector3.Distance(position, previousPosition);
            }
            previousPosition = position;
            wasHunting = hunting;
            recording.Sample();
        }

        bool SightBlocked()
        {
            PlayerPresence player = sandbox.Player;
            return !LineOfSight.Clear(Eye.Eye, player.Head) && !LineOfSight.Clear(Eye.Eye, player.Chest)
                && !LineOfSight.Clear(Eye.Eye, player.Feet);
        }

        IEnumerator CornerSearch()
        {
            yield return Until(() => Confirmed, HuntStartLimit);
            if (!Confirmed) { MissedHunt(); yield break; }
            yield return CrossGap();

            yield return Until(() => Pursuit.SpotsSearched > 0 || !Hunting || Eye.SeesNow, 12f);
            Result(At(BehindShelf) && sawBlock && runningOnMetres >= 0.25f && Pursuit.SpotsSearched > 0 &&
                Hunting && Pursuit.Searching && !Eye.SeesNow && !sandbox.Caught,
                $"Reached cover: {At(BehindShelf)}. Sight blocked during the hunt: {sawBlock}. " +
                $"Ran {runningOnMetres:0.00} m with sight blocked. Spots searched: {Pursuit.SpotsSearched}. " + HuntState());
        }

        IEnumerator CrossGap()
        {
            recording.Mark("CHECK", "Player sprints through the north shelf gap and turns behind the shelf.");
            bot.Sprint = true;
            yield return Drive(GapSouth);
            yield return Drive(BehindShelf);
            bot.Sprint = false;
        }

        IEnumerator FridgeExpiry()
        {
            yield return Until(() => Confirmed, HuntStartLimit);
            if (!Confirmed) { MissedHunt(); yield break; }

            // Round the east end of the north row. Its last shelf ends at x = 8.4 m.
            Vector3 corner = new Vector3(9.4f, 0f, 1.4f);
            Vector3 front = PursuitSandbox.FridgeFront + Vector3.left * 1.1f;
            // Walk just past the centre so the real controller clears the closing leaf.
            Vector3 inside = PursuitSandbox.FridgeFront + Vector3.right * (SandboxKit.FridgeInside.z * 0.6f);
            bot.Sprint = true;
            yield return Drive(corner);
            yield return Drive(front);
            bot.Sprint = false;
            if (!At(front) || sandbox.Caught)
            {
                Result(false, $"Did not reach the fridge entrance safely. {HuntState()}");
                yield break;
            }

            sandbox.FridgeDoor.Set(true);
            yield return new WaitForSeconds(sandbox.FridgeDoor.seconds);
            yield return Drive(inside);
            float coverage = sandbox.Fridge.Coverage(sandbox.Player);
            bool hidden = sandbox.Fridge.Conceals(sandbox.Player, coverage);
            if (hidden) sandbox.FridgeDoor.Set(false);
            if (!Hunting || !hidden || Eye.SeesNow || Eye.HiddenEntry.Active)
            {
                Result(false, $"Fridge coverage {coverage:P0}. Entry witnessed: {Eye.HiddenEntry.Active}. {HuntState()}");
                yield break;
            }

            recording.Mark("CHECK", "Player entered the fridge without being seen and shut the door.");
            float sighting = Pursuit.LastSighting;
            float deadline = Pursuit.Deadline;
            float window = deadline - sighting;
            while (Brain.IsChasing && !sandbox.Caught && !Eye.SeesNow && !Eye.HiddenEntry.Active &&
                sandbox.Fridge.Conceals(sandbox.Player, sandbox.Fridge.Coverage(sandbox.Player)) &&
                Pursuit.Deadline == deadline && Time.time <= deadline + 1f)
                yield return null;

            float elapsed = Time.time - sighting;
            bool expired = !Brain.IsChasing && !sandbox.Caught && !Eye.SeesNow && !Eye.HiddenEntry.Active &&
                Pursuit.LastSighting == sighting && Mathf.Abs(window - HuntSeconds) < 0.05f &&
                elapsed >= HuntSeconds - 0.05f && elapsed <= HuntSeconds + 0.5f;
            bool searched = Pursuit.SpotsSearched > 0;
            ThoughtRecord end = Brain.Log.Latest(24).LastOrDefault(r => r.Kind == "HUNT" && r.Text.Contains("the hunt ends:"));
            bool timedOut = end != null && end.Text.Contains("since she last saw you: the hunt is over");
            Result(expired && searched && timedOut,
                $"Fridge coverage {coverage:P0}. Last-sighting window {window:0.00} s, " +
                $"observed through {elapsed:0.00} s after the sighting. Spots searched: {Pursuit.SpotsSearched}. " +
                HuntState() + (end != null ? $" {end.Text}" : ""));
        }

        IEnumerator Reacquire()
        {
            yield return Until(() => Confirmed, HuntStartLimit);
            if (!Confirmed) { MissedHunt(); yield break; }
            yield return CrossGap();
            if (!At(BehindShelf))
            {
                Result(false, $"Did not reach cover behind the shelf. {HuntState()}");
                yield break;
            }

            yield return Until(() => !Hunting || Eye.SeesNow || Pursuit.Searching, 8f);
            if (!Hunting || Eye.SeesNow || !Pursuit.Searching || !sawBlock)
            {
                Result(false, $"No active search from cover before peeking out. {HuntState()}");
                yield break;
            }

            float originalSighting = Pursuit.LastSighting;
            float originalDeadline = Pursuit.Deadline;
            recording.Mark("CHECK", "Player steps back toward the gap to be seen again.");
            bot.Sprint = true;
            yield return Drive(GapSouth, () => Confirmed && Pursuit.LastSighting > originalSighting);
            bot.Sprint = false;
            yield return Until(() => !Hunting || Confirmed && Pursuit.LastSighting > originalSighting, 3f);

            float renewal = Pursuit.Deadline - originalDeadline;
            float window = Pursuit.Deadline - Pursuit.LastSighting;
            Result(Confirmed && !Pursuit.Searching && Pursuit.LastSighting > originalSighting && renewal > 0f &&
                Mathf.Abs(window - HuntSeconds) < 0.05f,
                $"Deadline extended by {(float.IsInfinity(renewal) ? "no measurable time" : $"{renewal:0.00} s")}. " + HuntState());
        }

        IEnumerator CatchAndRestart()
        {
            // Walk to her starting position using the player's motor. A real physics contact is
            // resolved by KarenBody and KarenBrain and raises the sandbox's existing catch event.
            yield return Drive(PursuitSandbox.KarenSpot, () => sandbox.Caught);
            yield return Until(() => sandbox.Caught, 3f);
            bool froze = sandbox.Caught && Brain.KilledThisShift && !Brain.enabled &&
                Brain.Body.Agent.isStopped && Brain.ShiftActive;
            recording.Mark("CHECK", $"Contact froze the sandbox: {froze}.");
            // Clear the spot so her reset cannot put her straight back in contact.
            yield return bot.WalkTo(PursuitSandbox.World(PursuitSandbox.KarenSpot + Vector3.right * 4f));

            sandbox.Restart();
            yield return null;
            float distance = Vector3.Distance(Brain.Body.Position, PursuitSandbox.World(PursuitSandbox.KarenSpot));
            bool reset = !Brain.enabled && Brain.Body.Agent.isStopped && !sandbox.Caught && !Brain.KilledThisShift && Brain.ShiftActive &&
                !Brain.IsChasing && !Pursuit.Active(Time.time) && Eye.Awareness == 0f && distance < 0.5f;
            Result(froze && reset,
                $"Catch froze the sandbox with the shift active: {froze}. Restart left Karen {distance:0.00} m from her spot, " +
                $"awareness {Eye.Awareness:0.00}. " + HuntState() + $" Running: {sandbox.Running}.");
        }

        // A waypoint timeout is reflected in the observed state. The route never teleports.
        IEnumerator Drive(Vector3 local, Func<bool> stop = null)
        {
            IEnumerator walk = bot.WalkTo(PursuitSandbox.World(local), 4f);
            while (!sandbox.Caught && !(stop?.Invoke() ?? false) && walk.MoveNext()) yield return walk.Current;
            bot.MoveWorld = Vector3.zero;
        }

        bool At(Vector3 local) => Vector3.Distance(bot.Feet, PursuitSandbox.World(local)) <= 0.25f;

        IEnumerator Until(Func<bool> done, float seconds)
        {
            float until = Time.time + seconds;
            while (!done() && !sandbox.Caught && Time.time < until) yield return null;
        }

        void Result(bool pass, string observed)
        {
            row.pass = pass;
            row.observed = observed;
        }

        void MissedHunt() => Result(false, $"No confirmed hunt within {HuntStartLimit:0} s. {HuntState()}");

        string HuntState() => $"Sees player: {Eye.SeesNow}. Chasing: {Brain.IsChasing}. " +
            $"Searching: {Pursuit.Searching}. Window left: {Pursuit.Left(Time.time):0.00} s. Caught: {sandbox.Caught}.";

        void OnDestroy()
        {
            recording?.Dispose();
            SandboxBot.FreeFrames();
        }

        void Finish()
        {
            int passed = rows.FindAll(r => r.pass).Count;
            var md = new StringBuilder("# Pursuit sandbox checks\n\n");
            md.AppendLine($"{passed} of {rows.Count} passed.").AppendLine();
            md.AppendLine("| Check | Expected | What happened | Result | Replay |").AppendLine("|---|---|---|---|---|");
            foreach (Row r in rows)
            {
                string replay = r.replay == null ? "—" : $"[{r.replay}]({r.replay})";
                md.AppendLine($"| {r.name} | {r.expected} | {r.observed.Replace("|", "\\|")} | {(r.pass ? "pass" : "**FAIL**")} | {replay} |");
            }

            ReportPath = Path.Combine(folder, "report.md");
            File.WriteAllText(ReportPath, md.ToString());
            File.WriteAllText(Path.Combine(folder, "report.json"), JsonUtility.ToJson(new Report { rows = rows.ToArray() }, true));
            ExitCode = passed == rows.Count ? 0 : 1;
            Debug.Log($"PursuitChecks: {passed} of {rows.Count} passed. Report: {ReportPath}");
            Done = true;
        }
    }
}
