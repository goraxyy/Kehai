"""A store with no scene behind it, built the way StoreMap.Synthetic builds it in C#.

Used by the Python tests (a small, known map) and by the C# parity gate: export_cs.py writes,
into MotionWeights.g.cs, what each PyTorch kernel makes of a fixed belief on this map with the
fixed features of MotionFeatures.FillSynthetic, and the EditMode test checks that the C#
kernels make the same of it (to 1e-5).

    python make_fixture.py            prints the map and one step of each model
"""
from __future__ import annotations

import numpy as np
import torch

import kernels
from features import synthetic, synthetic_state
from trace import MapData

COLUMNS, ROWS = 7, 6
# Floor ('.') and shelving ('#'), row 0 first (the C# array is row-major the same way).
LAYOUT = [
    ".......",
    ".#.#...",
    ".#.#.#.",
    "...#.#.",
    ".#.....",
    ".#..##.",
]
OPEN = [ch == "." for row in LAYOUT for ch in row]
DT = np.float32(0.13333334)
SPEED = np.float32(3.1)
CELL = np.float32(1.5)
DC = [1, -1, 0, 0, 1, 1, -1, -1]
DR = [0, 0, 1, -1, 1, -1, 1, -1]


def synthetic_map(columns: int = COLUMNS, rows: int = ROWS, open_: list[bool] | None = None) -> MapData:
    open_ = OPEN if open_ is None else open_
    grid = np.full(columns * rows, -1, np.int64)
    xs, zs, col, row = [], [], [], []
    for r in range(rows):
        for c in range(columns):
            g = r * columns + c
            if open_[g]:
                grid[g] = len(xs)
                xs.append(np.float32((c + np.float32(0.5)) * CELL))
                zs.append(np.float32((r + np.float32(0.5)) * CELL))
                col.append(c)
                row.append(r)
    n = len(xs)
    x, z = np.array(xs, np.float32), np.array(zs, np.float32)
    start, to, length, slots = [0] * (n + 1), [], [], []
    for cell in range(n):
        start[cell] = len(to)
        straight = [False] * 4
        for k in range(8):
            if k >= 4:
                horizontal = straight[0 if DC[k] > 0 else 1]
                vertical = straight[2 if DR[k] > 0 else 3]
                if not horizontal or not vertical:
                    continue
            nc, nr = col[cell] + DC[k], row[cell] + DR[k]
            linked = False
            if 0 <= nc < columns and 0 <= nr < rows:
                other = grid[nr * columns + nc]
                if other >= 0:
                    to.append(int(other))
                    slots.append(k)
                    dx, dz = np.float32(x[other] - x[cell]), np.float32(z[other] - z[cell])
                    length.append(np.float32(np.sqrt(np.float32(dx * dx + np.float32(0) + dz * dz))))
                    linked = True
            if k < 4:
                straight[k] = linked
    start[n] = len(to)
    edge_to = np.array(to, np.int32)
    edge_start = np.array(start, np.int32)
    frm = np.repeat(np.arange(n), np.diff(edge_start))
    slot = np.array(slots, np.uint8)   # the direction LinkCells linked it in (StoreMap.EdgeDirection)
    region = np.array(row, np.int32)   # one region per row, in order of the rows' first cells
    _, region = np.unique(region, return_inverse=True)
    return MapData(version=1, x=x, z=z, edge_start=edge_start, edge_to=edge_to, edge_len=np.array(length, np.float32),
                   cell_region=region.astype(np.int32), region_outdoors=np.zeros(region.max() + 1, np.uint8),
                   open3=np.zeros(n, np.float32), area=np.zeros(n, np.uint8), door=np.zeros(n, np.uint8),
                   choke=np.zeros(n, np.uint8), clock=np.zeros(n, np.float32), exit=np.zeros(n, np.float32),
                   edge_slot=slot)


def _slot(dx: float, dz: float) -> int:
    sx = 1 if dx > 0.1 else -1 if dx < -0.1 else 0
    sz = 1 if dz > 0.1 else -1 if dz < -0.1 else 0
    if sz == 0:
        return 0 if sx > 0 else 1
    if sx == 0:
        return 2 if sz > 0 else 3
    if sx > 0:
        return 4 if sz > 0 else 5
    return 6 if sz > 0 else 7


def fixture_from(states: int) -> np.ndarray:
    """A fixed, lumpy belief over the states (cells × headings), summing to one."""
    v = np.array([np.float32(1.5) + synthetic(s, 11) for s in range(states)], np.float32)
    return (v / v.sum(dtype=np.float32)).astype(np.float32)


def fixture_outputs(models: dict) -> dict[str, np.ndarray]:
    """One kernel step of each model on the fixture: what the C# kernels must reproduce."""
    m = synthetic_map()
    fs = synthetic_state(m)
    out = {}
    with torch.no_grad():
        for mid, model in models.items():
            k = model.build(fs)
            hc = model.headings
            b = torch.as_tensor(fixture_from(m.n * hc))
            if hc > 1:
                b = b.view(m.n, hc)
            to = model.apply(k, b, float(DT), float(SPEED))
            out[mid] = to.reshape(-1).numpy().astype(np.float32)
    return out


if __name__ == "__main__":
    m = synthetic_map()
    print(f"{m.n} cells, {m.edges} edges")
    for r in reversed(range(ROWS)):
        print("  " + LAYOUT[r])
    models = {"m0": kernels.make("m0"), "m1": kernels.make("m1"), "m2": kernels.make("m2")}
    for k, v in fixture_outputs(models).items():
        print(k, v[:6], "Σ", v.sum())


# ---- a synthetic trace (for the tests) -----------------------------------------------------

def _write_trace(path, m: MapData, fs, events: list, meta: dict) -> None:
    """Writes a .kbt.gz in BeliefTrace.cs's layout from a list of (kind, payload) events."""
    import gzip
    import struct

    def text(s: str) -> bytes:
        b = s.encode()
        return struct.pack("<H", len(b)) + b

    out = [b"KBTR", struct.pack("<i", 1)]
    out.append(struct.pack("<Bfii", 2, 0.0, meta["shift"], meta["seed"]) + text(meta["rung"]) + text(meta["agent"])
               + text(meta["motion"]) + struct.pack("<i", len(meta["shadows"])) + b"".join(text(s) for s in meta["shadows"])
               + struct.pack("<f", 150.0))
    n, e = m.n, m.edges
    out.append(struct.pack("<Biiii", 1, 1, n, e, int(m.cell_region.max()) + 1) + m.x.tobytes() + m.z.tobytes()
               + m.edge_start.astype("<i4").tobytes() + m.edge_to.astype("<i4").tobytes() + m.edge_len.tobytes()
               + m.cell_region.astype("<i4").tobytes() + m.region_outdoors.tobytes() + m.open3.tobytes()
               + m.area.tobytes() + m.door.tobytes() + m.choke.tobytes() + m.clock.tobytes() + m.exit.tobytes()
               + m.edge_slot.tobytes())
    for kind, p in events:
        if kind == "slow":
            rec = struct.pack("<BfiiBfBB", 7, p["t"], 1, fs.jobs, fs.done, fs.frac, fs.overtime, fs.power)
            light = np.rint(fs.light * 255).astype(np.uint8)
            rec += b"\x01" + light.tobytes() + b"\x01" + fs.desire.tobytes() + b"\x01" + fs.habit.tobytes()
            out.append(rec)
        elif kind == "fast":
            cm = np.where(fs.karen >= 999, 65535, np.rint(fs.karen * 100)).astype("<u2")
            cells = np.nonzero(fs.trail)[0].astype("<u2")
            out.append(struct.pack("<BfiiBff", 6, p["t"], 1, 0, fs.chasing, fs.since, fs.speed) + b"\x01" + cm.tobytes()
                       + struct.pack("<i", len(cells)) + cells.tobytes() + fs.trail[cells].astype("<f4").tobytes())
        elif kind == "reset":
            out.append(struct.pack("<BfBi", 8, p["t"], 3, n) + p["b"].astype("<f4").tobytes() + p["prior"].astype("<f4").tobytes())
        elif kind == "evidence":
            cells = p["cells"].astype("<u2")
            out.append(struct.pack("<BBBfi", 3, 0, 0, p["floor"], len(cells)) + cells.tobytes() + p["values"].astype("<f4").tobytes())
        elif kind == "bias":
            out.append(struct.pack("<Bif", 4, p["region"], p["amount"]))
        elif kind == "predict":
            out.append(struct.pack("<Bfff", 5, p["t"], p["dt"], p["speed"]))
        elif kind == "sample":
            out.append(struct.pack("<BfiBfffffB", 9, p["t"], p["truth"], p["view"], p["since"], 0.0, 0.0, p["own"],
                                   p["last_seen"], len(p["shadow"])) + np.asarray(p["shadow"], "<f4").tobytes()
                       + struct.pack("<i", 0))
    out.append(struct.pack("<Bfi", 11, 0.0, 0))
    with gzip.open(path, "wb") as f:
        f.write(b"".join(out))


class ReferenceFilter:
    """The C# grid's arithmetic, written out loop by loop in float32 (an independent copy of
    BeliefGrid + HandMotion with the shipped gains), to test the replay against."""

    def __init__(self, m: MapData, prior: np.ndarray, desire: np.ndarray, habit: np.ndarray):
        self.m, self.prior, self.desire, self.habit = m, prior.astype(np.float32), desire, habit
        self.b = prior.astype(np.float32).copy()

    def normalise(self):
        s = np.float32(0)
        for v in self.b:
            s = np.float32(s + v)
        if s < np.float32(1e-12) or not np.isfinite(s):
            self.b = self.prior.copy()
            return
        inv = np.float32(1) / s
        self.b = (self.b * inv).astype(np.float32)

    def evidence(self, floor, cells, values):
        mult = np.full(self.m.n, floor, np.float32)
        mult[cells] = values
        self.b = (self.b * mult).astype(np.float32)
        self.normalise()

    def bias(self, region, amount):
        self.b = (self.b * (np.float32(1) - np.float32(amount))).astype(np.float32)
        cells = self.m.region_cells(region)
        self.b[cells] += np.float32(amount) / np.float32(len(cells))
        self.normalise()

    def predict(self, dt, speed):
        m, F = self.m, np.float32
        mobility = F(np.clip(F(F(speed) * F(dt)) / F(1.5), 0, 0.8)) * F(0.65)
        out = np.zeros(m.n, np.float32)
        for c in range(m.n):
            mass = self.b[c]
            a, z = m.edge_start[c], m.edge_start[c + 1]
            if a == z:
                out[c] += mass
                continue
            leaving = F(mass * mobility)
            out[c] += F(mass - leaving)
            w = []
            for e in range(a, z):
                nb = m.edge_to[e]
                wt = F(1)
                if np.isfinite(self.desire[c]) and np.isfinite(self.desire[nb]):
                    wt = F(wt * F(F(1) + F(F(0.9) * F(0.5)) * F(np.clip(F(self.desire[c] - self.desire[nb]) / F(1.5), -1, 1))))
                wt = F(wt * F(F(1) + F(1.2) * self.habit[nb]))
                w.append(max(F(0.02), wt))
            total = F(sum(w, F(0)))
            for e, wt in zip(range(a, z), w):
                out[m.edge_to[e]] += F(F(leaving * wt) / total)
        u = F(np.clip(F(0.015) * F(dt), 0, 1))
        self.b = (F(1) - u) * out + u * self.prior
        self.b = self.b.astype(np.float32)


def synthetic_trace(path, ticks: int = 120, seed: int = 0) -> dict:
    """A plausible shift on the synthetic map: sightings and sweeps, the Director's hint now
    and then, a walk for the truth. The logged log p come from ReferenceFilter, with the hint
    (her own grid) and without (the m0 shadow). Returns what was logged."""
    rng = np.random.default_rng(seed)
    m = synthetic_map()
    fs = synthetic_state(m)
    prior = np.ones(m.n, np.float32) / np.float32(m.n)
    own = ReferenceFilter(m, prior, fs.desire, fs.habit)
    pure = ReferenceFilter(m, prior, fs.desire, fs.habit)
    events = [("slow", dict(t=0.0)), ("fast", dict(t=0.0)), ("reset", dict(t=0.0, b=own.b.copy(), prior=prior))]
    truth = 5
    logged = dict(own=[], pure=[])
    t = 0.0
    for i in range(ticks):
        if i % 3 == 0:
            truth = int(rng.choice(np.append(m.edge_to[m.edge_start[truth]:m.edge_start[truth + 1]], truth)))
        if i % 2 == 0:
            def log_of(b):
                return float(np.log(max(float(b[truth]), 1e-6)))
            s = dict(t=t, truth=truth, view=int(i % 10 < 3), since=float(i % 37), own=log_of(own.b),
                     last_seen=-5.0, shadow=[log_of(pure.b)])
            logged["own"].append(s["own"]); logged["pure"].append(s["shadow"][0])
            events.append(("sample", s))
        if i % 5 == 0:
            conf = float(rng.uniform(0.3, 0.95))
            floor = np.float32(np.float32(1e-4) ** np.float32(conf))
            centre = truth if rng.random() < 0.7 else int(rng.integers(m.n))
            d2 = (m.x - m.x[centre]) ** 2 + (m.z - m.z[centre]) ** 2
            cells = np.nonzero(d2 <= 9.0 * 4)[0]
            values = (floor + np.exp(-(d2[cells] / np.float32(2 * 2.0 ** 2)).astype(np.float64)).astype(np.float32)).astype(np.float32)
            events.append(("evidence", dict(floor=float(floor), cells=cells, values=values)))
            own.evidence(floor, cells, values); pure.evidence(floor, cells, values)
        if i % 4 == 2:
            cells = rng.choice(m.n, size=6, replace=False)
            values = (np.float32(1) - rng.uniform(0.1, 0.9, 6).astype(np.float32)).astype(np.float32)
            events.append(("evidence", dict(floor=1.0, cells=cells, values=values)))
            own.evidence(np.float32(1), cells, values); pure.evidence(np.float32(1), cells, values)
        if i % 9 == 4:
            region, amount = int(m.cell_region[truth]), 0.02
            events.append(("bias", dict(region=region, amount=amount)))
            own.bias(region, amount)
        dt, speed = float(np.float32(0.13333334)), float(np.float32(3.1 if i % 30 < 20 else 2.0))
        events.append(("predict", dict(t=t, dt=dt, speed=speed)))
        own.predict(dt, speed); pure.predict(dt, speed)
        t += dt
    meta = dict(shift=4, seed=1000 + seed, rung="C", agent="efficient", motion="m0", shadows=["m0"])
    _write_trace(path, m, fs, events, meta)
    return logged
