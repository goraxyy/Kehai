#!/usr/bin/env python3
"""The players' scorecard (0.4.0, build plan §7.7, C1.3): how realistic are the bots?

Reads shift records the same way for bots and for people, and answers eight questions:

    competence   does it finish shifts, at a human pace?           clocked out, jobs a minute, time-outs
    pattern      from its earlier shifts, can a simple predictor   next-job accuracy against chance
                 guess its next job?
    no repeats   same player, two shifts: ever the same?           identical pairs; route overlap
    the band     how predictable is it?                            the pattern score, placed between
                                                                    chance and the scripted bots
    variety      can a classifier tell players apart from one      leave-one-out accuracy against chance
                 shift?
    reactions    does it answer her chores; does it back off?      answers a shift, back-offs a shift
    bot or human can a classifier tell bots from people?           reported only (needs about 10 people)
    glitches     falls, wedges, impossible jumps                   jumps over 10 m in a frame

    python3 tools/learn/scorecard.py <shift_records folder or files>... [--json out.json]
        [--targets tools/learn/scorecard_targets.json]

The targets' numbers are written in C1.4 from your own shifts and the scripted players, before
any training, and kept as written. Until then the card reports and judges nothing.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import records  # noqa: E402

CELL = 3.0          # metres: the grid routes are compared on
JUMP = 10.0         # metres in one frame (0.2 s): a glitch, not a walk


# ---- per shift ---------------------------------------------------------------------------------

def features(s: records.Shift) -> dict:
    t = s.col("t")
    minutes = max(1e-6, (t[-1] - t[0]) / 60.0) if t else 1e-6
    motion = s.col("motion")
    n = max(1, len(motion))
    jobs = s.jobs
    choices = s.choices
    return {
        "clocked_out": 1.0 if s.clocked_out else 0.0,
        "caught": 1.0 if s.caught else 0.0,
        "minutes": minutes,
        "jobs_per_min": len(jobs) / minutes,
        "sprint_share": sum(1 for m in motion if m == 3) / n,
        "crouch_share": sum(1 for m in motion if m == 1) / n,
        "still_share": sum(1 for m in motion if m == 0) / n,
        "hides": float(sum(1 for c in choices if c.what == "Hide")) if s.is_bot else float(hide_episodes(s)),
        "back_offs": float(sum(1 for c in choices if c.what == "BackOff")),
        "answers": float(len(s.answers)),
        "jumps": float(jumps(s)),
        "saw_her_share": sum(1 for x in s.col("saw") if x) / n,
    }


def hide_episodes(s: records.Shift, seconds: float = 3.0) -> int:
    """A person's hides: crouched and still for `seconds` or more."""
    t, m = s.col("t"), s.col("motion")
    count, since = 0, None
    for ti, mi in zip(t, m):
        if mi == 1:
            since = ti if since is None else since
        else:
            if since is not None and ti - since >= seconds:
                count += 1
            since = None
    return count


def jumps(s: records.Shift) -> int:
    x, z = s.col("px"), s.col("pz")
    return sum(1 for i in range(1, len(x)) if math.hypot(x[i] - x[i - 1], z[i] - z[i - 1]) > JUMP)


def cells(s: records.Shift) -> set:
    return {(int(x // CELL), int(z // CELL)) for x, z in zip(s.col("px"), s.col("pz"))}


def job_sequence(s: records.Shift) -> list:
    return [w for _, w in s.jobs]


# ---- across shifts --------------------------------------------------------------------------------

def jaccard(a: set, b: set) -> float:
    return len(a & b) / len(a | b) if a or b else 1.0


def identical(a: records.Shift, b: records.Shift, tolerance: float = 0.5) -> bool:
    if len(a.frames) != len(b.frames):
        return False
    for fa, fb in zip(a.frames, b.frames):
        if math.hypot(fa[1] - fb[1], fa[2] - fb[2]) > tolerance:
            return False
    return True


def pattern(by_player: dict) -> tuple:
    """Next-job accuracy from the player's earlier shifts (bigrams), against always guessing the
    commonest job: (accuracy, chance, predictions)."""
    hits = guesses = base_hits = 0
    for shifts in by_player.values():
        shifts = sorted(shifts, key=lambda s: s.shift)
        bigrams = defaultdict(Counter)
        overall = Counter()
        for s in shifts:
            seq = job_sequence(s)
            if overall:
                for prev, nxt in zip(seq, seq[1:]):
                    guess = bigrams[prev].most_common(1)[0][0] if bigrams[prev] else overall.most_common(1)[0][0]
                    hits += guess == nxt
                    base_hits += overall.most_common(1)[0][0] == nxt
                    guesses += 1
            for prev, nxt in zip(seq, seq[1:]):
                bigrams[prev][nxt] += 1
            overall.update(seq)
    if not guesses:
        return float("nan"), float("nan"), 0
    return hits / guesses, base_hits / guesses, guesses


VEC = ("jobs_per_min", "sprint_share", "crouch_share", "still_share", "hides", "back_offs", "saw_her_share")


def nearest_centroid_loo(rows: list, labels: list) -> tuple:
    """Leave-one-out accuracy of a nearest-centroid classifier on standardised features, and chance
    (the commonest label's share)."""
    if len(set(labels)) < 2 or len(rows) < 3:
        return float("nan"), float("nan")
    dims = len(rows[0])
    mean = [sum(r[d] for r in rows) / len(rows) for d in range(dims)]
    sd = [max(1e-9, math.sqrt(sum((r[d] - mean[d]) ** 2 for r in rows) / len(rows))) for d in range(dims)]
    z = [[(r[d] - mean[d]) / sd[d] for d in range(dims)] for r in rows]
    hits = 0
    for i in range(len(z)):
        groups = defaultdict(list)
        for j in range(len(z)):
            if j != i:
                groups[labels[j]].append(z[j])
        best, best_d = None, float("inf")
        for lab, members in groups.items():
            c = [sum(m[d] for m in members) / len(members) for d in range(dims)]
            dist = sum((z[i][d] - c[d]) ** 2 for d in range(dims))
            if dist < best_d:
                best, best_d = lab, dist
        hits += best == labels[i]
    chance = Counter(labels).most_common(1)[0][1] / len(labels)
    return hits / len(z), chance


def card(shifts: list) -> dict:
    out = {"shifts": len(shifts), "groups": {}}
    by_group = defaultdict(list)
    for s in shifts:
        by_group["people" if not s.is_bot else s.persona_name].append(s)

    for group, ss in sorted(by_group.items()):
        f = [features(s) for s in ss]
        by_player = defaultdict(list)
        for s in ss:
            by_player[s.player].append(s)
        acc, chance, n = pattern(by_player)
        pairs = repeats = 0
        overlaps = []
        for player_shifts in by_player.values():
            for i in range(len(player_shifts)):
                for j in range(i + 1, len(player_shifts)):
                    pairs += 1
                    repeats += identical(player_shifts[i], player_shifts[j])
                    overlaps.append(jaccard(cells(player_shifts[i]), cells(player_shifts[j])))
        out["groups"][group] = {
            "shifts": len(ss),
            "players": len(by_player),
            "competence": {
                "clocked_out": mean(x["clocked_out"] for x in f),
                "caught": mean(x["caught"] for x in f),
                "jobs_per_min": mean(x["jobs_per_min"] for x in f),
                "minutes": mean(x["minutes"] for x in f),
            },
            "pattern": {"next_job": acc, "chance": chance, "predictions": n},
            "no_repeats": {"pairs": pairs, "identical": repeats, "route_overlap": mean(overlaps)},
            "reactions": {"answers": mean(x["answers"] for x in f), "back_offs": mean(x["back_offs"] for x in f),
                          "hides": mean(x["hides"] for x in f)},
            "habits": {"sprint_share": mean(x["sprint_share"] for x in f), "saw_her_share": mean(x["saw_her_share"] for x in f)},
            "glitches": {"jumps": sum(x["jumps"] for x in f)},
        }

    bots = [s for s in shifts if s.is_bot]
    rows = [[features(s)[k] for k in VEC] for s in bots]
    v_acc, v_chance = nearest_centroid_loo(rows, [s.persona_name for s in bots])
    out["variety"] = {"accuracy": v_acc, "chance": v_chance}
    people = [s for s in shifts if not s.is_bot]
    if bots and people:
        rows = [[features(s)[k] for k in VEC] for s in bots + people]
        b_acc, b_chance = nearest_centroid_loo(rows, ["bot"] * len(bots) + ["person"] * len(people))
    else:
        b_acc = b_chance = float("nan")
    out["bot_or_human"] = {"accuracy": b_acc, "chance": b_chance, "people": len({s.player for s in people})}
    return out


def mean(xs) -> float:
    xs = list(xs)
    return sum(xs) / len(xs) if xs else float("nan")


# ---- the targets (C1.4) ---------------------------------------------------------------------------

def judge(c: dict, targets: dict) -> list:
    """(group, measure, value, target, passed) for every target that applies. The targets file is
    written in C1.4, before any training; groups named there must exist in the card."""
    verdicts = []
    for group, g in c["groups"].items():
        if group == "people":
            continue
        t = targets.get("bots", {})
        comp = g["competence"]
        if "jobs_per_min" in t:
            lo, hi = t["jobs_per_min"]
            verdicts.append((group, "competence: jobs a minute", comp["jobs_per_min"], f"{lo}–{hi}", lo <= comp["jobs_per_min"] <= hi))
        if "pattern_over_chance" in t and not math.isnan(g["pattern"]["next_job"]):
            gain = g["pattern"]["next_job"] - g["pattern"]["chance"]
            verdicts.append((group, "pattern: next job over chance", gain, f">= {t['pattern_over_chance']}", gain >= t["pattern_over_chance"]))
        verdicts.append((group, "no repeats: identical pairs", g["no_repeats"]["identical"], "0", g["no_repeats"]["identical"] == 0))
        if "route_overlap_max" in t and not math.isnan(g["no_repeats"]["route_overlap"]):
            verdicts.append((group, "no repeats: route overlap", g["no_repeats"]["route_overlap"], f"<= {t['route_overlap_max']}",
                             g["no_repeats"]["route_overlap"] <= t["route_overlap_max"]))
        verdicts.append((group, "glitches: jumps", g["glitches"]["jumps"], "0", g["glitches"]["jumps"] == 0))
    if "variety_over_chance" in targets.get("bots", {}) and not math.isnan(c["variety"]["accuracy"]):
        gain = c["variety"]["accuracy"] - c["variety"]["chance"]
        verdicts.append(("all bots", "variety: persona from one shift, over chance", gain,
                         f">= {targets['bots']['variety_over_chance']}", gain >= targets["bots"]["variety_over_chance"]))
    return verdicts


def markdown(c: dict, verdicts: list) -> str:
    def f(x, fmt="{:.2f}"):
        return "—" if x is None or (isinstance(x, float) and math.isnan(x)) else fmt.format(x)
    lines = [f"The players' scorecard: {c['shifts']} shifts.\n",
             "| group | shifts | players | clocked out | caught | jobs / min | next job (chance) | identical pairs | route overlap | answers | back-offs | hides | sprint share | jumps |",
             "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for name, g in c["groups"].items():
        lines.append(f"| {name} | {g['shifts']} | {g['players']} | {f(g['competence']['clocked_out'], '{:.0%}')} | "
                     f"{f(g['competence']['caught'], '{:.0%}')} | {f(g['competence']['jobs_per_min'])} | "
                     f"{f(g['pattern']['next_job'], '{:.0%}')} ({f(g['pattern']['chance'], '{:.0%}')}) | "
                     f"{g['no_repeats']['identical']}/{g['no_repeats']['pairs']} | {f(g['no_repeats']['route_overlap'])} | "
                     f"{f(g['reactions']['answers'], '{:.1f}')} | {f(g['reactions']['back_offs'], '{:.1f}')} | "
                     f"{f(g['reactions']['hides'], '{:.1f}')} | {f(g['habits']['sprint_share'], '{:.0%}')} | {g['glitches']['jumps']:.0f} |")
    lines.append(f"\nVariety (persona from one shift): {f(c['variety']['accuracy'], '{:.0%}')} against chance "
                 f"{f(c['variety']['chance'], '{:.0%}')}. Bot or human: {f(c['bot_or_human']['accuracy'], '{:.0%}')} "
                 f"(chance {f(c['bot_or_human']['chance'], '{:.0%}')}, {c['bot_or_human']['people']} people; reported only).")
    if verdicts:
        lines += ["", "| group | target | value | wanted | |", "|---|---|---|---|---|"]
        lines += [f"| {g} | {m} | {f(float(v))} | {t} | {'pass' if ok else '**fail**'} |" for g, m, v, t, ok in verdicts]
    else:
        lines.append("\nNo targets yet: they are written in C1.4, from your shifts and the scripted players.")
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--json")
    ap.add_argument("--targets")
    a = ap.parse_args(argv)
    shifts = records.load_all(a.paths)
    if not shifts:
        sys.exit("scorecard: no shift records found")
    c = card(shifts)
    targets = json.load(open(a.targets)) if a.targets and os.path.exists(a.targets) else {}
    v = judge(c, targets) if targets else []
    print(markdown(c, v))
    if a.json:
        with open(a.json, "w") as fh:
            json.dump({"card": c, "verdicts": v}, fh, indent=1, default=str)
    return 0 if all(ok for *_, ok in v) else 1


if __name__ == "__main__":
    sys.exit(main())
