#!/usr/bin/env python3
"""Speaks the same lines in several candidate voices, so the owner can pick (Q10; BUILD_PLAN.md, Phase 6).

    uv run voice_samples.py [--backend azure|say] [--role narrator|karen] [--lang en|ru] [--dry-run] [--root DIR]

Writes audio/samples/<lang>-<role>-<voice>.wav for each candidate below and lists them. The
chosen voices then go into brand.json (`voices`). A voice the service doesn't have is reported
and skipped. Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import re

from km import lock, paths, tts
from km.cli import run, write_json
from km.llm import prompts
from km.tts import usage

LINES = {
    ("en", "narrator"): "I sprinted once. She heard it. Now she checks the dairy aisle first.",
    ("en", "karen"): "Employee wellbeing is a tracked metric. I am optimising it.",
    ("ru", "narrator"): "Я пробежал всего один раз. Она это услышала. Теперь она первым делом проверяет молочный отдел.",
    ("ru", "karen"): "Благополучие сотрудников — отслеживаемый показатель. Я его оптимизирую.",
}
# Azure neural voices to compare; the rate and pitch nudges are what brand.json would hold.
CANDIDATES = {
    ("en", "narrator"): [{"azure": "en-US-AndrewNeural"}, {"azure": "en-US-BrianNeural"},
                         {"azure": "en-US-GuyNeural"}, {"azure": "en-GB-RyanNeural"}],
    ("en", "karen"): [{"azure": "en-US-AvaNeural", "rate": "-6%", "pitch": "-3%"},
                     {"azure": "en-US-EmmaNeural", "rate": "-6%", "pitch": "-3%"},
                     {"azure": "en-GB-SoniaNeural", "rate": "-6%"},
                     {"azure": "en-US-AvaMultilingualNeural", "rate": "-8%", "pitch": "-4%"}],
    ("ru", "narrator"): [{"azure": "ru-RU-DmitryNeural"}, {"azure": "en-US-AndrewMultilingualNeural"}],
    ("ru", "karen"): [{"azure": "ru-RU-SvetlanaNeural", "rate": "-6%", "pitch": "-3%"},
                     {"azure": "ru-RU-DariyaNeural", "rate": "-6%", "pitch": "-3%"},
                     {"azure": "en-US-AvaMultilingualNeural", "rate": "-8%", "pitch": "-4%"}],
}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--backend", default="azure", choices=("azure", "say"))
    ap.add_argument("--role", choices=("narrator", "karen"))
    ap.add_argument("--lang", choices=("en", "ru"))
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    brand = prompts.brand()
    jobs = []
    for (lang, role), voices in CANDIDATES.items():
        if (a.lang and lang != a.lang) or (a.role and role != a.role):
            continue
        if a.backend == "say":       # one stand-in per role, to try the script without a key
            voices = [tts.voice_for(brand, lang, role)]
        for v in voices:
            jobs.append((lang, role, v))
    chars = sum(len(LINES[(lang, role)]) for lang, role, _ in jobs)
    print(f"voice_samples: {len(jobs)} samples, {chars:,} characters ({a.backend})")
    if a.dry_run:
        for lang, role, v in jobs:
            print(f"  {lang} {role}: {v.get('azure') if a.backend == 'azure' else v.get('say')}")
        return 0
    usage.check(root, a.backend, chars)
    engine = tts.backend(a.backend)
    made = []
    with lock.heavy(root, "voice samples"):
        for lang, role, v in jobs:
            name = v["azure"] if a.backend == "azure" else v["say"]
            out = root / "audio" / "samples" / f"{lang}-{role}-{re.sub(r'[^A-Za-z0-9-]+', '-', name)}.wav"
            try:
                spoken = engine.speak(LINES[(lang, role)], lang, {**v, "say": v.get("say", "")}, out)
            except tts.TtsError as e:
                print(f"  {lang} {role} {name}: skipped ({e})")
                continue
            usage.record(root, {"time": dt.datetime.now().isoformat(timespec="seconds"),
                                "backend": a.backend, "voice": name, "lang": lang, "chars": len(LINES[(lang, role)]),
                                "seconds": round(spoken.duration, 2), "ref": "samples"})
            made.append({"lang": lang, "role": role, "voice": v, "file": out.relative_to(root).as_posix(),
                         "seconds": round(spoken.duration, 2)})
            print(f"  {lang} {role} {name}: {out} ({spoken.duration:.1f}s)")
    write_json(root / "audio" / "samples" / "samples.json", {"version": 1, "backend": a.backend, "samples": made})
    print(f"voice_samples: {len(made)} made; pick one voice per language and role for brand.json")
    return 0


if __name__ == "__main__":
    run(main)
