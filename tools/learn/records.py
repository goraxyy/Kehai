"""Reading shift records (ShiftRecording.ToJson), the same way for bots and for people.

A shift record is one JSON file per shift, written by the game's ShiftRecorder to
<persistent data>/shift_records/ (and, for bot runs, the eval's copy of it). Frames are arrays
of numbers, five a second; see FRAME for what each position holds. Events carry the jobs done,
her story, and (for simulated players, since 0.4.0) every choice with where it led.

Standard library only, plus numpy where arrays help.
"""
from __future__ import annotations

import glob
import json
import os
from dataclasses import dataclass, field

FRAME = dict(t=0, px=1, pz=2, yaw=3, motion=4, held=5, energy=6, karen=7, kx=8, kz=9, kyaw=10,
             mood=11, sees=12, chase=13, gx=14, gz=15, gconf=16, power=17, customers=18, spills=19,
             empty=20, bins=21, bags=22, locked=23, props=24, saw=25, heard=26)
MOTION = ("still", "crouching", "walking", "sprinting")
CHOICE_FIELDS = ("what", "how", "which", "verb", "target", "chance", "by", "result")


@dataclass
class Choice:
    t: float
    what: str
    how: str
    which: str
    verb: str
    target: str
    chance: float
    by: str
    ok: bool
    x: float
    z: float


@dataclass
class Shift:
    path: str
    player: str            # a person's name, or the bot's persona label
    is_bot: bool
    shift: int
    length: float
    clocked_out: bool
    frames: list
    events: list
    answers: list = field(default_factory=list)
    seed: int = 0

    def col(self, name):
        i = FRAME[name]
        return [f[i] if i < len(f) else 0 for f in self.frames]

    @property
    def caught(self) -> bool:
        return any(e.get("k") == "Chase" and "got you" in e.get("s", "") for e in self.events)

    @property
    def jobs(self):
        return [(e["t"], e.get("w", "")) for e in self.events if e.get("k") == "job"]

    @property
    def choices(self):
        out = []
        for e in self.events:
            if e.get("k") != "choice":
                continue
            parts = (e.get("s", "") + "|" * 8).split("|")
            try:
                chance = float(parts[5] or 1)
            except ValueError:
                chance = 1.0
            out.append(Choice(e["t"], parts[0], parts[1], parts[2], parts[3], parts[4], chance, parts[6],
                              parts[7] != "cut", e.get("x", 0.0), e.get("y", 0.0)))
        return out

    @property
    def persona_name(self) -> str:
        return self.player.split(":", 1)[0] if self.is_bot else "person"


def load(path: str) -> Shift:
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    bot = d.get("bot") or ""
    return Shift(path=path, player=bot or d.get("player", "?"), is_bot=bool(bot), shift=int(d.get("shift", 0)),
                 length=float(d.get("length", 0)), clocked_out=bool(d.get("clockedOut", False)),
                 frames=d.get("frames", []), events=d.get("events", []), answers=d.get("answers", []), seed=int(d.get("seed", 0)))


def find(paths) -> list:
    """Shift record files under the given files or folders (not the .markers.json beside them)."""
    out = []
    for p in paths:
        if os.path.isdir(p):
            out += sorted(x for x in glob.glob(os.path.join(p, "**", "*.json"), recursive=True)
                          if not x.endswith(".markers.json") and not os.path.basename(x).startswith("ablation_"))
        else:
            out.append(p)
    return out


def load_all(paths) -> list:
    shifts = []
    for p in find(paths):
        try:
            s = load(p)
        except (OSError, ValueError, KeyError):
            continue
        if s.frames:
            shifts.append(s)
    return shifts
