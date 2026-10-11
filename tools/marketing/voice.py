#!/usr/bin/env python3
"""Speaks an edit's script into its voice track, in each language (docs/MARKETING.md).

    uv run voice.py <edit.json | edit folder | id> [--lang en,ru] [--backend azure|elevenlabs|say] \\
        [--force] [--dry-run] [--root DIR]

Each line becomes audio/<id>/<lang>/<line>.wav with word timings for the captions; lines already
spoken with the same voice and text are reused. Lines that would overlap move later. The
backend is brand.json's `voices.backend` (azure) unless --backend or KEHAI_TTS_BACKEND (.env)
says otherwise; `say` is the macOS stand-in that needs no key. Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import json

from km import edits, env, paths, voicing
from km.cli import BadInput, run


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("edit")
    ap.add_argument("--lang", help="comma-separated languages (default: the edit's)")
    ap.add_argument("--backend", choices=("azure", "elevenlabs", "say"))
    ap.add_argument("--force", action="store_true", help="speak every line again")
    ap.add_argument("--dry-run", action="store_true", help="say what would be spoken and stop")
    ap.add_argument("--root")
    a = ap.parse_args()

    root = paths.root(a.root)
    path = edits.resolve(root, a.edit)
    edit = edits.load(path)
    if not edit.get("script"):
        raise BadInput(f"{path} has no script to speak")
    brand = json.loads(paths.BRAND.read_text(encoding="utf-8"))
    backend = a.backend or env.get("KEHAI_TTS_BACKEND") or brand["voices"]["backend"]
    langs = a.lang.split(",") if a.lang else edit["languages"]
    unknown = [l for l in langs if l not in edit["languages"]]
    if unknown:
        raise BadInput(f"the edit isn't in {', '.join(unknown)} (its languages: {', '.join(edit['languages'])})")

    plans = [voicing.plan(root, edit, lang, brand, backend, a.force) for lang in langs]
    for p in plans:
        if p.missing:
            raise BadInput(f"lines {', '.join(p.missing)} have no {p.lang} text: run translate.py first")
        print(f"voice: {edit['id']} {p.lang} ({backend}): {len(p.lines)} lines, {len(p.to_speak)} to speak "
              f"({p.chars:,} characters), {len(p.lines) - len(p.to_speak)} already spoken")
    if a.dry_run:
        print("voice: dry run: nothing spoken.")
        return 0

    for p in plans:
        voicing.speak(root, edit, p)
        notes = voicing.apply(root, edit, p)
        clips = edit["voice"][p.lang]
        end = max(c["at"] + c["duration"] for c in clips) if clips else 0
        print(f"voice: {p.lang}: {len(clips)} clips, the voice ends at {end:.2f}s")
        for n in notes:
            print(f"voice: {p.lang}: {n}")
    edits.save(path, edit)
    print(f"voice: wrote {path}")
    return 0


if __name__ == "__main__":
    run(main)
