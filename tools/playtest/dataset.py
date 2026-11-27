#!/usr/bin/env python3
"""Download a fixed snapshot of the playtest sessions that may train Karen (playtest v2, C3.7).

For the Windows training PC (and any computer): Python 3.10+, nothing to install, no Unity,
launchd, Telegram or Claude. It uses only the read-only dataset key, so it can't write, delete
or publish anything, and it never starts training.

    python dataset.py list    [--round R] [--build B]
    python dataset.py fetch   <folder> [--round R] [--build B] [--session ID ...]
    python dataset.py reconcile <folder>

On PowerShell the same, with `py` for `python`:

    py tools\\playtest\\dataset.py fetch D:\\kehai\\round1 --round round1

Settings: KEHAI_PLAYTEST_UPLOAD_URL and KEHAI_PLAYTEST_DATASET_KEY, from the environment or from
tools/playtest/.env (never put the key in git or in a message).

What `fetch` leaves in <folder>:

    snapshot.json            the fixed list: each session's id, round, build, consent, schema,
                             size and SHA-256, and what became of it here (done or rejected, why)
    zips/<id>.zip            each zip, kept as downloaded
    sessions/<id>/           each zip unpacked, once its hash and every path inside are checked

Run `fetch` again on the same folder and it carries on with the same list: what's done is
skipped (checked by its hash), what was cut off is downloaded again, nothing is fetched twice.
A new list needs a new folder, or --new. Every `fetch` first reconciles: sessions removed in the
cloud (by a deletion request, or Q5's six months) are removed here too, and so are sessions past
their expiry date, before anything else uses the folder.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import http.client
import re
import json
import os
import shutil
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

HERE = Path(__file__).resolve().parent
SESSION_ID = re.compile(r"^[A-Za-z0-9-]{1,40}/(?:[A-Z0-9-]{2,16}|[A-F0-9]{32})/\d{8}_\d{6}(?:-[a-f0-9]{32})?$")
BIGGEST_FILE = 200 * 1024 * 1024   # one file inside a zip, unpacked


class DatasetError(Exception):
    """Something the person running it should read."""


# ---- settings and the service ------------------------------------------------------------------

def read_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if path.is_file():
        for raw in path.read_text(encoding="utf-8").splitlines():
            line = raw.strip()
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                values[key.strip()] = value.strip().strip("'\"")
    return values


def settings(url: str | None, key: str | None) -> tuple[str, str]:
    env = read_env(HERE / ".env")
    url = url or os.environ.get("KEHAI_PLAYTEST_UPLOAD_URL") or env.get("KEHAI_PLAYTEST_UPLOAD_URL", "")
    key = key or os.environ.get("KEHAI_PLAYTEST_DATASET_KEY") or env.get("KEHAI_PLAYTEST_DATASET_KEY", "")
    if not url or not key:
        raise DatasetError("Set KEHAI_PLAYTEST_UPLOAD_URL and KEHAI_PLAYTEST_DATASET_KEY (environment or tools/playtest/.env).")
    return url.rstrip("/"), key


class Service:
    """The upload service's read-only side. Only GETs, with the dataset key."""

    def __init__(self, url: str, key: str, timeout: float = 120):
        self.url, self.key, self.timeout = url.rstrip("/"), key, timeout

    def _open(self, path: str):
        request = urllib.request.Request(self.url + path, headers={"Authorization": f"Bearer {self.key}", "User-Agent": "kehai-dataset"})
        try:
            return urllib.request.urlopen(request, timeout=self.timeout)
        except urllib.error.HTTPError as e:
            if e.code == 401:
                raise DatasetError("The service refused the key (401): check KEHAI_PLAYTEST_DATASET_KEY.") from e
            raise DatasetError(f"{path}: the service answered {e.code}") from e
        except urllib.error.URLError as e:
            raise DatasetError(f"Couldn't reach {self.url}: {e.reason}") from e

    def json(self, path: str) -> dict:
        with self._open(path) as r:
            return json.loads(r.read().decode("utf-8"))

    def manifest(self, round_: str | None = None, build: str | None = None) -> dict:
        query = urllib.parse.urlencode({k: v for k, v in (("round", round_), ("build", build)) if v})
        return self.json("/dataset/manifest" + ("?" + query if query else ""))

    def deletions(self, since: str = "") -> dict:
        return self.json("/dataset/deletions?" + urllib.parse.urlencode({"since": since}))

    def download(self, session_id: str, target: Path) -> None:
        part = target.with_name(target.name + ".part")
        target.parent.mkdir(parents=True, exist_ok=True)
        try:
            with self._open(f"/dataset/sessions/{session_id}.zip") as r, part.open("wb") as out:
                shutil.copyfileobj(r, out, 1024 * 1024)
        except (OSError, http.client.HTTPException) as e:
            raise DatasetError(f"the download stopped part way ({e.__class__.__name__}); run fetch again") from e
        part.replace(target)


# ---- the folder ---------------------------------------------------------------------------------

def flat(session_id: str) -> str:
    return session_id.replace("/", "_")


def now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def unsafe_entry(name: str) -> bool:
    """A path inside a zip that could land outside its folder (or is a link, on Windows)."""
    if not name or "\\" in name or name.startswith("/") or ":" in name.split("/")[0]:
        return True
    parts = PurePosixPath(name).parts
    return any(p in ("..", "") for p in parts)


def unpack(zip_path: Path, folder: Path) -> None:
    """Unpacks a checked zip into `folder`, refusing it whole if any entry is unsafe."""
    with zipfile.ZipFile(zip_path) as z:
        entries = z.infolist()
        bad = [i.filename for i in entries if unsafe_entry(i.filename) or i.file_size > BIGGEST_FILE]
        if bad:
            raise DatasetError(f"unsafe or oversized paths inside: {', '.join(bad[:3])}")
        if not any(PurePosixPath(i.filename).name == "session.jsonl" for i in entries):
            raise DatasetError("no session.jsonl inside")
        part = folder.with_name(folder.name + ".part")
        shutil.rmtree(part, ignore_errors=True)
        part.mkdir(parents=True)
        for info in entries:
            if info.is_dir():
                continue
            target = part / PurePosixPath(info.filename)
            target.parent.mkdir(parents=True, exist_ok=True)
            with z.open(info) as src, target.open("wb") as dst:
                shutil.copyfileobj(src, dst)
        shutil.rmtree(folder, ignore_errors=True)
        part.replace(folder)


def load_snapshot(folder: Path) -> dict | None:
    path = folder / "snapshot.json"
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


def save_snapshot(folder: Path, snapshot: dict) -> None:
    folder.mkdir(parents=True, exist_ok=True)
    tmp = folder / "snapshot.json.part"
    tmp.write_text(json.dumps(snapshot, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    tmp.replace(folder / "snapshot.json")


def remove_local(folder: Path, session_id: str) -> None:
    (folder / "zips" / (flat(session_id) + ".zip")).unlink(missing_ok=True)
    (folder / "zips" / (flat(session_id) + ".zip.part")).unlink(missing_ok=True)
    shutil.rmtree(folder / "sessions" / flat(session_id), ignore_errors=True)


def reconcile(service: Service, folder: Path, log=print) -> int:
    """Removes local copies of sessions deleted or expired in the cloud, and any past their date."""
    snapshot = load_snapshot(folder)
    if snapshot is None:
        return 0
    gone = {d["id"]: d.get("reason", "deleted") for d in service.deletions(snapshot.get("reconciled", "")).get("deletions", [])}
    today = now_iso()
    removed = 0
    for s in snapshot["sessions"]:
        reason = gone.get(s["id"]) or ("expired" if s.get("expires", "9999") <= today else None)
        if reason and s.get("status") != "removed":
            remove_local(folder, s["id"])
            s["status"], s["removed"], s["why"] = "removed", today, reason
            removed += 1
            log(f"removed {s['id']} ({reason})")
    snapshot["reconciled"] = today
    save_snapshot(folder, snapshot)
    return removed


def fetch(service: Service, folder: Path, round_=None, build=None, only=None, new=False, log=print) -> dict:
    snapshot = load_snapshot(folder)
    if snapshot is not None and new:
        snapshot = None
    if snapshot is None:
        manifest = service.manifest(round_, build)
        sessions = [s for s in manifest.get("sessions", []) if SESSION_ID.match(s.get("id", ""))]
        if only:
            wanted = set(only)
            sessions = [s for s in sessions if s["id"] in wanted]
            missing = wanted - {s["id"] for s in sessions}
            if missing:
                log(f"not in the dataset (no consent to training, deleted or expired): {', '.join(sorted(missing))}")
        snapshot = {
            "made": now_iso(), "service_generated": manifest.get("generated"),
            "filters": {"round": round_, "build": build, "sessions": sorted(only) if only else None},
            "sessions": [{**s, "status": "waiting"} for s in sorted(sessions, key=lambda s: s["id"])],
        }
        save_snapshot(folder, snapshot)
        log(f"snapshot of {len(sessions)} sessions in {folder / 'snapshot.json'}")
    else:
        log(f"carrying on with the snapshot in {folder / 'snapshot.json'} ({len(snapshot['sessions'])} sessions)")
        reconcile(service, folder, log)
        snapshot = load_snapshot(folder)

    for s in snapshot["sessions"]:
        if s.get("status") in ("done", "removed"):
            if s["status"] == "done" and not (folder / "sessions" / flat(s["id"]) / ".verified").is_file():
                s["status"] = "waiting"   # its folder went missing: fetch it again
            else:
                continue
        zip_path = folder / "zips" / (flat(s["id"]) + ".zip")
        try:
            if not (zip_path.is_file() and sha256_of(zip_path) == s.get("zip_sha256")):
                service.download(s["id"], zip_path)
            digest = sha256_of(zip_path)
            if digest != s.get("zip_sha256"):
                zip_path.unlink(missing_ok=True)
                raise DatasetError(f"its hash is {digest[:12]}…, the manifest says {str(s.get('zip_sha256'))[:12]}…")
            target = folder / "sessions" / flat(s["id"])
            unpack(zip_path, target)
            (target / ".verified").write_text(digest + "\n", encoding="utf-8")
            s["status"] = "done"
            s.pop("why", None)
            log(f"done {s['id']}")
        except (DatasetError, zipfile.BadZipFile) as e:
            s["status"], s["why"] = "rejected", str(e)
            log(f"rejected {s['id']}: {e}")
        save_snapshot(folder, snapshot)   # after each session, so a stop loses at most one
    return snapshot


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    p.add_argument("--url"), p.add_argument("--key")
    sub = p.add_subparsers(dest="command", required=True)
    ls = sub.add_parser("list", help="the sessions that may train Karen")
    ls.add_argument("--round"), ls.add_argument("--build")
    fe = sub.add_parser("fetch", help="download a fixed snapshot into a folder")
    fe.add_argument("folder", type=Path)
    fe.add_argument("--round"), fe.add_argument("--build")
    fe.add_argument("--session", action="append", help="only these session ids (repeatable)")
    fe.add_argument("--new", action="store_true", help="make a new list instead of carrying on")
    re_ = sub.add_parser("reconcile", help="remove local copies of sessions removed in the cloud")
    re_.add_argument("folder", type=Path)
    args = p.parse_args(argv)
    try:
        service = Service(*settings(args.url, args.key))
        if args.command == "list":
            for s in service.manifest(args.round, args.build).get("sessions", []):
                print(f"{s['id']}  {s.get('build', '')}  {s.get('zip_bytes', 0) / 1048576:.1f} MB  "
                      f"{'complete' if s.get('complete') else 'zip only'}  until {s.get('expires', '')[:10]}")
        elif args.command == "fetch":
            snapshot = fetch(service, args.folder, args.round, args.build, args.session, args.new)
            counts: dict[str, int] = {}
            for s in snapshot["sessions"]:
                counts[s["status"]] = counts.get(s["status"], 0) + 1
            print(", ".join(f"{n} {k}" for k, n in sorted(counts.items())) or "nothing to fetch")
            return 1 if counts.get("rejected") or counts.get("waiting") else 0
        else:
            print(f"{reconcile(service, args.folder)} removed")
    except DatasetError as e:
        print(f"dataset: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
