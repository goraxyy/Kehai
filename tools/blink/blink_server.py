#!/usr/bin/env python3
"""Blink sidecar for Kehai (IDEAS.md "Blink": webcam path).

Owns the camera, decides how shut your eyes are, and sends one small JSON packet per frame
to the game over UDP on 127.0.0.1 — nothing else. It never records, never saves a frame,
and never talks to any other host.

    python blink_server.py --synthetic                 # no camera: a human-shaped blink trace
    python blink_server.py --method ear                # MediaPipe face mesh + eye aspect ratio
    python blink_server.py --method blendshapes --model face_landmarker.task
    python blink_server.py --method cnn --onnx eye_cnn.onnx   # your own model (train_eye_cnn.py)

Packet (one per frame, UTF-8 JSON), read by UdpBlinkSource.cs:

    {"seq": 1234, "closed": 0.93, "conf": 0.88, "src": "ear",
     "capture": 1727000000.1234, "sent": 1727000000.1391, "fps": 30.0}

`closed` is 0 = open .. 1 = shut, before the game's own calibration (F9 in game).
`capture`/`sent` are this machine's wall clock; the game reads the gap as pipeline latency
and uses it to date each blink's true start.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import socket
import sys
import time
from collections import deque

LEFT_EYE = (33, 160, 158, 133, 153, 144)    # p1..p6 in the usual EAR notation
RIGHT_EYE = (362, 385, 387, 263, 373, 380)
EYE_BOX_LEFT = (33, 133, 159, 145, 160, 144, 158, 153)
EYE_BOX_RIGHT = (362, 263, 386, 374, 385, 380, 387, 373)
CROP = 24                                   # the CNN's input: 24x24 grayscale, top-down rows


class Sender:
    """UDP to the game. Loopback only, by construction."""

    def __init__(self, port: int, source: str):
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.addr = ("127.0.0.1", port)
        self.source = source
        self.seq = 0
        self.fps = 0.0
        self._last = None

    def send(self, closed: float, conf: float, capture: float) -> dict:
        now = time.time()
        if self._last is not None:
            dt = now - self._last
            if dt > 0:
                self.fps = 0.9 * self.fps + 0.1 * (1.0 / dt) if self.fps else 1.0 / dt
        self._last = now
        self.seq += 1
        packet = {
            "seq": self.seq,
            "closed": round(max(0.0, min(1.0, closed)), 4),
            "conf": round(max(0.0, min(1.0, conf)), 3),
            "src": self.source,
            "capture": round(capture, 4),
            "sent": round(time.time(), 4),
            "fps": round(self.fps, 1),
        }
        self.sock.sendto(json.dumps(packet).encode("utf-8"), self.addr)
        return packet


# ---- synthetic -------------------------------------------------------------------------------


class SyntheticBlinks:
    """Blinks at `rate` per minute with the asymmetric human shape: ~1/3 closing, ~2/3 opening,
    220-380 ms in all. The same generator as ReplayBlinkSource.Synthetic in the game."""

    def __init__(self, rate: float, seed: int | None):
        self.rng = random.Random(seed)
        self.rate = max(1.0, rate)
        self.start = time.time()
        self.next_blink = self._gap()
        self.blink_start = None
        self.length = 0.3

    def _gap(self) -> float:
        return -math.log(1.0 - self.rng.random()) * 60.0 / self.rate

    def closed(self, now: float) -> float:
        t = now - self.start
        if self.blink_start is None and t >= self.next_blink:
            self.blink_start = t
            self.length = 0.22 + self.rng.random() * 0.16
        if self.blink_start is None:
            return 0.0
        local = t - self.blink_start
        if local > self.length:
            self.blink_start = None
            self.next_blink = t + self._gap()
            return 0.0
        close = self.length * 0.33
        return local / close if local < close else 1.0 - (local - close) / (self.length - close)


def run_synthetic(args) -> None:
    sender = Sender(args.port, "synthetic")
    blinks = SyntheticBlinks(args.rate, args.seed)
    period = 1.0 / args.fps
    print(f"synthetic blinks at {args.rate:.0f}/min -> udp 127.0.0.1:{args.port} ({args.fps} fps). Ctrl+C to stop.")
    try:
        while True:
            now = time.time()
            sender.send(blinks.closed(now), 1.0, now)
            time.sleep(max(0.0, period - (time.time() - now)))
    except KeyboardInterrupt:
        pass


# ---- the camera paths ------------------------------------------------------------------------


def eye_aspect_ratio(pts, idx) -> float:
    p = [pts[i] for i in idx]

    def d(a, b):
        return math.hypot(a[0] - b[0], a[1] - b[1])

    horizontal = d(p[0], p[3])
    return (d(p[1], p[5]) + d(p[2], p[4])) / (2.0 * horizontal) if horizontal > 1e-6 else 0.0


class EarScale:
    """Turns raw EAR into 0..1 shut without per-person tuning: the open reference is a high
    percentile of the recent past, so it adapts to your face, lighting and camera."""

    def __init__(self):
        self.history = deque(maxlen=300)

    def closed(self, ear: float) -> float:
        self.history.append(ear)
        ordered = sorted(self.history)
        open_ref = ordered[int(0.9 * (len(ordered) - 1))] if ordered else 0.3
        shut_ref = 0.45 * open_ref
        if open_ref - shut_ref < 1e-4:
            return 0.0
        return (open_ref - ear) / (open_ref - shut_ref)


def eye_crop(gray, pts, idx, w, h):
    """A square crop around one eye, resized to CROP x CROP, values 0..1, rows top-down."""
    import cv2
    import numpy as np

    xs = [pts[i][0] * w for i in idx]
    ys = [pts[i][1] * h for i in idx]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    half = max(max(xs) - min(xs), max(ys) - min(ys)) * 0.75 + 2
    x0, x1 = int(max(0, cx - half)), int(min(w, cx + half))
    y0, y1 = int(max(0, cy - half)), int(min(h, cy + half))
    if x1 - x0 < 4 or y1 - y0 < 4:
        return None
    crop = cv2.resize(gray[y0:y1, x0:x1], (CROP, CROP), interpolation=cv2.INTER_AREA)
    return crop.astype(np.float32) / 255.0


class FaceMesh:
    """MediaPipe face mesh with iris refinement — the landmarks every camera method needs."""

    def __init__(self):
        import mediapipe as mp

        self.mesh = mp.solutions.face_mesh.FaceMesh(
            max_num_faces=1, refine_landmarks=True,
            min_detection_confidence=0.5, min_tracking_confidence=0.5)

    def landmarks(self, rgb):
        result = self.mesh.process(rgb)
        if not result.multi_face_landmarks:
            return None
        return [(p.x, p.y) for p in result.multi_face_landmarks[0].landmark]


class Blendshapes:
    """MediaPipe FaceLandmarker's eyeBlinkLeft/Right blendshapes — the most accurate
    off-the-shelf signal. Needs the face_landmarker.task model file (see README)."""

    def __init__(self, model_path: str):
        import mediapipe as mp
        from mediapipe.tasks.python import BaseOptions, vision

        self.mp = mp
        options = vision.FaceLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=model_path),
            running_mode=vision.RunningMode.VIDEO,
            output_face_blendshapes=True,
            num_faces=1)
        self.landmarker = vision.FaceLandmarker.create_from_options(options)
        self.t0 = time.time()

    def closed(self, rgb):
        image = self.mp.Image(image_format=self.mp.ImageFormat.SRGB, data=rgb)
        result = self.landmarker.detect_for_video(image, int((time.time() - self.t0) * 1000))
        if not result.face_blendshapes:
            return None
        scores = {c.category_name: c.score for c in result.face_blendshapes[0]}
        return (scores.get("eyeBlinkLeft", 0.0) + scores.get("eyeBlinkRight", 0.0)) / 2.0


class OnnxEyes:
    """Your own tiny CNN (train_eye_cnn.py), run on both eye crops."""

    def __init__(self, path: str):
        import numpy as np
        import onnxruntime as ort

        self.np = np
        self.session = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
        self.input = self.session.get_inputs()[0].name

    def closed(self, crops) -> float:
        np = self.np
        batch = np.stack(crops)[:, None, :, :].astype(np.float32)
        out = self.session.run(None, {self.input: batch})[0].reshape(len(crops), -1)
        if out.shape[1] >= 2:
            e = np.exp(out - out.max(axis=1, keepdims=True))
            p = (e / e.sum(axis=1, keepdims=True))[:, 1]
        else:
            p = 1.0 / (1.0 + np.exp(-out[:, 0]))
        return float(p.mean())


def run_camera(args) -> None:
    try:
        import cv2
    except ImportError:
        sys.exit("OpenCV isn't installed: pip install -r requirements.txt (or use --synthetic)")

    mesh = FaceMesh() if args.method in ("ear", "cnn") else None
    shapes = Blendshapes(args.model) if args.method == "blendshapes" else None
    cnn = OnnxEyes(args.onnx) if args.method == "cnn" else None
    scale = EarScale()
    sender = Sender(args.port, args.method)

    cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FPS, args.fps)
    if not cap.isOpened():
        sys.exit(f"couldn't open camera {args.camera}")
    print(f"{args.method} -> udp 127.0.0.1:{args.port}. Frames are processed in memory and discarded. Ctrl+C to stop.")

    try:
        while True:
            ok, frame = cap.read()
            capture = time.time()
            if not ok:
                continue
            h, w = frame.shape[:2]
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            closed, conf = 0.0, 0.0

            if shapes is not None:
                value = shapes.closed(rgb)
                if value is not None:
                    closed, conf = value, 0.95
            else:
                pts = mesh.landmarks(rgb)
                if pts is not None:
                    if cnn is not None:
                        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                        crops = [c for c in (eye_crop(gray, pts, EYE_BOX_LEFT, w, h),
                                             eye_crop(gray, pts, EYE_BOX_RIGHT, w, h)) if c is not None]
                        if crops:
                            closed, conf = cnn.closed(crops), 0.9
                    else:
                        ear = (eye_aspect_ratio(pts, LEFT_EYE) + eye_aspect_ratio(pts, RIGHT_EYE)) / 2.0
                        closed, conf = scale.closed(ear), 0.85

            packet = sender.send(closed, conf, capture)
            if args.preview:
                bar = int(packet["closed"] * 200)
                cv2.rectangle(frame, (10, 10), (10 + bar, 30), (0, 0, 255) if packet["closed"] > 0.6 else (0, 200, 0), -1)
                cv2.putText(frame, f"{args.method} closed={packet['closed']:.2f} conf={packet['conf']:.2f} {packet['fps']:.0f}fps",
                            (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.imshow("Kehai blink sidecar (preview only - nothing is saved)", frame)
                if cv2.waitKey(1) & 0xFF == 27:
                    break
    except KeyboardInterrupt:
        pass
    finally:
        cap.release()
        if args.preview:
            cv2.destroyAllWindows()


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=5066, help="UDP port the game listens on (BlinkTracker.udpPort)")
    ap.add_argument("--fps", type=int, default=60)
    ap.add_argument("--synthetic", action="store_true", help="no camera: send a synthetic blink trace")
    ap.add_argument("--rate", type=float, default=17.0, help="synthetic blinks per minute")
    ap.add_argument("--seed", type=int, default=None, help="synthetic trace seed")
    ap.add_argument("--method", choices=("ear", "blendshapes", "cnn"), default="ear")
    ap.add_argument("--model", default="face_landmarker.task", help="MediaPipe model for --method blendshapes")
    ap.add_argument("--onnx", default="eye_cnn.onnx", help="your model for --method cnn")
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--preview", action="store_true", help="show the camera with the reading (on screen only)")
    args = ap.parse_args(argv)

    if args.synthetic:
        run_synthetic(args)
    else:
        run_camera(args)


if __name__ == "__main__":
    main()
