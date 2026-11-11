"""Reading belief traces (Eval/Scripts/BeliefTrace.cs): a map.json per run, one .jsonl per shift."""
from __future__ import annotations

import glob
import json
import os

import kernels


def load_map(folder: str) -> kernels.Map:
    with open(os.path.join(folder, "map.json")) as f:
        return kernels.Map(json.load(f))


def ops(path: str):
    with open(path) as f:
        for line in f:
            if line.strip():
                yield json.loads(line)


def shifts(folder: str) -> list:
    return sorted(glob.glob(os.path.join(folder, "trace_*.jsonl")))


def header(path: str) -> dict:
    for op in ops(path):
        if op["op"] == "start":
            return op
    return {}


def career_of(path: str) -> str:
    """trace_<seed>_<shift>_<agent>.jsonl: a career is a seed and an agent."""
    parts = os.path.basename(path)[len("trace_"):-len(".jsonl")].split("_", 2)
    return parts[0] + "_" + (parts[2] if len(parts) > 2 else "")
