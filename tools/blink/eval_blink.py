#!/usr/bin/env python3
"""Score every blink detector on recorded sessions against your own frame-by-frame labels.

Each helper's reading goes through a copy of the game's BlinkTracker (calibration, smoothing,
hysteresis, the reopen prediction), so the numbers are what Karen would get, and every
method is scored on the same frames:

    python eval_blink.py --data eye_dataset                       # every labelled session
    python eval_blink.py --data eye_dataset --onnx eye_cnn.onnx   # + your CNN, on sessions it never saw
    python eval_blink.py --data eye_dataset --per-session --json results.json

Measured against your labels (label_blinks.py; closed = more than half shut), on the free
part of each session (the calibration at the start is used to calibrate, never scored):

  frames  precision, recall and F1 of "eyes closed" per frame; AP is the average precision
          of the raw reading before any threshold
  blinks  recall: the share of your blinks the game saw, with a 95% interval;
          false/min: blinks the game saw that you didn't (one blink seen twice counts once false)
  timing  onset: when the game learnt of a blink, after your first closed frame
          reopen seen: when the game saw the eyes open again, after your first open frame
          reopen predicted: when PredictedReopenIn said they would open, at the moment the
              blink was detected, against your first open frame: the median error, the mean
              absolute error, and how often it was late (Karen acting while you can see)
          window: how long the eyes really stayed shut after the game learnt of the blink
          (both only for blinks under half a second; longer closures are counted apart)
          compute: the helper's own time per frame; add it to onset for the live delay
All times are in ms on the camera's frame clock: 0 is the same frame as you, and one frame
is the resolution (33 ms at 30 fps).
"""
from __future__ import annotations

import argparse
import json
import math
import os
import sys
import time

import numpy as np

import blink_data as bd

METHODS = ("ear", "blendshapes", "vision", "cnn")
TEACHERS = ("ear", "blendshapes", "vision")

# BlinkTracker.cs (inspector defaults and constants).
CLOSE_THRESHOLD = 0.5
OPEN_THRESHOLD = 0.3
EXPECTED_BLINK_SECONDS = 0.3
FALL_LERP = 0.6
OPEN_SKIP, SHUT_SKIP, MIN_GAP, MIN_SAMPLES = 0.5, 1.0, 0.12, 10
# PredictedReopenIn is for blinks; a closure longer than this is on purpose (OnEyesClosedFor).
LONG_CLOSURE_S = 0.5


# ---- the game's tracker -------------------------------------------------------------------------


def game_median(values) -> float:
    """BlinkTracker.Median: the upper middle of the sorted values."""
    v = np.sort(np.asarray(values, np.float64))
    return float(v[len(v) // 2])


def calibrate(reading, t, phase):
    """BlinkTracker.Fit on the session's own calibration segment. Returns (open, shut, note);
    if the fit fails the game keeps its defaults, 0 and 1, and so does this."""
    def samples(step, skip):
        idx = np.nonzero(phase == step)[0]
        if not len(idx):
            return idx
        return reading[idx[(t[idx] - t[idx[0]]) > skip]]

    opened, shut = samples(bd.PHASE_OPEN, OPEN_SKIP), samples(bd.PHASE_SHUT, SHUT_SKIP)
    if len(opened) < MIN_SAMPLES or len(shut) < MIN_SAMPLES:
        return 0.0, 1.0, "no calibration segment: uncalibrated"
    o, s = game_median(opened), game_median(shut)
    if s - o < MIN_GAP:
        return 0.0, 1.0, f"calibration failed (open {o:.2f}, shut {s:.2f}): uncalibrated"
    return o, s, f"open {o:.2f}, shut {s:.2f}"


class Track:
    def __init__(self, closed, blinks):
        self.closed = closed      # bool [n]: EyesClosed after each frame
        self.blinks = blinks      # [(on, off, predicted_reopen_t)]; off = n if still shut at the end


def track(reading, t, open_level=0.0, closed_level=1.0) -> Track:
    """BlinkTracker.Update/Step, one camera frame at a time: normalise by the calibration,
    smooth on the way down only, hysteresis, and learn the blink length for PredictedReopenIn."""
    n = len(reading)
    closed = np.zeros(n, bool)
    blinks = []
    span = closed_level - open_level
    c01, shut, started, on, predicted = 0.0, False, 0.0, 0, 0.0
    expected = EXPECTED_BLINK_SECONDS
    for i in range(n):
        v = min(1.0, max(0.0, (float(reading[i]) - open_level) / span)) if span != 0 else 0.0
        c01 = v if v > c01 else c01 + (v - c01) * FALL_LERP
        if not shut and c01 >= CLOSE_THRESHOLD:
            shut, started, on = True, t[i], i
            predicted = started + expected          # PredictedReopenIn, as an absolute time
        elif shut and c01 <= OPEN_THRESHOLD:
            shut = False
            length = t[i] - started
            if 0.08 < length < 0.8:
                expected += (length - expected) * 0.1
            blinks.append((on, i, predicted))
        closed[i] = shut
    if shut:
        blinks.append((on, n, predicted))
    return Track(closed, blinks)


# ---- metrics -------------------------------------------------------------------------------------


def average_precision(score, truth) -> float:
    """Area under the precision-recall curve (the step-wise sum sklearn uses)."""
    score = np.asarray(score, np.float64)
    truth = np.asarray(truth, bool)
    if not truth.any():
        return float("nan")
    order = np.argsort(-score, kind="mergesort")
    s, y = score[order], truth[order]
    tp, fp = np.cumsum(y), np.cumsum(~y)
    last = np.r_[np.nonzero(np.diff(s))[0], len(s) - 1]
    tp, fp = tp[last], fp[last]
    precision, recall = tp / (tp + fp), tp / truth.sum()
    return float(np.sum(np.diff(np.r_[0.0, recall]) * precision))


def match(human, detected, slack: int = 0):
    """Pair detected blinks with your blinks by overlap, one to one, in time order. A detection
    that spans two of your blinks takes only the first; a second detection of one blink stays
    unmatched. Returns ([(human_index, detection_index)], [unmatched detection indices])."""
    pairs, unmatched, taken = [], [], set()
    h = 0
    for j, (da, db) in enumerate(detected):
        while h < len(human) and human[h][1] + slack <= da:
            h += 1
        hit = None
        k = h
        while k < len(human) and human[k][0] - slack < db:
            if k not in taken:
                hit = k
                break
            k += 1
        if hit is None:
            unmatched.append(j)
        else:
            taken.add(hit)
            pairs.append((hit, j))
    return pairs, unmatched


def wilson(k: int, n: int, z: float = 1.96):
    if n == 0:
        return float("nan"), float("nan")
    p = k / n
    d = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / d
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return centre - half, centre + half


def score_session(session: bd.Session, reading, calibration: bool = True, slack_ms: float = 0.0) -> dict:
    """One method on one labelled session: the raw counts and per-blink times, for pooling."""
    labels = session.labels
    t, n = session.t, session.n
    reading = np.nan_to_num(np.asarray(reading, np.float64), nan=0.0)   # no face: the helper sends 0
    open_level, shut_level, note = calibrate(reading, t, session.phase) if calibration else (0.0, 1.0, "uncalibrated")
    tracked = track(reading, t, open_level, shut_level)

    scored = (session.phase == bd.PHASE_FREE) & labels.reviewed & ~labels.ignore
    truth, said = labels.closed, tracked.closed
    frames = {
        "tp": int((said & truth & scored).sum()), "fp": int((said & ~truth & scored).sum()),
        "fn": int((~said & truth & scored).sum()), "tn": int((~said & ~truth & scored).sum()),
    }
    span = shut_level - open_level
    normalised = (reading - open_level) / span      # unclamped, so sessions pool on one scale

    human = bd.runs(truth)
    counted = [scored[a] and labels.reviewed[a:b].all() and not labels.ignore[a:b].any() for a, b in human]
    period = float(np.median(np.diff(t))) if n > 1 else 1 / 30
    pairs, unmatched = match(human, [(a, b) for a, b, _ in tracked.blinks], int(round(slack_ms / 1000 / period)))
    false = [j for j in unmatched if scored[tracked.blinks[j][0]]]

    onset, reopen_seen, reopen_err, window = [], [], [], []
    for hi, dj in pairs:
        if not counted[hi]:
            continue
        (ha, hb), (da, db, predicted) = human[hi], tracked.blinks[dj]
        onset.append((t[da] - t[ha]) * 1000)
        if hb < n:
            if db < n:
                reopen_seen.append((t[db] - t[hb]) * 1000)
            if t[hb] - t[ha] < LONG_CLOSURE_S:
                reopen_err.append((predicted - t[hb]) * 1000)
                window.append((t[hb] - t[da]) * 1000)
    long = sum(1 for (a, b), c in zip(human, counted) if c and t[min(b, n - 1)] - t[a] >= LONG_CLOSURE_S)
    return {
        "session": session.id, "calibration": note, "frames": frames,
        "scores": normalised[scored].tolist(), "truth": truth[scored].tolist(),
        "human_blinks": int(sum(counted)), "long_closures": long, "seen": len(onset), "false": len(false),
        "minutes": float(scored.sum() * period / 60.0),
        "onset_ms": onset, "reopen_seen_ms": reopen_seen, "reopen_error_ms": reopen_err, "window_ms": window,
    }


def summarise(parts: list, compute_ms) -> dict:
    """Pool one method's per-session results (sums of counts, all blinks together)."""
    f = {k: sum(p["frames"][k] for p in parts) for k in ("tp", "fp", "fn", "tn")}
    precision = f["tp"] / max(1, f["tp"] + f["fp"])
    recall = f["tp"] / max(1, f["tp"] + f["fn"])
    blinks = sum(p["human_blinks"] for p in parts)
    long = sum(p["long_closures"] for p in parts)
    seen = sum(p["seen"] for p in parts)
    false = sum(p["false"] for p in parts)
    minutes = sum(p["minutes"] for p in parts)
    scores = np.concatenate([np.asarray(p["scores"], np.float64) for p in parts]) if parts else np.zeros(0)
    truth = np.concatenate([np.asarray(p["truth"], bool) for p in parts]) if parts else np.zeros(0, bool)

    def dist(key):
        v = np.asarray([x for p in parts for x in p[key]], np.float64)
        if not len(v):
            return {"n": 0}
        return {"n": int(len(v)), "median": float(np.median(v)), "p90": float(np.percentile(v, 90)),
                "mean": float(v.mean()), "mae": float(np.abs(v).mean()), "late": float((v > 0).mean())}

    lo, hi = wilson(seen, blinks)
    return {
        "frames": dict(f, precision=precision, recall=recall,
                       f1=2 * precision * recall / max(1e-9, precision + recall),
                       ap=average_precision(scores, truth)),
        "blinks": {"human": blinks, "long_closures": long, "seen": seen, "recall": seen / blinks if blinks else float("nan"),
                   "recall_ci95": [lo, hi], "false": false, "minutes": minutes,
                   "false_per_min": false / minutes if minutes else float("nan")},
        "timing": {"onset_ms": dist("onset_ms"), "reopen_seen_ms": dist("reopen_seen_ms"),
                   "reopen_error_ms": dist("reopen_error_ms"), "window_ms": dist("window_ms"),
                   "compute_ms": compute_ms},
        "calibration": {p["session"]: p["calibration"] for p in parts},
    }


# ---- the readings ---------------------------------------------------------------------------------


def closed_probability(out):
    """The model's output to P(shut): one logit, or two-class logits (as blink_server.OnnxEyes)."""
    out = np.asarray(out, np.float64).reshape(len(out), -1)
    if out.shape[1] >= 2:
        e = np.exp(out - out.max(axis=1, keepdims=True))
        return (e / e.sum(axis=1, keepdims=True))[:, 1]
    return 1.0 / (1.0 + np.exp(-out[:, 0]))


class OnnxModel:
    """The exported model, run the way blink_server.py --method cnn runs it."""

    def __init__(self, path: str):
        try:
            import onnxruntime as ort
        except ImportError:
            sys.exit("onnxruntime isn't installed: pip install -r requirements.txt")
        self.session = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
        self.input = self.session.get_inputs()[0].name

    def __call__(self, batch):
        return closed_probability(self.session.run(None, {self.input: np.asarray(batch, np.float32)})[0])


def cnn_reading(session: bd.Session, predict, batch: int = 2048):
    """Both eye crops of every frame through the model, averaged, as the live helper does.
    Frames with no crop read NaN (no face)."""
    ok = session.eye_ok
    crops = session.x[ok].astype(np.float32)[:, None] / 255.0
    probs = np.concatenate([predict(crops[i:i + batch]) for i in range(0, len(crops), batch)]) if len(crops) else np.zeros(0)
    per_eye = np.full(ok.shape, np.nan)
    per_eye[ok] = probs
    with np.errstate(invalid="ignore"):
        counts = ok.sum(axis=1)
        reading = np.where(counts > 0, np.nansum(per_eye, axis=1) / np.maximum(1, counts), np.nan)
    return reading.astype(np.float32)


def cnn_compute_ms(session: bd.Session, predict, frames: int = 200) -> float:
    """The model's own time per frame, one frame (two crops) at a time as in the live helper."""
    idx = np.nonzero(session.eye_ok.all(axis=1))[0][:frames]
    if not len(idx):
        return float("nan")
    times = []
    for i in idx:
        crops = session.x[i].astype(np.float32)[:, None] / 255.0
        start = time.perf_counter()
        predict(crops)
        times.append((time.perf_counter() - start) * 1000)
    return float(np.median(times))


def recorded_compute_ms(sessions, method: str) -> float:
    """Median time per frame the recorder measured. ear (and the CNN's crops) need the face
    mesh; blendshapes and vision run their own models on the whole frame."""
    key = {"ear": "mesh", "cnn": "mesh"}.get(method, method)
    values = [s.ms[key][np.isfinite(s.ms[key])] for s in sessions if key in s.ms]
    values = [v for v in values if len(v)]
    return float(np.median(np.concatenate(values))) if values else float("nan")


# ---- choosing what to compare ------------------------------------------------------------------


def model_card(onnx_path: str) -> dict:
    """The training record train_eye_cnn.py writes next to the model (which sessions it saw)."""
    path = onnx_path + ".json"
    if not os.path.exists(path):
        return {}
    with open(path) as f:
        return json.load(f)


def choose(sessions, methods=None, have_cnn: bool = False, cnn_seen=frozenset()):
    """Which methods and which sessions, so that every method is scored on the same frames.
    Returns (methods, kept sessions, {session id: why it was left out}, [notes])."""
    notes, dropped = [], {}
    labelled = []
    for s in sessions:
        if s.labelled:
            labelled.append(s)
        else:
            dropped[s.id] = "not labelled yet (label_blinks.py)"
    if methods is None:
        methods = []
        for m in TEACHERS:
            have = sum(s.has(m) for s in labelled)
            if labelled and have == len(labelled):
                methods.append(m)
            elif have:
                notes.append(f"{m} was recorded in only {have} of {len(labelled)} labelled sessions; left out so "
                             f"every method is scored on the same frames (--methods {m},... to score it there)")
        if have_cnn:
            methods.append("cnn")
    for m in methods:
        if m not in METHODS:
            raise SystemExit(f"unknown method {m!r}; choose from {', '.join(METHODS)}")
    if "cnn" in methods and not have_cnn:
        raise SystemExit("--methods cnn needs --onnx (your model from train_eye_cnn.py)")

    kept = []
    for s in labelled:
        missing = []
        for m in methods:
            if m == "cnn":
                if s.id in cnn_seen:
                    missing.append("the CNN was trained or selected on it")
            elif not s.has(m):
                missing.append(f"no {m} reading recorded")
        if missing:
            dropped[s.id] = "; ".join(missing)
        else:
            kept.append(s)
    return methods, kept, dropped, notes


def evaluate(sessions, methods, cnn_predict=None, calibration: bool = True, slack_ms: float = 0.0) -> dict:
    """Score each method on each session; pooled results plus the per-session parts."""
    result = {"synthetic": any(s.synthetic for s in sessions), "sessions": [s.id for s in sessions],
              "people": sorted({s.person for s in sessions}),
              "fps": float(np.median([s.fps for s in sessions])) if sessions else float("nan"),
              "methods": {}, "per_session": {}}
    cnn_ms = cnn_compute_ms(sessions[0], cnn_predict) if cnn_predict and sessions else float("nan")
    for m in methods:
        parts = []
        for s in sessions:
            reading = cnn_reading(s, cnn_predict) if m == "cnn" else s.signals[m]
            parts.append(score_session(s, reading, calibration, slack_ms))
        compute = recorded_compute_ms(sessions, m) + (cnn_ms if m == "cnn" else 0.0)
        result["methods"][m] = summarise(parts, compute)
        for p in parts:
            result["per_session"].setdefault(p["session"], {})[m] = summarise([p], None)
    return result


# ---- report --------------------------------------------------------------------------------------


def _table(header, rows) -> str:
    widths = [max(len(str(r[i])) for r in [header] + rows) for i in range(len(header))]
    line = lambda r: "  ".join(str(c).ljust(w) if i == 0 else str(c).rjust(w) for i, (c, w) in enumerate(zip(r, widths)))
    return "\n".join([line(header), line(["-" * w for w in widths])] + [line(r) for r in rows])


def _f(v, fmt="{:.2f}"):
    return "-" if v is None or (isinstance(v, float) and math.isnan(v)) else fmt.format(v)


def _ms(d, key="median"):
    return "-" if not d or not d.get("n") else f"{d[key]:+.0f}"


def format_report(result: dict, title: str = "") -> str:
    out = []
    if result["synthetic"]:
        out.append("SYNTHETIC DATA: this checks that the pipeline runs. It measures nothing about real eyes.\n")
    frame_ms = 1000 / result["fps"] if result["fps"] else float("nan")
    any_method = next(iter(result["methods"].values()), None)
    blinks = any_method["blinks"] if any_method else {"human": 0, "minutes": 0}
    out.append(f"{title}{len(result['sessions'])} session(s) from {len(result['people'])} person(s): "
               f"{blinks['minutes']:.1f} scored minutes, {blinks['human']} of your blinks "
               f"({blinks['long_closures']} of them closures over {LONG_CLOSURE_S:g} s), "
               f"{result['fps']:.0f} fps (1 frame = {frame_ms:.0f} ms)")
    rows = []
    for m, r in result["methods"].items():
        f, b = r["frames"], r["blinks"]
        lo, hi = b["recall_ci95"]
        rows.append([m, _f(f["precision"]), _f(f["recall"]), _f(f["f1"]), _f(f["ap"]),
                     f"{_f(b['recall'])} ({_f(lo)}-{_f(hi)})", f"{b['seen']}/{b['human']}",
                     _f(b["false_per_min"], "{:.1f}")])
    out.append("")
    out.append(_table(["method", "frame P", "R", "F1", "AP", "blink recall (95%)", "seen", "false/min"], rows))
    rows = []
    for m, r in result["methods"].items():
        tm = r["timing"]
        on, rs, re_, w = tm["onset_ms"], tm["reopen_seen_ms"], tm["reopen_error_ms"], tm["window_ms"]
        rows.append([m, f"{_ms(on)} / {_ms(on, 'p90')}", f"{_ms(rs)} / {_ms(rs, 'p90')}",
                     "-" if not re_.get("n") else f"{re_['median']:+.0f} / {re_['mae']:.0f} / {100 * re_['late']:.0f}%",
                     "-" if not w.get("n") else f"{w['median']:.0f}", _f(tm["compute_ms"], "{:.1f}")])
    out.append("")
    out.append(_table(["ms", "onset med/p90", "reopen seen med/p90", "reopen predicted med/MAE/late",
                       "window left", "compute"], rows))
    cal = {}
    for m, r in result["methods"].items():
        for sid, note in r["calibration"].items():
            cal.setdefault(sid, []).append(f"{m} {note}")
    if cal:
        out.append("\ncalibration (each session's own F9-style segment):")
        out.extend(f"  {sid}: " + "; ".join(v) for sid, v in cal.items())
    return "\n".join(out)


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default="eye_dataset")
    ap.add_argument("--session", action="append", help="only this session (repeatable)")
    ap.add_argument("--methods", help=f"comma-separated, from {', '.join(METHODS)} (default: all that every labelled session has)")
    ap.add_argument("--onnx", help="your model from train_eye_cnn.py, scored as 'cnn'")
    ap.add_argument("--no-calibration", action="store_true", help="score the raw readings, as before F9")
    ap.add_argument("--slack-ms", type=float, default=0.0, help="let a detection this close to one of your blinks count as it")
    ap.add_argument("--per-session", action="store_true", help="a table for each session too")
    ap.add_argument("--json", help="write every number to this file")
    args = ap.parse_args(argv)

    sessions = bd.load_sessions(args.data, args.session)
    cnn_predict, seen = None, set()
    if args.onnx:
        cnn_predict = OnnxModel(args.onnx)
        card = model_card(args.onnx)
        if card:
            seen = set(card["splits"]["train"]) | set(card["splits"]["val"])
        else:
            print(f"warning: no {args.onnx}.json, so there's no record of which sessions this model was "
                  "trained on. Scores on those sessions would be meaningless.\n")
    methods = args.methods.split(",") if args.methods else None
    methods, kept, dropped, notes = choose(sessions, methods, cnn_predict is not None, seen)
    for sid, why in dropped.items():
        print(f"left out {sid}: {why}")
    for note in notes:
        print(f"note: {note}")
    if not kept or not methods:
        raise SystemExit("\nnothing to score: label a session with label_blinks.py, and check --methods")
    if dropped or notes:
        print()

    result = evaluate(kept, methods, cnn_predict, not args.no_calibration, args.slack_ms)
    result["left_out"] = dropped
    print(format_report(result))
    if args.per_session:
        for sid, per in result["per_session"].items():
            s = next(x for x in kept if x.id == sid)
            sub = dict(result, sessions=[sid], people=[s.person], fps=s.fps, methods=per)
            print("\n" + format_report(dict(sub, synthetic=False), title=f"[{sid}] "))
    if args.json:
        with open(args.json, "w") as f:
            json.dump(result, f, indent=1)
        print(f"\nwrote {args.json}")


if __name__ == "__main__":
    main()
