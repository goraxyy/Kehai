#!/usr/bin/env python3
"""Writes a short from a picked moment: scenes, text, voice-over script, music (BUILD_PLAN.md, Phase 6).

    uv run write_short.py --picks plans/<week>/picks.json --pick <name> [--dry-run] [--root DIR]
    uv run write_short.py --shots <folder of rendered shots> --brief "what it's about" [--moment <stem>#<rank>]
    … [--pattern NAME]   follow a pattern from the library (a pick may already name one)

Claude writes the draft (schemas/llm/draft.schema.json) against the pick's rendered shots and the
asset library; it is checked (lengths, ranges, timing, files) and saved as edits/<id>/ with its
edit.json in English. A pick's pattern (patterns/<name>.json, learnt from a reference video) comes
with its whole recipe, and the short follows it. Next: translate.py, voice.py, then editor/render.mjs.
Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
from pathlib import Path

from km import context, drafts, moments, paths, patterns, timeline, writing
from km.cli import BadInput, read_json, run
from km.llm import client, prompts
from km.llm.steps import step


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--picks", type=Path)
    ap.add_argument("--pick")
    ap.add_argument("--shots", type=Path, help="a folder of rendered shots (instead of a pick)")
    ap.add_argument("--brief", help="with --shots: what the short is about")
    ap.add_argument("--moment", help="with --shots: the moment's ref, for its events")
    ap.add_argument("--id", help="the edit's id, whatever the draft says (the pipeline names videos <week>-<pick>)")
    ap.add_argument("--pattern", help="a library pattern to follow (a pick's own pattern otherwise)")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)

    docs = {p.name.removesuffix(".markers.json"): p for p in moments.records().glob("*.markers.json")}

    def doc_for(stem: str) -> dict | None:
        return moments.load(docs[stem]) if stem in docs else None

    if a.picks:
        plan = read_json(a.picks, "picks")
        pick = next((p for p in plan["answer"]["shorts"] if p["name"] == a.pick), None)
        if pick is None:
            raise BadInput(f"no pick {a.pick!r} in {a.picks} (there are: {', '.join(p['name'] for p in plan['answer']['shorts'])})")
        stem, rank = moments.split_ref(pick["moment"])
        doc = doc_for(stem)
        moment = next((m for m in doc["moments"] if m["rank"] == rank), None) if doc else None
        shots = []
        for r in (r for r in plan["renders"] if r["pick"] == pick["name"]):
            sidecar = (root / r["out"]).with_suffix(".json")
            entry = context.shot_entry(root, sidecar, {stem: doc} if doc else None)
            if entry is None:
                if not a.dry_run:
                    raise BadInput(f"shot {r['out']} isn't rendered yet: render_picks.py {a.picks} --only {pick['name']}")
                camera = next(s for s in pick["shots"] if s["name"] == r["name"])
                entry = context.planned_shot(r, camera, doc)
            entry["id"] = r["name"]
            shots.append(entry)
        brief = {"angle": pick["angle"], "hook_idea": pick["hook_idea"], "why": pick["why"]}
        source = {"stem": stem, "moments": [rank], "notes": pick["angle"]}
        base_id = pick["name"]
        wanted = a.pattern or pick.get("pattern")
    elif a.shots:
        if not a.brief:
            raise BadInput("--shots needs --brief")
        stem = rank = moment = doc = None
        if a.moment:
            stem, rank = moments.split_ref(a.moment)
            doc = doc_for(stem)
            moment = next((m for m in doc["moments"] if m["rank"] == rank), None) if doc else None
        shots = context.shots_in(root, a.shots, {stem: doc} if doc else None)
        if not shots:
            raise BadInput(f"no rendered shots in {a.shots}")
        brief = {"angle": a.brief}
        source = {"stem": stem, "moments": [rank], "notes": a.brief} if stem else {"notes": a.brief}
        base_id = None
        wanted = a.pattern
    else:
        raise BadInput("give --picks and --pick, or --shots and --brief")

    if moment:
        brief["moment"] = {"seconds": round(moment["end"] - moment["start"], 1), "score": moment["score"],
                           "chase": moment["chase"], "subjects": moment["subjects"], "tags": moment["tags"],
                           "narrator": moment["captionSeed"]}
    pattern = patterns.load(root, wanted) if wanted else None
    if wanted and not pattern:
        print(f"write_short: no pattern {wanted!r} in {patterns.folder(root)}; writing without one")
    if pattern:
        brief["pattern"] = patterns.brief(pattern)
        source["notes"] = f"{source['notes']} · Pattern: {pattern['title']}"
    ctx = {"kind": "short", "format": "9:16", "fps": 30, "source": source, "shots": shots,
           "assets": context.assets_for(root)}
    if pattern:
        ctx["pattern"] = pattern["name"]
    user = "\n\n".join([
        prompts.data_block("brief", {**brief, "length": "15 to 40 seconds, end card included", "format": "9:16 vertical",
                                     "recent_hooks": writing.recent_hooks(root)}),
        writing.material(ctx),
        ("Write the short in the brief's pattern." if pattern else "Write the short.")
        + (f" Use the id {a.id or base_id!r}." if (a.id or base_id) else ""),
    ])
    request = client.Request(step=step("write_short"), system=prompts.system("write_short"), user=user,
                             schema=client.schema("draft"), ref=base_id or a.brief[:40])
    draft = client.ask(request, root, lambda d: drafts.problems(d, ctx), dry_run=a.dry_run)
    if draft is None:
        return 0
    draft["id"] = a.id if a.id else writing.unique_id(root, draft["id"])
    path, _ = writing.save(root, draft, ctx, f"written {dt.datetime.now():%Y-%m-%d}")
    if pattern:
        patterns.used(root, pattern["name"], draft["id"])
    edit = read_json(path, "edit")
    print(f"write_short: {draft['id']}: {draft['title']} — {len(draft['scenes'])} scenes, "
          f"{timeline.total(edit):.1f}s, {len(draft['script'])} lines")
    print(f"write_short: wrote {path}. Next: translate.py {draft['id']}; voice.py {draft['id']}")
    return 0


if __name__ == "__main__":
    run(main)
