using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    public enum MotionState { Still, Crouching, Walking, Sprinting }

    // How lit a point on the floor is, from the ceiling lights that are actually on.
    // Sight uses it for the player's visibility; Karen uses it to prefer dark approaches.
    public static class LightProbe
    {
        const float Cell = 5f;
        const float Reach = 7f;

        static readonly Dictionary<long, List<Light>> grid = new Dictionary<long, List<Light>>();
        static readonly List<Light> all = new List<Light>();
        static UnityEngine.SceneManagement.Scene builtFor;
        static bool stale;

        public static IReadOnlyList<Light> CeilingLights { get { Ensure(); return all; } }

        // A new group of ceiling lamps was built (a sandbox room, 0.5.2): look again next time.
        public static void Refresh() => stale = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            grid.Clear();
            all.Clear();
            builtFor = default;
            stale = false;
            lampRooms.Clear();
            roomsMap = null;
            roomsRule = null;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static void Ensure()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!stale && all.Count > 0 && builtFor == scene && all[0] != null) return;

            grid.Clear();
            all.Clear();
            lampRooms.Clear();
            builtFor = scene;
            stale = false;

            GameObject root = SceneObjectNames.Find("CeilingLights");
            if (root != null) root.GetComponentsInChildren(true, all);
            // Every other "CeilingLights" group too: the sandbox island's rooms sit in the store
            // scene beside the store's own, and their lamps light you for her as theirs do in a
            // room on its own (0.5.2; she saw you 0.8 s later on the island without them).
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.gameObject.scene != scene || all.Contains(light)) continue;
                for (Transform t = light.transform.parent; t != null; t = t.parent)
                    if (t.name == "CeilingLights") { all.Add(light); break; }
            }
            foreach (Light light in all)
            {
                Vector3 p = light.transform.position;
                long key = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                if (!grid.TryGetValue(key, out List<Light> bucket)) grid[key] = bucket = new List<Light>();
                bucket.Add(light);
            }
        }

        // 0 = pitch black, ~1 = under a working light. Ambient bounce adds a little while
        // the mains are on.
        public static float LevelAt(Vector3 position)
        {
            Ensure();
            float best = 0f;
            string here = null;
            int cx = Mathf.FloorToInt(position.x / Cell);
            int cz = Mathf.FloorToInt(position.z / Cell);

            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (!grid.TryGetValue(Key(cx + dx, cz + dz), out List<Light> bucket)) continue;
                foreach (Light light in bucket)
                {
                    if (light == null || !light.enabled || !light.gameObject.activeInHierarchy) continue;
                    Vector3 d = light.transform.position - position;
                    d.y = 0f;
                    float distance = d.magnitude;
                    if (distance > Reach) continue;
                    // The point's room once per call, the lamp's from the cache (0.5.2, C6.4): each
                    // lookup searches the map's cells 6 m around, and her sweep asks this for every
                    // cell it looks at, so it was most of a batch frame's extra cost since 0.5.0.
                    here ??= RoomLightSwitch.RoomOf(position);
                    if (RoomOfLight(light) != here) continue;
                    best = Mathf.Max(best, 1f - Mathf.Clamp01((distance - 1.5f) / (Reach - 1.5f)));
                }
            }

            // Karen's light sense, not the rendering: kept at 0.12 / 0.02 in 0.5.1. Item 3 darkens
            // the rendered store; retuning how fast she notices you in the dim waits for a check
            // of this input against the rendered state (0.5.1 implementation notes).
            float ambient = PowerSystem.PowerOn && RoomLightSwitch.AllowsRoom(here ?? RoomLightSwitch.RoomOf(position)) ? 0.12f : 0.02f;
            return Mathf.Clamp01(best * 0.88f + ambient);
        }

        // A ceiling lamp's room, worked out once while it stays where it is, on the same map and
        // with the same room rule (a test or a sandbox may swap RoomLightSwitch.RoomOf).
        static readonly Dictionary<Light, (Vector3 at, string room)> lampRooms = new Dictionary<Light, (Vector3, string)>();
        static object roomsMap;
        static System.Func<Vector3, string> roomsRule;

        static string RoomOfLight(Light light)
        {
            object map = Application.isPlaying ? Kehai.Store.StoreMap.Current : null;
            if (!ReferenceEquals(map, roomsMap) || roomsRule != RoomLightSwitch.RoomOf)
            {
                lampRooms.Clear();
                roomsMap = map;
                roomsRule = RoomLightSwitch.RoomOf;
            }
            Vector3 at = light.transform.position;
            if (lampRooms.TryGetValue(light, out var known) && known.at == at) return known.room;
            string room = RoomLightSwitch.RoomOf(at);
            lampRooms[light] = (at, room);
            return room;
        }

        // The nearest working light to a point — Mirror-black kills this one.
        public static Light NearestOn(Vector3 position, float maxDistance = 8f)
        {
            Ensure();
            Light nearest = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (Light light in all)
            {
                if (light == null || !light.enabled) continue;
                Vector3 d = light.transform.position - position;
                d.y = 0f;
                if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; nearest = light; }
            }
            return nearest;
        }
    }

    // Everything about the employee that can be *sensed* — and nothing else.
    //
    // This is the one door between the player and Karen, and only sensors may use it:
    // SightSensor looks at these points, CustomerMemory remembers them, the Director (which
    // is omniscient by design) reads them for the Panic Index. KarenBody, the belief grid
    // and the planner never touch it — the fairness test in _Tests checks that in source.
    [DisallowMultipleComponent]
    public class PlayerPresence : MonoBehaviour
    {
        public static PlayerPresence Current { get; private set; }

        PlayerMotor motor;
        CharacterController controller;
        CarrySlot carry;
        Camera view;
        float lastKickNoise;

        void Awake()
        {
            Current = this;
            motor = GetComponent<PlayerMotor>();
            controller = GetComponent<CharacterController>();
            carry = GetComponent<CarrySlot>();
            view = GetComponentInChildren<Camera>();
            if (view == null) view = Camera.main;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        // The bottom of your body (0.5.2, found by the sandbox island): the store's player starts
        // a shift with its CharacterController centred on its origin, so its origin is 1 m up
        // until the first crouch (PlayerMotor then centres it at half its height, putting the
        // origin at the feet). Her eye tested your feet and chest from the origin, so until then
        // they were 1 m too high: your chest showed over a 2.2 m shelf, even crouched behind a
        // short one. From the body's real bottom, they're right either way.
        Vector3 Ground => controller != null ? transform.position + controller.center - Vector3.up * (controller.height * 0.5f) : transform.position;

        public Vector3 Feet => Ground + Vector3.up * 0.1f;
        public Vector3 Chest => Ground + Vector3.up * (IsCrouching ? 0.55f : 1.2f);
        public Vector3 Head => view != null ? view.transform.position : Ground + Vector3.up * 1.6f;
        public Vector3 Facing => view != null ? view.transform.forward : transform.forward;
        public Vector3 Position => transform.position;

        public bool IsCrouching => motor != null && motor.IsCrouching;

        // Upright capsule envelope in the hiding place's axes. Horizontal radius is rotation
        // invariant, so turning beside a cabinet cannot change coverage. Height/centre follow
        // the live controller, including the intermediate frames of a crouch.
        public Bounds BodyEnvelopeIn(Transform frame)
        {
            Vector3 centre = controller != null ? transform.TransformPoint(controller.center) : Position + Vector3.up * 0.9f;
            float radius = controller != null ? controller.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : 0.5f;
            float height = controller != null ? Mathf.Max(controller.height, controller.radius * 2f) * Mathf.Abs(transform.lossyScale.y) : 1.8f;
            return EnvelopeIn(frame, centre, radius, height);
        }

        // An upright body `radius` wide and `height` tall, centred on `centre` (world), in
        // `frame`'s axes. Shared with the scene checks that fit a body into each hiding place.
        public static Bounds EnvelopeIn(Transform frame, Vector3 centre, float radius, float height)
        {
            Vector3 scale = frame.lossyScale;
            return new Bounds(frame.InverseTransformPoint(centre), new Vector3(
                2f * radius / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
                height / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
                2f * radius / Mathf.Max(0.001f, Mathf.Abs(scale.z))));
        }

        public MotionState Motion
        {
            get
            {
                if (motor == null || !motor.IsMoving) return MotionState.Still;
                if (motor.IsSprinting()) return MotionState.Sprinting;
                return motor.IsCrouching ? MotionState.Crouching : MotionState.Walking;
            }
        }

        // Karen.md §3.1 motion salience.
        public float MotionSalience
        {
            get
            {
                switch (Motion)
                {
                    case MotionState.Sprinting: return 1f;
                    case MotionState.Walking: return 0.55f;
                    case MotionState.Crouching: return 0.2f;
                    default: return 0.05f;
                }
            }
        }

        // A torch in hand and switched on lights you up far more than any ceiling panel.
        public Flashlight HeldTorch => carry != null && carry.IsCarrying
            ? carry.currentItem.GetComponent<Flashlight>() : null;
        public bool TorchOn => HeldTorch is Flashlight torch && torch.IsOn;

        public float LightLevel => TorchOn ? 1f : LightProbe.LevelAt(transform.position);

        public Item HeldItem => carry != null && carry.IsCarrying ? carry.currentItem : null;

        public bool Owns(Collider c) => c != null && (c == controller || c.transform.IsChildOf(transform));

        // Walking into stock left on the floor — the noise carpet Karen lays with a shelf
        // sweep (Karen.md §8.2).
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody == null) return;
            if (hit.collider.GetComponentInParent<Item>() == null) return;
            if (Time.time - lastKickNoise < 0.35f) return;
            if (motor == null || !motor.IsMoving) return;

            lastKickNoise = Time.time;
            NoiseBus.Emit(hit.point, motor.IsSprinting() ? 0.75f : 0.5f, NoiseKind.KickedItem, NoiseAuthor.Player);
        }
    }
}
