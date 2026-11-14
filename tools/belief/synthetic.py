"""The synthetic map of StoreMap.Synthetic, built by the same rule in Python (no outside cells):
`columns` × `rows` cells of 1.5 m, `open` saying which are floor, 8-neighbour links where a
diagonal counts only when both its straights do, one region per row."""
import math

import kernels

CELL = 1.5
DC = (1, -1, 0, 0, 1, 1, -1, -1)
DR = (0, 0, 1, -1, 1, -1, 1, -1)


def build(columns: int, rows: int, open_):
    grid_to_cell, xs, zs = [], [], []
    for r in range(rows):
        for c in range(columns):
            g = r * columns + c
            if open_ is None or open_[g]:
                grid_to_cell.append(len(xs))
                xs.append((c + 0.5) * CELL)
                zs.append((r + 0.5) * CELL)
            else:
                grid_to_cell.append(-1)
    n = len(xs)
    col = [0] * n
    row = [0] * n
    for g, cell in enumerate(grid_to_cell):
        if cell >= 0:
            col[cell], row[cell] = g % columns, g // columns
    start, to, length = [], [], []
    for cell in range(n):
        start.append(len(to))
        straight = [False] * 4
        for k in range(8):
            if k >= 4:
                if not straight[0 if DC[k] > 0 else 1] or not straight[2 if DR[k] > 0 else 3]:
                    continue
            nc, nr = col[cell] + DC[k], row[cell] + DR[k]
            linked = False
            if 0 <= nc < columns and 0 <= nr < rows:
                other = grid_to_cell[nr * columns + nc]
                if other >= 0:
                    to.append(other)
                    length.append(math.hypot(xs[cell] - xs[other], zs[cell] - zs[other]))
                    linked = True
            if k < 4:
                straight[k] = linked
    start.append(len(to))
    region_of_row = {}
    region = []
    for cell in range(n):
        r = int(math.floor(zs[cell] / CELL))
        region_of_row.setdefault(r, len(region_of_row))
        region.append(region_of_row[r])
    d = {"cells": n, "cell_size": CELL, "x": xs, "z": zs, "region": region, "regions": len(region_of_row), "hers": [1] * n,
         "edge_start": start, "edge_to": to, "edge_length": length, "outside_cell": -1, "exits": [], "exit_delay": [],
         "floor": 1e-4, "moving": 0.65, "desire_gain": 0.9, "habit_gain": 1.2, "danger_avoidance": 0.6, "uncertainty": 0.015,
         "outside_return": 0.08, "outside_prior": 0.002, "line_seconds": 40, "outside_cells": 0}
    return kernels.Map(d), d
