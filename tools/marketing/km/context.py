"""What a video is written against: the rendered shots (their sidecars) and the asset library."""
from __future__ import annotations

import json
import wave
from pathlib import Path

from . import assets as library
from . import moments, schemas


def _rel(root: Path, path: Path) -> str:
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError as e:
        raise ValueError(f"{path} is outside the working folder {root}") from e


def shot_entry(root: Path, sidecar: Path, docs_by_stem: dict | None = None) -> dict | None:
    try:
        d = json.loads(sidecar.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    if not isinstance(d, dict) or schemas.errors("shot", d):
        return None
    video = sidecar.parent / d["video"]
    if not video.is_file():
        return None
    entry = {"id": sidecar.stem, "src": _rel(root, video), "seconds": round(d["frames"] / d["fps"], 2),
             "camera": d["shot"], "subject": d["subject"], "layers": d["layers"], "alpha": d["alpha"],
             "size": f"{d['width']}x{d['height']}", "shift": d["stem"], "shift_from": d["from"], "shift_to": d["to"]}
    doc = (docs_by_stem or {}).get(d["stem"])
    if doc:
        entry["events"] = moments.events(doc, d["from"], d["to"])
    if d.get("track"):
        if "aiko" in d["track"] and "karen" not in d["track"]:   # rendered while she was called Aiko
            d["track"]["karen"] = d["track"].pop("aiko")
        entry["track"] = d["track"]
        entry["where"] = where(d["track"], entry["seconds"])
    return entry


def where(track: dict, seconds: float) -> list:
    """Once a second: [t, karen, you], each [x, y] in the picture or null when off screen."""
    hz = track["hz"]
    out = []
    for t in range(int(seconds) + 1):
        i = t * hz
        row = [t]
        for who in ("karen", "you"):
            s = track[who][i] if i < len(track[who]) else None
            row.append([round(s[0], 2), round(s[1], 2)] if s else None)
        out.append(row)
    return out


def shots_in(root: Path, folder: Path, docs_by_stem: dict | None = None) -> list[dict]:
    """Every rendered shot in a folder (a sidecar .json beside its video)."""
    out = []
    for sidecar in sorted(folder.glob("*.json")):
        e = shot_entry(root, sidecar, docs_by_stem)
        if e:
            out.append(e)
    return out


def planned_shot(request: dict, camera: dict, doc: dict | None) -> dict:
    """A shot not rendered yet (a dry run), described from its render request."""
    entry = {"id": request["name"], "src": request["out"], "seconds": round(request["to"] - request["from"], 2),
             "camera": camera["camera"], "subject": camera["subject"], "layers": camera["layers"],
             "alpha": camera["alpha"], "shift_from": request["from"], "shift_to": request["to"], "rendered": False}
    if doc:
        entry["events"] = moments.events(doc, request["from"], request["to"])
    return entry


def _seconds(path: Path) -> float | None:
    if path.suffix.lower() != ".wav":
        return None
    try:
        with wave.open(str(path), "rb") as w:
            return round(w.getnframes() / w.getframerate(), 1)
    except (OSError, wave.Error, EOFError):
        return None


def assets_for(root: Path) -> list[dict]:
    """The library as a prompt sees it: what each asset is and what it's for."""
    out = []
    for a in library.load(root)["assets"]:
        if a["type"] == "music" and not all(a.get("cleared", {}).get(p) for p in library.PLATFORMS):
            continue
        e = {"id": a["id"], "type": a["type"], "file": a["file"], "mood": a.get("mood", []), "tags": a.get("tags", [])}
        if a.get("notes"):
            e["notes"] = a["notes"]
        seconds = _seconds(root / a["file"])
        if seconds:
            e["seconds"] = seconds
        out.append(e)
    return out
