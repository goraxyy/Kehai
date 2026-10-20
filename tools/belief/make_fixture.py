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
    start, to, length = [0] * (n + 1), [], []
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
                    dx, dz = np.float32(x[other] - x[cell]), np.float32(z[other] - z[cell])
                    length.append(np.float32(np.sqrt(np.float32(dx * dx + np.float32(0) + dz * dz))))
                    linked = True
            if k < 4:
                straight[k] = linked
    start[n] = len(to)
    edge_to = np.array(to, np.int32)
    edge_start = np.array(start, np.int32)
    frm = np.repeat(np.arange(n), np.diff(edge_start))
    slot = np.array([_slot(x[b] - x[a], z[b] - z[a]) for a, b in zip(frm, edge_to)], np.uint8)
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
