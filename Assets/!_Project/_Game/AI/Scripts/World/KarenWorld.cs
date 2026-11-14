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

        // ---- tells ------------------------------------------------------------------

        // Whether a tell goes through the PA, and so can't play while it's switched off (0.3.0).
        public static bool ThroughThePa(TellKind kind) => kind == TellKind.PaChime || kind == TellKind.HoldMusic;

        // False when it couldn't play: a PA sound with the PA switched off.
        public bool PlayTell(TellKind kind, Vector3 at, float lead)
        {
            if (ThroughThePa(kind) && !Pa.Powered) return false;
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
                case TellKind.PaChime:
                case TellKind.HoldMusic:
                    Pa.PlayNear(ListenerPosition(), clip, 0.9f);
                    break;
                default:
                    OneShotAudio.PlayAt(clip, at, 1f, TellNear, TellFar, TellSpatial, SoundKind.Karen);
                    break;
            }
            NoiseBus.Emit(at, 0.4f, NoiseKind.Tell, NoiseAuthor.Karen);
            return true;
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

        // ---- fog (0.3.0) --------------------------------------------------------------

        // The rooms she can flood. Until 0.4.1 these were the three with fridges (the sales floor,
        // cold storage, the staff room), and the sales floor is most of the building: the fog
        // seemed to be everywhere. Now it's the two cold back rooms only (the owner's call).
        public static readonly string[] FogRooms = { "Cold storage", "Stockroom" };

        // Where the fog pours into a room from: its fridges; in a room without any (the
        // stockroom), the doorways it shares with a room that has them (the cold room's doors);
        // failing that, the middle of its own floor. Empty for a room that isn't on the map.
        public IReadOnlyList<Vector3> FogSources(string room)
        {
            if (room == null) return noSources;
            if (fogSources.TryGetValue(room, out List<Vector3> known)) return known;
            var sources = new List<Vector3>();
            foreach (Transform f in FridgesIn(room)) if (f != null) sources.Add(f.position);
            StoreMap map = StoreMap.Current;
            if (sources.Count == 0 && map != null)
            {
                foreach (Region r in map.Regions)
                {
                    if (!r.IsDoor || r.Name == null || !r.Name.Contains(room)) continue;
                    foreach (string other in FogRooms)
                        if (other != room && r.Name.Contains(other) && FridgesIn(other).Count > 0) { sources.Add(r.Centroid); break; }
                }
                if (sources.Count == 0)
                    foreach (Region r in map.Regions)
                        if (!r.IsDoor && r.Area == room && sources.Count < 3) sources.Add(r.Centroid);
            }
            // A room of fridges (the cold room has 46) pours from eight of them, spread out: enough
            // to fill it, without thousands of particles.
            if (sources.Count > MaxFogSources)
            {
                var spread = new List<Vector3>();
                for (int i = 0; i < MaxFogSources; i++) spread.Add(sources[i * sources.Count / MaxFogSources]);
                sources = spread;
            }
            if (map != null) fogSources[room] = sources;   // found once per scene, like the fridges
            return sources;
        }

        public const int MaxFogSources = 8;
        readonly Dictionary<string, List<Vector3>> fogSources = new Dictionary<string, List<Vector3>>();
        static readonly List<Vector3> noSources = new List<Vector3>();

        Dictionary<string, List<Transform>> fridges;
        static readonly List<Transform> none = new List<Transform>();

        // The fridges, chillers and freezers in a room, where the fog pours out from. Found in
        // the scene once, by name, under the store (not the authoring shelf off to one side).
        public IReadOnlyList<Transform> FridgesIn(string room)
        {
            if (fridges == null) FindFridges();
            return room != null && fridges.TryGetValue(room, out List<Transform> list) ? list : none;
        }

        public static bool IsFridge(string name)
        {
            string n = name.ToLowerInvariant();
            return (n.Contains("chiller") || n.Contains("fridge") || n.Contains("freezer")) && !n.Contains("label");
        }

        void FindFridges()
        {
            fridges = new Dictionary<string, List<Transform>>();
            foreach (Transform t in FindObjectsByType<Transform>())
            {
                if (!IsFridge(t.name) || !UnderTheStore(t)) continue;
                string room = StoreMap.AreaAt(t.position);
                if (System.Array.IndexOf(FogRooms, room) < 0) continue;
                if (!fridges.TryGetValue(room, out List<Transform> list)) fridges[room] = list = new List<Transform>();
                // One per fridge: its parts (doors, shelves) sit within a metre of it.
                bool known = false;
                foreach (Transform f in list) if (FairnessGuard.Flat(f.position, t.position) < 1f) { known = true; break; }
                if (!known) list.Add(t);
            }
        }

        static bool UnderTheStore(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent) if (p.name == "Supermarket") return true;
            return false;
        }

        // Flood a room: the fog rolls out of its sources (FogVisuals draws it, for someone standing
        // in the room) and everyone in it sees `visibility` metres, her included.
        public void FogRoom(string room, float visibility) => FogState.Fill(room, visibility);

        // Camera bolt-on (0.3.0): a camera standing in a shelf slot, turned and tilted. Hands-on.
        public const int MaxCameras = 2;   // of hers at once (build plan D7)

        public static int HerCameras
        {
            get
            {
                int n = 0;
                foreach (CctvCamera cam in CctvCamera.All) if (cam != null && cam.BoltedOn && !cam.Dead) n++;
                return n;
            }
        }

        public CctvCamera MountCamera(ShelfSlot slot, float yaw, float pitch)
        {
            if (slot == null) return null;
            FairnessGuard.CheckHandsOn("camera bolt-on", Hands, slot.Position);
            CctvCamera cam = CctvCamera.Mount(slot, yaw, pitch);
            if (cam != null) Track(cam.gameObject);
            return cam;
        }

        // ---- work ---------------------------------------------------------------------

        // Her hands, for the rule 9 audit: where her body stands, if she has one.
        static KarenBody Hands => KarenBrain.Instance != null ? KarenBrain.Instance.Body : null;

        public Dirt Spill(Vector3 at)
        {
            if (DirtPrefab == null) return null;
            FairnessGuard.CheckHandsOn("spill", Hands, at);
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

        // ---- phantom chime (0.3.0) ------------------------------------------------------

        public const float PhantomSeconds = 20f;   // the owner's 20 s
        public const float PhantomPeriod = 3.2f;    // open, held, shut, again (build plan D3)

        Coroutine phantom;
        public AutoDoubleDoor PhantomDoor { get; private set; }
        public float PhantomUntil { get; private set; } = -1f;
        public bool PhantomRunning => PhantomDoor != null && Time.time < PhantomUntil;

        // The front doors open and shut by themselves, chiming each time, with nobody there.
        // A real person walking in takes over as normal: the doors just stay open for them.
        // Her own ears know it's her (the noise is hers); a shopper's, a bot's and yours don't.
        public void PhantomChime(AutoDoubleDoor door, float seconds = PhantomSeconds)
        {
            if (door == null) return;
            StopPhantom();
            PhantomDoor = door;
            PhantomUntil = Time.time + seconds;
            phantom = StartCoroutine(Phantom(door));
        }

        IEnumerator Phantom(AutoDoubleDoor door)
        {
            while (door != null && Time.time < PhantomUntil)
            {
                if (!door.IsOpen)
                {
                    door.PhantomCycle();
                    NoiseBus.Emit(door.transform.position, 0.55f, NoiseKind.AutoDoor, NoiseAuthor.Karen);
                }
                yield return new WaitForSeconds(PhantomPeriod);
            }
            phantom = null;
            PhantomDoor = null;
        }

        void StopPhantom()
        {
            if (phantom != null) StopCoroutine(phantom);
            phantom = null;
            PhantomDoor = null;
            PhantomUntil = -1f;
        }

        // ---- space --------------------------------------------------------------------

        public CrateWall Crates(Vector3 at, Vector3 across, float width, int region)
        {
            CrateWall wall = CrateWall.Spawn(at, across, width);
            wall.Region = region;
            Track(wall.gameObject);
            return wall;
        }

        // Block door has no timer since 0.3.0: a door stays blocked until someone moves the box
        // (DoorBlock), so there's no lock of hers to time out here.

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
            // A tool she hid goes back on its rack for the next shift (Tool theft).
            KarenBody body = Hands;
            if (body != null) body.DropCarried();
            var racks = new List<ToolSnapPoint>(ToolSnapPoint.TakenRacks);
            foreach (ToolSnapPoint rack in racks)
            {
                if (rack == null) continue;
                if (rack.tool != null)
                {
                    ItemHome home = rack.tool.GetComponent<ItemHome>();
                    if (home != null) home.ReturnHome();
                    else rack.tool.transform.SetPositionAndRotation(rack.transform.position, rack.transform.rotation);
                }
                rack.MarkTaken(false);
            }

            foreach (GameObject go in placed) if (go != null) Destroy(go);
            placed.Clear();
            DoorBlock.ClearAll();
            StoreCameras.Instance?.ResetForShift();   // 0.4.1: the store's cameras work again
            FogState.Reset();
            StopPhantom();
            Pa.Clear();
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
