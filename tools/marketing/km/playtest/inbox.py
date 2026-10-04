"""Getting sessions onto the Mac: from the upload service (list, download, unpack, then delete it
there), or from <playtests>/inbox/ for zips that came another way."""
from __future__ import annotations

import io
import re
import shutil
import zipfile
from pathlib import Path

import httpx

from . import setting

REMOTE = re.compile(r"^([A-Za-z0-9-]+)/([A-Z0-9-]{2,16})/(\d{8}_\d{6})\.zip$")       # round/code/launch.zip
LOCAL = re.compile(r"^([A-Za-z0-9-]+)_([A-Z0-9-]{2,16})_(\d{8}_\d{6})\.zip$")        # round_code_launch.zip
BIGGEST = 200 * 1024 * 1024                                                            # one file inside a zip


class BadSession(Exception):
    """A zip that isn't a playtest session."""


class Remote:
    """The upload service's admin side."""

    def __init__(self, url: str, key: str, transport: httpx.BaseTransport | None = None):
        self.url = url.rstrip("/")
        self.http = httpx.Client(timeout=120, transport=transport, headers={"Authorization": f"Bearer {key}"})

    def waiting(self) -> list[dict]:
        r = self.http.get(f"{self.url}/admin/sessions")
        r.raise_for_status()
        return r.json()["sessions"]

    def fetch(self, key: str) -> bytes:
        r = self.http.get(f"{self.url}/admin/sessions/{key}")
        r.raise_for_status()
        return r.content

    def remove(self, key: str) -> None:
        self.http.delete(f"{self.url}/admin/sessions/{key}").raise_for_status()


def remote() -> Remote | None:
    url, key = setting("KEHAI_PLAYTEST_UPLOAD_URL"), setting("KEHAI_PLAYTEST_ADMIN_KEY")
    return Remote(url, key) if url and key else None


def unpack(data: bytes, round_: str, code: str, launch: str, where: Path) -> Path:
    """Unzips one session into <where>/<round>/<code>/<launch>/ and returns the folder."""
    folder = where / round_ / code / launch
    part = folder.with_name(launch + ".part")
    shutil.rmtree(part, ignore_errors=True)
    part.mkdir(parents=True)
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for info in z.infolist():
                name = Path(info.filename)
                if info.is_dir() or name.is_absolute() or ".." in name.parts or info.file_size > BIGGEST:
                    continue
                target = part / name
                target.parent.mkdir(parents=True, exist_ok=True)
                with z.open(info) as src, target.open("wb") as dst:
                    shutil.copyfileobj(src, dst)
    except zipfile.BadZipFile as e:
        _discard(part, where)
        raise BadSession(f"{round_}/{code}/{launch}: not a zip ({e})") from e
    if not (part / "session.jsonl").exists():
        _discard(part, where)
        raise BadSession(f"{round_}/{code}/{launch}: no session.jsonl inside")
    if folder.exists():
        shutil.rmtree(folder)
    part.rename(folder)
    return folder


def _discard(part: Path, where: Path) -> None:
    """A rejected session leaves nothing: its half-unpacked folder, and the folders made for it."""
    shutil.rmtree(part, ignore_errors=True)
    for folder in (part.parent, part.parent.parent):
        if folder != where and folder.is_dir() and not any(folder.iterdir()):
            folder.rmdir()


def import_zip(path: Path, where: Path) -> Path:
    """A session zip with its outbox name (round_code_launch.zip)."""
    m = LOCAL.match(path.name)
    if not m:
        raise BadSession(f"{path.name}: not named like round_code_launch.zip")
    return unpack(path.read_bytes(), *m.groups(), where)


def pull(where: Path, service: Remote | None) -> tuple[list[Path], list[str]]:
    """New sessions, from the inbox folder and the service. A session is deleted from the service
    only once it's unpacked here."""
    new: list[Path] = []
    notes: list[str] = []
    inbox = where / "inbox"
    if inbox.is_dir():
        for z in sorted(inbox.glob("*.zip")):
            try:
                new.append(import_zip(z, where))
                done = inbox / "done"
                done.mkdir(exist_ok=True)
                z.rename(done / z.name)
            except BadSession as e:
                notes.append(f"playtest: left {z.name} in the inbox: {e}")
    if service:
        for s in service.waiting():
            m = REMOTE.match(s["key"])
            if not m:
                notes.append(f"playtest: ignored {s['key']} on the service")
                continue
            round_, code, launch = m.groups()
            if (where / round_ / code / launch / "session.jsonl").exists():
                service.remove(s["key"])              # already here from an earlier pull
                continue
            try:
                new.append(unpack(service.fetch(s["key"]), round_, code, launch, where))
            except BadSession as e:
                notes.append(f"playtest: {e}")
                continue
            service.remove(s["key"])
    return new, notes
