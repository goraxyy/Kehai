#!/usr/bin/env python3
"""Translates an edit's text (on-screen text and the voice-over script) from English (BUILD_PLAN.md, Phase 6).

    uv run translate.py <edit.json | edit folder | id> [--to ru] [--dry-run] [--root DIR]

Text translated before (the same English, in edits/<id>/translations.json) is reused, so a
revision only sends what changed. Spoken lines are checked to fit the time they have; names follow
brand.json (the Latin name on screen, the Russian spelling in speech). When every text has the
language, it's added to the edit's languages. Next: voice.py. Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import re

from km import edits, paths, timeline
from km.cli import BadInput, run
from km.llm import client, prompts
from km.llm.steps import step

LANGS = ("ru",)
CHARS_PER_SECOND = {"ru": 14.0}


def texts(edit: dict) -> list[dict]:
    """Every text object in the edit: where it is, what kind of text it is, and the object itself."""
    found = []
    langs = {"en", *LANGS}

    def visit(node, path: list, kind: str) -> None:
        if isinstance(node, dict):
            if "en" in node and set(node) <= langs and all(isinstance(v, str) for v in node.values()):
                found.append({"path": path, "kind": kind, "text": node})
                return
            for k, v in node.items():
                visit(v, path + [k], kind if k in ("text", "cta") else f"{kind} {k}")
        elif isinstance(node, list):
            for i, v in enumerate(node):
                visit(v, path + [i], kind)

    for i, scene in enumerate(edit["scenes"]):
        visit(scene.get("visual"), ["scenes", i, "visual"], "shot")
        visit(scene.get("pip"), ["scenes", i, "pip"], "picture in picture")
        for j, o in enumerate(scene.get("overlays", [])):
            kind = o["type"] + (f" {o['template']}" if o["type"] == "meme" else "")
            visit(o, ["scenes", i, "overlays", j], kind)
    for i, line in enumerate(edit.get("script", [])):
        who = "the narrator" if line["speaker"] == "narrator" else prompts.names()["karen"]
        found.append({"path": ["script", i, "text"], "kind": f"spoken by {who}", "text": line["text"],
                      "line": line})
    if "title" in edit:
        visit(edit["title"], ["title"], "working title")
    if "endCard" in edit:
        visit(edit["endCard"], ["endCard"], "end card call to action")
    return [f for f in found if isinstance(f["text"], dict)]


def seconds_for(edit: dict, index: int) -> float:
    """The time a spoken line has: until the next line starts, or the last scene ends."""
    lines = edit["script"]
    end = lines[index + 1]["at"] if index + 1 < len(lines) else timeline.scenes_end(edit)
    spoken = next((c for c in edit.get("voice", {}).get("en", []) if c["src"].endswith(f"/{lines[index]['id']}.wav")), None)
    room = end - lines[index]["at"]
    return round(max(room, spoken["duration"] if spoken else 0), 2)


def checks(answer: dict, items: list[dict], lang: str, names: dict) -> list[str]:
    out = []
    by_id = {it["id"]: it for it in items}
    got = [x["id"] for x in answer["items"]]
    for i in by_id:
        if got.count(i) != 1:
            out.append(f"item {i} needs exactly one translation (it has {got.count(i)})")
    for x in answer["items"]:
        it = by_id.get(x["id"])
        if it is None:
            out.append(f"there is no item {x['id']}")
            continue
        t = x["text"].strip()
        if not t:
            out.append(f"item {x['id']}: empty")
            continue
        if t.count("*") != it["en"].count("*"):
            out.append(f"item {x['id']}: keep the *stars* around the same idea ({it['en'].count('*')} in the English)")
        spoken = it["kind"].startswith("spoken")
        if spoken and "seconds" in it:
            need = len(t) / CHARS_PER_SECOND[lang]
            if need > it["seconds"] * 1.15 + 0.3:
                out.append(f"item {x['id']}: about {need:.1f}s to say, but the line has {it['seconds']:.1f}s; say it shorter")
        if names["game"] in it["en"]:
            want = names["game_ru"] if spoken else names["game"]
            if want not in t:
                out.append(f"item {x['id']}: write the game's name as {want} " + ("(it is spoken)" if spoken else "(on screen it stays in Latin letters)"))
        if names["karen"] in it["en"] and names["karen_ru"] not in t:
            out.append(f"item {x['id']}: write her name as {names['karen_ru']}")
        if re.search(r"[A-Za-z]{4,}", t.replace(names["game"], "")) and spoken:
            out.append(f"item {x['id']}: a spoken line should be all {lang} (Latin words are read out badly)")
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("edit")
    ap.add_argument("--to", default="ru", choices=LANGS)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    path = edits.resolve(root, a.edit)
    edit = edits.load(path)
    folder = path.parent
    memory = edits.translations(folder)
    known = memory.get(a.to, {})

    found = texts(edit)
    items, reused = [], 0
    for n, f in enumerate(found):
        if a.to in f["text"]:
            continue
        if f["text"]["en"] in known:
            reused += 1
            continue
        item = {"id": f"t{n + 1}", "en": f["text"]["en"], "kind": f["kind"]}
        if "line" in f:
            item["seconds"] = seconds_for(edit, f["path"][1])
        items.append(item)
    print(f"translate: {edit['id']} → {a.to}: {len(found)} texts, {len(items)} to translate, {reused} known from before")

    if items:
        names = prompts.names()
        request = client.Request(
            step=step("translate"), system=prompts.system("translate"),
            user="\n\n".join([prompts.data_block("job", {"to": a.to, "video": edit["kind"], "format": edit["format"]}),
                              prompts.data_block("items", items), f"Translate every item into {a.to}."]),
            schema=client.schema("translate"), ref=edit["id"])
        answer = client.ask(request, root, lambda ans: checks(ans, items, a.to, names), dry_run=a.dry_run)
        if answer is None:
            return 0
        by_id = {it["id"]: it for it in items}
        for x in answer["items"]:
            known[by_id[x["id"]]["en"]] = x["text"].strip()
    elif a.dry_run:
        print("translate: dry run: nothing to send.")
        return 0

    edits.snapshot(folder, f"translated to {a.to}")
    for f in found:
        if a.to not in f["text"] and f["text"]["en"] in known:
            f["text"][a.to] = known[f["text"]["en"]]
    if all(a.to in f["text"] for f in found) and a.to not in edit["languages"]:
        edit["languages"].append(a.to)
    memory[a.to] = known
    edits.save_translations(folder, memory)
    edits.save(path, edit)
    print(f"translate: wrote {path} (languages: {', '.join(edit['languages'])}). Next: voice.py {edit['id']} --lang {a.to}")
    return 0


if __name__ == "__main__":
    run(main)
