#!/usr/bin/env python3
"""§11's first three tests (0.4.0, C4.11), from a test run with shadow filters (tools/belief/test_run.sh).

    python3 tools/eval/analyze_prediction.py <league jsonl>… [--senses s1|s2] [--motion p2|p3]

Written before any test run, and kept as written (build plan §11):
  her senses        the chosen fitted senses (--senses, chosen on validation) beat S0 on log p of the
                    true cell over the whole shift: the 95% interval, resampled by career, above zero.
  what you know     K2 names the area where you last sensed her more often than K1, sampled every 2 s
                    while you can't sense her: by more than 2 standard errors.
  the prediction    1. the chosen of P2 and P3 (--motion) beats P1 on log p out of view: the 95%
                    interval, resampled by career, above zero (PR #68's rule).
                    2. over shifts 3–6, P3 with your Ledger names your next destination (top 1, at the
                    moment she loses you) more often than P3 with someone else's (p3x): > 2 SE.
The report leads with the moment she loses sight of you. Standard library only.
"""
from __future__ import annotations

import argparse
import json
import math
import random
from collections import defaultdict


def load(paths):
    rows = []
    for p in paths:
        with open(p) as f:
            rows += [json.loads(x) for x in f if x.strip()]
    return rows


def career(r):
    return f"{r['seed']}|{r['agent']}"


def by_career(rows, f):
    g = defaultdict(list)
    for r in rows:
        v = f(r)
        if v is not None and not (isinstance(v, float) and math.isnan(v)):
            g[career(r)].append(v)
    return {k: sum(v) / len(v) for k, v in g.items() if v}


def bootstrap(diffs: list, n: int = 2000, seed: int = 0) -> tuple:
    if len(diffs) < 2:
        return float("nan"), float("nan"), float("nan")
    rnd = random.Random(seed)
    means = sorted(sum(rnd.choice(diffs) for _ in diffs) / len(diffs) for _ in range(n))
    return sum(diffs) / len(diffs), means[int(0.025 * n)], means[int(0.975 * n) - 1]


def se_test(diffs: list) -> tuple:
    if len(diffs) < 2:
        return float("nan"), float("nan")
    m = sum(diffs) / len(diffs)
    se = math.sqrt(sum((d - m) ** 2 for d in diffs) / (len(diffs) - 1) / len(diffs))
    return m, se


def paired(rows, a, b):
    A, B = by_career(rows, a), by_career(rows, b)
    return [A[k] - B[k] for k in sorted(set(A) & set(B))]


def get(key):
    return lambda r: r.get(key)


def rate(num, den, shifts=None):
    def f(r):
        if shifts and r["shift"] not in shifts:
            return None
        d = r.get(den, 0)
        return r.get(num, 0) / d if d else None
    return f


def analyse(rows, senses="s1", motion="p2") -> dict:
    out = {}
    m, lo, hi = bootstrap(paired(rows, get(f"shadow_{senses}_logp"), get("shadow_p0_logp")))
    out["senses"] = dict(diff=m, lo=lo, hi=hi, passed=lo > 0)
    m, se = se_test(paired(rows, rate("mirror_k2_right", "mirror_k2_samples"), rate("mirror_k1_right", "mirror_k1_samples")))
    out["mirror"] = dict(diff=m, se=se, passed=m > 2 * se if not math.isnan(se) else False)
    m0, se0 = se_test(paired(rows, rate("mirror_k2_right", "mirror_k2_samples"), rate("mirror_k0_right", "mirror_k0_samples")))
    out["mirror_vs_k0"] = dict(diff=m0, se=se0)
    m, lo, hi = bootstrap(paired(rows, get(f"shadow_{motion}_logp_out"), get("shadow_p1_logp_out")))
    out["prediction"] = dict(diff=m, lo=lo, hi=hi, passed=lo > 0)
    late = {3, 4, 5, 6}
    m, se = se_test(paired(rows, rate("shadow_p3_dest_top1", "shadow_p3_loss_checks", late), rate("shadow_p3x_dest_top1", "shadow_p3x_loss_checks", late)))
    out["personal"] = dict(diff=m, se=se, passed=m > 2 * se if not math.isnan(se) else False)
    loss = {}
    for arm in ("p0", "p1", "p2", "p3"):
        checks = sum(r.get(f"shadow_{arm}_loss_checks", 0) for r in rows)
        loss[arm] = dict(top3=sum(r.get(f"shadow_{arm}_dest_top3", 0) for r in rows) / checks if checks else float("nan"),
                         logp5=mean(r.get(f"shadow_{arm}_logp_5s") for r in rows), logp10=mean(r.get(f"shadow_{arm}_logp_10s") for r in rows),
                         logp20=mean(r.get(f"shadow_{arm}_logp_20s") for r in rows), ms=mean(r.get(f"shadow_{arm}_ms_per_tick") for r in rows))
    out["at_loss"] = loss
    out["careers"] = len({career(r) for r in rows})
    return out


def mean(xs):
    xs = [x for x in xs if x is not None and not (isinstance(x, float) and math.isnan(x))]
    return sum(xs) / len(xs) if xs else float("nan")


def markdown(a: dict) -> str:
    f = lambda x, s="{:+.3f}": "—" if x is None or (isinstance(x, float) and math.isnan(x)) else s.format(x)  # noqa: E731
    lines = [f"{a['careers']} careers. Negative results are reported as they come.\n",
             "**The moment she loses sight of you** (destination arms):\n",
             "| arm | real destination in her top 3 | log p 5 s later | 10 s | 20 s | ms a tick |", "|---|---|---|---|---|---|"]
    for arm, v in a["at_loss"].items():
        lines.append(f"| {arm} | {f(v['top3'], '{:.0%}')} | {f(v['logp5'])} | {f(v['logp10'])} | {f(v['logp20'])} | {f(v['ms'], '{:.2f}')} |")
    s, k, p, q = a["senses"], a["mirror"], a["prediction"], a["personal"]
    lines += ["", "| test | difference | interval or SE | result |", "|---|---|---|---|",
              f"| her senses (fitted − S0, log p) | {f(s['diff'])} | 95% [{f(s['lo'])}, {f(s['hi'])}] | {'pass' if s['passed'] else 'not shown'} |",
              f"| what you know (K2 − K1, right) | {f(k['diff'])} | SE {f(k['se'], '{:.3f}')} | {'pass' if k['passed'] else 'not shown'} |",
              f"| the prediction (chosen − P1, log p out of view) | {f(p['diff'])} | 95% [{f(p['lo'])}, {f(p['hi'])}] | {'pass' if p['passed'] else 'not shown'} |",
              f"| the personal part (P3 − P3 with another's Ledger, top 1) | {f(q['diff'])} | SE {f(q['se'], '{:.3f}')} | {'pass' if q['passed'] else 'not shown'} |"]
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="+")
    ap.add_argument("--senses", default="s1")
    ap.add_argument("--motion", default="p2")
    a = ap.parse_args(argv)
    print(markdown(analyse(load(a.paths), a.senses, a.motion)))


if __name__ == "__main__":
    main()
