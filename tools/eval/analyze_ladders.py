#!/usr/bin/env python3
"""§11's two ladders (0.4.0, C6.2), from tools/eval/test_ladders.sh's runs.

    python3 tools/eval/analyze_ladders.py <test040 folder>

Written before any test run, and kept as written (build plan §11). Paired by career (seed and
persona: every arm plays the same test careers), with standard errors.
  Vital (pacing off in every arm)
    1. time to catch: Vital faster than the hunter by more than 2 SE, in both training runs. A shift
       with no catch is "not caught yet" (restricted mean time to catch over the shift, Kaplan–Meier)
    2. fairness violations: 0
  Basic (pacing on)
    1. setpoint error: F beats E by more than 2 SE, in both runs
    2. adaptation, shifts 3–6: F with your Ledger beats F wiped every shift, and F with someone else's
    3. catches a shift inside 0.20–0.35
    4. fairness violations 0; stuck shifts no more than E
Also reported: the trick mix (no trick above 40% of a shift's picks), catches within 10 m of the
clock (the clock-camp guard: above 50% adds a rule), catches by cause. Standard library only.
"""
from __future__ import annotations

import glob
import json
import math
import os
import sys
from collections import Counter, defaultdict

CATCH_TARGET = (0.20, 0.35)


def arm_rows(folder, arm):
    rows = []
    for p in glob.glob(os.path.join(folder, arm, "**", "league_*.jsonl"), recursive=True):
        rows += [json.loads(x) for x in open(p) if x.strip()]
    return rows


def career(r):
    return f"{r['seed']}|{r['agent']}"


def per_career(rows, f, shifts=None):
    g = defaultdict(list)
    for r in rows:
        if shifts and r["shift"] not in shifts:
            continue
        v = f(r)
        if v is not None:
            g[career(r)].append(v)
    return {k: sum(v) / len(v) for k, v in g.items()}


def paired(a, b):
    keys = sorted(set(a) & set(b))
    d = [a[k] - b[k] for k in keys]
    if len(d) < 2:
        return float("nan"), float("nan"), len(d)
    m = sum(d) / len(d)
    return m, math.sqrt(sum((x - m) ** 2 for x in d) / (len(d) - 1) / len(d)), len(d)


def time_to_catch(r):
    """Restricted to the shift: a catch's time, or the shift's length when there was none."""
    t = r.get("time_to_catch_s", -1)
    return t if t is not None and t >= 0 else r["sim_seconds"]


def analyse(folder, suffix=""):
    a = {k: arm_rows(folder, k + suffix if k in ("catch_vital", "fear_F", "fear_F_wiped", "fear_F_other") else k)
         for k in ("catch_hunter", "catch_vital", "ref_planner", "fear_C", "fear_E", "fear_F", "fear_F_wiped", "fear_F_other")}
    out = {}
    m, se, n = paired(per_career(a["catch_vital"], time_to_catch), per_career(a["catch_hunter"], time_to_catch))
    out["vital_time_to_catch"] = dict(diff=m, se=se, careers=n, passed=(m < -2 * se) if not math.isnan(se) else False)
    out["vital_violations"] = sum(r.get("karen_fairness_violations", 0) for r in a["catch_vital"])
    err = lambda r: r.get("panic_setpoint_rmse")  # noqa: E731
    m, se, n = paired(per_career(a["fear_F"], err), per_career(a["fear_E"], err))
    out["basic_setpoint"] = dict(diff=m, se=se, careers=n, passed=(m < -2 * se) if not math.isnan(se) else False)
    late = {3, 4, 5, 6}
    for other in ("fear_F_wiped", "fear_F_other"):
        m, se, n = paired(per_career(a["fear_F"], err, late), per_career(a[other], err, late))
        out["adaptation_vs_" + other] = dict(diff=m, se=se, careers=n, passed=(m < 0) if not math.isnan(m) else False)
    f = a["fear_F"]
    catches = sum(r.get("karen_catches", 0) for r in f) / len(f) if f else float("nan")
    out["basic_catches_per_shift"] = dict(value=catches, passed=CATCH_TARGET[0] <= catches <= CATCH_TARGET[1] if f else False)
    out["basic_violations"] = sum(r.get("karen_fairness_violations", 0) for r in f)
    out["stuck"] = dict(F=sum(bool(r.get("timed_out")) for r in f), E=sum(bool(r.get("timed_out")) for r in a["fear_E"]))
    for arm in ("catch_vital", "catch_hunter", "fear_F"):
        causes = [c for r in a[arm] for c in r.get("catch_causes", [])]
        out["clock_" + arm] = sum(0 <= c["clock_m"] <= 10 for c in causes) / len(causes) if causes else float("nan")
        mix = Counter()
        for r in a[arm]:
            for k, v in r.items():
                if k.startswith("uses_"):
                    mix[k[5:]] += v
        total = sum(mix.values())
        out["top_trick_" + arm] = (mix.most_common(1)[0][0], mix.most_common(1)[0][1] / total) if total else ("—", 0.0)
    return out


def markdown(o):
    f = lambda x, s="{:+.3f}": "—" if x is None or (isinstance(x, float) and math.isnan(x)) else s.format(x)  # noqa: E731
    v, b = o["vital_time_to_catch"], o["basic_setpoint"]
    lines = ["| test | difference | SE | careers | result |", "|---|---|---|---|---|",
             f"| Vital − hunter, time to catch (s) | {f(v['diff'])} | {f(v['se'], '{:.3f}')} | {v['careers']} | {'pass' if v['passed'] else 'not shown'} |",
             f"| Basic (F) − E, setpoint error | {f(b['diff'])} | {f(b['se'], '{:.3f}')} | {b['careers']} | {'pass' if b['passed'] else 'not shown'} |"]
    for k in ("adaptation_vs_fear_F_wiped", "adaptation_vs_fear_F_other"):
        x = o[k]
        lines.append(f"| F − {k.split('_vs_')[1]}, setpoint error, shifts 3–6 | {f(x['diff'])} | {f(x['se'], '{:.3f}')} | {x['careers']} | {'pass' if x['passed'] else 'not shown'} |")
    c = o["basic_catches_per_shift"]
    lines += ["", f"Basic's catches a shift: {f(c['value'], '{:.2f}')} (target 0.20–0.35: {'inside' if c['passed'] else 'outside'}). "
              f"Fairness violations: Vital {o['vital_violations']}, Basic {o['basic_violations']}. Stuck shifts: F {o['stuck']['F']}, E {o['stuck']['E']}.",
              f"Catches within 10 m of the clock: Vital {f(o['clock_catch_vital'], '{:.0%}')}, the hunter {f(o['clock_catch_hunter'], '{:.0%}')}, "
              f"Basic {f(o['clock_fear_F'], '{:.0%}')} (above 50%: a rule is added, and she's retrained).",
              "Top trick share: " + ", ".join(f"{k[10:]} {v[0]} {v[1]:.0%}" for k, v in o.items() if k.startswith("top_trick_")) + " (above 40% is reported)."]
    return "\n".join(lines)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    print(markdown(analyse(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "")))
