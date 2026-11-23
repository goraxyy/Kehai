using System.Collections.Generic;
using Kehai.Karen;
using UnityEngine;

namespace Kehai.Sandbox
{
    // The production sensors, with a separate witness and camera so the island's pursuit
    // brain cannot pick up evidence from a different room.
    [RequireComponent(typeof(SightSandbox))]
    public sealed class InteractionSandbox : MonoBehaviour
    {
        public static readonly Vector3 Observer = new Vector3(1.9f, 0f, 2f);
        static readonly Vector3 WitnessAt = new Vector3(1.8f, 0f, 2.4f);
        public SightSandbox Room { get; private set; }
        public CustomerMemory Witness { get; private set; }
        public Flashlight Torch { get; private set; }
        public int CameraReports { get; private set; }
        public Vector3 LastCameraEntry { get; private set; }
        public bool FeedLive { get; private set; } = true;
        public Observation? LastTestimony { get; private set; }
        public bool Ready => cameraEye != null && Room.Player != null;

        SightSensor cameraEye;
        Sensorium testimony;

        void Start()
        {
            Room = GetComponent<SightSandbox>();
            var camera = SandboxKit.Group(transform, "Test camera eye", new Vector3(1.9f, 2.05f, 2f), 90f);
            cameraEye = camera.gameObject.AddComponent<SightSensor>();
            cameraEye.range = 30f;
            cameraEye.fieldOfView = 84f;
            cameraEye.halfStrengthAt = 14f;
            cameraEye.gain = 1.1f;
            cameraEye.darkVision = .55f;
            SandboxButton.Label(camera, "TEST CAMERA", Vector3.up * .33f, .05f);

            var shopper = SandboxKit.Group(transform, "Test shopper", WitnessAt, 90f);
            shopper.gameObject.SetActive(false);
            Witness = shopper.gameObject.AddComponent<CustomerMemory>();
            Witness.enabled = false; // Driven here and absent from the store's witness list.
            SandboxButton.Label(shopper, "SHOPPER", Vector3.up * 1.7f, .05f);
            shopper.gameObject.SetActive(true);
            testimony = new Sensorium(null, null);

            var torch = SandboxKit.Box(transform, "Test torch", new Vector3(3f, .72f, -2.5f),
                new Vector3(.09f, .09f, .23f), SandboxKit.Fridge);
            torch.SetActive(false);
            torch.AddComponent<Rigidbody>().isKinematic = true;
            Torch = torch.AddComponent<Flashlight>();
            torch.GetComponent<Item>().type = ItemType.Flashlight;
            Torch.beam = SandboxKit.Group(torch.transform, "Torch beam", Vector3.forward * .52f).gameObject.AddComponent<Light>();
            Torch.beam.type = LightType.Spot;
            Torch.beam.range = 12f;
            Torch.beam.intensity = 2f;
            torch.AddComponent<PickupInteractable>();
            torch.SetActive(true);

            SandboxButton.Panel(transform, "Interaction controls", new Vector3(2f, SightSandbox.PanelHeight, -5.89f), 0f,
                ("FEED", "Switch the test camera feed on or off", Color.cyan, () => SetFeed(!FeedLive)),
                ("ASK", "Ask the nearby shopper for their last sighting", Color.green, () => Ask(Room.Karen.Body.Position)),
                ("RESET", "Clear the interaction readings", Color.yellow, () => ResetSenses()));
            Room.Indicators.title = "Interactions sandbox";
            Room.Indicators.status = () => "sensor checks";
            Room.Indicators.extra = Readings;
            Room.Indicators.keys = "E on FEED, ASK or RESET   E to pick up and switch the torch   F4 indicators";
            ResetSenses();
        }

        public void ResetSenses(bool facing = true)
        {
            float yaw = transform.eulerAngles.y;
            Room.Karen.PlaceAt(transform.TransformPoint(Observer), yaw + (facing ? 90f : -90f));
            cameraEye.transform.localRotation = Quaternion.Euler(0f, facing ? 90f : -90f, 0f);
            cameraEye.ResetAwareness();
            Witness.transform.localRotation = cameraEye.transform.localRotation;
            Witness.ResetObservation();
            testimony.ForgetShift();
            CameraReports = 0;
            LastCameraEntry = Vector3.zero;
            LastTestimony = null;
        }

        public void SetFeed(bool live)
        {
            FeedLive = live;
            cameraEye.ResetAwareness();
        }

        public bool Ask(Vector3 observer)
        {
            if (!testimony.TryPollWitness(Witness, observer, out Observation observation)) return false;
            LastTestimony = observation;
            return true;
        }

        void Update()
        {
            if (!Ready || !Room.ControlsInArea()) return;
            Witness.TickObservation();
            if (InfrastructureFeed.ObserveCameraEntry(cameraEye, Time.deltaTime, FeedLive))
            {
                CameraReports++;
                LastCameraEntry = cameraEye.HiddenEntry.Position;
            }
        }

        IEnumerable<string> Readings()
        {
            yield return $"Camera {(FeedLive ? "live" : "off")} · entries reported {CameraReports}";
            yield return LastTestimony?.Label ?? "Shopper has given no testimony";
            if (Room.Karen.Body.Sight.TryHiddenTorchGlow(out Vector3 spot))
                yield return $"Torch glow seen on the surface at {spot:F1}";
        }

        void OnDestroy() => testimony?.Dispose();
    }
}
