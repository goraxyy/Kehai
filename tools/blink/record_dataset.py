#!/usr/bin/env python3
"""Record one session of eye crops, for training your own blink model and for measuring how
well every blink helper works (README "Measuring accuracy").

    python record_dataset.py --out eye_dataset --person p1 --minutes 3
    python record_dataset.py --out eye_dataset --person p1 --minutes 3 --vision   # + Apple Vision (Mac)

Each run is one session, tagged with a session id (the start time unless you give one) and
a person id (a pseudonym such as p1, never your name), so the trainer can hold whole
sessions or people out of training. It starts with the game's own calibration (eyes open,
eyes shut until the beep, three blinks), then you just look at the screen and blink as usual.

For every camera frame it keeps the two eye crops, the time, and what each helper would have
told the game: MediaPipe's eye aspect ratio, MediaPipe's blink blendshapes (if
face_landmarker.task is there), and with --vision the Apple Vision helper, which runs
alongside on its own camera stream and is lined up by capture time. These readings are
the teachers. Your own labels are separate: SPACE here only drops a coarse hint, and the
frame-by-frame labels that every score is measured against come from label_blinks.py.

SPACE: before you close your eyes on purpose, press SPACE; keep them shut a second or so,
open them, and press SPACE again. Do it a few times a session. Esc stops early.

What is kept: 24x24 grayscale crops of each eye and the numbers above. No full frames, no
face, no audio. It is written only to the folder you choose, on this machine, and nothing is
sent anywhere. Delete the folder to delete the data.
"""
from __future__ import annotations

import argparse
import json
import os
import socket
import subprocess
import sys
import threading
import time

import numpy as np

import blink_data as bd
import blink_server as bs

HERE = os.path.dirname(os.path.abspath(__file__))
# The game's calibration (BlinkTracker.cs): 3 s open, 3.5 s shut, 6 s for three blinks.
STEPS = ((3.0, bd.PHASE_OPEN, "Keep your eyes open and look at the screen."),
         (3.5, bd.PHASE_SHUT, "Close your eyes and keep them closed until you hear the beep."),
         (6.0, bd.PHASE_BLINKS, "Open your eyes. Now blink 3 times, the way you usually do."))
FREE = "Look at the screen and blink as usual. Now and then: SPACE, eyes shut a second, open, SPACE."

CONSENT = """
This will turn on your webcam and save, for every camera frame, to:

    {out}

  - two small crops of your eyes (24x24 pixels, grayscale)
  - the time, how shut each blink helper read your eyes, and how long it took
  - whether SPACE marking was on

Nothing else is saved (no full frames, no face, no audio) and nothing leaves this computer.
It is tagged session '{session}', person '{person}'. Stop at any time with Esc, and delete
the folder afterwards to remove everything.

Type 'yes' to start: """


class VisionLog:
    """The Apple Vision helper (mac/build/BlinkVision) on its own port while recording. It reads
    the camera itself; its packets are kept and lined up with these frames by capture time."""

    def __init__(self, binary: str, port: int, camera: int):
        self.packets = []
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", port))
        self.sock.settimeout(0.2)
        self.running = True
        self.thread = threading.Thread(target=self._listen, daemon=True)
        self.thread.start()
        self.proc = subprocess.Popen([binary, "--port", str(port), "--camera", str(camera), "--quiet"],
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def _listen(self):
        while self.running:
            try:
                data = self.sock.recv(4096)
            except socket.timeout:
                continue
            except OSError:
                break
            try:
                p = json.loads(data)
                self.packets.append((p["capture"], p["closed"], p["conf"], p["sent"]))
            except (ValueError, KeyError):
                pass

    def stop(self):
        self.proc.terminate()
        try:
            self.proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            self.proc.kill()
        self.running = False
        self.thread.join(timeout=1)
        self.sock.close()

    def align(self, t):
        """Vision's reading at each frame time: the packet captured nearest to it, within half a
        frame. NaN where there is none or Vision saw no face."""
        closed = np.full(len(t), np.nan, np.float32)
        ms = np.full(len(t), np.nan, np.float32)
        if not self.packets or len(t) < 2:
            return closed, ms
        p = np.array(sorted(self.packets))
        cap = p[:, 0]
        tolerance = 0.5 * float(np.median(np.diff(t)))
        right = np.clip(np.searchsorted(cap, t), 1, len(cap) - 1)
        nearest = np.where(np.abs(cap[right - 1] - t) <= np.abs(cap[right] - t), right - 1, right)
        ok = (np.abs(cap[nearest] - t) <= tolerance) & (p[nearest, 2] > 0)
        closed[ok] = p[nearest[ok], 1]
        ms[ok] = (p[nearest[ok], 3] - cap[nearest[ok]]) * 1000
        return closed, ms


def phase_at(elapsed: float, calibrate: bool):
    if calibrate:
        for seconds, phase, text in STEPS:
            if elapsed < seconds:
                return phase, text
            elapsed -= seconds
    return bd.PHASE_FREE, FREE


def beep():
    if sys.stdout.isatty():
        sys.stdout.write("\a")
        sys.stdout.flush()


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default="eye_dataset")
    ap.add_argument("--session", default=time.strftime("%Y%m%d-%H%M%S"), help="session id (default: now)")
    ap.add_argument("--person", default="me", help="who is recorded: a pseudonym such as p1, not a name")
    ap.add_argument("--notes", default="", help="e.g. 'evening, desk lamp, glasses'")
    ap.add_argument("--minutes", type=float, default=3.0)
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--model", default="face_landmarker.task", help="MediaPipe model for the blendshapes reading")
    ap.add_argument("--vision", action="store_true", help="also log the Apple Vision helper (build it with mac/build.sh)")
    ap.add_argument("--vision-port", type=int, default=5067)
    ap.add_argument("--no-calibration", action="store_true", help="skip the calibration at the start")
    args = ap.parse_args(argv)

    bd.check_id(args.session, "--session")
    bd.check_id(args.person, "--person")
    out = os.path.abspath(args.out)
    path = os.path.join(out, args.session + ".npz")
    if os.path.exists(path):
        sys.exit(f"{path} already exists; pick another --session")
    model = args.model if os.path.exists(args.model) else os.path.join(HERE, args.model)
    vision_bin = os.path.join(HERE, "mac", "build", "BlinkVision")
    if args.vision and not os.path.exists(vision_bin):
        sys.exit(f"--vision needs {vision_bin}: build it with tools/blink/mac/build.sh")

    if input(CONSENT.format(out=out, session=args.session, person=args.person)).strip().lower() != "yes":
        print("Not recording.")
        return

    import cv2

    os.makedirs(out, exist_ok=True)
    mesh = bs.FaceMesh()
    shapes = bs.Blendshapes(model) if os.path.exists(model) else None
    scale = bs.EarScale()
    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        sys.exit(f"couldn't open camera {args.camera}")
    vision = VisionLog(vision_bin, args.vision_port, args.camera) if args.vision else None
    print("Helpers recorded: ear" + (", blendshapes" if shapes else " (no face_landmarker.task: no blendshapes)")
          + (", vision" if vision else ""))

    t, x, eye_ok, phases, space = [], [], [], [], []
    sig = {"ear": [], "blendshapes": []}
    ms = {"mesh": [], "blendshapes": []}
    marking, last_phase = False, None
    start = time.time()
    until = start + args.minutes * 60
    nan = float("nan")
    try:
        while time.time() < until:
            ok, frame = cap.read()
            capture = time.time()
            if not ok:
                continue
            phase, text = phase_at(capture - start, not args.no_calibration)
            if last_phase == bd.PHASE_SHUT and phase != bd.PHASE_SHUT:
                beep()
            last_phase = phase
            key = cv2.waitKey(1) & 0xFF
            if key == 27:
                break
            if key == 32:
                marking = not marking

            h, w = frame.shape[:2]
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            began = time.perf_counter()
            pts = mesh.landmarks(rgb)
            ms["mesh"].append((time.perf_counter() - began) * 1000)
            pair, found = np.zeros((2, bd.CROP, bd.CROP), np.uint8), [False, False]
            if pts is not None:
                ear = (bs.eye_aspect_ratio(pts, bs.LEFT_EYE) + bs.eye_aspect_ratio(pts, bs.RIGHT_EYE)) / 2
                sig["ear"].append(max(0.0, min(1.0, scale.closed(ear))))      # what the sidecar sends
                for k, idx in enumerate((bs.EYE_BOX_LEFT, bs.EYE_BOX_RIGHT)):
                    c = bs.eye_crop(gray, pts, idx, w, h)
                    if c is not None:
                        pair[k], found[k] = (c * 255).astype(np.uint8), True
            else:
                sig["ear"].append(nan)
            if shapes is not None:
                began = time.perf_counter()
                value = shapes.closed(rgb)
                ms["blendshapes"].append((time.perf_counter() - began) * 1000)
                sig["blendshapes"].append(nan if value is None else value)
            t.append(capture)
            x.append(pair)
            eye_ok.append(found)
            phases.append(phase)
            space.append(marking)

            shown = frame.copy()
            for k in range(2):                                                # what is saved
                big = cv2.resize(pair[k], (96, 96), interpolation=cv2.INTER_NEAREST)
                shown[10:106, w - 212 + 106 * k:w - 116 + 106 * k] = big[:, :, None]
            cv2.putText(shown, "saved:", (w - 212, 122), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (255, 255, 255), 1)
            cv2.putText(shown, text, (10, h - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)
            left = until - capture
            status = f"{len(t)} frames  {int(left // 60)}:{int(left % 60):02d} left" + ("" if pts is not None else "  (no face)")
            cv2.putText(shown, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            if marking:
                cv2.putText(shown, "MARKING: SPACE again once your eyes are open", (10, 60),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
            cv2.imshow("Recording eye crops (Esc to stop)", shown)
    finally:
        cap.release()
        cv2.destroyAllWindows()
        if vision is not None:
            vision.stop()

    if len(t) < 2 or not any(any(f) for f in eye_ok):
        print("No face found; nothing saved.")
        return
    t = np.array(t)
    signals = {"ear": sig["ear"]}
    timings = {"mesh": ms["mesh"]}
    if shapes is not None:
        signals["blendshapes"], timings["blendshapes"] = sig["blendshapes"], ms["blendshapes"]
    if vision is not None:
        signals["vision"], timings["vision"] = vision.align(t)
        if not np.isfinite(signals["vision"]).any():
            print("Apple Vision sent nothing usable (could it open the camera alongside this one?); "
                  "this session has no vision reading.")
    bd.save_session(path, session=args.session, person=args.person, t=t, x=np.stack(x), eye_ok=eye_ok,
                    phase=phases, space=space, signals=signals, ms=timings, notes=args.notes,
                    created=time.strftime("%Y-%m-%d %H:%M:%S"))
    face = 100 * np.mean([any(f) for f in eye_ok])
    print(f"Saved {len(t)} frames ({(t[-1] - t[0]) / 60:.1f} min at {1 / np.median(np.diff(t)):.0f} fps, "
          f"face found in {face:.0f}%) to {path}")
    print(f"Next, label it: python label_blinks.py --data {args.out} --session {args.session}")


if __name__ == "__main__":
    main()
