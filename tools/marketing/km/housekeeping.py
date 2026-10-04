"""Daily upkeep of the working folder (pipeline.json's `storage` and `retention`, Q12).

- Archive to Drive: every approved, scheduled or posted video (its drafts and its edit folder),
  and the shift recordings whose moments were picked.
- Public copies (for Buffer) come down a few days after their post went out.
- Retention: rejected videos go after `rejected_days`; posted ones' media after `posted_days`
  once archived; shots after `shots_days` once their video is decided; recordings `krec_days`
  after archiving; reference videos (with their frames and audio, not their study or pattern)
  `references_days` after they arrived, never archived (they're someone else's); logs after
  `logs_days`. Nothing else that isn't archived is deleted, except rejects. With
  `retention.apply` false (until the owner answers Q12) this only reports.
- Storage: the working folder against its budget, the disk against its floor; the owner hears
  when either is crossed, and production waits (`too_full`).
"""
from __future__ import annotations

import datetime as dt
import json
import shutil
from pathlib import Path

from . import alerts, media, moments
from .settings import settings


def size(path: Path) -> int:
    if path.is_file():
        return path.stat().st_size
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file() and not f.is_symlink()) if path.exists() else 0


def too_full(root: Path) -> str | None:
    """Why heavy work should wait, or None."""
    s = settings()["storage"]
    used = size(root) / 1e9
    free = shutil.disk_usage(root if root.exists() else Path.home()).free / 1e9
    if free < s["free_floor_gb"]:
        return f"only {free:.1f} GB free on the disk (the floor is {s['free_floor_gb']} GB)"
    if used > s["working_gb"]:
        return f"the working folder is {used:.1f} GB (the budget is {s['working_gb']} GB)"
    return None


def older(stamp: str | None, days: int, today: dt.datetime) -> bool:
    return bool(stamp) and dt.datetime.fromisoformat(stamp) <= today - dt.timedelta(days=days)


class Keeper:
    def __init__(self, root: Path, store, apply: bool | None = None, today: dt.datetime | None = None):
        self.root, self.store = root, store
        self.rules = settings()["retention"]
        self.apply = self.rules["apply"] if apply is None else apply
        self.today = today or dt.datetime.now()
        self.freed = 0
        self.lines: list[str] = []

    def remove(self, path: Path, why: str) -> None:
        """Deletes a file or folder inside the working folder (or reports it)."""
        resolved = path.resolve()
        inside = resolved.is_relative_to(self.root.resolve()) or resolved.is_relative_to(moments.records().resolve())
        if not inside or not path.exists():
            return
        n = size(path)
        self.freed += n
        self.lines.append(f"{'deleted' if self.apply else 'would delete'} {path.relative_to(path.parents[1])} ({n / 1e6:.1f} MB): {why}")
        if self.apply:
            shutil.rmtree(path) if path.is_dir() else path.unlink()

    def video_media(self, vid: str) -> list[Path]:
        return [*self.root.glob(f"drafts/{vid}.*"), *self.root.glob(f"drafts/previews/{vid}.*"), self.root / "audio" / vid]

    def shots(self, video: dict) -> Path | None:
        if video.get("week") and video.get("pick"):
            return self.root / "shots" / video["week"] / video["pick"]
        return None

    # ---- the parts -------------------------------------------------------------------------

    def archive(self) -> None:
        for video in self.store.videos("approved", "scheduled", "posted"):
            if video["archived_at"]:
                continue
            base = f"archive/{video['week'] or video['kind']}/{video['id']}"
            try:
                for f in self.root.glob(f"drafts/{video['id']}.*.mp4"):
                    media.archive(f, f"{base}/{f.name}")
                folder = self.root / "edits" / video["id"]
                for f in folder.glob("*.json"):
                    media.archive(f, f"{base}/edit/{f.name}")
            except media.NotSetUp as e:
                self.not_set_up(str(e))
                return
            self.store.update(video["id"], archived_at=dt.datetime.now().isoformat(timespec="seconds"))
            self.lines.append(f"archived {video['id']} to Drive")
        for plan in self.root.glob("plans/*/picks.json"):
            data = json.loads(plan.read_text(encoding="utf-8"))
            for stem in {p["moment"].split("#")[0] for p in data["answer"]["shorts"]}:
                key = f"krec.archived.{stem}"
                krec = moments.records() / f"{stem}.krec"
                if self.store.get(key) or not krec.exists():
                    continue
                try:
                    media.archive(krec, f"recordings/{krec.name}")
                    marks = krec.with_suffix(".markers.json")
                    if marks.exists():
                        media.archive(marks, f"recordings/{marks.name}")
                except media.NotSetUp as e:
                    self.not_set_up(str(e))
                    return
                self.store.set(key, dt.datetime.now().isoformat(timespec="seconds"))
                self.lines.append(f"archived {krec.name} to Drive")

    def not_set_up(self, why: str) -> None:
        week = self.today.strftime("%G-W%V")
        if self.store.get("archive.not_set_up") != week:
            self.store.set("archive.not_set_up", week)
            alerts.alert(self.root, "archive", f"Nothing is archived to Drive yet: {why}. Retention keeps everything until it is.")
        self.lines.append(f"no archive: {why}")

    def unpublish(self) -> None:
        r = self.rules
        for video in self.store.videos("posted"):
            if not older(video["posted_at"], r["posted_days"], self.today) or self.store.get(f"unpublished.{video['id']}"):
                continue
            for p in self.store.posts(video["id"]):
                if p["media_url"]:
                    try:
                        media.unpublish(f"{video['id']}/{video['id']}.{p['lang']}.mp4")
                    except (media.NotSetUp, RuntimeError):
                        pass
            self.store.set(f"unpublished.{video['id']}", self.today.isoformat(timespec="seconds"))
            self.lines.append(f"took down the public copy of {video['id']}")

    def retention(self) -> None:
        r = self.rules
        for video in self.store.videos("rejected"):
            if video["deleted_at"] or not older(video["rejected_at"], r["rejected_days"], self.today):
                continue
            for p in self.video_media(video["id"]):
                self.remove(p, f"rejected {video['rejected_at'][:10]}")
            shots = self.shots(video)
            if shots:
                self.remove(shots, "its video was rejected")
            if self.apply:
                self.store.update(video["id"], deleted_at=self.today.isoformat(timespec="seconds"))
        for video in self.store.videos("posted"):
            if video["deleted_at"] or not video["archived_at"] or not older(video["posted_at"], r["posted_days"], self.today):
                continue
            for p in self.video_media(video["id"]):
                self.remove(p, f"posted {video['posted_at'][:10]} and archived")
            if self.apply:
                self.store.update(video["id"], deleted_at=self.today.isoformat(timespec="seconds"))
        for video in self.store.videos("approved", "scheduled", "posted"):
            decided = video["approved_at"]
            shots = self.shots(video)
            if shots and older(decided, r["shots_days"], self.today) and video["archived_at"]:
                self.remove(shots, f"its video was approved {decided[:10]}")
        for stem_key in self._archived_krecs():
            stem, when = stem_key
            if older(when, r["krec_days"], self.today):
                self.remove(moments.records() / f"{stem}.krec", f"archived {when[:10]}")
        for ref in self.store.refs("ready", "used", "dropped", "failed"):
            if older(ref["created"], r.get("references_days", 30), self.today):
                here = self.root / "references" / ref["id"]
                for part in [*here.glob("source.*"), here / "frames", here / "audio.wav"]:
                    self.remove(part, f"a reference from {ref['created'][:10]}")
        for log in [*self.root.glob("logs/jobs/*.log"), *self.root.glob("logs/scheduled/*.log"),
                    *self.root.glob("logs/render_*.log"), *self.root.glob("logs/llm_requests/*.json"),
                    *self.root.glob("logs/buffer_requests/*.json")]:
            if dt.datetime.fromtimestamp(log.stat().st_mtime) <= self.today - dt.timedelta(days=r["logs_days"]):
                self.remove(log, f"older than {r['logs_days']} days")

    def _archived_krecs(self) -> list[tuple[str, str]]:
        rows = self.store.db.execute("SELECT key, value FROM meta WHERE key LIKE 'krec.archived.%'").fetchall()
        return [(row["key"].removeprefix("krec.archived."), row["value"]) for row in rows]

    def storage(self) -> None:
        why = too_full(self.root)
        if why:
            day = self.today.date().isoformat()
            if self.store.get("storage.full") != day:
                self.store.set("storage.full", day)
                alerts.alert(self.root, "storage", f"Production waits: {why}.")
            self.lines.append(f"storage: {why}")

    def run(self) -> list[str]:
        self.archive()
        self.unpublish()
        self.retention()
        self.storage()
        if self.freed:
            self.lines.append(f"{'freed' if self.apply else 'would free'} {self.freed / 1e6:.0f} MB"
                              + ("" if self.apply else " (retention.apply is off until Q12 is answered)"))
        return self.lines
