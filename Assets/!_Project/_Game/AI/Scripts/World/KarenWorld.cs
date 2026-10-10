using System.Collections;
using System.Collections.Generic;
using Kehai.Store;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Everything Karen can do to the building, in one place. Tactics ask for effects here
    // rather than reaching into gameplay scripts, so the list of what she *can* touch is
    // the list of methods on this class.
    public sealed class KarenWorld : MonoBehaviour
    {
        public static KarenWorld Instance { get; private set; }

        public PaSystem Pa { get; private set; }
        public LightControl Lights { get; private set; }
        public BreakerPanel Breakers { get; private set; }

        public GameObject CustomerPrefab { get; private set; }
        public GameObject DirtPrefab { get; private set; }

        readonly Dictionary<HingeDoor, float> lockedUntil = new Dictionary<HingeDoor, float>();
        readonly List<GameObject> placed = new List<GameObject>();

        void Awake()
        {
            Instance = this;
            Pa = gameObject.AddComponent<PaSystem>();
            Lights = gameObject.AddComponent<LightControl>();
            Breakers = gameObject.AddComponent<BreakerPanel>();
            KarenScreen.Ensure();

            CustomerSpawner spawner = FindAnyObjectByType<CustomerSpawner>();
            if (spawner != null && spawner.customerPrefab != null)
            {
                CustomerPrefab = spawner.customerPrefab;
                CustomerNPC npc = CustomerPrefab.GetComponent<CustomerNPC>();
                if (npc != null) DirtPrefab = npc.dirtPrefab;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // Unlock doors whose time is up.
            if (lockedUntil.Count == 0) return;
            var done = new List<HingeDoor>();
            foreach (KeyValuePair<HingeDoor, float> pair in lockedUntil)
                if (pair.Key == null || Time.time > pair.Value) done.Add(pair.Key);
            foreach (HingeDoor door in done)
            {
                lockedUntil.Remove(door);
                if (door != null) door.SetLocked(false);
            }
        }

        // ---- tells ------------------------------------------------------------------

        public void PlayTell(TellKind kind, Vector3 at, float lead)
        {
            // A warning only counts if it can be heard (fairness rule 3), so tells carry much
            // further than ordinary sounds and are only partly positional: you can tell which
            // way it came from without having to be in the same aisle.
            AudioClip clip = ProceduralAudio.Tell(kind);
            switch (kind)
            {
                case TellKind.BallastWhine:
                    // A blackout is audible storewide and visible as a flicker everywhere.
                    Pa.PlayNear(at, clip, 1f);
                    OneShotAudio.PlayAt(clip, ListenerPosition(), 0.6f, 5f, 40f, 0.3f, SoundKind.Karen);
                    Lights.FlickerAll(lead);
                    break;
                case TellKind.Flicker:
                    Light near = LightProbe.NearestOn(at);
                    if (near != null) StartCoroutine(Lights.Flicker(near, lead));
                    OneShotAudio.PlayAt(clip, near != null ? near.transform.position : at, 0.8f, TellNear, TellFar, TellSpatial, SoundKind.Karen);
                    break;
                case TellKind.PaChime:
                case TellKind.SpeakerCrackle:
                case TellKind.HoldMusic:
                    Pa.PlayNear(ListenerPosition(), clip, 0.9f);
                    break;
                case TellKind.CrtTick:
                    // The HUD tell has to reach you wherever you are.
                    OneShotAudio.PlayAt(clip, ListenerPosition(), 0.7f, 5f, 40f, 0f, SoundKind.Karen);
                    break;
                default:
                    OneShotAudio.PlayAt(clip, at, 1f, TellNear, TellFar, TellSpatial, SoundKind.Karen);
                    break;
            }
            NoiseBus.Emit(at, 0.4f, NoiseKind.Tell, NoiseAuthor.Karen);
        }

        const float TellNear = 6f, TellFar = 60f, TellSpatial = 0.8f;

        public static Vector3 ListenerPosition()
        {
            AudioListener ear = FindAnyObjectByType<AudioListener>();
            return ear != null ? ear.transform.position : Vector3.zero;
        }

        // ---- sight --------------------------------------------------------------------

        public void Blackout(KarenRng rng)
        {
            Breakers.TripAll(rng);
            PowerSystem.Instance?.CutPower();
        }

        public void TripCircuit(LightCircuit circuit) => Breakers.Trip(circuit);

        public bool MirrorBlack(Vector3 near)
        {
            Light light = LightProbe.NearestOn(near, 6f);
            if (light == null) return false;
            Lights.Kill(light);
            return true;
        }

        public void Fog(Vector3 at, float radius, float seconds) => Track(FogCloud.Spawn(at, radius, seconds).gameObject);

        public CctvCamera BoltOnCamera(Vector3 overSpot)
        {
            // Mount it on the nearest shelf top or wall, looking down at the spot.
            Vector3 mount = overSpot + Vector3.up * 2.6f + Vector3.forward * 0.1f;
            CctvCamera cam = CctvCamera.Spawn(mount, (overSpot - mount).normalized + Vector3.down * 0.2f, true);
            Track(cam.gameObject);
            return cam;
        }

        // ---- work ---------------------------------------------------------------------

        public Dirt Spill(Vector3 at)
        {
            if (DirtPrefab == null) return null;
            Vector3 p = NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : at;
            GameObject go = Instantiate(DirtPrefab, p + Vector3.up * 0.02f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            go.name = "Spill (" + GameNames.Antagonist + ")";
            return go.GetComponent<Dirt>();
        }

        public CoffeeCup Coffee(Vector3 at, bool isLast)
        {
            Vector3 p = NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : at;
            CoffeeCup cup = CoffeeCup.Spawn(p, isLast);
            Track(cup.gameObject);
            return cup;
        }

        public void PhantomChime(AutoDoubleDoor door)
        {
            if (door != null) door.PhantomCycle();
        }

        // ---- space --------------------------------------------------------------------

        public CrateWall Crates(Vector3 at, Vector3 across, float width, int region)
        {
            CrateWall wall = CrateWall.Spawn(at, across, width);
            wall.Region = region;
            Track(wall.gameObject);
            return wall;
        }

        public bool LockDoor(HingeDoor door, float seconds)
        {
            if (door == null) return false;
            door.SetLocked(true);
            lockedUntil[door] = Time.time + seconds;
            return true;
        }

        public bool IsLocked(HingeDoor door) => door != null && lockedUntil.ContainsKey(door);

        // Slide a bay along the floor. The NavMesh catches up by carving the new position
        // now; the old footprint is re-baked between shifts (see MazeMutation).
        public IEnumerator SlideBay(ShelfUnit bay, Vector3 to, float seconds)
        {
            if (bay == null) yield break;
            Vector3 from = bay.transform.position;
            var carve = bay.gameObject.GetOrAdd<NavMeshObstacle>();
            carve.carving = true;
            carve.shape = NavMeshObstacleShape.Box;
            Bounds local = LocalBounds(bay);
            carve.center = local.center;
            carve.size = local.size;

            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                bay.transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
                if (Mathf.Repeat(t, 0.4f) < Time.deltaTime)
                    NoiseBus.Emit(bay.transform.position, 0.8f, NoiseKind.Environmental, NoiseAuthor.Karen);
                yield return null;
            }
            bay.transform.position = to;
        }

        static Bounds LocalBounds(ShelfUnit bay)
        {
            var b = new Bounds(Vector3.up, Vector3.one);
            bool any = false;
            foreach (Collider c in bay.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                Bounds wb = c.bounds;
                Vector3 min = bay.transform.InverseTransformPoint(wb.min);
                Vector3 max = bay.transform.InverseTransformPoint(wb.max);
                var lb = new Bounds((min + max) * 0.5f, new Vector3(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y), Mathf.Abs(max.z - min.z)));
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            return b;
        }

        // ---- social -------------------------------------------------------------------

        public Possession Possess(CustomerNPC customer, float seconds) => Possession.Take(customer, seconds);

        public Understudy HireUnderstudy(Vector3 at)
        {
            Understudy u = Understudy.Spawn(CustomerPrefab, at);
            if (u != null) Track(u.gameObject);
            return u;
        }

        // ---- housekeeping --------------------------------------------------------------

        void Track(GameObject go)
        {
            if (go != null) placed.Add(go);
        }

        // Clears everything she placed. Called at the end of each shift so the store is
        // reset for the next one — the maze mutation aside, which is meant to persist.
        public void ClearShift()
        {
            foreach (GameObject go in placed) if (go != null) Destroy(go);
            placed.Clear();
            foreach (HingeDoor door in lockedUntil.Keys) if (door != null) door.SetLocked(false);
            lockedUntil.Clear();
            Pa.Clear();
            HudFeed.Clear();
            Breakers.ResetForShift();
            if (!PowerSystem.PowerOn) PowerSystem.Instance?.RestorePower();
            Lights.ApplyCircuits();
        }

        // ---- the NavMesh ---------------------------------------------------------------

        // The surface whose volume covers the store floor — rebaked after the maze moves.
        public static NavMeshSurface StoreSurface()
        {
            foreach (NavMeshSurface s in FindObjectsByType<NavMeshSurface>())
                if (s.navMeshData != null && s.navMeshData.sourceBounds.size.x > 30f) return s;
            return FindAnyObjectByType<NavMeshSurface>();
        }
    }
}
