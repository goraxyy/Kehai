"""The recorded eye dataset: one file per recording session, your own labels kept beside it.

    <folder>/<session>.npz           written once by record_dataset.py (or make_fixture.py)
    <folder>/<session>.labels.json   written by label_blinks.py: your frame-by-frame labels

The .npz holds one row per camera frame:

    t            float64 [n]           capture time, seconds (this machine's wall clock)
    x            uint8   [n, 2, 24, 24] the two eye crops, rows top-down (zeros where not found)
    eye_ok       bool    [n, 2]        whether each crop was found
    phase        int8    [n]           0 free, 1 calibration "eyes open", 2 "eyes shut", 3 "blink 3 times"
    space        bool    [n]           SPACE marking was on (a coarse human hint, see README)
    sig_<method> float32 [n]           what that helper would have sent the game, 0 open .. 1 shut;
                                       NaN where it found no face
    ms_<method>  float32 [n]           how long that helper took on the frame, in ms
    meta         str                   JSON: version, session, person, created, synthetic, notes

The teachers' readings (sig_*) and your labels never share a file, so the labels stay a test
set that no teacher can leak into.
"""
from __future__ import annotations

import glob
import json
import os
import re
from dataclasses import dataclass, field

import numpy as np

VERSION = 2
CROP = 24
PHASE_FREE, PHASE_OPEN, PHASE_SHUT, PHASE_BLINKS = 0, 1, 2, 3
PHASE_NAMES = {PHASE_FREE: "free", PHASE_OPEN: "calibration: eyes open",
               PHASE_SHUT: "calibration: eyes shut", PHASE_BLINKS: "calibration: 3 blinks"}
LABEL_DEFINITION = "closed = the eye is more than half shut (the pupil is mostly covered)"
_ID = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")


def check_id(value: str, what: str) -> str:
    if not _ID.match(value or ""):
        raise ValueError(f"{what} {value!r}: use letters, digits, '-', '_' or '.' (a pseudonym, not your name)")
    return value


# ---- intervals ----------------------------------------------------------------------------------


def runs(mask) -> list:
    """The [start, end) frame intervals where `mask` is true."""
    m = np.asarray(mask, bool).astype(np.int8)
    edges = np.diff(np.r_[0, m, 0])
    return [(int(a), int(b)) for a, b in zip(np.nonzero(edges == 1)[0], np.nonzero(edges == -1)[0])]


def mask_from_runs(intervals, n: int) -> np.ndarray:
    m = np.zeros(n, bool)
    for a, b in intervals:
        m[max(0, int(a)):min(n, int(b))] = True
    return m


# ---- labels -------------------------------------------------------------------------------------


@dataclass
class Labels:
    """Your own frame-by-frame labels for one session (label_blinks.py)."""
    closed: np.ndarray            # bool [n]: eye more than half shut
    ignore: np.ndarray            # bool [n]: can't tell (no face, motion blur, looking away)
    reviewed: np.ndarray          # bool [n]: frames you have actually looked at
    labeller: str = "me"

    @classmethod
    def empty(cls, n: int, labeller: str = "me") -> "Labels":
        return cls(np.zeros(n, bool), np.zeros(n, bool), np.zeros(n, bool), labeller)


def labels_path(npz_path: str) -> str:
    return npz_path[:-len(".npz")] + ".labels.json"


def save_labels(path: str, session: str, labels: Labels) -> None:
    doc = {
        "version": 1,
        "session": session,
        "frames": int(len(labels.closed)),
        "definition": LABEL_DEFINITION,
        "labeller": labels.labeller,
        "reviewed": runs(labels.reviewed),
        "closed": runs(labels.closed),
        "ignore": runs(labels.ignore),
    }
    tmp = path + ".tmp"
    with open(tmp, "w") as f:
        json.dump(doc, f, indent=1)
    os.replace(tmp, path)


def load_labels(path: str, n: int) -> Labels | None:
    if not os.path.exists(path):
        return None
    with open(path) as f:
        doc = json.load(f)
    if doc.get("frames") != n:
        raise ValueError(f"{path} labels {doc.get('frames')} frames but the session has {n}")
    return Labels(mask_from_runs(doc["closed"], n), mask_from_runs(doc["ignore"], n),
                  mask_from_runs(doc["reviewed"], n), doc.get("labeller", "me"))


# ---- sessions -----------------------------------------------------------------------------------


@dataclass
class Session:
    path: str
    id: str
    person: str
    t: np.ndarray
    x: np.ndarray
    eye_ok: np.ndarray
    phase: np.ndarray
    space: np.ndarray
    signals: dict = field(default_factory=dict)   # method -> float32 [n], NaN = no reading
    ms: dict = field(default_factory=dict)        # method -> float32 [n]
    meta: dict = field(default_factory=dict)
    labels: Labels | None = None

    @property
    def n(self) -> int:
        return len(self.t)

    @property
    def synthetic(self) -> bool:
        return bool(self.meta.get("synthetic", False))

    @property
    def minutes(self) -> float:
        return float(self.t[-1] - self.t[0]) / 60.0 if self.n > 1 else 0.0

    @property
    def fps(self) -> float:
        return float(1.0 / np.median(np.diff(self.t))) if self.n > 1 else 0.0

    @property
    def labelled(self) -> bool:
        return self.labels is not None and bool(self.labels.reviewed.any())

    def has(self, method: str) -> bool:
        s = self.signals.get(method)
        return s is not None and bool(np.isfinite(s).any())


def save_session(path: str, *, session: str, person: str, t, x, eye_ok, phase, space,
                 signals: dict, ms: dict, synthetic: bool = False, notes: str = "",
                 created: str = "") -> None:
    check_id(session, "session")
    check_id(person, "person")
    t = np.asarray(t, np.float64)
    n = len(t)
    x = np.asarray(x, np.uint8)
    if x.shape != (n, 2, CROP, CROP):
        raise ValueError(f"x has shape {x.shape}, expected {(n, 2, CROP, CROP)}")
    if n > 1 and not np.all(np.diff(t) > 0):
        raise ValueError("frame times must increase")
    arrays = {
        "t": t, "x": x,
        "eye_ok": np.asarray(eye_ok, bool).reshape(n, 2),
        "phase": np.asarray(phase, np.int8).reshape(n),
        "space": np.asarray(space, bool).reshape(n),
    }
    for name, values in signals.items():
        arrays["sig_" + name] = np.asarray(values, np.float32).reshape(n)
    for name, values in ms.items():
        arrays["ms_" + name] = np.asarray(values, np.float32).reshape(n)
    meta = {"version": VERSION, "session": session, "person": person, "created": created,
            "synthetic": bool(synthetic), "notes": notes}
    tmp = path + ".tmp.npz"
    np.savez_compressed(tmp, meta=np.array(json.dumps(meta)), **arrays)
    os.replace(tmp, path)


def load_session(path: str, with_labels: bool = True) -> Session:
    with np.load(path, allow_pickle=False) as d:
        if "meta" not in d.files:
            raise ValueError(f"{path} is an old recording from before sessions and human labels; "
                             "record it again with record_dataset.py")
        meta = json.loads(str(d["meta"]))
        s = Session(
            path=path, id=meta["session"], person=meta.get("person", "me"),
            t=d["t"], x=d["x"], eye_ok=d["eye_ok"], phase=d["phase"], space=d["space"],
            signals={k[4:]: d[k] for k in d.files if k.startswith("sig_")},
            ms={k[3:]: d[k] for k in d.files if k.startswith("ms_")},
            meta=meta)
    if with_labels:
        s.labels = load_labels(labels_path(path), s.n)
    return s


def session_paths(folder: str) -> list:
    return sorted(p for p in glob.glob(os.path.join(folder, "*.npz")) if not p.endswith(".tmp.npz"))


def load_sessions(folder: str, ids=None) -> list:
    paths = session_paths(folder)
    if not paths:
        raise SystemExit(f"no recordings in {folder}: record some with record_dataset.py "
                         "(or make a synthetic set with make_fixture.py)")
    sessions = [load_session(p) for p in paths]
    seen = {}
    for s in sessions:
        if s.id in seen:
            raise SystemExit(f"session id {s.id!r} is in both {seen[s.id]} and {s.path}")
        seen[s.id] = s.path
    if ids:
        wanted = set(ids)
        missing = wanted - set(seen)
        if missing:
            raise SystemExit(f"no session(s) {', '.join(sorted(missing))} in {folder}")
        sessions = [s for s in sessions if s.id in wanted]
    return sorted(sessions, key=lambda s: s.id)
