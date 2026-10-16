#!/usr/bin/env python3
"""Summarise an ablation run (AblationRunner's ablation_<time>.jsonl) as markdown.

    python analyze_ablation.py path/to/ablation_20260925_231500.jsonl > results.md

Rungs see the same seeds (paired design), so differences against rung C are computed per
(seed, shift) pair, and rung G (a trained tactic chooser, tools/rl) against rung F, the one
thing it changes. Standard library only.
"""
from __future__ import annotations

import json
import math
import sys
from collections import Counter, defaultdict

RUNGS = "ABCDEFG"
PROFILES = ("efficient", "skittish", "reckless")


def load(path):
    with open(path, encoding="utf-8") as f:
        return first_of_each_shift([karens_keys(json.loads(line)) for line in f if line.strip()])


# Runs before 2026-10-15's fix logged a shift that never started (a failed clock-in between
# shifts) as a copy of the one before: same rung, player, seed and shift. Keep the first,
# the real one; a copy would otherwise replace it when rungs are paired.
def first_of_each_shift(rows):
    kept, seen = [], set()
    for r in rows:
        key = (r["rung"], r["agent"], r["seed"], r["shift"])
        if key in seen:
            continue
        seen.add(key)
        kept.append(r)
    if len(kept) < len(rows):
        print(f"analyze_ablation: dropped {len(rows) - len(kept)} repeated (rung, player, seed, shift) rows: "
              "shifts that never started", file=sys.stderr)
    return kept


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


def pm_or_dash(xs, fmt="{:.2f}"):
    xs = list(xs)
    return pm(xs, fmt) if xs else "—"


# Belief accuracy (BeliefAccuracy.cs): per-episode means, null when the shift had no samples
# in that split. Top-k are fractions, shown as percentages.
VIEWS = (("out_of_view", "out of view"), ("in_view", "in view"))
SCALE = {"logp": 1, "top1": 100, "top5": 100, "top10": 100, "err_m": 1}


def acc(row, who, view, metric):
    v = row.get(f"{who}_{view}_{metric}")
    return None if v is None else v * SCALE[metric]


def accs(rows, who, view, metric):
    return [v for v in (acc(r, who, view, metric) for r in rows) if v is not None]


def paired(rows, base, key):
    """Per-row differences against base[key(row)] (or the row itself when base is None), where both have a value."""
    def diff(who_a, who_b, view, metric):
        out = []
        for r in rows:
            b = base.get(key(r)) if base is not None else r
            if b is None:
                continue
            a, c = acc(r, who_a, view, metric), acc(b, who_b, view, metric)
            if a is not None and c is not None:
                out.append(a - c)
        return out
    return diff


def belief_section(rows, rungs, w):
    # Only shifts Karen was on: an episode whose shift never started (a failed clock-in
    # between shifts) has no samples, and would otherwise shadow its rung's real row when
    # pairing by (agent, seed, shift).
    rows = [r for r in rows if r.get("belief_source")]
    if not rows:
        return
    by_rung = defaultdict(list)
    for r in rows:
        by_rung[r["rung"]].append(r)
    rungs = [g for g in rungs if g in by_rung]
    cells = [r["belief_cells"] for r in rows if r.get("belief_cells")]
    n = round(mean(cells)) if cells else 0

    w("\n### How well she knows where the player is\n")
    w("Her belief scored five times a second of sim time against the player's true cell, split by whether her sight "
      "has the player that frame. **log p** is the belief's log-probability of the true cell (nats, floored at 1e-6; "
      "0 is certainty); **top-k** is how often the true cell is among her k likeliest; **error** is metres from her "
      "likeliest cell to the true one. Rungs A and B keep no belief and are scored on the *last-seen* baseline: a "
      "Gaussian round her last fix (the punch at the time clock, then her sight), σ = 1.5 m + 1 m per second since. "
      + (f"For scale, a uniform guess over the {n} cells scores log p = {-math.log(n):.2f}, top-5 = {500 / n:.2f}%."
         if n else "") + "\n")
    for view, label in VIEWS:
        w(f"#### {label.capitalize()}\n")
        w(f"| rung | belief | shifts | {label} | log p | top-1 (%) | top-5 (%) | top-10 (%) | error (m) |")
        w("|---|---|---|---|---|---|---|---|---|")
        for g in rungs:
            rs = by_rung[g]
            samples = sum(r.get(f"belief_{view}_samples", 0) for r in rs)
            total = sum(r.get("belief_in_view_samples", 0) + r.get("belief_out_of_view_samples", 0) for r in rs)
            source = next((r["belief_source"] for r in rs if r.get("belief_source")), "—")
            w(f"| **{g}** | {source} | {len(rs)} | {pct(samples, total)} | " +
              " | ".join(pm_or_dash(accs(rs, "belief", view, m), f) for m, f in
                         (("logp", "{:.2f}"), ("top1", "{:.1f}"), ("top5", "{:.1f}"), ("top10", "{:.1f}"), ("err_m", "{:.1f}"))) + " |")
        w("")

    key = lambda r: (r["agent"], r["seed"], r["shift"])
    metrics = (("logp", "{:+.2f}"), ("top5", "{:+.1f}"), ("err_m", "{:+.1f}"))
    if "C" in by_rung:
        base = {key(r): r for r in by_rung["C"]}
        w("#### Against rung C, paired by seed and shift\n")
        w("Positive log p and top-5 (percentage points) = she knew better than at C; positive error = she was further off.\n")
        w("| rung | pairs | Δ log p out of view | Δ top-5 out of view | Δ error out of view (m) | Δ log p in view | Δ top-5 in view | Δ error in view (m) |")
        w("|---|---|---|---|---|---|---|---|")
        for g in rungs:
            if g == "C":
                continue
            diff = paired(by_rung[g], base, key)
            pairs = sum(1 for r in by_rung[g] if key(r) in base)
            cols = [pm_or_dash(diff("belief", "belief", view, m), f) for view, _ in VIEWS for m, f in metrics]
            w(f"| {g} | {pairs} | " + " | ".join(cols) + " |")
        w("")

    grid_rungs = [g for g in rungs if any(r.get("belief_source") == "grid" for r in by_rung[g])]
    if grid_rungs:
        w("#### The belief grid against last-seen, same shifts\n")
        w("Within each shift, the grid's score minus the last-seen baseline's on the same samples: what the filter "
          "(prediction, hearing, traces, testimony, negative evidence) adds over remembering the last sighting.\n")
        w("| rung | shifts | Δ log p out of view | Δ top-5 out of view | Δ error out of view (m) | Δ log p in view | Δ top-5 in view | Δ error in view (m) |")
        w("|---|---|---|---|---|---|---|---|")
        for g in grid_rungs:
            diff = paired(by_rung[g], None, key)
            cols = [pm_or_dash(diff("belief", "last_seen", view, m), f) for view, _ in VIEWS for m, f in metrics]
            w(f"| {g} | {len(by_rung[g])} | " + " | ".join(cols) + " |")
        w("")

    w("#### By rung and player profile, out of view\n")
    w("| rung | profile | out of view | log p | top-5 (%) | error (m) | last-seen log p | last-seen top-5 (%) | last-seen error (m) |")
    w("|---|---|---|---|---|---|---|---|---|")
    for g in rungs:
        for p in PROFILES:
            rs = [r for r in by_rung[g] if r["agent"] == p]
            if not rs:
                continue
            samples = sum(r.get("belief_out_of_view_samples", 0) for r in rs)
            total = sum(r.get("belief_in_view_samples", 0) + r.get("belief_out_of_view_samples", 0) for r in rs)
            w(f"| {g} | {p} | {pct(samples, total)} | " +
              " | ".join(pm_or_dash(accs(rs, who, "out_of_view", m), f) for who in ("belief", "last_seen") for m, f in
                         (("logp", "{:.2f}"), ("top5", "{:.1f}"), ("err_m", "{:.1f}"))) + " |")


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


def paired(w, by_rung, base_rung, rungs):
    base = {(r["agent"], r["seed"], r["shift"]): r for r in by_rung[base_rung]}
    w("| rung | pairs | Δ detections | Δ catches | Δ mean panic | Δ setpoint RMSE | Δ jobs done | Δ clocked out |")
    w("|---|---|---|---|---|---|---|---|")
    for g in rungs:
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

    # Paired differences against C, and G against F (the one thing G changes).
    if "C" in by_rung:
        w("\n### Each rung against rung C, paired by seed and shift\n")
        w("Positive = more than C. Pairs share the same customers and spills; Karen is the only difference.\n")
        paired(w, by_rung, "C", [g for g in rungs if g != "C"])
    if "F" in by_rung and "G" in by_rung:
        w("\n### Rung G against rung F, paired by seed and shift\n")
        w("G is F with the bandit's pick handed to the trained policy. Positive = more than F.\n")
        paired(w, by_rung, "F", ["G"])

    belief_section(rows, rungs, w)

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
