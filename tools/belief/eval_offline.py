"""Offline scores for the motion models, from packed traces (no Unity).

    python eval_offline.py horizon --data <dir> [--weights out/weights.npz]     the horizon curve
    python eval_offline.py replay  --data <dir> [--weights out/weights.npz]     the filter, replayed

  horizon   the motion model on its own: from the player's true cell (and heading), with no
            evidence, mean log p of where they really are k = 1–30 s later. Rollouts start every
            3 s of each shift.
  replay    the whole filter without the Director's hint, as the shadows run it in game:
            out-of-view and in-view log p per model (validation numbers come from here).

Both print a markdown table and write out/<name>.json. The test set is for the Unity run (the
shadow filters); `horizon --data <test>` is its one offline number, run after the test run.
"""
from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path

import numpy as np
import torch

import kernels
import replay
import train
from trace import load_shift

MODELS = ["m0", "m1", "m2", "m3", "m4"]


def load_models(weights: str | None, ids: list[str]) -> dict:
    w = replay.load_weights(weights) if weights else {}
    out = {}
    for mid in ids:
        base = mid.split("/")[0]
        if base == "m1":
            out[mid] = kernels.HandKernel(avoid=True, gains=w.get("m1"))
        elif base in ("m2", "m3", "m4"):
            if base not in w:
                continue
            out[mid] = kernels.make(mid)
            out[mid].load_pack(w[base])
        else:
            out[mid] = kernels.make(mid)
    return out


def horizon(files, models: dict, start_every: float = 3.0) -> dict:
    """{model: {k seconds: (mean log p, count)}}."""
    result = {}
    for mid, model in models.items():
        sums, counts = defaultdict(float), defaultdict(int)

        def record(age, lp):
            k = int(round(age))
            if 1 <= k <= 30:
                sums[k] += lp
                counts[k] += 1

        for f in files:
            train.rollouts(load_shift(f), model, start_every=start_every, horizon=30.5, record=record)
        result[mid] = {k: (sums[k] / counts[k], counts[k]) for k in sorted(counts)}
        print(f"  {mid}: {sum(counts.values())} scores", flush=True)
    return result


def replay_scores(files, models: dict) -> dict:
    out = {}
    for mid, model in models.items():
        per_shift = []
        for f in files:
            s = load_shift(f)
            r = replay.replay(s, model, bias=False)
            seen = s.sa_truth >= 0
            oov = seen & (s.sa_view == 0)
            iv = seen & (s.sa_view == 1)
            per_shift.append(dict(seed=int(s.seed), agent=s.agent, shift=int(s.shift), rung=s.rung,
                                  out_of_view=float(r.logp[oov].mean()) if oov.any() else None,
                                  in_view=float(r.logp[iv].mean()) if iv.any() else None))
        out[mid] = per_shift
        oo = [p["out_of_view"] for p in per_shift if p["out_of_view"] is not None]
        print(f"  {mid}: out of view {np.mean(oo):.4f} over {len(oo)} shifts", flush=True)
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("what", choices=["horizon", "replay"])
    ap.add_argument("--data", required=True)
    ap.add_argument("--weights")
    ap.add_argument("--models", default=",".join(MODELS))
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--out", default=str(train.OUT))
    ap.add_argument("--name")
    ap.add_argument("--threads", type=int, default=4)
    a = ap.parse_args(argv)
    torch.set_num_threads(a.threads)
    files = train.shift_files(a.data)[: a.limit or None]
    models = load_models(a.weights, a.models.split(","))
    print(f"{a.what} on {len(files)} shifts from {a.data}: {', '.join(models)}")
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    name = a.name or f"{a.what}_{Path(a.data).name}"
    if a.what == "horizon":
        res = horizon(files, models)
        ks = [1, 2, 3, 5, 10, 15, 20, 30]
        print("\n| model | " + " | ".join(f"{k} s" for k in ks) + " |")
        print("|---|" + "---|" * len(ks))
        for mid, curve in res.items():
            print(f"| {mid} | " + " | ".join(f"{curve[k][0]:.2f}" if k in curve else "—" for k in ks) + " |")
        (out / f"{name}.json").write_text(json.dumps({m: {str(k): v for k, v in c.items()} for m, c in res.items()}, indent=1))
    else:
        res = replay_scores(files, models)
        (out / f"{name}.json").write_text(json.dumps(res, indent=1))
        print("\n| model | out-of-view log p | in-view log p |")
        print("|---|---|---|")
        for mid, rows in res.items():
            oo = [p["out_of_view"] for p in rows if p["out_of_view"] is not None]
            iv = [p["in_view"] for p in rows if p["in_view"] is not None]
            print(f"| {mid} | {np.mean(oo):.3f} ± {np.std(oo, ddof=1) / np.sqrt(len(oo)):.3f} | {np.mean(iv):.3f} |")
    print(f"\nwrote {out / (name + '.json')}")


if __name__ == "__main__":
    main()
