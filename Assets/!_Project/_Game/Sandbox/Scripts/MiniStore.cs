#if UNITY_EDITOR
using System.Collections.Generic;
using Kehai.Store;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kehai.Sandbox
{
    // The mini store sandbox (0.5.2, C11.17; the owner's calls of 24 and 25 November): the whole
    // store at half size, the lobby, the sales floor, the WC, the office, the staff room, the
    // backstreet, the stockroom and the cold room (not the parking lot), made when Play starts on the
    // sandbox island west of the store. Every wall and door stands where the store's does at half the
    // distance; the furniture is the store's own at full size (you and Karen are full size), nudged
    // inside its room where halving the distance put it in a wall. Nothing is saved into the scene.
    //
    // Half size is one map both ways (Half, ToStore), so every table that reads the store's metres
    // (her rooms, the stock's aisles, the lamps' breakers, the cameras' spots) reads the mini store
    // through StoreFrame without a table of its own.
    public static class MiniStore
    {
        public const string SessionKey = "Kehai.Sandbox.MiniStore";
        public const string StoreScene = "Assets/!_Project/_Core/Scenes/SampleScene.unity";
        public const string RootName = "Mini store";

        // On the sandbox island's ground (SandboxIsland: x -80..0, z -49.9..30.1).
        public static readonly Vector3 Origin = new Vector3(-40f, 0f, -12f);
        // The store's middle (its walls run x 10..85, z -190..-110) and how much smaller this is.
        public static readonly Vector3 StoreCentre = new Vector3(47.5f, 0f, -150f);
        public const float Scale = 0.5f;
        public const float WallHeight = 6f, Wall = 0.2f, DoorGap = 2.1f, StaffDoorGap = 1.7f, AutoDoorGap = 3.2f;

        // The store's metres to this one's (heights unchanged), and back.
        public static Vector3 Half(Vector3 store) => Origin + new Vector3((store.x - StoreCentre.x) * Scale, store.y, (store.z - StoreCentre.z) * Scale);
        public static Vector3 HalfLocal(Vector3 store) => Half(store) - Origin;
        public static Vector3 World(Vector3 local) => Origin + local;
        public static Vector3 Local(Vector3 world) => world - Origin;

        // The building, x 10..85 and z -190..-110 in the store, and its pavement to the store's
        // pavement edge (StoreMap.PavementEdge, z -106).
        public static readonly Rect StoreBuilding = Rect.MinMaxRect(10f, -190f, 85f, -110f);
        public const float StorePavementEdge = -106f;

        public static bool Contains(Vector3 world)
        {
            Vector3 s = StoreCentre + new Vector3((world.x - Origin.x) / Scale, 0f, (world.z - Origin.z) / Scale);
            return s.x >= StoreBuilding.xMin - 0.6f && s.x <= StoreBuilding.xMax + 0.6f &&
                   s.z >= StoreBuilding.yMin - 0.6f && s.z <= StorePavementEdge + 0.6f;
        }

        // A point here, as the matching point in the store; anywhere else, itself.
        public static Vector3 ToStore(Vector3 world)
        {
            if (!Contains(world)) return world;
            return StoreCentre + new Vector3((world.x - Origin.x) / Scale, world.y, (world.z - Origin.z) / Scale);
        }

        // Her map of the mini store (StoreMap.TestArea): the building and its pavement.
        public static Bounds MapBounds
        {
            get
            {
                Vector3 a = Half(new Vector3(StoreBuilding.xMin, 0f, StoreBuilding.yMin)), b = Half(new Vector3(StoreBuilding.xMax, 0f, StorePavementEdge));
                var bounds = new Bounds((a + b) * 0.5f + Vector3.up * 2f, new Vector3(b.x - a.x + 1f, 6f, b.z - a.z + 1f));
                return bounds;
            }
        }

        // The store's rooms (StoreMap.AreaAt's lines), in its metres; the backstreet is outdoors.
        public static readonly (string name, Rect store, bool outdoors)[] Rooms =
        {
            ("Lobby", Rect.MinMaxRect(25f, -120f, 85f, -110f), false),
            ("Sales floor", Rect.MinMaxRect(25f, -175f, 85f, -120f), false),
            ("WC", Rect.MinMaxRect(10f, -123.33f, 25f, -110f), false),
            ("Office", Rect.MinMaxRect(10f, -136.67f, 25f, -123.33f), false),
            ("Staff room", Rect.MinMaxRect(10f, -150f, 25f, -136.67f), false),
            ("Backstreet", Rect.MinMaxRect(10f, -175f, 25f, -150f), true),
            ("Stockroom", Rect.MinMaxRect(10f, -190f, 55f, -175f), false),
            ("Cold storage", Rect.MinMaxRect(55f, -190f, 85f, -175f), false),
        };

        // Its walls, in the store's metres: (from, to, doorways as centre and width in metres).
        // The doorways keep their real width; the store's own doors are copied into them.
        static readonly (Vector2 a, Vector2 b, float[] gaps)[] Walls =
        {
            (new Vector2(10f, -110f), new Vector2(85f, -110f), new[] { 42.5f, AutoDoorGap, 67.5f, AutoDoorGap }),   // the front
            (new Vector2(10f, -190f), new Vector2(85f, -190f), new float[0]),                                       // the back
            (new Vector2(10f, -190f), new Vector2(10f, -110f), new float[0]),                                       // west (the yard closed off: the alley isn't hers)
            (new Vector2(85f, -190f), new Vector2(85f, -110f), new float[0]),                                       // east
            (new Vector2(25f, -120f), new Vector2(85f, -120f), new[] { 42.5f, AutoDoorGap, 67.5f, AutoDoorGap }),   // lobby to the sales floor
            (new Vector2(25f, -175f), new Vector2(25f, -110f), new[] { -116.7f, StaffDoorGap, -130f, StaffDoorGap, -142.5f, StaffDoorGap }), // the west rooms and the yard
            (new Vector2(10f, -123.33f), new Vector2(25f, -123.33f), new float[0]),                                 // WC | office
            (new Vector2(10f, -136.67f), new Vector2(25f, -136.67f), new[] { OfficeDoorX, StaffDoorGap }),         // office | staff room
            (new Vector2(10f, -150f), new Vector2(25f, -150f), new[] { 17.5f, StaffDoorGap }),                     // staff room | yard
            (new Vector2(10f, -175f), new Vector2(85f, -175f), new[] { 17.5f, DoorGap, 42.5f, DoorGap, 67.5f, DoorGap }), // the back rooms
            (new Vector2(55f, -190f), new Vector2(55f, -175f), new[] { -182.5f, DoorGap }),                        // stockroom | cold room
        };
        // The office's door to the staff room stands 0.75 m from the west rooms' wall in the store;
        // at half the distance its leaf would meet that wall, so it moves 0.9 m along.
        const float OfficeDoorX = 22.6f;

        // The store's doors, copied to where they stand here (their store paths).
        static readonly (string path, Vector3? store)[] Doors =
        {
            ("Supermarket/Shared_Building/Doors/Automatic_Doors_01/Auto_Double_Door_01", null),
            ("Supermarket/Shared_Building/Doors/Automatic_Doors_01/Auto_Double_Door_02", null),
            ("Supermarket/Shared_Building/Doors/Automatic_Doors_01/Auto_Double_Door_03", null),
            ("Supermarket/Shared_Building/Doors/Automatic_Doors_01/Auto_Double_Door_04", null),
            ("Supermarket/Shared_Building/Doors/Hinged_Doors_01/Door_03", null),
            ("Supermarket/Shared_Building/Doors/Hinged_Doors_01/Door_04", null),
            ("Supermarket/Shared_Building/Doors/Hinged_Doors_01/Door_05", null),
            ("Supermarket/Shared_Building/Doors/Hinged_Doors_01/Door_06", null),
            ("Supermarket/Staff_Room/Doors/Staff_Entrance_01/Hinge_Staff_Door_01", null),
            ("Supermarket/Office/Doors/WC_Entrance_01/Hinge_Staff_Door_01", null),
            ("Supermarket/Office/Doors/Office_Entrance_01/Hinge_Staff_Door_01", null),
            ("Supermarket/Staff_Room/Doors/Office_Connection_01/Hinge_Staff_Door_01", new Vector3(OfficeDoorX, 0f, -136.67f)),
            ("Supermarket/Staff_Room/Doors/Backstreet_Connection_01/Hinge_Staff_Door_01", null),
        };

        // The store's furniture copied here: (name or path, where in the store, or null for where it
        // stands there; turn). Each lands at half the distance, inside its room.
        static readonly (string name, Vector3? store, float turn)[] Copies =
        {
            // The lobby.
            ("Lobby_Furnished_Recycling_01", null, 0f), ("Lobby_Furnished_Recycling_02", null, 0f), ("Lobby_Furnished_Recycling_03", null, 0f),
            // The sales floor's till (one) and a bin (its shelves and chillers are laid out below).
            // Only what's used: the lobby's service counter and vending machine, the WC's cabins,
            // the office's filing cabinet and the second till stood in the way and did nothing.
            ("Checkout_03", null, 0f),
            ("Sales_Floor_Bin_02", new Vector3(54f, 0f, -121f), 0f),
            // The staff room: the counter the coffee machine stands on, the table the torch lies on,
            // lockers you can hide in, the bin, the dining table (the fridge would stand in the counter).
            ("Appliance_Counter_01", null, 0f), ("Uniform_Folding_Table_01", new Vector3(17f, 0f, -146.4f), 0f),
            ("Locker_01", new Vector3(11.5f, 0f, -137.5f), 0f), ("Locker_02", new Vector3(13.9f, 0f, -137.5f), 0f), ("Locker_03", new Vector3(16.3f, 0f, -137.5f), 0f),
            ("Staff_Room_Furnished_Recycling_01", new Vector3(21f, 0f, -149.6f), 0f),
            ("Shared_Dining_Table_01", null, 0f),
        };

        // The store's own things her tricks and your chores are built beside, moved here (one of
        // each, found by kind or name): (name, where in the store or null for where it stands, the
        // thing it sits on or null). A thing on another keeps its place on it.
        static readonly (string name, Vector3? store, string on)[] Anchors =
        {
            ("Electric_Box_02", null, null),                    // the power box: its EAST/WEST/BACK switches and the fog box beside it
            ("Container_01", new Vector3(22.6f, 0f, -168f), null), // the bin bags' drop zone
            ("Waste_Collection_Container_01", new Vector3(23.8f, 0f, -160f), null),
            ("Paper_Collection_Container_01", new Vector3(23.8f, 0f, -169.5f), null),
            ("Service_Yard_Light_01", new Vector3(24.6f, 3.4f, -154f), null), ("Service_Yard_Light_02", new Vector3(24.6f, 3.4f, -168f), null),
            ("Time_Clock_01", null, null),                      // the PA box and its microphone go up beside it
            ("Staff_Radio_01", null, null),
            ("Coffee_Machine_01", null, "Appliance_Counter_01"),
            ("Flashlight_01", null, "Uniform_Folding_Table_01"),
            ("Mop_Snap_Point_01", new Vector3(24.5f, 1.0f, -147f), null),       // its rack, on the staff room's east wall
            ("Mop_01", null, "Mop_Snap_Point_01"),
            ("Stock_Snap_Point_01", new Vector3(14.6f, 0.5f, -148.7f), null),  // the stock crate: in the staff room, as in the store
            ("Stock_Test_Crate_01", null, "Stock_Snap_Point_01"),
            ("WC_Mop_Home_01", null, null), ("WC_Cleaning_Mop_01", null, "WC_Mop_Home_01"),
            ("Security workstation", null, null),               // the cameras' screens go up on it
            ("Counter_Facing_01", null, null),
            ("Storage_Rack_07", new Vector3(20f, 0f, -187.8f), null), ("Storage_Rack_08", new Vector3(32f, 0f, -187.8f), null),
            ("Storage_Rack_03", new Vector3(44f, 0f, -187.8f), null),
        };

        public static readonly Vector3 PlayerStartStore = new Vector3(57.5f, 0f, -128f);   // the sales floor, by the tills
        public static Vector3 PlayerStart => HalfLocal(PlayerStartStore);
        public static Vector3 KarenStart => HalfLocal(new Vector3(32.5f, 0f, -172.5f));     // where she starts in the store

        public static bool Active { get; private set; }
        public static GameObject Root { get; private set; }
        public static string Report { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            Active = SessionState.GetBool(SessionKey, false);
            Root = null;
            if (!Active) return;
            StoreFrame.Use(ToStore);
            StoreMap.UseTestArea(MapBounds);   // her map: the mini store only
            // Before the store's own handlers (StoreLayout stocks every shelf it finds on load,
            // the copies included), so this one is added first.
            SceneManager.sceneLoaded += OnLoaded;
        }

        public static void EndSession()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            if (Active)
            {
                StoreFrame.Use(null);
                StoreMap.UseTestArea(null);
            }
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
            CopyDoors(root.transform, report);
            CopyFurniture(root.transform, report);
            SalesFloor(root.transform, models, report);
            ColdRoom(root.transform, models, report);
            MoveAnchors(root.transform, report);
            Shoppers(root.transform, report);
            Cameras(report);
            Lamps(root.transform, models);
            if (holder != null) Object.Destroy(holder.gameObject);
            holder = null;

            // Her floor: its own NavMesh, baked the way the store's is (StoreRebake leaves the
            // doors' leaves out, so doorways stay walkable). Then her map is cut from it, before the
            // store's room lighting and stocking read the map.
            Physics.SyncTransforms();
            Bake(root);
            StoreMap.Rebuild();
            report.Add($"her map: {StoreMap.Current.CellCount} cells, rooms {string.Join(", ", StoreMap.Current.Rooms)}; " +
                       $"{root.GetComponentsInChildren<HingeDoor>(true).Length} hinged doors");

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) root.AddComponent<MiniStoreStart>().player = player.transform;
            Physics.SyncTransforms();
            Report = string.Join("\n", report);
            Debug.Log("MiniStore: built.\n" + Report);
            return root;
        }

        // A floor for each room and a ceiling for each one indoors, marked as room surfaces
        // (InteriorLightAnchor, named floor/ceiling) so the store lights each by its own room;
        // the walls with their doorways and a plinth along them; the pavement out front.
        static void Shell(Transform root, Models m)
        {
            Transform shell = Group(root, "Shell");
            foreach (var (name, store, outdoors) in Rooms)
            {
                Vector3 a = HalfLocal(new Vector3(store.xMin, 0f, store.yMin)), b = HalfLocal(new Vector3(store.xMax, 0f, store.yMax));
                var centre = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
                var size = new Vector3(b.x - a.x + Wall, 0f, b.z - a.z + Wall);
                var floor = Slab(shell, name + " floor", centre + Vector3.down * 0.05f, size + Vector3.up * 0.12f, outdoors ? m.Yard ?? m.Floor : m.Floor);
                floor.AddComponent<InteriorLightAnchor>().outdoors = outdoors;
                if (!outdoors)
                    Slab(shell, name + " ceiling", centre + Vector3.up * (WallHeight + 0.05f), size + Vector3.up * 0.1f, m.Ceiling).AddComponent<InteriorLightAnchor>();
            }
            Vector3 p0 = HalfLocal(new Vector3(StoreBuilding.xMin, 0f, StoreBuilding.yMax)), p1 = HalfLocal(new Vector3(StoreBuilding.xMax, 0f, StoreBuilding.yMax));
            Slab(shell, "Pavement", new Vector3((p0.x + p1.x) * 0.5f, -0.05f, p0.z + 3.5f), new Vector3(p1.x - p0.x + 1f, 0.12f, 7f), m.Yard ?? m.Floor);

            foreach (var (a, b, gaps) in Walls)
            {
                bool alongX = Mathf.Approximately(a.y, b.y);
                Vector3 la = HalfLocal(new Vector3(a.x, 0f, a.y)), lb = HalfLocal(new Vector3(b.x, 0f, b.y));
                var local = new float[gaps.Length];
                for (int i = 0; i < gaps.Length; i += 2)
                {
                    Vector3 g = HalfLocal(alongX ? new Vector3(gaps[i], 0f, a.y) : new Vector3(a.x, 0f, gaps[i]));
                    local[i] = alongX ? g.x : g.z;
                    local[i + 1] = gaps[i + 1];
                }
                if (alongX) WallX(shell, "Wall", la.z, Mathf.Min(la.x, lb.x), Mathf.Max(la.x, lb.x), m, local);
                else WallZ(shell, "Wall", la.x, Mathf.Min(la.z, lb.z), Mathf.Max(la.z, lb.z), m, local);
            }
        }

        static void WallX(Transform parent, string name, float z, float x0, float x1, Models m, float[] gaps)
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

        static void WallZ(Transform parent, string name, float x, float z0, float z1, Models m, float[] gaps)
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

        // ---- what's in it --------------------------------------------------------------------

        static void CopyDoors(Transform root, List<string> report)
        {
            Transform doors = Group(root, "Doors");
            foreach (var (path, store) in Doors)
            {
                GameObject original = Find(path);
                if (original == null) { report.Add($"MISSING door {path}"); continue; }
                Vector3 at = HalfLocal(store ?? Footprint(original).center);
                Place(doors, original, at, 0f, Leaf(path), report, keepInside: false);
            }
        }

        static string Leaf(string path)
        {
            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;
            if (leaf == "Hinge_Staff_Door_01" && slash > 0)
            {
                string parent = path.Substring(0, slash);
                leaf = parent.Substring(parent.LastIndexOf('/') + 1);
            }
            return leaf;
        }

        static void CopyFurniture(Transform root, List<string> report)
        {
            Transform group = Group(root, "Furniture");
            foreach (var (name, store, turn) in Copies)
            {
                GameObject original = Find(name);
                if (original == null) { report.Add($"MISSING furniture {name}"); continue; }
                Vector3 at = HalfLocal(store ?? Footprint(original).center);
                Place(group, original, at, turn, name, report);
            }
        }

        // The sales floor here is 30 × 27.5 m: three runs of the store's tall and short bays with
        // two cross aisles, and its chillers along the back wall between the doors.
        static void SalesFloor(Transform root, Models m, List<string> report)
        {
            Transform sales = Group(root, "Sales floor");
            float[] bays = { -8f, -4f, 2f, 6f, 12f, 16f };
            foreach (float x in bays)
            {
                Place(sales, m.TallBay, new Vector3(x, 0f, 2.5f), 0f, "Tall shelf", report);
                Place(sales, m.LowBay, new Vector3(x, 0f, -2.5f), 90f, "Short shelf", report);
                Place(sales, m.TallBay, new Vector3(x, 0f, -7.5f), 0f, "Tall shelf", report);
            }
            float back = HalfLocal(new Vector3(0f, 0f, -175f)).z;
            foreach (float x in new[] { -10.2f, -8.2f, -6.2f, 0.5f, 2.5f, 4.5f, 6.5f, 13f, 15f, 17f })
                Place(sales, m.RetailFreezer, new Vector3(x, 0f, back + 0.8f), 0f, "Chiller", report);
        }

        // The cold room here is 15 × 7.5 m: two chillers you can hide in on its north wall, storage
        // chillers on its east and south walls.
        static void ColdRoom(Transform root, Models m, List<string> report)
        {
            Transform cold = Group(root, "Cold room");
            float north = HalfLocal(new Vector3(0f, 0f, -175f)).z, south = HalfLocal(new Vector3(0f, 0f, -190f)).z;
            float east = HalfLocal(new Vector3(85f, 0f, 0f)).x;
            Place(cold, m.HideChiller, new Vector3(6.5f, 0f, north - 0.8f), 0f, "Chiller you can hide in", report);
            Place(cold, m.HideChiller, new Vector3(13.5f, 0f, north - 0.8f), 0f, "Chiller you can hide in", report);
            Place(cold, m.ColdChiller, new Vector3(east - 0.8f, 0f, -16.5f), 0f, "Cold chiller", report);
            Place(cold, m.ColdChiller, new Vector3(9f, 0f, south + 0.9f), 90f, "Cold chiller", report);
            Place(cold, m.ColdChiller, new Vector3(12f, 0f, south + 0.9f), 90f, "Cold chiller", report);
        }

        static void MoveAnchors(Transform root, List<string> report)
        {
            Transform moved = Group(root, "Moved from the store");
            var placed = new Dictionary<string, (Vector3 from, Vector3 to)>();
            foreach (var (name, store, on) in Anchors)
            {
                GameObject go = Find(name);
                if (go == null) { report.Add($"MISSING {name}"); continue; }
                Vector3 before = go.transform.position;
                // Racks stay in the store's rack group: the stockroom's boxes are laid on its racks.
                bool rack = name.StartsWith("Storage_Rack_");
                if (on != null && placed.TryGetValue(on, out var host))
                {
                    if (!rack) go.transform.SetParent(moved, true);
                    go.transform.position = before + (host.to - host.from);
                }
                else
                {
                    Bounds b = Footprint(go);
                    Vector3 offset = b.size == Vector3.zero ? Vector3.zero : b.center - before;
                    Vector3 target = Half(store ?? (b.size == Vector3.zero ? before : b.center));
                    if (!rack) go.transform.SetParent(moved, true);
                    go.transform.position = new Vector3(target.x - offset.x, store.HasValue && store.Value.y > 0f ? store.Value.y : before.y, target.z - offset.z);
                    KeepInside(go);
                }
                // The yard's things are outdoor fixtures: the store lights them by the yard's lamps.
                if (StoreMap.AreaAt(go.transform.position) == "Backstreet")
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        if (r.GetComponentInParent<InteriorLightAnchor>() == null) r.gameObject.AddComponent<InteriorLightAnchor>().outdoors = true;
                placed[name] = (before, go.transform.position);
                report.Add($"moved {name} into the {StoreMap.AreaAt(go.transform.position)}");
            }
        }

        // Shoppers come in by the front's automatic doors, browse the aisles, pay at the till.
        static void Shoppers(Transform root, List<string> report)
        {
            CustomerSpawner spawner = Object.FindAnyObjectByType<CustomerSpawner>();
            if (spawner == null) { report.Add("MISSING the shoppers' spawner"); return; }
            Transform points = Group(root, "Shopper points");
            Transform Point(string name, Vector3 local, float yaw)
            {
                Transform t = Group(points, name);
                t.position = World(local);
                t.rotation = Quaternion.Euler(0f, yaw, 0f);
                return t;
            }
            float front = HalfLocal(new Vector3(0f, 0f, -110f)).z;
            spawner.transform.position = World(new Vector3(HalfLocal(new Vector3(67.5f, 0f, 0f)).x, 0f, front + 5f));
            spawner.entrancePoint = Point("Entrance", new Vector3(HalfLocal(new Vector3(67.5f, 0f, 0f)).x, 0f, front + 1.5f), 180f);
            spawner.exitPoint = Point("Exit", new Vector3(HalfLocal(new Vector3(42.5f, 0f, 0f)).x, 0f, front + 1.5f), 0f);
            spawner.cashierPoint = Point("Till", HalfLocal(new Vector3(41.5f, 0f, -132.5f)), 0f);
            var stands = new List<Transform>();
            foreach (float x in new[] { -8f, -4f, 2f, 6f, 12f, 16f })
                foreach (float z in new[] { 0f, -5f, -10f })
                    stands.Add(Point("Browse", new Vector3(x, 0f, z), z < -9f ? 0f : 180f));
            spawner.shelfPoints = stands.ToArray();
            report.Add($"shoppers come in at the front: {stands.Count} browsing spots");
        }

        // The store's six cameras, each where it hangs in the store at half the distance, with
        // their screens on the moved security desk.
        public static StoreCameras.Spot[] CameraSpots
        {
            get
            {
                var spots = new List<StoreCameras.Spot>();
                foreach (StoreCameras.Spot s in StoreCameras.Spots)
                {
                    Vector3 probe = Half(new Vector3(s.Probe.x, 0f, s.Probe.y));
                    spots.Add(new StoreCameras.Spot(s.Name, s.Area, new Vector2(probe.x, probe.z), s.Wall, Half(s.LookAt)));
                }
                return spots.ToArray();
            }
        }

        static void Cameras(List<string> report)
        {
            Physics.SyncTransforms();
            Material outline = null;
            foreach (OutlineHighlight o in Object.FindObjectsByType<OutlineHighlight>())
                if (o != null && o.outlineMaterial != null) { outline = o.outlineMaterial; break; }
            StoreCameras cams = StoreCameras.InstallAt(CameraSpots, outline);
            report.Add(cams != null ? $"cameras: {cams.Cameras.Count}, screens {(cams.Wall != null ? "on the desk" : "MISSING")}" : "MISSING cameras");
        }

        // Lamps from the store's own model, about 5 m apart in each room indoors, a light under
        // each in a "CeilingLights" group so her light sense (LightProbe) and the breakers
        // (LightControl, by StoreFrame) count them. The backstreet has its own yard lights.
        static void Lamps(Transform root, Models m)
        {
            var lights = new GameObject("CeilingLights").transform;
            lights.SetParent(root, false);
            float height = WallHeight - 0.4f;
            int n = 0;
            foreach (var (name, store, outdoors) in Rooms)
            {
                if (outdoors) continue;
                Vector3 a = HalfLocal(new Vector3(store.xMin, 0f, store.yMin)), b = HalfLocal(new Vector3(store.xMax, 0f, store.yMax));
                int nx = Mathf.Max(1, Mathf.RoundToInt((b.x - a.x) / 5f)), nz = Mathf.Max(1, Mathf.RoundToInt((b.z - a.z) / 5f));
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        var at = new Vector3(a.x + (i + 0.5f) * (b.x - a.x) / nx, 0f, a.z + (j + 0.5f) * (b.z - a.z) / nz);
                        n++;
                        if (m.Lamp != null)
                        {
                            GameObject model = Object.Instantiate(m.Lamp, lights);
                            model.SetActive(true);
                            model.name = "Lamp " + n;
                            model.transform.localPosition = at + Vector3.up * (WallHeight - (m.CeilingHeight - m.Lamp.transform.position.y));
                            model.transform.localRotation = Quaternion.identity;
                        }
                        var go = new GameObject($"Ceiling light {n} ({name})");
                        go.transform.SetParent(lights, false);
                        go.transform.localPosition = at + Vector3.up * height;
                        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                        Light light = go.AddComponent<Light>();
                        light.type = m.LightType;
                        light.spotAngle = m.SpotAngle;
                        light.innerSpotAngle = m.InnerSpotAngle;
                        light.color = m.LightColour;
                        light.range = m.LightRange;
                        light.intensity = m.LightIntensity;
                        light.shadows = LightShadows.None;
                    }
            }
            Kehai.Karen.LightProbe.Refresh();
        }

        // Its floor's NavMesh: everything inside its walls and on its pavement, the things moved in
        // from the store included, baked the way the store's is (StoreRebake leaves people,
        // stand-ins and the doors' leaves out).
        static void Bake(GameObject root)
        {
            var surface = root.GetComponent<Unity.AI.Navigation.NavMeshSurface>() ?? root.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surface.collectObjects = Unity.AI.Navigation.CollectObjects.Volume;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            Vector3 a = HalfLocal(new Vector3(StoreBuilding.xMin, 0f, StoreBuilding.yMin)), b = HalfLocal(new Vector3(StoreBuilding.xMax, 0f, StoreBuilding.yMax));
            float z0 = a.z - 1f, z1 = b.z + 7.5f;
            surface.center = new Vector3((a.x + b.x) * 0.5f, 2f, (z0 + z1) * 0.5f);   // the root stands at Origin
            surface.size = new Vector3(b.x - a.x + 2f, 5f, z1 - z0);
            Physics.SyncTransforms();
            using (new Kehai.Karen.StoreRebake(surface)) surface.BuildNavMesh();
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

        // A thing found by its name or its path in the store, hidden ones too (a shift's layout
        // hides the store's own shelves, StoreLayoutRuntime).
        static GameObject Find(string nameOrPath)
        {
            GameObject found = SceneObjectNames.Find(nameOrPath) ?? GameObject.Find(nameOrPath);
            if (found != null) return found;
            string name = nameOrPath.Contains("/") ? nameOrPath.Substring(nameOrPath.LastIndexOf('/') + 1) : nameOrPath;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == name && t.gameObject.scene.IsValid() && (!nameOrPath.Contains("/") || PathOf(t).EndsWith(nameOrPath))) return t.gameObject;
            return null;
        }

        static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        // A copy of one of the store's fixtures, its footprint centred on `at` (mini store metres,
        // on the floor), turned `turn` degrees from the way the original stands, then nudged inside
        // the room it's in. Renamed, so nothing that looks the store's fixtures up by name finds
        // the copy instead. Made under an inactive holder, so the copy wakes only once it's ready:
        // the store's doors add their NavMesh obstacles when they wake, and a copy that already
        // carried the original's would fail to add its own.
        static GameObject Place(Transform parent, GameObject original, Vector3 at, float turn, string name, List<string> report, bool keepInside = true)
        {
            if (original == null) { report.Add($"missing model for {name}"); return null; }
            GameObject copy = Object.Instantiate(original, original.transform.position, original.transform.rotation, Holder);
            copy.name = "Mini " + name;
            copy.SetActive(true);   // the original may be one the store hid; the holder keeps it asleep
            StripRuntimeParts(copy);
            Vector3 offset = Footprint(copy).center - copy.transform.position;
            Quaternion spin = Quaternion.Euler(0f, turn, 0f);
            copy.transform.rotation = spin * original.transform.rotation;
            Vector3 world = World(at);
            Vector3 turned = spin * offset;
            copy.transform.position = new Vector3(world.x - turned.x, original.transform.position.y, world.z - turned.z);
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.isStatic = false;
            if (keepInside) KeepInside(copy);
            copy.transform.SetParent(parent, true);   // wakes here
            return copy;
        }

        // Moves a thing so its footprint stays inside the room its middle is in, clear of the walls.
        static void KeepInside(GameObject go)
        {
            Bounds b = Footprint(go);
            if (b.size == Vector3.zero) return;
            Vector3 c = Local(b.center);
            foreach (var (_, store, _) in Rooms)
            {
                Vector3 a = HalfLocal(new Vector3(store.xMin, 0f, store.yMin)), z = HalfLocal(new Vector3(store.xMax, 0f, store.yMax));
                if (c.x < a.x || c.x > z.x || c.z < a.z || c.z > z.z) continue;
                float m = Wall * 0.5f + 0.02f;
                float dx = Push(b.min.x - Origin.x, b.max.x - Origin.x, a.x + m, z.x - m);
                float dz = Push(b.min.z - Origin.z, b.max.z - Origin.z, a.z + m, z.z - m);
                go.transform.position += new Vector3(dx, 0f, dz);
                return;
            }
        }

        static float Push(float lo, float hi, float min, float max)
        {
            if (hi - lo > max - min) return (min + max) * 0.5f - (lo + hi) * 0.5f;
            if (lo < min) return min - lo;
            if (hi > max) return max - hi;
            return 0f;
        }

        static Transform holder;
        static Transform Holder
        {
            get
            {
                if (holder != null) return holder;
                var go = new GameObject("Mini store copies (asleep)");
                go.SetActive(false);
                holder = go.transform;
                return holder;
            }
        }

        // The parts the store's fixtures add to themselves in Play: a door's NavMesh obstacles.
        static void StripRuntimeParts(GameObject copy)
        {
            BackPanel(copy);
            bool door = copy.GetComponentInChildren<HingeDoor>(true) != null || copy.GetComponentInChildren<FurnitureDoor>(true) != null ||
                        copy.GetComponentInChildren<AutoDoubleDoor>(true) != null;
            if (!door) return;
            foreach (var obstacle in copy.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true)) Object.DestroyImmediate(obstacle);
            // A door's ParentMaterialController paints it with its own material when it starts: on the
            // store's doors a placeholder grid. The copy keeps the paint it was copied with.
            foreach (var paint in copy.GetComponentsInChildren<ParentMaterialController>(true)) Object.DestroyImmediate(paint);
        }

        // A shelf bay's middle panel (Wall_01) shows see-through on a copy; a solid panel of the same
        // grey in its place, so you can't see through a shelf and her eye can't either.
        static void BackPanel(GameObject copy)
        {
            if (copy.GetComponent<ShelfUnit>() == null || copy.GetComponent<ColdFixture>() != null) return;
            foreach (Renderer wall in copy.GetComponentsInChildren<Renderer>(true))
            {
                if (wall.name != "Wall_01") continue;
                Transform t = wall.transform;
                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Back panel";
                panel.transform.SetParent(t.parent, false);
                panel.transform.localPosition = t.localPosition;
                panel.transform.localRotation = t.localRotation;
                Vector3 local = t.InverseTransformVector(wall.bounds.size);
                panel.transform.localScale = Vector3.Scale(t.localScale, new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Max(Mathf.Abs(local.z), 0.05f)));
                Bounds want = wall.bounds;
                Physics.SyncTransforms();
                panel.transform.position += want.center - panel.GetComponent<Renderer>().bounds.center;
                panel.GetComponent<Renderer>().sharedMaterial = wall.sharedMaterial;
            }
        }

        public static Bounds Footprint(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // The store's originals to copy, found by their names in the scene (SCENE_MAP.md).
        sealed class Models
        {
            public GameObject TallBay, LowBay, RetailFreezer, HideChiller, ColdChiller, Lamp;
            public Material Floor, Yard, Wall, Plinth, Ceiling;
            public float CeilingHeight = 8f;
            public LightType LightType = LightType.Spot;
            public float SpotAngle = 120f, InnerSpotAngle = 60f, LightRange = 17f, LightIntensity = 40f;
            public Color LightColour = new Color(1f, 0.96f, 0.88f);

            public Models(List<string> report)
            {
                TallBay = Need("Shelf_Double_Sided_Tall_10", report);
                LowBay = Need("Shelf_Double_Sided_Low_01", report);
                RetailFreezer = Need("Retail_Freezer_02", report);
                HideChiller = Need("Cold_Hiding_Cabinet_04", report);
                ColdChiller = Need("Storage_Chiller_16", report);
                Lamp = Need("Lamp_01", report);
                Floor = MaterialOf("Original_Floor_01", report);
                Yard = MaterialOf("Backstreet_Floor_01", report);
                Ceiling = MaterialOf("Original_Roof_01", report);
                Wall = MaterialOf("Cream_Wall_Panel_02", report);
                Plinth = MaterialOf("Charcoal_Plinth_01", report);
                GameObject light = MiniStore.Find("Ceiling_Light_42_5_132_01");
                if (light != null && light.TryGetComponent(out Light l))
                {
                    LightType = l.type;
                    SpotAngle = l.spotAngle;
                    InnerSpotAngle = l.innerSpotAngle;
                    LightRange = l.range;
                    LightIntensity = l.intensity;
                    LightColour = l.color;
                }
            }

            static GameObject Need(string name, List<string> report)
            {
                GameObject go = MiniStore.Find(name);
                report.Add(go != null ? $"model {name}" : $"MISSING model {name}");
                return go;
            }

            static Material MaterialOf(string name, List<string> report)
            {
                GameObject go = MiniStore.Find(name);
                Renderer r = go != null ? go.GetComponentInChildren<Renderer>(true) : null;
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
