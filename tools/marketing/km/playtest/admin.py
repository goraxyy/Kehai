"""The owner's two admin steps on the upload service (playtest v2):

    forget  (C3.4)  a deletion request: every file and row of one tester, in the cloud and here
    publish (C3.5)  a round's Mac and Windows zips into R2 builds/, then the current build (/latest)

Both use the admin key (KEHAI_PLAYTEST_ADMIN_KEY), which stays on the owner's Mac. Builds are
made only on the owner's go; `publish` sends zips that already exist.
"""
from __future__ import annotations

import re
import shutil
import subprocess
from pathlib import Path
from typing import Callable

from .inbox import Remote

TESTER = re.compile(r"^(?:[A-Z0-9-]{2,16}|[A-F0-9]{32})$")
VERSION = re.compile(r"^[0-9A-Za-z.+-]{1,40}$")
ROUND = re.compile(r"^[A-Za-z0-9-]{1,40}$")
BUCKET = "kehai-playtests"
WORKER = Path(__file__).resolve().parents[4] / "tools" / "playtest" / "worker"


def forget(where: Path, service: Remote | None, tester: str) -> list[str]:
    """Deletes one tester everywhere: the service (files, rows, and later sends refused) and every
    local folder of theirs under <playtests>/<round>/<tester>/. Returns what it did, line by line."""
    tester = tester.strip().upper()
    if not TESTER.match(tester):
        raise ValueError(f"{tester!r} isn't a tester ID")
    lines = []
    if service:
        r = service.http.delete(f"{service.url}/admin/tester/{tester}")
        r.raise_for_status()
        lines.append(f"the service: {r.json().get('sessions', 0)} session(s) of {tester} deleted; later sends from that ID are refused")
    else:
        lines.append("the service isn't set up here: nothing deleted in the cloud")
    local = 0
    if where.is_dir():
        for round_dir in where.iterdir():
            folder = round_dir / tester
            if round_dir.is_dir() and folder.is_dir():
                local += sum(1 for _ in folder.iterdir())
                shutil.rmtree(folder)
    lines.append(f"this Mac: {local} session folder(s) removed")
    return lines


def publish(service: Remote, round_: str, version: str, mac: Path, windows: Path,
            run: Callable[..., subprocess.CompletedProcess] = subprocess.run) -> dict:
    """Puts both zips into R2 with wrangler (a Worker can't take a request that big), then makes
    them the build testers download and the game's "a newer build is out" line points at."""
    if not ROUND.match(round_) or not VERSION.match(version):
        raise ValueError("round and version, like round1 0.5.2-beta.1")
    for path in (mac, windows):
        if not path.is_file() or path.suffix != ".zip":
            raise ValueError(f"{path}: not a zip")
    for platform, path in (("mac", mac), ("windows", windows)):
        key = f"{BUCKET}/builds/{round_}/{version}/{platform}.zip"
        done = run(["npx", "wrangler", "r2", "object", "put", key, "--file", str(path), "--remote",
                    "--content-type", "application/zip"], cwd=WORKER)
        if done.returncode != 0:
            raise RuntimeError(f"wrangler couldn't put {key} (exit {done.returncode})")
    r = service.http.post(f"{service.url}/admin/latest", json={"round": round_, "version": version})
    r.raise_for_status()
    return r.json()


def tester_zips(builds: Path, round_: str) -> tuple[Path | None, Path | None]:
    """KehaiBuild's zips for a round: Builds/playtest-<round>/<product>-playtest-<round>-<macOS|Windows>.zip."""
    folder = builds / f"playtest-{round_}"
    mac = next(iter(sorted(folder.glob("*-macOS.zip"))), None)
    windows = next(iter(sorted(folder.glob("*-Windows.zip"))), None)
    return mac, windows
