"""Claude reads one session (the playtest_summary step): what confused the tester, whether Karen was
too harsh or too soft, what to change, and each bug note rewritten as a neutral public issue.

Runs only with ANTHROPIC_API_KEY set (or KEHAI_LLM_REPLAY, for tests); otherwise the session goes
out without a summary and its bug notes get a plain template. One call per session, priced and
logged like every other step, inside the monthly cap.
"""
from __future__ import annotations

import json
import re
from pathlib import Path

from .. import env
from ..llm import client, prompts
from ..llm.steps import step
from . import session

STEP = "playtest_summary"
VERDICTS = ("too harsh", "about right", "too soft", "unclear")


def ready() -> bool:
    return bool(env.get("ANTHROPIC_API_KEY") or env.get("KEHAI_LLM_REPLAY"))


def system() -> list[dict]:
    """The game reference and the brand facts (not the video style guide), then the step's prompt."""
    shared = "\n\n".join([prompts.text("reference"), prompts.brand_facts()])
    return [
        {"type": "text", "text": shared, "cache_control": {"type": "ephemeral"}},
        {"type": "text", "text": prompts.text(STEP), "cache_control": {"type": "ephemeral"}},
    ]


def checks(answer: dict, facts: dict) -> list[str]:
    problems = []
    if len(answer["headline"]) > 160:
        problems.append("headline: over 160 characters")
    if len(answer["confusions"]) > 6:
        problems.append("confusions: more than six")
    if len(answer["suggestions"]) > 5:
        problems.append("suggestions: more than five")
    wanted = [b["i"] for b in facts["bugs"]]
    if sorted(b["i"] for b in answer["bugs"]) != wanted:
        problems.append(f"bugs: one entry per bug note, with i = {wanted}")
    code = (facts.get("code") or "").lower()
    for b in answer["bugs"]:
        text = (b["title"] + "\n" + b["body"]).lower()
        if code and re.search(rf"\b{re.escape(code)}\b", text):
            problems.append(f"bugs[{b['i']}]: names the tester code; issues are public")
        if re.search(r"https?://|www\.", text):
            problems.append(f"bugs[{b['i']}]: has a link; issues carry none")
        if len(b["title"]) > 100:
            problems.append(f"bugs[{b['i']}]: title over 100 characters")
    return problems


def summarise(marketing: Path, folder: Path, facts: dict, dry_run: bool = False) -> dict | None:
    """Writes <session>/summary.json and returns it; None without a key (or on a dry run)."""
    if not ready():
        return None
    shown = {k: v for k, v in facts.items() if k != "path"}
    request = client.Request(
        step=step(STEP), system=system(),
        user="\n\n".join([prompts.data_block("facts", shown), prompts.data_block("timeline", session.timeline(folder)),
                          "Summarise this session."]),
        schema=client.schema(STEP), ref=f"playtest {facts['round']}/{facts['code']}/{facts['launch']}")
    answer = client.ask(request, marketing, lambda a: checks(a, facts), dry_run=dry_run)
    if answer is None:
        return None
    (folder / "summary.json").write_text(json.dumps(answer, indent=1, ensure_ascii=False), encoding="utf-8")
    return answer


def load(folder: Path) -> dict | None:
    try:
        return json.loads((folder / "summary.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
