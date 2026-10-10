"""The prompts in tools/marketing/prompts/*.md, with the names filled in from brand.json.

`{{game}}`, `{{game_jp}}`, `{{game_ru}}`, `{{karen}}`, `{{karen_jp}}`, `{{karen_ru}}`, `{{studio}}`,
`{{tagline_en}}` and `{{tagline_ru}}` are replaced, so a rename touches brand.json alone. The system prompt is two
cached blocks: style.md + reference.md + the brand facts (the same for every step, so one cache
entry serves a whole run), then the step's own prompt.
"""
from __future__ import annotations

import json
import re
from functools import lru_cache

from .. import paths

PROMPTS = paths.HERE / "prompts"
SHARED = ("style", "reference")


@lru_cache(maxsize=1)
def brand() -> dict:
    return json.loads(paths.BRAND.read_text(encoding="utf-8"))


def names() -> dict[str, str]:
    b = brand()
    return {
        "game": b["game"]["name"], "game_jp": b["game"]["japanese"], "game_ru": b["game"].get("russian", b["game"]["name"]),
        "karen": b["antagonist"]["name"], "karen_jp": b["antagonist"]["japanese"],
        "karen_ru": b["antagonist"].get("russian", b["antagonist"]["name"]),
        "studio": b["studio"],
        "tagline_en": b["game"]["tagline"]["en"], "tagline_ru": b["game"]["tagline"]["ru"],
    }


def fill(text: str) -> str:
    values = names()

    def sub(m: re.Match) -> str:
        key = m.group(1)
        if key not in values:
            raise KeyError(f"unknown prompt placeholder {{{{{key}}}}}")
        return values[key]

    return re.sub(r"\{\{(\w+)\}\}", sub, text)


def text(name: str) -> str:
    return fill((PROMPTS / f"{name}.md").read_text(encoding="utf-8")).strip()


def brand_facts() -> str:
    b = brand()
    handles = {k: v for k, v in b["handles"].items() if v}
    links = {k: v for k, v in b["links"].items() if v}
    lines = ["# Brand facts (from brand.json)", "",
             f"- Studio: {b['studio']}",
             f"- Game: {b['game']['name']} ({b['game']['japanese']}; in Russian speech {b['game'].get('russian', b['game']['name'])})",
             f"- Tagline: {b['game']['tagline']['en']} / {b['game']['tagline']['ru']}",
             f"- Antagonist: {b['antagonist']['name']} ({b['antagonist']['japanese']}; in Russian {b['antagonist'].get('russian', b['antagonist']['name'])}), {b['antagonist']['pronoun']}",
             "- Handles: " + (", ".join(f"{k} {v}" for k, v in sorted(handles.items())) or "none yet: don't invent any"),
             "- Links: " + (", ".join(f"{k} {v}" for k, v in sorted(links.items())) or "none yet: don't invent any")]
    return "\n".join(lines)


def system(step_prompt: str) -> list[dict]:
    shared = "\n\n".join([text(n) for n in SHARED] + [brand_facts()])
    return [
        {"type": "text", "text": shared, "cache_control": {"type": "ephemeral"}},
        {"type": "text", "text": text(step_prompt), "cache_control": {"type": "ephemeral"}},
    ]


def data_block(tag: str, data: object) -> str:
    """Data for the user turn, as compact JSON inside a tag."""
    return f"<{tag}>\n{json.dumps(data, ensure_ascii=False, separators=(',', ':'))}\n</{tag}>"
