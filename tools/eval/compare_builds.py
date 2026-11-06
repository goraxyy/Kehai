#!/usr/bin/env python3
"""Compare two ablation runs shift by shift: 0.2.0 against main, or the combo learner on and off.

    python compare_builds.py main.jsonl branch.jsonl > compare.md
    python compare_builds.py --labels "learner without" "learner with" off.jsonl on.jsonl

Rows are paired by (rung, player, seed, shift): both runs saw the same seeds. Each line is the
mean of the second run minus the first, ± its standard error, over the pairs where both have the
number. The held-out cautious player is reported on its own, never pooled. The 0.2.0 build plan's
primary metrics (§5) come first. Standard library only.
"""
from __future__ import annotations

import argparse
import sys
from collections import defaultdict

from analyze_ablation import load, mean, se

HELD_OUT = "cautious"

# (key, label, how to read it). A value of None, or -1 for gap_after_loss_m (no losses that
# shift), is "not measured" and leaves the pair out.
PRIMARY = [
    ("gap_after_loss_m", "metres from her to you 5 s after she loses sight in a hunt", "lower: she stays on you"),
    ("karen_fairness_violations", "fairness violations", "must stay 0"),
    ("panic_setpoint_rmse", "setpoint error (panic against the Director's target)", "lower: better pacing"),
]
SECONDARY = [
    ("karen_detections", "detections", ""),
    ("karen_first_detection_s", "first detection (s)", ""),
    ("karen_catches", "catches", ""),
    ("karen_chases", "hunts", ""),
    ("hunt_s", "seconds hunting", ""),
    ("sprint_s", "seconds she runs faster than a hurry", ""),
    ("outside_s", "seconds outside her area", "0.2.0 must be 0"),
    ("far_sightings", "sightings from beyond 18 m", ""),
    ("blind_catches", "catches you never saw coming (5 s)", ""),
    ("catches_after_throw", "catches within 5 s of a throw", ""),
    ("throws", "throws", ""),
    ("throw_hits", "throws that hit", ""),
    ("cuts", "cuts", ""),
    ("cuts_right", "cuts whose spot you passed", ""),
    ("belief_out_of_view_err_m", "belief error out of view (m)", ""),
    ("karen_chains_found", "combos found", ""),
    ("bot_turn_backs", "bot turned back", ""),
    ("bot_dodges", "bot dodged a throw", ""),
]


def value(row, key):
    v = row.get(key)
    if v is None or (key == "gap_after_loss_m" and v < 0) or (key == "karen_first_detection_s" and v < 0):
        return None
    return float(v)


def paired(a_rows, b_rows):
    a = {(r["rung"], r["agent"], r["seed"], r["shift"]): r for r in a_rows}
    return [(a[k], r) for r in b_rows if (k := (r["rung"], r["agent"], r["seed"], r["shift"])) in a]


def table(pairs, metrics, labels, w):
    w(f"| metric | {labels[0]} | {labels[1]} | difference | pairs | |")
    w("|---|---|---|---|---|---|")
    for key, label, note in metrics:
        both = [(value(x, key), value(y, key)) for x, y in pairs]
        both = [(x, y) for x, y in both if x is not None and y is not None]
        if not both:
            w(f"| {label} | — | — | — | 0 | {note} |")
            continue
        xs, ys = [x for x, _ in both], [y for _, y in both]
        d = [y - x for x, y in both]
        spread = f" ± {se(d):.2f}" if len(d) > 1 else ""
        w(f"| {label} | {mean(xs):.2f} | {mean(ys):.2f} | {mean(d):+.2f}{spread} | {len(d)} | {note} |")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("first")
    p.add_argument("second")
    p.add_argument("--labels", nargs=2, default=("main", "0.2.0"))
    args = p.parse_args(argv)

    pairs = paired(load(args.first), load(args.second))
    out = []
    w = out.append
    w(f"# {args.labels[1]} against {args.labels[0]}\n")
    w(f"{len(pairs)} shifts paired by rung, player, seed and shift. Differences are {args.labels[1]} minus "
      f"{args.labels[0]}, ± one standard error.\n")
    if not pairs:
        w("No shifts pair up: were both runs made with the same seeds, rungs and players?")
    groups = defaultdict(list)
    for x, y in pairs:
        groups[(x["rung"], "held out" if x["agent"] == HELD_OUT else "training players")].append((x, y))
    for (rung, who), ps in sorted(groups.items(), key=lambda g: (g[0][0], g[0][1] == "held out")):
        w(f"\n## Rung {rung}, {who} ({len(ps)} shifts)\n")
        w("**Primary** (fixed before the run, build plan §5):\n")
        table(ps, PRIMARY, args.labels, w)
        w("\n**Secondary:**\n")
        table(ps, SECONDARY, args.labels, w)
    print("\n".join(out))


if __name__ == "__main__":
    sys.exit(main())
