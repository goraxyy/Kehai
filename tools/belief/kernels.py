"""The motion models (the predict step's kernel T), in PyTorch, matching the C# ones.

  m0  HandMotion with the shipped gains (BeliefGrid's old Predict, unchanged)
  m1  HandMotion with its gains tuned, plus keeping away from Karen (the Danger layer)
  m2  log-linear in the features                    (LearnedMotion.Kind.LogLinear)
  m3  m2 plus an MLP per cell                       (LearnedMotion.Kind.Mlp)
  m4  m3 with a heading in the state, cell × 9      (LearnedMotion.Kind.Heading)

Each model has `build(fs)` (a kernel from the features at one moment) and
`apply(kernel, b, dt, speed)` (one tick: b' = T b, mass kept, moved only along edges).
`leak` and `heading_prior` complete the filter. `pack()`/`load_pack()` give the flat
float32 weights in the order MotionWeights.g.cs holds them (export_cs.py).
"""
from __future__ import annotations

import math

import numpy as np
import torch
from torch import nn

from features import CELL, EDGE, MLP_INPUTS, SLOTS, FeatureState

HIDDEN = 32
HEADINGS = 9
CELL_SIZE = 1.5

torch.set_default_dtype(torch.float32)


class MapTensors:
    """A map's edges as tensors, built once per map."""

    def __init__(self, m):
        self.m = m
        self.n = m.n
        self.edge_from = torch.as_tensor(m.edge_from, dtype=torch.long)
        self.edge_to = torch.as_tensor(m.edge_to.astype(np.int64))
        self.slot = torch.as_tensor(m.edge_slot.astype(np.int64))
        self.degree = torch.as_tensor(np.diff(m.edge_start).astype(np.int64))
        self.has_edges = self.degree > 0


_maps: dict[int, MapTensors] = {}


def map_tensors(m) -> MapTensors:
    key = id(m)
    t = _maps.get(key)
    if t is None or t.m is not m:
        if len(_maps) > 64:
            _maps.clear()
        t = _maps[key] = MapTensors(m)
    return t


def segment_softmax(score: torch.Tensor, group: torch.Tensor, groups: int) -> torch.Tensor:
    """Softmax of `score` [E, ...] within each group (the edges leaving one cell)."""
    shape = (groups,) + tuple(score.shape[1:])
    idx = group.view(-1, *([1] * (score.dim() - 1))).expand_as(score)
    mx = torch.full(shape, -math.inf).scatter_reduce(0, idx, score.detach(), reduce="amax", include_self=True)
    e = torch.exp(score - mx.gather(0, idx))
    total = torch.zeros(shape).scatter_add(0, idx, e)
    return e / total.gather(0, idx)


# ---- the hand step (m0, m1) ---------------------------------------------------------------

SHIPPED = dict(moving=0.65, desire=0.9, habit=1.2, avoid=0.6, leak=0.015, speed_scale=1.0, radius=6.0, trail=0.5)
M1_ORDER = ["moving", "desire", "habit", "avoid", "leak", "speed_scale", "radius", "trail"]


class HandKernel(nn.Module):
    """HandMotion. m0 = the shipped gains and no Danger; m1 = learnable gains and Danger from
    her walking distance and trail. Parameters live in unconstrained form."""

    headings = 1

    def __init__(self, avoid: bool, gains: dict | None = None):
        super().__init__()
        self.id = "m1" if avoid else "m0"
        self.avoid = avoid
        g = dict(SHIPPED, **(gains or {}))
        if not avoid:
            for k, v in g.items():
                self.register_buffer("g_" + k, torch.tensor(float(v)))
        else:
            self.p_moving = nn.Parameter(torch.tensor(_logit(g["moving"])))
            self.p_desire = nn.Parameter(torch.tensor(_inv_softplus(g["desire"])))
            self.p_habit = nn.Parameter(torch.tensor(_inv_softplus(g["habit"])))
            self.p_avoid = nn.Parameter(torch.tensor(_logit(g["avoid"])))
            self.p_leak = nn.Parameter(torch.tensor(math.log(g["leak"])))
            self.p_speed_scale = nn.Parameter(torch.tensor(math.log(g["speed_scale"])))
            self.p_radius = nn.Parameter(torch.tensor(math.log(g["radius"])))
            self.p_trail = nn.Parameter(torch.tensor(_logit(g["trail"])))

    def gains(self) -> dict[str, torch.Tensor]:
        if not self.avoid:
            return {k: getattr(self, "g_" + k) for k in SHIPPED}
        return dict(moving=torch.sigmoid(self.p_moving), desire=nn.functional.softplus(self.p_desire),
                    habit=nn.functional.softplus(self.p_habit), avoid=torch.sigmoid(self.p_avoid),
                    leak=torch.exp(self.p_leak), speed_scale=torch.exp(self.p_speed_scale),
                    radius=torch.exp(self.p_radius), trail=torch.sigmoid(self.p_trail))

    def gains_float(self) -> dict[str, float]:
        return {k: float(np.float32(v.detach().item())) for k, v in self.gains().items()}

    @property
    def leak(self) -> torch.Tensor:
        return self.gains()["leak"]

    heading_prior = None

    def build(self, fs: FeatureState):
        mt = map_tensors(fs.m)
        g = self.gains()
        c, n = mt.edge_from, mt.edge_to
        desire = torch.as_tensor(fs.desire)
        habit = torch.as_tensor(fs.habit)
        finite = torch.isfinite(desire[c]) & torch.isfinite(desire[n])
        delta = torch.where(finite, desire[c] - desire[n], torch.zeros(()))
        w = torch.ones(len(n))
        w = w * torch.where(finite, 1 + g["desire"] * 0.5 * torch.clamp(delta / CELL_SIZE, -1, 1), torch.ones(()))
        w = w * (1 + g["habit"] * habit[n])
        if self.avoid:
            karen = torch.as_tensor(fs.karen)
            trail = torch.as_tensor(fs.trail)
            danger = torch.maximum(torch.exp(-karen / torch.clamp(g["radius"], min=0.1)), g["trail"] * trail)
            w = w * (1 - g["avoid"] * torch.clamp(danger[n], 0, 1))
        w = torch.clamp(w, min=0.02)
        total = torch.zeros(mt.n).index_add(0, c, w)
        share = w / total[c]
        return dict(mt=mt, share=share, g=g)

    def apply(self, k, b: torch.Tensor, dt: float, speed: float) -> torch.Tensor:
        mt, g = k["mt"], k["g"]
        if self.avoid:
            mobility = torch.clamp(speed * g["speed_scale"] * dt / CELL_SIZE, 0, 0.8) * g["moving"]
        else:
            mobility = float(np.clip(np.float32(np.float32(speed) * np.float32(dt)) / np.float32(CELL_SIZE), 0, 0.8)
                             * np.float32(g["moving"].item()))
        leaving = b * mobility
        stay = torch.where(mt.has_edges, b - leaving, b)
        moved = (leaving[mt.edge_from] * k["share"])
        return stay.index_add(0, mt.edge_to, moved)


def _logit(p: float) -> float:
    return math.log(p / (1 - p))


def _inv_softplus(y: float) -> float:
    return math.log(math.expm1(y))


# ---- the learned models (m2, m3, m4) -------------------------------------------------------

KINDS = {"m2": "loglinear", "m3": "mlp", "m4": "heading"}


class LearnedKernel(nn.Module):
    def __init__(self, kind: str):
        super().__init__()
        assert kind in KINDS, kind
        self.id = kind
        self.headings = HEADINGS if kind == "m4" else 1
        # The head every learned model has. Starts as the shipped step's average speed.
        self.rate_w = nn.Parameter(torch.zeros(CELL))
        self.rate_b = nn.Parameter(torch.tensor(math.log(0.65 * 3.1 / CELL_SIZE)))
        self.edge_w = nn.Parameter(torch.zeros(EDGE))
        self.dir_b = nn.Parameter(torch.zeros(SLOTS))
        self.log_leak = nn.Parameter(torch.tensor(math.log(0.015)))
        self.outputs = 0
        if kind in ("m3", "m4"):
            self.outputs = 10 if kind == "m4" else 9
            self.w1 = nn.Parameter(torch.randn(MLP_INPUTS, HIDDEN) * math.sqrt(2 / MLP_INPUTS))
            self.b1 = nn.Parameter(torch.zeros(HIDDEN))
            self.w2 = nn.Parameter(torch.randn(HIDDEN, HIDDEN) * math.sqrt(2 / HIDDEN))
            self.b2 = nn.Parameter(torch.zeros(HIDDEN))
            # The last layer starts at zero: an untrained MLP adds nothing to m2's head.
            self.w3 = nn.Parameter(torch.zeros(HIDDEN, self.outputs))
            self.b3 = nn.Parameter(torch.zeros(self.outputs))
        if kind == "m4":
            # Moving on is likelier once you're moving; stopping is a slow rate; a fresh belief is
            # mostly "still or walking somewhere".
            self.rho = nn.Parameter(torch.tensor([-1.0] + [0.5] * 8))
            self.mom = nn.Parameter(torch.zeros(HEADINGS, SLOTS))
            with torch.no_grad():
                for k in range(SLOTS):
                    self.mom[1 + k, k] = 1.5
            self.sigma = nn.Parameter(torch.zeros(HEADINGS))
            self.stop_w = nn.Parameter(torch.zeros(CELL))
            self.stop_b = nn.Parameter(torch.tensor(math.log(0.3)))
            self.hp_logits = nn.Parameter(torch.zeros(HEADINGS))

    # -- the order MotionWeights.g.cs holds them in --
    def _ordered(self) -> list[torch.Tensor]:
        p = [self.rate_w, self.rate_b.view(1), self.edge_w, self.dir_b, self.log_leak.view(1)]
        if self.outputs:
            p += [self.w1, self.b1, self.w2, self.b2, self.w3, self.b3]
        if self.id == "m4":
            p += [self.rho, self.mom, self.sigma, self.stop_w, self.stop_b.view(1), self.hp_logits]
        return p

    def pack(self) -> np.ndarray:
        return np.concatenate([t.detach().reshape(-1).numpy().astype(np.float32) for t in self._ordered()])

    def load_pack(self, flat: np.ndarray):
        flat = np.asarray(flat, np.float32)
        at = 0
        with torch.no_grad():
            for t in self._ordered():
                k = t.numel()
                t.copy_(torch.as_tensor(flat[at:at + k]).view_as(t))
                at += k
        assert at == len(flat), (at, len(flat))

    @property
    def leak(self) -> torch.Tensor:
        return torch.exp(self.log_leak)

    @property
    def heading_prior(self):
        return torch.softmax(self.hp_logits, 0) if self.id == "m4" else None

    def build(self, fs: FeatureState, xyz=None):
        mt = map_tensors(fs.m)
        x_np, y_np = (fs.cells(), fs.edges()) if xyz is None else xyz[:2]
        x = torch.as_tensor(x_np)
        y = torch.as_tensor(y_np)
        c = mt.edge_from
        log_rate = x @ self.rate_w + self.rate_b
        score = y @ self.edge_w + self.dir_b[mt.slot]
        o = None
        if self.outputs:
            u = torch.as_tensor(fs.mlp_input(x_np, y_np) if xyz is None or xyz[2] is None else xyz[2])
            h = torch.relu(u @ self.w1 + self.b1)
            h = torch.relu(h @ self.w2 + self.b2)
            o = h @ self.w3 + self.b3
            log_rate = log_rate + o[:, 0]
            score = score + o[c, 1 + mt.slot]
        if self.id != "m4":
            rate = torch.where(mt.has_edges, torch.exp(log_rate), torch.zeros(()))
            share = segment_softmax(score, c, mt.n)
            return dict(mt=mt, rate=rate, share=share)

        log_stop = x @ self.stop_w + self.stop_b + o[:, 9]
        move = torch.where(mt.has_edges[:, None], torch.exp(log_rate[:, None] + self.rho[None, :]), torch.zeros(()))
        stop = torch.exp(log_stop[:, None] + self.sigma[None, :])
        stop = torch.cat([torch.zeros(mt.n, 1), stop[:, 1:]], 1)
        total = move + stop
        safe = torch.where(total > 0, total, torch.ones(()))
        stop_share = torch.where(total > 0, stop / safe, torch.zeros(()))
        moving = torch.where(total > 0, move / safe, torch.zeros(()))
        score_h = score[:, None] + self.mom.t()[mt.slot]          # [E, 9]: mom[h, slot]
        share = segment_softmax(score_h, c, mt.n) * moving[c]
        return dict(mt=mt, rate=total, share=share, stop=stop_share)

    def apply(self, k, b: torch.Tensor, dt: float, speed: float) -> torch.Tensor:
        mt = k["mt"]
        stay = torch.exp(-k["rate"] * dt)
        leave = b * (1 - stay)
        to = b * stay
        if self.id != "m4":
            return to.index_add(0, mt.edge_to, leave[mt.edge_from] * k["share"])
        n = mt.n
        to = to.clone()
        to[:, 0] = to[:, 0] + (leave * k["stop"]).sum(1)
        moved = (leave[mt.edge_from] * k["share"]).sum(1)                 # [E]
        flat = to.reshape(-1).index_add(0, mt.edge_to * HEADINGS + 1 + mt.slot, moved)
        return flat.view(n, HEADINGS)


def make(model_id: str, gains: dict | None = None) -> nn.Module:
    if model_id in ("m0", "hand"):
        return HandKernel(avoid=False)
    if model_id == "m1":
        return HandKernel(avoid=True, gains=gains)
    return LearnedKernel(model_id)


def pack_size(model_id: str) -> int:
    return len(LearnedKernel(model_id).pack())
