#!/usr/bin/env python3
"""Renders the shots a plan asks for, one at a time, through render_shot.sh (docs/MARKETING.md).

    uv run render_picks.py <plans/<week>/picks.json | long/<month>/outline.json> [--only NAME] [--dry-run] [--root DIR]

Shots already rendered (the video and its .json beside it) are skipped. Unity renders only
while the editor is closed: render_shot.sh refuses otherwise (exit 3), and so does this, keeping
what was done. Each render waits up to KEHAI_LOCK_WAIT seconds (default 600 here) for the
heavy-job lock. Exit codes: see km/cli.py; render_shot.sh's 2 and 3 come through as they are.
"""
from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path

from km import paths
from km.cli import BadInput, read_json, run

SCRIPT = paths.HERE / "render_shot.sh"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("plan", type=Path)
    ap.add_argument("--only", help="one pick (or outline section shot) by name")
    ap.add_argument("--dry-run", action="store_true", help="list what would render")
    ap.add_argument("--root")
    a = ap.parse_args()

    root = paths.root(a.root)
    plan = read_json(a.plan, "plan")
    renders = plan.get("renders")
    if renders is None:
        raise BadInput(f"{a.plan} has no renders")
    todo = [r for r in renders if not a.only or a.only in (r.get("pick"), r["name"])]
    env = {**os.environ, "KEHAI_MARKETING": str(root), "KEHAI_LOCK_WAIT": os.environ.get("KEHAI_LOCK_WAIT", "600")}
    done = skipped = 0
    for r in todo:
        out = root / r["out"]
        if out.exists() and out.with_suffix(".json").exists():
            skipped += 1
            continue
        args = [x if x != r["out"] else str(out) for x in r["args"]]
        label = f"{r.get('pick', '')}/{r['name']}".strip("/")
        if a.dry_run:
            print(f"render_picks: would render {label}: {r['moment']} {r['from']}–{r['to']}s → {r['out']}")
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        print(f"render_picks: rendering {label} ({r['to'] - r['from']:.1f}s) → {r['out']}", flush=True)
        code = subprocess.run([str(SCRIPT), *args], env=env).returncode
        if code != 0:
            print(f"render_picks: render_shot.sh stopped with {code} at {label}; {done} rendered, {skipped} already there")
            return code
        done += 1
    print(f"render_picks: {done} rendered, {skipped} already there" + (" (dry run)" if a.dry_run else ""))
    return 0


if __name__ == "__main__":
    run(main)
