"""Train the motion models (m1–m4) on belief traces, and choose among them on validation.

    python train.py stage1 m3 --train data/train --val data/val
    python train.py stage2 m3 --train data/train --val data/val [--init out/m3_stage1.pt]
    python train.py select                         # best learned model on validation → out/weights.npz

  stage 1   trajectories: start a rollout at the player's true cell (and, for m4, the heading
            they're walking in) every few seconds and roll the kernel alone forward up to 30 s,
            raising log p of where they really are (no evidence, no leak).
  stage 2   through the filter: replay each shift's evidence (replay.py) with the model as the
            predict step and no Director's hint, and raise −log p of the true cell at each 5 Hz
            sample — weight 1 out of view, 0.25 in view: the metric itself. Truncated backprop
            every 20 s with the state carried on (a learnable histogram filter: Jonschkowski &
            Brock, 2016).

m1 (the hand step, tuned) is trained by stage 2 alone (tune_hand.py). Early stopping and the
choice of model read validation only; the test set is never opened here. Data is .npz from
`python trace.py pack`. Everything runs on CPU in well under 2 GB.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
import time
from pathlib import Path

import numpy as np
import torch

import kernels
import replay
from features import FeatureState
from trace import OP_FAST, OP_MAP, OP_PREDICT, OP_SAMPLE, OP_SLOW, Shift, load_shift

HERE = Path(__file__).resolve().parent
OUT = HERE / "out"
HORIZON = 30.0


def shift_files(folder: str | Path) -> list[Path]:
    return sorted(Path(folder).glob("*.npz"))


def spread(files: list, limit: int) -> list:
    """`limit` files spread evenly over the list (names sort by rung and player), or all."""
    if not limit or limit >= len(files):
        return files
    return [files[int(i * len(files) / limit)] for i in range(limit)]


def log(msg: str, file: Path | None = None):
    line = f"[{time.strftime('%H:%M:%S')}] {msg}"
    print(line, flush=True)
    if file:
        with open(file, "a") as f:
            f.write(line + "\n")


# ---- the score -----------------------------------------------------------------------------

def out_of_view_logp(s: Shift, logp: np.ndarray) -> float:
    """BeliefAccuracy's per-shift number: mean log p over the samples out of her view."""
    keep = (s.sa_truth >= 0) & (s.sa_view == 0) & np.isfinite(logp)
    return float(logp[keep].mean()) if keep.any() else float("nan")


def validate(model, files: list[Path], limit: int = 0) -> dict:
    """Replay each validation shift without the hint; mean of the per-shift means."""
    scores, inview = [], []
    for f in files[: limit or None]:
        s = load_shift(f)
        r = replay.replay(s, model, bias=False)
        scores.append(out_of_view_logp(s, r.logp))
        keep = (s.sa_truth >= 0) & (s.sa_view == 1) & np.isfinite(r.logp)
        if keep.any():
            inview.append(float(r.logp[keep].mean()))
    v = np.array(scores, float)
    return dict(out_of_view_logp=float(np.nanmean(v)), in_view_logp=float(np.mean(inview)) if inview else float("nan"),
                shifts=int(np.isfinite(v).sum()))


# ---- stage 1: rollouts from the truth ------------------------------------------------------

def heading_of(vx: float, vz: float) -> int:
    """Still (0) below a slow walk, else 1 + the nearest of the eight directions (LinkCells' order)."""
    if math.hypot(vx, vz) < 0.5:
        return 0
    k = int(round(math.atan2(vz, vx) / (math.pi / 4))) % 8      # 0 = +x, counter-clockwise
    dx = [1, 1, 0, -1, -1, -1, 0, 1][k]
    dz = [0, 1, 1, 1, 0, -1, -1, -1][k]
    slot = {(1, 0): 0, (-1, 0): 1, (0, 1): 2, (0, -1): 3, (1, 1): 4, (1, -1): 5, (-1, 1): 6, (-1, -1): 7}[(dx, dz)]
    return 1 + slot


def rollouts(s: Shift, model, start_every: float = 3.0, horizon: float = HORIZON, segment: float = 10.0,
             grad: bool = False, on_segment=None, record=None):
    """Roll the kernel forward from the true cell (and heading), with no evidence. Every 5 Hz
    sample scores each open rollout on where the player really is. With `grad`, the mean
    −log p of a `segment` of sim time goes to `on_segment(loss, count)`; rollouts then carry on
    detached. `record(age, logp)` sees every score (the horizon curve)."""
    hc = model.headings
    fs = None
    kernel, dirty = None, True
    cols: list[float] = []          # start times of the open rollouts
    state = None                    # [n, R] or [n, 9, R]
    last_start = -1e9
    loss, count, seg_start = torch.zeros(()), 0, None
    ctx = torch.enable_grad() if grad else torch.no_grad()
    with ctx:
        for code, row in s.ops:
            if code == OP_MAP:
                fs = FeatureState(s.maps[row]); dirty = True
                cols, state = [], None
            elif code == OP_SLOW:
                fs.set_slow(s, row); dirty = True
            elif code == OP_FAST:
                fs.set_fast(s, row); dirty = True
            elif code == OP_PREDICT:
                if state is None:
                    continue
                if dirty:
                    kernel = model.build(fs); dirty = False
                state = model.apply(kernel, state, float(s.pr_dt[row]), float(s.pr_speed[row]))
            elif code == OP_SAMPLE:
                truth = int(s.sa_truth[row])
                t = float(s.sa_t[row])
                if state is not None and cols and truth >= 0:
                    marg = state if hc == 1 else state.sum(1)
                    p = marg[truth]                              # [R]
                    ages = t - torch.tensor(cols)
                    live = (ages > 0) & (ages <= horizon)
                    if live.any():
                        lp = torch.log(p[live] + replay.FLOOR)
                        if record is not None:
                            for a, v in zip(ages[live].tolist(), lp.detach().tolist()):
                                record(a, v)
                        if grad:
                            loss = loss - lp.sum()
                            count += int(live.sum())
                # Retire old rollouts, start a new one.
                if cols:
                    keep = [i for i, t0 in enumerate(cols) if t - t0 <= horizon]
                    if len(keep) < len(cols):
                        cols = [cols[i] for i in keep]
                        state = state[..., keep] if keep else None
                if truth >= 0 and t - last_start >= start_every:
                    last_start = t
                    n = fs.m.n
                    col = torch.zeros(n, hc, 1) if hc > 1 else torch.zeros(n, 1)
                    if hc > 1:
                        col[truth, heading_of(float(s.sa_vx[row]), float(s.sa_vz[row])), 0] = 1
                    else:
                        col[truth, 0] = 1
                    state = col if state is None else torch.cat([state, col], -1)
                    cols.append(t)
                if grad:
                    if seg_start is None:
                        seg_start = t
                    if t - seg_start >= segment:
                        if count and on_segment:
                            on_segment(loss, count)
                        loss, count, seg_start = torch.zeros(()), 0, t
                        if state is not None:
                            state = state.detach()
                        dirty = True
        if grad and count and on_segment:
            on_segment(loss, count)


# ---- training loops --------------------------------------------------------------------------

def make_optimiser(model, lr: float):
    return torch.optim.Adam(model.parameters(), lr=lr)


def train(model_id: str, stage: int, train_dir: str, val_dir: str, epochs: int, lr: float, init: str | None,
          max_hours: float, limit: int, val_limit: int, out: Path, start_every: float = 3.0) -> dict:
    out.mkdir(parents=True, exist_ok=True)
    tag = f"{model_id}_stage{stage}"
    logfile = out / f"{tag}.log"
    model = kernels.make(model_id)
    if init:
        model.load_state_dict(torch.load(init))
        log(f"{tag}: starting from {init}", logfile)
    opt = make_optimiser(model, lr)
    files = spread(shift_files(train_dir), limit)
    val = shift_files(val_dir)
    started = time.time()
    best = validate(model, val, val_limit)
    log(f"{tag}: {len(files)} training shifts, {len(val)} validation; before training, validation "
        f"out-of-view log p {best['out_of_view_logp']:.4f}", logfile)
    torch.save(model.state_dict(), out / f"{tag}.pt")
    history = [dict(epoch=0, **best)]
    bad = 0
    for epoch in range(1, epochs + 1):
        random.Random(epoch).shuffle(files)
        losses = []

        def step(loss, weight):
            opt.zero_grad()
            (loss / weight).backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 5.0)
            opt.step()
            losses.append(float(loss) / weight)

        for i, f in enumerate(files):
            s = load_shift(f)
            if stage == 1:
                rollouts(s, model, start_every=start_every, grad=True, on_segment=step)
            else:
                replay.replay(s, model, bias=False, grad=True, on_window=step)
            if (i + 1) % 20 == 0:
                log(f"{tag} epoch {epoch}: {i + 1}/{len(files)} shifts, loss {np.mean(losses[-200:]):.4f}", logfile)
            if (time.time() - started) / 3600 > max_hours:
                break
        v = validate(model, val, val_limit)
        history.append(dict(epoch=epoch, loss=float(np.mean(losses)) if losses else None, **v))
        improved = v["out_of_view_logp"] > best["out_of_view_logp"] + 1e-4
        log(f"{tag} epoch {epoch}: train loss {np.mean(losses) if losses else float('nan'):.4f}, validation out-of-view "
            f"log p {v['out_of_view_logp']:.4f} (best {max(best['out_of_view_logp'], v['out_of_view_logp']):.4f})"
            f"{'' if improved else ' — no better'}", logfile)
        if improved:
            best = v
            bad = 0
            torch.save(model.state_dict(), out / f"{tag}.pt")
        else:
            bad += 1
            if bad >= 2:
                log(f"{tag}: stopping early", logfile)
                break
        if (time.time() - started) / 3600 > max_hours:
            log(f"{tag}: out of time ({max_hours} h)", logfile)
            break
    result = dict(model=model_id, stage=stage, best=best, history=history, lr=lr, init=init, start_every=start_every,
                  train=str(train_dir), shifts=len(files), hours=(time.time() - started) / 3600)
    (out / f"{tag}.json").write_text(json.dumps(result, indent=2))
    return result


# ---- choosing --------------------------------------------------------------------------------

def select(out: Path, card_extra: dict) -> None:
    """The best learned model (m2–m4) by validation out-of-view log p, then weights.npz."""
    scores, packs = {}, {}
    for mid in ("m2", "m3", "m4"):
        f = out / f"{mid}_stage2.json"
        if not f.exists():
            continue
        r = json.loads(f.read_text())
        scores[mid] = r["best"]["out_of_view_logp"]
        model = kernels.make(mid)
        model.load_state_dict(torch.load(out / f"{mid}_stage2.pt"))
        packs[mid] = model.pack()
    m1 = json.loads((out / "m1.json").read_text())
    best = max(scores, key=scores.get) if scores else "m0"
    card = dict(card_extra)
    card["validation out-of-view log p (no hint)"] = ", ".join(
        [f"m0 {m1['m0_validation']['out_of_view_logp']:.3f}", f"m1 {m1['best']['out_of_view_logp']:.3f}"]
        + [f"{k} {v:.3f}" for k, v in scores.items()])
    card["best learned model (validation)"] = best
    np.savez(out / "weights.npz", m1=np.array([m1["gains"][k] for k in kernels.M1_ORDER], np.float32),
             best=np.array(best), card=np.array(json.dumps(card)), **packs)
    print(json.dumps(card, indent=2))
    print(f"wrote {out / 'weights.npz'}: best learned model {best}")


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["stage1", "stage2", "select"])
    ap.add_argument("model", nargs="?", choices=["m2", "m3", "m4"])
    ap.add_argument("--train", default=str(HERE / "data/train"))
    ap.add_argument("--val", default=str(HERE / "data/val"))
    ap.add_argument("--out", default=str(OUT))
    ap.add_argument("--epochs", type=int, default=6)
    ap.add_argument("--lr", type=float, default=None)
    ap.add_argument("--init")
    ap.add_argument("--hours", type=float, default=8.0)
    ap.add_argument("--limit", type=int, default=0, help="train on N shifts spread over the set")
    ap.add_argument("--val-limit", type=int, default=0)
    ap.add_argument("--threads", type=int, default=4)
    ap.add_argument("--start-every", type=float, default=3.0, help="stage 1: seconds between rollouts")
    ap.add_argument("--card", default="", help="JSON of extra model-card fields (data seeds, profiles…)")
    a = ap.parse_args(argv)
    torch.set_num_threads(a.threads)
    torch.manual_seed(0)
    out = Path(a.out)
    if a.cmd == "select":
        select(out, json.loads(a.card) if a.card else {})
        return 0
    stage = 1 if a.cmd == "stage1" else 2
    lr = a.lr if a.lr is not None else (3e-3 if stage == 1 else 1e-3)
    init = a.init
    if stage == 2 and init is None and (out / f"{a.model}_stage1.pt").exists():
        init = str(out / f"{a.model}_stage1.pt")
    train(a.model, stage, a.train, a.val, a.epochs, lr, init, a.hours, a.limit, a.val_limit, out, a.start_every)
    return 0


if __name__ == "__main__":
    sys.exit(main())
