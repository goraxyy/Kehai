#!/usr/bin/env python3
"""Fit her senses, the mirror map and the prediction model (0.4.0, build plan §8.5–8.8, C4.10).

    python3 tools/belief/train.py senses --train <traces>… --val <traces>…       S1, S2
    python3 tools/belief/train.py mirror --train … --val …                       the mirror map's numbers
    python3 tools/belief/train.py p1     --train … --val …                       the hand step, fitted
    python3 tools/belief/train.py p2     --train … --val … --legs legs.jsonl     stage 1 (legs), stage 2 (filter)
    python3 tools/belief/train.py all    --train … --val … --legs legs.jsonl     in that order, each frozen in turn

Supervised learning: the answer is where the player really was (the trace's truth lines), or what
they really sensed of her. Nothing here runs unless every training trace passes the parity gate
first. Each fit is made on the training traces and chosen on the validation traces; the held-out
players and the test seeds are never read here (EvalSplits). Arms share everything they aren't
testing: the senses are fitted on today's step (P0), then frozen for every later arm.

Writes, into the game's Belief/Generated folder (or --out): SenseWeights.g.cs, MirrorWeights.g.cs,
PredictionWeights.g.cs, each with its card, and .part.json files for the model pack.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import features as feat  # noqa: E402
import kernels  # noqa: E402
import replay  # noqa: E402
import trace as tr  # noqa: E402

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "learn"))
import unity_meta  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GENERATED = os.path.join(ROOT, "Assets", "!_Project", "_Game", "AI", "Scripts", "Belief", "Generated")

SENSE_NAMES = ["sight_sigma_far", "sight_sigma_near", "hear_confidence", "hear_sigma0", "hear_sigma_per_metre",
               "testimony_sigma0", "testimony_sigma_per_second", "sweep_scale"]
S2_EXTRA = ["wall_gain", "dark_gain"]


# ---- a small Nelder–Mead (no scipy on this Mac) ------------------------------------------------------

def nelder_mead(f, x0, step=0.2, iters=60, lo=None, hi=None):
    x0 = np.asarray(x0, float)
    n = len(x0)
    clip = (lambda x: np.clip(x, lo, hi)) if lo is not None else (lambda x: x)
    pts = [x0] + [clip(x0 + step * np.eye(n)[i] * max(abs(x0[i]), 0.1)) for i in range(n)]
    vals = [f(p) for p in pts]
    for _ in range(iters):
        order = np.argsort(vals)
        pts, vals = [pts[i] for i in order], [vals[i] for i in order]
        centre = np.mean(pts[:-1], axis=0)
        r = clip(centre + (centre - pts[-1]))
        fr = f(r)
        if fr < vals[0]:
            e = clip(centre + 2 * (centre - pts[-1]))
            fe = f(e)
            pts[-1], vals[-1] = (e, fe) if fe < fr else (r, fr)
        elif fr < vals[-2]:
            pts[-1], vals[-1] = r, fr
        else:
            c = clip(centre + 0.5 * (pts[-1] - centre))
            fc = f(c)
            if fc < vals[-1]:
                pts[-1], vals[-1] = c, fc
            else:
                pts = [pts[0]] + [clip(pts[0] + 0.5 * (p - pts[0])) for p in pts[1:]]
                vals = [vals[0]] + [f(p) for p in pts[1:]]
    i = int(np.argmin(vals))
    return pts[i], vals[i]


# ---- objectives -------------------------------------------------------------------------------------

def gate(folders):
    for folder in folders:
        res = replay.parity(folder)
        bad = [r for r in res if not r[3]]
        if not res or bad:
            sys.exit(f"train: the parity gate failed on {folder} ({len(bad)} of {len(res)} shifts); nothing is trained")


# Shifts replay on their own, so a fit's pass shares them among worker processes (the editor is
# closed for C4.10; each worker keeps its maps' distances between passes). Results come back in
# shift order, so the score is the same with any number of workers. KEHAI_FIT_WORKERS=1: no pool.
WORKERS = int(os.environ.get("KEHAI_FIT_WORKERS", "0")) or min(4, os.cpu_count() or 1)
_pool = None


def _score(task):
    folder, path, out_of_view, kw = task
    r = replay.Replay(tr.load_map(folder), use_hint=False, **kw).run(path)
    return r.log_p(out_of_view)


def mean_log_p(folders, out_of_view=False, **kw):
    """Mean over shifts of the replay's log p of the true cell (one shift, one number)."""
    global _pool
    tasks = [(folder, path, out_of_view, kw) for folder in folders for path in tr.shifts(folder)]
    if WORKERS > 1 and len(tasks) > 1:
        if _pool is None:
            import multiprocessing
            _pool = multiprocessing.get_context("spawn").Pool(WORKERS)
        lps = _pool.map(_score, tasks, chunksize=1)
    else:
        lps = [_score(t) for t in tasks]
    scores = [lp for lp in lps if not math.isnan(lp)]
    return float(np.mean(scores)) if scores else float("nan")


def fit_senses(train, val, sid):
    names = SENSE_NAMES + (S2_EXTRA if sid == "s2" else [])
    x0 = np.array([kernels.S0[k] for k in names])

    def loss(x, folders=train):
        return -mean_log_p(folders, motion="p0", senses=sid, sense_numbers=dict(zip(names, x)))

    lo = np.array([0.5 if "sigma" in k else 0.0 for k in names])
    hi = np.array([20.0 if "sigma" in k else 3.0 for k in names])
    x, _ = nelder_mead(loss, x0, lo=lo, hi=hi)
    return dict(zip(names, map(float, x))), -loss(x, val), -loss(x0, val)


def fit_p1(train, val, senses):
    p1 = feat.HAND["p1"]
    names = ["moving", "desire", "habit", "danger", "speed"]
    x0 = np.array([p1[k] for k in names])

    def loss(x, folders=train):
        w = dict(feat.HAND, p1=dict(zip(names, x)))
        return -mean_log_p(folders, True, motion="p1", senses=senses.get("id", "s0"), sense_numbers=senses, weights=w)

    x, _ = nelder_mead(loss, x0, lo=np.zeros(5), hi=np.array([1.0, 3.0, 3.0, 1.0, 2.0]))
    return dict(zip(names, map(float, x))), -loss(x, val)


# Stage 1: the choice and the speed mix, from legs (minutes, no Unity).
def stage1(legs: list, traces: list) -> dict:
    """Multinomial logit over each leg's candidate places: kind, ln(1 + metres), waited.
    Legs are matched to the trace's last "features" op before they start."""
    rows = []
    for folder in traces:
        m = tr.load_map(folder)
        for path in tr.shifts(folder):
            hdr = tr.header(path)
            began = hdr.get("t", 0.0)               # trace times are the game clock; legs' are the shift's
            ops = [o for o in tr.ops(path) if o["op"] == "features"]
            # A leg belongs to the trace of its own career: the same seed, player and shift.
            mine = [l for l in legs if l.get("found") == "logged" and l.get("shift") == hdr.get("shift")
                    and l.get("seed") == hdr.get("seed") and l.get("agent") == hdr.get("agent")]
            for leg in mine:
                prior = [o for o in ops if o["t"] - began <= leg["t0"]]
                if not prior:
                    continue
                op = prior[-1]
                places = [p for p in op["places"] if p["cell"] >= 0]
                if not places:
                    continue
                tx, tz = leg["to"]
                d2 = [(p["x"] - tx) ** 2 + (p["z"] - tz) ** 2 for p in places]
                chosen = int(np.argmin(d2))
                if d2[chosen] > 9.0:
                    continue
                fx, fz = leg["from"]
                start = feat.nearest_cell(m, fx, fz)
                if start < 0:
                    continue
                x = []
                for p in places:
                    k = np.zeros(len(feat.KINDS))
                    k[feat.KINDS.index(p["kind"])] = 1
                    metres = m.distances(p["cell"])[start]
                    x.append(np.concatenate([k, [math.log1p(metres if not math.isinf(metres) else 999.0), p["waited"] / (p["waited"] + 30.0)]]))
                rows.append((np.array(x), chosen))
    w = np.zeros(len(feat.KINDS) + 2)
    w[len(feat.KINDS)] = feat.HAND["distance"]
    for _ in range(300):
        g = np.zeros_like(w)
        for x, c in rows:
            z = x @ w
            p = np.exp(z - z.max())
            p /= p.sum()
            g += x[c] - p @ x
        w += 0.05 * g / max(1, len(rows)) - 1e-3 * w
    speeds = [l["speed"] for l in legs if l.get("metres", 0) > 3 and l.get("found") == "logged"]
    share = np.zeros(3)
    for s in speeds:
        share[int(np.argmin([abs(s - v) for v in feat.SPEEDS]))] += 1
    share = (share / share.sum()).tolist() if share.sum() > 0 else feat.HAND["speed_share"]
    out = dict(feat.HAND)
    out["kind"] = [float(v) for v in w[:len(feat.KINDS)]]
    out["kind"][feat.KINDS.index("Stay")] = feat.HAND["kind"][feat.KINDS.index("Stay")]
    out["kind"][feat.KINDS.index("TurnBack")] = feat.HAND["kind"][feat.KINDS.index("TurnBack")]
    out["distance"] = float(w[len(feat.KINDS)])
    out["wait"] = float(w[len(feat.KINDS) + 1])
    out["speed_share"] = share
    out["legs_used"] = len(rows)
    return out


# Stage 2: the same numbers through the filter, on the out-of-view log p.
def stage2(w1: dict, train, val, senses, motion="p2"):
    names = ["distance", "between", "heading", "avoid", "ledger_answer", "ledger_hide"]

    def weights(x):
        return dict(w1, **dict(zip(names, map(float, x))))

    def loss(x, folders=train):
        return -mean_log_p(folders, True, motion=motion, senses=senses.get("id", "s0"), sense_numbers=senses, weights=weights(x))

    x0 = np.array([w1[k] for k in names])
    x, _ = nelder_mead(loss, x0, iters=40)
    return weights(x), -loss(x, val)


# The mirror map's numbers (§8.8): how likely the player is to notice her, by sense and distance.
def fit_mirror(train):
    """Sight: a logistic of "the player saw her" on the distance between them, over the ticks she
    sees the player. Sound: the share of her noises the player heard within 2 s, when it carried to
    them. The spread speed stays hand-set (her walk) unless the validation run says otherwise."""
    xs, ys, heard, noises = [], [], 0, 0
    for folder in train:
        for path in tr.shifts(folder):
            pending = []
            last = None
            for op in tr.ops(path):
                if op["op"] == "truth":
                    last = op
                    if op["sees"]:
                        xs.append(math.hypot(op["karen"][0] - op["x"], op["karen"][1] - op["z"]))
                        ys.append(1.0 if op["saw"] else 0.0)
                    for n in list(pending):
                        if op["t"] - n["t"] > 2.0:
                            pending.remove(n)
                            noises += 1
                        elif op["heard"]:
                            pending.remove(n)
                            noises += 1
                            heard += 1
                elif op["op"] == "her_noise" and last is not None:
                    if math.hypot(op["x"] - last["x"], op["z"] - last["z"]) <= op["carry"]:
                        pending.append(op)
    w = np.array([2.5, -0.15])
    x = np.array(xs)
    y = np.array(ys)
    for _ in range(500):
        if len(x) == 0:
            break
        z = w[0] + w[1] * x
        p = 1 / (1 + np.exp(-z))
        w += 0.05 * np.array([np.mean(y - p), np.mean((y - p) * x)])
    return {"sight_bias": float(w[0]), "sight_per_metre": float(-w[1]),
            "sound_notice": float(heard / noises) if noises else 0.8, "spread_speed": 2.5, "samples": len(xs), "noises": noises}


# ---- writing ------------------------------------------------------------------------------------------

def cs(v: float) -> str:
    return repr(float(np.float32(v))) + "f"


def write_weights(out: str, name: str, numbers: dict, card: dict, builder: str):
    os.makedirs(out, exist_ok=True)
    flat = {k: float(v) for k, v in numbers.items() if isinstance(v, (int, float))}
    with open(os.path.join(out, name + ".part.json"), "w") as f:
        json.dump({"kind": "weights", "name": name, "card": card, "values": flat}, f, sort_keys=True)
    with open(os.path.join(out, name + ".card.md"), "w") as f:
        f.write(f"# Model card: {name}\n\n| | |\n|---|---|\n" + "".join(f"| {k} | {v} |\n" for k, v in card.items()))
    with open(os.path.join(out, name + ".g.cs"), "w") as f:
        f.write(builder)
    for ext in (".g.cs", ".card.md", ".part.json"):
        unity_meta.write_meta(os.path.join(out, name + ext))


def sense_cs(fits: dict, card: dict) -> str:
    lines = ["// <auto-generated> by tools/belief/train.py — do not edit; refit instead.", "namespace Kehai.Karen.Generated", "{",
             "    public static class SenseWeightsTable", "    {", "        public static Kehai.Karen.SenseModel Build(string id)", "        {",
             f"            var s = new Kehai.Karen.SenseModel {{ Id = id, Version = \"{card['version']}\", Trained = true }};"]
    for sid, nums in fits.items():
        lines.append(f"            if (id == \"{sid}\")")
        lines.append("            {")
        lines += [f"                s.Set(\"{k}\", {cs(v)});" for k, v in nums.items()]
        lines.append("                return s;")
        lines.append("            }")
    lines += ["            return null;", "        }", "    }", "}", ""]
    return "\n".join(lines)


def table_cs(name: str, cls: str, numbers: dict, version: str) -> str:
    lines = ["// <auto-generated> by tools/belief/train.py — do not edit; refit instead.", "namespace Kehai.Karen.Generated", "{",
             f"    public static class {name}Table", "    {", f"        public static Kehai.Karen.{cls} Build()", "        {",
             f"            var w = new Kehai.Karen.{cls} {{ Version = \"{version}\", Trained = true }};"]
    lines += [f"            w.Set(\"{k}\", {cs(v)});" for k, v in numbers.items()]
    lines += ["            return w;", "        }", "    }", "}", ""]
    return "\n".join(lines)


def prediction_numbers(w: dict) -> dict:
    out = {k: w[k] for k in ("distance", "wait", "between", "heading", "clock_late", "ledger_answer", "ledger_hide", "avoid", "danger_radius")}
    for i, v in enumerate(w["kind"]):
        out["kind_" + feat.KINDS[i].lower()] = v
    for i, v in enumerate(w["speed_share"]):
        out[f"speed_{i}"] = v
    for k, v in w["p1"].items():
        out["p1_" + {"moving": "moving", "desire": "desire", "habit": "habit", "danger": "danger", "speed": "speed"}[k]] = v
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("what", choices=["senses", "mirror", "p1", "p2", "all"])
    ap.add_argument("--train", nargs="+", required=True)
    ap.add_argument("--val", nargs="+", required=True)
    ap.add_argument("--legs")
    ap.add_argument("--out", default=GENERATED)
    ap.add_argument("--version", default="fit")
    a = ap.parse_args(argv)
    gate(a.train + a.val)
    report = {}
    senses = {"id": "s0"}
    if a.what in ("senses", "all"):
        fits = {}
        base = None
        for sid in ("s1", "s2"):
            nums, val_score, base = fit_senses(a.train, a.val, sid)
            fits[sid] = nums
            report[sid] = val_score
        report["s0"] = base
        best = max(("s1", "s2"), key=lambda s: report[s])
        senses = dict(fits[best], id=best)
        card = {"version": a.version, "validation": json.dumps({k: round(v, 4) for k, v in report.items()}), "chosen": best}
        write_weights(a.out, "SenseWeights", fits[best], card, sense_cs(fits, card))
    if a.what in ("mirror", "all"):
        mw = fit_mirror(a.train)
        report["mirror"] = mw
        nums = {k: v for k, v in mw.items() if k not in ("samples", "noises")}
        write_weights(a.out, "MirrorWeights", nums, {"version": a.version, "samples": mw["samples"], "noises": mw["noises"]},
                      table_cs("MirrorWeights", "MirrorWeights", nums, a.version))
    if a.what in ("p1", "all"):
        gains, val = fit_p1(a.train, a.val, senses)
        report["p1"] = val
        w = dict(feat.HAND, p1=gains)
        write_weights(a.out, "PredictionWeights", prediction_numbers(w), {"version": a.version, "stage": "p1"},
                      table_cs("PredictionWeights", "PredictionWeights", prediction_numbers(w), a.version))
    if a.what in ("p2", "all"):
        legs = [json.loads(x) for x in open(a.legs)] if a.legs else []
        w1 = stage1(legs, a.train)
        w2, val2 = stage2(w1, a.train, a.val, senses, "p2")
        w3, val3 = stage2(w2, a.train, a.val, senses, "p3")
        report.update(p2=val2, p3=val3)
        w = w3 if val3 > val2 else w2
        write_weights(a.out, "PredictionWeights", prediction_numbers(w), {"version": a.version, "stage": "p2/p3", "legs": w1.get("legs_used", 0)},
                      table_cs("PredictionWeights", "PredictionWeights", prediction_numbers(w), a.version))
    print(json.dumps(report, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
