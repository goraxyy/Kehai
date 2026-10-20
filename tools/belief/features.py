"""The motion features, computed from a trace exactly as MotionFeatures.cs computes them in game.

Per cell (24) and per edge (9); the MLP's input per cell (48) adds, for each of the eight
directions, the change in distance to work and from Karen, and whether the edge exists.
Arithmetic is float32, and every exp/log is taken in float64 on a float32 argument and cast
back, which is what Unity's Mathf does — so the two copies agree to the last bit or two.
`check_probes` compares against the features C# wrote into the trace.
"""
from __future__ import annotations

import numpy as np

from trace import FAR, MapData, Shift

CELL, EDGE, SLOTS = 24, 9, 8
MLP_INPUTS = CELL + 3 * SLOTS
F = np.float32


def mexp(x) -> np.ndarray:
    """Mathf.Exp: float32 in, exp in double, float32 out."""
    return np.exp(np.asarray(x, np.float32).astype(np.float64)).astype(np.float32)


def mlog(x) -> np.ndarray:
    return np.log(np.asarray(x, np.float32).astype(np.float64)).astype(np.float32)


def step(metres: np.ndarray) -> np.ndarray:
    return np.clip(metres.astype(np.float32) / F(1.5), F(-1), F(1)).astype(np.float32)


def metres(cm: np.ndarray) -> np.ndarray:
    cm = np.asarray(cm)
    out = cm.astype(np.float32) / F(100)
    return np.where(cm == 65535, FAR, out).astype(np.float32)


class FeatureState:
    """The raw fields at one moment: the map's, the 2 s refresh's and the 2 Hz refresh's."""

    def __init__(self, m: MapData):
        self.m = m
        n = m.n
        self.edge_from = m.edge_from
        self.degree = np.diff(m.edge_start).astype(np.int32)
        if len(np.unique(self.edge_from * 8 + m.edge_slot)) != m.edges:
            raise ValueError("two edges of one cell share a direction slot (a trace from before StoreMap.EdgeDirection?)")
        self.desire = np.full(n, np.inf, np.float32)
        self.habit = np.zeros(n, np.float32)
        self.light = np.ones(n, np.float32)
        self.jobs, self.done, self.frac, self.overtime, self.power = 0, 0, F(0), 0, 1
        self.karen = np.full(n, FAR, np.float32)
        self.trail = np.zeros(n, np.float32)
        self.chasing, self.since, self.speed = 0, F(0), F(3.1)
        self._x = self._y = self._u = None

    def touched(self):
        """Call after changing fields by hand (the trace setters do it themselves)."""
        self._x = self._y = self._u = None

    # -- from a trace --
    def set_slow(self, s: Shift, row: int):
        self.desire = s.desire_rows[s.sl_desire_row[row]][: self.m.n]
        self.habit = s.habit_rows[s.sl_habit_row[row]][: self.m.n]
        q = s.light_rows[s.sl_light_row[row]][: self.m.n]
        self.light = (q.astype(np.float32) / F(255)).astype(np.float32)
        self.jobs, self.done = int(s.sl_jobs[row]), int(s.sl_done[row])
        self.frac, self.overtime, self.power = F(s.sl_frac[row]), int(s.sl_overtime[row]), int(s.sl_power[row])
        self.touched()

    def set_fast(self, s: Shift, row: int):
        self.karen = metres(s.dist_rows[s.fa_dist_row[row]][: self.m.n])
        self.trail = np.zeros(self.m.n, np.float32)
        a, k = s.fa_trail_start[row], s.fa_trail_count[row]
        self.trail[s.trail_cells[a:a + k]] = s.trail_values[a:a + k]
        self.chasing, self.since, self.speed = int(s.fa_chasing[row]), F(s.fa_since[row]), F(s.fa_speed[row])
        self.touched()

    # -- cached: rebuilt only after a refresh --
    def features(self):
        """(cell features, edge features, MLP input), rebuilt after each refresh."""
        if self._x is None:
            self._x, self._y = self.cells(), self.edges()
            self._u = self.mlp_input(self._x, self._y)
        return self._x, self._y, self._u

    # -- the features --
    def cells(self) -> np.ndarray:
        m, n = self.m, self.m.n
        x = np.zeros((n, CELL), np.float32)
        x[:, 0] = self.degree.astype(np.float32) / F(8)
        x[:, 1] = m.open3
        for a in range(6):
            x[:, 2 + a] = (m.area == a)
        x[:, 8] = m.choke != 0
        x[:, 9] = mexp(-(m.clock / F(20)))
        x[:, 10] = mexp(-(m.exit / F(20)))
        finite = np.isfinite(self.desire)
        x[:, 11] = np.where(finite, mexp(-(np.where(finite, self.desire, 0) / F(8))), F(0))
        x[:, 12] = self.habit
        x[:, 13] = F(min(self.jobs, 10)) / F(10)
        x[:, 14] = F(1) if self.done else F(0)
        x[:, 15] = np.clip(self.frac, F(0), F(1.5))
        x[:, 16] = F(1) if self.overtime else F(0)
        x[:, 17] = self.light
        x[:, 18] = F(1) if self.power else F(0)
        x[:, 19] = mexp(-(self.karen / F(10)))
        x[:, 20] = self.trail
        x[:, 21] = F(1) if self.chasing else F(0)
        x[:, 22] = min(F(mlog(F(1) + self.since) / F(5)), F(1))
        x[:, 23] = self.speed / F(3.1)
        return x

    def edges(self) -> np.ndarray:
        m = self.m
        c, n = self.edge_from, m.edge_to
        y = np.zeros((m.edges, EDGE), np.float32)
        finite = np.isfinite(self.desire[c]) & np.isfinite(self.desire[n])
        y[:, 0] = np.where(finite, step(np.where(finite, self.desire[c] - self.desire[n], 0)), F(0))
        y[:, 1] = self.habit[n]
        y[:, 2] = step(self.karen[n] - self.karen[c])
        y[:, 3] = self.trail[n]
        y[:, 4] = step(m.clock[c] - m.clock[n])
        y[:, 5] = step(m.exit[c] - m.exit[n])
        y[:, 6] = m.edge_slot >= 4
        y[:, 7] = m.door[n] != 0
        y[:, 8] = m.open3[n]
        return y

    def mlp_input(self, x: np.ndarray, y: np.ndarray) -> np.ndarray:
        m = self.m
        u = np.zeros((m.n, MLP_INPUTS), np.float32)
        u[:, :CELL] = x
        slot = m.edge_slot.astype(np.int64)
        c = self.edge_from
        u[c, CELL + slot] = y[:, 0]
        u[c, CELL + SLOTS + slot] = y[:, 2]
        u[c, CELL + 2 * SLOTS + slot] = 1
        return u


def check_kernel_probes(s: Shift, models: dict) -> dict[str, float]:
    """What each learned shadow's C# kernel made of the probed cells, against PyTorch's kernel
    for the same features. Returns the worst |Δ| of rates and shares per model."""
    import torch
    from trace import OP_FAST, OP_MAP, OP_SLOW
    worst: dict[str, float] = {}
    if not s.kernel_probes:
        return worst
    probes = sorted(s.kernel_probes, key=lambda p: p[0])
    fs, k, built = None, 0, {}
    for i, (code, row) in enumerate(s.ops):
        while k < len(probes) and probes[k][0] <= i:
            _, sid, cell, rates, shares = probes[k]
            k += 1
            if sid not in models:
                continue
            if sid not in built:
                with torch.no_grad():
                    built[sid] = models[sid].build(fs)
            K = built[sid]
            hc = models[sid].headings
            r = K["rate"].detach().numpy().reshape(-1)[cell * hc:(cell + 1) * hc]
            a, b = fs.m.edge_start[cell], fs.m.edge_start[cell + 1]
            sh = K["share"].detach().numpy().reshape(fs.m.edges, -1)[a:b]
            err = max(float(np.max(np.abs(r - rates)) / max(1.0, float(np.max(np.abs(rates))))),
                      float(np.max(np.abs(sh - shares))) if b > a else 0.0)
            worst[sid] = max(worst.get(sid, 0.0), err)
        if code in (OP_MAP, OP_SLOW, OP_FAST):
            built = {}
            if code == OP_MAP:
                fs = FeatureState(s.maps[row])
            elif code == OP_SLOW:
                fs.set_slow(s, row)
            else:
                fs.set_fast(s, row)
    return worst


def check_probes(s: Shift, tol: float = 1e-5) -> tuple[int, float]:
    """Recompute the features at each probe C# wrote and compare. Returns (probes, worst error)."""
    if not s.probes:
        return 0, 0.0
    from trace import OP_FAST, OP_MAP, OP_SLOW
    probes = sorted(s.probes, key=lambda p: p[0])
    fs, worst, k, cache = None, 0.0, 0, {}
    for i, (code, row) in enumerate(s.ops):
        while k < len(probes) and probes[k][0] <= i:
            _, cell, cf, ef = probes[k]
            key = id(fs)
            if key not in cache:
                cache.clear()
                cache[key] = (fs.cells(), fs.edges())
            x, y = cache[key]
            worst = max(worst, float(np.max(np.abs(x[cell] - cf))))
            a, b = fs.m.edge_start[cell], fs.m.edge_start[cell + 1]
            if b > a:
                worst = max(worst, float(np.max(np.abs(y[a:b] - ef))))
            k += 1
        if code == OP_MAP:
            fs = FeatureState(s.maps[row]); cache.clear()
        elif code == OP_SLOW:
            fs.set_slow(s, row); cache.clear()
        elif code == OP_FAST:
            fs.set_fast(s, row); cache.clear()
    return len(probes), worst


# ---- the synthetic state (MotionFeatures.FillSynthetic) -----------------------------------

def synthetic(i, j) -> np.float32:
    return np.float32(np.sin(1.7 * i + 0.31 * j + 0.05 * i * j))


def synthetic_state(m: MapData) -> FeatureState:
    n = m.n
    fs = FeatureState(m)
    idx = np.arange(n)
    s = lambda j: np.array([synthetic(int(c), j) for c in idx], np.float32)
    m.open3 = (s(1) * F(0.5) + F(0.5)).astype(np.float32)
    m.area = (idx % 6).astype(np.uint8)
    m.door = (idx % 7 == 3).astype(np.uint8)
    m.choke = (idx % 5 == 1).astype(np.uint8)
    m.clock = (F(20) + F(15) * s(2)).astype(np.float32)
    m.exit = (F(30) + F(25) * s(3)).astype(np.float32)
    fs.desire = np.where(idx % 9 == 4, np.float32(np.inf), F(10) + F(8) * s(4)).astype(np.float32)
    fs.habit = (F(0.5) + F(0.5) * s(5)).astype(np.float32)
    fs.light = (np.rint((F(0.5) + F(0.5) * s(6)) * F(255)).astype(np.float32) / F(255)).astype(np.float32)
    cm = np.rint(np.maximum(F(0), F(12) + F(10) * s(7)) * F(100)).astype(np.uint16)
    fs.karen = metres(cm)
    fs.trail = np.where(idx % 4 == 0, F(0.5) + F(0.5) * s(8), F(0)).astype(np.float32)
    fs.jobs, fs.done, fs.frac, fs.overtime, fs.power = 4, 0, F(0.6), 0, 1
    fs.chasing, fs.since, fs.speed = 1, F(12.5), F(3.1)
    return fs
