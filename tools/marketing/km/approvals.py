"""The owner's side of the pipeline, in Telegram.

A finished short arrives as a video (the English one) with what it's about and four buttons:
✅ approve · ❌ reject · ✏️ change (the bot asks what; the reply is the note revise.py works
from) · 🇷🇺 the Russian version; ↩️ puts back the previous version once there is one. Buttons
carry the version they were sent with, so tapping an old preview does nothing. Every decision is
logged to <root>/state/approvals.jsonl (the weekly report counts them).

Commands: /status, /report, /costs, /retry <id>, /help.
"""
from __future__ import annotations

import datetime as dt
import json
from pathlib import Path

from . import alerts, edits, timeline
from .llm import ledger
from .media import NotSetUp, megabytes, preview
from .production import draft_file, edit_path
from .settings import settings
from .telegram import buttons, esc

HELP = ("<b>Kehai marketing</b>\n"
        "Previews come here with ✅ ❌ ✏️ 🇷🇺 ↩️. ✏️ asks what to change: reply to that message with your note "
        "(any language).\n/status: what's in progress · /report: the latest weekly report · /costs: this month's "
        "spend · /retry &lt;id&gt;: try a failed video again")


def log_decision(root: Path, video: str, decision: str, version: int, note: str = "") -> None:
    path = root / "state" / "approvals.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True)
    entry = {"time": dt.datetime.now().isoformat(timespec="seconds"), "video": video, "decision": decision,
             "version": version}
    if note:
        entry["note"] = note
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(entry, ensure_ascii=False) + "\n")


def spent_on(root: Path, vid: str) -> float:
    return round(sum(float(r["usd"] or 0) for r in ledger.rows(root) if r.get("ref") == vid), 2)


def caption(root: Path, video: dict) -> str:
    edit = json.loads(edit_path(root, video["id"]).read_text(encoding="utf-8"))
    draft_path = edit_path(root, video["id"]).parent / "draft.json"
    draft = json.loads(draft_path.read_text(encoding="utf-8")) if draft_path.exists() else {}
    title = edit.get("title", {})
    title = title.get("en", "") if isinstance(title, dict) else title
    langs = " + ".join(l.upper() for l in edit["languages"])
    lines = [f"<b>{esc(title or video['id'])}</b>",
             f"{edit['kind']} · {timeline.total(edit):.1f} s · {langs} · v{video['version']}"]
    if draft.get("hook"):
        lines.append(f"Hook: {esc(draft['hook'])}")
    notes = (edit.get("source") or {}).get("notes")
    if notes:
        lines.append(esc(notes))
    package = edit_path(root, video["id"]).parent / "package.json"
    if package.exists():
        yt = json.loads(package.read_text(encoding="utf-8"))["languages"].get("en", {}).get("youtube", {})
        if yt.get("title"):
            lines.append(f"YouTube title: {esc(yt['title'])}")
    lines.append(f"Claude so far: ${spent_on(root, video['id']):.2f} · id <code>{esc(video['id'])}</code>")
    return "\n".join(lines)


def keyboard(root: Path, video: dict) -> dict:
    v, vid = video["version"], video["id"]
    row2 = [("✏️ Change", f"e:{vid}:{v}"), ("🇷🇺 Russian", f"ru:{vid}:{v}")]
    if v > 1 and edits.shown(edit_path(root, vid).parent, v - 1):
        row2.append(("↩️ Undo", f"u:{vid}:{v}"))
    return buttons([[("✅ Approve", f"a:{vid}:{v}"), ("❌ Reject", f"r:{vid}:{v}")], row2])


def sendable(root: Path, path: Path) -> Path:
    """The file itself, or a smaller preview of it when it's over Telegram's limit."""
    limit = settings()["telegram"]["max_upload_mb"]
    if megabytes(path) <= limit:
        return path
    small = root / "drafts" / "previews" / path.name
    if not small.exists() or small.stat().st_mtime < path.stat().st_mtime:
        preview(path, small, 540)
    return small


class Approvals:
    def __init__(self, root: Path, store, bot):
        self.root, self.store, self.bot = root, store, bot

    # ---- sending ---------------------------------------------------------------------------

    def present(self, video: dict) -> None:
        """Sends the newest version of a video for approval."""
        vid = video["id"]
        video = self.store.video(vid)
        if video["message_id"]:                    # sent before: this is a new version
            self.bot.set_buttons(video["message_id"], None)
            self.store.update(vid, version=video["version"] + 1)
            video = self.store.video(vid)
        edits.remember_shown(edit_path(self.root, vid).parent, video["version"])
        film = draft_file(self.root, vid, "en")
        try:
            message = self.bot.video(sendable(self.root, film), caption(self.root, video), keyboard(self.root, video))
        except NotSetUp as e:
            message = self.bot.text(caption(self.root, video) + f"\n(the video is {megabytes(film):.0f} MB: {esc(e)})",
                                    keyboard(self.root, video))
        self.store.update(vid, status="awaiting", message_id=message, attempts=0, error=None)

    # ---- receiving -------------------------------------------------------------------------

    def handle(self, update: dict) -> str:
        if "callback_query" in update:
            return self.tap(update["callback_query"])
        msg = update.get("message") or {}
        reply_to = (msg.get("reply_to_message") or {}).get("message_id")
        text = (msg.get("text") or "").strip()
        if reply_to:
            prompt = self.store.prompt(reply_to)
            if prompt and text:
                return self.note(prompt, text)
        if text.startswith("/"):
            return self.command(text)
        if text:
            self.bot.text("To change a video, tap ✏️ under its preview and reply to my question. /help")
            return "text outside a prompt"
        return "ignored"

    def tap(self, cb: dict) -> str:
        data = cb.get("data", "")
        parts = data.split(":")
        act = parts[0]
        if act == "lg":
            from .longform import Long
            return Long(self.root, self.store, self.bot).tap(cb, parts)
        if act == "pt":
            from .playtest import notify
            return notify.tap(self.bot, cb, parts)
        if act == "pd" and len(parts) == 4:
            from .publishing import NAMES, mark_posted
            _, vid, service, lang = parts
            done = mark_posted(self.store, vid, service, lang)
            self.bot.answer(cb["id"], f"Marked as posted on {NAMES.get(service, service)}")
            self.bot.set_buttons(cb["message"]["message_id"], None)
            if done:
                self.bot.text(f"🎉 <b>{esc(vid)}</b> is up everywhere.")
            return f"{vid}: posted on {service}"
        if len(parts) != 3 or not parts[2].isdigit():
            self.bot.answer(cb["id"], "?")
            return f"unknown button {data}"
        vid, version = parts[1], int(parts[2])
        video = self.store.video(vid)
        if not video:
            self.bot.answer(cb["id"], "That video isn't in the pipeline any more")
            return f"{vid}: gone"
        if version != video["version"]:
            self.bot.answer(cb["id"], f"That's v{version}; the newest is v{video['version']}")
            return f"{vid}: stale button v{version}"
        if act == "ru":
            film = draft_file(self.root, vid, "ru")
            self.bot.answer(cb["id"], "Sending the Russian version" if film.exists() else "No Russian version yet")
            if film.exists():
                self.bot.video(sendable(self.root, film), f"🇷🇺 {esc(vid)} v{version}")
            return f"{vid}: russian sent"
        if video["status"] != "awaiting":
            self.bot.answer(cb["id"], f"It's {video['status']} now")
            return f"{vid}: not awaiting ({video['status']})"
        if act == "a":
            now = dt.datetime.now().isoformat(timespec="seconds")
            self.store.update(vid, status="approved", approved_at=now)
            log_decision(self.root, vid, "approved", version)
            self.bot.answer(cb["id"], "Approved ✅")
            self.bot.set_buttons(cb["message"]["message_id"], None)
            days = "/".join(d.capitalize() for d in settings()["posting"]["days"])
            self.bot.text(f"✅ <b>{esc(vid)}</b> approved: it goes out on the next posting day ({days}).")
            return f"{vid}: approved"
        if act == "r":
            now = dt.datetime.now().isoformat(timespec="seconds")
            self.store.update(vid, status="rejected", rejected_at=now)
            log_decision(self.root, vid, "rejected", version)
            self.bot.answer(cb["id"], "Rejected")
            self.bot.set_buttons(cb["message"]["message_id"], None)
            days = settings()["retention"]["rejected_days"]
            self.bot.text(f"❌ <b>{esc(vid)}</b> rejected. Its files go after {days} days.")
            return f"{vid}: rejected"
        if act == "e":
            self.bot.answer(cb["id"], "What should change?")
            asked = self.bot.text(f"✏️ What should change in <b>{esc(vid)}</b>? Reply to this message.",
                                  force_reply="What should change?")
            self.store.remember_prompt(asked, vid, "revise")
            return f"{vid}: asked for a note"
        if act == "u":
            self.store.update(vid, status="revising")
            self.store.queue("undo", vid)
            log_decision(self.root, vid, "undo", version)
            self.bot.answer(cb["id"], "Putting the previous version back")
            self.bot.set_buttons(cb["message"]["message_id"], None)
            self.bot.text(f"↩️ Putting back the previous version of <b>{esc(vid)}</b>; it comes here when it's ready.")
            return f"{vid}: undo queued"
        self.bot.answer(cb["id"], "?")
        return f"unknown action {act}"

    def note(self, prompt: dict, text: str) -> str:
        vid = prompt["video"]
        if prompt["purpose"].startswith("long:"):
            from .longform import Long
            return Long(self.root, self.store, self.bot).note(prompt, text)
        video = self.store.video(vid)
        if not video or video["status"] != "awaiting":
            self.bot.text(f"<b>{esc(vid)}</b> isn't waiting for changes any more ({video['status'] if video else 'gone'}).")
            return f"{vid}: note too late"
        self.store.update(vid, status="revising")
        self.store.queue("revise", vid, note=text)
        log_decision(self.root, vid, "revised", video["version"], text)
        if video["message_id"]:
            self.bot.set_buttons(video["message_id"], None)
        self.bot.text(f"✏️ Got it. Revising <b>{esc(vid)}</b>; the new version comes here when it's ready.")
        return f"{vid}: revise queued"

    def command(self, text: str) -> str:
        cmd, _, arg = text.partition(" ")
        cmd = cmd.split("@")[0].lower()
        if cmd in ("/start", "/help"):
            self.bot.text(HELP)
        elif cmd == "/status":
            self.bot.text(self.status())
        elif cmd == "/costs":
            self.bot.text(f"Claude this month: ${ledger.month_spent(self.root):.2f} of ${ledger.cap():.2f}")
        elif cmd == "/report":
            reports = sorted((self.root / "reports").glob("*.md"))
            self.bot.text(reports[-1].read_text(encoding="utf-8") if reports else "No weekly report yet.")
        elif cmd == "/retry":
            video = self.store.video(arg.strip())
            if not video or video["status"] != "failed":
                self.bot.text("Give the id of a failed video: /retry &lt;id&gt; (see /status).")
            else:
                self.store.update(video["id"], status="writing", attempts=0, error=None)
                self.bot.text(f"🔁 <b>{esc(video['id'])}</b> goes again on the next run.")
        else:
            self.bot.text("I don't know that one. /help")
        return f"command {cmd}"

    def status(self) -> str:
        lines = ["<b>In progress</b>"]
        for v in self.store.videos("writing", "revising", "awaiting", "approved", "scheduled", "failed"):
            extra = f" at {v['step']}" if v["status"] in ("writing", "failed") and v["step"] else ""
            err = f" — {esc(v['error'][:120])}" if v["status"] == "failed" and v["error"] else ""
            lines.append(f"• <code>{esc(v['id'])}</code> {v['status']}{extra}{err}")
        if len(lines) == 1:
            lines.append("nothing")
        queued = self.store.jobs("queued", "running")
        if queued:
            lines.append(f"Jobs waiting: {len(queued)}")
        pending = alerts.pending(self.root)
        if pending:
            lines.append(f"Alerts not sent yet: {len(pending)}")
        lines.append(f"Claude this month: ${ledger.month_spent(self.root):.2f} of ${ledger.cap():.2f}")
        return "\n".join(lines)


def send_alerts(root: Path, bot) -> int:
    """Sends the alerts not sent yet and marks them sent."""
    path = alerts.alerts_file(root)
    if not path.exists():
        return 0
    entries = [json.loads(l) for l in path.read_text(encoding="utf-8").splitlines() if l.strip()]
    sent = 0
    for e in entries:
        if not e.get("sent"):
            bot.text(f"⚠️ {esc(e['message'])}")
            e["sent"] = True
            sent += 1
    if sent:
        tmp = path.with_suffix(".tmp")
        tmp.write_text("".join(json.dumps(e, ensure_ascii=False) + "\n" for e in entries), encoding="utf-8")
        tmp.replace(path)
    return sent
