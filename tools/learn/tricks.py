"""Karen's tricks, read from the game's own C# (Decision/Tactics/*.cs): id, kind, authored prior,
the actions each serves, and whether it's a draft. The authored data lives in C# (CLAUDE.md), so
the Python tools read it from there instead of keeping a copy.
"""
import glob
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DECISION = os.path.join(ROOT, "Assets", "!_Project", "_Game", "AI", "Scripts", "Decision")


def load() -> dict:
    out = {}
    for path in sorted(glob.glob(os.path.join(DECISION, "**", "*.cs"), recursive=True)):
        text = open(path, encoding="utf-8").read()
        for block in re.split(r"\n\s*(?:public |internal )?(?:sealed |abstract )*class ", text)[1:]:
            m = re.search(r'string Id => "([^"]+)"', block)
            if not m:
                continue
            tid = m.group(1)
            attacks = re.search(r'string Attacks => "([^"]*)"', block)
            prior = re.search(r"float PanicPrior => (-?[0-9.]+)f", block)
            actions = re.search(r"ActionId\[\] Actions => new\[\] \{([^}]*)\}", block)
            kind = (attacks.group(1) if attacks else "").replace(",", " ").split()
            out[tid] = {
                "kind": kind[0].lower() if kind else "",
                "prior": float(prior.group(1)) if prior else 0.0,
                "actions": [a.strip().replace("ActionId.", "") for a in actions.group(1).split(",")] if actions else [],
                "draft": "Draft => true" in block,
            }
    return out


def kinds() -> dict:
    return {t: v["kind"] for t, v in load().items()}


def priors() -> dict:
    return {t: v["prior"] for t, v in load().items()}
