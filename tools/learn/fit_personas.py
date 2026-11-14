#!/usr/bin/env python3
"""Trait fitting (0.4.0, build plan §7.5, C3.4): which persona plays like this person?

    python3 tools/learn/fit_personas.py <bot shift records…> --people <dataset or shift records…> [--ranges out.json]

1. Measure what shows in a shift, the same way for bots and people (scorecard.features, plus how
   alike its routes are and how fast it answers her chores).
2. From bot shifts whose personas are known (the label carries the 14 traits), fit how each measure
   depends on the traits: least squares on the traits and their squares.
3. For each person, find the persona whose predicted measures are closest to theirs, inside 0–1,
   pulled gently toward the middle where the measures say nothing (a small ridge).
4. Set the training ranges to cover the people found, with a margin (--ranges writes a league's
   "ranges": {"low": [14], "high": [14]}).

Only traits that change what can be measured can be recovered; the report says which, and how
well (the leave-out error on bots it wasn't told about).
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from collections import defaultdict

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dataset as ds  # noqa: E402
import records  # noqa: E402
import scorecard  # noqa: E402

TRAITS = ["diligence", "caution", "pace", "habit", "consistency", "attention", "curiosity", "grudge", "calming_down",
          "fear_dark", "fear_sounds", "fear_her", "fear_dashes", "fear_chase"]
MEASURES = ["jobs_per_min", "sprint_share", "crouch_share", "still_share", "hides", "back_offs", "saw_her_share", "route_alike"]


def player_measures(shifts: list) -> np.ndarray:
    """One player's measures, averaged over their shifts; route_alike is the mean overlap of their
    shifts' routes (how much of a habit they have)."""
    f = [scorecard.features(s) for s in shifts]
    m = [float(np.mean([x[k] for x in f])) for k in MEASURES[:-1]]
    cells = [scorecard.cells(s) for s in shifts]
    pairs = [scorecard.jaccard(cells[i], cells[j]) for i in range(len(cells)) for j in range(i + 1, len(cells))]
    m.append(float(np.mean(pairs)) if pairs else 0.0)
    return np.array(m)


def design(traits: np.ndarray) -> np.ndarray:
    t = np.atleast_2d(traits)
    return np.hstack([np.ones((t.shape[0], 1)), t, t ** 2])


class Model:
    """measures ≈ design(traits) · W, fitted on bots with known personas."""

    def __init__(self, traits: np.ndarray, measures: np.ndarray, ridge: float = 1e-2):
        self.mu = measures.mean(0)
        self.sd = measures.std(0) + 1e-9
        y = (measures - self.mu) / self.sd
        x = design(traits)
        self.W = np.linalg.solve(x.T @ x + ridge * np.eye(x.shape[1]), x.T @ y)

    def predict(self, traits: np.ndarray) -> np.ndarray:
        return design(traits) @ self.W            # in standardised units

    def invert(self, measures: np.ndarray, prior: float = 0.05, iters: int = 60) -> np.ndarray:
        """The persona whose predicted measures are nearest: damped Gauss–Newton inside 0–1, from
        a few starts, with a small pull toward 0.5 (traits the measures can't see stay near it)."""
        target = (measures - self.mu) / self.sd
        n = len(TRAITS)
        w1, w2 = self.W[1:1 + n], self.W[1 + n:1 + 2 * n]

        def loss(t):
            r = self.predict(t)[0] - target
            return float(r @ r + prior * np.sum((t - 0.5) ** 2))

        best, best_loss = None, float("inf")
        starts = [np.full(n, 0.5)] + [np.random.default_rng(k).uniform(0.1, 0.9, n) for k in range(4)]
        for t in starts:
            lam = 1e-2
            for _ in range(iters):
                r = self.predict(t)[0] - target
                J = (w1 + 2 * t[:, None] * w2).T                     # measures × traits
                g = J.T @ r + prior * (t - 0.5)
                H = J.T @ J + (prior + lam) * np.eye(n)
                cand = np.clip(t - np.linalg.solve(H, g), 0.0, 1.0)
                if loss(cand) < loss(t):
                    t, lam = cand, max(lam / 3, 1e-6)
                else:
                    lam *= 10
                    if lam > 1e6:
                        break
            if loss(t) < best_loss:
                best, best_loss = t, loss(t)
        return best

    def identifiable(self, share: float = 0.2) -> list:
        """Traits whose effect on the measures is at least `share` of the strongest trait's."""
        n = len(TRAITS)
        effect = np.abs(self.W[1:1 + n]).sum(1) + np.abs(self.W[1 + n:1 + 2 * n]).sum(1)
        return [TRAITS[i] for i in range(n) if effect[i] >= share * effect.max()]


def by_player(shifts: list) -> dict:
    g = defaultdict(list)
    for s in shifts:
        g[s.player].append(s)
    return g


def known_bots(shifts: list) -> tuple:
    xs, ys, names = [], [], []
    for player, ss in sorted(by_player([s for s in shifts if s.is_bot]).items()):
        t = ds.persona_traits(player)
        if t is None:
            continue
        xs.append(t)
        ys.append(player_measures(ss))
        names.append(player)
    return np.array(xs), np.array(ys), names


def leave_out_error(traits: np.ndarray, measures: np.ndarray, folds: int = 5) -> np.ndarray:
    """Per trait, the mean absolute error of recovering a bot's persona when the fit never saw it."""
    n = len(traits)
    errs = np.zeros((n, traits.shape[1]))
    idx = np.arange(n)
    for k in range(folds):
        test = idx[k::folds]
        train = np.setdiff1d(idx, test)
        model = Model(traits[train], measures[train])
        for i in test:
            errs[i] = np.abs(model.invert(measures[i]) - traits[i])
    return errs.mean(0)


def ranges(people: np.ndarray, margin: float = 0.1) -> dict:
    lo = np.clip(people.min(0) - margin, 0, 1) if len(people) else np.zeros(len(TRAITS))
    hi = np.clip(people.max(0) + margin, 0, 1) if len(people) else np.ones(len(TRAITS))
    return {"low": [round(float(x), 3) for x in lo], "high": [round(float(x), 3) for x in hi]}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("bots", nargs="+")
    ap.add_argument("--people", nargs="*", default=[])
    ap.add_argument("--ranges")
    a = ap.parse_args(argv)
    bots = records.load_all(a.bots)
    traits, measures, _ = known_bots(bots)
    if len(traits) < 10:
        sys.exit(f"fit_personas: {len(traits)} bots with known personas; at least 10 are needed")
    err = leave_out_error(traits, measures)
    model = Model(traits, measures)
    print("Recovering a bot's traits it wasn't told (mean absolute error, leave-out):")
    for name, e in zip(TRAITS, err):
        print(f"  {name:14} {e:.3f}{'' if name in model.identifiable() else '   (barely shows in a shift: not recovered)'}")
    people_shifts = [s for s in records.load_all(a.people) if not s.is_bot]
    fitted = []
    for player, ss in sorted(by_player(people_shifts).items()):
        t = model.invert(player_measures(ss))
        fitted.append(t)
        print(f"{player}: " + ", ".join(f"{n} {v:.2f}" for n, v in zip(TRAITS, t)))
    if a.ranges:
        with open(a.ranges, "w") as f:
            json.dump(ranges(np.array(fitted)), f)
        print("ranges written to " + a.ranges)
    return 0


if __name__ == "__main__":
    sys.exit(main())
