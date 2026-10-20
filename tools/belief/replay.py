"""Karen's belief filter, re-run in PyTorch from a trace, with any motion model.

The evidence is replayed exactly as C# computed it (the trace holds every likelihood), so the
only thing that can differ is the predict step. With the m0 kernel the replay must give the
log p C# logged on every sample — that is the parity gate, and nothing is trained until it
passes:

    python replay.py parity <trace.kbt.gz | folder> [--tol 1e-3]

  * with the Director's hint replayed, against Karen's own grid;
  * without it, against the m0 shadow (BeliefShadows), which never gets the hint;
  * the motion features, against the probes C# wrote (features.check_probes).

`replay()` is also what training runs (train.py: stage 2 backpropagates through it).
"""
from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

import numpy as np
import torch

import kernels
from features import FeatureState, check_kernel_probes, check_probes
from trace import (OP_BIAS, OP_EVIDENCE, OP_FAST, OP_MAP, OP_PREDICT, OP_RESET, OP_SAMPLE, OP_SLOW, Shift,
                   load_shift, read_trace)

FLOOR = 1e-6


@dataclass
class Result:
    logp: np.ndarray          # per sample (NaN where the player was off the map)
    collapses: int
    dists: list | None = None  # marginal per sample, when asked for


def load(path: str | Path) -> Shift:
    path = str(path)
    return load_shift(path) if path.endswith(".npz") else read_trace(path)


def replay(s: Shift, model, bias: bool = False, grad: bool = False, window: float = 20.0,
           on_window=None, keep_dists: bool = False, view_weight: float = 0.25, csharp_sums: bool = False) -> Result:
    """Run the filter over the shift. With `grad`, the loss −log(p + 1e-6) of the true cell
    (weight 1 out of view, `view_weight` in view) is collected over `window` seconds of sim
    time and handed to `on_window(loss, weight)`, which steps the optimiser; the state is then
    carried on, detached (truncated backprop through time).

    `csharp_sums` normalises as BeliefGrid does — a float32 running sum over the cells (in
    double with headings) — so the parity gate can hold the replay to C# within ~1e-5 rather
    than within C#'s own round-off (~1e-4 of the total)."""
    hc = model.headings
    state = prior = hp = None
    fs: FeatureState | None = None
    kernel = None
    dirty = True
    n = 0
    logp = np.full(len(s.sa_t), np.nan, np.float32)
    dists = [] if keep_dists else None
    collapses = 0
    loss = torch.zeros(())
    weight = 0.0
    window_start = None
    ctx = torch.enable_grad() if grad else torch.no_grad()

    def heading_prior():
        p = model.heading_prior
        return None if p is None else p

    def expand(b):
        if hc == 1:
            return b
        return b[:, None] * hp[None, :]

    def normalise(st):
        nonlocal collapses
        if csharp_sums:
            flat = st.detach().numpy().ravel()
            total = np.float32(flat.astype(np.float64).sum()) if hc > 1 else np.cumsum(flat, dtype=np.float32)[-1]
            if not np.isfinite(total) or total < np.float32(1e-12):
                collapses += 1
                return expand(prior.clone())
            return st * float(np.float32(1) / total)
        total = st.sum()
        if not bool(torch.isfinite(total)) or float(total) < 1e-12:
            collapses += 1
            return expand(prior.clone())
        return st / total

    with ctx:
        hp = heading_prior()
        for code, row in s.ops:
            if code == OP_MAP:
                fs = FeatureState(s.maps[row])
                n = fs.m.n
                dirty = True
            elif code == OP_SLOW:
                fs.set_slow(s, row)
                dirty = True
            elif code == OP_FAST:
                fs.set_fast(s, row)
                dirty = True
            elif code == OP_RESET:
                b = torch.as_tensor(s.re_b[row][:n].copy(), dtype=torch.get_default_dtype())
                prior = torch.as_tensor(s.re_prior[row][:n].copy(), dtype=torch.get_default_dtype())
                if hc > 1:
                    hp = heading_prior()
                state = expand(b)
            elif code == OP_EVIDENCE:
                a, k = s.ev_start[row], s.ev_count[row]
                mult = torch.full((n,), float(s.ev_floor[row]))
                mult[torch.as_tensor(s.ev_cells[a:a + k].astype(np.int64))] = torch.as_tensor(s.ev_values[a:a + k], dtype=mult.dtype)
                state = normalise(state * (mult if hc == 1 else mult[:, None]))
            elif code == OP_BIAS:
                if not bias:
                    continue
                region, amount = int(s.bias_region[row]), float(np.clip(s.bias_amount[row], 0, 1))
                cells = torch.as_tensor(fs.m.region_cells(region).astype(np.int64))
                if len(cells) == 0 or amount <= 0:
                    continue
                state = state * (np.float32(1) - np.float32(amount))
                share = np.float32(amount) / np.float32(len(cells))
                add = torch.zeros_like(state)
                add[cells] = float(share) if hc == 1 else float(share) * hp
                state = normalise(state + add)
            elif code == OP_PREDICT:
                if dirty:
                    kernel = model.build(fs)
                    dirty = False
                dt, speed = float(s.pr_dt[row]), float(s.pr_speed[row])
                moved = model.apply(kernel, state, dt, speed)
                u = torch.clamp(model.leak * np.float32(dt), 0, 1)
                state = (1 - u) * moved + u * expand(prior)
            elif code == OP_SAMPLE:
                truth = int(s.sa_truth[row])
                marg = state if hc == 1 else state.sum(1)
                if keep_dists:
                    dists.append(marg.detach().numpy().copy())
                if truth < 0 or truth >= n:
                    continue
                p = marg[truth]
                logp[row] = np.log(max(float(p), FLOOR))
                if grad:
                    t = float(s.sa_t[row])
                    if window_start is None:
                        window_start = t
                    w = view_weight if s.sa_view[row] else 1.0
                    loss = loss - w * torch.log(p + FLOOR)
                    weight += w
                    if t - window_start >= window:
                        if weight > 0 and on_window is not None:
                            on_window(loss, weight)
                        loss, weight, window_start = torch.zeros(()), 0.0, t
                        state = state.detach()
                        dirty = True   # the parameters moved: rebuild the kernel from them
                        hp = heading_prior()
                        if prior is not None:
                            prior = prior.detach()
        if grad and weight > 0 and on_window is not None:
            on_window(loss, weight)
    return Result(logp=logp, collapses=collapses, dists=dists)


# ---- the parity gate -----------------------------------------------------------------------

def parity(s: Shift, tol: float, weights: dict | None = None) -> tuple[bool, list[str]]:
    lines, ok = [], True
    seen = s.sa_truth >= 0
    m0 = kernels.make("m0")

    r = replay(s, m0, bias=True, csharp_sums=True)
    err = np.abs(r.logp[seen] - s.sa_own[seen])
    worst = float(err.max()) if err.size else 0.0
    ok &= worst <= tol
    lines.append(f"  m0 with the hint vs her own grid: {seen.sum()} samples, max |Δ log p| {worst:.2e}, "
                 f"mean {float(err.mean()) if err.size else 0:.1e}, collapses {r.collapses} (C#: {s.end_collapses})")

    if "m0" in s.shadows:
        k = s.shadows.index("m0")
        r = replay(s, m0, bias=False, csharp_sums=True)
        err = np.abs(r.logp[seen] - s.sa_shadow[seen, k])
        worst = float(err.max()) if err.size else 0.0
        ok &= worst <= tol
        lines.append(f"  m0 without the hint vs the m0 shadow: max |Δ log p| {worst:.2e}, mean {float(err.mean()) if err.size else 0:.1e}")
    else:
        lines.append("  (no m0 shadow in this trace: the no-hint check was skipped)")

    probes, worst = check_probes(s)
    ok &= worst <= 1e-5
    lines.append(f"  features: {probes} probed cells, max |Δ| {worst:.2e}")

    # The learned shadows' kernels at the probes, then the filter end to end.
    if weights is not None and s.kernel_probes:
        models = {}
        for sid in s.shadows:
            if sid in ("m2", "m3", "m4") and sid in weights:
                models[sid] = kernels.make(sid)
                models[sid].load_pack(weights[sid])
        for sid, err in check_kernel_probes(s, models).items():
            ok &= err <= 1e-5
            lines.append(f"  {sid} kernel at the probes (C# vs PyTorch): max |Δ| {err:.2e}")

    # The learned shadows, if their weights are at hand: features, kernel and filter end to end.
    for k, sid in enumerate(s.shadows):
        if sid in ("m0",) or weights is None or sid not in weights:
            continue
        model = kernels.make(sid)
        if sid == "m1":
            model = kernels.HandKernel(avoid=True, gains=weights["m1"])
        else:
            model.load_pack(weights[sid])
        r = replay(s, model, bias=False, csharp_sums=True)
        err = np.abs(r.logp[seen] - s.sa_shadow[seen, k])
        worst = float(err.max()) if err.size else 0.0
        ok &= worst <= tol
        lines.append(f"  {sid} shadow (C#) vs replay: max |Δ log p| {worst:.2e}, mean {float(err.mean()) if err.size else 0:.1e}")
    return ok, lines


def load_weights(path: str | None) -> dict | None:
    if not path:
        return None
    z = np.load(path, allow_pickle=False)
    out = {k: z[k] for k in z.files if k in ("m2", "m3", "m4")}
    if "m1" in z.files:
        out["m1"] = dict(zip(kernels.M1_ORDER, [float(v) for v in z["m1"]]))
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("parity", help="the parity gate on traces")
    p.add_argument("paths", nargs="+")
    p.add_argument("--tol", type=float, default=1e-3)
    p.add_argument("--weights", help="weights.npz from export_cs.py, to check the learned shadows too")
    p.add_argument("--limit", type=int, default=0)
    a = ap.parse_args(argv)

    files = []
    for x in a.paths:
        q = Path(x)
        files += sorted(q.glob("*.kbt.gz")) + sorted(q.glob("*.npz")) if q.is_dir() else [q]
    if a.limit:
        files = files[: a.limit]
    weights = load_weights(a.weights)
    torch.set_num_threads(4)
    failed = 0
    for f in files:
        s = load(f)
        ok, lines = parity(s, a.tol, weights)
        print(f"{'PASS' if ok else 'FAIL'} {f.name}: shift {s.shift} {s.rung} {s.agent} seed {s.seed}")
        for line in lines:
            print(line)
        failed += not ok
    print(f"\nparity gate: {len(files) - failed}/{len(files)} traces pass (tol {a.tol})")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
