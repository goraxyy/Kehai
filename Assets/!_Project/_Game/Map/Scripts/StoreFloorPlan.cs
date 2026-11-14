using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Kehai.Store
{
    // A drawing of the store as it really is, for people to look at: the walkable floor
    // (straight from the NavMesh), every wall, shelf, counter and door (from their
    // colliders, at their true size and angle), the rooms' names and the landmarks.
    //
    // StoreMap is the store as Karen reasons about it — 1.5 m cells and a graph. This is the
    // store as it looks from above, for the F1 map, the replay and the shift report.
    // Coordinates are world X (right) and world Z (up the page: north).
    //
    // 0.4.1: the drawing is of the building. It used to be fitted round every walkable
    // triangle, so the street and the parking lot took the top half of the page and the store
    // was small under them. Now it's fitted round the rooms (Zones), and each room and each
    // aisle of the sales floor has its own colour and its own label.
    public sealed class StoreFloorPlan
    {
        // A place with a name: a room, or one aisle of the sales floor. Its floor is painted its
        // own colour and its name is written on it.
        public sealed class Zone
        {
            public string Name;         // "Stockroom", "Aisle 7"
            public string Detail = "";  // what the aisle sells: "Noodles, Pasta & Rice"
            public bool Aisle;
            public Color Colour;
            public Vector2 LabelAt;
            public readonly List<Vector2> Cells = new List<Vector2>();   // the middles of its 1.5 m cells
        }

        public readonly List<Zone> Zones = new List<Zone>();

        public enum ShapeKind { Wall, Shelf, Fixture, Door, AutoDoor }

        public sealed class Shape
        {
            public ShapeKind Kind;
            public Vector2[] Corners;   // four, in order round the footprint
            public int Bay = -1;        // StoreMap bay index, for shelves
            public string Section = "";
            public string Name = "";
        }

        public sealed class Label
        {
            public string Text;
            public Vector2 At;
            public bool Outdoors;
        }

        public sealed class Pin
        {
            public LandmarkKind Kind;
            public string Name;
            public Vector2 At;
        }

        public Rect Bounds;                                  // x = world X, y = world Z
        public readonly List<Vector2[]> Floor = new List<Vector2[]>();       // triangles
        public readonly List<bool> FloorOutdoors = new List<bool>();
        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly List<Label> Rooms = new List<Label>();
        public readonly List<Pin> Pins = new List<Pin>();
        public readonly List<string> Sections = new List<string>();

        // ---- the shared instance -------------------------------------------------

        static StoreFloorPlan current;
        static Scene currentScene;

        public static StoreFloorPlan Current
        {
            get
            {
                Scene scene = SceneManager.GetActiveScene();
                if (current == null || currentScene != scene)
                {
                    current = Build(StoreMap.Current);
                    currentScene = scene;
                }
                return current;
            }
        }

        public static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        // ---- building it -----------------------------------------------------------

        public static StoreFloorPlan Build(StoreMap map)
        {
            var plan = new StoreFloorPlan();
            plan.AddFloor();
            plan.AddShelves(map);
            plan.AddWallsAndFixtures();
            plan.AddDoors();
            plan.AddRooms(map);
            plan.AddZones(map);
            foreach (Landmark l in map.Landmarks)
                plan.Pins.Add(new Pin { Kind = l.Kind, Name = l.Name, At = Flat(l.Position) });
            plan.FitBounds();
            return plan;
        }

        void AddFloor()
        {
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                Vector3 a = tri.vertices[tri.indices[i]], b = tri.vertices[tri.indices[i + 1]], c = tri.vertices[tri.indices[i + 2]];
                // The store's floor only: skip roofs and shelf tops the bake may have walked onto.
                if (Mathf.Max(a.y, b.y, c.y) > 1.2f) continue;
                Floor.Add(new[] { Flat(a), Flat(b), Flat(c) });
                FloorOutdoors.Add(StoreMap.IsOutdoors(StoreMap.AreaAt((a + b + c) / 3f)));
            }
        }

        void AddShelves(StoreMap map)
        {
            for (int i = 0; i < map.Bays.Count; i++)
            {
                Bay bay = map.Bays[i];
                if (bay.Unit == null) continue;
                Vector2[] corners = Footprint(bay.Unit.transform, bay.Unit.GetComponentsInChildren<Collider>());
                if (corners == null) continue;
                Shapes.Add(new Shape { Kind = ShapeKind.Shelf, Corners = corners, Bay = i, Section = bay.Section ?? "", Name = bay.Unit.name });
                if (!string.IsNullOrEmpty(bay.Section) && !Sections.Contains(bay.Section)) Sections.Add(bay.Section);
            }
        }

        void AddWallsAndFixtures()
        {
            foreach (Collider c in Object.FindObjectsByType<Collider>())
            {
                if (c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c is CharacterController) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (c.GetComponentInParent<ShelfUnit>() != null || c.GetComponentInParent<HingeDoor>() != null ||
                    c.GetComponentInParent<AutoDoubleDoor>() != null || c.GetComponentInParent<Item>() != null ||
                    c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<IMapTransient>() != null) continue;

                Bounds b = c.bounds;
                if (b.size.y < 0.5f || b.min.y > 1.8f) continue;                 // flat things and things overhead
                float footprint = b.size.x * b.size.z;
                if (footprint > 400f) continue;                                    // floors, roofs, the ground

                Vector2[] corners = c is BoxCollider box ? BoxFootprint(box) : AxisBox(b);
                float thin = Mathf.Min(b.size.x, b.size.z);
                bool wall = b.size.y >= 1.5f && (thin <= 0.6f || c is BoxCollider bx && Mathf.Min(bx.size.x * Mathf.Abs(bx.transform.lossyScale.x), bx.size.z * Mathf.Abs(bx.transform.lossyScale.z)) <= 0.6f);
                if (!wall && footprint < 0.15f) continue;                          // small clutter
                Shapes.Add(new Shape { Kind = wall ? ShapeKind.Wall : ShapeKind.Fixture, Corners = corners, Name = c.name });
            }
        }

        void AddDoors()
        {
            foreach (HingeDoor door in Object.FindObjectsByType<HingeDoor>())
            {
                BoxCollider panel = door.GetComponentsInChildren<BoxCollider>().Where(x => !x.isTrigger)
                    .OrderByDescending(x => x.bounds.size.sqrMagnitude).FirstOrDefault();
                if (panel == null) continue;
                Shapes.Add(new Shape { Kind = ShapeKind.Door, Corners = BoxFootprint(panel), Name = door.name });
            }
            foreach (AutoDoubleDoor door in Object.FindObjectsByType<AutoDoubleDoor>())
                foreach (BoxCollider panel in door.GetComponentsInChildren<BoxCollider>().Where(x => !x.isTrigger))
                    Shapes.Add(new Shape { Kind = ShapeKind.AutoDoor, Corners = BoxFootprint(panel), Name = door.name });
        }

        void AddRooms(StoreMap map)
        {
            // One label per area (Sales floor, Stockroom, Street...), at the cell nearest the
            // middle of that area, so an L-shaped room doesn't get its name in a wall.
            var byArea = new Dictionary<string, List<Vector3>>();
            foreach (Region r in map.Regions)
            {
                if (r.IsDoor || string.IsNullOrEmpty(r.Area)) continue;
                if (!byArea.TryGetValue(r.Area, out var list)) byArea[r.Area] = list = new List<Vector3>();
                foreach (int cell in r.Cells) list.Add(map.CellPosition[cell]);
            }
            foreach (var pair in byArea)
            {
                Vector3 mean = Vector3.zero;
                foreach (Vector3 p in pair.Value) mean += p;
                mean /= pair.Value.Count;
                Vector3 best = pair.Value.OrderBy(p => (p - mean).sqrMagnitude).First();
                Rooms.Add(new Label { Text = pair.Key, At = Flat(best), Outdoors = StoreMap.IsOutdoors(pair.Key) });
            }
        }

        // The rooms, and the sales floor aisle by aisle, from Karen's map: every region that
        // isn't a doorway and is part of the building (StoreMap.KarenWorksIn: indoors, the
        // backstreet, the pavement out front; not the street, the parking lot or the alleys).
        void AddZones(StoreMap map)
        {
            var byName = new Dictionary<string, Zone>();
            foreach (Region r in map.Regions)
            {
                if (r.IsDoor || string.IsNullOrEmpty(r.Area) || !StoreMap.KarenWorksIn(r.Area)) continue;
                bool aisle = r.Area == "Sales floor";
                string name = aisle ? StoreMap.ShortSign(r.Section) : r.Area;
                if (!byName.TryGetValue(name, out Zone zone))
                {
                    zone = new Zone { Name = name, Aisle = aisle, Colour = ZoneColour(name), Detail = aisle ? SignDetail(r.Section) : "" };
                    byName[name] = zone;
                    Zones.Add(zone);
                }
                foreach (int cell in r.Cells) zone.Cells.Add(Flat(map.CellPosition[cell]));
            }
            foreach (Zone zone in Zones)
            {
                if (zone.Cells.Count == 0) continue;
                Vector2 mean = Vector2.zero;
                foreach (Vector2 c in zone.Cells) mean += c;
                mean /= zone.Cells.Count;
                zone.LabelAt = zone.Cells.OrderBy(c => (c - mean).sqrMagnitude).First();
            }
            Zones.RemoveAll(z => z.Cells.Count == 0);
        }

        // "Aisle 7 · Noodles, Pasta & Rice" → "Noodles, Pasta & Rice".
        public static string SignDetail(string sign)
        {
            if (string.IsNullOrEmpty(sign)) return "";
            int dot = sign.IndexOf('·');
            return dot >= 0 ? sign.Substring(dot + 1).Trim() : "";
        }

        // How far past the building the drawing goes, in metres.
        public const float Margin = 3f;

        void FitBounds()
        {
            bool any = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            void Grow(Vector2 p)
            {
                if (!any) { minX = maxX = p.x; minY = maxY = p.y; any = true; return; }
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
            }
            // The building first (0.4.1). A level with no named rooms falls back to all its floor.
            foreach (Zone z in Zones) foreach (Vector2 p in z.Cells) Grow(p);
            if (!any) foreach (Vector2[] t in Floor) foreach (Vector2 p in t) Grow(p);
            if (!any) foreach (Shape s in Shapes) foreach (Vector2 p in s.Corners) Grow(p);
            Bounds = Rect.MinMaxRect(minX - Margin, minY - Margin, maxX + Margin, maxY + Margin);

            // Anything wholly outside the walkable area (scenery across the road) isn't drawn.
            Rect keep = Bounds;
            Shapes.RemoveAll(s => s.Corners.All(p => !keep.Contains(p)));
        }

        // ---- footprints ------------------------------------------------------------

        // The four corners of a box collider's footprint, at its true angle.
        static Vector2[] BoxFootprint(BoxCollider box)
        {
            Transform t = box.transform;
            Vector3 c = box.center, h = box.size * 0.5f;
            return new[]
            {
                Flat(t.TransformPoint(c + new Vector3(-h.x, 0f, -h.z))),
                Flat(t.TransformPoint(c + new Vector3(h.x, 0f, -h.z))),
                Flat(t.TransformPoint(c + new Vector3(h.x, 0f, h.z))),
                Flat(t.TransformPoint(c + new Vector3(-h.x, 0f, h.z))),
            };
        }

        static Vector2[] AxisBox(Bounds b) => new[]
        {
            new Vector2(b.min.x, b.min.z), new Vector2(b.max.x, b.min.z), new Vector2(b.max.x, b.max.z), new Vector2(b.min.x, b.max.z)
        };

        // A shelf unit's footprint in its own frame (so a rotated bay stays a thin rectangle).
        static Vector2[] Footprint(Transform frame, Collider[] colliders)
        {
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            foreach (Collider c in colliders)
            {
                if (c.isTrigger) continue;
                Bounds wb = c.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 local = frame.InverseTransformPoint(corner);
                    if (!any) { min = max = local; any = true; }
                    else { min = Vector3.Min(min, local); max = Vector3.Max(max, local); }
                }
            }
            if (!any) return null;
            // World AABBs of rotated parts overshoot a little; pull the footprint in by 5%.
            Vector3 centre = (min + max) * 0.5f, half = (max - min) * 0.475f;
            return new[]
            {
                Flat(frame.TransformPoint(centre + new Vector3(-half.x, 0f, -half.z))),
                Flat(frame.TransformPoint(centre + new Vector3(half.x, 0f, -half.z))),
                Flat(frame.TransformPoint(centre + new Vector3(half.x, 0f, half.z))),
                Flat(frame.TransformPoint(centre + new Vector3(-half.x, 0f, half.z))),
            };
        }

        // ---- colours shared by the in-game map and the report ---------------------------

        // A shelf's colour: its aisle's, brighter, so the shelves stand out on their own floor.
        public static Color SectionColour(string section)
        {
            if (string.IsNullOrEmpty(section)) return new Color(0.55f, 0.52f, 0.47f);
            Color.RGBToHSV(ZoneColour(StoreMap.ShortSign(section)), out float hue, out _, out _);
            return Color.HSVToRGB(hue, 0.4f, 0.78f);
        }

        // A zone's floor colour: soft, and different from its neighbours'. The rooms have their
        // own; the aisles step round the colour wheel by the golden ratio, in aisle order, so
        // aisles side by side never get near-identical colours.
        public static Color ZoneColour(string name)
        {
            switch (name)
            {
                case "Lobby": return Color.HSVToRGB(0.11f, 0.30f, 0.50f);
                case "Staff room": return Color.HSVToRGB(0.44f, 0.42f, 0.40f);
                case "Office": return Color.HSVToRGB(0.74f, 0.26f, 0.50f);
                case "WC": return Color.HSVToRGB(0.57f, 0.30f, 0.50f);
                case "Stockroom": return Color.HSVToRGB(0.05f, 0.36f, 0.48f);
                case "Cold storage": return Color.HSVToRGB(0.53f, 0.22f, 0.60f);
                case "Backstreet": return Color.HSVToRGB(0.36f, 0.16f, 0.36f);
                case "Pavement": return Color.HSVToRGB(0.25f, 0.08f, 0.34f);
            }
            int n = 0;
            bool numbered = false;
            foreach (char ch in name ?? "")
                if (char.IsDigit(ch)) { n = n * 10 + (ch - '0'); numbered = true; }
            if (!numbered) unchecked { foreach (char ch in name ?? "") n = n * 31 + ch; n = Mathf.Abs(n % 97); }
            float hue = Mathf.Repeat(0.02f + n * 0.61803f, 1f);
            return Color.HSVToRGB(hue, 0.34f, n % 2 == 0 ? 0.46f : 0.54f);
        }

        // ---- JSON, for the shift report ------------------------------------------------

        public string ToJson()
        {
            var sb = new StringBuilder(64 * 1024);
            sb.Append("{\"bounds\":[").Append(N(Bounds.xMin)).Append(',').Append(N(Bounds.yMin)).Append(',')
              .Append(N(Bounds.xMax)).Append(',').Append(N(Bounds.yMax)).Append("],\"floor\":[");
            for (int i = 0; i < Floor.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Vector2[] t = Floor[i];
                sb.Append('[').Append(N(t[0].x)).Append(',').Append(N(t[0].y)).Append(',').Append(N(t[1].x)).Append(',').Append(N(t[1].y))
                  .Append(',').Append(N(t[2].x)).Append(',').Append(N(t[2].y)).Append(',').Append(FloorOutdoors[i] ? 1 : 0).Append(']');
            }
            sb.Append("],\"shapes\":[");
            for (int i = 0; i < Shapes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Shape s = Shapes[i];
                sb.Append("{\"k\":\"").Append(s.Kind).Append("\",\"b\":").Append(s.Bay).Append(",\"s\":\"").Append(MiniJson.EscapeInner(s.Section)).Append("\",\"c\":[");
                for (int j = 0; j < s.Corners.Length; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(N(s.Corners[j].x)).Append(',').Append(N(s.Corners[j].y));
                }
                sb.Append("]}");
            }
            sb.Append("],\"rooms\":[");
            for (int i = 0; i < Rooms.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"t\":\"").Append(MiniJson.EscapeInner(Rooms[i].Text)).Append("\",\"x\":").Append(N(Rooms[i].At.x)).Append(",\"y\":").Append(N(Rooms[i].At.y)).Append('}');
            }
            sb.Append("],\"pins\":[");
            for (int i = 0; i < Pins.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"k\":\"").Append(Pins[i].Kind).Append("\",\"n\":\"").Append(MiniJson.EscapeInner(Pins[i].Name)).Append("\",\"x\":")
                  .Append(N(Pins[i].At.x)).Append(",\"y\":").Append(N(Pins[i].At.y)).Append('}');
            }
            sb.Append("],\"sections\":{");
            for (int i = 0; i < Sections.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(MiniJson.EscapeInner(Sections[i])).Append("\":\"#").Append(ColorUtility.ToHtmlStringRGB(SectionColour(Sections[i]))).Append('"');
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public static string N(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "0" : (Mathf.Round(v * 100f) / 100f).ToString(CultureInfo.InvariantCulture);
    }
}
