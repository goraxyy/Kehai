"""Her belief filter in numpy (0.4.0, build plan §8.5, C4.2): BeliefGrid.cs, op for op, on today's map
(her cells, the one outside cell and its exit lines), with the predict steps P0–P3 and her senses'
formulas (SenseModel.cs). Nothing is trained until replay.py shows this matches the C# filter on a
recorded trace (the parity gate, to 1e-3).

Doubles throughout; the C# uses floats, which is why the gate allows 1e-3.
"""
from __future__ import annotations

import heapq
import math

import numpy as np

# ---- the map --------------------------------------------------------------------------------------


class Map:
    def __init__(self, d: dict):
        self.n = int(d["cells"])
        self.cell_size = float(d.get("cell_size", 1.5))
        self.x = np.asarray(d["x"], float)
        self.z = np.asarray(d["z"], float)
        self.region = np.asarray(d["region"], int)
        self.regions = int(d.get("regions", self.region.max() + 1 if self.n else 0))
        self.hers = np.asarray(d["hers"], bool)
        self.edge_start = np.asarray(d["edge_start"], int)
        self.edge_to = np.asarray(d["edge_to"], int)
        self.edge_length = np.asarray(d["edge_length"], float)
        self.edge_from = np.repeat(np.arange(self.n), np.diff(self.edge_start))
        self.outside = int(d.get("outside_cell", -1))
        self.exits = [list(e) for e in d.get("exits", [])]
        self.exit_delay = np.asarray(d.get("exit_delay", []), float).reshape(len(self.exits), len(self.exits)) if self.exits else np.zeros((0, 0))
        self.exit_of = -np.ones(self.n, int)
        for k, cells in enumerate(self.exits):
            for c in cells:
                self.exit_of[c] = k
        self.c = {k: float(d[k]) for k in ("floor", "moving", "desire_gain", "habit_gain", "danger_avoidance", "uncertainty",
                                           "outside_return", "outside_prior") if k in d}
        self.line_seconds = int(d.get("line_seconds", 40))
        self.outside_cells = int(d.get("outside_cells", int((~self.hers).sum())))
        self.region_cells = [np.flatnonzero(self.region == r) for r in range(self.regions)]

    def with_regions(self, region, count: int) -> "Map":
        """This map with another region table: a trace's own "regions" line (the doorways' numbers
        can differ between the game's map builds; see BeliefTrace)."""
        import copy
        m = copy.copy(self)
        m.region = np.asarray(region, int)
        m.regions = int(count)
        m.region_cells = [np.flatnonzero(m.region == r) for r in range(m.regions)]
        return m

    def within(self, x: float, z: float, radius: float) -> np.ndarray:
        return (self.x - x) ** 2 + (self.z - z) ** 2 <= radius * radius

    def distances(self, sources, max_distance=math.inf, blocked=None) -> np.ndarray:
        """StoreMap.Distances / DistancesFrom: Dijkstra, stopping once past max_distance, so a cell
        just beyond it may carry the distance it was reached at (as in the C#)."""
        d = np.full(self.n, math.inf)
        heap = []
        for s in ([sources] if isinstance(sources, (int, np.integer)) else sources):
            if 0 <= s < self.n:
                d[s] = 0.0
                heap.append((0.0, int(s)))
        heapq.heapify(heap)
        single = isinstance(sources, (int, np.integer))
        while heap:
            dist, c = heapq.heappop(heap)
            if dist > d[c]:
                continue
            if dist > max_distance:
                if single:
                    break
                continue
            for e in range(self.edge_start[c], self.edge_start[c + 1]):
                nb = self.edge_to[e]
                if blocked is not None and blocked[nb]:
                    continue
                nd = dist + self.edge_length[e]
                if nd < d[nb]:
                    d[nb] = nd
                    heapq.heappush(heap, (nd, int(nb)))
        return d


# ---- the grid -------------------------------------------------------------------------------------


class Grid:
    """BeliefGrid: correct, predict, the outside cell's exit lines, the leak to the prior."""

    def __init__(self, m: Map, motion=None):
        self.m = m
        self.b = np.zeros(m.n)
        self.prior = np.zeros(m.n)
        self.desire = np.full(m.n, math.inf)
        self.habit = np.zeros(m.n)
        self.danger = np.zeros(m.n)
        self.motion = motion or HandMotion()
        k = len(m.exits)
        self.line = np.zeros((k, m.line_seconds))
        self.line_clock = 0.0
        self.last_exit = 0
        self.reset_to_prior()

    # -- the prior
    def rebuild_prior(self):
        m, c = self.m, self.m.c
        w = np.where(np.isinf(self.desire), 1.0, 1.0 + 1.5 * np.exp(-np.where(np.isinf(self.desire), 0.0, self.desire) / 8.0))
        w = w * (1.0 + c["habit_gain"] * self.habit)
        w = np.where(m.hers, w, 0.0)
        s = w.sum()
        if s <= 0:
            return
        inside = 1.0 - c["outside_prior"] if m.outside >= 0 else 1.0
        self.prior = w * inside / s
        if m.outside >= 0:
            self.prior[m.outside] = c["outside_prior"]

    def reset_to_prior(self):
        self.rebuild_prior()
        self.b = self.prior.copy()
        self.clear_lines()
        self.sync_lines()

    def reset_to(self, x, z, sigma, outside):
        self.b = self.prior.copy()
        self.clear_lines()
        self.multiply(x, z, sigma, 1.0, outside)
        self.normalise()

    # -- correct
    def floor(self, confidence):
        return self.m.c["floor"] ** min(1.0, max(0.0, confidence))

    def multiply(self, x, z, sigma, confidence, at_outside):
        m = self.m
        near = m.within(x, z, sigma * 3.0)
        f = self.floor(confidence)
        d2 = (m.x - x) ** 2 + (m.z - z) ** 2
        like = np.where(near, f + np.exp(-d2 / (2 * sigma * sigma)), f)
        if m.outside >= 0:
            like[m.outside] = 1.0 if at_outside else f
        self.b = self.b * like

    def multiply_along_floor(self, source, x, z, sigma, confidence, at_outside):
        m = self.m
        if source < 0:
            self.multiply(x, z, sigma, confidence, at_outside)
            return
        d = m.distances(int(source), sigma * 3.0)
        f = self.floor(confidence)
        like = np.where(np.isinf(d), f, f + np.exp(-np.where(np.isinf(d), 0.0, d) ** 2 / (2 * sigma * sigma)))
        if m.outside >= 0:
            like[m.outside] = 1.0 if at_outside else f
        self.b = self.b * like

    def apply(self, op: dict) -> bool:
        ch = op["ch"]
        x, z, sigma, conf, outside = op["x"], op["z"], op["sigma"], op["conf"], op["outside"]
        if ch == "Hearing":
            self.multiply_along_floor(op["cell"], x, z, sigma, conf, outside)
        elif ch == "Trace":
            self.multiply_along_floor(op.get("centre_cell", op["cell"]), op.get("cx", x), op.get("cz", z), sigma, conf, outside)
        else:
            self.multiply(x, z, sigma, conf, outside)
        if outside:
            self.regroup(x, z)
        return self.normalise()

    def regroup(self, x, z):
        m = self.m
        if m.outside < 0 or not m.exits:
            return
        best, nearest = math.inf, 0
        for k, cells in enumerate(m.exits):
            for c in cells:
                s = (m.x[c] - x) ** 2 + (m.z[c] - z) ** 2
                if s < best:
                    best, nearest = s, k
        mass = self.line.sum()
        self.clear_lines()
        self.line[nearest, 0] = mass
        self.last_exit = nearest

    def sweep(self, cells, p):
        m = self.m
        looked = 0.0
        for c, q in zip(cells, p):
            q = min(1.0, max(0.0, q))
            if m.hers[c]:
                self.b[c] *= 1.0 - q
            else:
                looked += q
        if m.outside >= 0 and looked > 0:
            self.b[m.outside] *= 1.0 - min(1.0, max(0.0, looked / m.outside_cells))
        return self.normalise()

    def bias(self, region, amount):
        cells = self.m.region_cells[region]
        if region < 0 or amount <= 0 or len(cells) == 0:
            return
        amount = min(1.0, max(0.0, amount))
        self.b *= 1.0 - amount
        share = amount / len(cells)
        for c in cells:
            if self.m.hers[c]:
                self.b[c] += share
            elif self.m.outside >= 0:
                self.b[self.m.outside] += share
        self.normalise()

    # -- predict
    def predict(self, dt, speed):
        m = self.m
        scratch = np.zeros(m.n)
        inflow = np.zeros(len(m.exits))
        self.motion.spread(self, self.b, scratch, inflow, dt, speed)
        if m.outside >= 0:
            scratch[m.outside] = self.step_outside(dt, scratch, inflow)
        u = min(1.0, max(0.0, m.c["uncertainty"] * dt))
        self.b = (1.0 - u) * scratch + u * self.prior
        self.sync_lines()

    def step_outside(self, dt, scratch, inflow):
        m = self.m
        k_exits = len(m.exits)
        back = min(1.0, max(0.0, m.c["outside_return"] * dt))
        returning = np.zeros(k_exits)
        for k in range(k_exits):
            line = self.line[k]
            for a in range(m.line_seconds):
                mass = line[a]
                if mass <= 0:
                    continue
                r = mass * back
                line[a] = mass - r
                age = a + self.line_clock
                open_ = [j for j in range(k_exits) if j == k or age >= m.exit_delay[k, j]]
                for j in open_:
                    returning[j] += r / len(open_)
            if inflow[k] > 0:
                line[0] += inflow[k]
                self.last_exit = k
        for j in range(k_exits):
            if returning[j] > 0:
                border = m.exits[j]
                scratch[border] += returning[j] / len(border)
        self.line_clock += dt
        while self.line_clock >= 1.0:
            self.line_clock -= 1.0
            self.line[:, -1] += self.line[:, -2]
            self.line[:, 1:-1] = self.line[:, :-2].copy()
            self.line[:, 0] = 0.0
        return self.line.sum()

    # -- bookkeeping
    def normalise(self) -> bool:
        s = self.b.sum()
        if s < 1e-12 or math.isnan(s):
            self.b = self.prior.copy()
            self.clear_lines()
            self.sync_lines()
            return True
        self.b = self.b / s
        self.sync_lines()
        return False

    def sync_lines(self):
        if self.m.outside < 0 or self.line.size == 0:
            return
        want, have = self.b[self.m.outside], self.line.sum()
        if have > 1e-12:
            self.line *= want / have
        elif want > 0:
            self.line[self.last_exit, 0] = want

    def clear_lines(self):
        self.line[:] = 0.0
        self.line_clock = 0.0


# ---- the predict steps ----------------------------------------------------------------------------


class HandMotion:
    """P0 (today's gains: the map's own) and P1 (fitted gains, with the Danger layer)."""

    def __init__(self, gains: dict | None = None):
        self.gains = gains            # None: today's, from the map file

    def spread(self, g: Grid, b, scratch, inflow, dt, speed):
        m = g.m
        c = m.c
        gains = self.gains or {}
        moving = gains.get("moving", c["moving"])
        desire_gain = gains.get("desire", c["desire_gain"])
        habit_gain = gains.get("habit", c["habit_gain"])
        danger_avoid = gains.get("danger", c["danger_avoidance"])
        if self.gains is not None:
            speed *= gains.get("speed", 1.0)
        mobility = min(0.8, max(0.0, speed * dt / m.cell_size)) * moving
        src, dst = m.edge_from, m.edge_to
        here, there = g.desire[src], g.desire[dst]
        finite = ~np.isinf(here) & ~np.isinf(there)
        w = np.ones(len(dst))
        delta = np.where(finite, here, 0.0) - np.where(finite, there, 0.0)
        w = np.where(finite, w * (1.0 + desire_gain * 0.5 * np.clip(delta / m.cell_size, -1.0, 1.0)), w)
        w = w * (1.0 + habit_gain * g.habit[dst])
        w = w * (1.0 - danger_avoid * np.clip(g.danger[dst], 0.0, 1.0))
        w = np.maximum(0.02, w)
        degree = np.diff(m.edge_start)
        active = m.hers & (b > 0)
        stay = active & (degree == 0)
        scratch[stay] += b[stay]
        moving_cells = active & (degree > 0)
        leaving = np.where(moving_cells, b * mobility, 0.0)
        scratch[moving_cells] += b[moving_cells] - leaving[moving_cells]
        total = np.bincount(src, weights=w, minlength=m.n)
        share = leaving[src] * w / np.where(total[src] > 0, total[src], 1.0)
        share = np.where(moving_cells[src], share, 0.0)
        inside = m.hers[dst]
        np.add.at(scratch, dst[inside], share[inside])
        out = ~inside & (share > 0)
        if out.any():
            np.add.at(inflow, m.exit_of[src[out]], share[out])


class DestinationMotion:
    """P2 and P3: per-edge rates from the destinations (features.py), refreshed every 2 s."""

    def __init__(self, rates=None, exit_rates=None):
        self.rate = rates
        self.exit_rate = exit_rates

    def spread(self, g: Grid, b, scratch, inflow, dt, speed):
        if self.rate is None:
            HandMotion().spread(g, b, scratch, inflow, dt, speed)
            return
        m = g.m
        src, dst = m.edge_from, m.edge_to
        total = np.bincount(src, weights=self.rate, minlength=m.n) + self.exit_rate
        leaving = np.minimum(0.8, total * dt)
        active = m.hers & (b > 0)
        moves = active & (total > 0) & (leaving > 0)
        scratch[active & ~moves] += b[active & ~moves]
        scale = np.where(moves, b * leaving / np.where(total > 0, total, 1.0), 0.0)
        scratch[moves] += b[moves] * (1.0 - leaving[moves])
        flow = self.rate * scale[src]
        inside = m.hers[dst]
        np.add.at(scratch, dst[inside & (flow > 0)], flow[inside & (flow > 0)])
        out = ~inside & (flow > 0)
        if out.any():
            np.add.at(inflow, m.exit_of[src[out]], flow[out])
        ex = moves & (self.exit_rate > 0)
        if ex.any():
            has_exit = m.exit_of[ex] >= 0
            cells = np.flatnonzero(ex)
            np.add.at(inflow, m.exit_of[cells[has_exit]], (self.exit_rate * scale)[cells[has_exit]])
            scratch[cells[~has_exit]] += (self.exit_rate * scale)[cells[~has_exit]]


# ---- her senses (SenseModel.cs) --------------------------------------------------------------------

S0 = dict(sight_sigma_far=3.0, sight_sigma_near=0.8, hear_confidence=0.9, hear_sigma0=1.0, hear_sigma_per_metre=0.12,
          wall_gain=0.0, dark_gain=0.0, testimony_sigma0=2.5, testimony_sigma_per_second=0.25, sweep_scale=0.85)
TRACES = {0: ("WetFootprint", 0.55, 3.0, 0.0, True), 1: ("UndoneSabotage", 0.6, 6.0, 0.1, False), 2: ("DroppedItem", 0.45, 5.0, 0.1, False),
          3: ("DoorOpened", 0.5, 4.0, 0.0, True), 4: ("MopAway", 0.4, 6.0, 0.0, False), 5: ("BaggedBin", 0.35, 8.0, 0.0, True)}


def remake(op: dict, s: dict, sid: str = "s1") -> tuple:
    """An observation's (confidence, sigma) under other sense numbers, from its raw inputs."""
    raw = op.get("raw") or {}
    ch, conf, sigma = op["ch"], op["conf"], op["sigma"]
    if ch == "Sight":
        sigma = s["sight_sigma_far"] + (s["sight_sigma_near"] - s["sight_sigma_far"]) * min(1.0, max(0.0, raw.get("a", 0.0)))
    elif ch == "Hearing" and raw.get("carry", 0) > 0:
        d, carry = raw["d"], raw["carry"]
        conf = math.sqrt(max(0.0, 1 - d / carry)) * s["hear_confidence"]
        sigma = s["hear_sigma0"] + d * s["hear_sigma_per_metre"]
        if sid == "s2":
            straight = raw.get("straight", 0.0)
            detour = max(0.0, d / straight - 1.0) if straight > 0.5 else 0.0
            sigma *= (1 + s["wall_gain"] * detour) * ((1 + s["dark_gain"]) if raw.get("dark") else 1.0)
    elif ch == "Trace":
        name, trust, s0, per, fades = TRACES.get(raw.get("kind", -1), ("Other", 0.3, 7.0, 0.1, False))
        trust = s.get(f"trace_{name}_trust", trust)
        s0 = s.get(f"trace_{name}_sigma0", s0)
        per = s.get(f"trace_{name}_sigma_per_second", per)
        conf = trust * (raw.get("fresh", 1.0) if fades else 1.0)
        sigma = s0 + raw.get("age", 0.0) * per
    elif ch == "Testimony":
        sigma = s["testimony_sigma0"] + raw.get("age", 0.0) * s["testimony_sigma_per_second"]
    return min(1.0, max(0.0, conf)), max(0.5, sigma)
