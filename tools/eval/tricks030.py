#!/usr/bin/env python3
"""Karen 0.3.0's ablation: what compare_builds.py doesn't cover (build plan §7).

    python tricks030.py branch.jsonl [main.jsonl] > tricks030.md

Primary, per build: fairness violations (must be 0) and stuck shifts (timed out instead of
clocked out or caught), each stuck shift with what was still open and what she did after the
doors closed. The setpoint error against main is compare_builds.py's. Then 0.3.0's
own measurements, which main doesn't record: catches in her fog and at a chore she left, how
often each trick ran, how long each chore took to answer, how long her cameras saw you, and
whether what her places paid rose between a career's first and last shifts. The held-out
cautious player is always reported on its own. Standard library only.
"""
from __future__ import annotations

import argparse
import sys
from collections import defaultdict

from analyze_ablation import load, mean, se

HELD_OUT = "cautious"
TRICKS = ["poll_witnesses", "shelf_sweep", "cut", "phantom_chime", "spill", "bin_tamper", "ambush",
          "throw_flour", "throw_oil", "throw_tin", "tool_theft", "fog", "door_lock", "camera_bolt_on",
          "blackout", "understudy", "overtime"]
ANSWERS = ["fog_reset", "clock_restarted", "mop_returned", "box_moved", "understudy_sent_away"]


def num(row, key):
    v = row.get(key)
    return None if v is None else float(v)


def fmt(xs, digits=2):
    xs = [x for x in xs if x is not None]
    if not xs:
        return "—"
    spread = f" ± {se(xs):.{digits}f}" if len(xs) > 1 else ""
    return f"{mean(xs):.{digits}f}{spread}"


def groups(rows):
    g = defaultdict(list)
    for r in rows:
        g[(r["rung"], "held out" if r["agent"] == HELD_OUT else "training players")].append(r)
    return sorted(g.items(), key=lambda kv: (kv[0][0], kv[0][1] == "held out"))


def primary(rows, label, w):
    w(f"\n### {label}: {len(rows)} shifts\n")
    w("| rung | players | shifts | fairness violations | stuck (timed out) | clocked out | caught |")
    w("|---|---|---|---|---|---|---|")
    for (rung, who), rs in groups(rows):
        fair = sum(int(r.get("karen_fairness_violations") or 0) for r in rs)
        stuck = sum(1 for r in rs if r.get("timed_out"))
        out = sum(1 for r in rs if r.get("clocked_out"))
        caught = sum(1 for r in rs if (r.get("karen_catches") or 0) > 0)
        w(f"| {rung} | {who} | {len(rs)} | {fair} | {stuck} | {out} | {caught} |")


def stuck(rows, label, w):
    """Each shift that timed out: what was still open, and what she did after the doors closed.
    Runs recorded before these fields existed show dashes."""
    rs = [r for r in rows if r.get("timed_out")]
    if not rs:
        return
    w(f"\n### {label}: the {len(rs)} stuck shifts\n")
    w("| rung | player | seed | shift | doors closed at (s) | still open at the end | her tricks after closing | the bot's failed actions (and why, when recorded) |")
    w("|---|---|---|---|---|---|---|---|")

    def show(d):
        if d is None:
            return "—"
        return ", ".join(f"{k} {v}" for k, v in sorted(d.items(), key=lambda kv: -kv[1])) or "nothing"

    for r in sorted(rs, key=lambda r: (r["rung"], r["agent"] == HELD_OUT, r["agent"], int(r["seed"]), int(r["shift"]))):
        closed = num(r, "closed_at_s")
        closed = "—" if closed is None or closed < 0 else f"{closed:.0f}"
        who = r["agent"] + (" (held out)" if r["agent"] == HELD_OUT else "")
        w(f"| {r['rung']} | {who} | {r['seed']} | {r['shift']} | {closed} | {show(r.get('open_at_end'))} | "
          f"{show(r.get('tricks_after_close'))} | {show(r.get('failed_why') or r.get('verbs_failed'))} |")


def secondary(rows, w):
    for (rung, who), rs in groups(rows):
        w(f"\n### Rung {rung}, {who} ({len(rs)} shifts)\n")
        w("| measurement | per shift (± standard error) |")
        w("|---|---|")
        for key, label in [("karen_catches", "catches"), ("catches_in_fog", "catches while a room was fogged"),
                           ("catches_at_chore", "catches while Ambush waited by a chore"),
                           ("camera_seen_s", "seconds her cameras saw you"),
                           ("panic_setpoint_rmse", "setpoint error")]:
            w(f"| {label} | {fmt([num(r, key) for r in rs])} |")

        w("\n| trick | started per shift | landed per shift | shifts it ran in |")
        w("|---|---|---|---|")
        for t in TRICKS:
            uses = [num(r, "uses_" + t) for r in rs if num(r, "uses_" + t) is not None]
            if not uses:
                continue
            landed = [num(r, "landed_" + t) for r in rs if num(r, "landed_" + t) is not None]
            ran = sum(1 for u in uses if u > 0)
            w(f"| {t} | {mean(uses):.2f} | {fmt(landed) if landed else '—'} | {ran} of {len(uses)} |")

        w("\n| answer | seconds to answer | times |")
        w("|---|---|---|")
        for a in ANSWERS:
            xs = [num(r, f"answer_{a}_s") for r in rs if num(r, f"answer_{a}_s") is not None]
            w(f"| {a} | {fmt(xs, 0)} | {len(xs)} |")

        # Placement memory: what her places paid, early in a career against late (rung E keeps it).
        early = [num(r, "place_reward_mean") for r in rs if int(r["shift"]) <= 3]
        late = [num(r, "place_reward_mean") for r in rs if int(r["shift"]) >= 8]
        early = [x for x in early if x is not None and x >= 0]
        late = [x for x in late if x is not None and x >= 0]
        w(f"\nWhat her tricks' places paid (0–1): shifts 1–3 {fmt(early)} ({len(early)} shifts), "
          f"shifts 8–10 {fmt(late)} ({len(late)} shifts). Fewer tricks are unlocked early, so the two "
          "aren't the same mix of tricks.")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("branch")
    p.add_argument("main", nargs="?")
    args = p.parse_args(argv)
    out = []
    w = out.append
    branch = load(args.branch)
    w("# Karen 0.3.0's ablation: the plan's own measurements\n")
    w("## Primary (fixed before the run, build plan §7)\n")
    w("Every live trick end to end is the trick check's (`-kehai-trick-check`); setpoint error against "
      "`main` is `compare_builds.py`'s. Here: fairness violations and stuck shifts.")
    primary(branch, "0.3.0", w)
    main_rows = load(args.main) if args.main else None
    if main_rows is not None:
        primary(main_rows, "main", w)
    stuck(branch, "0.3.0", w)
    if main_rows is not None:
        stuck(main_rows, "main", w)
    w("\n## Secondary: 0.3.0 alone (main doesn't record these)")
    secondary(branch, w)
    print("\n".join(out))


if __name__ == "__main__":
    sys.exit(main())
