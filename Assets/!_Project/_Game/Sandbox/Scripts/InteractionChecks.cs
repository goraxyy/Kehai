using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kehai.Karen;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Sandbox
{
    [RequireComponent(typeof(InteractionSandbox))]
    public sealed class InteractionChecks : MonoBehaviour
    {
        enum Channel { Camera, Testimony, Torch, Ledger }
        [Serializable] sealed class Row
        {
            public string name, expected, observed, replay;
            public bool pass;
        }
        [Serializable] sealed class Report { public Row[] rows; }

        public static bool Done { get; private set; }
        public static int ExitCode { get; private set; }
        public static string ReportPath { get; private set; }

        readonly List<Row> rows = new List<Row>();
        InteractionSandbox sandbox;
        SandboxBot bot;
        SandboxDoor door;
        SandboxCheckRecording recording;
        string folder;
        float previousStep;
        bool fixedStep;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Done = false;
            ExitCode = 0;
            ReportPath = null;
        }

        IEnumerator Start()
        {
            sandbox = GetComponent<InteractionSandbox>();
            folder = Path.Combine(Application.persistentDataPath, "sandbox_checks", "interactions_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(folder);
            float until = Time.realtimeSinceStartup + 10f;
            while (!sandbox.Ready && Time.realtimeSinceStartup < until) yield return null;
            if (!sandbox.Ready)
            {
                rows.Add(new Row { name = "setup", expected = "The room and its sensors start", observed = "No ready sandbox" });
                Finish();
                yield break;
            }
            previousStep = Time.captureDeltaTime;
            SandboxBot.FixFrames();
            fixedStep = true;
            sandbox.Room.Karen.Body.Silent = true;
            bot = new SandboxBot(sandbox.Room.Player.GetComponent<PlayerMotor>());
            door = sandbox.Room.Fridge.transform.parent.GetComponentInChildren<SandboxDoor>();

            yield return Check("camera-live", Channel.Camera, active: true);
            yield return Check("camera-off", Channel.Camera, active: false);
            yield return Check("camera-facing-away", Channel.Camera, active: true, facing: false);
            yield return Check("shopper-saw-entry", Channel.Testimony, active: true);
            yield return Check("shopper-facing-away", Channel.Testimony, active: true, facing: false);
            yield return Check("hidden-torch-on", Channel.Torch, active: true);
            yield return Check("hidden-torch-off", Channel.Torch, active: false);
            yield return Check("ledger-saw-entry", Channel.Ledger, active: true);
            yield return Check("ledger-unseen-entry", Channel.Ledger, active: false);
            Finish();
        }

        IEnumerator Check(string name, Channel channel, bool active, bool facing = true)
        {
            var room = sandbox.Room;
            var player = room.Player;
            PoseTorch(channel == Channel.Torch && active, carry: channel == Channel.Torch);
            sandbox.ResetSenses(facing);
            sandbox.SetFeed(channel == Channel.Camera && active);
            // Keep her eye off the entry in the indirect-evidence rows. For the Ledger
            // only, compare exactly the same route with her eye turned either way.
            var eye = room.Karen.Body.Sight;
            bool seesEntry = channel == Channel.Ledger && active;
            Vector3 observer = InteractionSandbox.Observer;
            // Give the beam a clear path past Karen to the tall shelf.
            if (channel == Channel.Torch) observer += Vector3.forward * 1.2f;
            room.Karen.PlaceAt(transform.TransformPoint(observer), transform.eulerAngles.y + (seesEntry ? 90f : -90f));
            door.Set(true, instantly: true);
            Physics.SyncTransforms();
            Vector3 threshold = SightSandbox.FridgeFront + Vector3.left * 1.2f;
            bot.Teleport(transform.TransformPoint(threshold));
            player.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y + (channel == Channel.Torch ? -95f : 90f), 0f);

            bool expect = active && facing;
            var row = new Row { name = name, expected = expect ? "Evidence" : "No evidence", replay = name + ".krec" };
            recording = new SandboxCheckRecording(Path.Combine(folder, row.replay), room);
            recording.Mark("CHECK", name + ": " + row.expected);
            int boundaries = 0;
            void CountBoundary(Vector3 _) => boundaries++;
            eye.SawHidingBoundary += CountBoundary;
            yield return bot.WalkTo(transform.TransformPoint(SightSandbox.FridgeFront + Vector3.left * .12f));
            yield return Wait(.55f); // A shopper gets a real look close enough to the opening.
            yield return bot.WalkTo(transform.TransformPoint(SightSandbox.FridgeFront + Vector3.right * (SandboxKit.FridgeInside.z * .5f + .15f)));
            yield return Wait(.55f);
            eye.SawHidingBoundary -= CountBoundary;
            float coverage = room.Fridge.Coverage(player);
            bool hidden = room.Fridge.Conceals(player, coverage);
            bool directSight = eye.SeesNow || eye.HiddenEntry.Active;
            bool valid = true;
            bool observed;
            switch (channel)
            {
                case Channel.Camera:
                    observed = sandbox.CameraReports > 0;
                    valid = !observed || sandbox.CameraReports == 1 &&
                        room.Fridge.DistanceTo(sandbox.LastCameraEntry) <= WitnessedHide.EntryReach &&
                        Vector3.Distance(player.Position, sandbox.LastCameraEntry) > .25f;
                    row.observed = $"Camera reports {sandbox.CameraReports}";
                    break;
                case Channel.Testimony:
                    var witness = sandbox.Witness;
                    bool unasked = !witness.HasSpokenToKaren;
                    bool fromFar = sandbox.Ask(witness.transform.position + Vector3.back * 4f);
                    bool nearby = sandbox.Ask(witness.transform.position + Vector3.back);
                    observed = nearby && witness.SawHidingEntry;
                    valid = unasked && !fromFar && (expect || !nearby) &&
                        (!observed || sandbox.LastTestimony.Value.Position == witness.LastSeenAt &&
                        Vector3.Distance(player.Position, witness.LastSeenAt) > .25f &&
                        !sandbox.Ask(witness.transform.position + Vector3.back));
                    row.observed = $"Shopper saw entry {witness.SawHidingEntry}, told at 4 m {fromFar}, told at 1 m {nearby}";
                    break;
                case Channel.Torch:
                    observed = eye.TryHiddenTorchGlow(out Vector3 spot);
                    var beam = sandbox.Torch.beam;
                    bool hitSurface = Physics.Raycast(beam.transform.position, beam.transform.forward,
                        out RaycastHit hit, beam.range, ~0, QueryTriggerInteraction.Ignore);
                    string target = hitSurface ? hit.collider.transform.parent?.name : "none";
                    valid = player.HeldTorch == sandbox.Torch && sandbox.Torch.IsOn == active &&
                        target == "Tall shelf 1" && eye.CanSee(hit.point + hit.normal * .015f) &&
                        (!observed || Vector3.Distance(player.Position, spot) > .5f);
                    row.observed = observed ? $"Light on a surface at {spot:F1}" : "No visible light from the torch";
                    row.observed += $". Beam target {target}";
                    break;
                default:
                    float weight = TestLedger(boundaries, Path.Combine(folder, name + "-ledger.json"));
                    observed = weight > .8f;
                    valid = boundaries == (expect ? 1 : 0) &&
                        Mathf.Abs(weight - .8f * (1f + (expect ? KarenLedger.WitnessedHideSeconds : 0f))) < .001f;
                    row.observed = $"Witnessed boundaries {boundaries}. Concealment weight after the shift {weight:0.0}";
                    break;
            }
            row.pass = hidden && directSight == seesEntry && valid && observed == expect;
            row.observed += $". Fridge coverage {coverage:P0}. Direct sight or witnessed entry {directSight}. Evidence valid {valid}.";
            recording.Mark("CHECK", row.observed);
            recording.Finish();
            recording = null;
            rows.Add(row);
            Debug.Log($"InteractionChecks: {(row.pass ? "PASS" : "FAIL")} {name}. {row.observed}");
        }

        static float TestLedger(int boundaries, string evidencePath)
        {
            var map = new StoreMap();
            map.Regions.Add(new Region { Key = "fridge fixture" });
            var ledger = new KarenLedger(new KarenConfig { ledgerPath = evidencePath }) { Persistent = true };
            ledger.BeginShift(1);
            ledger.ObservePlayer(map, 0, 1f, true, MotionState.Still, 0f);
            for (int i = 0; i < boundaries; i++) ledger.ObserveWitnessedHide(map, 0);
            ledger.EndShift(10f, 1f, false);
            return ledger.Data.regions.Single().concealment;
        }

        void PoseTorch(bool on, bool carry)
        {
            var hands = sandbox.Room.Player.GetComponent<CarrySlot>();
            Item item = sandbox.Torch.GetComponent<Item>();
            if (carry && !hands.Contains(item)) hands.TryPickup(item);
            sandbox.Torch.SetOn(on);
        }

        static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        void LateUpdate() => recording?.Sample();

        void Finish()
        {
            ReportPath = Path.Combine(folder, "report.json");
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report { rows = rows.ToArray() }, true));
            ExitCode = rows.All(row => row.pass) ? 0 : 1;
            Done = true;
            RestoreFrames();
        }

        void RestoreFrames()
        {
            if (!fixedStep) return;
            Time.captureDeltaTime = previousStep;
            fixedStep = false;
        }

        void OnDestroy()
        {
            recording?.Dispose();
            RestoreFrames();
        }
    }
}
