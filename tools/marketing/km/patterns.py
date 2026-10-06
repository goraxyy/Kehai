"""The pattern library: recipes learnt from reference videos the owner sent (study_reference.py).

Each pattern is <root>/patterns/<name>.json: what Claude made of the video (hook, beats, pacing,
format, the recipe for the editor, the shots it needs, what not to copy), the owner's note, and
which videos have used it. The week's picking sees every active pattern and can give one to a
short; a reference the owner sent is given to one of the next week's shorts for certain; a ✏️
note that names a pattern brings it in. ❌ under a pattern drops it from the library (the file
stays, marked dropped).
"""
from __future__ import annotations

import datetime as dt
import json
import re
from pathlib import Path

from .cli import write_json

NAME = re.compile(r"[a-z0-9][a-z0-9-]{2,48}")


def folder(root: Path) -> Path:
    return root / "patterns"


def path(root: Path, name: str) -> Path:
    return folder(root) / f"{name}.json"


def load(root: Path, name: str) -> dict | None:
    if not name or not NAME.fullmatch(name):
        return None
    try:
        return json.loads(path(root, name).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def active(root: Path, most: int = 20) -> list[dict]:
    """Active patterns, newest first."""
    found = []
    for f in folder(root).glob("*.json"):
        try:
            p = json.loads(f.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        if p.get("status") == "active":
            found.append(p)
    return sorted(found, key=lambda p: p.get("created", ""), reverse=True)[:most]


def free_name(root: Path, wanted: str, keep: str | None = None) -> str:
    """`wanted`, or wanted-2, -3…, unless it's `keep` (the same pattern being studied again)."""
    name, n = wanted, 1
    while path(root, name).exists() and name != keep:
        n += 1
        name = f"{wanted}-{n}"
    return name


def save(root: Path, study: dict, reference: str, note: str, measured: dict, keep: str | None = None) -> dict:
    """Saves a studied reference as a pattern (again under `keep`, when it's a re-study)."""
    old = load(root, keep) if keep else None
    name = keep if old else free_name(root, study["name"])
    now = dt.datetime.now().isoformat(timespec="seconds")
    pattern = {"version": 1, "name": name, "title": study["title"], "status": "active", "reference": reference,
               "note": note, "created": old["created"] if old else now, "updated": now,
               "measured": measured, "study": {**study, "name": name},
               "used_by": old["used_by"] if old else []}
    write_json(path(root, name), pattern)
    return pattern


def set_status(root: Path, name: str, status: str) -> None:
    p = load(root, name)
    if p:
        p["status"] = status
        p["updated"] = dt.datetime.now().isoformat(timespec="seconds")
        write_json(path(root, name), p)


def used(root: Path, name: str, video: str) -> None:
    p = load(root, name)
    if p and video not in p["used_by"]:
        p["used_by"].append(video)
        write_json(path(root, name), p)


def compact(p: dict) -> dict:
    """What the week's picking needs to know about a pattern."""
    s = p["study"]
    return {"name": p["name"], "title": p["title"], "summary": s["summary"], "moments": s["kehai"]["moments"],
            "tags": s["kehai"]["tags"], "idea": s["kehai"]["idea"], "length": s["pacing"]["length"],
            "shots": s["shots"], "used": len(p["used_by"])}


def brief(p: dict) -> dict:
    """What a writer follows: the whole recipe, the owner's note, and what not to copy."""
    s = p["study"]
    return {"name": p["name"], "title": p["title"], "owner_note": p.get("note", ""), "summary": s["summary"],
            "why_it_works": s["why_it_works"], "hook": s["hook"], "beats": s["beats"], "pacing": s["pacing"],
            "format": s["format"], "recipe": s["recipe"], "missing": s["missing"], "idea": s["kehai"]["idea"],
            "avoid": s["avoid"]}


def named_in(root: Path, text: str) -> list[dict]:
    """Active patterns a note mentions, by name or title."""
    low = text.lower()
    return [p for p in active(root, most=200)
            if p["name"] in low or (len(p["title"]) > 3 and p["title"].lower() in low)]
