#!/usr/bin/env python3
"""Studies a reference video and saves it as a pattern for future shorts (BUILD_PLAN.md, Phase 8).

    uv run study_reference.py <video file> [--note "what I like about it"] [--dry-run] [--root DIR]
    uv run study_reference.py <reference id> [--note "what you got wrong"]     (one Telegram brought in)

The video is read the way Claude can see it (km/watch.py: frames, cuts, loudness) and heard
through Azure speech-to-text when it's set up (km/stt.py); Claude describes it second by second and
writes the recipe for doing the same with the editor (schemas/llm/reference_study.schema.json).
It lands in references/<id>/ (the video, what was read from it, study.json, meta.json) and in the
pattern library (patterns/<name>.json). Studying an id again, with a note, keeps the pattern's name.
Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import shutil
from pathlib import Path

from km import env, patterns, paths, stt, watch
from km.cli import BadInput, read_json, run, write_json
from km.llm import client, prompts
from km.llm.steps import step
from km.media import NotSetUp
from km.references import VIDEO, folder, new_id


def source(here: Path) -> Path:
    found = sorted(p for p in here.glob("source.*") if p.suffix.lower() in VIDEO)
    if not found:
        raise BadInput(f"no source video in {here}")
    return found[0]


def bring_in(root: Path, file: Path, note: str) -> str:
    """A video file from anywhere becomes a reference of its own."""
    if file.suffix.lower() not in VIDEO:
        raise BadInput(f"{file.name} doesn't look like a video ({', '.join(VIDEO)})")
    rid = new_id(root)
    here = folder(root, rid)
    here.mkdir(parents=True)
    shutil.copyfile(file, here / f"source{file.suffix.lower()}")
    write_json(here / "meta.json", {"id": rid, "received": dt.datetime.now().isoformat(timespec="seconds"),
                                    "from": file.name, "note": note, "corrections": []})
    return rid


def checks(answer: dict, seconds: float) -> list[str]:
    out = []
    if not patterns.NAME.fullmatch(answer["name"]):
        out.append("name: 2 to 5 lowercase words joined by dashes")
    beats = answer["beats"]
    if not beats:
        out.append("beats: describe the video from 0 to its end")
    else:
        if beats[0]["from"] > 0.5:
            out.append(f"beats: the first starts at {beats[0]['from']}s; start at 0")
        if abs(beats[-1]["to"] - seconds) > max(1.5, seconds * 0.08):
            out.append(f"beats: the last ends at {beats[-1]['to']}s but the video runs {seconds:.1f}s")
        for a, b in zip(beats, beats[1:]):
            if b["from"] < a["to"] - 0.3 or b["from"] > a["to"] + 0.6:
                out.append(f"beats: {a['from']}–{a['to']}s and {b['from']}–{b['to']}s should meet, in order")
        for b in beats:
            if b["to"] <= b["from"]:
                out.append(f"beats: {b['from']}–{b['to']}s ends before it starts")
    if answer["hook"]["until"] > min(seconds, 8):
        out.append(f"hook: until {answer['hook']['until']}s; a hook is the first few seconds")
    if abs(answer["pacing"]["length"] - seconds) > max(1.5, seconds * 0.1):
        out.append(f"pacing.length is {answer['pacing']['length']}s; the video runs {seconds:.1f}s")
    if not 1 <= len(answer["shots"]) <= 4:
        out.append(f"shots: 1 to 4 replay shots ({len(answer['shots'])} given)")
    if not answer["recipe"]:
        out.append("recipe: say how the editor rebuilds the format")
    return out


def material(here: Path, seen: dict, heard: dict, meta: dict, keep: str | None) -> tuple[str, list[dict]]:
    images = [{"label": f"frame at {f['t']:.1f} s", "path": str(here / f["file"]), "width": f["width"], "height": f["height"]}
              for f in seen["frames"]]
    about = {"length": seen["studied_seconds"], "full_length": seen["seconds"], "size": seen["size"],
             "owner_note": meta.get("note", ""), "owner_corrections": meta.get("corrections", [])}
    parts = [prompts.data_block("reference", about),
             prompts.data_block("cuts", {"at": seen["cuts"], "shots": seen["shots"], "average_shot": seen["average_shot"]})]
    if seen["loudness"]:
        parts.append(prompts.data_block("loudness", seen["loudness"]))
    else:
        parts.append("<loudness>the video has no sound</loudness>")
    parts.append(prompts.data_block("transcript", heard))
    ask = "Study this reference and write its pattern."
    if keep:
        ask += f" It was studied before as {keep!r}: keep that name; fix what the owner's corrections say."
    if seen["seconds"] > seen["studied_seconds"]:
        ask += f" Only its first {seen['studied_seconds']:.0f} s are shown; study those."
    parts.append(ask)
    return "\n\n".join(parts), images


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("video", help="a video file, or the id of a reference already in references/")
    ap.add_argument("--note", help="what you like about it; for an id, what the last study got wrong")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    note = (a.note or "").strip()

    given = Path(a.video).expanduser()
    if given.is_file():
        rid = bring_in(root, given, note)
    elif folder(root, a.video).is_dir():
        rid = a.video
        meta = read_json(folder(root, rid) / "meta.json", "reference")
        if note and note != meta.get("note"):
            meta.setdefault("corrections", []).append(note)
            write_json(folder(root, rid) / "meta.json", meta)
    else:
        raise BadInput(f"{a.video} is neither a video file nor a reference in {root / 'references'}")
    here = folder(root, rid)
    meta = read_json(here / "meta.json", "reference")

    try:
        seen = watch.read(source(here), here)
    except NotSetUp as e:
        raise env.Missing(str(e)) from e
    except watch.VideoError as e:
        raise BadInput(f"can't read {rid}'s video: {e}") from e
    heard_file = here / "transcript.json"
    if heard_file.exists():
        heard = json.loads(heard_file.read_text(encoding="utf-8"))
    elif not seen["audio"]:
        heard = {"none": "the video has no sound"}
    elif a.dry_run:
        heard = {"none": "not transcribed in a dry run"}
    else:
        heard = stt.transcribe(root, here / "audio.wav", rid)
        if "none" not in heard:
            write_json(heard_file, heard)

    keep = meta.get("pattern")
    user, images = material(here, seen, heard, meta, keep)
    request = client.Request(step=step("study_reference"), system=prompts.system("study_reference"), user=user,
                             schema=client.schema("reference_study"), ref=rid, images=images)
    answer = client.ask(request, root, lambda ans: checks(ans, seen["studied_seconds"]), dry_run=a.dry_run)
    if answer is None:
        print(f"study_reference: {rid}: read {len(images)} frames, {len(seen['cuts'])} cuts "
              f"({'transcript' if 'none' not in heard else heard['none']})")
        return 0
    write_json(here / "study.json", answer)
    measured = {"seconds": seen["seconds"], "cuts": seen["cuts"], "shots": seen["shots"], "average_shot": seen["average_shot"]}
    pattern = patterns.save(root, answer, rid, meta.get("note", ""), measured, keep=keep)
    meta["pattern"] = pattern["name"]
    meta["studied"] = dt.datetime.now().isoformat(timespec="seconds")
    write_json(here / "meta.json", meta)
    print(f"study_reference: {rid}: {pattern['title']} — {answer['summary']}")
    if answer["missing"]:
        print(f"study_reference: the editor can't do yet: {'; '.join(m['element'] for m in answer['missing'])}")
    print(f"study_reference: wrote {patterns.path(root, pattern['name'])}")
    return 0


if __name__ == "__main__":
    run(main)
