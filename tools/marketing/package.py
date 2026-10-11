#!/usr/bin/env python3
"""Writes what goes with a video when it's posted: titles, descriptions, captions, hashtags, a
pinned comment, per platform and language (docs/MARKETING.md).

    uv run package.py <edit.json | edit folder | id> [--dry-run] [--root DIR]

Saved as edits/<id>/package.json. Lengths are checked against each platform's limit, and nothing
may name a handle or link that brand.json doesn't have. Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import copy
import datetime as dt
import json
import re

from km import edits, paths, timeline
from km.cli import read_json, run, write_json
from km.llm import client, prompts
from km.llm.steps import step

LIMITS = {"youtube.title": 100, "youtube.description": 5000, "instagram.caption": 2200, "tiktok.caption": 2200,
          "x": 280, "bluesky": 300, "pinned_comment": 500}
MAX_HASHTAGS = {"instagram": 30, "tiktok": 8}
NAME_MEANING = re.compile(r"(name|имя|имени)[^.!?\n]{0,40}(means|meaning|stands for|означа|значит|переводится)", re.I)


def answer_schema(langs: list[str]) -> dict:
    block = client.schema("package")
    block.pop("description", None)
    return {"type": "object", "additionalProperties": False, "required": langs,
            "properties": {lang: copy.deepcopy(block) for lang in langs}}


def _posted_text(block: dict, platform: str) -> str:
    if platform == "instagram":
        return block["instagram"]["caption"] + " " + " ".join(block["instagram"]["hashtags"])
    if platform == "tiktok":
        return block["tiktok"]["caption"] + " " + " ".join(block["tiktok"]["hashtags"])
    if platform == "youtube":
        return block["youtube"]["title"] + "\n" + block["youtube"]["description"]
    return block[platform]


def checks(answer: dict, langs: list[str], kind: str, brand: dict) -> list[str]:
    out = []
    handles = {v.lower() for v in brand["handles"].values() if v}
    links = [v for v in brand["links"].values() if v]
    for lang in langs:
        b = answer[lang]
        for key, limit in LIMITS.items():
            value = b
            for part in key.split("."):
                value = value[part]
            if not value.strip() and key not in ("pinned_comment",):
                out.append(f"{lang} {key}: empty")
            if len(value) > limit:
                out.append(f"{lang} {key}: {len(value)} characters; at most {limit}")
        for platform in ("instagram", "tiktok"):
            tags = b[platform]["hashtags"]
            if len(tags) > MAX_HASHTAGS[platform]:
                out.append(f"{lang} {platform}: {len(tags)} hashtags; at most {MAX_HASHTAGS[platform]}")
            bad = [t for t in tags if not re.fullmatch(r"#[\w]+", t)]
            if bad:
                out.append(f"{lang} {platform}: hashtags are '#' and one word: {', '.join(bad[:5])}")
            combined = len(b[platform]["caption"]) + sum(len(t) + 1 for t in tags)
            if combined > 2200:
                out.append(f"{lang} {platform}: caption and hashtags come to {combined} characters; at most 2200")
        if any(t.startswith("#") for t in b["youtube"]["tags"]):
            out.append(f"{lang} youtube tags: no '#' in tags")
        if sum(len(t) + 1 for t in b["youtube"]["tags"]) > 500:
            out.append(f"{lang} youtube tags: over 500 characters together")
        if kind == "short" and b["thumbnail_text"]:
            out.append(f"{lang} thumbnail_text: '' for a short")
        if kind == "long" and not (2 <= len(b["thumbnail_text"].split()) <= 5):
            out.append(f"{lang} thumbnail_text: 2 to 5 words for a long video")
        everything = json.dumps(b, ensure_ascii=False)
        for h in re.findall(r"(?<![\w@])@([A-Za-z0-9_.]{2,30})", everything):
            if h.lower() not in handles:
                out.append(f"{lang}: @{h} isn't one of our handles (brand.json has {', '.join(sorted(handles)) or 'none yet'}); don't name accounts")
        for url in re.findall(r"https?://[^\s\"]+", everything):
            if not any(url.startswith(l) for l in links):
                out.append(f"{lang}: {url} isn't one of our links (brand.json has {', '.join(links) or 'none yet'})")
        if NAME_MEANING.search(everything):
            out.append(f"{lang}: never explain what her name means")
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("edit")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    path = edits.resolve(root, a.edit)
    edit = edits.load(path)
    folder = path.parent
    draft = read_json(folder / "draft.json", "draft") if (folder / "draft.json").exists() else {}
    brand = prompts.brand()
    langs = edit["languages"]

    def text(t) -> str:
        return t if isinstance(t, str) else t.get("en", "")

    video = {
        "kind": edit["kind"], "format": edit["format"], "seconds": round(timeline.total(edit), 1), "languages": langs,
        "title": text(edit.get("title", "")), "angle": (edit.get("source") or {}).get("notes", ""),
        "hook": draft.get("hook", ""),
        "on_screen": [text(o.get("text") or o.get("title") or o.get("top") or "") for s in edit["scenes"]
                      for o in s.get("overlays", []) if o["type"] in ("hook", "label", "lowerThird", "meme")],
        "script": {lang: [t.get(lang, "") if isinstance(t := l["text"], dict) else t for l in edit.get("script", [])] for lang in langs},
        "end_card_cta": text((edit.get("endCard") or {}).get("cta", "")),
    }
    request = client.Request(
        step=step("package"), system=prompts.system("package"),
        user="\n\n".join([prompts.data_block("video", video),
                          f"Write the posting text in {', '.join(langs)}."]),
        schema=answer_schema(langs), ref=edit["id"])
    answer = client.ask(request, root, lambda ans: checks(ans, langs, edit["kind"], brand), dry_run=a.dry_run)
    if answer is None:
        return 0
    out = folder / "package.json"
    write_json(out, {"version": 1, "id": edit["id"], "made": dt.datetime.now().isoformat(timespec="seconds"), "languages": answer})
    for lang in langs:
        print(f"package: {lang}: {answer[lang]['youtube']['title']}")
    print(f"package: wrote {out}")
    return 0


if __name__ == "__main__":
    run(main)
