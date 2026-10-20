"""Real players' shift records (ShiftRecorder) → trajectories the motion models can be scored on.

    python from_shift_records.py <shift_records folder> --map <any packed trace .npz> --out <dir>
    python eval_offline.py horizon --data <dir> --weights out/weights.npz

A shift record holds where the player and Karen were five times a second, but not Karen's
evidence stream, so there is no filter to replay: only the motion model on its own (the
horizon curve). Each record becomes a packed "shift" with no evidence:
  - the map: today's, from a trace (`--map`); records whose positions don't all land on it
    (older layouts) are skipped;
  - the 2 Hz features from Karen's recorded position (her walking distance, her 30 s trail,
    whether she was chasing); the 2 s features she can't know from a record are left neutral
    (no desire field, no habit, no jobs, lights on, power as recorded);
  - a 10 Hz tick, and the player's true cell and velocity at each recorded frame.

The developer's own ~13 shifts are a smoke test of this path, not data: one person, who knows
the game, partly on older layouts. Playtesters' records are what it is for.
"""
from __future__ import annotations

import argparse
import heapq
import json
from pathlib import Path

import numpy as np

from trace import OP_FAST, OP_MAP, OP_PREDICT, OP_RESET, OP_SAMPLE, OP_SLOW, Shift, load_shift, save_shift

ON_MAP = 1.5          # metres: a position further than this from every cell is off the map
TICK = 0.1
FAST = 0.5
TRAIL = 30.0


def nearest_cells(m, xz: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    cells = np.stack([m.x, m.z], 1)
    out, dist = np.empty(len(xz), np.int64), np.empty(len(xz))
    for i in range(0, len(xz), 256):
        d = ((xz[i:i + 256, None, :] - cells[None, :, :]) ** 2).sum(-1)
        out[i:i + 256] = d.argmin(1)
        dist[i:i + 256] = np.sqrt(d.min(1))
    return out, dist


def dijkstra(m, source: int) -> np.ndarray:
    d = np.full(m.n, np.inf)
    d[source] = 0.0
    heap = [(0.0, source)]
    while heap:
        du, u = heapq.heappop(heap)
        if du > d[u]:
            continue
        for e in range(m.edge_start[u], m.edge_start[u + 1]):
            v, nd = m.edge_to[e], du + float(m.edge_len[e])
            if nd < d[v]:
                d[v] = nd
                heapq.heappush(heap, (nd, v))
    return d


def convert(record: dict, m, name: str) -> Shift | None:
    frames = record.get("frames") or []
    if len(frames) < 50:
        return None
    t = np.array([f[0] for f in frames], float)
    player = np.array([[f[1], f[2]] for f in frames], float)
    present = np.array([f[7] for f in frames], bool)
    karen = np.array([[f[8], f[9]] for f in frames], float)
    chase = np.array([f[13] for f in frames], np.uint8)
    sees = np.array([f[12] for f in frames], np.uint8)
    power = np.array([f[17] for f in frames], np.uint8)
    cell, dist = nearest_cells(m, player)
    if (dist > ON_MAP).mean() > 0.01:       # an older layout: the player walks where today's map has no floor
        return None
    kcell, kdist = nearest_cells(m, karen)
    kcell[(kdist > ON_MAP) | ~present] = -1

    n = m.n
    ops, dist_rows, fa, sl, pr, sa = [], [], [], [], [], []
    trail_cells, trail_values, trail_start, trail_count = [], [], [], []
    ops.append((OP_MAP, 0))
    sl.append((0.0, 1))
    ops.append((OP_SLOW, 0))
    walked, cache, last_fast, row = [], {}, -1e9, 0
    # 10 Hz ticks over the record; each recorded frame is a sample just before its tick.
    for now in np.arange(t[0], t[-1], TICK):
        while row < len(t) and t[row] <= now:
            v = (player[row] - player[row - 1]) / max(1e-3, t[row] - t[row - 1]) if row > 0 else np.zeros(2)
            sa.append((t[row], cell[row] if dist[row] <= ON_MAP else -1, sees[row], v[0], v[1]))
            ops.append((OP_SAMPLE, len(sa) - 1))
            row += 1
        if now - last_fast >= FAST:
            last_fast = now
            i = min(max(row - 1, 0), len(t) - 1)
            k = int(kcell[i])
            if k not in cache:
                d = dijkstra(m, k) if k >= 0 else np.full(n, np.inf)
                cache[k] = np.where(np.isfinite(d), np.minimum(np.rint(d * 100), 65534), 65535).astype(np.uint16)
            if not dist_rows or not np.array_equal(dist_rows[-1], cache[k]):
                dist_rows.append(cache[k])
            if k >= 0:
                walked.append((now, k))
            walked = [(tt, c) for tt, c in walked if now - tt <= TRAIL]
            trail = {}
            for tt, c in walked:
                v = 1 - (now - tt) / TRAIL
                for x in [c, *m.edge_to[m.edge_start[c]:m.edge_start[c + 1]].tolist()]:
                    trail[x] = max(trail.get(x, 0.0), v)
            trail_start.append(sum(trail_count))
            trail_count.append(len(trail))
            trail_cells += list(trail)
            trail_values += list(trail.values())
            fa.append((now, k, chase[i], 0.0 if sees[i] else 10.0, 3.1, len(dist_rows) - 1, power[i]))
            ops.append((OP_FAST, len(fa) - 1))
            if len(fa) == 1:
                ops.append((OP_RESET, 0))
        pr.append((now, TICK, 3.1))
        ops.append((OP_PREDICT, len(pr) - 1))
    if not sa:
        return None

    f32, i32, u8 = np.float32, np.int32, np.uint8
    empty = np.zeros(0, f32)
    return Shift(
        path=name, shift=int(record.get("shift", 0)), seed=0, rung=str(record.get("rung", ""))[:1], agent="player",
        motion="m0", shadows=[], duration=float(record.get("length", 0)), maps=[m],
        ops=np.array(ops, i32), op_map=np.zeros(len(ops), i32),
        ev_channel=np.zeros(0, u8), ev_flags=np.zeros(0, u8), ev_floor=empty, ev_start=np.zeros(0, np.int64),
        ev_count=np.zeros(0, np.int64), ev_cells=np.zeros(0, i32), ev_values=empty,
        bias_region=np.zeros(0, i32), bias_amount=empty,
        pr_t=np.array([p[0] for p in pr], f32), pr_dt=np.array([p[1] for p in pr], f32), pr_speed=np.array([p[2] for p in pr], f32),
        fa_t=np.array([x[0] for x in fa], f32), fa_version=np.arange(len(fa), dtype=i32), fa_cell=np.array([x[1] for x in fa], i32),
        fa_chasing=np.array([x[2] for x in fa], u8), fa_since=np.array([x[3] for x in fa], f32),
        fa_speed=np.array([x[4] for x in fa], f32), fa_dist_row=np.array([x[5] for x in fa], i32),
        fa_trail_start=np.array(trail_start, np.int64), fa_trail_count=np.array(trail_count, np.int64),
        trail_cells=np.array(trail_cells, i32), trail_values=np.array(trail_values, f32), dist_rows=np.stack(dist_rows),
        sl_t=np.zeros(1, f32), sl_version=np.ones(1, i32), sl_jobs=np.zeros(1, i32), sl_done=np.zeros(1, u8),
        sl_frac=np.zeros(1, f32), sl_overtime=np.zeros(1, u8), sl_power=np.ones(1, u8),
        sl_light_row=np.zeros(1, i32), sl_desire_row=np.zeros(1, i32), sl_habit_row=np.zeros(1, i32),
        light_rows=np.full((1, n), 255, u8), desire_rows=np.full((1, n), np.inf, f32), habit_rows=np.zeros((1, n), f32),
        re_t=np.zeros(1, f32), re_kind=np.full(1, 3, u8),
        re_b=np.full((1, n), 1.0 / n, f32), re_prior=np.full((1, n), 1.0 / n, f32),
        sa_t=np.array([x[0] for x in sa], f32), sa_truth=np.array([x[1] for x in sa], i32), sa_view=np.array([x[2] for x in sa], u8),
        sa_since=np.zeros(len(sa), f32), sa_vx=np.array([x[3] for x in sa], f32), sa_vz=np.array([x[4] for x in sa], f32),
        sa_own=np.zeros(len(sa), f32), sa_last_seen=np.zeros(len(sa), f32), sa_shadow=np.zeros((len(sa), 0), f32),
        sa_collapses=np.zeros(len(sa), i32))


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("records")
    ap.add_argument("--map", required=True, help="a packed trace (.npz) of today's map")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    m = load_shift(a.map).map
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    kept = skipped = 0
    for f in sorted(Path(a.records).glob("shift_*.json")):
        if f.name.endswith(".markers.json"):
            continue
        try:
            record = json.loads(f.read_text())
        except (OSError, json.JSONDecodeError):
            continue
        s = convert(record, m, f.name)
        if s is None:
            skipped += 1
            print(f"skipped {f.name}: too short, or not on today's map")
            continue
        save_shift(s, out / (f.stem + ".npz"))
        kept += 1
        print(f"{f.name}: {len(s.sa_t)} frames, {len(s.pr_t)} ticks")
    print(f"{kept} records converted, {skipped} skipped → {out}")


if __name__ == "__main__":
    main()
