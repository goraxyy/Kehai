"""One session read into facts: who, on what, for how long, how it ended; how long until the first
clock-in and how far they walked first; what they opened and pressed (E on nothing is a sign of
confusion); each shift's numbers from its record; frame rate and errors; answers and bug notes;
the 3D replays it brought; and the path they walked, for the map."""
from __future__ import annotations

import json
import math
from collections import Counter
from pathlib import Path

SAMPLE_SECONDS = 0.5           # PlaytestSession samples position twice a second
MOST_PATH_POINTS = 3000

# The shift record's numbers worth carrying (ShiftAnalysis).
NUMBERS = {
    "Walked (m)": "walked_m", "Sprinting (s)": "sprinting_s", "Crouching (s)": "crouching_s",
    "Times Karen spotted you": "spotted", "Seconds in her sight": "seen_s", "Closest she got (m)": "closest_m",
    "Chases": "chases", "Catches": "catches", "Warning sounds": "warnings", "Customers served": "served",
    "Directions given": "directions", "Gave up asking": "gave_up_asking", "Gave up at the till": "gave_up_till",
    "Lowest energy (%)": "lowest_energy",
}


def read_log(folder: Path) -> list[dict]:
    lines = []
    path = folder / "session.jsonl"
    if not path.exists():
        return lines
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        try:
            line = json.loads(raw)
        except ValueError:
            continue                              # a line a crash cut short
        if isinstance(line, dict) and "k" in line:
            lines.append(line)
    return lines


def records(folder: Path) -> dict[str, dict]:
    """The shift records it brought, by stem."""
    found = {}
    for f in sorted((folder / "shifts").glob("shift_*.json")):
        if f.name.endswith(".markers.json"):
            continue
        try:
            found[f.stem] = json.loads(f.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
    return found


def facts(folder: Path) -> dict:
    lines = read_log(folder)
    start = next((l for l in lines if l["k"] == "start"), {})
    end = next((l for l in reversed(lines) if l["k"] == "end"), None)
    seconds = max((l.get("t", 0) for l in lines), default=0)
    launch = folder.name
    code = (start.get("code") or next((l.get("code") for l in lines if l["k"] == "code"), None) or folder.parent.name)

    # Shifts, from the log, with each one's record.
    recs = records(folder)
    shifts, open_shift = [], None
    for l in lines:
        if l["k"] != "shift":
            continue
        if l.get("state") == "start":
            open_shift = {"n": l.get("n"), "stem": l.get("stem"), "start_t": l["t"]}
            shifts.append(open_shift)
        elif l.get("state") == "end" and open_shift is not None:
            open_shift["end_t"] = l["t"]
            open_shift = None
    for s in shifts:
        r = recs.get(s.get("stem") or "")
        if r:
            numbers = (r.get("analysis") or {}).get("numbers") or {}
            s["length_s"] = r.get("length")
            s["clocked_out"] = r.get("clockedOut")
            s.update({short: numbers[name] for name, short in NUMBERS.items() if name in numbers})
            s["findings"] = list((r.get("analysis") or {}).get("findings") or [])[:6]
            s["jobs"] = (r.get("analysis") or {}).get("jobs") or []
        elif "end_t" in s:
            s["length_s"] = round(s["end_t"] - s["start_t"], 1)

    first_clock_in = shifts[0]["start_t"] if shifts else None

    # Where they went.
    samples = [l for l in lines if l["k"] == "pos" and isinstance(l.get("p"), list) and len(l["p"]) == 3]
    walked_before, last = 0.0, None
    for s in samples:
        if first_clock_in is not None and s["t"] >= first_clock_in:
            break
        if last is not None:
            walked_before += math.dist((s["p"][0], s["p"][2]), (last["p"][0], last["p"][2]))
        last = s
    still = sum(1 for s in samples if s.get("move") == "still") * SAMPLE_SECONDS
    step = max(1, math.ceil(len(samples) / MOST_PATH_POINTS))
    path = [[round(s["p"][0], 1), round(s["p"][2], 1), 1 if s.get("shift") else 0] for s in samples[::step]]

    # What they opened and pressed.
    opened = Counter(l.get("name") for l in lines if l["k"] == "panel" and l.get("open"))
    keys = Counter(l.get("key") for l in lines if l["k"] == "key")
    e_nothing = sum(1 for l in lines if l["k"] == "key" and l.get("key") == "E" and l.get("target") == "nothing")

    fps = [l for l in lines if l["k"] == "fps"]
    errors = [l for l in lines if l["k"] == "error"]
    answers = next((l for l in reversed(lines) if l["k"] == "answers"), None)
    bugs = [{"i": i + 1, "t": l.get("t"), "note": l.get("note", ""), "rec": l.get("rec"), "rec_time": l.get("recTime"),
             "shift_time": l.get("shiftTime")} for i, l in enumerate(l for l in lines if l["k"] == "bug")]
    replays = sorted(p.name for p in (folder / "shifts").glob("*.krec*")) if (folder / "shifts").is_dir() else []

    return {
        "code": code,
        "round": start.get("round") or folder.parent.parent.name,
        "launch": launch,
        "build": start.get("build"),
        "computer": {k: start.get(k) for k in ("platform", "os", "device", "cpu", "ramMB", "gpu", "gpuMB", "graphics", "screen", "quality", "language")},
        "minutes": round(seconds / 60, 1),
        "ended": (end or {}).get("reason") or "unknown (no end line)",
        "continues": start.get("continues"),
        "sends": start.get("sends"),
        "shifts": shifts,
        "first_clock_in_s": first_clock_in,
        "walked_before_clock_in_m": round(walked_before, 1),
        "still_s": round(still),
        "opened": dict(opened),
        "task_list_opened": opened.get("task list", 0) > 0,
        "map_opened": opened.get("map (F1)", 0) > 0,
        "settings_opened": opened.get("settings", 0) > 0,
        "keys": dict(keys),
        "e_on_nothing": e_nothing,
        "focus_lost": sum(1 for l in lines if l["k"] == "focus" and not l.get("on")),
        "fps": {"avg": round(sum(f["avg"] for f in fps) / len(fps), 1) if fps else None,
                "lowest": round(min(f["avg"] for f in fps), 1) if fps else None,
                "worst_frame_ms": round(max(f["worstMs"] for f in fps), 1) if fps else None},
        "errors": len(errors),
        "error_messages": list(dict.fromkeys(e.get("message", "") for e in errors))[:10],
        "answers": {k: v for k, v in answers.items() if k not in ("t", "k")} if answers else None,
        "bugs": bugs,
        "replays": replays,
        "path": path,
    }


def write_facts(folder: Path) -> dict:
    f = facts(folder)
    (folder / "facts.json").write_text(json.dumps(f, indent=1, ensure_ascii=False), encoding="utf-8")
    return f


def plan(folder: Path) -> dict | None:
    """The store's floor plan, from any shift record the session brought."""
    for r in records(folder).values():
        if isinstance(r.get("plan"), dict) and r["plan"].get("bounds"):
            return r["plan"]
    return None


def timeline(folder: Path, most: int = 400) -> list[list]:
    """The session as Claude reads it: [seconds, what, detail], without the position and frame-rate noise."""
    out = []
    for l in read_log(folder):
        k, t = l["k"], l.get("t")
        if k == "panel":
            out.append([t, "opened" if l.get("open") else "closed", l.get("name")])
        elif k == "key":
            out.append([t, "key", l.get("key") + (f" on {l['target']}" if "target" in l else "") + (" (bug mark)" if l.get("bug") else "")])
        elif k == "shift":
            out.append([t, f"shift {l.get('state')}", l.get("n")])
        elif k in ("bug", "answers", "career", "consent", "end"):
            out.append([t, k, {kk: v for kk, v in l.items() if kk not in ("t", "k")}])
        elif k == "focus":
            out.append([t, "window " + ("focused" if l.get("on") else "left"), None])
        elif k == "error":
            out.append([t, "error", (l.get("message") or "")[:200]])
    if len(out) > most:
        out = out[: most // 2] + [[None, "…", f"{len(out) - most} lines left out"]] + out[-most // 2:]
    return out
