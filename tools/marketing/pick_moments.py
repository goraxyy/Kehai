#!/usr/bin/env python3
"""Picks the week's shorts from the shifts' clip moments, and the shots to render for each
(BUILD_PLAN.md, Phase 6).

    uv run pick_moments.py [--week 2026-W40 | --files a.markers.json ...] [--shorts 3] [--pattern NAME ...] [--dry-run] [--root DIR]

Reads the week's <stem>.markers.json (the game's shift_records folder, or KEHAI_SHIFT_RECORDS),
asks Claude which moments make the best shorts and how to film them, and writes
plans/<week>/picks.json: the picks, a decision for every moment marked kept (F7), and the
render_shot.sh runs that make the shots (render_picks.py runs them). Moments already used in an
earlier week are marked as such. Claude sees the pattern library (patterns/, from the reference
videos the owner sent) and may give a short a pattern; each --pattern must be given to one of the
shorts, unless no moment fits it (then `pattern_fit` says what to play). Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
from pathlib import Path

from km import moments, paths, patterns
from km.cli import BadInput, run, write_json
from km.llm import client, prompts
from km.llm.steps import step

def used_before(root: Path, week: str) -> set[str]:
    used = set()
    for f in (root / "plans").glob("*/picks.json"):
        if f.parent.name == week:
            continue
        try:
            used |= {p["moment"] for p in json.loads(f.read_text(encoding="utf-8"))["answer"]["shorts"]}
        except (OSError, ValueError, KeyError):
            continue
    return used


def checks(answer: dict, by_ref: dict, kept: list[str], wanted: int, library: set[str] = frozenset(),
           must: list[str] = ()) -> list[str]:
    out = []
    names = [p["name"] for p in answer["shorts"]]
    for dup in sorted({n for n in names if names.count(n) > 1}):
        out.append(f"two shorts are called {dup!r}")
    if len(answer["shorts"]) != wanted:
        out.append(f"{len(answer['shorts'])} shorts picked; pick exactly {wanted}")
    for p in answer["shorts"]:
        w = f"short {p['name']!r}"
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{2,40}", p["name"]):
            out.append(f"{w}: the name must be lowercase words joined by dashes")
        if p["moment"] not in by_ref:
            out.append(f"{w}: no moment {p['moment']!r} in the digest")
            continue
        out += moments.shot_problems(p["shots"], w, by_ref, p["moment"])
        if not any(not s["alpha"] for s in p["shots"]):
            out.append(f"{w}: needs at least one full-frame shot (not alpha)")
    decided = [k["moment"] for k in answer["kept"]]
    for r in kept:
        if decided.count(r) != 1:
            out.append(f"kept moment {r} needs exactly one entry in `kept` (it has {decided.count(r)})")
    for r in decided:
        if r not in kept:
            out.append(f"{r} isn't marked kept; `kept` lists only kept moments")
    given = [p["pattern"] for p in answer["shorts"] if p["pattern"]]
    for name in sorted({n for n in given if given.count(n) > 1}):
        out.append(f"two shorts follow the pattern {name!r}; one a week")
    for p in answer["shorts"]:
        if p["pattern"] and p["pattern"] not in library:
            out.append(f"short {p['name']!r}: no pattern {p['pattern']!r} in the library")
    fits = {f["pattern"]: f for f in answer["pattern_fit"]}
    if sorted(fits) != sorted(must) or len(answer["pattern_fit"]) != len(must):
        out.append(f"pattern_fit needs exactly one entry for each must_use pattern: {list(must)}")
    for name in must:
        following = [p["name"] for p in answer["shorts"] if p["pattern"] == name]
        f = fits.get(name)
        if f is None:
            continue
        if f["fits"] and len(following) != 1:
            out.append(f"pattern {name!r} fits, so exactly one short follows it ({len(following)} do)")
        if not f["fits"] and (following or not f["play"].strip()):
            out.append(f"pattern {name!r} doesn't fit: no short follows it, and `play` says what moment to play for it")
    picked = {p["moment"] for p in answer["shorts"]}
    for k in answer["kept"]:
        if (k["use"] == "short") != (k["moment"] in picked):
            out.append(f"kept moment {k['moment']}: use is {k['use']!r} but it {'is' if k['moment'] in picked else 'is not'} in shorts")
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--week", help="ISO week, e.g. 2026-W40 (default: the last 7 days)")
    ap.add_argument("--files", nargs="+", type=Path, help="markers files to use instead of a week's")
    ap.add_argument("--shorts", type=int, default=3)
    ap.add_argument("--pattern", action="append", default=[], help="a library pattern one of the shorts must follow")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()

    root = paths.root(a.root)
    start, end, week = moments.week_range(a.week)
    files = a.files or moments.find_files(moments.records(), start, end)
    if not files:
        print(f"pick_moments: no shifts with clip moments in {week} ({moments.records()}); nothing to pick.")
        return 0
    docs = [moments.load(f) for f in files]
    by_ref = moments.by_ref(docs)
    kept = moments.kept(docs)
    available = len(by_ref)
    wanted = min(a.shorts, available)
    if wanted == 0:
        print(f"pick_moments: the {len(docs)} shift(s) of {week} have no moments; nothing to pick.")
        return 0
    used = sorted(used_before(root, week) & set(by_ref))
    library = {p["name"]: p for p in patterns.active(root)}
    for name in a.pattern:
        if name not in library:
            found = patterns.load(root, name)
            if not found or found["status"] != "active":
                raise BadInput(f"no active pattern {name!r} in {patterns.folder(root)}")
            library[name] = found
    must = a.pattern[:wanted]

    brief = {"week": week, "shorts_wanted": wanted, "format": "9:16, 15 to 40 seconds",
             "kept_moments": kept, "used_in_earlier_weeks": used, "must_use": must}
    parts = [prompts.data_block("brief", brief)]
    if library:
        parts.append(prompts.data_block("patterns", [patterns.compact(p) for p in library.values()]))
    parts += [prompts.data_block("shifts", moments.digest(docs)),
              f"Pick the {wanted} shorts for {week} and decide every kept moment."
              + (f" One of them follows each must_use pattern ({', '.join(must)}) if a moment fits it." if must else "")]
    request = client.Request(
        step=step("pick_moments"), system=prompts.system("pick_moments"), user="\n\n".join(parts),
        schema=client.schema("pick_moments"), ref=week)
    answer = client.ask(request, root, lambda ans: checks(ans, by_ref, kept, wanted, set(library), must),
                        dry_run=a.dry_run)
    if answer is None:
        return 0

    renders = []
    for p in answer["shorts"]:
        doc, m = by_ref[p["moment"]]
        for s in p["shots"]:
            ext = "webm" if s["alpha"] else "mp4"
            renders.append({"pick": p["name"], **moments.render_request(doc, m, s, f"shots/{week}/{p['name']}/{s['name']}.{ext}", "9:16")})
    out = root / "plans" / week / "picks.json"
    write_json(out, {"version": 1, "week": week, "made": dt.datetime.now().isoformat(timespec="seconds"),
                     "sources": [str(f) for f in files], "answer": answer, "renders": renders})
    print(f"pick_moments: {week}: {answer['week_theme']}")
    for p in answer["shorts"]:
        print(f"  {p['name']}: {p['moment']} — {p['angle']} ({len(p['shots'])} shots"
              + (f", pattern {p['pattern']})" if p["pattern"] else ")"))
    for f in answer["pattern_fit"]:
        if not f["fits"]:
            print(f"  pattern {f['pattern']}: no moment fits; play a shift where {f['play']}")
    for k in answer["kept"]:
        print(f"  kept {k['moment']}: {k['use']} — {k['reason']}")
    print(f"pick_moments: wrote {out} ({len(renders)} shots to render: render_picks.py {out})")
    return 0


if __name__ == "__main__":
    run(main)
