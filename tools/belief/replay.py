#!/usr/bin/env python3
"""Replay her belief from a recorded trace (0.4.0, build plan §8.5, C4.2), and the parity gate.

    python3 tools/belief/replay.py <trace folder> [--parity] [--motion p0|p1|p2|p3] [--senses s0|s1|s2]

--parity     replays with today's numbers (p0, s0) and compares with every "check" the game wrote
             (her whole belief, every 25 ticks). Nothing is trained until this passes, to 1e-3, on
             every sample (the gate). Exit code 1 if it doesn't.
otherwise    scores the replay against where the player really was: log p of their cell, every tick
             and out of view, one number per shift.
The senses are fitted first, then the mirror map, then P1–P3 (train.py), each on the same replay.
"""
from __future__ import annotations

import argparse
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import features as feat  # noqa: E402
import kernels  # noqa: E402
import trace as tr  # noqa: E402

FLOOR = 1e-6


class Replay:
    def __init__(self, m: kernels.Map, motion: str = "p0", senses: str = "s0", sense_numbers: dict | None = None,
                 weights: dict | None = None, use_hint: bool = True):
        self.m = m
        self.motion_id = motion
        self.senses_id = senses
        self.s = dict(kernels.S0, **(sense_numbers or {}))
        self.w = weights or feat.HAND
        self.use_hint = use_hint
        hand = kernels.HandMotion(self.w["p1"]) if motion == "p1" else kernels.HandMotion()
        self.dest = kernels.DestinationMotion() if motion in ("p2", "p3") else None
        self.grid = kernels.Grid(m, self.dest or hand)
        self.desire_sources = []
        self.max_diff = 0.0
        self.checks = 0
        self.scores = []          # (log p, seen) per predict with a truth line

    def layers(self, op):
        if "desire_sources" in op:
            self.desire_sources = op["desire_sources"]
            self.grid.desire = self.m.distances(self.desire_sources) if self.desire_sources else np.full(self.m.n, math.inf)
        if "habit" in op:
            h = np.zeros(self.m.n)
            for k, v in op["habit"].items():
                h[int(k)] = v
            self.grid.habit = h

    def step(self, op):
        kind = op["op"]
        g = self.grid
        if kind == "regions":
            self.m = self.m.with_regions(op["region"], op["count"])
            g.m = self.m
        elif kind == "layers":
            self.layers(op)
        elif kind == "state":
            g.b = np.asarray(op["b"], float)
            g.prior = np.asarray(op["prior"], float)
            if op.get("lines"):
                g.line = np.asarray(op["lines"], float).reshape(g.line.shape)
            g.line_clock = float(op.get("line_clock", 0.0))
            g.last_exit = int(op.get("last_exit", 0))
        elif kind == "reset":
            if op["sigma"] <= 0:
                g.reset_to_prior()
            else:
                g.reset_to(op["x"], op["z"], op["sigma"], op["outside"])
        elif kind == "obs":
            if self.senses_id != "s0":
                op = dict(op)
                op["conf"], op["sigma"] = kernels.remake(op, self.s, self.senses_id)
            g.apply(op)
        elif kind == "sweep":
            p = op["p"]
            if self.s["sweep_scale"] != kernels.S0["sweep_scale"]:
                p = [min(1.0, max(0.0, q / kernels.S0["sweep_scale"] * self.s["sweep_scale"])) for q in p]
            g.sweep(op["cells"], p)
        elif kind == "bias":
            if self.use_hint:
                g.bias(op["region"], op["amount"])
        elif kind == "features":
            if self.dest is not None or self.motion_id == "p1":
                rate, exit_rate, _, _ = feat.compute(self.m, op, self.w, read_ledger=self.motion_id == "p3")
                if self.dest is not None:
                    self.dest.rate, self.dest.exit_rate = rate, exit_rate
                if self.motion_id == "p1":
                    g.danger = feat.danger(self.m, op["thought"], self.w)
        elif kind == "predict":
            g.predict(op["dt"], op["speed"])
        elif kind == "check":
            b = np.asarray(op["b"], float)
            self.max_diff = max(self.max_diff, float(np.abs(b - g.b).max()))
            self.checks += 1
        elif kind == "truth":
            cell = op["cell"]
            if 0 <= cell < self.m.n:
                self.scores.append((math.log(max(FLOOR, g.b[cell])), bool(op["sees"])))

    def run(self, path: str):
        for op in tr.ops(path):
            self.step(op)
        return self

    def log_p(self, out_of_view_only: bool = False) -> float:
        xs = [lp for lp, seen in self.scores if not (out_of_view_only and seen)]
        return sum(xs) / len(xs) if xs else float("nan")


def parity(folder: str, tolerance: float = 1e-3) -> list:
    m = tr.load_map(folder)
    results = []
    for path in tr.shifts(folder):
        r = Replay(m).run(path)
        results.append((os.path.basename(path), r.checks, r.max_diff, r.checks > 0 and r.max_diff <= tolerance))
    return results


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("folder")
    ap.add_argument("--parity", action="store_true")
    ap.add_argument("--motion", default="p0")
    ap.add_argument("--senses", default="s0")
    ap.add_argument("--tolerance", type=float, default=1e-3)
    a = ap.parse_args(argv)
    if a.parity:
        res = parity(a.folder, a.tolerance)
        for name, checks, diff, ok in res:
            print(f"{'ok  ' if ok else 'FAIL'} {name}: {checks} checks, largest difference {diff:.2e}")
        good = res and all(ok for *_, ok in res)
        print("parity gate: " + ("passed" if good else "FAILED — nothing is trained until it passes"))
        return 0 if good else 1
    m = tr.load_map(a.folder)
    for path in tr.shifts(a.folder):
        r = Replay(m, a.motion, a.senses).run(path)
        print(f"{os.path.basename(path)}: log p {r.log_p():.3f}, out of view {r.log_p(True):.3f} ({len(r.scores)} ticks)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
