using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Store
{
    // What a named place in the building is for. Karen plans against these, the eval
    // harness names them in its observations, and STORE_MAP.md lists them.
    public enum LandmarkKind
    {
        CustomerSpawn,
        AutoDoor,
        Door,
        Checkout,
        CoffeeMachine,
        TimeClock,
        BreakerBox,
        Radio,
        MopHome,
        StockCrateHome,
        Bin,
        TrashSkip
    }

    // Put on anything placed in the store at runtime that should not become part of the
    // map's walls — Karen's crate stacks, for instance, are blockages, not architecture.
    public interface IMapTransient { }

    public struct Landmark
    {
        public LandmarkKind Kind;
        public string Name;
        public Vector3 Position;
        public int Cell;
        public int Region;
        public Object Source;
    }

    // A shelf bay as the map sees it: where it stands and what it sells.
    public struct Bay
    {
        public ShelfUnit Unit;
        public Vector3 Position;
        public string Section;
        public int Region;
    }

    // One edge of the region graph: the regions either side of a boundary, and how wide
    // the boundary is in walkable cell edges. Width is what min-cut treats as capacity —
    // a doorway is one or two, an open stretch of floor is a dozen.
    public struct RegionLink
    {
        public int To;
        public int Capacity;
        public Vector3 Crossing;
    }

    public sealed class Region
    {
        public int Id;
        public string Key;          // stable across runs: "tx_tz_k" or "door_i"
        public string Name;         // "Aisle 2/B", "Stockroom/C", "Door: Sales floor - Stockroom"
        public string Room;         // the walled space it sits in, as physics sees it
        public string Area;         // what that part of the building is called — "Stockroom"
        public string Section;      // planogram sign on the sales floor, empty elsewhere
        public bool IsDoor;
        public bool IsChokepoint;   // articulation point: removing it disconnects the store
        public Vector3 Centroid;
        public readonly List<int> Cells = new List<int>();
        public readonly List<RegionLink> Links = new List<RegionLink>();
    }

    // The store as a graph, built from the NavMesh.
    //
    // Two layers. Cells are a 1.5 m grid over the walkable floor, joined wherever a NavMesh
    // raycast between their centres is clear — this is what probability diffuses over and
    // what noise travels along. Regions are cells grouped into rooms (split at every door)
    // and then into 5 m tiles, each tile further split into its connected pieces so a shelf
    // down the middle of a tile makes two regions, not one. The region graph is small
    // enough to run articulation points and min-cut on, and every region has a name.
    //
    // Nothing here is authored. Move a shelf and rebuild, and the map follows.
    public sealed class StoreMap
    {
        public const float CellSize = 1.5f;
        public const float TileSize = 5f;
        const float DoorRadius = 1.3f;

        // ---- cells ------------------------------------------------------------
        public int CellCount { get; private set; }
        public Vector3[] CellPosition;
        public int[] CellRegion;
        public int[] CellRoom;
        public int[] EdgeStart;       // CSR adjacency: neighbours of c are EdgeTo[EdgeStart[c]..EdgeStart[c+1])
        public int[] EdgeTo;
        public float[] EdgeLength;

        Vector3 origin;
        int cols, rows;
        int[] gridToCell;

        // ---- regions, rooms, landmarks -----------------------------------------
        public readonly List<Region> Regions = new List<Region>();
        public readonly List<string> Rooms = new List<string>();
        public readonly List<Landmark> Landmarks = new List<Landmark>();
        public readonly List<Bay> Bays = new List<Bay>();

        public Bounds Bounds { get; private set; }
        public float BuildMilliseconds { get; private set; }

        // ---- the shared instance -------------------------------------------------

        static StoreMap current;
        static UnityEngine.SceneManagement.Scene currentScene;

        // The map for the loaded scene, built on first use and cached until the scene
        // changes. Everything that reads the store goes through here.
        public static StoreMap Current
        {
            get
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (current == null || currentScene != scene)
                {
                    current = Build();
                    currentScene = scene;
                }
                return current;
            }
        }

        // Rebuild after the maze has been changed (see Karen's shelf relocation).
        public static StoreMap Rebuild()
        {
            current = Build();
            currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return current;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current = null;
            currentScene = default;
        }

        // ======================================================================
        // Building
        // ======================================================================

        public static StoreMap Build()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var map = new StoreMap();
            map.SampleCells();
            map.CollectLandmarks();
            map.LinkCells();
            map.PruneUnreachable();
            map.CarveRooms();
            map.CarveRegions();
            map.LinkRegions();
            map.FindChokepoints();
            map.ResolveLandmarks();
            map.CollectBays();
            map.BuildMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            return map;
        }

        void SampleCells()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                Bounds = new Bounds(Vector3.zero, Vector3.zero);
                CellCount = 0;
                CellPosition = new Vector3[0];
                gridToCell = new int[0];
                return;
            }

            var b = new Bounds(tri.vertices[0], Vector3.zero);
            foreach (Vector3 v in tri.vertices) b.Encapsulate(v);
            b.Expand(new Vector3(CellSize, 0f, CellSize));
            Bounds = b;

            origin = new Vector3(b.min.x, 0f, b.min.z);
            cols = Mathf.CeilToInt(b.size.x / CellSize);
            rows = Mathf.CeilToInt(b.size.z / CellSize);
            gridToCell = new int[cols * rows];

            var positions = new List<Vector3>(cols * rows / 2);
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int g = r * cols + c;
                    gridToCell[g] = -1;

                    Vector3 centre = GridCentre(c, r, b.center.y);
                    if (!NavMesh.SamplePosition(centre, out NavMeshHit hit, CellSize, NavMesh.AllAreas)) continue;

                    // The sample must land inside this cell, or two cells would share one
                    // patch of floor and a thin wall could be stepped over.
                    if (Mathf.Abs(hit.position.x - centre.x) > CellSize * 0.5f) continue;
                    if (Mathf.Abs(hit.position.z - centre.z) > CellSize * 0.5f) continue;

                    gridToCell[g] = positions.Count;
                    positions.Add(hit.position);
                }
            }

            CellCount = positions.Count;
            CellPosition = positions.ToArray();
        }

        Vector3 GridCentre(int c, int r, float y)
        {
            return new Vector3(origin.x + (c + 0.5f) * CellSize, y, origin.z + (r + 0.5f) * CellSize);
        }

        void LinkCells()
        {
            var start = new int[CellCount + 1];
            var to = new List<int>(CellCount * 6);
            var length = new List<float>(CellCount * 6);

            // Recover each cell's grid coordinates once.
            var cellCol = new int[CellCount];
            var cellRow = new int[CellCount];
            for (int g = 0; g < gridToCell.Length; g++)
            {
                int cell = gridToCell[g];
                if (cell < 0) continue;
                cellCol[cell] = g % cols;
                cellRow[cell] = g / cols;
            }

            // Straight neighbours first; a diagonal only counts when both straights do, so
            // the graph never cuts the corner of a shelf end.
            int[] dc = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dr = { 0, 0, 1, -1, 1, -1, 1, -1 };
            var straight = new bool[4];

            for (int cell = 0; cell < CellCount; cell++)
            {
                start[cell] = to.Count;
                for (int k = 0; k < 8; k++)
                {
                    if (k >= 4)
                    {
                        bool horizontal = straight[dc[k] > 0 ? 0 : 1];
                        bool vertical = straight[dr[k] > 0 ? 2 : 3];
                        if (!horizontal || !vertical) continue;
                    }

                    int nc = cellCol[cell] + dc[k];
                    int nr = cellRow[cell] + dr[k];
                    bool linked = false;

                    if (nc >= 0 && nc < cols && nr >= 0 && nr < rows)
                    {
                        int other = gridToCell[nr * cols + nc];
                        if (other >= 0 && Walkable(CellPosition[cell], CellPosition[other]))
                        {
                            to.Add(other);
                            length.Add(Vector3.Distance(CellPosition[cell], CellPosition[other]));
                            linked = true;
                        }
                    }

                    if (k < 4) straight[k] = linked;
                }
            }
            start[CellCount] = to.Count;

            EdgeStart = start;
            EdgeTo = to.ToArray();
            EdgeLength = length.ToArray();
        }

        // Can a person walk straight from one cell centre to the next? Decided by physics
        // rather than by the NavMesh: the bake leaves the building's 3 cm walls out, so a
        // NavMesh raycast walks straight through them. Two sweeps, knee and chest height,
        // and anything that moves or opens — doors, stock, people — doesn't count as a wall.
        static readonly RaycastHit[] hits = new RaycastHit[16];
        static readonly float[] sweepHeights = { 0.45f, 1.3f };
        readonly Dictionary<Collider, bool> ignorable = new Dictionary<Collider, bool>();

        bool Walkable(Vector3 a, Vector3 b)
        {
            if (Mathf.Abs(a.y - b.y) > 0.6f) return false;

            foreach (float height in sweepHeights)
            {
                Vector3 from = a + Vector3.up * height;
                Vector3 delta = b + Vector3.up * height - from;
                float distance = delta.magnitude;
                if (distance < 0.001f) continue;

                int count = Physics.RaycastNonAlloc(from, delta / distance, hits, distance, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (!IsIgnorable(hits[i].collider)) return false;
            }
            return true;
        }

        bool IsIgnorable(Collider collider)
        {
            if (ignorable.TryGetValue(collider, out bool cached)) return cached;

            bool skip = collider is CharacterController
                     || (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic)
                     || collider.GetComponentInParent<HingeDoor>() != null
                     || collider.GetComponentInParent<AutoDoubleDoor>() != null
                     || collider.GetComponentInParent<Item>() != null
                     || collider.GetComponentInParent<NavMeshAgent>() != null
                     || collider.GetComponentInParent<IMapTransient>() != null;

            ignorable[collider] = skip;
            return skip;
        }

        // Cells nobody can walk to — pockets sealed between shelves, the far side of a wall
        // with no door — are dropped, so belief never leaks into places a person can't be.
        void PruneUnreachable()
        {
            var seeds = new List<int>();
            foreach (Landmark l in Landmarks)
            {
                int cell = NearestCell(l.Position, 4f, skipDoors: false);
                if (cell >= 0) seeds.Add(cell);
            }
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                int cell = NearestCell(player.transform.position, 4f, skipDoors: false);
                if (cell >= 0) seeds.Add(cell);
            }
            if (seeds.Count == 0) return;

            // Keep the connected piece that holds most of the landmarks.
            var component = new int[CellCount];
            for (int i = 0; i < CellCount; i++) component[i] = -1;
            var votes = new List<int>();
            var stack = new Stack<int>();
            foreach (int seed in seeds)
            {
                if (component[seed] >= 0) { votes[component[seed]]++; continue; }
                int id = votes.Count;
                votes.Add(1);
                component[seed] = id;
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    for (int e = EdgeStart[c]; e < EdgeStart[c + 1]; e++)
                    {
                        int n = EdgeTo[e];
                        if (component[n] >= 0) continue;
                        component[n] = id;
                        stack.Push(n);
                    }
                }
            }

            int keep = 0;
            for (int i = 1; i < votes.Count; i++) if (votes[i] > votes[keep]) keep = i;

            var remap = new int[CellCount];
            var positions = new List<Vector3>();
            for (int c = 0; c < CellCount; c++)
            {
                remap[c] = component[c] == keep ? positions.Count : -1;
                if (remap[c] >= 0) positions.Add(CellPosition[c]);
            }
            if (positions.Count == CellCount) return;

            var start = new int[positions.Count + 1];
            var to = new List<int>(EdgeTo.Length);
            var length = new List<float>(EdgeTo.Length);
            for (int c = 0; c < CellCount; c++)
            {
                int nc = remap[c];
                if (nc < 0) continue;
                start[nc] = to.Count;
                for (int e = EdgeStart[c]; e < EdgeStart[c + 1]; e++)
                {
                    int n = remap[EdgeTo[e]];
                    if (n < 0) continue;
                    to.Add(n);
                    length.Add(EdgeLength[e]);
                }
            }
            start[positions.Count] = to.Count;

            for (int g = 0; g < gridToCell.Length; g++)
                if (gridToCell[g] >= 0) gridToCell[g] = remap[gridToCell[g]];

            PrunedCells = CellCount - positions.Count;
            CellCount = positions.Count;
            CellPosition = positions.ToArray();
            EdgeStart = start;
            EdgeTo = to.ToArray();
            EdgeLength = length.ToArray();
        }

        public int PrunedCells { get; private set; }

        // ---- rooms ------------------------------------------------------------

        readonly List<Vector3> doorPositions = new List<Vector3>();
        readonly List<string> doorNames = new List<string>();
        readonly List<bool> doorIsAuto = new List<bool>();
        readonly List<Vector3> doorAxis = new List<Vector3>();       // along the doorway
        readonly List<float> doorHalfWidth = new List<float>();
        bool[] cellIsDoor;
        int[] cellDoor;

        void CarveRooms()
        {
            cellIsDoor = new bool[CellCount];
            cellDoor = new int[CellCount];
            for (int i = 0; i < CellCount; i++) cellDoor[i] = -1;

            for (int d = 0; d < doorPositions.Count; d++)
            {
                float reach = doorHalfWidth[d] + DoorRadius;
                foreach (int cell in CellsNear(doorPositions[d], reach + CellSize))
                {
                    Vector3 offset = CellPosition[cell] - doorPositions[d];
                    offset.y = 0f;
                    float along = Mathf.Abs(Vector3.Dot(offset, doorAxis[d]));
                    float across = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(doorAxis[d], Vector3.up)));
                    if (along > doorHalfWidth[d] + 0.6f || across > DoorRadius) continue;

                    cellIsDoor[cell] = true;
                    cellDoor[cell] = d;
                }
            }

            // Flood fill with the doorways cut out: whatever is left in one piece is a room.
            CellRoom = new int[CellCount];
            for (int i = 0; i < CellCount; i++) CellRoom[i] = -1;

            var roomSizes = new List<int>();
            var stack = new Stack<int>();
            for (int seed = 0; seed < CellCount; seed++)
            {
                if (cellIsDoor[seed] || CellRoom[seed] >= 0) continue;

                int room = roomSizes.Count;
                roomSizes.Add(0);
                CellRoom[seed] = room;
                stack.Push(seed);

                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    roomSizes[room]++;
                    for (int e = EdgeStart[cell]; e < EdgeStart[cell + 1]; e++)
                    {
                        int n = EdgeTo[e];
                        if (cellIsDoor[n] || CellRoom[n] >= 0) continue;
                        CellRoom[n] = room;
                        stack.Push(n);
                    }
                }
            }

            // Doorway cells belong to the room they open onto most; they still get their own
            // region below, so the door stays a distinct node in the graph.
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (!cellIsDoor[cell]) continue;
                CellRoom[cell] = NearestRoomTo(cell);
            }

            NameRooms(roomSizes.Count);
        }

        int NearestRoomTo(int cell)
        {
            var seen = new HashSet<int> { cell };
            var queue = new Queue<int>();
            queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                if (!cellIsDoor[c] && CellRoom[c] >= 0) return CellRoom[c];
                for (int e = EdgeStart[c]; e < EdgeStart[c + 1]; e++)
                    if (seen.Add(EdgeTo[e])) queue.Enqueue(EdgeTo[e]);
            }
            return 0;
        }

        // A room is named for the part of the building most of it lies in. Rooms come from
        // physics — whatever the walls enclose — and the names from the area table below.
        void NameRooms(int count)
        {
            var votes = new Dictionary<string, int>[count];
            for (int r = 0; r < count; r++) votes[r] = new Dictionary<string, int>();

            for (int i = 0; i < CellCount; i++)
            {
                int room = CellRoom[i];
                if (room < 0 || cellIsDoor[i]) continue;
                string area = AreaAt(CellPosition[i]);
                votes[room].TryGetValue(area, out int n);
                votes[room][area] = n + 1;
            }

            Rooms.Clear();
            for (int r = 0; r < count; r++)
            {
                string name = "Room";
                int best = -1;
                foreach (KeyValuePair<string, int> pair in votes[r])
                    if (pair.Value > best) { best = pair.Value; name = pair.Key; }

                // The street, the alleys and the yard are one open space; call it that.
                if (IsOutdoors(name)) name = "Outside";

                string unique = name;
                for (int n = 2; Rooms.Contains(unique); n++) unique = name + " " + n;
                Rooms.Add(unique);
            }
        }

        public static bool IsOutdoors(string area)
        {
            return area == "Street" || area == "West alley" || area == "East alley"
                || area == "South yard" || area == "Backstreet";
        }

        // What each part of the building is called. The physical rooms come from the walls;
        // this table only names them, the same way StoreLayout names the sales floor. First
        // match wins. World XZ; the store runs south along -Z from the street.
        public static string AreaAt(Vector3 p)
        {
            if (p.z > -110f) return "Street";
            if (p.x < 10.5f) return "West alley";
            if (p.x > 85.5f) return "East alley";
            if (p.z < -189.5f) return "South yard";
            if (p.z < -175.5f) return "Stockroom";
            if (p.x < 25f) return p.z < -150.5f ? "Backstreet" : "Staff room";
            if (p.z >= -120.5f) return "Lobby";
            return "Sales floor";
        }

        int RoomAtRaw(Vector3 position, float radius)
        {
            int cell = NearestCell(position, radius, skipDoors: true);
            return cell >= 0 ? CellRoom[cell] : -1;
        }

        // ---- regions ------------------------------------------------------------

        void CarveRegions()
        {
            Regions.Clear();
            CellRegion = new int[CellCount];
            for (int i = 0; i < CellCount; i++) CellRegion[i] = -1;

            // Every doorway is a region on its own.
            for (int d = 0; d < doorPositions.Count; d++)
            {
                var region = new Region { Id = Regions.Count, IsDoor = true, Key = "door_" + d };
                for (int cell = 0; cell < CellCount; cell++)
                    if (cellDoor[cell] == d && CellRegion[cell] < 0)
                    {
                        CellRegion[cell] = region.Id;
                        region.Cells.Add(cell);
                    }
                if (region.Cells.Count == 0) continue;
                Regions.Add(region);
            }

            // Everything else: tile, then connected piece within the tile and room.
            var stack = new Stack<int>();
            var pieceCount = new Dictionary<long, int>();
            for (int seed = 0; seed < CellCount; seed++)
            {
                if (CellRegion[seed] >= 0) continue;

                long tile = TileKey(CellPosition[seed]);
                pieceCount.TryGetValue(tile, out int piece);
                pieceCount[tile] = piece + 1;

                int tx = Mathf.FloorToInt(CellPosition[seed].x / TileSize);
                int tz = Mathf.FloorToInt(CellPosition[seed].z / TileSize);
                var region = new Region
                {
                    Id = Regions.Count,
                    Key = $"{tx}_{tz}_{piece}",
                    Room = Rooms.Count > 0 ? Rooms[CellRoom[seed]] : "Store"
                };
                Regions.Add(region);

                CellRegion[seed] = region.Id;
                stack.Push(seed);
                while (stack.Count > 0)
                {
                    int cell = stack.Pop();
                    region.Cells.Add(cell);
                    for (int e = EdgeStart[cell]; e < EdgeStart[cell + 1]; e++)
                    {
                        int n = EdgeTo[e];
                        if (CellRegion[n] >= 0) continue;
                        if (CellRoom[n] != CellRoom[seed]) continue;
                        if (TileKey(CellPosition[n]) != tile) continue;
                        CellRegion[n] = region.Id;
                        stack.Push(n);
                    }
                }
            }

            foreach (Region region in Regions)
            {
                Vector3 sum = Vector3.zero;
                foreach (int cell in region.Cells) sum += CellPosition[cell];
                region.Centroid = sum / Mathf.Max(1, region.Cells.Count);
                region.Area = region.IsDoor ? "Doorway" : AreaAt(region.Centroid);
            }

            NameRegions();
        }

        static long TileKey(Vector3 p)
        {
            long tx = Mathf.FloorToInt(p.x / TileSize);
            long tz = Mathf.FloorToInt(p.z / TileSize);
            return (tx << 32) ^ (tz & 0xffffffffL);
        }

        void NameRegions()
        {
            // Group by what the region is part of — its planogram section on the sales floor,
            // its room anywhere else — then letter them north to south, east to west, the way
            // a customer coming in through the front doors would meet them.
            var groups = new Dictionary<string, List<Region>>();
            foreach (Region region in Regions)
            {
                if (region.IsDoor) continue;

                string group;
                if (region.Area == "Sales floor")
                {
                    region.Section = StoreLayout.SignAt(region.Centroid);
                    group = ShortSign(region.Section);
                }
                else
                {
                    region.Section = string.Empty;
                    group = region.Area;
                }

                if (!groups.TryGetValue(group, out List<Region> list))
                {
                    list = new List<Region>();
                    groups[group] = list;
                }
                list.Add(region);
            }

            foreach (KeyValuePair<string, List<Region>> pair in groups)
            {
                pair.Value.Sort((a, b) =>
                {
                    int byZ = Mathf.RoundToInt(b.Centroid.z / TileSize).CompareTo(Mathf.RoundToInt(a.Centroid.z / TileSize));
                    return byZ != 0 ? byZ : b.Centroid.x.CompareTo(a.Centroid.x);
                });

                for (int i = 0; i < pair.Value.Count; i++)
                    pair.Value[i].Name = pair.Value.Count == 1 ? pair.Key : $"{pair.Key}/{Letters(i)}";
            }
        }

        // "Aisle 2 · Snacks & Crisps" → "Aisle 2"; "Back Wall · Dairy & Chilled" → "Dairy wall".
        public static string ShortSign(string sign)
        {
            if (string.IsNullOrEmpty(sign)) return "Floor";
            int dot = sign.IndexOf('·');
            string head = dot >= 0 ? sign.Substring(0, dot).Trim() : sign.Trim();
            string tail = dot >= 0 ? sign.Substring(dot + 1).Trim() : string.Empty;

            if (head.StartsWith("Aisle")) return head;
            if (head == "Back Wall")
            {
                int amp = tail.IndexOf('&');
                string first = amp >= 0 ? tail.Substring(0, amp).Trim() : tail;
                return first + " wall";
            }
            return head;
        }

        static string Letters(int index)
        {
            const string abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            return index < abc.Length ? abc[index].ToString() : abc[index % abc.Length] + (index / abc.Length + 1).ToString();
        }

        void LinkRegions()
        {
            var capacity = new Dictionary<long, int>();
            var crossing = new Dictionary<long, Vector3>();

            for (int cell = 0; cell < CellCount; cell++)
            {
                int a = CellRegion[cell];
                for (int e = EdgeStart[cell]; e < EdgeStart[cell + 1]; e++)
                {
                    int b = CellRegion[EdgeTo[e]];
                    if (a == b || a < 0 || b < 0) continue;

                    long key = ((long)a << 32) | (uint)b;
                    capacity.TryGetValue(key, out int count);
                    capacity[key] = count + 1;
                    crossing.TryGetValue(key, out Vector3 sum);
                    crossing[key] = sum + (CellPosition[cell] + CellPosition[EdgeTo[e]]) * 0.5f;
                }
            }

            foreach (KeyValuePair<long, int> pair in capacity)
            {
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffff);
                Regions[a].Links.Add(new RegionLink
                {
                    To = b,
                    Capacity = pair.Value,
                    Crossing = crossing[pair.Key] / pair.Value
                });
            }

            // Doors are named for the rooms either side of them.
            foreach (Region region in Regions)
            {
                if (!region.IsDoor) continue;
                var sides = new SortedSet<string>();
                var rooms = new SortedSet<string>();
                foreach (RegionLink link in region.Links)
                {
                    if (Regions[link.To].IsDoor) continue;
                    sides.Add(Regions[link.To].Area);
                    rooms.Add(Regions[link.To].Room);
                }
                if (sides.Count < 2 && rooms.Count >= 2) sides = rooms;
                region.Room = "Doorway";
                region.Section = string.Empty;
                string noun = region.Key.StartsWith("door_") && IsAutoDoor(region) ? "Doors" : "Door";
                region.Name = sides.Count >= 2 ? $"{noun}: {string.Join(" - ", sides)}" : $"{noun}: " + (sides.Count == 1 ? sides.Min : "?");
            }

            // The same pair of rooms can be joined by more than one door; number them.
            var seen = new Dictionary<string, int>();
            foreach (Region region in Regions)
            {
                if (!region.IsDoor) continue;
                seen.TryGetValue(region.Name, out int n);
                seen[region.Name] = n + 1;
                if (n > 0) region.Name += " (" + (n + 1) + ")";
            }
        }

        bool IsAutoDoor(Region region)
        {
            int d = int.Parse(region.Key.Substring("door_".Length));
            return d >= 0 && d < doorIsAuto.Count && doorIsAuto[d];
        }

        // Articulation points of the region graph (iterative Tarjan): regions whose loss
        // splits the store in two. Where Karen stands to cut you off.
        void FindChokepoints()
        {
            int n = Regions.Count;
            var disc = new int[n];
            var low = new int[n];
            var parent = new int[n];
            var childCount = new int[n];
            var linkIndex = new int[n];
            for (int i = 0; i < n; i++) { disc[i] = -1; parent[i] = -1; }

            int time = 0;
            var stack = new Stack<int>();
            for (int root = 0; root < n; root++)
            {
                if (disc[root] >= 0) continue;
                disc[root] = low[root] = time++;
                stack.Push(root);

                while (stack.Count > 0)
                {
                    int u = stack.Peek();
                    List<RegionLink> links = Regions[u].Links;
                    if (linkIndex[u] < links.Count)
                    {
                        int v = links[linkIndex[u]++].To;
                        if (disc[v] < 0)
                        {
                            parent[v] = u;
                            childCount[u]++;
                            disc[v] = low[v] = time++;
                            stack.Push(v);
                        }
                        else if (v != parent[u])
                        {
                            low[u] = Mathf.Min(low[u], disc[v]);
                        }
                        continue;
                    }

                    stack.Pop();
                    int p = parent[u];
                    if (p < 0) continue;
                    low[p] = Mathf.Min(low[p], low[u]);
                    if (parent[p] >= 0 && low[u] >= disc[p]) Regions[p].IsChokepoint = true;
                }

                if (childCount[root] > 1) Regions[root].IsChokepoint = true;
            }
        }

        // ---- landmarks --------------------------------------------------------

        void CollectLandmarks()
        {
            Landmarks.Clear();
            doorPositions.Clear();
            doorNames.Clear();
            doorIsAuto.Clear();
            doorAxis.Clear();
            doorHalfWidth.Clear();

            foreach (AutoDoubleDoor door in Object.FindObjectsByType<AutoDoubleDoor>())
            {
                AddDoorway(door, true);
            }

            foreach (HingeDoor door in Object.FindObjectsByType<HingeDoor>())
            {
                AddDoorway(door, false);
            }

            GameObject checkouts = GameObject.Find("Cashierdesks");
            if (checkouts != null)
            {
                int i = 1;
                foreach (Transform desk in checkouts.transform)
                    AddLandmark(LandmarkKind.Checkout, "Checkout " + i++, desk.position, desk);
            }

            GameObject spawn = GameObject.Find("CustomerSpawner");
            if (spawn != null) AddLandmark(LandmarkKind.CustomerSpawn, "Customer spawn", spawn.transform.position, spawn);

            foreach (CoffeeMachine m in Object.FindObjectsByType<CoffeeMachine>())
                AddLandmark(LandmarkKind.CoffeeMachine, "Coffee machine", m.transform.position, m);
            foreach (Puncher p in Object.FindObjectsByType<Puncher>())
                AddLandmark(LandmarkKind.TimeClock, "Time clock", p.transform.position, p);
            foreach (ElectricBox e in Object.FindObjectsByType<ElectricBox>())
                AddLandmark(LandmarkKind.BreakerBox, "Breaker box", e.transform.position, e);
            foreach (MusicBox m in Object.FindObjectsByType<MusicBox>())
                AddLandmark(LandmarkKind.Radio, "Store radio", m.transform.position, m);
            foreach (TrashContainer t in Object.FindObjectsByType<TrashContainer>())
                AddLandmark(LandmarkKind.TrashSkip, "Skip", t.transform.position, t);

            int bin = 1;
            foreach (Trashcan can in Object.FindObjectsByType<Trashcan>())
                AddLandmark(LandmarkKind.Bin, "Bin " + bin++, can.transform.position, can);

            foreach (ToolSnapPoint snap in Object.FindObjectsByType<ToolSnapPoint>())
            {
                bool isMop = snap.tool != null && snap.tool.type == ItemType.Mop;
                AddLandmark(isMop ? LandmarkKind.MopHome : LandmarkKind.StockCrateHome,
                            isMop ? "Mop rack" : "Stock pallet", snap.transform.position, snap);
            }
        }

        // A door's transform sits on its hinge, not in the middle of the opening, and a pair
        // of sliding doors is four metres wide. So the doorway is measured from the door's
        // own panels: their combined bounds give the centre of the opening and its width.
        void AddDoorway(Component door, bool automatic)
        {
            var bounds = new Bounds(door.transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider c in door.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }

            Vector3 centre = any ? new Vector3(bounds.center.x, door.transform.position.y, bounds.center.z) : door.transform.position;
            bool alongX = !any || bounds.size.x >= bounds.size.z;
            float half = any ? Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f : 1f;

            AddLandmark(automatic ? LandmarkKind.AutoDoor : LandmarkKind.Door,
                        automatic ? "Automatic doors" : "Door", centre, door);
            doorPositions.Add(centre);
            doorNames.Add(door.name);
            doorIsAuto.Add(automatic);
            doorAxis.Add(alongX ? Vector3.right : Vector3.forward);
            doorHalfWidth.Add(half);
        }

        void AddLandmark(LandmarkKind kind, string name, Vector3 position, Object source)
        {
            Landmarks.Add(new Landmark { Kind = kind, Name = name, Position = position, Source = source, Cell = -1, Region = -1 });
        }

        void ResolveLandmarks()
        {
            for (int i = 0; i < Landmarks.Count; i++)
            {
                Landmark l = Landmarks[i];
                l.Cell = NearestCell(l.Position, 4f, skipDoors: false);
                l.Region = l.Cell >= 0 ? CellRegion[l.Cell] : -1;
                Landmarks[i] = l;
            }
        }

        void CollectBays()
        {
            Bays.Clear();
            foreach (ShelfUnit unit in Object.FindObjectsByType<ShelfUnit>())
            {
                Vector3 p = unit.transform.position;
                int cell = NearestCell(p, 4f, skipDoors: true);
                Bays.Add(new Bay
                {
                    Unit = unit,
                    Position = p,
                    Section = StoreLayout.SignAt(p),
                    Region = cell >= 0 ? CellRegion[cell] : -1
                });
            }
        }

        // ======================================================================
        // Queries
        // ======================================================================

        // The cell under a point, or the nearest walkable one within `radius`.
        public int CellAt(Vector3 position, float radius = 3f) => NearestCell(position, radius, skipDoors: false);

        public int RegionAt(Vector3 position)
        {
            int cell = CellAt(position);
            return cell >= 0 ? CellRegion[cell] : -1;
        }

        public Region RegionOf(Vector3 position)
        {
            int region = RegionAt(position);
            return region >= 0 ? Regions[region] : null;
        }

        public string RegionName(int region) => region >= 0 && region < Regions.Count ? Regions[region].Name : "off the map";
        public string NameAt(Vector3 position) => RegionName(RegionAt(position));

        int NearestCell(Vector3 position, float radius, bool skipDoors)
        {
            if (CellCount == 0) return -1;

            int c = Mathf.FloorToInt((position.x - origin.x) / CellSize);
            int r = Mathf.FloorToInt((position.z - origin.z) / CellSize);
            int reach = Mathf.CeilToInt(radius / CellSize);

            int best = -1;
            float bestSqr = radius * radius;
            for (int dr = -reach; dr <= reach; dr++)
            {
                int rr = r + dr;
                if (rr < 0 || rr >= rows) continue;
                for (int dc = -reach; dc <= reach; dc++)
                {
                    int cc = c + dc;
                    if (cc < 0 || cc >= cols) continue;
                    int cell = gridToCell[rr * cols + cc];
                    if (cell < 0) continue;
                    if (skipDoors && cellIsDoor != null && cellIsDoor[cell]) continue;

                    Vector3 d = CellPosition[cell] - position;
                    d.y = 0f;
                    float sqr = d.sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = cell; }
                }
            }
            return best;
        }

        IEnumerable<int> CellsNear(Vector3 position, float radius)
        {
            int c = Mathf.FloorToInt((position.x - origin.x) / CellSize);
            int r = Mathf.FloorToInt((position.z - origin.z) / CellSize);
            int reach = Mathf.CeilToInt(radius / CellSize);
            float sqrRadius = radius * radius;

            for (int dr = -reach; dr <= reach; dr++)
            {
                int rr = r + dr;
                if (rr < 0 || rr >= rows) continue;
                for (int dc = -reach; dc <= reach; dc++)
                {
                    int cc = c + dc;
                    if (cc < 0 || cc >= cols) continue;
                    int cell = gridToCell[rr * cols + cc];
                    if (cell < 0) continue;
                    Vector3 d = CellPosition[cell] - position;
                    d.y = 0f;
                    if (d.sqrMagnitude <= sqrRadius) yield return cell;
                }
            }
        }

        // Every cell within `radius` of a point, as the crow flies. Used for sight cones.
        public void CellsWithin(Vector3 position, float radius, List<int> into)
        {
            into.Clear();
            foreach (int cell in CellsNear(position, radius)) into.Add(cell);
        }

        public Landmark? FindLandmark(LandmarkKind kind)
        {
            foreach (Landmark l in Landmarks) if (l.Kind == kind) return l;
            return null;
        }

        // Nearest landmark of a kind to a point, by walking distance when a distance field
        // from that point is supplied, straight-line otherwise.
        public Landmark? NearestLandmark(LandmarkKind kind, Vector3 from)
        {
            Landmark? best = null;
            float bestSqr = float.MaxValue;
            foreach (Landmark l in Landmarks)
            {
                if (l.Kind != kind) continue;
                float sqr = (l.Position - from).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = l; }
            }
            return best;
        }

        // A short human description of a place: "Aisle 2/B (Aisle 2 · Snacks & Crisps)".
        public string Describe(Vector3 position)
        {
            Region region = RegionOf(position);
            if (region == null) return "off the map";
            return string.IsNullOrEmpty(region.Section) ? region.Name : $"{region.Name} ({region.Section})";
        }

        // ======================================================================
        // Graph algorithms
        // ======================================================================

        // Walking distance from one cell to every other, stopping at maxDistance.
        // Unreached cells read as +infinity. `blocked` cells are treated as walls.
        public float[] Distances(int source, float maxDistance = float.MaxValue, float[] into = null, bool[] blocked = null)
        {
            if (into == null || into.Length != CellCount) into = new float[CellCount];
            for (int i = 0; i < CellCount; i++) into[i] = float.PositiveInfinity;
            if (source < 0 || source >= CellCount) return into;

            var heap = new MinHeap(64);
            into[source] = 0f;
            heap.Push(source, 0f);

            while (heap.Count > 0)
            {
                heap.Pop(out int cell, out float dist);
                if (dist > into[cell]) continue;
                if (dist > maxDistance) break;

                for (int e = EdgeStart[cell]; e < EdgeStart[cell + 1]; e++)
                {
                    int n = EdgeTo[e];
                    if (blocked != null && blocked[n]) continue;
                    float nd = dist + EdgeLength[e];
                    if (nd >= into[n]) continue;
                    into[n] = nd;
                    heap.Push(n, nd);
                }
            }
            return into;
        }

        // Same, from several sources at once — distance to the nearest of them. This is the
        // desire field: how far every cell is from the nearest unfinished job.
        public float[] DistancesFrom(IList<int> sources, float maxDistance = float.MaxValue, float[] into = null)
        {
            if (into == null || into.Length != CellCount) into = new float[CellCount];
            for (int i = 0; i < CellCount; i++) into[i] = float.PositiveInfinity;

            var heap = new MinHeap(64);
            foreach (int s in sources)
            {
                if (s < 0 || s >= CellCount) continue;
                into[s] = 0f;
                heap.Push(s, 0f);
            }

            while (heap.Count > 0)
            {
                heap.Pop(out int cell, out float dist);
                if (dist > into[cell] || dist > maxDistance) continue;
                for (int e = EdgeStart[cell]; e < EdgeStart[cell + 1]; e++)
                {
                    int n = EdgeTo[e];
                    float nd = dist + EdgeLength[e];
                    if (nd >= into[n]) continue;
                    into[n] = nd;
                    heap.Push(n, nd);
                }
            }
            return into;
        }

        // Regions reachable from `from` when the regions in `closed` cannot be entered.
        public bool[] ReachableRegions(int from, ICollection<int> closed)
        {
            var reach = new bool[Regions.Count];
            if (from < 0) return reach;
            var queue = new Queue<int>();
            reach[from] = true;
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int r = queue.Dequeue();
                foreach (RegionLink link in Regions[r].Links)
                {
                    if (reach[link.To]) continue;
                    if (closed != null && closed.Contains(link.To)) continue;
                    reach[link.To] = true;
                    queue.Enqueue(link.To);
                }
            }
            return reach;
        }

        // Minimum cut on the region graph between a source region and a set of sinks, with
        // link widths as capacities (Edmonds-Karp). Returns the links to close to separate
        // them, cheapest total width first — Karen's shopping list for a funnel.
        public List<(int from, int to, Vector3 at)> MinCut(int source, ICollection<int> sinks, ICollection<int> closed = null)
        {
            var result = new List<(int, int, Vector3)>();
            int n = Regions.Count;
            if (source < 0 || sinks == null || sinks.Count == 0 || sinks.Contains(source)) return result;

            int sink = n;        // a super-sink joined to every sink region
            int size = n + 1;
            var residual = new Dictionary<long, int>();
            var adjacency = new List<int>[size];
            for (int i = 0; i < size; i++) adjacency[i] = new List<int>();

            void AddArc(int a, int b, int cap)
            {
                long key = ((long)a << 32) | (uint)b;
                if (!residual.ContainsKey(key)) { adjacency[a].Add(b); residual[key] = 0; }
                long back = ((long)b << 32) | (uint)a;
                if (!residual.ContainsKey(back)) { adjacency[b].Add(a); residual[back] = 0; }
                residual[key] += cap;
            }

            for (int a = 0; a < n; a++)
            {
                if (closed != null && closed.Contains(a)) continue;
                foreach (RegionLink link in Regions[a].Links)
                {
                    if (closed != null && closed.Contains(link.To)) continue;
                    AddArc(a, link.To, link.Capacity);
                }
            }
            foreach (int s in sinks) AddArc(s, sink, 1 << 20);

            var parent = new int[size];
            while (true)
            {
                for (int i = 0; i < size; i++) parent[i] = -1;
                parent[source] = source;
                var queue = new Queue<int>();
                queue.Enqueue(source);
                while (queue.Count > 0 && parent[sink] < 0)
                {
                    int u = queue.Dequeue();
                    foreach (int v in adjacency[u])
                    {
                        if (parent[v] >= 0) continue;
                        if (residual[((long)u << 32) | (uint)v] <= 0) continue;
                        parent[v] = u;
                        queue.Enqueue(v);
                    }
                }
                if (parent[sink] < 0) break;

                int flow = int.MaxValue;
                for (int v = sink; v != source; v = parent[v])
                    flow = Mathf.Min(flow, residual[((long)parent[v] << 32) | (uint)v]);
                for (int v = sink; v != source; v = parent[v])
                {
                    residual[((long)parent[v] << 32) | (uint)v] -= flow;
                    residual[((long)v << 32) | (uint)parent[v]] += flow;
                }
            }

            // Source side of the cut = everything still reachable in the residual graph.
            var sourceSide = new bool[size];
            var bfs = new Queue<int>();
            sourceSide[source] = true;
            bfs.Enqueue(source);
            while (bfs.Count > 0)
            {
                int u = bfs.Dequeue();
                foreach (int v in adjacency[u])
                {
                    if (sourceSide[v]) continue;
                    if (residual[((long)u << 32) | (uint)v] <= 0) continue;
                    sourceSide[v] = true;
                    bfs.Enqueue(v);
                }
            }

            for (int a = 0; a < n; a++)
            {
                if (!sourceSide[a]) continue;
                foreach (RegionLink link in Regions[a].Links)
                    if (!sourceSide[link.To] && (closed == null || !closed.Contains(link.To)))
                        result.Add((a, link.To, link.Crossing));
            }
            return result;
        }

        // A small binary heap keyed on float, allocation-light for Dijkstra.
        sealed class MinHeap
        {
            int[] items;
            float[] keys;
            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                items = new int[capacity];
                keys = new float[capacity];
            }

            public void Push(int item, float key)
            {
                if (Count == items.Length)
                {
                    System.Array.Resize(ref items, Count * 2);
                    System.Array.Resize(ref keys, Count * 2);
                }
                int i = Count++;
                items[i] = item;
                keys[i] = key;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public void Pop(out int item, out float key)
            {
                item = items[0];
                key = keys[0];
                Count--;
                items[0] = items[Count];
                keys[0] = keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < Count && keys[l] < keys[m]) m = l;
                    if (r < Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
            }

            void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (keys[a], keys[b]) = (keys[b], keys[a]);
            }
        }

        // ======================================================================
        // The readable version
        // ======================================================================

        // Grid coordinates for a cell — for renderers such as the debug heatmap.
        public void CellGrid(int cell, out int column, out int row)
        {
            Vector3 p = CellPosition[cell];
            column = Mathf.FloorToInt((p.x - origin.x) / CellSize);
            row = Mathf.FloorToInt((p.z - origin.z) / CellSize);
        }

        public int Columns => cols;
        public int Rows => rows;
        public Vector3 Origin => origin;

        public string Summary()
        {
            int chokepoints = 0, links = 0;
            foreach (Region r in Regions)
            {
                if (r.IsChokepoint) chokepoints++;
                links += r.Links.Count;
            }
            return $"{CellCount} cells, {Regions.Count} regions in {Rooms.Count} rooms, {links / 2} links, " +
                   $"{chokepoints} chokepoints, {Landmarks.Count} landmarks, {Bays.Count} bays ({BuildMilliseconds:F0} ms)";
        }

        // The floor plan as text: one character per cell, north up. What STORE_MAP.md is
        // generated from, and what an agent in the eval harness can be handed as its map.
        public string ToAscii(bool withLegendMarks = true)
        {
            var glyph = new char[cols * rows];
            for (int i = 0; i < glyph.Length; i++) glyph[i] = ' ';

            for (int cell = 0; cell < CellCount; cell++)
            {
                CellGrid(cell, out int c, out int r);
                Region region = Regions[CellRegion[cell]];
                glyph[r * cols + c] = region.IsDoor ? '+' : RoomGlyph(region);
            }

            if (withLegendMarks)
            {
                foreach (Landmark l in Landmarks)
                {
                    if (l.Cell < 0) continue;
                    CellGrid(l.Cell, out int c, out int r);
                    char mark = LandmarkGlyph(l.Kind);
                    if (mark != '\0') glyph[r * cols + c] = mark;
                }
            }

            var sb = new StringBuilder();
            sb.Append("       ");
            for (int c = 0; c < cols; c++)
            {
                float x = origin.x + (c + 0.5f) * CellSize;
                sb.Append(Mathf.Abs(x % 10f) < CellSize * 0.5f || Mathf.Abs(x % 10f) > 10f - CellSize * 0.5f ? '|' : ' ');
            }
            sb.AppendLine();

            for (int r = rows - 1; r >= 0; r--)
            {
                float z = origin.z + (r + 0.5f) * CellSize;
                sb.Append(z.ToString("F0").PadLeft(6)).Append(' ');
                for (int c = 0; c < cols; c++) sb.Append(glyph[r * cols + c]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        char RoomGlyph(Region region)
        {
            if (region.Area == "Sales floor")
                return SectionGlyph(StoreLayout.SectionAt(region.Centroid));

            switch (region.Area)
            {
                case "Street": return '~';
                case "West alley":
                case "East alley":
                case "South yard": return '_';
                case "Lobby": return 'l';
                case "Stockroom": return 's';
                case "Staff room": return 'r';
                case "Backstreet": return 'b';
                default: return ':';
            }
        }

        public static char SectionGlyph(ItemType section)
        {
            switch (section)
            {
                case ItemType.Produce: return 'P';
                case ItemType.Bakery: return 'B';
                case ItemType.Confectionery: return 'W';
                case ItemType.SoftDrinks: return 'D';
                case ItemType.Snacks: return 'S';
                case ItemType.Canned: return 'T';
                case ItemType.Cereal: return 'C';
                case ItemType.Noodles: return 'N';
                case ItemType.PersonalCare: return 'H';
                case ItemType.Household: return 'K';
                case ItemType.Dairy: return 'M';
                case ItemType.Frozen: return 'F';
                case ItemType.PetFood: return 'A';
                default: return '.';
            }
        }

        static char LandmarkGlyph(LandmarkKind kind)
        {
            switch (kind)
            {
                case LandmarkKind.Checkout: return '$';
                case LandmarkKind.CoffeeMachine: return 'c';
                case LandmarkKind.TimeClock: return 't';
                case LandmarkKind.BreakerBox: return 'e';
                case LandmarkKind.Radio: return 'm';
                case LandmarkKind.MopHome: return 'o';
                case LandmarkKind.StockCrateHome: return 'x';
                case LandmarkKind.Bin: return 'u';
                case LandmarkKind.TrashSkip: return 'k';
                case LandmarkKind.CustomerSpawn: return '@';
                default: return '\0';
            }
        }
    }
}
