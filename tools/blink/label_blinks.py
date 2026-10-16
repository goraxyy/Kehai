#!/usr/bin/env python3
"""Label your own blinks frame by frame, for a test set no helper had a hand in.

    python label_blinks.py --data eye_dataset                        # the next session not yet done
    python label_blinks.py --data eye_dataset --session 20261015-141200

It shows the session's saved eye crops (both eyes for each frame; nothing else was kept) a
page at a time, two seconds to a page. Click a frame to mark it closed, drag to mark a run.
Closed means the eye is more than half shut: the pupil mostly covered. Right-click marks a
frame you can't judge (blurred, turned away, no face); those are left out of scoring. No
helper's reading is shown, so none of them can sway you. A yellow corner means SPACE marking
was on while recording: a closure you made on purpose is somewhere near.

  click / drag       closed <-> open          right-click / drag   can't tell <-> fine
  shift-click        the same, for every frame since the last click (a long closure)
  SPACE or d         next page (counts this one as reviewed)
  a                  previous page            g    first page not yet reviewed
  s                  save                     q or Esc   save and quit

Labels are saved beside the recording as <session>.labels.json, on every page turn; the
recording itself is never changed. Only reviewed pages are scored, so you can stop any time
and come back.
"""
from __future__ import annotations

import argparse
import sys

import numpy as np

import blink_data as bd

COLS, ROWS = 10, 6
CELL_W, CELL_H, BAR, PAD, TEXT = 120, 60, 10, 6, 14
HEADER = 64
CLOSED, OPEN, IGNORE, SPACE_HINT = (40, 40, 230), (70, 70, 70), (170, 70, 170), (0, 220, 255)


class Labeler:
    """The labelling state and its drawing, without the window (so it can be tested)."""

    def __init__(self, session: bd.Session, labels: bd.Labels | None = None, path: str = ""):
        self.s = session
        self.labels = labels or bd.Labels.empty(session.n)
        self.path = path or bd.labels_path(session.path)
        self.per_page = COLS * ROWS
        self.pages = max(1, -(-session.n // self.per_page))
        self.page = self.first_unreviewed()
        self.painting = None          # ("closed" | "ignore", value) while a mouse button is down
        self.last = None              # the last frame clicked, for shift-click
        self.dirty = False

    # ---- navigation ------------------------------------------------------------------------

    def frames(self):
        a = self.page * self.per_page
        return a, min(self.s.n, a + self.per_page)

    def first_unreviewed(self) -> int:
        todo = np.nonzero(~self.labels.reviewed)[0]
        return int(todo[0] // self.per_page) if len(todo) else 0

    def turn(self, step: int) -> None:
        if step > 0:
            a, b = self.frames()
            self.labels.reviewed[a:b] = True
            self.dirty = True
        self.page = min(self.pages - 1, max(0, self.page + step))
        self.save()

    def key(self, k: int) -> bool:
        """One key press; False means quit."""
        if k in (ord(" "), ord("d")):
            self.turn(+1)
        elif k == ord("a"):
            self.turn(-1)
        elif k == ord("g"):
            self.page = self.first_unreviewed()
        elif k == ord("s"):
            self.save(force=True)
        elif k in (ord("q"), 27):
            self.save()
            return False
        return True

    # ---- marking ---------------------------------------------------------------------------

    def cell_at(self, x: int, y: int):
        if y < HEADER:
            return None
        col, row = (x - PAD) // (CELL_W + PAD), (y - HEADER) // (CELL_H + BAR + TEXT + PAD)
        if not (0 <= col < COLS and 0 <= row < ROWS):
            return None
        i = self.page * self.per_page + row * COLS + col
        return i if i < self.s.n else None

    def press(self, i, which: str, extend: bool = False) -> None:
        if i is None:
            return
        mask = self.labels.closed if which == "closed" else self.labels.ignore
        if extend and self.last is not None:
            a, b = sorted((self.last, i))
            mask[a:b + 1] = mask[self.last]
            self.dirty = True
            self.last = i
            return
        self.painting = (which, not mask[i])
        self.last = i
        self.drag(i)

    def drag(self, i) -> None:
        if i is None or self.painting is None:
            return
        which, value = self.painting
        (self.labels.closed if which == "closed" else self.labels.ignore)[i] = value
        self.dirty = True

    def release(self) -> None:
        self.painting = None

    def save(self, force: bool = False) -> None:
        if self.dirty or force:
            bd.save_labels(self.path, self.s.id, self.labels)
            self.dirty = False

    # ---- drawing ---------------------------------------------------------------------------

    def render(self):
        import cv2

        lab, s = self.labels, self.s
        height = HEADER + ROWS * (CELL_H + BAR + TEXT + PAD)
        img = np.full((height, PAD + COLS * (CELL_W + PAD), 3), 24, np.uint8)
        a, b = self.frames()
        t0 = s.t[0]
        phase = bd.PHASE_NAMES.get(int(s.phase[a]), "")
        done = 100 * lab.reviewed.mean()
        state = "reviewed" if lab.reviewed[a:b].all() else "not reviewed yet"
        lines = [
            f"{s.id}   page {self.page + 1}/{self.pages}   {s.t[a] - t0:5.1f}-{s.t[b - 1] - t0:5.1f} s   {phase}   ({state})",
            f"{done:.0f}% reviewed   {len(bd.runs(lab.closed))} closed runs   closed = more than half shut",
            "click/drag closed   right-click can't tell   SPACE next   a back   g first to do   q save+quit",
        ]
        for k, line in enumerate(lines):
            cv2.putText(img, line, (PAD, 18 + 19 * k), cv2.FONT_HERSHEY_SIMPLEX, 0.45 if k < 2 else 0.4,
                        (235, 235, 235) if k < 2 else (150, 150, 150), 1, cv2.LINE_AA)
        for i in range(a, b):
            k = i - a
            x0 = PAD + (k % COLS) * (CELL_W + PAD)
            y0 = HEADER + (k // COLS) * (CELL_H + BAR + TEXT + PAD)
            if s.eye_ok[i].any():
                pair = np.hstack([s.x[i, 0], s.x[i, 1]])
                cell = cv2.resize(pair, (CELL_W, CELL_H), interpolation=cv2.INTER_CUBIC)
                img[y0:y0 + CELL_H, x0:x0 + CELL_W] = cell[:, :, None]
            else:
                img[y0:y0 + CELL_H, x0:x0 + CELL_W] = 50
                cv2.putText(img, "no face", (x0 + 30, y0 + 34), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (160, 160, 160), 1, cv2.LINE_AA)
            if s.space[i]:
                cv2.fillPoly(img, [np.array([(x0 + CELL_W - 14, y0), (x0 + CELL_W, y0), (x0 + CELL_W, y0 + 14)])], SPACE_HINT)
            colour = IGNORE if lab.ignore[i] else CLOSED if lab.closed[i] else OPEN
            img[y0 + CELL_H:y0 + CELL_H + BAR, x0:x0 + CELL_W] = colour
            if lab.ignore[i]:
                cv2.putText(img, "?", (x0 + CELL_W // 2 - 4, y0 + CELL_H + BAR - 1), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (255, 255, 255), 1)
            cv2.putText(img, f"{i}  +{1000 * (s.t[i] - s.t[a]):.0f}ms", (x0, y0 + CELL_H + BAR + TEXT - 3),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.33, (140, 140, 140), 1, cv2.LINE_AA)
        return img


def pick_session(folder: str, wanted: str | None) -> bd.Session:
    sessions = bd.load_sessions(folder, [wanted] if wanted else None)
    if wanted:
        return sessions[0]
    for s in sessions:
        if s.labels is None or not s.labels.reviewed.all():
            return s
    raise SystemExit("every session is fully labelled; pick one with --session to look again")


def main(argv=None) -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--data", default="eye_dataset")
    ap.add_argument("--session", help="which session (default: the first not fully labelled)")
    ap.add_argument("--labeller", default="me", help="who is labelling (a pseudonym)")
    args = ap.parse_args(argv)
    try:
        import cv2
    except ImportError:
        sys.exit("OpenCV isn't installed: pip install -r requirements.txt")

    session = pick_session(args.data, args.session)
    labeler = Labeler(session, session.labels)
    labeler.labels.labeller = bd.check_id(args.labeller, "labeller")
    print(f"Labelling {session.id}: {session.n} frames, {session.minutes:.1f} min, {labeler.pages} pages. "
          f"Saving to {labeler.path}")

    window = f"Label blinks: {session.id}"
    cv2.namedWindow(window)

    def mouse(event, x, y, flags, _):
        i = labeler.cell_at(x, y)
        shift = bool(flags & cv2.EVENT_FLAG_SHIFTKEY)
        if event == cv2.EVENT_LBUTTONDOWN:
            labeler.press(i, "closed", shift)
        elif event == cv2.EVENT_RBUTTONDOWN:
            labeler.press(i, "ignore", shift)
        elif event == cv2.EVENT_MOUSEMOVE and flags & (cv2.EVENT_FLAG_LBUTTON | cv2.EVENT_FLAG_RBUTTON):
            labeler.drag(i)
        elif event in (cv2.EVENT_LBUTTONUP, cv2.EVENT_RBUTTONUP):
            labeler.release()

    cv2.setMouseCallback(window, mouse)
    try:
        while True:
            cv2.imshow(window, labeler.render())
            k = cv2.waitKey(30) & 0xFF
            if k != 255 and not labeler.key(k):
                break
            if cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1:
                break
    finally:
        labeler.save(force=True)
        cv2.destroyAllWindows()
    lab = labeler.labels
    print(f"Saved: {100 * lab.reviewed.mean():.0f}% reviewed, {len(bd.runs(lab.closed))} closed runs. "
          f"Score it: python eval_blink.py --data {args.data}")


if __name__ == "__main__":
    main()
