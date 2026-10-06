"""Moving finished files around: a smaller preview for Telegram, the archive on Google Drive, and
a public copy for Buffer.

- **ffmpeg:** the one Remotion ships with the editor (run from its own folder), or KEHAI_FFMPEG.
- **Drive** (archive): rclone's `gdrive` remote (Q7: the drive.file scope), folder
  "TokenLimit Marketing".
- **Public host** (Buffer fetches videos from a public URL and refuses Drive share links; it
  fetches them when the post goes out, so the file must stay up until then): any rclone remote
  serving files at a stable public URL, e.g. a Cloudflare R2 bucket with public access (Q8).
  KEHAI_PUBLIC_REMOTE is the rclone path (e.g. `r2:kehai-public`), KEHAI_PUBLIC_URL the URL the
  same files are served at (e.g. `https://pub-….r2.dev`).

Without rclone or those settings, each call says so (NotSetUp) and the pipeline carries on
without that part.
"""
from __future__ import annotations

import glob
import shutil
import subprocess
from pathlib import Path

from . import env, paths


class NotSetUp(Exception):
    """A tool or an account this needs isn't set up yet."""


def ffmpeg() -> Path:
    given = env.get("KEHAI_FFMPEG")
    if given and Path(given).is_file():
        return Path(given)
    found = sorted(glob.glob(str(paths.HERE / "editor" / "node_modules" / "@remotion" / "compositor-*" / "ffmpeg")))
    if found:
        return Path(found[0])
    system = shutil.which("ffmpeg")
    if system:
        return Path(system)
    raise NotSetUp("no ffmpeg: run `npm install` in tools/marketing/editor")


def ffprobe() -> Path:
    """The ffprobe beside whichever ffmpeg is used (Remotion ships both)."""
    exe = ffmpeg().with_name("ffprobe")
    if exe.is_file():
        return exe
    system = shutil.which("ffprobe")
    if system:
        return Path(system)
    raise NotSetUp("no ffprobe beside ffmpeg: run `npm install` in tools/marketing/editor")


def preview(src: Path, dst: Path, width: int) -> Path:
    """A smaller copy of a video, for when the full one is too big to send."""
    exe = ffmpeg()
    dst.parent.mkdir(parents=True, exist_ok=True)
    cmd = [str(exe), "-y", "-hide_banner", "-loglevel", "error", "-i", str(src.resolve()), "-vf", f"scale={width}:-2",
           "-c:v", "libx264", "-preset", "medium", "-crf", "28", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
           str(dst.resolve())]
    r = subprocess.run(cmd, cwd=exe.parent, capture_output=True, text=True, timeout=900)
    if r.returncode != 0:
        raise RuntimeError(f"ffmpeg couldn't make a preview of {src.name}: {r.stderr.strip()[-300:]}")
    return dst


def megabytes(path: Path) -> float:
    return path.stat().st_size / 1e6


def rclone() -> str:
    exe = shutil.which("rclone")
    if not exe:
        raise NotSetUp("rclone isn't installed (brew install rclone; BUILD_PLAN.md, owner setup)")
    return exe


def _run(args: list[str], timeout: int = 3600) -> str:
    r = subprocess.run([rclone(), *args], capture_output=True, text=True, timeout=timeout)
    if r.returncode != 0:
        raise RuntimeError(f"rclone {args[0]} failed: {r.stderr.strip()[-400:]}")
    return r.stdout.strip()


def drive_target(rel: str) -> str:
    remote = paths.DRIVE_REMOTE
    if remote not in _run(["listremotes"]).replace(":", "").split():
        raise NotSetUp(f"rclone has no `{remote}` remote yet (rclone config; BUILD_PLAN.md, owner setup)")
    return f"{remote}:{paths.DRIVE_FOLDER}/{rel}"


def archive(path: Path, rel: str) -> str:
    """Copies a file to Drive (TokenLimit Marketing/<rel>); returns where it went."""
    target = drive_target(rel)
    _run(["copyto", str(path), target])
    return target


def drive_link(rel: str) -> str:
    """A link to view an archived file (for the owner; Buffer can't use these)."""
    return _run(["link", drive_target(rel)])


def public_remote() -> tuple[str, str]:
    remote, url = env.get("KEHAI_PUBLIC_REMOTE"), env.get("KEHAI_PUBLIC_URL")
    if not remote or not url:
        raise NotSetUp("no public host for Buffer yet: set KEHAI_PUBLIC_REMOTE and KEHAI_PUBLIC_URL (Q8)")
    rclone()
    return remote.rstrip("/"), url.rstrip("/")


def publish(path: Path, rel: str) -> str:
    """Puts a file where Buffer can fetch it; returns its public URL."""
    remote, url = public_remote()
    _run(["copyto", str(path), f"{remote}/{rel}"])
    return f"{url}/{rel}"


def unpublish(rel: str) -> None:
    remote, _ = public_remote()
    _run(["deletefile", f"{remote}/{rel}"])
