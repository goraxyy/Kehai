"""The destinations' chances and the walk's rates (MotionFeatures.cs), from a trace's "features" op.

The same arithmetic as the C#: each place's logit from its kind, how far it is from each cell
(ln(1 + metres)), how long it has waited, whether you'd think she stands between, your heading,
the clock as the shift ends, and (P3) your Ledger; a softmax per cell; then per edge the share of
mass a second moving along it, toward each place, bent away from where you think she is.
"""
from __future__ import annotations

import math

import numpy as np

KINDS = ["Job", "Queue", "Chore", "Breakers", "Coffee", "Clock", "Exit", "Hide", "Stay", "TurnBack"]
SPEEDS = (2.5, 4.0, 6.5)

HAND = dict(kind=[0.4, 0.8, 0.6, 1.2, -0.6, 0.2, -1.5, -0.4, 0.9, -1.2], distance=-0.9, wait=0.6, between=-1.2, heading=0.6,
            clock_late=2.0, ledger_answer=0.8, ledger_hide=1.0, speed_share=[0.15, 0.6, 0.25], avoid=0.6, danger_radius=6.0,
            p1=dict(moving=0.65, desire=0.9, habit=1.2, danger=0.6, speed=1.0))


def mean_speed(w):
    s = sum(a * b for a, b in zip(w["speed_share"], SPEEDS))
    t = sum(w["speed_share"])
    return s / t if t > 0 else 4.0


def danger(m, thought, w):
    cell = nearest_cell(m, thought[0], thought[1])
    if cell < 0:
        return np.zeros(m.n)
    r = max(0.5, w["danger_radius"])
    d = m.distances(cell, r * 4)
    return np.where(np.isinf(d), 0.0, np.exp(-np.where(np.isinf(d), 0.0, d) / r))


def nearest_cell(m, x, z, radius=3.0):
    d2 = (m.x - x) ** 2 + (m.z - z) ** 2
    i = int(np.argmin(d2)) if m.n else -1
    return i if i >= 0 and d2[i] < radius * radius else -1


def between(m, thought, px, pz):
    """Per cell: would you think she stands between it and the place (within 4 m of the way)?"""
    ax, az = m.x, m.z
    bx, bz = px, pz
    hx, hz = thought
    abx, abz = bx - ax, bz - az
    len2 = abx ** 2 + abz ** 2
    t = np.where(len2 > 0.01, ((hx - ax) * abx + (hz - az) * abz) / np.where(len2 > 0.01, len2, 1.0), -1.0)
    inside = (t > 0) & (t < 1)
    qx, qz = ax + abx * t - hx, az + abz * t - hz
    return inside & (qx ** 2 + qz ** 2 < 16.0) & (len2 >= 0.01)


def compute(m, op: dict, w: dict = HAND, read_ledger: bool = False):
    """(rates per edge, exit rates per cell, chances [cells × places], distances) for one refresh."""
    places = op["places"]
    k = len(places)
    blocked = ~m.hers
    dist = []
    for p in places:
        if p["cell"] < 0:
            dist.append(None)
            continue
        sources = [p["cell"]]
        if p["kind"] == "Exit" and m.exit_of[p["cell"]] >= 0:
            sources = m.exits[m.exit_of[p["cell"]]]
        dist.append(m.distances(sources, math.inf, blocked))
    dz = danger(m, op["thought"], w)
    logits = np.zeros((m.n, k))
    for i, p in enumerate(places):
        g = w["kind"][KINDS.index(p["kind"])]
        if p["kind"] in ("Job", "Chore", "Queue"):
            g += w["wait"] * p["waited"] / (p["waited"] + 30.0)
        if p["kind"] == "Clock":
            g += w["clock_late"] * ((1 - op["time_left"]) if op["open"] else 1.5)
        hx, hz = op["heading"]
        if p["cell"] >= 0 and hx * hx + hz * hz > 0.01:
            tx, tz = p["x"] - op["seen"][0], p["z"] - op["seen"][1]
            if tx * tx + tz * tz > 0.01:
                g += w["heading"] * (tx * hx + tz * hz) / math.hypot(tx, tz) / math.hypot(hx, hz)
        if read_ledger:
            if p["kind"] == "Chore" and p.get("answer_s", -1) >= 0:
                s = p["answer_s"]
                g += w["ledger_answer"] * (1 - s / (s + 60.0))
            if p["kind"] == "Hide":
                g += w["ledger_hide"] * p.get("habit", 0.0)
        col = np.full(m.n, g)
        if p["cell"] >= 0:
            d = dist[i]
            col = np.where(np.isinf(d), -np.inf, col + w["distance"] * np.log1p(np.where(np.isinf(d), 0.0, d)))
            col = np.where(between(m, op["thought"], p["x"], p["z"]) & ~np.isinf(col), col + w["between"], col)
        logits[:, i] = col
    mx = logits.max(axis=1, keepdims=True)
    e = np.where(np.isinf(logits), 0.0, np.exp(logits - np.where(np.isinf(mx), 0.0, mx)))
    s = e.sum(axis=1, keepdims=True)
    chance = np.where(s > 0, e / np.where(s > 0, s, 1.0), 0.0)

    rate = np.zeros(len(m.edge_to))
    exit_rate = np.zeros(m.n)
    move = mean_speed(w) / m.cell_size
    avoid = min(1.0, max(0.0, w["avoid"]))
    src, dst = m.edge_from, m.edge_to
    bend = 1.0 - avoid * dz[dst]
    hx, hz = op["heading"]
    for i, p in enumerate(places):
        pk = chance[:, i] * move
        if p["kind"] == "Stay":
            continue
        if p["kind"] == "TurnBack":
            gain = -((m.x[dst] - m.x[src]) * hx + (m.z[dst] - m.z[src]) * hz)
        else:
            d = dist[i]
            if d is None:
                continue
            arrived = d <= 0.01
            if p["kind"] == "Exit":
                ex = arrived & (m.exit_of >= 0) & m.hers & (chance[:, i] > 1e-5)
                exit_rate[ex] += pk[ex]
            gain = np.where(np.isinf(d[src]) | np.isinf(d[dst]), 0.0, d[src] - np.where(np.isinf(d[dst]), 0.0, d[dst]))
            gain = np.where(arrived[src], 0.0, gain)
        gain = np.where(gain > 0, gain, 0.0) * bend
        total = np.bincount(src, weights=gain, minlength=m.n)
        ok = (total[src] > 0) & m.hers[src] & (chance[src, i] > 1e-5)
        rate += np.where(ok, pk[src] * gain / np.where(total[src] > 0, total[src], 1.0), 0.0)
    return rate, exit_rate, chance, dist
