using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Kehai.Store
{
    // The store map in forms other things can read.
    //
    //   ToMarkdown — STORE_MAP.md: the floor plan, rooms, doors, landmarks, chokepoints and
    //                every region, for a person or a language model to read.
    //   ToJson     — the same graph as data, served to agents by the eval harness.
    //
    // Karen reads the StoreMap object itself; these are views of it, generated rather than
    // written, so they can never disagree with what the game is actually doing.
    public static class StoreMapReport
    {
        static string F(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        static string P(Vector3 p) => $"{F(p.x)}, {F(p.z)}";

        public static string ToMarkdown(StoreMap map)
        {
            var sb = new StringBuilder(64 * 1024);

            sb.AppendLine("# Kehai — Store Map");
            sb.AppendLine();
            sb.AppendLine("The building as " + GameNames.Antagonist + " and the eval harness see it. **Generated** from the scene by");
            sb.AppendLine("`Kehai/Map/Export STORE_MAP.md` (`StoreMap.cs` + `StoreMapReport.cs`) — do not edit by");
            sb.AppendLine("hand; move a shelf and re-export instead.");
            sb.AppendLine();
            sb.AppendLine($"**{map.Summary()}**");
            sb.AppendLine();
            sb.AppendLine("World coordinates are metres. The street is north (high Z); the store runs south");
            sb.AppendLine("along −Z to the stockroom. East is +X.");
            sb.AppendLine();

            // ---- how it's built
            sb.AppendLine("## How the map is built");
            sb.AppendLine();
            sb.AppendLine($"- **Cells** — a {F(StoreMap.CellSize)} m grid over the walkable NavMesh. Two cells are joined when a");
            sb.AppendLine("  person could walk straight between them: two physics sweeps (knee and chest height)");
            sb.AppendLine("  that stop at walls and shelving but pass doors, stock and people. Cells nobody can walk");
            sb.AppendLine("  to are dropped.");
            sb.AppendLine("- **Rooms** — what the walls enclose once every doorway is cut out; named from the area table");
            sb.AppendLine("  in `StoreMap.AreaAt`.");
            sb.AppendLine($"- **Regions** — each room divided into {F(StoreMap.TileSize)} m tiles, each tile split into its connected");
            sb.AppendLine("  pieces. Every doorway is a region of its own. Sales-floor regions are named for their");
            sb.AppendLine("  planogram section (`StoreLayout`), lettered north→south, east→west: `Aisle 2/B`.");
            sb.AppendLine("- **Links** — region neighbours, weighted by how wide the boundary is. Min-cut uses the width.");
            sb.AppendLine("- **Chokepoints** — articulation points: regions whose loss splits the store in two.");
            sb.AppendLine();

            // ---- floor plan
            sb.AppendLine("## Floor plan");
            sb.AppendLine();
            sb.AppendLine($"One character per {F(StoreMap.CellSize)} m cell, north up. The ruler marks every 10 m of X; row labels are Z.");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.Append(map.ToAscii());
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("| glyph | meaning | glyph | meaning |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine("| `~` | street | `_` | alleys and yard (outside) |");
            sb.AppendLine("| `l` | lobby | `r` | staff rooms |");
            sb.AppendLine("| `b` | backstreet | `s` | stockroom |");
            sb.AppendLine("| `+` | doorway | ` ` | wall, shelving, or unreachable |");
            sb.AppendLine("| `P` | Produce | `B` | Bakery |");
            sb.AppendLine("| `W` | Checkout · Sweets | `D` | Aisle 1 · Soft Drinks |");
            sb.AppendLine("| `S` | Aisle 2 · Snacks | `T` | Aisle 3 · Tins & Jars |");
            sb.AppendLine("| `C` | Aisle 4 · Cereal | `N` | Aisle 5 · Noodles |");
            sb.AppendLine("| `H` | Aisle 6 · Health & Beauty | `K` | Aisle 7 · Household |");
            sb.AppendLine("| `M` | Dairy wall | `F` | Frozen wall |");
            sb.AppendLine("| `A` | Pet wall | | |");
            sb.AppendLine("| `$` | checkout till | `c` | coffee machine |");
            sb.AppendLine("| `t` | time clock | `e` | breaker box |");
            sb.AppendLine("| `m` | store radio | `o` | mop rack |");
            sb.AppendLine("| `x` | stock pallet | `u` | bin |");
            sb.AppendLine("| `k` | skip | `@` | customer spawn |");
            sb.AppendLine();

            // ---- rooms
            sb.AppendLine("## Rooms");
            sb.AppendLine();
            sb.AppendLine("| room | regions | cells | area (m²) | doors |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (string room in map.Rooms)
            {
                var regions = map.Regions.Where(r => r.Room == room).ToList();
                int cells = regions.Sum(r => r.Cells.Count);
                var doors = new SortedSet<string>();
                foreach (Region r in regions)
                    foreach (RegionLink link in r.Links)
                        if (map.Regions[link.To].IsDoor) doors.Add(map.Regions[link.To].Name);
                sb.AppendLine($"| {room} | {regions.Count} | {cells} | {F(cells * StoreMap.CellSize * StoreMap.CellSize)} | {string.Join("; ", doors)} |");
            }
            sb.AppendLine();

            // ---- doors
            sb.AppendLine("## Doorways");
            sb.AppendLine();
            sb.AppendLine("| doorway | at (x, z) | width (links) | chokepoint |");
            sb.AppendLine("|---|---|---|---|");
            foreach (Region door in map.Regions.Where(r => r.IsDoor))
            {
                int width = door.Links.Sum(l => l.Capacity);
                sb.AppendLine($"| {door.Name} | {P(door.Centroid)} | {width} | {(door.IsChokepoint ? "**yes**" : "no")} |");
            }
            sb.AppendLine();

            // ---- landmarks
            sb.AppendLine("## Landmarks");
            sb.AppendLine();
            sb.AppendLine("| landmark | kind | at (x, z) | region |");
            sb.AppendLine("|---|---|---|---|");
            foreach (Landmark l in map.Landmarks.Where(l => l.Kind != LandmarkKind.Door && l.Kind != LandmarkKind.AutoDoor)
                                                  .OrderBy(l => l.Kind.ToString()).ThenBy(l => l.Name))
                sb.AppendLine($"| {l.Name} | {l.Kind} | {P(l.Position)} | {map.RegionName(l.Region)} |");
            sb.AppendLine();

            // ---- sections
            sb.AppendLine("## Planogram sections");
            sb.AppendLine();
            sb.AppendLine("Which regions each section of the sales floor covers — where to send a customer asking");
            sb.AppendLine("for something. `STORE_CATALOG.md` lists what each section sells.");
            sb.AppendLine();
            sb.AppendLine("| section | bays | regions |");
            sb.AppendLine("|---|---|---|");
            foreach (StoreLayout.Zone zone in StoreLayout.Zones)
            {
                var regions = map.Regions.Where(r => r.Section == zone.Sign).Select(r => r.Name).ToList();
                int bays = map.Bays.Count(b => b.Section == zone.Sign);
                sb.AppendLine($"| {zone.Sign} | {bays} | {string.Join(", ", regions)} |");
            }
            sb.AppendLine();

            // ---- chokepoints
            sb.AppendLine("## Chokepoints");
            sb.AppendLine();
            sb.AppendLine("Regions whose loss cuts part of the store off. " + GameNames.Antagonist + " blocks these first; a player who");
            sb.AppendLine("knows them knows where not to be cornered.");
            sb.AppendLine();
            foreach (Region r in map.Regions.Where(r => r.IsChokepoint))
            {
                var cutOff = CutOffBy(map, r.Id);
                sb.AppendLine($"- **{r.Name}** ({P(r.Centroid)}) — cuts off {cutOff} cells");
            }
            sb.AppendLine();

            // ---- every region
            sb.AppendLine("## Region index");
            sb.AppendLine();
            sb.AppendLine("<details><summary>All regions with their neighbours (click to expand)</summary>");
            sb.AppendLine();
            sb.AppendLine("| region | room | section | centre (x, z) | cells | neighbours (width) |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (Region r in map.Regions.OrderBy(r => r.Room).ThenBy(r => r.Name))
            {
                string links = string.Join(", ", r.Links.Select(l => $"{map.Regions[l.To].Name} ({l.Capacity})"));
                sb.AppendLine($"| {r.Name} | {r.Room} | {(string.IsNullOrEmpty(r.Section) ? "—" : r.Section)} | {P(r.Centroid)} | {r.Cells.Count} | {links} |");
            }
            sb.AppendLine();
            sb.AppendLine("</details>");

            return sb.ToString();
        }

        // How many cells become unreachable from the biggest room if this region is closed.
        static int CutOffBy(StoreMap map, int region)
        {
            int start = -1, bestSize = -1;
            foreach (Region r in map.Regions)
                if (r.Id != region && !r.IsDoor && r.Cells.Count > bestSize) { bestSize = r.Cells.Count; start = r.Id; }
            if (start < 0) return 0;

            bool[] reach = map.ReachableRegions(start, new HashSet<int> { region });
            int lost = 0;
            foreach (Region r in map.Regions)
                if (r.Id != region && !reach[r.Id]) lost += r.Cells.Count;
            return lost;
        }

        // The graph as data. Regions, links, landmarks, doors and the ASCII plan; cells are
        // left out (an agent plans over regions, not a 2,800-node grid).
        public static string ToJson(StoreMap map, bool includeAscii = true)
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Field("summary", map.Summary());
            w.Field("cell_size", StoreMap.CellSize);
            w.Field("rooms", map.Rooms);

            w.Key("regions").BeginArray();
            foreach (Region r in map.Regions)
            {
                w.BeginObject();
                w.Field("id", r.Id);
                w.Field("name", r.Name);
                w.Field("room", r.Room);
                w.Field("section", r.Section ?? string.Empty);
                w.Field("door", r.IsDoor);
                w.Field("chokepoint", r.IsChokepoint);
                w.Key("centre").BeginArray().Value(r.Centroid.x).Value(r.Centroid.z).EndArray();
                w.Field("cells", r.Cells.Count);
                w.Key("links").BeginArray();
                foreach (RegionLink l in r.Links)
                    w.BeginObject().Field("to", l.To).Field("width", l.Capacity).EndObject();
                w.EndArray();
                w.EndObject();
            }
            w.EndArray();

            w.Key("landmarks").BeginArray();
            foreach (Landmark l in map.Landmarks)
            {
                w.BeginObject();
                w.Field("name", l.Name);
                w.Field("kind", l.Kind.ToString());
                w.Key("at").BeginArray().Value(l.Position.x).Value(l.Position.z).EndArray();
                w.Field("region", l.Region);
                w.EndObject();
            }
            w.EndArray();

            if (includeAscii) w.Field("ascii", map.ToAscii());
            w.EndObject();
            return w.ToString();
        }
    }
}
