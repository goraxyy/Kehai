"""What the marketing may claim about the game (facts.json): the facts, the phrases no public text
may use, and a check that each fact's evidence is still in the repository, so a change in the game
shows up as a stale fact instead of a false claim in a video.
"""
from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

from . import paths, schemas

FILE = paths.HERE / "facts.json"
REPO = paths.HERE.parent.parent
# The steps whose answers viewers read.
PUBLIC_STEPS = ("write_short", "revise", "package", "long_outline", "long_script", "long_edit")


@lru_cache(maxsize=1)
def load() -> tuple[dict, ...]:
    doc = json.loads(FILE.read_text(encoding="utf-8"))
    problems = schemas.errors("facts", doc)
    if problems:
        raise ValueError(f"{FILE} isn't a valid fact sheet: {problems[0]}")
    return tuple(doc["facts"])


def _plain(text: str) -> str:
    return text.replace("’", "'").casefold()


def banned_in(text: str) -> list[str]:
    """The fact sheet's `avoid` phrases that this text uses."""
    plain = _plain(text)
    return [phrase for fact in load() for phrase in fact["avoid"] if _plain(phrase) in plain]


def stale(repo: Path = REPO) -> list[str]:
    """The facts whose evidence has gone from the repository."""
    out = []
    for fact in load():
        for proof in fact["evidence"]:
            file = repo / proof["file"]
            if not file.is_file():
                out.append(f"{fact['id']}: {proof['file']} is gone")
            elif proof["contains"] not in file.read_text(encoding="utf-8"):
                out.append(f"{fact['id']}: {proof['file']} no longer says {proof['contains']!r}")
    return out
