"""Read Karen's belief traces (Eval/Scripts/BeliefTrace.cs) into numpy arrays.

A trace is one shift: the map, then every event of her filter in the order it happened —
evidence (as the likelihoods C# multiplied in), the Director's hint, resets, predict steps,
the motion features as they refreshed, and 5 Hz samples of the truth. `read_trace` turns it
into a `Shift`, whose arrays are what the replay (replay.py) and the training (train.py) use.
`save_shift` / `load_shift` keep a compact .npz of it, so the raw traces can be deleted.

The record layouts here and in BeliefTrace.cs must match.
"""
from __future__ import annotations

import gzip
import io
import struct
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

MAGIC = b"KBTR"
VERSION = 2

MAP, SHIFT, EVIDENCE, BIAS, PREDICT, FAST, SLOW, RESET, SAMPLE, PROBE, END = range(1, 12)

# Event codes in Shift.ops (the order of the filter's work).
OP_EVIDENCE, OP_BIAS, OP_PREDICT, OP_FAST, OP_SLOW, OP_RESET, OP_SAMPLE, OP_MAP = range(8)

CHANNELS = ["sight", "hearing", "trace", "testimony", "infrastructure", "absence", "touch", "blink"]
FAR = np.float32(999.0)


@dataclass
class MapData:
    version: int
    x: np.ndarray            # f32[n]
    z: np.ndarray            # f32[n]
    edge_start: np.ndarray   # i32[n+1]
    edge_to: np.ndarray      # i32[E]
    edge_len: np.ndarray     # f32[E]
    cell_region: np.ndarray  # i32[n]
    region_outdoors: np.ndarray  # u8[R]
    open3: np.ndarray        # f32[n]
    area: np.ndarray         # u8[n]
    door: np.ndarray         # u8[n]
    choke: np.ndarray        # u8[n]
    clock: np.ndarray        # f32[n]
    exit: np.ndarray         # f32[n]
    edge_slot: np.ndarray    # u8[E]

    @property
    def n(self) -> int:
        return len(self.x)

    @property
    def edges(self) -> int:
        return len(self.edge_to)

    @property
    def edge_from(self) -> np.ndarray:
        return np.repeat(np.arange(self.n, dtype=np.int64), np.diff(self.edge_start))

    def region_cells(self, region: int) -> np.ndarray:
        return np.nonzero(self.cell_region == region)[0]


@dataclass
class Shift:
    """One shift of a trace, as arrays. Events are in `ops` (code, row): row indexes the table
    for that code. Per-cell fields that only change now and then (desire, habit, light, her
    distance field) are stored once per change, and each refresh row points at its copy."""
    path: str
    shift: int
    seed: int
    rung: str
    agent: str
    motion: str
    shadows: list[str]
    duration: float
    maps: list[MapData]
    ops: np.ndarray                         # i32[K, 2]: (code, row)
    # evidence
    ev_channel: np.ndarray; ev_flags: np.ndarray; ev_floor: np.ndarray
    ev_start: np.ndarray; ev_count: np.ndarray
    ev_cells: np.ndarray; ev_values: np.ndarray
    # bias
    bias_region: np.ndarray; bias_amount: np.ndarray
    # predict
    pr_t: np.ndarray; pr_dt: np.ndarray; pr_speed: np.ndarray
    # fast refresh
    fa_t: np.ndarray; fa_version: np.ndarray; fa_cell: np.ndarray; fa_chasing: np.ndarray
    fa_since: np.ndarray; fa_speed: np.ndarray; fa_dist_row: np.ndarray
    fa_trail_start: np.ndarray; fa_trail_count: np.ndarray
    trail_cells: np.ndarray; trail_values: np.ndarray
    dist_rows: np.ndarray                   # u16[D, n]
    # slow refresh
    sl_t: np.ndarray; sl_version: np.ndarray; sl_jobs: np.ndarray; sl_done: np.ndarray
    sl_frac: np.ndarray; sl_overtime: np.ndarray; sl_power: np.ndarray
    sl_light_row: np.ndarray; sl_desire_row: np.ndarray; sl_habit_row: np.ndarray
    light_rows: np.ndarray                  # u8[L, n]
    desire_rows: np.ndarray                 # f32[Q, n]
    habit_rows: np.ndarray                  # f32[H, n]
    # resets
    re_t: np.ndarray; re_kind: np.ndarray; re_b: np.ndarray; re_prior: np.ndarray   # f32[Z, n]
    # samples
    sa_t: np.ndarray; sa_truth: np.ndarray; sa_view: np.ndarray; sa_since: np.ndarray
    sa_vx: np.ndarray; sa_vz: np.ndarray; sa_own: np.ndarray; sa_last_seen: np.ndarray
    sa_shadow: np.ndarray                   # f32[S, shadows]
    sa_collapses: np.ndarray
    # which map each op belongs to
    op_map: np.ndarray
    probes: list = field(default_factory=list)   # (op index, cell, cell features, edge features)
    kernel_probes: list = field(default_factory=list)   # (op index, shadow id, cell, rates[h], shares[edge, h])
    end_collapses: int = 0

    @property
    def map(self) -> MapData:
        return self.maps[-1]

    @property
    def career(self) -> int:
        """The career's seed: one player profile's run of shifts on one seed (the bootstrap unit)."""
        return self.seed


class _Reader:
    def __init__(self, data: bytes):
        self.b = memoryview(data)
        self.i = 0

    def done(self) -> bool:
        return self.i >= len(self.b)

    def take(self, fmt: str):
        size = struct.calcsize(fmt)
        v = struct.unpack_from(fmt, self.b, self.i)
        self.i += size
        return v

    def u8(self) -> int: return self.take("<B")[0]
    def i32(self) -> int: return self.take("<i")[0]
    def f32(self) -> float: return self.take("<f")[0]

    def arr(self, dtype, count: int) -> np.ndarray:
        dt = np.dtype(dtype).newbyteorder("<")
        size = dt.itemsize * count
        a = np.frombuffer(self.b[self.i:self.i + size], dtype=dt, count=count).copy()
        self.i += size
        return a.astype(dt.newbyteorder("="))

    def text(self) -> str:
        n = self.take("<H")[0]
        s = bytes(self.b[self.i:self.i + n]).decode("utf-8")
        self.i += n
        return s


def read_trace(path: str | Path) -> Shift:
    path = str(path)
    with gzip.open(path, "rb") as f:
        data = f.read()
    r = _Reader(data)
    if bytes(r.b[0:4]) != MAGIC:
        raise ValueError(f"{path}: not a belief trace")
    r.i = 4
    version = r.i32()
    if version not in (1, VERSION):
        raise ValueError(f"{path}: trace version {version}, reader knows 1–{VERSION}")

    meta = {}
    maps: list[MapData] = []
    ops: list[tuple[int, int]] = []
    op_map: list[int] = []
    t = {k: [] for k in ["ev_channel", "ev_flags", "ev_floor", "ev_start", "ev_count", "bias_region", "bias_amount",
                         "pr_t", "pr_dt", "pr_speed", "fa_t", "fa_version", "fa_cell", "fa_chasing", "fa_since",
                         "fa_speed", "fa_dist_row", "fa_trail_start", "fa_trail_count", "sl_t", "sl_version",
                         "sl_jobs", "sl_done", "sl_frac", "sl_overtime", "sl_power", "sl_light_row",
                         "sl_desire_row", "sl_habit_row", "re_t", "re_kind", "sa_t", "sa_truth", "sa_view",
                         "sa_since", "sa_vx", "sa_vz", "sa_own", "sa_last_seen", "sa_collapses"]}
    ev_cells, ev_values, trail_cells, trail_values = [], [], [], []
    dist_rows, light_rows, desire_rows, habit_rows, re_b, re_prior, sa_shadow = [], [], [], [], [], [], []
    ev_at = tr_at = 0
    probes = []
    kernel_probes = []
    end_collapses = 0

    def op(code: int, row: int):
        ops.append((code, row))
        op_map.append(len(maps) - 1)

    while not r.done():
        rec = r.u8()
        if rec == SHIFT:
            meta["time"] = r.f32()
            meta["shift"] = r.i32()
            meta["seed"] = r.i32()
            meta["rung"] = r.text()
            meta["agent"] = r.text()
            meta["motion"] = r.text()
            meta["shadows"] = [r.text() for _ in range(r.i32())]
            meta["duration"] = r.f32()
        elif rec == MAP:
            ver, n, e, regions = r.i32(), r.i32(), r.i32(), r.i32()
            m = MapData(
                version=ver,
                x=r.arr(np.float32, n), z=r.arr(np.float32, n),
                edge_start=r.arr(np.int32, n + 1), edge_to=r.arr(np.int32, e), edge_len=r.arr(np.float32, e),
                cell_region=r.arr(np.int32, n), region_outdoors=r.arr(np.uint8, regions),
                open3=r.arr(np.float32, n), area=r.arr(np.uint8, n), door=r.arr(np.uint8, n),
                choke=r.arr(np.uint8, n), clock=r.arr(np.float32, n), exit=r.arr(np.float32, n),
                edge_slot=r.arr(np.uint8, e))
            maps.append(m)
            op(OP_MAP, len(maps) - 1)
            # A new map starts every per-cell field afresh.
            dist_rows.append(np.full(n, 65535, np.uint16))
            light_rows.append(np.full(n, 255, np.uint8))
            desire_rows.append(np.full(n, np.inf, np.float32))
            habit_rows.append(np.zeros(n, np.float32))
        elif rec == SLOW:
            n = maps[-1].n
            t["sl_t"].append(r.f32()); t["sl_version"].append(r.i32()); t["sl_jobs"].append(r.i32())
            t["sl_done"].append(r.u8()); t["sl_frac"].append(r.f32()); t["sl_overtime"].append(r.u8())
            t["sl_power"].append(r.u8())
            if r.u8(): light_rows.append(r.arr(np.uint8, n))
            if r.u8(): desire_rows.append(r.arr(np.float32, n))
            if r.u8(): habit_rows.append(r.arr(np.float32, n))
            t["sl_light_row"].append(len(light_rows) - 1)
            t["sl_desire_row"].append(len(desire_rows) - 1)
            t["sl_habit_row"].append(len(habit_rows) - 1)
            op(OP_SLOW, len(t["sl_t"]) - 1)
        elif rec == FAST:
            n = maps[-1].n
            t["fa_t"].append(r.f32()); t["fa_version"].append(r.i32()); t["fa_cell"].append(r.i32())
            t["fa_chasing"].append(r.u8()); t["fa_since"].append(r.f32()); t["fa_speed"].append(r.f32())
            if r.u8(): dist_rows.append(r.arr(np.uint16, n))
            t["fa_dist_row"].append(len(dist_rows) - 1)
            k = r.i32()
            trail_cells.append(r.arr(np.uint16, k).astype(np.int32)); trail_values.append(r.arr(np.float32, k))
            t["fa_trail_start"].append(tr_at); t["fa_trail_count"].append(k); tr_at += k
            op(OP_FAST, len(t["fa_t"]) - 1)
        elif rec == RESET:
            t["re_t"].append(r.f32()); t["re_kind"].append(r.u8())
            n = r.i32()
            re_b.append(r.arr(np.float32, n)); re_prior.append(r.arr(np.float32, n))
            op(OP_RESET, len(t["re_t"]) - 1)
        elif rec == EVIDENCE:
            t["ev_channel"].append(r.u8()); t["ev_flags"].append(r.u8()); t["ev_floor"].append(r.f32())
            k = r.i32()
            ev_cells.append(r.arr(np.uint16, k).astype(np.int32)); ev_values.append(r.arr(np.float32, k))
            t["ev_start"].append(ev_at); t["ev_count"].append(k); ev_at += k
            op(OP_EVIDENCE, len(t["ev_channel"]) - 1)
        elif rec == BIAS:
            t["bias_region"].append(r.i32()); t["bias_amount"].append(r.f32())
            op(OP_BIAS, len(t["bias_region"]) - 1)
        elif rec == PREDICT:
            t["pr_t"].append(r.f32()); t["pr_dt"].append(r.f32()); t["pr_speed"].append(r.f32())
            op(OP_PREDICT, len(t["pr_t"]) - 1)
        elif rec == SAMPLE:
            t["sa_t"].append(r.f32()); t["sa_truth"].append(r.i32()); t["sa_view"].append(r.u8())
            t["sa_since"].append(r.f32()); t["sa_vx"].append(r.f32()); t["sa_vz"].append(r.f32())
            t["sa_own"].append(r.f32()); t["sa_last_seen"].append(r.f32())
            k = r.u8()
            sa_shadow.append(r.arr(np.float32, k))
            t["sa_collapses"].append(r.i32())
            op(OP_SAMPLE, len(t["sa_t"]) - 1)
        elif rec == PROBE:
            cells = []
            for _ in range(r.i32()):
                cell = r.i32()
                cf = r.arr(np.float32, 24)
                ne = r.u8()
                ef = r.arr(np.float32, ne * 9).reshape(ne, 9)
                probes.append((len(ops), cell, cf, ef))
                cells.append((cell, ne))
            if version >= 2:
                for _ in range(r.u8()):
                    sid = r.text()
                    hc = r.u8()
                    for cell, ne in cells:
                        rates = r.arr(np.float32, hc)
                        shares = r.arr(np.float32, ne * hc).reshape(ne, hc)
                        kernel_probes.append((len(ops), sid, cell, rates, shares))
        elif rec == END:
            r.f32()
            end_collapses = r.i32()
            break
        else:
            raise ValueError(f"{path}: unknown record {rec} at byte {r.i - 1}")

    def a(name, dtype):
        return np.asarray(t[name], dtype=dtype)

    nshadow = len(meta.get("shadows", []))
    return Shift(
        path=path, shift=meta.get("shift", 0), seed=meta.get("seed", 0), rung=meta.get("rung", ""),
        agent=meta.get("agent", ""), motion=meta.get("motion", "m0"), shadows=meta.get("shadows", []),
        duration=meta.get("duration", 0.0), maps=maps,
        ops=np.asarray(ops, dtype=np.int32).reshape(-1, 2), op_map=np.asarray(op_map, dtype=np.int32),
        ev_channel=a("ev_channel", np.uint8), ev_flags=a("ev_flags", np.uint8), ev_floor=a("ev_floor", np.float32),
        ev_start=a("ev_start", np.int64), ev_count=a("ev_count", np.int64),
        ev_cells=np.concatenate(ev_cells) if ev_cells else np.zeros(0, np.int32),
        ev_values=np.concatenate(ev_values) if ev_values else np.zeros(0, np.float32),
        bias_region=a("bias_region", np.int32), bias_amount=a("bias_amount", np.float32),
        pr_t=a("pr_t", np.float32), pr_dt=a("pr_dt", np.float32), pr_speed=a("pr_speed", np.float32),
        fa_t=a("fa_t", np.float32), fa_version=a("fa_version", np.int32), fa_cell=a("fa_cell", np.int32),
        fa_chasing=a("fa_chasing", np.uint8), fa_since=a("fa_since", np.float32), fa_speed=a("fa_speed", np.float32),
        fa_dist_row=a("fa_dist_row", np.int32), fa_trail_start=a("fa_trail_start", np.int64),
        fa_trail_count=a("fa_trail_count", np.int64),
        trail_cells=np.concatenate(trail_cells) if trail_cells else np.zeros(0, np.int32),
        trail_values=np.concatenate(trail_values) if trail_values else np.zeros(0, np.float32),
        dist_rows=_stack(dist_rows, np.uint16),
        sl_t=a("sl_t", np.float32), sl_version=a("sl_version", np.int32), sl_jobs=a("sl_jobs", np.int32),
        sl_done=a("sl_done", np.uint8), sl_frac=a("sl_frac", np.float32), sl_overtime=a("sl_overtime", np.uint8),
        sl_power=a("sl_power", np.uint8), sl_light_row=a("sl_light_row", np.int32),
        sl_desire_row=a("sl_desire_row", np.int32), sl_habit_row=a("sl_habit_row", np.int32),
        light_rows=_stack(light_rows, np.uint8), desire_rows=_stack(desire_rows, np.float32),
        habit_rows=_stack(habit_rows, np.float32),
        re_t=a("re_t", np.float32), re_kind=a("re_kind", np.uint8), re_b=_stack(re_b, np.float32),
        re_prior=_stack(re_prior, np.float32),
        sa_t=a("sa_t", np.float32), sa_truth=a("sa_truth", np.int32), sa_view=a("sa_view", np.uint8),
        sa_since=a("sa_since", np.float32), sa_vx=a("sa_vx", np.float32), sa_vz=a("sa_vz", np.float32),
        sa_own=a("sa_own", np.float32), sa_last_seen=a("sa_last_seen", np.float32),
        sa_shadow=(np.stack(sa_shadow).astype(np.float32) if sa_shadow and nshadow else np.zeros((len(t["sa_t"]), 0), np.float32)),
        sa_collapses=a("sa_collapses", np.int32),
        probes=probes, kernel_probes=kernel_probes, end_collapses=end_collapses)


def _stack(rows, dtype):
    """Rows of one length stack; rows from different maps (a rebuild mid-shift) are padded."""
    if not rows:
        return np.zeros((0, 0), dtype)
    width = max(len(x) for x in rows)
    if all(len(x) == width for x in rows):
        return np.stack(rows).astype(dtype)
    out = np.zeros((len(rows), width), dtype)
    for i, x in enumerate(rows):
        out[i, :len(x)] = x
    return out


# ---- the compact copy ---------------------------------------------------------------------

_MAP_FIELDS = ["x", "z", "edge_start", "edge_to", "edge_len", "cell_region", "region_outdoors", "open3", "area",
               "door", "choke", "clock", "exit", "edge_slot"]
_SCALARS = ["path", "shift", "seed", "rung", "agent", "motion", "duration", "end_collapses"]


def save_shift(s: Shift, path: str | Path) -> None:
    arrays = {}
    for name, value in vars(s).items():
        if isinstance(value, np.ndarray):
            arrays[name] = value
    for i, m in enumerate(s.maps):
        arrays[f"map{i}_version"] = np.asarray(m.version)
        for f in _MAP_FIELDS:
            arrays[f"map{i}_{f}"] = getattr(m, f)
    arrays["meta_maps"] = np.asarray(len(s.maps))
    arrays["meta_shadows"] = np.asarray(",".join(s.shadows))
    for k in _SCALARS:
        arrays["meta_" + k] = np.asarray(getattr(s, k))
    np.savez_compressed(path, **arrays)


def load_shift(path: str | Path) -> Shift:
    z = np.load(path, allow_pickle=False)
    maps = []
    for i in range(int(z["meta_maps"])):
        maps.append(MapData(version=int(z[f"map{i}_version"]), **{f: z[f"map{i}_{f}"] for f in _MAP_FIELDS}))
    kw = {k: z[k] for k in z.files if not k.startswith("map") and not k.startswith("meta_")}
    shadows = str(z["meta_shadows"])
    return Shift(maps=maps, shadows=[x for x in shadows.split(",") if x], probes=[], kernel_probes=[],
                 path=str(z["meta_path"]), shift=int(z["meta_shift"]), seed=int(z["meta_seed"]),
                 rung=str(z["meta_rung"]), agent=str(z["meta_agent"]), motion=str(z["meta_motion"]),
                 duration=float(z["meta_duration"]), end_collapses=int(z["meta_end_collapses"]), **kw)


def describe(s: Shift) -> str:
    codes = np.bincount(s.ops[:, 0], minlength=8) if len(s.ops) else np.zeros(8, int)
    return (f"{Path(s.path).name}: shift {s.shift} seed {s.seed} rung {s.rung} {s.agent}, motion {s.motion}, "
            f"shadows {s.shadows}; {s.map.n} cells, {s.map.edges} edges; {codes[OP_PREDICT]} ticks, "
            f"{codes[OP_EVIDENCE]} likelihoods, {codes[OP_BIAS]} hints, {codes[OP_SAMPLE]} samples, "
            f"{codes[OP_FAST]}/{codes[OP_SLOW]} fast/slow refreshes, {codes[OP_RESET]} resets")


def pack(src: str | Path, dst: str | Path, delete: bool = False) -> int:
    """Every trace in `src` → a compact .npz in `dst` (then the trace can go)."""
    dst = Path(dst)
    dst.mkdir(parents=True, exist_ok=True)
    count = 0
    for f in sorted(Path(src).glob("*.kbt.gz")):
        out = dst / (f.name[: -len(".kbt.gz")] + ".npz")
        if not out.exists():
            s = read_trace(f)
            if s.end_collapses < 0 or len(s.sa_t) == 0:
                print(f"skipped {f.name}: no samples")
                continue
            save_shift(s, out)
            print(f"{out.name}: {describe(s)}")
        count += 1
        if delete:
            f.unlink()
    return count


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Belief traces: describe them, or pack them into .npz")
    sub = ap.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("describe")
    d.add_argument("paths", nargs="+")
    k = sub.add_parser("pack")
    k.add_argument("src")
    k.add_argument("dst")
    k.add_argument("--delete", action="store_true", help="delete each trace once packed")
    a = ap.parse_args()
    if a.cmd == "describe":
        for p in a.paths:
            print(describe(read_trace(p)))
    else:
        print(f"{pack(a.src, a.dst, a.delete)} shifts packed into {a.dst}")
