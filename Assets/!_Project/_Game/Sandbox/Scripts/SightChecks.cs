using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The sight sandbox's checks, played by a bot (0.5.2, C11.6, the owner's item 4): each one puts
    // Karen on a spot facing a given way, frozen but seeing, puts the player somewhere (walking
    // there, or crouching, or stepping into the fridge), watches her eye for a few seconds, and
    // writes what was expected and what happened. Run them with Kehai → Sandbox → Run Sight Checks,
    // or headless: tools/run/kehai-run unity -- -executeMethod SandboxChecks.Sight
    // The report goes to <persistent data>/sandbox_checks/sight_<time>.md and .json.
    [RequireComponent(typeof(SightSandbox))]
    public sealed class SightChecks : MonoBehaviour
    {
        public enum Expect { Seen, Unseen, Witnessed, NotWitnessed }

        public sealed class Check
        {
            public string Name, What;
            public Vector3 Karen;
            public float KarenYaw;
            public Expect Expect;
            public System.Func<SightChecks, IEnumerator> Act;
        }

        public sealed class Row
        {
            public string Name, What, Expected, Observed, Replay;
            public bool Pass;
            public float MaxAwareness, SecondsToSee = -1f;
        }

        public const float Watch = 3f;   // seconds of watching her eye after each setup

        public static bool Done { get; private set; }
        public static int ExitCode { get; private set; }
        public static string ReportPath { get; private set; }
        public readonly List<Row> Rows = new List<Row>();

        public SightSandbox Sandbox { get; private set; }
        System.Action watching;
        float endCoverage = -1f;   // a walk-in's last coverage, so a bot that stopped short is told apart

        string stem;
        SandboxCheckRecording recording;

        void LateUpdate() => recording?.Sample();
        void Update() => watching?.Invoke();
        void OnDestroy()
        {
            recording?.Dispose();
            SandboxBot.FreeFrames();
        }
        public SandboxBot Bot { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Done = false;
            ExitCode = 0;
            ReportPath = null;
        }

        // The list. Positions are the sight sandbox's (SightSandbox's layout comment).
        public static IEnumerable<Check> All()
        {
            Vector3 fridgeInside = SightSandbox.FridgeFront + Vector3.right * (SandboxKit.FridgeInside.z * 0.5f);
            Vector3 fridgeFront = SightSandbox.FridgeFront + Vector3.left * 1.2f;
            // 3 m from the fridge's front: beyond arm's length (SightSensor.CloseRange, 2.2 m), where
            // she notices anyone, whichever way she faces.
            Vector3 facingFridge = new Vector3(1.9f, 0f, 2.0f);
            yield return new Check { Name = "open-8m", What = "standing still in the open, 8 m in front of her, lit",
                Karen = new Vector3(-6f, 0f, -4f), KarenYaw = 90f, Expect = Expect.Seen,
                Act = c => c.Place(new Vector3(2f, 0f, -4f)) };
            yield return new Check { Name = "short-shelf-standing", What = "standing behind a short shelf: your eyes show over it",
                Karen = new Vector3(2f, 0f, -4.4f), KarenYaw = 0f, Expect = Expect.Seen,
                Act = c => c.Place(new Vector3(2f, 0f, 2.2f)) };
            yield return new Check { Name = "short-shelf-crouched", What = "crouched behind a short shelf",
                Karen = new Vector3(2f, 0f, -4.4f), KarenYaw = 0f, Expect = Expect.Unseen,
                Act = c => c.Place(new Vector3(2f, 0f, 2.2f), crouched: true) };
            yield return new Check { Name = "rack-standing", What = "standing in front of the stock rack",
                Karen = new Vector3(2.2f, 0f, 1.9f), KarenYaw = 0f, Expect = Expect.Seen,
                Act = c => c.Place(new Vector3(2.2f, 0f, 3.6f)) };
            yield return new Check { Name = "rack-crouched", What = "crouched under the stock rack's lowest board",
                Karen = new Vector3(2.2f, 0f, 1.9f), KarenYaw = 0f, Expect = Expect.Unseen,
                Act = c => c.Place(SightSandbox.RackCentre, crouched: true) };
            yield return new Check { Name = "fridge-inside", What = "inside the fridge, already in when she looks",
                Karen = facingFridge, KarenYaw = 90f, Expect = Expect.Unseen,
                Act = c => c.Place(fridgeInside, fridgeOpen: true) };
            yield return new Check { Name = "fridge-walk-in-seen", What = "walking into the fridge while she watches",
                Karen = facingFridge, KarenYaw = 90f, Expect = Expect.Witnessed,
                Act = c => c.WalkInto(fridgeFront, fridgeInside) };
            yield return new Check { Name = "fridge-walk-in-unseen", What = "walking into the fridge behind her back",
                Karen = facingFridge, KarenYaw = -90f, Expect = Expect.NotWitnessed,
                Act = c => c.WalkInto(fridgeFront, fridgeInside) };
        }

        IEnumerator Start()
        {
            Sandbox = GetComponent<SightSandbox>();
            if (!Sandbox.Built) Sandbox.Build();
            yield return null;
            Sandbox.Karen.Body.Silent = true;
            SandboxBot.FixFrames();
            Bot = new SandboxBot(Sandbox.Player.GetComponent<PlayerMotor>());
            string dir = Path.Combine(Application.persistentDataPath, "sandbox_checks");
            stem = Path.Combine(dir, "sight_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(stem);
            foreach (Check check in All())
            {
                yield return Run(check);
            }
            Finish();
        }

        IEnumerator Run(Check check)
        {
            // Out of the way, standing, the doors as they were; then her spot; then the setup.
            Bot.Teleport(SightSandbox.PlayerStart);
            yield return Bot.SetCrouch(false);
            SetFridgeDoor(false);
            Sandbox.Karen.PlaceAt(check.Karen, check.KarenYaw);
            yield return null;
            Sandbox.Karen.Body.Sight.ResetAwareness();
            endCoverage = -1f;
            SightSensor eye = Sandbox.Karen.Body.Sight;
            // Her eye is watched through the whole check, the setup included (a walk-in is seen
            // during the walk, not after it).
            float start = Time.time, max = 0f, seenAt = -1f;
            watching = () =>
            {
                max = Mathf.Max(max, eye.Awareness);
                if (seenAt < 0f && eye.Awareness >= eye.glimpseAt) seenAt = Time.time - start;
            };
            string file = check.Name + ".krec";
            recording = new SandboxCheckRecording(Path.Combine(stem, file), Sandbox);
            recording.Mark("CHECK", check.What);
            yield return check.Act(this);
            float end = Time.time + Watch;
            while (Time.time < end) yield return null;
            watching = null;
            bool witnessed = eye.HiddenEntry.Active;
            var row = new Row { Name = check.Name, What = check.What, Expected = check.Expect.ToString(), MaxAwareness = max, SecondsToSee = seenAt };
            switch (check.Expect)
            {
                case Expect.Seen: row.Pass = max >= eye.glimpseAt; break;
                case Expect.Unseen: row.Pass = max < eye.glimpseAt; break;
                case Expect.Witnessed: row.Pass = witnessed; break;
                case Expect.NotWitnessed: row.Pass = !witnessed; break;
            }
            row.Observed = $"awareness up to {max:0.00}" + (seenAt >= 0f ? $", noticed after {seenAt:0.0} s" : ", never noticed") +
                           (witnessed ? ", saw you go in" : "");
            if (endCoverage >= 0f && !HidingPlace.Conceals(endCoverage))
            {
                // Not her eye's failure: the bot didn't get far enough in to be hidden.
                row.Pass = false;
                row.Observed += $"; the bot stopped {endCoverage * 100f:0}% inside, short of hidden: the check itself failed";
            }
            recording.Mark("CHECK", row.Observed);
            recording.Finish();
            recording = null;
            row.Replay = Path.GetFileName(stem) + "/" + file;
            Rows.Add(row);
            Debug.Log($"SightChecks: {(row.Pass ? "PASS" : "FAIL")} {row.Name}: expected {row.Expected}; {row.Observed}");
        }

        // ---- the bot's moves ----

        IEnumerator Place(Vector3 feet, bool crouched = false, bool fridgeOpen = false)
        {
            if (fridgeOpen) SetFridgeDoor(true);
            if (crouched) yield return Bot.SetCrouch(true);
            Bot.Teleport(feet);
            yield return null;
        }

        IEnumerator WalkInto(Vector3 front, Vector3 inside)
        {
            SetFridgeDoor(true);
            Bot.Teleport(front);
            SightSensor eye = Sandbox.Karen.Body.Sight;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)   // in front of it, where she can see you
            {
                yield return null;
            }
            PlayerPresence p = PlayerPresence.Current;
            Debug.Log($"SightChecks: walk-in from {Bot.Feet}; her eye at {eye.Eye} facing {eye.Forward}; " +
                      $"you {(p != null ? p.Position.ToString() : "missing")}, light {(p != null ? p.LightLevel : 0f):0.00}; " +
                      $"now {eye.DetectNow:0.00}, exposure {eye.Exposure:0.00}, awareness {eye.Awareness:0.00}");
            // All the way to the back, so the body ends well past the 80% that hides it.
            bool walking = true;
            StartCoroutine(Walk(inside + Vector3.right * 0.15f, () => walking = false));
            float nextTrace = 0f;
            for (float t = 0f; walking; t += Time.deltaTime)
            {
                if (t >= nextTrace)
                {
                    nextTrace += 0.5f;
                    Debug.Log($"SightChecks: walk t={t:0.0} feet {Bot.Feet} move {Bot.MoveWorld} door {(FridgeDoorNow() is SandboxDoor d ? $"{(d.IsOpen ? "open" : "shut")} {d.transform.localEulerAngles.y:0}" : "none")}");
                }
                yield return null;
            }
            yield return null;
            HidingPlace place = p != null ? HidingPlace.At(p, out _) : null;
            endCoverage = place != null ? place.Coverage(p) : 0f;
            Debug.Log($"SightChecks: walked in to {Bot.Feet}: {endCoverage * 100f:0}% inside; entry {(eye.HiddenEntry.Active ? "seen" : "not seen")}");
            if (!HidingPlace.Conceals(endCoverage))
            {
                var names = Physics.OverlapCapsule(Bot.Feet + Vector3.up * 0.6f, Bot.Feet + Vector3.up * 1.5f, 0.62f)
                    .Where(c => c.transform != Sandbox.Player.transform && !c.transform.IsChildOf(Sandbox.Player.transform))
                    .Select(c => c.name + (c.isTrigger ? " (trigger)" : ""));
                Debug.Log("SightChecks: stopped short; touching " + string.Join(", ", names));
            }
        }

        IEnumerator Walk(Vector3 target, System.Action done)
        {
            yield return Bot.WalkTo(target);
            done();
        }

        SandboxDoor FridgeDoorNow() => Sandbox.Fridge != null ? Sandbox.Fridge.transform.parent.GetComponentInChildren<SandboxDoor>() : null;

        void SetFridgeDoor(bool open)
        {
            SandboxDoor door = FridgeDoorNow();
            if (door != null) door.Set(open, instantly: true);
            Physics.SyncTransforms();
        }

        // ---- the report ----

        void Finish()
        {
            int failed = Rows.Count(r => !r.Pass);
            var md = new StringBuilder();
            md.AppendLine("# Sight sandbox checks").AppendLine();
            md.AppendLine($"{Rows.Count - failed} of {Rows.Count} passed. Her eye is the game's; each check watched it for {Watch:0} s.").AppendLine();
            md.AppendLine("| Check | What | Expected | What happened | Result | Replay |").AppendLine("|---|---|---|---|---|---|");
            foreach (Row r in Rows) md.AppendLine($"| {r.Name} | {r.What} | {r.Expected} | {r.Observed} | {(r.Pass ? "pass" : "**FAIL**")} | [replay]({r.Replay}) |");
            File.WriteAllText(stem + ".md", md.ToString());
            var json = new StringBuilder("[");
            for (int i = 0; i < Rows.Count; i++)
            {
                Row r = Rows[i];
                json.Append(i > 0 ? "," : "").Append("\n {")
                    .Append($"\"name\":\"{r.Name}\",\"expected\":\"{r.Expected}\",\"pass\":{(r.Pass ? "true" : "false")},")
                    .Append($"\"replay\":\"{r.Replay}\",")
                    .Append($"\"max_awareness\":{r.MaxAwareness.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},")
                    .Append($"\"seconds_to_see\":{r.SecondsToSee.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}}}");
            }
            File.WriteAllText(stem + ".json", json.Append("\n]\n").ToString());
            ReportPath = stem + ".md";
            ExitCode = failed == 0 ? 0 : 1;
            Debug.Log($"SightChecks: {Rows.Count - failed} of {Rows.Count} passed. Report: {ReportPath}");
            Done = true;
        }
    }
}
