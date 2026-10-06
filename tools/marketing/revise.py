#!/usr/bin/env python3
"""Revises a video from the owner's note (the ✏️ reply in Telegram), or undoes the last change
(BUILD_PLAN.md, Phase 6).

    uv run revise.py <edit.json | edit folder | id> --note "make the hook about the blink" [--dry-run] [--root DIR]
    uv run revise.py <edit> --undo

Claude rewrites the draft with the note applied; the old draft and edit are kept as a version, and
--undo puts the last version back. Translations and spoken lines that didn't change are reused,
so after a revision translate.py and voice.py only redo what changed. The video's pattern (if it
follows one), and any library pattern the note names, come with the note. Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt

from km import context, drafts, edits, paths, patterns, timeline, writing
from km.cli import BadInput, read_json, run
from km.llm import client, prompts
from km.llm.steps import step


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("edit")
    ap.add_argument("--note", help="what to change, in the owner's words")
    ap.add_argument("--undo", action="store_true", help="put the previous version back")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    path = edits.resolve(root, a.edit)
    folder = path.parent

    if a.undo:
        if a.dry_run:
            versions = edits.versions(folder)
            print(f"revise: dry run: would restore {versions[-1].name if versions else 'nothing (no versions)'}")
            return 0
        restored = edits.undo(folder)
        print(f"revise: restored {restored.name} ({(restored / 'why.txt').read_text().strip() if (restored / 'why.txt').exists() else ''}); "
              f"voice.py {folder.name} picks its audio back up from the cache")
        return 0
    if not a.note or not a.note.strip():
        raise BadInput("say what to change with --note, or --undo")

    draft = read_json(folder / "draft.json", "draft (only videos written by write_short or long_video can be revised)")
    ctx = read_json(folder / "context.json", "context")
    ctx["assets"] = context.assets_for(root)       # the library may have grown
    edit = edits.load(path)
    named = patterns.named_in(root, a.note)
    own = patterns.load(root, ctx.get("pattern", "")) if ctx.get("pattern") else None
    follow = {p["name"]: p for p in ([own] if own else []) + named}
    if named:
        ctx["pattern"] = named[0]["name"]            # the note asks for this one now
    user = "\n\n".join([
        prompts.data_block("note", a.note.strip()),
        *[prompts.data_block("pattern", patterns.brief(p)) for p in follow.values()],
        prompts.data_block("draft", draft),
        prompts.data_block("video", {"kind": ctx["kind"], "format": ctx["format"],
                                     "length_now": round(timeline.total(edit), 1), "languages": edit["languages"]}),
        writing.material(ctx),
        f"Revise the draft as the note asks. Keep the id {draft['id']!r}.",
    ])
    request = client.Request(step=step("revise"), system=prompts.system("revise"), user=user,
                             schema=client.schema("draft"), ref=draft["id"])

    def checks(d: dict) -> list[str]:
        out = drafts.problems(d, ctx)
        if d["id"] != draft["id"]:
            out.append(f"keep the id {draft['id']!r}")
        return out

    new = client.ask(request, root, checks, dry_run=a.dry_run)
    if new is None:
        return 0
    saved, missing = writing.save(root, new, ctx, f"revised {dt.datetime.now():%Y-%m-%d %H:%M}: {a.note.strip()[:200]}")
    for p in named:
        patterns.used(root, p["name"], new["id"])
    old_lines = {l["text"] for l in draft["script"]}
    changed = sum(1 for l in new["script"] if l["text"] not in old_lines)
    print(f"revise: {new['id']}: {len(new['scenes'])} scenes, {timeline.total(read_json(saved, 'edit')):.1f}s; "
          f"{changed} of {len(new['script'])} lines new")
    if missing:
        print(f"revise: needs translating again: translate.py {new['id']} --to {' '.join(sorted(missing))}")
    print(f"revise: wrote {saved} (the previous version is in versions/; --undo puts it back). Next: voice.py {new['id']}")
    return 0


if __name__ == "__main__":
    run(main)
