"""The heavy-work lock: one Unity, training or render job at a time on this Mac (RULES.md).

The lock is a directory, ~/TokenLimit/marketing/state/heavy.lock by default, holding:
  pid      the holder's process id
  started  the holder's start time, as `ps -o lstart=` prints it (written by this module)
  job      what it's doing, for people
The marketing tools (km/lock.py, render_shot.sh, editor/render.mjs) write only `pid` and `job`
into the same directory, and keep working unchanged: a lock without `started` is judged by its
pid alone, as they judge it. A lock whose holder has died, or whose pid now belongs to a newer
process (the start time differs), is taken over, and the caller is told whose it was.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import time
from contextlib import contextmanager
from pathlib import Path

BUSY_EXIT = 75
DEFAULT = Path.home() / 'TokenLimit' / 'marketing' / 'state' / 'heavy.lock'
FRESH_SECONDS = 5.0   # a lock just made whose pid isn't written yet


class Busy(Exception):
    """Another heavy job holds the lock."""


def start_time(pid: int) -> str | None:
    """The process's start time as ps prints it, or None if there's no such process."""
    if pid <= 0:
        return None
    try:
        out = subprocess.run(['ps', '-o', 'lstart=', '-p', str(pid)], capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return None
    text = out.stdout.strip()
    return text or None


def alive(pid: int, started: str | None = None) -> bool:
    """Whether `pid` is running and, when `started` is known, is the same process."""
    if pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        pass
    if started:
        observed = start_time(pid)
        return observed is None or observed == started
    return True


def holder(lock: Path = DEFAULT) -> dict:
    """What the lock says about its holder: pid, started, job (any may be missing)."""
    info = {}
    for name in ('pid', 'started', 'job'):
        try:
            info[name] = (Path(lock) / name).read_text().strip()
        except OSError:
            pass
    return info


def take(lock: Path, job: str, pid: int | None = None) -> tuple[bool, dict | None]:
    """Try once. Returns (taken, the dead holder taken over from, if any)."""
    lock = Path(lock)
    pid = pid or os.getpid()
    lock.parent.mkdir(parents=True, exist_ok=True)
    taken_over = None
    try:
        lock.mkdir()
    except FileExistsError:
        info = holder(lock)
        try:
            other = int(info.get('pid') or 0)
        except ValueError:
            other = 0
        if other and alive(other, info.get('started')):
            return False, None
        if not other:
            try:
                if time.time() - lock.stat().st_mtime < FRESH_SECONDS:
                    return False, None
            except OSError:
                pass
        taken_over = info
        shutil.rmtree(lock, ignore_errors=True)
        try:
            lock.mkdir()
        except FileExistsError:
            return False, None
    (lock / 'pid').write_text(str(pid))
    (lock / 'started').write_text(start_time(pid) or '')
    (lock / 'job').write_text(job)
    return True, taken_over


def release(lock: Path = DEFAULT, pid: int | None = None) -> bool:
    """Free the lock if `pid` (default: this process) holds it."""
    lock = Path(lock)
    pid = pid or os.getpid()
    if holder(lock).get('pid') == str(pid):
        shutil.rmtree(lock, ignore_errors=True)
        return True
    return False


@contextmanager
def held(job: str, lock: Path = DEFAULT, wait: float = 0.0, log=print):
    """Hold the lock for a `with` block; wait up to `wait` seconds for it, else raise Busy."""
    waited = 0.0
    while True:
        ok, dead = take(lock, job)
        if ok:
            break
        if waited >= wait:
            info = holder(lock)
            raise Busy(f"another heavy job holds the lock: {info.get('job', 'unknown')} (pid {info.get('pid', '?')})")
        time.sleep(1.0)
        waited += 1.0
    if dead:
        log(f"took over a lock left by a job that had stopped: {dead.get('job', 'unknown')} (pid {dead.get('pid', '?')})")
    try:
        yield
    finally:
        release(lock)


def main(argv=None) -> int:
    """For the shell scripts: `lock.py take "<job>" --pid $$` (exit 75 when busy) and `release --pid $$`."""
    import argparse
    import sys
    a = argparse.ArgumentParser(prog='lock.py')
    a.add_argument('action', choices=['take', 'release', 'show'])
    a.add_argument('job', nargs='?', default='a heavy job')
    a.add_argument('--pid', type=int, default=None)
    a.add_argument('--lock', type=Path, default=DEFAULT)
    o = a.parse_args(argv)
    if o.action == 'show':
        print(holder(o.lock) or 'free')
        return 0
    if o.action == 'release':
        release(o.lock, o.pid)
        return 0
    ok, dead = take(o.lock, o.job, o.pid)
    if dead:
        print(f"took over a lock left by a stopped job: {dead.get('job', 'unknown')} (pid {dead.get('pid', '?')})", file=sys.stderr)
    if not ok:
        info = holder(o.lock)
        print(f"another heavy job holds the lock: {info.get('job', 'unknown')} (pid {info.get('pid', '?')})", file=sys.stderr)
        return BUSY_EXIT
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
