#!/usr/bin/env python3
"""The starting Ledger: "everyone" (0.4.0, build plan §6.1, C3.2).

    python3 tools/learn/fit_start.py <dataset> [--split training] [--out <Generated folder>]
    python3 tools/learn/fit_start.py <dataset> --check          # PR #76's offline check, on held-out players

From the dataset's training players it fits what each trick and each kind of trick does to people
on average (Δpanic over the 20 s after it, from the learning records), by the Director's phase;
how soon people answer each of her chores; where people hide; how much they sprint. One player,
one vote: each player's own mean first, then the mean over players.

Writes LedgerStart.g.cs (class Kehai.Karen.Generated.LedgerStartTable), LedgerStart.card.md and
LedgerStart.part.json (for the model pack). Fitting twice from the same records gives the same
files, byte for byte: no clock, no randomness, keys sorted.

--check: does the fitted start predict held-out players' reactions better than the hand-set guesses
(each trick's PanicPrior, read from the learning records' own "everyone" when absent)? It reports
the mean squared error of both, one player one vote.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dataset as ds  # noqa: E402
import tricks  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
GENERATED = os.path.join(HERE, "..", "..", "Assets", "!_Project", "_Game", "AI", "Scripts", "Core", "Generated")
PHASES = 6                      # KarenDirector.Phase: OffShift, Settle, Build, Spike, Recover, Crunch
MIN_PLAYERS = 1                 # a number needs this many players behind it


def trick_of(pick: str) -> str:
    return pick.split(":", 1)[1] if ":" in pick else pick


def player_means(recs: list, kinds: dict) -> dict:
    """One player's mean Δpanic per (trick, phase) and (kind, phase); phase -1 is all phases.
    Windows where the Director's hint fired are left out (§6.4 rule 4)."""
    sums = defaultdict(lambda: [0.0, 0])
    for r in recs:
        d = r.get("d_panic_20")
        if d is None or r.get("hint"):
            continue
        t = trick_of(r.get("pick", ""))
        k = kinds.get(t, "")
        ph = int(r.get("phase", -1))
        for key in (("trick", t, -1), ("trick", t, ph), ("kind", k, -1), ("kind", k, ph)):
            if key[1] and (key[2] >= -1):
                s = sums[key]
                s[0] += float(d)
                s[1] += 1
    return {k: v[0] / v[1] for k, v in sums.items() if v[1] > 0}


def fit(dataset: str, side: str = "training", kinds: dict | None = None) -> dict:
    kinds = kinds or {}
    splits = json.load(open(os.path.join(dataset, "splits.json"))) if os.path.exists(os.path.join(dataset, "splits.json")) else None
    pids = splits[side] if splits else [p for p, _ in ds.players(dataset)]
    per_player, answers, hides, sprint = [], defaultdict(list), defaultdict(float), []
    for pid in sorted(pids):
        recs = ds.learning(dataset, pid)
        if recs:
            per_player.append(player_means(recs, kinds))
        shifts = ds.shifts(dataset, pid)
        mine = defaultdict(list)
        for s in shifts:
            for a in s.answers:
                if isinstance(a, dict) and a.get("seconds", -1) >= 0:
                    mine[a["kind"]].append(float(a["seconds"]))
        for k, v in mine.items():
            answers[k].append(sum(v) / len(v))
        hid = defaultdict(int)
        total = 0
        for s in shifts:
            for c in s.choices:
                if c.what == "Hide" and c.target:
                    hid[c.target] += 1
                    total += 1
        for region, n in hid.items():
            hides[region] += n / total / max(1, len(pids))       # one player, one vote
        motion = [m for s in shifts for m in s.col("motion")]
        if motion:
            sprint.append(sum(1 for m in motion if m == 3) / len(motion))

    table = {"tricks": {}, "kinds": {}}
    for what in ("trick", "kind"):
        keys = sorted({k[1] for p in per_player for k in p if k[0] == what})
        for key in keys:
            row = []
            for ph in range(-1, PHASES):
                vals = [p[(what, key, ph)] for p in per_player if (what, key, ph) in p]
                row.append(round(sum(vals) / len(vals), 6) if len(vals) >= MIN_PLAYERS else None)
            table["tricks" if what == "trick" else "kinds"][key] = row
    return {
        "kind": "start", "name": "LedgerStart",
        "tricks": table["tricks"], "kinds": table["kinds"],
        "answers": {k: round(sum(v) / len(v), 3) for k, v in sorted(answers.items())},
        "hides": [[k, round(v, 6)] for k, v in sorted(hides.items(), key=lambda kv: (-kv[1], kv[0]))[:12]],
        "sprint_share": round(sum(sprint) / len(sprint), 6) if sprint else 0.2,
        "players": len(pids),
    }


def fingerprint(dataset: str, side: str) -> str:
    """A hash of what the fit read: the card names its data without a clock."""
    h = hashlib.sha256()
    splits = json.load(open(os.path.join(dataset, "splits.json"))) if os.path.exists(os.path.join(dataset, "splits.json")) else None
    for pid in sorted(splits[side] if splits else [p for p, _ in ds.players(dataset)]):
        h.update(pid.encode())
        for r in ds.learning(dataset, pid):
            h.update(json.dumps(r, sort_keys=True).encode())
    return h.hexdigest()[:16]


def cs_float(v) -> str:
    return "float.NaN" if v is None else repr(float(v)) + "f"


def write(start: dict, out: str, version: str) -> None:
    os.makedirs(out, exist_ok=True)
    start = dict(start, card={"version": version, "trained": True, "players": start["players"]})
    lines = ["// <auto-generated> by tools/learn/fit_start.py — do not edit; refit instead.",
             f"// The starting Ledger, {version}. Its card: LedgerStart.card.md",
             "namespace Kehai.Karen.Generated", "{", "    public static class LedgerStartTable", "    {",
             "        public static Kehai.Karen.LedgerStart Build()", "        {",
             f"            var s = new Kehai.Karen.LedgerStart {{ Version = \"{version}\", Fitted = true, SprintShare = {cs_float(start['sprint_share'])} }};"]
    for what, setter in (("tricks", "SetTrick"), ("kinds", "SetKind")):
        for key, row in start[what].items():
            for i, v in enumerate(row):
                if v is not None:
                    lines.append(f"            s.{setter}(\"{key}\", {i - 1}, {cs_float(v)});")
    for k, v in start["answers"].items():
        lines.append(f"            s.SetAnswer(\"{k}\", {cs_float(v)});")
    for region, share in start["hides"]:
        lines.append(f"            s.Hides.Add(new System.Collections.Generic.KeyValuePair<string, float>(\"{region}\", {cs_float(share)}));")
    lines += ["            return s;", "        }", "    }", "}", ""]
    with open(os.path.join(out, "LedgerStart.g.cs"), "w") as f:
        f.write("\n".join(lines))
    with open(os.path.join(out, "LedgerStart.part.json"), "w") as f:
        json.dump(start, f, sort_keys=True, separators=(",", ":"))
    with open(os.path.join(out, "LedgerStart.card.md"), "w") as f:
        f.write(f"# Model card: the starting Ledger\n\n| | |\n|---|---|\n| Version | {version} |\n| Players | {start['players']} (one player, one vote) |\n"
                f"| Tricks | {len(start['tricks'])} |\n| Kinds | {len(start['kinds'])} |\n| Answers timed | {len(start['answers'])} |\n"
                f"| Command | `python3 tools/learn/fit_start.py <dataset>` |\n")


def check(dataset: str, start: dict, hand: dict) -> dict:
    """PR #76's check: mean squared error of the fitted start and the hand-set guesses on held-out
    players' Δpanic, one player one vote."""
    splits = json.load(open(os.path.join(dataset, "splits.json")))
    errs = {"fitted": [], "hand": []}
    for pid in splits["held_out"]:
        f_err, h_err = [], []
        for r in ds.learning(dataset, pid):
            d = r.get("d_panic_20")
            if d is None or r.get("hint"):
                continue
            t = trick_of(r["pick"])
            row = start["tricks"].get(t)
            ph = int(r.get("phase", -1))
            fitted = None
            if row:
                fitted = row[ph + 1] if 0 <= ph + 1 < len(row) and row[ph + 1] is not None else row[0]
            guess_hand = hand.get(t, 0.0)
            f_err.append((d - (fitted if fitted is not None else guess_hand)) ** 2)
            h_err.append((d - guess_hand) ** 2)
        if f_err:
            errs["fitted"].append(sum(f_err) / len(f_err))
            errs["hand"].append(sum(h_err) / len(h_err))
    return {k: (sum(v) / len(v) if v else float("nan")) for k, v in errs.items()} | {"players": len(errs["fitted"])}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dataset")
    ap.add_argument("--split", default="training")
    ap.add_argument("--out", default=GENERATED)
    ap.add_argument("--hand", help="JSON {trick: PanicPrior} for --check; default: the game's own priors (tricks.py)")
    ap.add_argument("--check", action="store_true")
    a = ap.parse_args(argv)
    start = fit(a.dataset, a.split, tricks.kinds())
    if a.check:
        hand = json.load(open(a.hand)) if a.hand else tricks.priors()
        print(json.dumps(check(a.dataset, start, hand), indent=1))
        return 0
    version = "start-" + fingerprint(a.dataset, a.split)
    write(start, a.out, version)
    print(f"fit_start: {version}, {start['players']} players, {len(start['tricks'])} tricks → {a.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
