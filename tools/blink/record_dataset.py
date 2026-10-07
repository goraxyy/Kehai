#!/usr/bin/env python3
"""Record a small eye dataset for training your own blink model (IDEAS.md "Own training path").

Distillation: MediaPipe labels every frame for you (eye aspect ratio, or the FaceLandmarker
blink blendshapes if you have the model), so you don't hand-label thousands of crops. Hold
SPACE while your eyes are shut to add human labels on top; those override the teacher.

What is kept: 24x24 grayscale crops of each eye and a number saying how shut it was — no
full frames, no face, no audio. It is written only to the folder you choose, on this machine,
and nothing is sent anywhere. Delete the folder to delete the data.

    python record_dataset.py --out eye_dataset --minutes 3
"""
from __future__ import annotations

import argparse
import os
import sys
import time

import blink_server as bs

CONSENT = """
This will turn on your webcam and save small crops of your eyes (24x24 pixels, grayscale)
with a label for how shut they were, to:

    {out}

Nothing else is saved and nothing leaves this computer. You can stop at any time with Esc,
and delete the folder afterwards to remove everything.

Type 'yes' to start: """


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default="eye_dataset")
    ap.add_argument("--minutes", type=float, default=3.0)
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--teacher", choices=("ear", "blendshapes"), default="ear")
    ap.add_argument("--model", default="face_landmarker.task", help="for --teacher blendshapes")
    args = ap.parse_args(argv)

    out = os.path.abspath(args.out)
    if input(CONSENT.format(out=out)).strip().lower() != "yes":
        print("Not recording.")
        return

    import cv2
    import numpy as np

    os.makedirs(out, exist_ok=True)
    mesh = bs.FaceMesh()
    shapes = bs.Blendshapes(args.model) if args.teacher == "blendshapes" else None
    scale = bs.EarScale()
    cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        sys.exit(f"couldn't open camera {args.camera}")

    crops, labels, human = [], [], []
    until = time.time() + args.minutes * 60
    print("Recording. Look at the screen, blink naturally, and now and then hold your eyes shut "
          "with SPACE held down. Esc stops.")
    try:
        while time.time() < until:
            ok, frame = cap.read()
            if not ok:
                continue
            h, w = frame.shape[:2]
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            pts = mesh.landmarks(rgb)

            shown = frame.copy()
            key = cv2.waitKey(1) & 0xFF
            if key == 27:
                break
            if pts is not None:
                if shapes is not None:
                    teacher = shapes.closed(rgb)
                    teacher = 0.0 if teacher is None else teacher
                else:
                    ear = (bs.eye_aspect_ratio(pts, bs.LEFT_EYE) + bs.eye_aspect_ratio(pts, bs.RIGHT_EYE)) / 2
                    teacher = max(0.0, min(1.0, scale.closed(ear)))
                held = key == 32
                label = 1.0 if held else teacher
                for idx in (bs.EYE_BOX_LEFT, bs.EYE_BOX_RIGHT):
                    c = bs.eye_crop(gray, pts, idx, w, h)
                    if c is not None:
                        crops.append((c * 255).astype(np.uint8))
                        labels.append(label)
                        human.append(held)
                cv2.putText(shown, f"{len(crops)} crops  label={label:.2f}{'  (SPACE)' if held else ''}",
                            (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            cv2.imshow("Recording eye crops (Esc to stop)", shown)
    finally:
        cap.release()
        cv2.destroyAllWindows()

    if not crops:
        print("No face found; nothing saved.")
        return
    path = os.path.join(out, time.strftime("eyes_%Y%m%d_%H%M%S.npz"))
    np.savez_compressed(path, x=np.stack(crops), y=np.array(labels, np.float32), human=np.array(human, bool))
    print(f"Saved {len(crops)} crops to {path}. Train with: python train_eye_cnn.py --data {out}")


if __name__ == "__main__":
    main()
