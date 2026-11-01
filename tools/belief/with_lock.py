#!/usr/bin/env python3
"""Run one long job the way this Mac needs it: no Unity editor open, holding the heavy-work lock,
and kept awake.

    python with_lock.py <job name> <command...>

It waits for the owner's editor to close first (8 GB can't run both, CLAUDE.md), then takes the
lock render_shot.sh and the marketing jobs take (km/lock.py), so one heavy job runs at a time.
`caffeinate -i` stops the Mac idling to sleep; it can't help with a closed lid on battery.
"""
from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "marketing"))

from km import lock  # noqa: E402

LOCK_WAIT = 3600.0


def unity_open() -> bool:
    return subprocess.run(["pgrep", "-f", "Unity.app/Contents/MacOS/Unity"], capture_output=True).returncode == 0


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__)
        return 2
    job, cmd = argv[0], argv[1:]

    if unity_open():
        print(f"[{time.strftime('%H:%M:%S')}] {job}: waiting for Unity to close", flush=True)
        while unity_open():
            time.sleep(5)

    root = Path(os.environ.get("KEHAI_MARKETING") or Path.home() / "TokenLimit" / "marketing")
    with lock.heavy(root, job, wait=LOCK_WAIT):
        print(f"[{time.strftime('%H:%M:%S')}] {job}: started, holding the heavy-work lock", flush=True)
        code = subprocess.call(["caffeinate", "-i", *cmd])
    print(f"[{time.strftime('%H:%M:%S')}] {job}: finished, exit {code}", flush=True)
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
