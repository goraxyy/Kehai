"""`playtest.py open <session>` (playtest v2, C3.3): one session's replays from the cloud, on this Mac
only while they're being looked at.

The session goes into ~/TokenLimit/playtests/cache/<round>/<tester>/<launch>/, ready for
Kehai → Replay → Open Shift…. The cache empties itself: anything not opened for 7 days goes,
and past 2 GB the least recently opened go first. The cloud copy is never touched.
"""
from __future__ import annotations

import os
import shutil
import time
from pathlib import Path

from .inbox import REMOTE, Remote, unpack

CACHE = Path.home() / "TokenLimit" / "playtests" / "cache"
DAYS = 7
LIMIT = 2 * 1024 ** 3


def _size(folder: Path) -> int:
    return sum(f.stat().st_size for f in folder.rglob("*") if f.is_file())


def _sessions(cache: Path) -> list[Path]:
    return [launch for round_ in cache.iterdir() if round_.is_dir()
            for tester in round_.iterdir() if tester.is_dir()
            for launch in tester.iterdir() if launch.is_dir()] if cache.is_dir() else []


def prune(cache: Path = CACHE, now: float | None = None, days: float = DAYS, limit: int = LIMIT, keep: Path | None = None) -> list[Path]:
    """Removes sessions not opened for `days`, then the least recently opened while the cache is
    over `limit` bytes. `keep` (the one being opened) always stays. Returns what went."""
    now = time.time() if now is None else now
    gone: list[Path] = []
    found = sorted(_sessions(cache), key=lambda p: p.stat().st_mtime)
    for folder in list(found):
        if folder != keep and now - folder.stat().st_mtime > days * 86400:
            shutil.rmtree(folder)
            found.remove(folder)
            gone.append(folder)
    sizes = {f: _size(f) for f in found}
    total = sum(sizes.values())
    for folder in found:
        if total <= limit:
            break
        if folder == keep:
            continue
        shutil.rmtree(folder)
        total -= sizes[folder]
        gone.append(folder)
    for empty in sorted({p.parent for p in gone} | {p.parent.parent for p in gone}, key=lambda p: len(p.parts), reverse=True):
        if empty != cache and empty.is_dir() and not any(empty.iterdir()):
            empty.rmdir()
    return gone


def open_session(service: Remote, session_id: str, cache: Path = CACHE) -> Path:
    """Fetches one session (unless it's already here), marks it opened now, and tidies the cache."""
    m = REMOTE.match(session_id.removesuffix(".zip") + ".zip")
    if not m:
        raise ValueError(f"{session_id!r} isn't a session id (round/tester/launch)")
    round_, tester, launch = m.groups()
    folder = cache / round_ / tester / launch
    if not (folder / "session.jsonl").is_file():
        unpack(service.fetch(f"{round_}/{tester}/{launch}.zip"), round_, tester, launch, cache)
    os.utime(folder)
    prune(cache, keep=folder)
    return folder
