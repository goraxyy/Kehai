#!/usr/bin/env python3
"""The monthly long video, in stages the owner approves one by one (BUILD_PLAN.md, Phases 6–7).

    uv run long_video.py outline --month 2026-10 [--note "…"] [--dry-run] [--root DIR]   sections, beats, shots to render
    uv run long_video.py script  --month 2026-10 [--note "…"] [--dry-run]                the voice-over, section by section
    uv run long_video.py voice   --month 2026-10 [--backend say] [--dry-run]  speaks it (English), to time the edit
    uv run long_video.py edit    --month 2026-10 [--dry-run]                the edit, timed to the voice

Files: long/<month>/outline.json (with the render_shot.sh runs: render_picks.py renders them),
script.json, voiced.json; the edit is edits/long-<month>/ like any other (translate.py, voice.py,
package.py, revise.py work on it). The outline reads the month's clip moments, CHANGELOG.md and
the month's commits, and the owner's notes in inbox/notes-<month>.md if there are any.
Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import subprocess

from km import context, drafts, moments, paths, voicing, writing
from km.cli import BadInput, read_json, run, write_json
from km.llm import client, prompts
from km.llm.steps import step

WORDS_PER_MINUTE = 150
MAX_MOMENTS = 80


def month_docs(month: str) -> list[dict]:
    start, end = moments.month_range(month)
    return [moments.load(f) for f in moments.find_files(moments.records(), start, end)]


def changes(month: str) -> dict:
    """What changed in the game this month: CHANGELOG.md's entries and the commits' subjects."""
    start, end = moments.month_range(month)
    log = (paths.REPO / "CHANGELOG.md").read_text(encoding="utf-8") if (paths.REPO / "CHANGELOG.md").exists() else ""
    entries = [line.strip("- ").strip() for line in log.splitlines() if re.match(rf"- \*\*{month}-\d\d\*\*", line)]
    try:
        out = subprocess.run(["git", "-C", str(paths.REPO), "log", f"--since={start}", f"--until={end}", "--no-merges",
                              "--format=%ad %s", "--date=short"], capture_output=True, text=True, timeout=30).stdout
        commits = [c for c in out.splitlines() if c.strip()][:120]
    except (OSError, subprocess.SubprocessError):
        commits = []
    return {"changelog": entries, "commits": commits}


def notes(root, month: str) -> str:
    for name in (f"notes-{month}.md", "notes.md"):
        f = root / "inbox" / name
        if f.exists():
            return f.read_text(encoding="utf-8")[:20000]
    return ""


def redo(previous, note: str | None) -> list[str]:
    """The last answer and the owner's note on it, when a stage is done again."""
    if not note:
        return []
    if not previous.exists():
        raise BadInput(f"--note needs an earlier answer to revise, and there's no {previous.name}")
    return [prompts.data_block("previous", read_json(previous, "previous answer")["answer"]),
            prompts.data_block("owner_note", note.strip())]


def folder(root, month: str):
    return root / "long" / month


def edit_id(month: str) -> str:
    return f"long-{month}"


def outline_checks(ans: dict, by_ref: dict) -> list[str]:
    out = []
    if not 3 <= len(ans["title_ideas"]) <= 5:
        out.append(f"{len(ans['title_ideas'])} title ideas; give 3 to 5")
    minutes = sum(s["minutes"] for s in ans["sections"])
    if not 6 <= minutes <= 15:
        out.append(f"the sections add up to {minutes:.1f} minutes; aim for 8 to 12 (6 to 15 at most)")
    shots = [sh for s in ans["sections"] for sh in s["shots"]]
    out += moments.shot_problems(shots, "outline", by_ref)
    if len(shots) > 30:
        out.append(f"{len(shots)} shots; at most 30 (each takes about a minute to render)")
    for s in ans["sections"]:
        if not s["beats"]:
            out.append(f"section {s['name']!r}: needs beats")
    return out


def script_checks(ans: dict, outline: dict) -> list[str]:
    out = []
    want = [s["name"] for s in outline["sections"]]
    got = [s["name"] for s in ans["sections"]]
    if got != want:
        out.append(f"the sections must be the outline's, in order: {want} (got {got})")
    lines = [l for s in ans["sections"] for l in s["lines"]]
    words = sum(drafts.words(l["text"]) for l in lines)
    minutes = sum(s["minutes"] for s in outline["sections"])
    lo, hi = minutes * WORDS_PER_MINUTE * 0.75, minutes * WORDS_PER_MINUTE * 1.15
    if not lo <= words <= hi:
        out.append(f"{words} words for a {minutes:.1f}-minute outline; aim for {int(lo)} to {int(hi)}")
    for i, l in enumerate(lines):
        if not l["text"].strip() or not l["cue"].strip():
            out.append(f"line {i + 1}: needs text and a cue")
        if "*" in l["text"]:
            out.append(f"line {i + 1}: no *stars* in spoken lines")
        if drafts.words(l["text"]) > 45:
            out.append(f"line {i + 1}: {drafts.words(l['text'])} words; split lines longer than 45 words")
    karen = sum(1 for l in lines if l["speaker"] == "karen")
    if lines and karen / len(lines) > 0.2:
        out.append(f"{karen} of {len(lines)} lines are hers; keep her to a few (at most a fifth)")
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("stage", choices=("outline", "script", "voice", "edit"))
    ap.add_argument("--month", default=dt.date.today().strftime("%Y-%m"))
    ap.add_argument("--backend", choices=("azure", "elevenlabs", "say"))
    ap.add_argument("--note", help="outline or script again, with the owner's note on the last one")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    moments.month_range(a.month)
    here = folder(root, a.month)

    if a.stage == "outline":
        docs = month_docs(a.month)
        by_ref = moments.by_ref(docs)
        digest = moments.digest(docs)
        ranked = sorted((m for s in digest for m in s["moments"]), key=lambda m: (not m["kept"], -m["score"]))[:MAX_MOMENTS]
        keep = {m["ref"] for m in ranked}
        for s in digest:
            s["moments"] = [m for m in s["moments"] if m["ref"] in keep]
        earlier = []
        for f in sorted((root / "long").glob("*/outline.json")):
            if f.parent.name != a.month:
                earlier.append(read_json(f, "outline")["answer"]["title_ideas"][0])
        user = "\n\n".join([
            prompts.data_block("month", {"month": a.month, "length": "8 to 12 minutes, 16:9", "earlier_videos": earlier}),
            prompts.data_block("changes", changes(a.month)),
            prompts.data_block("owner_notes", notes(root, a.month) or "(none this month)"),
            prompts.data_block("shifts", digest),
            *redo(here / "outline.json", a.note),
            "Outline this month's long video." + (" Revise the previous outline as the owner's note asks." if a.note else "")])
        request = client.Request(step=step("long_outline"), system=prompts.system("long_outline"), user=user,
                                 schema=client.schema("long_outline"), ref=a.month)
        ans = client.ask(request, root, lambda x: outline_checks(x, by_ref), dry_run=a.dry_run)
        if ans is None:
            return 0
        renders = []
        for s in ans["sections"]:
            for sh in s["shots"]:
                doc, m = by_ref[sh["moment"]]
                ext = "webm" if sh["alpha"] else "mp4"
                renders.append({"section": s["name"], **moments.render_request(doc, m, sh, f"shots/{edit_id(a.month)}/{sh['name']}.{ext}", "16:9")})
        write_json(here / "outline.json", {"version": 1, "month": a.month, "made": dt.datetime.now().isoformat(timespec="seconds"),
                                            "answer": ans, "renders": renders})
        print(f"long_video: outline for {a.month}: {ans['title_ideas'][0]} — {len(ans['sections'])} sections, {len(renders)} shots")
        print(f"long_video: wrote {here / 'outline.json'}. After approval: render_picks.py {here / 'outline.json'}; long_video.py script")
        return 0

    outline = read_json(here / "outline.json", "outline (run the outline stage first)")["answer"]
    if a.stage == "script":
        docs = month_docs(a.month)
        by_ref = moments.by_ref(docs)
        used = {sh["moment"] for s in outline["sections"] for sh in s["shots"]}
        material = {r: {"events": moments.events(by_ref[r][0], by_ref[r][1]["start"], by_ref[r][1]["end"]),
                        "narrator": by_ref[r][1]["captionSeed"]} for r in sorted(used) if r in by_ref}
        user = "\n\n".join([
            prompts.data_block("outline", outline), prompts.data_block("moments", material),
            prompts.data_block("changes", changes(a.month)),
            prompts.data_block("owner_notes", notes(root, a.month) or "(none this month)"),
            *redo(here / "script.json", a.note),
            "Write the voice-over for this outline." + (" Revise the previous script as the owner's note asks." if a.note else "")])
        request = client.Request(step=step("long_script"), system=prompts.system("long_script"), user=user,
                                 schema=client.schema("long_script"), ref=a.month)
        ans = client.ask(request, root, lambda x: script_checks(x, outline), dry_run=a.dry_run)
        if ans is None:
            return 0
        write_json(here / "script.json", {"version": 1, "month": a.month, "made": dt.datetime.now().isoformat(timespec="seconds"), "answer": ans})
        lines = [l for s in ans["sections"] for l in s["lines"]]
        print(f"long_video: script for {a.month}: {len(lines)} lines, {sum(drafts.words(l['text']) for l in lines)} words")
        print(f"long_video: wrote {here / 'script.json'}. After approval: long_video.py voice")
        return 0

    script = read_json(here / "script.json", "script (run the script stage first)")["answer"]
    flat = [{"section": s["name"], **l} for s in script["sections"] for l in s["lines"]]
    ids = drafts.line_ids(flat)
    if a.stage == "voice":
        brand = prompts.brand()
        backend = a.backend or brand["voices"]["backend"]
        pseudo = {"id": edit_id(a.month), "script": [{"id": i, "speaker": l["speaker"], "at": 0, "text": l["text"]} for i, l in ids]}
        p = voicing.plan(root, pseudo, "en", brand, backend)
        print(f"long_video: voice: {len(p.lines)} lines, {len(p.to_speak)} to speak ({p.chars:,} characters, {backend})")
        if a.dry_run:
            print("long_video: dry run: nothing spoken.")
            return 0
        voicing.speak(root, pseudo, p)
        voiced = [{"id": i, "section": l["section"], "speaker": l["speaker"], "text": l["text"], "cue": l["cue"],
                   "seconds": json.loads(voicing.audio_file(root, pseudo["id"], "en", i).with_suffix(".json").read_text())["duration"]}
                  for i, l in ids]
        write_json(here / "voiced.json", {"version": 1, "month": a.month, "backend": backend, "lines": voiced})
        print(f"long_video: the voice-over runs {sum(v['seconds'] for v in voiced) / 60:.1f} minutes spoken; wrote {here / 'voiced.json'}")
        return 0

    voiced = read_json(here / "voiced.json", "voiced lines (run the voice stage first)")["lines"]
    shots = context.shots_in(root, root / "shots" / edit_id(a.month))
    if not shots:
        raise BadInput(f"no rendered shots in shots/{edit_id(a.month)}: render_picks.py {here / 'outline.json'}")
    ctx = {"kind": "long", "format": "16:9", "fps": 30, "source": {"notes": outline["logline"]}, "shots": shots,
           "assets": context.assets_for(root)}
    spoken = {v["text"].strip(): v["seconds"] for v in voiced}

    def checks(d: dict) -> list[str]:
        out = drafts.problems(d, ctx, spoken)
        if d["id"] != edit_id(a.month):
            out.append(f"the id is {edit_id(a.month)!r}")
        got = [(l["speaker"], l["text"].strip()) for l in sorted(d["script"], key=lambda l: l["at"])]
        want = [(v["speaker"], v["text"].strip()) for v in voiced]
        if got != want:
            first = next((i for i, (g, w) in enumerate(zip(got, want)) if g != w), min(len(got), len(want)))
            out.append(f"the script must be the voiced lines word for word, in order ({len(want)} lines); "
                       f"it differs from line {first + 1}")
        return out

    user = "\n\n".join([
        prompts.data_block("voiced_lines", voiced),
        prompts.data_block("outline", {"logline": outline["logline"], "sections": [{k: s[k] for k in ("name", "minutes", "purpose")} for s in outline["sections"]], "cta": outline["cta"]}),
        writing.material(ctx),
        f"Cut the long video. Its id is {edit_id(a.month)!r}."])
    request = client.Request(step=step("long_edit"), system=prompts.system("long_edit"), user=user,
                             schema=client.schema("draft"), ref=a.month)
    draft = client.ask(request, root, checks, dry_run=a.dry_run)
    if draft is None:
        return 0
    path, _ = writing.save(root, draft, ctx, f"long edit {dt.datetime.now():%Y-%m-%d}")
    print(f"long_video: wrote {path}. Next: voice.py {draft['id']} (the lines are spoken already), then render the rough cut")
    return 0


if __name__ == "__main__":
    run(main)
