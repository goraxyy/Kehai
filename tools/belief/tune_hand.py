"""Tune the hand step (m1): the shipped structure, its gains fitted to data, plus keeping away
from Karen — the fair baseline for the learned models.

    python tune_hand.py --train data/train --val data/val

The gains (moving share, desire gain, habit gain, avoidance, leak, speed scale, and the danger
layer's radius and trail weight) are fitted the way the learned models' stage 2 is: through a
replay of the filter without the Director's hint, on the training shifts, with validation
choosing when to stop. So m1 and m2–m4 see the same data and are stopped by the same rule.
Writes out/m1.json (the gains, their validation score, and m0's for comparison).
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import torch

import kernels
import train


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--train", default=str(train.HERE / "data/train"))
    ap.add_argument("--val", default=str(train.HERE / "data/val"))
    ap.add_argument("--out", default=str(train.OUT))
    ap.add_argument("--epochs", type=int, default=4)
    ap.add_argument("--lr", type=float, default=0.03)
    ap.add_argument("--hours", type=float, default=3.0)
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--val-limit", type=int, default=0)
    ap.add_argument("--threads", type=int, default=4)
    a = ap.parse_args(argv)
    torch.set_num_threads(a.threads)
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)

    val = train.shift_files(a.val)
    m0 = train.validate(kernels.make("m0"), val, a.val_limit)
    train.log(f"m0 (shipped) validation out-of-view log p {m0['out_of_view_logp']:.4f}", out / "m1_stage2.log")
    result = train.train("m1", 2, a.train, a.val, a.epochs, a.lr, None, a.hours, a.limit, a.val_limit, out)

    model = kernels.make("m1")
    model.load_state_dict(torch.load(out / "m1_stage2.pt"))
    gains = model.gains_float()
    summary = dict(gains=gains, best=result["best"], m0_validation=m0, history=result["history"])
    (out / "m1.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(gains, indent=2))


if __name__ == "__main__":
    main()
