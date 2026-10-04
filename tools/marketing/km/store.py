"""The pipeline's state, in <root>/state/pipeline.db (SQLite): every video and where it is, the
queue of jobs, what went to which platform, and Telegram's bookkeeping.

n8n starts each job as its own process, sometimes at the same time; SQLite's locking keeps them
from tripping over each other, and every change is one short transaction.

A video moves: writing → awaiting (the owner's ✅/❌/✏️ in Telegram) → approved → scheduled
(in Buffer's queue) → posted; or rejected; revising while a note is applied; failed when a step
gave up (the owner is told).

A reference video the owner sent moves: studying → ready (its pattern is in the library) → used (a
week's short follows it); or dropped (❌), or failed.
"""
from __future__ import annotations

import datetime as dt
import json
import sqlite3
from contextlib import contextmanager
from pathlib import Path

STATUSES = ("writing", "awaiting", "revising", "approved", "scheduled", "posted", "rejected", "failed")
REF_STATUSES = ("studying", "ready", "used", "dropped", "failed")

SCHEMA = """
CREATE TABLE IF NOT EXISTS videos (
    id TEXT PRIMARY KEY, kind TEXT NOT NULL, week TEXT, pick TEXT, status TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, step TEXT, attempts INTEGER NOT NULL DEFAULT 0, error TEXT,
    message_id INTEGER, created TEXT NOT NULL, updated TEXT NOT NULL,
    approved_at TEXT, rejected_at TEXT, scheduled_at TEXT, posted_at TEXT, archived_at TEXT, deleted_at TEXT
);
CREATE TABLE IF NOT EXISTS jobs (
    id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, video TEXT, payload TEXT NOT NULL DEFAULT '{}',
    status TEXT NOT NULL DEFAULT 'queued', attempts INTEGER NOT NULL DEFAULT 0, not_before TEXT,
    error TEXT, created TEXT NOT NULL, updated TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS posts (
    id INTEGER PRIMARY KEY AUTOINCREMENT, video TEXT NOT NULL, platform TEXT NOT NULL, lang TEXT NOT NULL,
    buffer_id TEXT, media_url TEXT, status TEXT NOT NULL, error TEXT, created TEXT NOT NULL, updated TEXT NOT NULL,
    UNIQUE (video, platform, lang)
);
CREATE TABLE IF NOT EXISTS prompts (
    message_id INTEGER PRIMARY KEY, video TEXT NOT NULL, purpose TEXT NOT NULL, created TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS refs (
    id TEXT PRIMARY KEY, status TEXT NOT NULL, note TEXT, pattern TEXT, week TEXT, pick TEXT,
    message_id INTEGER, error TEXT, created TEXT NOT NULL, updated TEXT NOT NULL
);
"""


def stamp() -> str:
    return dt.datetime.now().isoformat(timespec="seconds")


class Store:
    def __init__(self, root: Path):
        self.path = root / "state" / "pipeline.db"
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(self.path, timeout=30, isolation_level=None)
        self.db.row_factory = sqlite3.Row
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.executescript(SCHEMA)

    def close(self) -> None:
        self.db.close()

    @contextmanager
    def tx(self):
        self.db.execute("BEGIN IMMEDIATE")
        try:
            yield self.db
            self.db.execute("COMMIT")
        except BaseException:
            self.db.execute("ROLLBACK")
            raise

    # ---- videos ----------------------------------------------------------------------------

    def video(self, vid: str) -> dict | None:
        row = self.db.execute("SELECT * FROM videos WHERE id = ?", (vid,)).fetchone()
        return dict(row) if row else None

    def videos(self, *statuses: str, kind: str | None = None) -> list[dict]:
        q, args = "SELECT * FROM videos", []
        where = []
        if statuses:
            where.append(f"status IN ({','.join('?' * len(statuses))})")
            args += statuses
        if kind:
            where.append("kind = ?")
            args.append(kind)
        if where:
            q += " WHERE " + " AND ".join(where)
        return [dict(r) for r in self.db.execute(q + " ORDER BY created, id", args)]

    def video_for_pick(self, week: str, pick: str) -> dict | None:
        row = self.db.execute("SELECT * FROM videos WHERE week = ? AND pick = ?", (week, pick)).fetchone()
        return dict(row) if row else None

    def add_video(self, vid: str, kind: str, status: str = "writing", week: str | None = None, pick: str | None = None) -> None:
        now = stamp()
        with self.tx() as db:
            db.execute("INSERT OR IGNORE INTO videos (id, kind, week, pick, status, created, updated) VALUES (?,?,?,?,?,?,?)",
                       (vid, kind, week, pick, status, now, now))

    def update(self, vid: str, **fields) -> None:
        if "status" in fields and fields["status"] not in STATUSES:
            raise ValueError(f"unknown status {fields['status']!r}")
        fields["updated"] = stamp()
        cols = ", ".join(f"{k} = ?" for k in fields)
        with self.tx() as db:
            db.execute(f"UPDATE videos SET {cols} WHERE id = ?", (*fields.values(), vid))

    # ---- jobs ------------------------------------------------------------------------------

    def queue(self, kind: str, video: str | None = None, **payload) -> int:
        now = stamp()
        with self.tx() as db:
            cur = db.execute("INSERT INTO jobs (kind, video, payload, created, updated) VALUES (?,?,?,?,?)",
                             (kind, video, json.dumps(payload, ensure_ascii=False), now, now))
            return cur.lastrowid

    def take_job(self) -> dict | None:
        """The oldest job that's due, marked running (so a second worker doesn't take it too)."""
        now = stamp()
        with self.tx() as db:
            row = db.execute("SELECT * FROM jobs WHERE status = 'queued' AND (not_before IS NULL OR not_before <= ?) "
                             "ORDER BY id LIMIT 1", (now,)).fetchone()
            if not row:
                return None
            db.execute("UPDATE jobs SET status = 'running', attempts = attempts + 1, updated = ? WHERE id = ?", (now, row["id"]))
        job = dict(row)
        job["attempts"] += 1
        job["payload"] = json.loads(job["payload"])
        return job

    def finish_job(self, job_id: int, status: str = "done", error: str | None = None, retry_at: str | None = None) -> None:
        with self.tx() as db:
            if retry_at:
                db.execute("UPDATE jobs SET status = 'queued', not_before = ?, error = ?, updated = ? WHERE id = ?",
                           (retry_at, error, stamp(), job_id))
            else:
                db.execute("UPDATE jobs SET status = ?, error = ?, updated = ? WHERE id = ?", (status, error, stamp(), job_id))

    def jobs(self, *statuses: str) -> list[dict]:
        rows = self.db.execute(f"SELECT * FROM jobs WHERE status IN ({','.join('?' * len(statuses))}) ORDER BY id", statuses)
        return [dict(r) for r in rows]

    # ---- posts -----------------------------------------------------------------------------

    def post(self, video: str, platform: str, lang: str) -> dict | None:
        row = self.db.execute("SELECT * FROM posts WHERE video = ? AND platform = ? AND lang = ?", (video, platform, lang)).fetchone()
        return dict(row) if row else None

    def posts(self, video: str | None = None, status: str | None = None) -> list[dict]:
        q, args, where = "SELECT * FROM posts", [], []
        if video:
            where.append("video = ?")
            args.append(video)
        if status:
            where.append("status = ?")
            args.append(status)
        if where:
            q += " WHERE " + " AND ".join(where)
        return [dict(r) for r in self.db.execute(q + " ORDER BY id", args)]

    def set_post(self, video: str, platform: str, lang: str, status: str, **fields) -> None:
        now = stamp()
        with self.tx() as db:
            db.execute("INSERT INTO posts (video, platform, lang, status, created, updated) VALUES (?,?,?,?,?,?) "
                       "ON CONFLICT (video, platform, lang) DO UPDATE SET status = excluded.status, updated = excluded.updated",
                       (video, platform, lang, status, now, now))
            if fields:
                cols = ", ".join(f"{k} = ?" for k in fields)
                db.execute(f"UPDATE posts SET {cols} WHERE video = ? AND platform = ? AND lang = ?",
                           (*fields.values(), video, platform, lang))

    # ---- reference videos ------------------------------------------------------------------

    def add_ref(self, rid: str, note: str = "") -> None:
        now = stamp()
        with self.tx() as db:
            db.execute("INSERT OR IGNORE INTO refs (id, status, note, created, updated) VALUES (?,?,?,?,?)",
                       (rid, "studying", note, now, now))

    def ref(self, rid: str) -> dict | None:
        row = self.db.execute("SELECT * FROM refs WHERE id = ?", (rid,)).fetchone()
        return dict(row) if row else None

    def refs(self, *statuses: str) -> list[dict]:
        q, args = "SELECT * FROM refs", list(statuses)
        if statuses:
            q += f" WHERE status IN ({','.join('?' * len(statuses))})"
        return [dict(r) for r in self.db.execute(q + " ORDER BY created, id", args)]

    def update_ref(self, rid: str, **fields) -> None:
        if "status" in fields and fields["status"] not in REF_STATUSES:
            raise ValueError(f"unknown reference status {fields['status']!r}")
        fields["updated"] = stamp()
        cols = ", ".join(f"{k} = ?" for k in fields)
        with self.tx() as db:
            db.execute(f"UPDATE refs SET {cols} WHERE id = ?", (*fields.values(), rid))

    # ---- Telegram --------------------------------------------------------------------------

    def remember_prompt(self, message_id: int, video: str, purpose: str) -> None:
        with self.tx() as db:
            db.execute("INSERT OR REPLACE INTO prompts VALUES (?,?,?,?)", (message_id, video, purpose, stamp()))

    def prompt(self, message_id: int) -> dict | None:
        row = self.db.execute("SELECT * FROM prompts WHERE message_id = ?", (message_id,)).fetchone()
        return dict(row) if row else None

    def get(self, key: str, default: str | None = None) -> str | None:
        row = self.db.execute("SELECT value FROM meta WHERE key = ?", (key,)).fetchone()
        return row["value"] if row else default

    def set(self, key: str, value: str) -> None:
        with self.tx() as db:
            db.execute("INSERT INTO meta VALUES (?, ?) ON CONFLICT (key) DO UPDATE SET value = excluded.value", (key, value))
