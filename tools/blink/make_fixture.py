#!/usr/bin/env python3
"""A small synthetic dataset in exactly the recorder's format, to check the whole pipeline
(train -> evaluate) without a camera or anyone's eyes.

The eyes are drawn, the blinks have the human shape (BlinkTracker's synthetic source), and each
helper's reading is simulated with its own kind of flaw: EAR noisy and self-scaling, the
blendshapes a frame behind, Apple Vision on a second camera stream with an eyelid outline that
never quite closes. Labels are the drawn truth (closed = more than half shut) with a
labeller's one-frame slips. Every file is marked synthetic and eval_blink.py says so: numbers
from it say the scripts work, not how well anything detects blinks.

    python make_fixture.py --out /tmp/blink_fixture
    python train_eye_cnn.py --data /tmp/blink_fixture --out /tmp/blink_fixture/eye_cnn.onnx
    python eval_blink.py --data /tmp/blink_fixture --onnx /tmp/blink_fixture/eye_cnn.onnx
"""
from __future__ import annotations

import argparse
import math
import os

import numpy as np

import blink_data as bd
import blink_server as bs

YY, XX = np.mgrid[0:bd.CROP, 0:bd.CROP].astype(np.float32) + 0.5


# ---- when the eyes are shut ---------------------------------------------------------------------


def human_blink(rng, start):
    """SyntheticBlinks' shape: 220-380 ms, a third closing and two thirds opening. Some blinks
    don't close all the way."""
    length = 0.22 + rng.random() * 0.16
    peak = 1.0 if rng.random() > 0.12 else rng.uniform(0.3, 0.65)
    return (start, length * 0.33, rng.uniform(0.0, 0.04), length * 0.67, peak)


def closure(events, t):
    c = np.zeros_like(t)
    for start, close, hold, opening, peak in events:
        x = t - start
        v = np.where(x < close + hold, np.clip(x / close, 0, 1), np.clip(1 - (x - close - hold) / opening, 0, 1))
        v[(x < 0) | (x > close + hold + opening)] = 0
        c = np.maximum(c, peak * v)
    return c


def timeline(rng, seconds):
    """Closures, SPACE windows and a look-away, following record_dataset.py's protocol."""
    events = [human_blink(rng, rng.uniform(1.0, 2.0))]                                   # eyes-open step
    shut_at, open_at = 3.0 + rng.uniform(0.25, 0.5), 6.5 + rng.uniform(0.2, 0.4)          # eyes-shut step
    events.append((shut_at, 0.15, open_at - shut_at - 0.15, 0.2, 1.0))
    events += [human_blink(rng, s + rng.uniform(0, 0.4)) for s in (7.3, 8.8, 10.3)]      # 3 blinks
    reserved, space = [], []
    free = seconds - 14.0
    for k in range(3):                                                                    # on purpose, with SPACE
        start = 14.0 + free * (k + 0.5) / 3.5
        hold = rng.uniform(0.6, 1.5)
        events.append((start, 0.12, hold, 0.2, 1.0))
        space.append((start - rng.uniform(0.2, 0.6), start + hold + 0.32 + rng.uniform(0.3, 0.8)))
        reserved.append((space[-1][0] - 0.3, space[-1][1] + 0.3))
    away_at = 14.0 + free * 0.9
    away = (away_at, away_at + rng.uniform(0.8, 1.5))
    reserved.append((away[0] - 0.5, away[1] + 0.5))
    t = 13.0
    while True:                                                                           # natural blinks, 17/min
        t += max(0.6, -math.log(1 - rng.random()) * 60 / 17)
        if t > seconds - 0.5:
            break
        if not any(a < t + 0.5 and t < b for a, b in reserved):
            events.append(human_blink(rng, t))
    return events, space, away


# ---- drawing an eye -----------------------------------------------------------------------------


def eye_style(rng):
    return {"cx": 12 + rng.uniform(-1, 1), "cy": 12.5 + rng.uniform(-1, 1), "a": rng.uniform(7.5, 9.5),
            "b": rng.uniform(3.6, 4.8), "skin": rng.uniform(120, 170), "sclera": rng.uniform(190, 225),
            "iris": rng.uniform(2.8, 3.6), "iris_tone": rng.uniform(45, 95), "shade": rng.uniform(-1.5, 1.5)}


def blur(img):
    p = np.pad(img, 1, mode="edge")
    return sum(p[dy:dy + bd.CROP, dx:dx + bd.CROP] for dy in range(3) for dx in range(3)) / 9.0


def draw_eye(c, st, gaze, gain, bias, noise, rng):
    cx, cy, a, b = st["cx"], st["cy"], st["a"], st["b"]
    img = st["skin"] + (YY - 12) * st["shade"]
    ellipse = ((XX - cx) / a) ** 2 + ((YY - cy) / b) ** 2
    inside = ellipse <= 1
    img = np.where(inside, st["sclera"], img)
    ix, iy = cx + gaze[0], cy + gaze[1]
    d2 = (XX - ix) ** 2 + (YY - iy) ** 2
    img = np.where(inside & (d2 <= st["iris"] ** 2), st["iris_tone"], img)
    img = np.where(inside & (d2 <= (0.45 * st["iris"]) ** 2), 25.0, img)
    lid = cy - b + c * 2 * b                       # the upper lid comes down over the eye
    img = np.where(inside & (YY < lid), st["skin"] - 18, img)
    img = np.where((np.abs(YY - lid) < 0.9) & (ellipse <= 1.15), 35.0, img)
    img = blur(img) * gain + bias + rng.normal(0, noise, img.shape)
    return np.clip(img, 0, 255).astype(np.uint8)


# ---- the helpers' readings ----------------------------------------------------------------------


class VisionScale:
    """BlinkVision.swift's EyeScale: open is the 80th percentile of the last 8 s."""

    def __init__(self, fps):
        self.keep, self.history = max(90, int(fps * 8)), []

    def closed(self, ratio):
        self.history = (self.history + [ratio])[-self.keep:]
        ordered = sorted(self.history)
        open_ = ordered[int((len(ordered) - 1) * 0.8)]
        return 0.0 if open_ <= 1e-4 else max(0.0, min(1.0, (1 - ratio / open_) / (1 - 0.55)))


def make_session(rng, folder, index, person, seconds, fps, labelled):
    period = 1.0 / fps
    steps = period * (1 + rng.normal(0, 0.03, int(seconds * fps * 1.1)))
    steps[rng.random(len(steps)) < 0.01] *= 2                       # the odd dropped frame
    rel = np.cumsum(steps)
    rel = rel[rel < seconds] - rel[0]
    n = len(rel)
    events, space, away = timeline(rng, seconds)
    c = closure(events, rel)
    face = ~((rel >= away[0]) & (rel < away[1]))
    phase = np.select([rel < 3.0, rel < 6.5, rel < 12.5], [bd.PHASE_OPEN, bd.PHASE_SHUT, bd.PHASE_BLINKS], bd.PHASE_FREE)

    style = eye_style(rng)
    mirrored = dict(style, cx=24 - style["cx"])
    gain, bias, noise = rng.uniform(0.75, 1.2), rng.uniform(-20, 20), rng.uniform(3, 8)
    x = np.zeros((n, 2, bd.CROP, bd.CROP), np.uint8)
    gaze = np.zeros(2)
    for i in range(n):
        gaze = 0.9 * gaze + rng.normal(0, 0.25, 2)
        if face[i]:
            ci = min(1.0, max(0.0, c[i] + rng.normal(0, 0.02)))
            x[i, 0] = draw_eye(ci, style, gaze, gain, bias, noise, rng)
            x[i, 1] = draw_eye(ci, mirrored, gaze * [-1, 1], gain, bias, noise, rng)

    nan = np.full(n, np.nan, np.float32)
    ear, ear_scale, ear_open = nan.copy(), bs.EarScale(), rng.uniform(0.27, 0.34)
    glitch = np.convolve(rng.random(n) < 0.004, np.ones(3), "same") > 0       # head moves fool EAR
    for i in np.nonzero(face)[0]:
        raw = ear_open * (1 - 0.78 * c[i]) * (0.7 if glitch[i] else 1.0) + rng.normal(0, 0.01)
        ear[i] = min(1.0, max(0.0, ear_scale.closed(raw)))
    behind = np.r_[c[0], c[:-1]]                                               # one frame behind
    shapes = np.clip(0.05 + 0.85 / (1 + np.exp(-(behind - 0.5) * 10)) + rng.normal(0, 0.025, n), 0, 1)
    shapes[~face] = np.nan
    seen = closure(events, rel + rng.uniform(-0.5, 0.5, n) * period)           # its own camera stream
    vision, vision_scale, ratio_open = nan.copy(), VisionScale(fps), rng.uniform(0.28, 0.36)
    for i in np.nonzero(face)[0]:
        vision[i] = vision_scale.closed(ratio_open * (1 - 0.5 * seen[i]) + rng.normal(0, 0.012))

    sid = f"synth{index:02d}"
    path = os.path.join(folder, sid + ".npz")
    bd.save_session(
        path, session=sid, person=person, t=1.76e9 + index * 86400 + rel, x=x,
        eye_ok=np.repeat(face[:, None], 2, axis=1), phase=phase,
        space=np.any([(rel >= a) & (rel < b) for a, b in space], axis=0),
        signals={"ear": ear, "blendshapes": shapes.astype(np.float32), "vision": vision},
        ms={"mesh": rng.normal(6.5, 0.8, n), "blendshapes": rng.normal(10, 1.5, n), "vision": rng.normal(14, 3, n)},
        synthetic=True, notes="make_fixture.py", created="synthetic")
    if labelled:
        closed = c > 0.5
        for a, b in bd.runs(c > 0.5):                                          # a labeller's slips:
            if rng.random() < 0.15:                                            # an edge one frame off
                if rng.random() < 0.5 and a > 0:
                    closed[a - 1] = True
                elif b - a > 1:
                    closed[a] = False
            if rng.random() < 0.15:
                if rng.random() < 0.5 and b < n:
                    closed[b] = True
                elif b - a > 2:
                    closed[b - 1] = False
        ignore = ~face
        ignore |= np.r_[ignore[1:], False] | np.r_[False, ignore[:-1]]
        bd.save_labels(bd.labels_path(path), sid, bd.Labels(closed, ignore, np.ones(n, bool), "fixture"))
    return path


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", required=True)
    ap.add_argument("--sessions", type=int, default=5)
    ap.add_argument("--labelled", type=int, default=3, help="how many of them (the latest) have labels")
    ap.add_argument("--minutes", type=float, default=1.5)
    ap.add_argument("--fps", type=float, default=30.0)
    ap.add_argument("--seed", type=int, default=0)
    args = ap.parse_args(argv)
    if args.minutes * 60 < 30:
        raise SystemExit("--minutes must leave room after the 12.5 s calibration (at least 0.5)")

    os.makedirs(args.out, exist_ok=True)
    rng = np.random.default_rng(args.seed)
    for i in range(args.sessions):
        path = make_session(rng, args.out, i + 1, ("p1", "p2")[i % 2], args.minutes * 60, args.fps,
                            labelled=i >= args.sessions - args.labelled)
        print(f"wrote {path}{' (+ labels)' if os.path.exists(bd.labels_path(path)) else ''}")


if __name__ == "__main__":
    main()
