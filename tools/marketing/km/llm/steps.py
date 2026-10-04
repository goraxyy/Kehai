"""Each Claude step's model, effort and output room, in one place.

Every step runs on Claude Opus 5.5. Effort is the cost lever: low for mechanical work
(translation, packaging, the report), medium for picking and revising, high for writing.
Opus 5.5's default effort is medium, so it is always set here. `expect` is the output tokens a
call usually takes (thinking included), used for the estimate checked against the monthly cap.
KEHAI_LLM_MODEL overrides the model for every step.
"""
from __future__ import annotations

from dataclasses import dataclass

from .. import env

MODEL = "claude-opus-5-5"


@dataclass(frozen=True)
class Step:
    name: str
    effort: str
    max_tokens: int
    expect: int
    model: str = MODEL

    def resolved_model(self) -> str:
        return env.get("KEHAI_LLM_MODEL") or self.model


STEPS = {s.name: s for s in [
    Step("pick_moments", "medium", 32000, 6000),
    Step("write_short", "high", 32000, 12000),
    Step("translate", "low", 16000, 3000),
    Step("revise", "medium", 32000, 10000),
    Step("long_outline", "high", 32000, 12000),
    Step("long_script", "high", 64000, 20000),
    Step("long_edit", "high", 64000, 24000),
    Step("package", "low", 16000, 3000),
    Step("weekly_report", "low", 16000, 3000),
    Step("playtest_summary", "medium", 16000, 4000),
    Step("study_reference", "medium", 32000, 6000),
]}


def step(name: str) -> Step:
    return STEPS[name]
