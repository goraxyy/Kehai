#!/usr/bin/env python3
"""Writes a Markdown map of a Unity scene: its hierarchy, where things are, and what each prefab is.

The scene (`SampleScene.unity`) isn't in git, so this map is how the repository knows the layout.
It reads the scene's text YAML directly, without Unity:

    python3 tools/scene/scene_map.py "Assets/!_Project/_Core/Scenes/SampleScene.unity" > docs/agents/store/SCENE_MAP.md

Positions are world metres, composed from each transform's local position, rotation and scale.
A prefab instance whose root keeps the prefab's own transform is placed from the scene's
overrides only, so its position can be off by the prefab root's offset (usually zero).
"""
import math
import os
import re
import sys
from collections import Counter, defaultdict
from datetime import date

HEADER = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?")
FILEID = re.compile(r"fileID: (-?\d+)")
GUID = re.compile(r"guid: ([0-9a-f]{32})")
VEC = re.compile(r"(\w): (-?[\d.eE+-]+)")


def vec(text, keys):
    found = dict(VEC.findall(text))
    return tuple(float(found.get(k, d)) for k, d in keys)


def parse(path):
    docs = {}
    cur = None
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            m = HEADER.match(line)
            if m:
                cur = {"cls": int(m.group(1)), "id": m.group(2), "stripped": bool(m.group(3)), "lines": []}
                docs[cur["id"]] = cur
            elif cur is not None:
                cur["lines"].append(line.rstrip("\n"))
    return docs


def field(lines, name):
    prefix = "  " + name + ":"
    for i, l in enumerate(lines):
        if l.startswith(prefix):
            return l[len(prefix):].strip(), i
    return None, -1


def ref(text):
    m = FILEID.search(text or "")
    return m.group(1) if m else "0"


def guid_names(project):
    names = {}
    assets = os.path.join(project, "Assets")
    for root, _, files in os.walk(assets):
        for fn in files:
            if not fn.endswith(".meta"):
                continue
            try:
                with open(os.path.join(root, fn), encoding="utf-8", errors="replace") as f:
                    for _ in range(3):
                        m = GUID.search(f.readline())
                        if m:
                            names[m.group(1)] = os.path.relpath(os.path.join(root, fn[:-5]), project)
                            break
            except OSError:
                pass
    return names


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


def qrot(q, v):
    x, y, z, w = q
    vx, vy, vz = v
    tx, ty, tz = 2 * (y * vz - z * vy), 2 * (z * vx - x * vz), 2 * (x * vy - y * vx)
    return (vx + w * tx + (y * tz - z * ty), vy + w * ty + (z * tx - x * tz), vz + w * tz + (x * ty - y * tx))


def build(docs, guids):
    nodes = {}          # transform id → node
    go_name, go_active = {}, {}
    for d in docs.values():
        if d["cls"] == 1 and not d["stripped"]:
            name, _ = field(d["lines"], "m_Name")
            active, _ = field(d["lines"], "m_IsActive")
            go_name[d["id"]] = name or "?"
            go_active[d["id"]] = active != "0"

    instances = {}
    for d in docs.values():
        if d["cls"] != 1001:
            continue
        lines = d["lines"]
        parent = ref(next((l for l in lines if "m_TransformParent:" in l), ""))
        source = next((l for l in lines if "m_SourcePrefab:" in l), "")
        g = GUID.search(source)
        mods = []
        for i, l in enumerate(lines):
            if l.strip().startswith("- target:"):
                tgt = ref(l)
                prop = lines[i + 1].split("propertyPath:", 1)[1].strip().strip("'") if i + 1 < len(lines) and "propertyPath:" in lines[i + 1] else ""
                val = lines[i + 2].split("value:", 1)[1].strip() if i + 2 < len(lines) and "value:" in lines[i + 2] else ""
                mods.append((tgt, prop, val))
        instances[d["id"]] = {"parent": parent, "prefab": guids.get(g.group(1), g.group(1)) if g else "?", "mods": mods}

    stripped_go_source = {}
    for d in docs.values():
        if d["cls"] == 1 and d["stripped"]:
            src = ref(next((l for l in d["lines"] if "m_CorrespondingSourceObject" in l), ""))
            inst = ref(next((l for l in d["lines"] if "m_PrefabInstance" in l), ""))
            stripped_go_source.setdefault(inst, set()).add(src)

    # A prefab instance's root shows up in its parent's m_Children; other stripped transforms of
    # the same instance are parts inside the prefab, referenced only as parents of added objects.
    listed = set()
    for d in docs.values():
        if d["cls"] in (4, 224) and not d["stripped"]:
            inside = False
            for l in d["lines"]:
                if l.startswith("  m_Children:"):
                    inside = True
                    continue
                if inside:
                    if l.startswith("  - "):
                        listed.add(ref(l))
                    else:
                        break
    inner = {}          # stripped transform id → its instance's root transform id (filled below)

    for d in docs.values():
        if d["cls"] not in (4, 224):
            continue
        lines = d["lines"]
        if d["stripped"] and d["id"] not in listed:
            inst_id = ref(next((l for l in lines if "m_PrefabInstance" in l), ""))
            inner[d["id"]] = inst_id
            continue
        if d["stripped"]:
            inst_id = ref(next((l for l in lines if "m_PrefabInstance" in l), ""))
            src = ref(next((l for l in lines if "m_CorrespondingSourceObject" in l), ""))
            inst = instances.get(inst_id)
            if inst is None:
                continue
            pos, rot, scl = [0.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0], [1.0, 1.0, 1.0]
            name, active = None, True
            roots = stripped_go_source.get(inst_id, set())
            for tgt, prop, val in inst["mods"]:
                try:
                    if tgt == src and prop.startswith("m_LocalPosition."):
                        pos["xyz".index(prop[-1])] = float(val)
                    elif tgt == src and prop.startswith("m_LocalRotation."):
                        rot["xyzw".index(prop[-1])] = float(val)
                    elif tgt == src and prop.startswith("m_LocalScale."):
                        scl["xyz".index(prop[-1])] = float(val)
                    elif prop == "m_Name" and (name is None or tgt in roots):
                        name = val
                    elif prop == "m_IsActive" and (not roots or tgt in roots):
                        active = val != "0"
                except ValueError:
                    pass
            prefab = inst["prefab"]
            nodes[d["id"]] = {"name": name or os.path.splitext(os.path.basename(prefab))[0], "parent": inst["parent"],
                              "pos": tuple(pos), "rot": tuple(rot), "scl": tuple(scl), "prefab": prefab,
                              "active": active, "children": [], "instance": inst_id}
        else:
            go = ref(field(lines, "m_GameObject")[0])
            r, _ = field(lines, "m_LocalRotation")
            p, _ = field(lines, "m_LocalPosition")
            s, _ = field(lines, "m_LocalScale")
            father, _ = field(lines, "m_Father")
            nodes[d["id"]] = {"name": go_name.get(go, "?"), "parent": ref(father),
                              "pos": vec(p or "", [("x", 0), ("y", 0), ("z", 0)]),
                              "rot": vec(r or "", [("x", 0), ("y", 0), ("z", 0), ("w", 1)]),
                              "scl": vec(s or "", [("x", 1), ("y", 1), ("z", 1)]),
                              "prefab": None, "active": go_active.get(go, True), "children": []}

    # Objects added under a part inside a prefab hang off the instance's root instead (the part's
    # own offset inside the prefab isn't in the scene file).
    root_of_instance = {n["instance"]: tid for tid, n in nodes.items() if n.get("instance")}
    for n in nodes.values():
        if n["parent"] in inner:
            n["parent"] = root_of_instance.get(inner[n["parent"]], n["parent"])

    roots = []
    for tid, n in nodes.items():
        if n["parent"] in nodes:
            nodes[n["parent"]]["children"].append(tid)
        else:
            roots.append(tid)

    order = next((d for d in docs.values() if d["cls"] == 1660057539), None)
    if order:
        ranked = [ref(l) for l in order["lines"] if l.strip().startswith("- {fileID")]
        rank = {t: i for i, t in enumerate(ranked)}
        roots.sort(key=lambda t: rank.get(t, 1e9))

    def place(tid, wpos, wrot, wscl):
        n = nodes[tid]
        lp = tuple(a * b for a, b in zip(wscl, n["pos"]))
        n["wpos"] = tuple(a + b for a, b in zip(wpos, qrot(wrot, lp)))
        n["wrot"] = qmul(wrot, n["rot"])
        n["wscl"] = tuple(a * b for a, b in zip(wscl, n["scl"]))
        for c in n["children"]:
            place(c, n["wpos"], n["wrot"], n["wscl"])

    for r in roots:
        place(r, (0.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (1.0, 1.0, 1.0))
    return nodes, roots


def count(nodes, tid):
    return sum(1 + count(nodes, c) for c in nodes[tid]["children"])


def stem(name):
    return re.sub(r"(\s*\(\d+\)|[_\s-]*\d+)+$", "", name) or name


def fmt_pos(n):
    x, y, z = n["wpos"]
    return f"({x:.1f}, {y:.1f}, {z:.1f})"


def label(n):
    bits = [f"**{n['name']}**"]
    if n["prefab"]:
        bits.append(f"`{os.path.basename(n['prefab'])}`")
    if not n["active"]:
        bits.append("*(inactive)*")
    return " ".join(bits)


def tree(nodes, tid, depth, max_depth, out, max_list=10):
    n = nodes[tid]
    inside = count(nodes, tid)
    extra = f" — {inside} objects inside" if inside else ""
    out.append(f"{'  ' * depth}- {label(n)} at {fmt_pos(n)}{extra}")
    kids = n["children"]
    if depth + 1 > max_depth or not kids:
        return
    if len(kids) > max_list:
        groups = Counter(stem(nodes[c]["name"]) for c in kids)
        shown = 0
        for c in kids:
            if groups[stem(nodes[c]["name"])] == 1 and shown < max_list // 2 and nodes[c]["children"]:
                tree(nodes, c, depth + 1, max_depth, out, max_list)
                shown += 1
        summary = ", ".join(f"{s} ×{k}" if k > 1 else s for s, k in groups.most_common(12))
        more = "" if len(groups) <= 12 else f", and {len(groups) - 12} more kinds"
        out.append(f"{'  ' * (depth + 1)}- *{len(kids)} children:* {summary}{more}")
    else:
        for c in kids:
            tree(nodes, c, depth + 1, max_depth, out, max_list)


def main():
    scene = os.path.abspath(sys.argv[1])
    parts = scene.split(os.sep)
    project = os.sep.join(parts[:parts.index("Assets")]) if "Assets" in parts else os.getcwd()
    guids = guid_names(project)
    docs = parse(scene)
    nodes, roots = build(docs, guids)

    total = len(nodes)
    prefabs = Counter(os.path.basename(n["prefab"]) for n in nodes.values() if n["prefab"])
    out = [
        "# Scene map: SampleScene",
        "",
        f"Generated on {date.today().isoformat()} by `tools/scene/scene_map.py` from "
        "`Assets/!_Project/_Core/Scenes/SampleScene.unity`. Don't edit by hand: rerun the script after changing the scene.",
        "",
        "The scene and the art aren't in git (the repository is code and docs only), so this file is how the "
        "repository records **where everything is**. Karen's own map of the same store (regions, links, "
        "chokepoints) is [`STORE_MAP.md`](STORE_MAP.md); the building, products and stocking are in "
        "[`STORE.md`](STORE.md); room plans in [`LEVEL_DESIGN.md`](LEVEL_DESIGN.md) and "
        "[`INTERIOR_MODELS.md`](INTERIOR_MODELS.md).",
        "",
        "- **Coordinates:** world metres, shown as (x, y, z). The street is north (high Z), the store runs south "
        "along −Z to the stockroom, and east is +X. y is height.",
        f"- **Size:** {total} objects in the hierarchy, {sum(prefabs.values())} of them prefab instances, "
        f"{len(roots)} top-level objects.",
        "- **How it's organised:** `Kehai → Scene → Organize by Area and Name Objects` (`SceneHierarchyOrganizer.cs`) "
        "puts everything under a few roots by area and gives objects stable names.",
        "",
        "## Top level",
        "",
        "| object | where | objects inside |",
        "|---|---|---|",
    ]
    for r in roots:
        n = nodes[r]
        out.append(f"| {label(n)} | {fmt_pos(n)} | {count(nodes, r)} |")

    notes = {
        "Supermarket": "the building: one folder per room (sales floor, staff room, office, WC, lobby, storage, cold room)",
        "Exterior": "outside: the storefront, the service yard (backstreet) and the parking lot",
        "Game_Systems": "the player, the UI, the light and post-processing volume, and customer routing points",
        "Models_Island": "an off-map showcase grid, away from the store, with one of every model and product box, "
                         "used to check models; nothing on it is part of the playable store",
        "Authoring_References": "builder containers, inactive prototypes and legacy fixtures kept for reference",
        "Generated_Content": "objects the builders and the game generate (room reflection probes, floor and ceiling "
                             "patches, the shelf product drawer)",
    }
    out += ["", "What each root holds:", ""]
    out += [f"- **{nodes[r]['name']}**: {notes[nodes[r]['name']]}." for r in roots if nodes[r]["name"] in notes]

    out += ["", "## The hierarchy", "",
            "Three levels down from each root. Long lists of similar objects are summarised by name.", ""]
    for r in roots:
        tree(nodes, r, 0, 3, out)

    interesting = re.compile(r"(Time_Clock|Coffee|Radio|Mop|Flashlight|Breaker|Power|PA_|Speaker|Checkout|Cashier|"
                             r"Till|Entrance|Exit|Spawner|Player|Door|Locker|Trash|Bin|Fridge|Chiller|Freezer|Camera|"
                             r"GameSystems|Karen|NavMesh|Volume|Directional Light)", re.I)
    picks = []
    for tid, n in nodes.items():
        if interesting.search(n["name"]) and count(nodes, tid) < 400:
            path, p = [n["name"]], n["parent"]
            while p in nodes:
                path.append(nodes[p]["name"])
                p = nodes[p]["parent"]
            picks.append((" / ".join(reversed(path)), n))
    priority = {"Game_Systems": 0, "Supermarket": 1, "Exterior": 2}
    seen, rows = set(), []
    for path, n in sorted(picks, key=lambda t: (priority.get(t[0].split(" / ")[0], 9), t[0])):
        key = stem(path)
        if key in seen:
            continue
        seen.add(key)
        rows.append(f"| {path} | {fmt_pos(n)} | {os.path.basename(n['prefab']) if n['prefab'] else ''} |")
    out += ["", "## Gameplay objects and fixtures", "",
            "The time clock, tools, doors, coolers, bins, spawn points and the like (first of each kind).", "",
            "| path | where | prefab |", "|---|---|---|"] + rows[:160]
    if len(rows) > 160:
        out.append(f"| … {len(rows) - 160} more | | |")

    out += ["", "## Most-used prefabs", "", "| prefab | instances |", "|---|---|"]
    out += [f"| `{p}` | {k} |" for p, k in prefabs.most_common(25)]
    print("\n".join(out))


if __name__ == "__main__":
    main()
