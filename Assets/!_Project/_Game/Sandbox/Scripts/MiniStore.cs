#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Sandbox
{
    // The mini store sandbox (0.5.2, C11.17, the owner's call of 24 November): a small copy of the
    // store with three rooms, a sales floor, a cold room and a stockroom, made when Play starts
    // from copies of the store's own models (its tall and short shelves, racks, chillers, doors,
    // lamps, till and bins). It stands on the sandbox island west of the store, and you start
    // inside it. Nothing is saved into the scene.
    //
    // Its plan, in metres from its centre (x east, z north; the store's own way round, the front
    // to the north):
    //
    //   z 13  ──────── front wall, the shoppers' automatic doors at x 9 ────────
    //         SALES FLOOR  x -15..15, z -3..13: tall shelves west, short shelves east,
    //                      the till by the front, bins, two chillers on the back wall
    //   z -3  ──── door at x -6 ──────────────────┬──── door at x 9 ───────────
    //         STOCKROOM  x -15..3: racks          │ COLD ROOM  x 3..15: chillers,
    //                                    door at z -7  two you can hide in
    //   z -11 ─────────────────────────────────────┴───────────────────────────
    public static class MiniStore
    {
        public const string SessionKey = "Kehai.Sandbox.MiniStore";
        public const string StoreScene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
        public const string RootName = "Mini store";

        // On the sandbox island's ground (SandboxIsland: x -80..0, z -49.9..30.1).
        public static readonly Vector3 Origin = new Vector3(-40f, 0f, -10f);
        public const float WallHeight = 6f, Wall = 0.2f, DoorGap = 2.1f, AutoDoorGap = 3.2f;
        public const float West = -15f, East = 15f, Front = 13f, Middle = -3f, Back = -11f, ColdWest = 3f;
        public static readonly Vector3 PlayerStart = new Vector3(0f, 0f, 10.5f);
        public static readonly Vector3 StockDoor = new Vector3(-6f, 0f, Middle);
        public static readonly Vector3 ColdDoor = new Vector3(9f, 0f, Middle);
        public static readonly Vector3 InnerDoor = new Vector3(ColdWest, 0f, -7f);
        public static readonly Vector3 ShopperDoor = new Vector3(9f, 0f, Front);

        // The rooms, in its own metres.
        public static readonly Rect SalesFloor = Rect.MinMaxRect(West, Middle, East, Front);
        public static readonly Rect Stockroom = Rect.MinMaxRect(West, Back, ColdWest, Middle);
        public static readonly Rect ColdRoom = Rect.MinMaxRect(ColdWest, Back, East, Middle);

        public static bool Active { get; private set; }
        public static GameObject Root { get; private set; }
        public static string Report { get; private set; } = "";

        public static Vector3 World(Vector3 local) => Origin + local;
        public static Vector3 Local(Vector3 world) => world - Origin;
        public static bool Contains(Vector3 world)
        {
            Vector3 p = Local(world);
            return p.x >= West - Wall && p.x <= East + Wall && p.z >= Back - Wall && p.z <= Front + Wall;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Active = SessionState.GetBool(SessionKey, false);
            Root = null;
            if (!Active) return;
            // Before the store's own handlers (StoreLayout stocks every shelf it finds on load,
            // the copies included), so this one is added first.
            SceneManager.sceneLoaded += OnLoaded;
        }

        public static void EndSession()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Active = false;
            Root = null;
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.path != StoreScene) return;
            Root = Build(scene);
        }

        // ---- the building ----------------------------------------------------------------

        public static GameObject Build(Scene scene)
        {
            foreach (GameObject top in scene.GetRootGameObjects())
                if (top.name == RootName) return top;
            var report = new List<string>();
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = Origin;
            var models = new Models(report);

            Shell(root.transform, models);
            Furnish(root.transform, models, report);
            Lamps(root.transform, models);

            // The store's own player starts inside, by the till, facing the shelves.
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                var start = root.AddComponent<MiniStoreStart>();
                start.player = player.transform;
            }
            Physics.SyncTransforms();
            Report = string.Join("\n", report);
            Debug.Log("MiniStore: built.\n" + Report);
            return root;
        }

        // The floor, the walls with their doorways, a plinth along them and the ceiling, in the
        // store's own materials.
        static void Shell(Transform root, Models m)
        {
            Transform shell = Group(root, "Shell");
            float w = East - West, d = Front - Back;
            Slab(shell, "Floor", new Vector3(0f, -0.05f, (Front + Back) * 0.5f), new Vector3(w + Wall, 0.12f, d + Wall), m.Floor);
            Slab(shell, "Ceiling", new Vector3(0f, WallHeight + 0.05f, (Front + Back) * 0.5f), new Vector3(w + Wall, 0.1f, d + Wall), m.Ceiling);

            // Outside walls.
            WallX(shell, "Front wall", Front, West, East, m, ShopperDoor.x, AutoDoorGap);
            WallX(shell, "Back wall", Back, West, East, m);
            WallZ(shell, "West wall", West, Back, Front, m);
            WallZ(shell, "East wall", East, Back, Front, m);
            // Inside: the sales floor's back wall with a door to each room, and the wall between
            // the stockroom and the cold room with a door.
            WallX(shell, "Sales floor back wall", Middle, West, East, m, StockDoor.x, DoorGap, ColdDoor.x, DoorGap);
            WallZ(shell, "Cold room wall", ColdWest, Back, Middle, m, InnerDoor.z, DoorGap);
        }

        // A wall along x at `z`, from `x0` to `x1`, with doorway gaps given as (centre, width) pairs.
        static void WallX(Transform parent, string name, float z, float x0, float x1, Models m, params float[] gaps)
        {
            foreach (Vector2 piece in Pieces(x0, x1, gaps))
            {
                float len = piece.y - piece.x, mid = (piece.x + piece.y) * 0.5f;
                Slab(parent, name, new Vector3(mid, WallHeight * 0.5f, z), new Vector3(len, WallHeight, Wall), m.Wall);
                Slab(parent, name + " plinth", new Vector3(mid, 0.4f, z), new Vector3(len, 0.8f, Wall + 0.04f), m.Plinth, collide: false);
            }
            for (int i = 0; i + 1 < gaps.Length; i += 2)
                Slab(parent, name + " over the door", new Vector3(gaps[i], (WallHeight + 2.6f) * 0.5f, z), new Vector3(gaps[i + 1], WallHeight - 2.6f, Wall), m.Wall);
        }

        static void WallZ(Transform parent, string name, float x, float z0, float z1, Models m, params float[] gaps)
        {
            foreach (Vector2 piece in Pieces(z0, z1, gaps))
            {
                float len = piece.y - piece.x, mid = (piece.x + piece.y) * 0.5f;
                Slab(parent, name, new Vector3(x, WallHeight * 0.5f, mid), new Vector3(Wall, WallHeight, len), m.Wall);
                Slab(parent, name + " plinth", new Vector3(x, 0.4f, mid), new Vector3(Wall + 0.04f, 0.8f, len), m.Plinth, collide: false);
            }
            for (int i = 0; i + 1 < gaps.Length; i += 2)
                Slab(parent, name + " over the door", new Vector3(x, (WallHeight + 2.6f) * 0.5f, gaps[i]), new Vector3(Wall, WallHeight - 2.6f, gaps[i + 1]), m.Wall);
        }

        // The solid stretches of a wall from a to b once its doorways are cut out.
        public static List<Vector2> Pieces(float a, float b, float[] gaps)
        {
            var cuts = new List<Vector2>();
            for (int i = 0; i + 1 < gaps.Length; i += 2) cuts.Add(new Vector2(gaps[i] - gaps[i + 1] * 0.5f, gaps[i] + gaps[i + 1] * 0.5f));
            cuts.Sort((p, q) => p.x.CompareTo(q.x));
            var pieces = new List<Vector2>();
            float at = a - Wall * 0.5f;
            foreach (Vector2 cut in cuts)
            {
                if (cut.x > at) pieces.Add(new Vector2(at, cut.x));
                at = Mathf.Max(at, cut.y);
            }
            float end = b + Wall * 0.5f;
            if (end > at) pieces.Add(new Vector2(at, end));
            return pieces;
        }

        // What stands in each room: copies of the store's own fixtures.
        static void Furnish(Transform root, Models m, List<string> report)
        {
            Transform sales = Group(root, "Sales floor");
            // Tall shelves to the west, short to the east: two runs of two bays each, in rows
            // 5 m apart, so the aisles are as wide as the store's.
            foreach (float z in new[] { 2f, 7f })
            {
                Place(sales, m.TallBay, new Vector3(-11f, 0f, z), 0f, "Tall shelf", report);
                Place(sales, m.TallBay, new Vector3(-7f, 0f, z), 0f, "Tall shelf", report);
                Place(sales, m.LowBay, new Vector3(1f, 0f, z), 90f, "Short shelf", report);
                Place(sales, m.LowBay, new Vector3(5f, 0f, z), 90f, "Short shelf", report);
            }
            // Two chillers on the back wall, between the doors; the till along the front wall.
            Place(sales, m.RetailFreezer, new Vector3(0.5f, 0f, Middle + 0.8f), 0f, "Chiller", report);
            Place(sales, m.RetailFreezer, new Vector3(2.5f, 0f, Middle + 0.8f), 0f, "Chiller", report);
            Place(sales, m.Checkout, new Vector3(-10f, 0f, Front - 1.6f), 90f, "Till", report);
            Place(sales, m.Bins, new Vector3(-13.6f, 0f, Front - 0.45f), 0f, "Bins", report);

            Transform stock = Group(root, "Stockroom");
            Place(stock, m.Rack, new Vector3(-11.5f, 0f, Back + 1.05f), 0f, "Rack", report);
            Place(stock, m.Rack, new Vector3(-5.5f, 0f, Back + 1.05f), 0f, "Rack", report);
            Place(stock, m.Rack, new Vector3(-12.5f, 0f, Middle - 2.6f), 90f, "Rack", report);

            Transform cold = Group(root, "Cold room");
            Place(cold, m.HideChiller, new Vector3(6.5f, 0f, Middle - 0.8f), 0f, "Chiller you can hide in", report);
            Place(cold, m.HideChiller, new Vector3(12.5f, 0f, Middle - 0.8f), 0f, "Chiller you can hide in", report);
            Place(cold, m.ColdChiller, new Vector3(East - 0.8f, 0f, -8.5f), 0f, "Cold chiller", report);
            Place(cold, m.ColdChiller, new Vector3(9f, 0f, Back + 0.9f), 90f, "Cold chiller", report);

            Transform doors = Group(root, "Doors");
            Place(doors, m.DoorAlongX, StockDoor, 0f, "Door to the stockroom", report);
            Place(doors, m.DoorAlongX, ColdDoor, 0f, "Door to the cold room", report);
            Place(doors, m.DoorAlongZ, InnerDoor, 0f, "Door between the stockroom and the cold room", report);
            Place(doors, m.AutoDoor, ShopperDoor, 0f, "Shoppers' doors", report);
        }

        // Lamps from the store's own model, a light under each in a "CeilingLights" group so her
        // light sense (LightProbe) counts them, spaced 5 m apart as in the store.
        static void Lamps(Transform root, Models m)
        {
            var lights = new GameObject("CeilingLights").transform;
            lights.SetParent(root, false);
            var spots = new List<Vector3>();
            for (float x = West + 2.5f; x < East; x += 5f)
                for (float z = Middle + 3f; z < Front; z += 5f) spots.Add(new Vector3(x, 0f, z));
            for (float x = West + 3f; x < ColdWest; x += 6f) spots.Add(new Vector3(x, 0f, -7f));
            for (float x = ColdWest + 3f; x < East; x += 6f) spots.Add(new Vector3(x, 0f, -7f));
            float height = WallHeight - 0.4f;
            float scale = height / m.LampHeight;
            int n = 0;
            foreach (Vector3 at in spots)
            {
                n++;
                if (m.Lamp != null)
                {
                    GameObject model = Object.Instantiate(m.Lamp, lights);
                    model.name = "Lamp " + n;
                    model.transform.localPosition = at + Vector3.up * (WallHeight - (m.CeilingHeight - m.Lamp.transform.position.y));
                    model.transform.localRotation = Quaternion.identity;
                }
                var go = new GameObject("Ceiling light " + n);
                go.transform.SetParent(lights, false);
                go.transform.localPosition = at + Vector3.up * height;
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Light light = go.AddComponent<Light>();
                light.type = m.LightType;
                light.spotAngle = m.SpotAngle;
                light.innerSpotAngle = m.InnerSpotAngle;
                light.color = m.LightColour;
                light.range = m.LightRange * scale;
                light.intensity = m.LightIntensity * scale * scale;
                light.shadows = LightShadows.None;
            }
            Kehai.Karen.LightProbe.Refresh();
        }

        // ---- helpers -----------------------------------------------------------------------

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Slab(Transform parent, string name, Vector3 centre, Vector3 size, Material material, bool collide = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            if (!collide) Object.Destroy(go.GetComponent<Collider>());
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            go.isStatic = false;
            return go;
        }

        // A copy of one of the store's fixtures, its footprint centred on `at` (mini store metres,
        // on the floor), turned `turn` degrees from the way the original stands. Renamed, so
        // nothing that looks the store's fixtures up by name finds the copy instead.
        static GameObject Place(Transform parent, GameObject original, Vector3 at, float turn, string name, List<string> report)
        {
            if (original == null) { report.Add($"missing model for {name}"); return null; }
            GameObject copy = Object.Instantiate(original, original.transform.position, original.transform.rotation, parent);
            copy.name = "Mini " + name;
            Vector3 offset = Footprint(copy).center - copy.transform.position;
            Quaternion spin = Quaternion.Euler(0f, turn, 0f);
            copy.transform.rotation = spin * original.transform.rotation;
            Vector3 world = World(at);
            Vector3 turned = spin * offset;
            copy.transform.position = new Vector3(world.x - turned.x, original.transform.position.y, world.z - turned.z);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            return copy;
        }

        public static Bounds Footprint(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // The store's originals to copy, found by their names in the scene (SCENE_MAP.md).
        sealed class Models
        {
            public GameObject TallBay, LowBay, RetailFreezer, Checkout, Bins, Rack, HideChiller, ColdChiller,
                DoorAlongX, DoorAlongZ, AutoDoor, Lamp;
            public Material Floor, Wall, Plinth, Ceiling;
            public float CeilingHeight = 8f, LampHeight = 7.6f;
            public LightType LightType = LightType.Spot;
            public float SpotAngle = 120f, InnerSpotAngle = 60f, LightRange = 17f, LightIntensity = 40f;
            public Color LightColour = new Color(1f, 0.96f, 0.88f);

            public Models(List<string> report)
            {
                TallBay = Need("Shelf_Double_Sided_Tall_10", report);
                LowBay = Need("Shelf_Double_Sided_Low_01", report);
                RetailFreezer = Need("Retail_Freezer_02", report);
                Checkout = Need("Checkout_03", report);
                Bins = Need("Sales_Floor_Bin_01", report);
                Rack = Need("Storage_Rack_07", report);
                HideChiller = Need("Cold_Hiding_Cabinet_04", report);
                ColdChiller = Need("Storage_Chiller_16", report);
                DoorAlongX = Need("Door_05", report);
                DoorAlongZ = Need("Door_06", report);
                AutoDoor = Need("Auto_Double_Door_03", report);
                Lamp = Need("Lamp_01", report);
                Floor = MaterialOf("Original_Floor_01", report);
                Ceiling = MaterialOf("Roof_Slab_01", report);
                Wall = MaterialOf("Cream_Wall_Panel_02", report);
                Plinth = MaterialOf("Charcoal_Plinth_01", report);
                GameObject light = Find("Ceiling_Light_42_5_132_01");
                if (light != null && light.TryGetComponent(out Light l))
                {
                    LightType = l.type;
                    SpotAngle = l.spotAngle;
                    InnerSpotAngle = l.innerSpotAngle;
                    LightRange = l.range;
                    LightIntensity = l.intensity;
                    LightColour = l.color;
                    LampHeight = light.transform.position.y;
                }
            }

            static GameObject Find(string name) => SceneObjectNames.Find(name) ?? GameObject.Find(name);

            static GameObject Need(string name, List<string> report)
            {
                GameObject go = Find(name);
                report.Add(go != null ? $"model {name}" : $"MISSING model {name}");
                return go;
            }

            static Material MaterialOf(string name, List<string> report)
            {
                GameObject go = Find(name);
                Renderer r = go != null ? go.GetComponentInChildren<Renderer>() : null;
                if (r == null) { report.Add($"MISSING material from {name}"); return null; }
                return r.sharedMaterial;
            }
        }
    }

    // Puts the store's player inside the mini store once the store has placed them, and again a
    // moment later in case the shift's start moves them back to the lobby.
    public sealed class MiniStoreStart : MonoBehaviour
    {
        public Transform player;
        int frames;

        void Start() => Move();

        void Update()
        {
            frames++;
            if (frames == 2 || frames == 30) Move();
            if (frames > 30) enabled = false;
        }

        void Move()
        {
            if (player == null) return;
            if (MiniStore.Contains(player.position) && frames > 2) return;
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.SetPositionAndRotation(MiniStore.World(MiniStore.PlayerStart) + Vector3.up * 0.1f, Quaternion.Euler(0f, 180f, 0f));
            if (controller != null) controller.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
#endif
