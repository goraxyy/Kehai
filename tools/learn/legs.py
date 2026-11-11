#!/usr/bin/env python3
"""Legs (0.4.0, build plan §8.5, C4.3): one walk from a stop to the next, with where it ended.

    python3 tools/learn/legs.py <shift records folder or files>… [--out legs.jsonl]

A bot's destination is known exactly: every choice it makes is logged with where it leads (the
shift record's "choice" events). A person's is read off their track: it's cut at the jobs they
finish and at long stops (still or crouched for 3 s or more), so a person's leg is a guess.

Each leg: player, shift, t0, t1, from [x, z], to [x, z], what (a bot's choice, or "stop"/a job's
name), metres walked, mean speed, and how it was found ("logged" or "track").
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import records  # noqa: E402

STOP_SECONDS = 3.0
MIN_METRES = 1.5


def track(s: records.Shift):
    return list(zip(s.col("t"), s.col("px"), s.col("pz"), s.col("motion")))


def walked(points, t0, t1):
    seg = [p for p in points if t0 <= p[0] <= t1]
    return sum(math.hypot(b[1] - a[1], b[2] - a[2]) for a, b in zip(seg, seg[1:])), seg


def at(points, t):
    best = min(points, key=lambda p: abs(p[0] - t)) if points else (t, 0.0, 0.0, 0)
    return [best[1], best[2]]


def from_choices(s: records.Shift) -> list:
    pts = track(s)
    cs = s.choices
    legs = []
    for i, c in enumerate(cs):
        t1 = cs[i + 1].t if i + 1 < len(cs) else (pts[-1][0] if pts else c.t)
        metres, _ = walked(pts, c.t, t1)
        legs.append({"player": s.player, "shift": s.shift, "t0": c.t, "t1": t1, "from": at(pts, c.t), "to": [c.x, c.z],
                     "what": c.what, "how": c.how, "ok": c.ok, "metres": metres, "speed": metres / max(1e-6, t1 - c.t), "found": "logged"})
    return legs


def from_track(s: records.Shift) -> list:
    """A person's legs: cut where a job was done, and at stops of STOP_SECONDS or more."""
    pts = track(s)
    if not pts:
        return []
    cuts = sorted({t for t, _ in s.jobs})
    still_since = None
    for t, x, z, m in pts:
        if m in (0, 1):
            still_since = t if still_since is None else still_since
            if t - still_since >= STOP_SECONDS:
                cuts.append(still_since)
                still_since = t + 1e9          # one cut per stop
        else:
            still_since = None
    cuts = sorted(set(round(c, 2) for c in cuts))
    legs, start = [], pts[0][0]
    jobs = dict((round(t, 2), w) for t, w in s.jobs)
    for c in cuts + [pts[-1][0]]:
        metres, _ = walked(pts, start, c)
        if metres >= MIN_METRES and c > start:
            legs.append({"player": s.player, "shift": s.shift, "t0": start, "t1": c, "from": at(pts, start), "to": at(pts, c),
                         "what": jobs.get(round(c, 2), "stop"), "how": "", "ok": True, "metres": metres,
                         "speed": metres / (c - start), "found": "track"})
        start = c
    return legs


def legs(shifts) -> list:
    out = []
    for s in shifts:
        out += from_choices(s) if s.is_bot and s.choices else from_track(s)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--out")
    a = ap.parse_args(argv)
    ls = legs(records.load_all(a.paths))
    if a.out:
        with open(a.out, "w") as f:
            for leg in ls:
                f.write(json.dumps(leg) + "\n")
    print(f"{len(ls)} legs ({sum(l['found'] == 'logged' for l in ls)} logged, {sum(l['found'] == 'track' for l in ls)} from tracks)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
