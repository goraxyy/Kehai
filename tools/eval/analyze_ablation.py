#!/usr/bin/env python3
"""Summarise an ablation run (AblationRunner's ablation_<time>.jsonl) as markdown.

    python analyze_ablation.py path/to/ablation_20260925_231500.jsonl > results.md

Rungs see the same seeds (paired design), so differences against rung C are computed per
(seed, shift) pair. Standard library only.
"""
from __future__ import annotations

import json
import math
import sys
from collections import Counter, defaultdict

RUNGS = "ABCDEF"
PROFILES = ("efficient", "skittish", "reckless")


def load(path):
    with open(path, encoding="utf-8") as f:
        return [karens_keys(json.loads(line)) for line in f if line.strip()]


# Runs recorded while she was called Aiko (2026-09-29 to 2026-10-10) name her numbers
# "aiko_catches" and so on; they read as "karen_catches" like every other run.
def karens_keys(row):
    return {("karen_" + k[len("aiko_"):] if k.startswith("aiko_") else k): v for k, v in row.items()}


def mean(xs):
    xs = list(xs)
    return sum(xs) / len(xs) if xs else float("nan")


def se(xs):
    xs = list(xs)
    if len(xs) < 2:
        return float("nan")
    m = mean(xs)
    return math.sqrt(sum((x - m) ** 2 for x in xs) / (len(xs) - 1) / len(xs))


def pm(xs, fmt="{:.2f}"):
    xs = list(xs)
    unsigned = fmt.replace("+", "")
    return f"{fmt.format(mean(xs))} ± {unsigned.format(se(xs))}" if len(xs) > 1 else fmt.format(mean(xs))


def pct(k, n):
    return f"{100 * k / n:.0f}%" if n else "—"


def tactics(row):
    counts = Counter()
    for part in (row.get("karen_top_tactics") or "").split():
        if "×" in part:
            name, n = part.rsplit("×", 1)
            counts[name] += int(n)
    return counts


def jensen_shannon(p: Counter, q: Counter) -> float:
    keys = set(p) | set(q)
    sp, sq = sum(p.values()) or 1, sum(q.values()) or 1
    a = {k: p[k] / sp for k in keys}
    b = {k: q[k] / sq for k in keys}
    m = {k: (a[k] + b[k]) / 2 for k in keys}

    def kl(x):
        return sum(x[k] * math.log2(x[k] / m[k]) for k in keys if x[k] > 0)

    return (kl(a) + kl(b)) / 2


def main(path):
    rows = load(path)
    by_rung = defaultdict(list)
    for r in rows:
        by_rung[r["rung"]].append(r)
    rungs = [g for g in RUNGS if g in by_rung]
    out = []
    w = out.append

    careers = len({(r["agent"], r["seed"]) for r in rows})
    shifts = sorted({r["shift"] for r in rows})
    w(f"{len(rows)} simulated shifts: {len(rungs)} rungs × {len({r['agent'] for r in rows})} player profiles, "
      f"{careers} careers of shifts {shifts[0]}–{shifts[-1]}. Mean ± standard error.\n")

    w("### Headline, by rung (all profiles)\n")
    w("| rung | shifts | player clocked out | Karen detected them | first detection (s) | catches / shift | "
      "tactics / shift | tactic entropy (bits) | mean Panic Index | setpoint RMSE | customers lost / shift | fairness violations |")
    w("|---|---|---|---|---|---|---|---|---|---|---|---|")
    for g in rungs:
        rs = by_rung[g]
        det = [r["karen_first_detection_s"] for r in rs if r["karen_first_detection_s"] >= 0]
        w(f"| **{g}** | {len(rs)} | {pct(sum(r['clocked_out'] for r in rs), len(rs))} | {pct(len(det), len(rs))} | "
          f"{pm(det, '{:.0f}') if det else '—'} | {pm(r['karen_catches'] for r in rs)} | "
          f"{pm((r['karen_tactics_used'] for r in rs), '{:.1f}')} | {pm(r['karen_tactic_entropy_bits'] for r in rs)} | "
          f"{pm(r['mean_panic'] for r in rs)} | {pm(r['panic_setpoint_rmse'] for r in rs)} | "
          f"{pm((r['customers_lost'] for r in rs), '{:.1f}')} | {sum(r.get('karen_fairness_violations', 0) for r in rs)} |")

    # Paired differences against C.
    if "C" in by_rung:
        base = {(r["agent"], r["seed"], r["shift"]): r for r in by_rung["C"]}
        w("\n### Each rung against rung C, paired by seed and shift\n")
        w("Positive = more than C. Pairs share the same customers and spills; Karen is the only difference.\n")
        w("| rung | pairs | Δ detections | Δ catches | Δ mean panic | Δ setpoint RMSE | Δ jobs done | Δ clocked out |")
        w("|---|---|---|---|---|---|---|---|")
        for g in rungs:
            if g == "C":
                continue
            d = defaultdict(list)
            for r in by_rung[g]:
                b = base.get((r["agent"], r["seed"], r["shift"]))
                if not b:
                    continue
                jobs = lambda x: x["spills_mopped"] + x["shelves_restocked"] + x["customers_served"] + x["bins_bagged"]
                d["det"].append(r["karen_detections"] - b["karen_detections"])
                d["catch"].append(r["karen_catches"] - b["karen_catches"])
                d["panic"].append(r["mean_panic"] - b["mean_panic"])
                d["rmse"].append(r["panic_setpoint_rmse"] - b["panic_setpoint_rmse"])
                d["jobs"].append(jobs(r) - jobs(b))
                d["out"].append(int(r["clocked_out"]) - int(b["clocked_out"]))
            n = len(d["det"])
            if n:
                w(f"| {g} | {n} | {pm(d['det'], '{:+.1f}')} | {pm(d['catch'], '{:+.2f}')} | {pm(d['panic'], '{:+.3f}')} | "
                  f"{pm(d['rmse'], '{:+.3f}')} | {pm(d['jobs'], '{:+.1f}')} | {pm(d['out'], '{:+.2f}')} |")

    w("\n### By rung and player profile\n")
    w("| rung | profile | clocked out | detections / shift | catches / shift | mean panic | setpoint RMSE | jobs done / shift |")
    w("|---|---|---|---|---|---|---|---|")
    for g in rungs:
        for p in PROFILES:
            rs = [r for r in by_rung[g] if r["agent"] == p]
            if not rs:
                continue
            jobs = [r["spills_mopped"] + r["shelves_restocked"] + r["customers_served"] + r["bins_bagged"] for r in rs]
            w(f"| {g} | {p} | {pct(sum(r['clocked_out'] for r in rs), len(rs))} | {pm((r['karen_detections'] for r in rs), '{:.1f}')} | "
              f"{pm(r['karen_catches'] for r in rs)} | {pm(r['mean_panic'] for r in rs)} | {pm(r['panic_setpoint_rmse'] for r in rs)} | "
              f"{pm(jobs, '{:.1f}')} |")

    w("\n### Across a career (does she get better at this player?)\n")
    w("Mean detections per shift, by the shift's place in the career. Rung E keeps its Ledger between "
      "shifts; C and D start every shift from the prior.\n")
    w("| rung | " + " | ".join(f"shift {s}" for s in shifts) + " |")
    w("|---|" + "---|" * len(shifts))
    for g in rungs:
        cells = []
        for s in shifts:
            rs = [r for r in by_rung[g] if r["shift"] == s]
            cells.append(pm((r["karen_detections"] for r in rs), "{:.1f}") if rs else "—")
        w(f"| {g} | " + " | ".join(cells) + " |")

    w("\n### What she reached for, per profile\n")
    w("Most-used tactics (tier 1+) over all careers; the last column is the Jensen–Shannon divergence "
      "between this profile's tactic mix and the other two profiles' (0 = identical, 1 = disjoint). "
      "Computed from each shift's top four tactics, so it understates the long tail.\n")
    w("| rung | profile | top tactics | JS vs other profiles |")
    w("|---|---|---|---|")
    for g in rungs:
        mixes = {p: sum((tactics(r) for r in by_rung[g] if r["agent"] == p), Counter()) for p in PROFILES}
        for p in PROFILES:
            if not mixes[p]:
                continue
            others = sum((mixes[q] for q in PROFILES if q != p), Counter())
            top = ", ".join(f"{k} ×{v}" for k, v in mixes[p].most_common(5))
            w(f"| {g} | {p} | {top} | {jensen_shannon(mixes[p], others):.3f} |")

    w("\n### Player failures (the eval taxonomy applied to the simulated players)\n")
    labels = ["starvation", "thrashing", "interference_blindness", "clock_blindness", "resource_mismanagement", "spurious_completion"]
    w("| rung | " + " | ".join(labels) + " |")
    w("|---|" + "---|" * len(labels))
    for g in rungs:
        rs = by_rung[g]
        w(f"| {g} | " + " | ".join(pct(sum(l in r['failures'] for r in rs), len(rs)) for l in labels) + " |")

    print("\n".join(out))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
