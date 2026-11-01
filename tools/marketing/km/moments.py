"""The shifts' clip moments (<stem>.markers.json, Phase 2) as the Claude steps see them, and the
shots to render for the moments they pick.

A moment is named `<stem>#<rank>`. Shift records live in the game's data folder
(KEHAI_SHIFT_RECORDS, default ~/Library/Application Support/TokenLimit/Kehai/shift_records).
"""
from __future__ import annotations

import datetime as dt
import json
import os
import re
import sys
from pathlib import Path

from . import schemas
from .cli import BadInput

DEFAULT_RECORDS = Path.home() / "Library" / "Application Support" / "TokenLimit" / "Kehai" / "shift_records"
CAMERAS = ("pov", "cctv", "chase", "orbit", "topdown")
MAX_CLIP = 60.0


def records() -> Path:
    env = os.environ.get("KEHAI_SHIFT_RECORDS")
    return Path(env).expanduser() if env else DEFAULT_RECORDS


def week_range(week: str | None, today: dt.date | None = None) -> tuple[dt.date, dt.date, str]:
    """An ISO week ('2026-W40') as [monday, next monday), or the last 7 days when None."""
    if week:
        m = re.fullmatch(r"(\d{4})-W(\d{2})", week)
        if not m:
            raise BadInput(f"{week!r} isn't an ISO week like 2026-W40")
        start = dt.date.fromisocalendar(int(m.group(1)), int(m.group(2)), 1)
        return start, start + dt.timedelta(days=7), week
    today = today or dt.date.today()
    start = today - dt.timedelta(days=6)
    year, wk, _ = today.isocalendar()
    return start, today + dt.timedelta(days=1), f"{year}-W{wk:02d}"


def month_range(month: str) -> tuple[dt.date, dt.date]:
    m = re.fullmatch(r"(\d{4})-(\d{2})", month)
    if not m:
        raise BadInput(f"{month!r} isn't a month like 2026-10")
    start = dt.date(int(m.group(1)), int(m.group(2)), 1)
    end = dt.date(start.year + (start.month == 12), start.month % 12 + 1, 1)
    return start, end


def started(doc: dict) -> dt.date:
    return dt.datetime.strptime(doc["started"], "%Y-%m-%d %H:%M").date()


def mend_length(doc) -> None:
    """A shift stopped mid-way used to save a negative length. Its moments are sound, and the end
    of the last one is as far as anything here reads."""
    try:
        if doc["length"] < 0:
            doc["length"] = max((m["end"] for m in doc["moments"]), default=0.0)
    except (KeyError, TypeError):
        pass                        # not a markers file at all: the schema says what's wrong with it


def load(path: Path) -> dict:
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise BadInput(f"can't read {path}: {e}") from e
    mend_length(doc)
    problems = schemas.errors("markers", doc)
    if problems:
        raise BadInput(f"{path} isn't a valid markers file: {problems[0]}")
    return doc


def load_all(files: list[Path]) -> list[dict]:
    """The files that can be read. A bad one is named and left out: one old or damaged recording
    mustn't stop a whole job."""
    docs = []
    for f in files:
        try:
            docs.append(load(f))
        except BadInput as e:
            print(f"skipped: {e}", file=sys.stderr)
    return docs


def find_files(folder: Path, start: dt.date, end: dt.date) -> list[Path]:
    """The markers files whose shift started in [start, end)."""
    out = []
    for p in sorted(folder.glob("*.markers.json")):
        try:
            when = started(json.loads(p.read_text(encoding="utf-8")))
        except (OSError, ValueError, KeyError, TypeError):
            continue
        if start <= when < end:
            out.append(p)
    return out


def ref(stem: str, rank: int) -> str:
    return f"{stem}#{rank}"


def split_ref(text: str) -> tuple[str, int]:
    m = re.fullmatch(r"(.+)#(\d+)", text)
    if not m:
        raise ValueError(f"{text!r} isn't a moment ref (<stem>#<rank>)")
    return m.group(1), int(m.group(2))


def by_ref(docs: list[dict]) -> dict[str, tuple[dict, dict]]:
    return {ref(d["stem"], m["rank"]): (d, m) for d in docs for m in d["moments"]}


def events(doc: dict, start: float, end: float) -> list[dict]:
    """The markers between start and end, timed from start: what happens on screen, and when."""
    out = []
    for mk in doc["markers"]:
        if start - 0.01 <= mk["t"] <= end + 0.01:
            e = {"at": round(mk["t"] - start, 2), "what": mk["id"]}
            if mk.get("end", mk["t"]) > mk["t"] + 0.5:
                e["lasts"] = round(mk["end"] - mk["t"], 1)
            if mk.get("text"):
                e["text"] = mk["text"]
            if "value" in mk:
                e["value"] = mk["value"]
            out.append(e)
    return out


def digest(docs: list[dict], min_score: float = 0) -> list[dict]:
    """The shifts and their moments, compact, for a prompt."""
    shifts = []
    for d in docs:
        moments = []
        for m in d["moments"]:
            if m["score"] < min_score and not m["kept"]:
                continue
            moments.append({
                "ref": ref(d["stem"], m["rank"]), "start": m["start"], "seconds": round(m["end"] - m["start"], 1),
                "score": m["score"], "chase": m["chase"], "kept": m["kept"], "subjects": m["subjects"], "tags": m["tags"],
                "events": events(d, m["start"], m["end"]), "narrator": m["captionSeed"],
            })
        shifts.append({"stem": d["stem"], "shift": d["shift"], "started": d["started"], "minutes": round(d["length"] / 60, 1),
                       "rung": d["rung"], "clocked_out": d["clockedOut"], "moments": moments})
    return shifts


def kept(docs: list[dict]) -> list[str]:
    return [ref(d["stem"], m["rank"]) for d in docs for m in d["moments"] if m["kept"]]


def shot_range(doc: dict, moment: dict, start_offset: float, end_offset: float) -> tuple[float, float]:
    a = max(0.0, moment["start"] + start_offset)
    b = min(doc["length"], moment["end"] + end_offset)
    return round(a, 2), round(b, 2)


def render_request(doc: dict, moment: dict, shot: dict, out: str, fmt: str) -> dict:
    """One render_shot.sh run: its arguments and where the shot lands (relative to the working folder)."""
    a, b = shot_range(doc, moment, shot["start_offset"], shot["end_offset"])
    alpha = shot["alpha"]
    if alpha:
        size = "960x960" if fmt == "9:16" else "960x540"
    else:
        size = "1080x1920" if fmt == "9:16" else "1920x1080"
    args = ["-krec", str(records() / f"{doc['stem']}.krec"), "-from", f"{a}", "-to", f"{b}",
            "-shot", shot["camera"], "-subject", "you" if shot["camera"] == "pov" else shot["subject"],
            "-layers", ",".join(shot["layers"]) or "none", "-size", size, "-fps", "30", "-out", out]
    if alpha:
        args.insert(-2, "-alpha")
    return {"name": shot["name"], "moment": ref(doc["stem"], moment["rank"]), "from": a, "to": b, "out": out, "args": args}


def shot_problems(shots: list[dict], where: str, docs_by_ref: dict, moment_ref: str | None = None) -> list[str]:
    """Rules for the shots Claude asks for, shared by pick_moments and the long video's outline."""
    out = []
    names = [s["name"] for s in shots]
    for dup in sorted({n for n in names if names.count(n) > 1}):
        out.append(f"{where}: two shots are called {dup!r}")
    for s in shots:
        w = f"{where} shot {s['name']!r}"
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,30}", s["name"]):
            out.append(f"{w}: the name must be lowercase letters, digits and dashes")
        r = moment_ref or s.get("moment")
        if r not in docs_by_ref:
            out.append(f"{w}: no moment {r!r} in the digest")
            continue
        doc, m = docs_by_ref[r]
        a, b = shot_range(doc, m, s["start_offset"], s["end_offset"])
        if b - a < 2:
            out.append(f"{w}: only {b - a:.1f}s long; give it at least 2 s")
        if b - a > MAX_CLIP:
            out.append(f"{w}: {b - a:.1f}s long; at most {MAX_CLIP:.0f} s")
        if s["alpha"] and not s["layers"]:
            out.append(f"{w}: an alpha shot shows her mind alone, so it needs layers")
    return out
