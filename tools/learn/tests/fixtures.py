"""Synthetic shift records, in the game's own format (ShiftRecording.ToJson)."""
import json
import math
import os
import random


def frame(t, x, z, motion=2, saw=0, heard=0):
    return [t, x, z, 0, motion, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, [], [], [], [], [], [], [], saw, heard]


def shift(player="Employee", bot="", n=0, seconds=60, seed=0, jobs=("served a customer", "mopped a spill"),
          sprint=0.0, choices=(), clocked_out=True, caught=False, wobble=1.0, start=(0.0, 0.0)):
    r = random.Random(seed)
    frames, x, z = [], start[0], start[1]
    for i in range(int(seconds * 5)):
        t = i * 0.2
        x += r.uniform(-wobble, wobble) * 0.3 + 0.2
        z += math.sin(t / 3.0 + seed) * 0.3
        frames.append(frame(t, x, z, 3 if r.random() < sprint else 2))
    events = []
    for k, w in enumerate(jobs):
        events.append({"t": 5 + k * 5.0, "k": "job", "w": w, "s": "did " + w})
    for c in choices:
        events.append({"t": c[0], "k": "choice", "x": c[2], "y": c[3], "w": "you", "s": c[1]})
    if caught:
        events.append({"t": seconds - 1, "k": "Chase", "w": "Karen", "s": "Karen got you. The shift starts again."})
    d = {"shift": n, "player": player, "rung": "E_Ledger", "length": seconds, "clockedOut": clocked_out,
         "frames": frames, "events": events, "answers": []}
    if bot:
        d["bot"] = bot
    return d


def write(folder, name, record):
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, name)
    with open(path, "w") as f:
        json.dump(record, f)
    return path
