using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Aiko
{
    public enum MotionState { Still, Crouching, Walking, Sprinting }

    // How lit a point on the floor is, from the ceiling lights that are actually on.
    // Sight uses it for the player's visibility; Aiko uses it to prefer dark approaches.
    public static class LightProbe
    {
        const float Cell = 5f;
        const float Reach = 7f;

        static readonly Dictionary<long, List<Light>> grid = new Dictionary<long, List<Light>>();
        static readonly List<Light> all = new List<Light>();
        static UnityEngine.SceneManagement.Scene builtFor;

        public static IReadOnlyList<Light> CeilingLights { get { Ensure(); return all; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            grid.Clear();
            all.Clear();
            builtFor = default;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static void Ensure()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (all.Count > 0 && builtFor == scene && all[0] != null) return;

            grid.Clear();
            all.Clear();
            builtFor = scene;

            GameObject root = GameObject.Find("CeilingLights");
            if (root == null) return;

            root.GetComponentsInChildren(true, all);
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
                    best = Mathf.Max(best, 1f - Mathf.Clamp01((distance - 1.5f) / (Reach - 1.5f)));
                }
            }

            float ambient = PowerSystem.PowerOn ? 0.12f : 0.02f;
            return Mathf.Clamp01(best * 0.88f + ambient);
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
    // This is the one door between the player and Aiko, and only sensors may use it:
    // SightSensor looks at these points, CustomerMemory remembers them, the Director (which
    // is omniscient by design) reads them for the Panic Index. AikoBody, the belief grid
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

        public Vector3 Feet => transform.position + Vector3.up * 0.1f;
        public Vector3 Chest => transform.position + Vector3.up * (IsCrouching ? 0.55f : 1.2f);
        public Vector3 Head => view != null ? view.transform.position : transform.position + Vector3.up * 1.6f;
        public Vector3 Facing => view != null ? view.transform.forward : transform.forward;
        public Vector3 Position => transform.position;

        public bool IsCrouching => motor != null && motor.IsCrouching;

        public MotionState Motion
        {
            get
            {
                if (motor == null || !motor.IsMoving) return MotionState.Still;
                if (motor.IsSprinting()) return MotionState.Sprinting;
                return motor.IsCrouching ? MotionState.Crouching : MotionState.Walking;
            }
        }

        // Aiko.md §3.1 motion salience.
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
        public bool TorchOn
        {
            get
            {
                if (carry == null || !carry.IsCarrying) return false;
                Flashlight torch = carry.currentItem.GetComponent<Flashlight>();
                return torch != null && torch.IsOn;
            }
        }

        public float LightLevel => TorchOn ? 1f : LightProbe.LevelAt(transform.position);

        public Item HeldItem => carry != null && carry.IsCarrying ? carry.currentItem : null;

        public bool Owns(Collider c) => c != null && (c == controller || c.transform.IsChildOf(transform));

        // Walking into stock left on the floor — the noise carpet Aiko lays with a shelf
        // sweep (Aiko.md §8.2).
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
