#!/usr/bin/env python3
"""The publish gate (0.4.0, build plan §6.5, C3.7): a candidate pack goes out only if it beats the
current one on the held-out players, and passes every check (no yes per model: the owner, 12 November).

    python3 tools/learn/publish.py gate candidate_eval.json current_eval.json
    python3 tools/learn/publish.py stage candidate.kpack candidate_eval.json current_eval.json [--to tools/learn/packs]

An evaluation file is what the held-out test runs measure for one pack:
    {"metrics": {"<name>": {"higher": true, "by_career": {"<career>": value, …}}, …},
     "fairness_violations": 0, "stuck_shifts": 1, "basic_catches_per_shift": 0.27}
Careers are paired by their id (same seed, same persona), so each metric's difference has a
standard error. A candidate passes only if:
    no metric is worse by more than one standard error;
    at least one is better by more than two;
    fairness violations are 0, and stuck shifts are no more than the current pack's;
    Basic's catch rate is inside your target, 0.20–0.35 catches a shift (Q2).
`stage` writes a passing pack into a local folder as the next version and points "current" at it
there. Nothing is uploaded: the server and --publish / --rollback are Phase 7.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import sys

CATCH_TARGET = (0.20, 0.35)


def paired(cand: dict, cur: dict) -> tuple:
    """Mean of (candidate − current) over shared careers, its standard error, and the count."""
    keys = sorted(set(cand) & set(cur))
    d = [float(cand[k]) - float(cur[k]) for k in keys]
    if len(d) < 2:
        return float("nan"), float("nan"), len(d)
    m = sum(d) / len(d)
    se = math.sqrt(sum((x - m) ** 2 for x in d) / (len(d) - 1) / len(d))
    return m, se, len(d)


def gate(candidate: dict, current: dict) -> tuple:
    """(passed, lines): every rule, with its numbers."""
    lines, ok, better = [], True, False
    for name, spec in sorted(candidate["metrics"].items()):
        if name not in current["metrics"]:
            lines.append(f"{name}: no current number to compare with (counts as not better)")
            continue
        sign = 1.0 if spec.get("higher", True) else -1.0
        m, se, n = paired(spec["by_career"], current["metrics"][name]["by_career"])
        if math.isnan(m) or n < 2:
            lines.append(f"{name}: too few paired careers ({n})")
            ok = False
            continue
        gain = sign * m
        se = max(se, 1e-12)
        if gain < -se:
            ok = False
            lines.append(f"{name}: worse by {-gain:.4f} (SE {se:.4f}, {n} careers) — more than one standard error")
        else:
            lines.append(f"{name}: {'+' if gain >= 0 else ''}{gain:.4f} (SE {se:.4f}, {n} careers)")
        if gain > 2 * se:
            better = True
    if not better:
        ok = False
        lines.append("no metric is better by more than two standard errors")
    if candidate.get("fairness_violations", 0) != 0:
        ok = False
        lines.append(f"fairness violations: {candidate['fairness_violations']} (must be 0)")
    if candidate.get("stuck_shifts", 0) > current.get("stuck_shifts", 0):
        ok = False
        lines.append(f"stuck shifts: {candidate['stuck_shifts']} against {current.get('stuck_shifts', 0)}")
    c = candidate.get("basic_catches_per_shift")
    if c is not None and not (CATCH_TARGET[0] <= c <= CATCH_TARGET[1]):
        ok = False
        lines.append(f"Basic's catches a shift: {c:.2f}, outside {CATCH_TARGET[0]}–{CATCH_TARGET[1]}")
    return ok, lines


def stage(pack_path: str, candidate: dict, current: dict, folder: str) -> str:
    ok, lines = gate(candidate, current)
    if not ok:
        raise SystemExit("publish: the gate refused it; kept as a report:\n  " + "\n  ".join(lines))
    os.makedirs(folder, exist_ok=True)
    versions = [int(f.split(".")[0]) for f in os.listdir(folder) if f.endswith(".kpack") and f.split(".")[0].isdigit()]
    n = max(versions, default=0) + 1
    target = os.path.join(folder, f"{n}.kpack")
    shutil.copyfile(pack_path, target)
    with open(os.path.join(folder, "current"), "w") as f:
        f.write(f"{n}\n")
    with open(os.path.join(folder, f"{n}.gate.txt"), "w") as f:
        f.write("\n".join(lines) + "\n")
    return target


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("gate")
    g.add_argument("candidate")
    g.add_argument("current")
    s = sub.add_parser("stage")
    s.add_argument("pack")
    s.add_argument("candidate")
    s.add_argument("current")
    s.add_argument("--to", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "packs"))
    for flag in ("--publish", "--rollback"):
        sub.add_parser(flag.strip("-"))
    a = ap.parse_args(argv)
    if a.cmd in ("publish", "rollback"):
        sys.exit("publish: the server side is Phase 7 (after playtest v2)")
    cand, cur = json.load(open(a.candidate)), json.load(open(a.current))
    if a.cmd == "gate":
        ok, lines = gate(cand, cur)
        print(("PASS" if ok else "REFUSED") + "\n  " + "\n  ".join(lines))
        return 0 if ok else 1
    print("staged " + stage(a.pack, cand, cur, a.to))
    return 0


if __name__ == "__main__":
    sys.exit(main())
