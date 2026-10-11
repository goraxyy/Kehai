"""The asset library: files the editor may use, each with where it came from and its licence.

Files live in <working folder>/assets/<kind>/ and are listed in assets/manifest.json; both are
mirrored to Google Drive (TokenLimit Marketing/assets) through rclone when it's set up.
Nothing without a licence gets in, and no music that isn't cleared for YouTube, TikTok and
Instagram (docs/MARKETING.md).
"""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import re
import shutil
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from . import paths, schemas

FOLDERS = {"music": "music", "sfx": "sfx", "image": "images", "gif": "gifs", "lottie": "lottie", "video": "video", "font": "fonts"}
EXTENSIONS = {
    "music": {".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg"},
    "sfx": {".wav", ".mp3", ".m4a", ".aac", ".flac", ".ogg"},
    "image": {".png", ".jpg", ".jpeg", ".webp"},
    "gif": {".gif"},
    "lottie": {".json"},
    "video": {".mp4", ".webm", ".mov"},
    "font": {".ttf", ".otf", ".woff", ".woff2"},
}
PLATFORMS = ("youtube", "tiktok", "instagram")
NOT_A_LICENCE = {"", "unknown", "none", "?", "n/a", "na", "tbd", "todo"}


class Refused(Exception):
    """The file can't enter the library, and why."""


@dataclass
class Request:
    file: Path
    kind: str
    licence: str
    source: str
    author: str = ""
    attribution: str = ""
    cleared: tuple[str, ...] = ()
    mood: list[str] = field(default_factory=list)
    tags: list[str] = field(default_factory=list)
    notes: str = ""
    id: str | None = None


def manifest_path(root: Path) -> Path:
    return root / "assets" / "manifest.json"


def load(root: Path) -> dict:
    path = manifest_path(root)
    if not path.exists():
        return {"version": 1, "assets": []}
    data = json.loads(path.read_text(encoding="utf-8"))
    problems = schemas.errors("manifest", data)
    if problems:
        raise Refused(f"{path} is damaged: {problems[0]}")
    return data


def save(root: Path, manifest: dict) -> None:
    problems = schemas.errors("manifest", manifest)
    if problems:
        raise Refused(f"the manifest wouldn't be valid: {problems[0]}")
    path = manifest_path(root)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(".json.tmp")
    tmp.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    tmp.replace(path)


def slug(text: str) -> str:
    s = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")
    return s or "asset"


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def check(req: Request) -> None:
    """Refuses what mustn't enter the library, with the reason."""
    if req.kind not in FOLDERS:
        raise Refused(f"unknown kind {req.kind!r} (one of {', '.join(FOLDERS)})")
    if not req.file.is_file():
        raise Refused(f"no such file: {req.file}")
    if req.licence.strip().lower() in NOT_A_LICENCE:
        raise Refused("no licence: every asset needs one (e.g. 'CC0 1.0', 'CC BY 4.0', 'Pixabay Content License', or 'own work')")
    if req.source.strip().lower() in NOT_A_LICENCE:
        raise Refused("no source: say where it came from (a URL, or 'own' for something we made)")
    unknown = set(req.cleared) - set(PLATFORMS)
    if unknown:
        raise Refused(f"unknown platform(s) {', '.join(sorted(unknown))} (youtube, tiktok, instagram)")
    if req.kind == "music" and set(req.cleared) != set(PLATFORMS):
        missing = [p for p in PLATFORMS if p not in req.cleared]
        raise Refused(f"music must be cleared for YouTube, TikTok and Instagram; not cleared for: {', '.join(missing)}")
    ext = req.file.suffix.lower()
    if ext not in EXTENSIONS[req.kind]:
        raise Refused(f"a {req.kind} file is one of {', '.join(sorted(EXTENSIONS[req.kind]))}, not {ext or 'no extension'}")
    if req.kind == "lottie":
        try:
            data = json.loads(req.file.read_text(encoding="utf-8"))
        except (ValueError, UnicodeDecodeError) as e:
            raise Refused(f"not a Lottie file: {e}") from e
        if not isinstance(data, dict) or "layers" not in data or "fr" not in data:
            raise Refused("not a Lottie file: no layers or frame rate")


@dataclass
class Result:
    entry: dict
    added: bool                 # False: the same file was already there
    actions: list[str]


def add(root: Path, req: Request, dry_run: bool = False, upload: bool = True) -> Result:
    check(req)
    manifest = load(root)
    digest = sha256(req.file)
    for existing in manifest["assets"]:
        if existing["sha256"] == digest:
            return Result(existing, False, [f"already in the library as {existing['id']} ({existing['file']})"])

    taken = {a["id"] for a in manifest["assets"]}
    base = slug(req.id or req.file.stem)
    new_id, n = base, 2
    while new_id in taken:
        new_id, n = f"{base}-{n}", n + 1

    rel = f"assets/{FOLDERS[req.kind]}/{new_id}{req.file.suffix.lower()}"
    entry = {
        "id": new_id, "type": req.kind, "file": rel, "sha256": digest, "bytes": req.file.stat().st_size,
        "added": dt.date.today().isoformat(), "licence": req.licence.strip(), "source": req.source.strip(),
        "cleared": {p: p in req.cleared for p in PLATFORMS}, "mood": req.mood, "tags": req.tags,
        "drive": {"status": "pending"},
    }
    for key in ("author", "attribution", "notes"):
        if getattr(req, key):
            entry[key] = getattr(req, key)

    actions = [f"copy {req.file} → {root / rel}", f"add {new_id} to {manifest_path(root)}"]
    if dry_run:
        return Result(entry, True, ["(dry run) " + a for a in actions])

    (root / rel).parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(req.file, root / rel)
    manifest["assets"].append(entry)
    save(root, manifest)
    if upload:
        actions += sync(root)
    return Result(entry, True, actions)


# ---- Google Drive ----------------------------------------------------------------------------

def drive_ready() -> bool:
    if shutil.which("rclone") is None:
        return False
    out = subprocess.run(["rclone", "listremotes"], capture_output=True, text=True)
    return f"{paths.DRIVE_REMOTE}:" in out.stdout.split()


def sync(root: Path, dry_run: bool = False) -> list[str]:
    """Uploads every asset not yet on Drive, then the manifest."""
    manifest = load(root)
    pending = [a for a in manifest["assets"] if a["drive"]["status"] != "uploaded"]
    if not pending:
        return ["Drive: nothing to upload"]
    if not drive_ready():
        return [f"Drive: not set up yet ({len(pending)} asset(s) wait): install rclone and add the '{paths.DRIVE_REMOTE}' remote (docs/MARKETING.md, Setup), then run add_asset.py --sync"]
    actions = []
    for a in pending:
        remote = f"{paths.DRIVE_REMOTE}:{paths.DRIVE_FOLDER}/{a['file']}"
        if dry_run:
            actions.append(f"(dry run) upload {a['file']} → {remote}")
            continue
        subprocess.run(["rclone", "copyto", str(root / a["file"]), remote], check=True)
        a["drive"] = {"status": "uploaded", "path": f"{paths.DRIVE_FOLDER}/{a['file']}", "uploaded": dt.datetime.now().isoformat(timespec="seconds")}
        actions.append(f"uploaded {a['file']}")
    if not dry_run:
        save(root, manifest)
        subprocess.run(["rclone", "copyto", str(manifest_path(root)), f"{paths.DRIVE_REMOTE}:{paths.DRIVE_FOLDER}/assets/manifest.json"], check=True)
        actions.append("uploaded the manifest")
    return actions
