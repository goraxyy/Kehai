#!/usr/bin/env python3
"""Summarise an ablation run (AblationRunner's ablation_<time>.jsonl) as markdown.

    python analyze_ablation.py path/to/ablation_20260925_231500.jsonl > results.md
    python analyze_ablation.py joined.jsonl --best m3 > results.md     (with belief shadows)

Rungs see the same seeds (paired design), so differences against rung C are computed per
(seed, shift) pair, and rung F (a trained tactic chooser, tools/rl) against rung E, the one
thing it changes. Runs with belief shadow filters (-belief-shadows, tools/belief) add a section
comparing the motion models on the same evidence; `--best` names the learned model chosen on
validation (MotionWeights.Best), the primary comparison. Rows where her own grid ran another
motion model (-belief-motion, the closed-loop run) are kept apart and paired with rung C on m0.
Standard library only.
"""
from __future__ import annotations

import json
import math
import random
import sys
from collections import Counter, defaultdict

RUNGS = "ABCDEF"
# "cautious" is held out: never used to tune Karen or train her policy, only to test her on.
PROFILES = ("efficient", "skittish", "reckless", "cautious")


def load(path):
    with open(path, encoding="utf-8") as f:
        return first_of_each_shift([karens_keys(json.loads(line)) for line in f if line.strip()])


# Runs before 2026-10-15's fix logged a shift that never started (a failed clock-in between
# shifts) as a copy of the one before: same rung, player, seed and shift. Keep the first,
# the real one; a copy would otherwise replace it when rungs are paired.
def first_of_each_shift(rows):
    kept, seen = [], set()
    for r in rows:
        key = (r["rung"], r["agent"], r["seed"], r["shift"], motion_of(r))
        if key in seen:
            continue
        seen.add(key)
        kept.append(r)
    if len(kept) < len(rows):
        print(f"analyze_ablation: dropped {len(rows) - len(kept)} repeated (rung, player, seed, shift) rows: "
              "shifts that never started", file=sys.stderr)
    return kept


# The motion model her own grid ran (tools/belief); "m0" is the shipped step, and runs from
# before it existed.
def motion_of(row):
    m = row.get("belief_motion") or "m0"
    return "m0" if m == "hand" else m


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


def paired_belief(rows, base, key):
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
            diff = paired_belief(by_rung[g], base, key)
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
            diff = paired_belief(by_rung[g], None, key)
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


# ---- motion models on the same evidence (tools/belief) -----------------------------------

LOST = (("0_5", "0–5 s"), ("5_15", "5–15 s"), ("15_45", "15–45 s"), ("45_up", "45 s +"))
MODEL_NAMES = {"last_seen": "last-seen", "belief": "her grid (m0 + hint)"}


def shadow_ids(rows):
    ids = []
    for r in rows:
        for i in (r.get("belief_shadows") or "").split(","):
            if i and i not in ids:
                ids.append(i)
    return ids


def who(model):
    """The jsonl prefix of a model: a shadow, her own grid, or the last-seen baseline."""
    return model if model in ("belief", "last_seen") else "shadow_" + model.replace("/", "s")


def shift_diffs(rows, a, b, view="out_of_view", metric="logp"):
    """(career, Δ) per shift, a minus b on the same samples."""
    out = []
    for r in rows:
        x, y = acc(r, who(a), view, metric), acc(r, who(b), view, metric)
        if x is not None and y is not None:
            out.append(((r["agent"], r["seed"]), x - y))
    return out


def career_bootstrap(diffs, resamples=10000, seed=0):
    """Mean of per-shift differences, and its 95% interval from resampling careers."""
    by_career = defaultdict(list)
    for career, d in diffs:
        by_career[career].append(d)
    careers = list(by_career)
    if not careers:
        return float("nan"), float("nan"), float("nan"), 0
    rng = random.Random(seed)
    means = []
    for _ in range(resamples):
        pick = [rng.choice(careers) for _ in careers]
        xs = [d for c in pick for d in by_career[c]]
        means.append(sum(xs) / len(xs))
    means.sort()
    point = mean(d for _, d in diffs)
    return point, means[int(0.025 * resamples)], means[int(0.975 * resamples) - 1], len(careers)


def ci(diffs, fmt="{:+.3f}"):
    point, lo, hi, n = career_bootstrap(diffs)
    if not diffs:
        return "—"
    return f"{fmt.format(point)} [{fmt.format(lo)}, {fmt.format(hi)}]"


def motion_section(rows, closed, w, best=None):
    rows = [r for r in rows if r.get("belief_shadows")]
    ids = shadow_ids(rows)
    if not rows or not ids:
        return
    learned = [i for i in ids if i.split("/")[0] in ("m2", "m3", "m4")]
    models = ids + ["last_seen"]
    careers = len({(r["agent"], r["seed"]) for r in rows})
    w("\n### Motion models on the same evidence\n")
    w(f"{len(rows)} shifts, {careers} careers. In each shift Karen acted on her own grid (m0, with the Director's hint); "
      f"shadow filters {', '.join(ids)} ran beside it on exactly the same evidence, without the hint, and every one was scored "
      "on the same 5 Hz samples. Mean ± standard error over shifts; Δ columns are per-shift differences on the same samples "
      "with a 95% interval from resampling careers (one player profile's shifts on one seed).\n")

    w("#### Out of view\n")
    w("| model | shifts | log p | top-1 (%) | top-5 (%) | top-10 (%) | error (m) | in view: log p |")
    w("|---|---|---|---|---|---|---|---|")
    for m in models + ["belief"]:
        vals = accs(rows, who(m), "out_of_view", "logp")
        if not vals:
            continue
        w(f"| {MODEL_NAMES.get(m, m)} | {len(vals)} | " +
          " | ".join(pm_or_dash(accs(rows, who(m), "out_of_view", k), f) for k, f in
                     (("logp", "{:.3f}"), ("top1", "{:.1f}"), ("top5", "{:.1f}"), ("top10", "{:.1f}"), ("err_m", "{:.1f}"))) +
          f" | {pm_or_dash(accs(rows, who(m), 'in_view', 'logp'), '{:.3f}')} |")
    w("")

    if best or learned:
        primary = best if best in ids else None
        w("#### The primary comparison\n")
        if primary and "m1" in ids:
            point, lo, hi, n = career_bootstrap(shift_diffs(rows, primary, "m1"))
            verdict = "**wins**: the interval is above 0" if lo > 0 else "does **not** win: the interval reaches 0 or below"
            w(f"Best learned model on validation: **{primary}**. Out-of-view log p, {primary} minus m1: "
              f"**{point:+.3f}** nats, 95% interval [{lo:+.3f}, {hi:+.3f}] over {n} careers. It {verdict}.\n")
        else:
            w("(No `--best` given, or no m1 shadow: every learned model against m1 is in the next table.)\n")

    w("#### Each model against m0, m1 and last-seen (out of view, same samples)\n")
    w("| model | Δ log p vs m0 | Δ log p vs m1 | Δ log p vs last-seen | Δ top-5 vs m1 (pts) | Δ error vs m1 (m) |")
    w("|---|---|---|---|---|---|")
    for m in ids:
        cols = [ci(shift_diffs(rows, m, b)) if m != b else "—" for b in ("m0", "m1", "last_seen") if b in models]
        while len(cols) < 3:
            cols.append("—")
        cols.append(ci(shift_diffs(rows, m, "m1", metric="top5"), "{:+.1f}") if "m1" in ids and m != "m1" else "—")
        cols.append(ci(shift_diffs(rows, m, "m1", metric="err_m"), "{:+.2f}") if "m1" in ids and m != "m1" else "—")
        w(f"| {m} | " + " | ".join(cols) + " |")
    w("")

    w("#### By player, out of view log p (cautious is held out: never trained on or used for choosing)\n")
    present = [p for p in PROFILES if any(r["agent"] == p for r in rows)]
    w("| model | " + " | ".join(p + (" (held out)" if p == "cautious" else "") for p in present) + " |")
    w("|---|" + "---|" * len(present))
    for m in models:
        w(f"| {MODEL_NAMES.get(m, m)} | " + " | ".join(pm_or_dash(accs([r for r in rows if r["agent"] == p], who(m), "out_of_view", "logp"), "{:.3f}") for p in present) + " |")
    if "m1" in ids and learned:
        for m in learned:
            w(f"| Δ {m} − m1 | " + " | ".join(ci(shift_diffs([r for r in rows if r["agent"] == p], m, "m1")) for p in present) + " |")
    w("")

    late = [r for r in rows if r["shift"] == max(r2["shift"] for r2 in rows)]
    if late and max(r["shift"] for r in rows) >= 7:
        w(f"#### Shift {late[0]['shift']} alone (after the maze changes), out of view\n")
        w("| model | shifts | log p | top-5 (%) | error (m) | Δ log p vs m1 |")
        w("|---|---|---|---|---|---|")
        for m in models:
            w(f"| {MODEL_NAMES.get(m, m)} | {len(accs(late, who(m), 'out_of_view', 'logp'))} | " +
              " | ".join(pm_or_dash(accs(late, who(m), "out_of_view", k), f) for k, f in (("logp", "{:.3f}"), ("top5", "{:.1f}"), ("err_m", "{:.1f}"))) +
              f" | {ci(shift_diffs(late, m, 'm1')) if 'm1' in ids and m not in ('m1',) else '—'} |")
        w("")

    w("#### By time since she lost sight, out of view log p\n")
    w("Where the evidence is fresh the motion model matters little; where it is stale it is nearly all there is.\n")
    w("| model | " + " | ".join(label for _, label in LOST) + " |")
    w("|---|" + "---|" * len(LOST))
    total = sum(r.get(f"belief_lost_{k}_samples", 0) for r in rows for k, _ in LOST)
    w("| share of out-of-view samples | " + " | ".join(pct(sum(r.get(f"belief_lost_{k}_samples", 0) for r in rows), total) for k, _ in LOST) + " |")
    for m in models:
        w(f"| {MODEL_NAMES.get(m, m)} | " + " | ".join(pm_or_dash(accs(rows, who(m), f"lost_{k}", "logp"), "{:.3f}") for k, _ in LOST) + " |")
    if "m1" in ids:
        for m in learned:
            w(f"| Δ {m} − m1 | " + " | ".join(ci(shift_diffs(rows, m, "m1", view=f"lost_{k}")) for k, _ in LOST) + " |")
    w("")

    w("#### Cost: milliseconds per believe tick (editor, batch mode)\n")
    w("Predict (the motion model, including rebuilding its kernel) and correct (multiplying in the evidence), per believe "
      "tick, averaged over each shift. At 60 fps and 10 believe ticks a second, a tick's cost is spread over 6 frames on "
      "average, but lands in one. Karen's whole budget is 0.7 ms a frame.\n")
    w("| model | predict (ms/tick) | correct (ms/tick) | per frame at 60 fps (ms, average) |")
    w("|---|---|---|---|")
    for m in ids:
        pr = [r[f"{who(m)}_ms_predict"] for r in rows if r.get(f"{who(m)}_ms_predict") is not None]
        co = [r[f"{who(m)}_ms_evidence"] for r in rows if r.get(f"{who(m)}_ms_evidence") is not None]
        if pr:
            w(f"| {m} | {pm(pr, '{:.2f}')} | {pm(co, '{:.3f}')} | {mean(pr + []) / 6 + (mean(co) / 6 if co else 0):.2f} |")
    own = [r["belief_ms_per_tick"] for r in rows if r.get("belief_ms_per_tick") is not None]
    if own:
        w(f"| her own grid (m0) | {pm(own, '{:.2f}')} | — | {mean(own) / 6:.2f} |")
    w("")

    if closed:
        base = {(r["agent"], r["seed"], r["shift"]): r for r in rows if r["rung"] == "C"}
        w("#### Closed loop: Karen acting on a learned model, against rung C on m0\n")
        w("Same rung, seeds and shifts; the only difference is the motion model her own grid predicts with (with the "
          "Director's hint, as in the game). Positive = more than on m0. Her belief accuracy here is her own grid's.\n")
        w("| model | pairs | Δ detections | Δ first detection (s) | Δ catches | Δ mean panic | Δ own log p out of view | Δ own error out of view (m) | fairness violations |")
        w("|---|---|---|---|---|---|---|---|---|")
        for m in sorted({motion_of(r) for r in closed}):
            rs = [r for r in closed if motion_of(r) == m and r["rung"] == "C"]
            pairs = [(r, base[(r["agent"], r["seed"], r["shift"])]) for r in rs if (r["agent"], r["seed"], r["shift"]) in base]
            def d(f):
                return [f(a) - f(b) for a, b in pairs if f(a) is not None and f(b) is not None]
            first = [a["karen_first_detection_s"] - b["karen_first_detection_s"] for a, b in pairs
                     if a["karen_first_detection_s"] >= 0 and b["karen_first_detection_s"] >= 0]
            w(f"| {m} | {len(pairs)} | {pm_or_dash(d(lambda r: r['karen_detections']), '{:+.1f}')} | {pm_or_dash(first, '{:+.1f}')} | "
              f"{pm_or_dash(d(lambda r: r['karen_catches']), '{:+.2f}')} | {pm_or_dash(d(lambda r: r['mean_panic']), '{:+.3f}')} | "
              f"{pm_or_dash(d(lambda r: acc(r, 'belief', 'out_of_view', 'logp')), '{:+.3f}')} | "
              f"{pm_or_dash(d(lambda r: acc(r, 'belief', 'out_of_view', 'err_m')), '{:+.1f}')} | "
              f"{sum(a.get('karen_fairness_violations', 0) for a, _ in pairs)} |")
        w("")


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


def main(path, best=None):
    rows = load(path)
    # Rows where her own grid ran another motion model (the closed-loop run) only appear in
    # the motion section, paired with rung C on m0.
    closed = [r for r in rows if motion_of(r) != "m0"]
    rows = [r for r in rows if motion_of(r) == "m0"]
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

    # Paired differences against C, and F against E (the one thing F changes).
    if "C" in by_rung:
        w("\n### Each rung against rung C, paired by seed and shift\n")
        w("Positive = more than C. Pairs share the same customers and spills; Karen is the only difference.\n")
        paired(w, by_rung, "C", [g for g in rungs if g != "C"])
    if "E" in by_rung and "F" in by_rung:
        w("\n### Rung F against rung E, paired by seed and shift\n")
        w("F is E with the bandit's pick handed to the trained policy. Positive = more than E.\n")
        paired(w, by_rung, "E", ["F"])

    belief_section(rows, rungs, w)
    motion_section(rows, closed, w, best)

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

    if any("karen_reward_mean_abs" in r for r in rows):
        w("\n### The reward signal, and how frightened the players were\n")
        w("Reward signal: mean |Δ panic| in the 20 s after each tactic she was rewarded for — what the "
          "bandit and the policy have to learn from. Fear is the simulated player's own (`BotFear`); "
          "static players don't react to her tricks.\n")
        w("| rung | profile | players | shifts | reward signal | mean fear | startles / shift | back-offs / shift | hides / shift | kills / shift |")
        w("|---|---|---|---|---|---|---|---|---|---|")
        for g in rungs:
            for p in PROFILES:
                rs = [r for r in by_rung[g] if r["agent"] == p and "karen_reward_mean_abs" in r]
                if not rs:
                    continue
                kind = "reacting" if all(r.get("bot_reactive", True) for r in rs) else "static" if not any(r.get("bot_reactive", True) for r in rs) else "mixed"
                w(f"| {g} | {p} | {kind} | {len(rs)} | {pm((r['karen_reward_mean_abs'] for r in rs), '{:.3f}')} | "
                  f"{pm(r.get('bot_mean_fear', 0) for r in rs)} | {pm((r.get('bot_startles', 0) for r in rs), '{:.1f}')} | "
                  f"{pm((r.get('bot_back_offs', 0) for r in rs), '{:.1f}')} | {pm((r.get('bot_hides', 0) for r in rs), '{:.1f}')} | "
                  f"{pm(r['karen_catches'] for r in rs)} |")

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
      "between this profile's tactic mix and the other profiles' (0 = identical, 1 = disjoint). "
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
    args = sys.argv[1:]
    best = None
    if "--best" in args:
        i = args.index("--best")
        best = args[i + 1]
        del args[i:i + 2]
    if len(args) != 1:
        sys.exit(__doc__)
    main(args[0], best)
