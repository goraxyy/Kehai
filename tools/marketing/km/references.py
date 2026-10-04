"""Reference videos the owner sends in Telegram, and what becomes of them (BUILD_PLAN.md, Phase 8).

1. The owner sends a video (as a video, under 20 MB; its caption is their note). The Telegram job
   saves it to references/<id>/ and queues a `reference` job.
2. The work job runs study_reference.py on it: Claude studies it and its pattern goes into the
   library. The pattern comes back to Telegram: what the video does, the recipe for Kehai, what
   the editor can't do yet. ✏️ studies it again with the owner's correction; ❌ drops it.
3. The week's picking gives the oldest ready reference (`references.per_week` of them) one of the
   week's shorts, made in its pattern; if no moment that week fits, the owner hears what to play
   and the reference waits for the next week.
"""
from __future__ import annotations

import datetime as dt
import json
from pathlib import Path

from . import env, patterns, production
from .cli import write_json
from .settings import settings
from .telegram import DOWNLOAD_MB, TelegramError, buttons, clip, esc

VIDEO = (".mp4", ".mov", ".m4v", ".webm", ".mkv", ".gif")
DAYS = {"mon": "Monday", "tue": "Tuesday", "wed": "Wednesday", "thu": "Thursday", "fri": "Friday", "sat": "Saturday",
        "sun": "Sunday"}
SUFFIX = {"video/mp4": ".mp4", "video/quicktime": ".mov", "video/webm": ".webm", "video/x-matroska": ".mkv",
          "video/x-m4v": ".m4v"}


def folder(root: Path, rid: str) -> Path:
    return root / "references" / rid


def new_id(root: Path) -> str:
    base = f"ref-{dt.datetime.now():%Y%m%d-%H%M%S}"
    rid, n = base, 1
    while folder(root, rid).exists():
        n += 1
        rid = f"{base}-{n}"
    return rid


def media_of(msg: dict) -> dict | None:
    """The video in a message: a video, a GIF (sent as mp4), a round video, or a video file."""
    for key in ("video", "animation", "video_note"):
        if msg.get(key):
            return msg[key]
    doc = msg.get("document") or {}
    return doc if str(doc.get("mime_type", "")).startswith("video/") else None


def per_week() -> int:
    return int(settings().get("references", {}).get("per_week", 1))


def receive(root: Path, store, bot, msg: dict) -> str:
    media = media_of(msg)
    size = media.get("file_size") or 0
    if size > DOWNLOAD_MB * 1024 * 1024:
        bot.text(f"That video is {size / 1e6:.0f} MB, and bots can only fetch {DOWNLOAD_MB} MB. Send it as a video "
                 "(not as a file) so Telegram compresses it, or trim it.")
        return "reference too big"
    rid = new_id(root)
    here = folder(root, rid)
    suffix = SUFFIX.get(media.get("mime_type", ""), ".mp4")
    try:
        bot.download(media["file_id"], here / f"source{suffix}")
    except (TelegramError, OSError) as e:
        bot.text(f"I couldn't fetch that video ({esc(e)}). Try sending it again.")
        return f"reference download failed: {e}"
    note = (msg.get("caption") or "").strip()
    write_json(here / "meta.json", {"id": rid, "received": dt.datetime.now().isoformat(timespec="seconds"),
                                    "from": "telegram", "note": note, "corrections": [],
                                    "telegram": {"message_id": msg.get("message_id"), "seconds": media.get("duration"),
                                                 "mb": round(size / 1e6, 1)}})
    store.add_ref(rid, note)
    store.queue("reference", rid)
    waits = "" if (env.get("ANTHROPIC_API_KEY") or env.get("KEHAI_LLM_REPLAY")) else \
        "\nIt waits for the Claude key: studying needs it."
    about = [f"{media['duration']} s"] if media.get("duration") else []
    about += ["with your note"] if note else []
    bot.text(f"📥 Got it{' (' + ', '.join(about) + ')' if about else ''}. I'm studying it; its pattern comes here "
             f"when it's done.{waits}")
    return f"reference {rid} received"


def summary(root: Path, store, pattern: dict, rid: str) -> str:
    s = pattern["study"]
    hook = s["hook"]
    lines = [f"🧩 <b>{esc(pattern['title'])}</b> · <code>{esc(pattern['name'])}</code>", esc(s["summary"]),
             f"<b>Hook</b> (0–{hook['until']:.0f} s, {esc(hook['kind'])}): "
             f"{esc(hook['on_screen'] or hook['spoken'] or hook['picture'])}",
             f"<b>Pacing</b>: {s['pacing']['length']:.0f} s, {s['pacing']['shots']} shots of about "
             f"{s['pacing']['average_shot']:.1f} s; {esc(s['pacing']['rhythm'])}",
             f"<b>Format</b>: {esc(s['format']['layout'])} {esc(s['format']['text'])}",
             f"<b>For Kehai</b>: {esc(s['kehai']['idea'])}"]
    if s["missing"]:
        lines.append("<b>The editor can't do yet</b>: " + "; ".join(
            f"{esc(m['element'])} (closest: {esc(m['closest'])})" for m in s["missing"]))
    ahead = [r for r in store.refs("ready") if r["id"] != rid and r["created"] < (store.ref(rid) or {}).get("created", "")]
    day = DAYS.get(settings()["production"]["pick_day"], settings()["production"]["pick_day"])
    lines.append(f"It goes into {day}'s picks as one of the week's shorts"
                 + (f", after {len(ahead)} other reference{'s' if len(ahead) > 1 else ''}" if ahead else "") + ".")
    return clip("\n".join(lines), 4000)


def keyboard(rid: str) -> dict:
    return buttons([[("✏️ Not quite", f"rf:{rid}:e"), ("❌ Don't use", f"rf:{rid}:x")]])


def work(root: Path, store, bot, job: dict) -> tuple[int, str]:
    """The `reference` job: study it (again, with a note) and show the owner the pattern."""
    rid = job["video"]
    note = job["payload"].get("note", "")
    r = production.script(root, "study_reference.py", rid, *(["--note", note] if note else []))
    if not r.ok:
        return r.code, r.output.splitlines()[-1] if r.output else f"exit {r.code}"
    meta = json.loads((folder(root, rid) / "meta.json").read_text(encoding="utf-8"))
    pattern = patterns.load(root, meta.get("pattern", ""))
    if not pattern:
        return 1, f"{rid}: studied, but its pattern isn't in the library"
    old = store.ref(rid) or {}
    if old.get("message_id"):
        bot.set_buttons(old["message_id"], None)
    message = bot.text(summary(root, store, pattern, rid), keyboard(rid))
    store.update_ref(rid, status="ready", pattern=pattern["name"], message_id=message, error=None)
    return 0, f"{rid}: pattern {pattern['name']}"


def tap(root: Path, store, bot, cb: dict, parts: list[str]) -> str:
    if len(parts) != 3:
        bot.answer(cb["id"], "?")
        return f"unknown button {':'.join(parts)}"
    _, rid, act = parts
    ref = store.ref(rid)
    if not ref:
        bot.answer(cb["id"], "That reference isn't here any more")
        return f"{rid}: gone"
    if act == "e":
        if ref["status"] != "ready":
            bot.answer(cb["id"], f"It's {ref['status']} now")
            return f"{rid}: not ready ({ref['status']})"
        bot.answer(cb["id"], "What did I miss?")
        asked = bot.text("✏️ What did I get wrong, or what should the pattern take from it? Reply to this message.",
                         force_reply="What should it take from the video?")
        store.remember_prompt(asked, rid, "ref")
        return f"{rid}: asked for a correction"
    if act == "x":
        if ref["pattern"]:
            patterns.set_status(root, ref["pattern"], "dropped")
        if ref["status"] != "used":
            store.update_ref(rid, status="dropped")
        bot.answer(cb["id"], "Dropped")
        bot.set_buttons(cb["message"]["message_id"], None)
        bot.text("🗑 Dropped: no short will follow it, and it's out of the pattern library.")
        return f"{rid}: dropped"
    bot.answer(cb["id"], "?")
    return f"{rid}: unknown action {act}"


def note(root: Path, store, bot, prompt: dict, text: str) -> str:
    rid = prompt["video"]
    ref = store.ref(rid)
    if not ref or ref["status"] != "ready":
        bot.text(f"That reference isn't waiting for corrections any more ({ref['status'] if ref else 'gone'}).")
        return f"{rid}: correction too late"
    if not any(p.suffix.lower() in VIDEO for p in folder(root, rid).glob("source.*")):
        bot.text("Its video is gone (references are kept a month): send it again with your note as the caption.")
        return f"{rid}: video gone"
    store.update_ref(rid, status="studying")
    store.queue("reference", rid, note=text)
    if ref["message_id"]:
        bot.set_buttons(ref["message_id"], None)
    bot.text("✏️ Got it: I'm studying it again with your note.")
    return f"{rid}: studying again"


def due(root: Path, store) -> list[dict]:
    """The references the week's picking must use: the oldest ready ones with a live pattern."""
    out, most = [], per_week()
    for ref in store.refs("ready"):
        if len(out) >= most:
            break
        p = patterns.load(root, ref["pattern"] or "")
        if p and p["status"] == "active":
            out.append(ref)
    return out


def after_pick(root: Path, store, bot, plan: dict, week: str, refs: list[dict]) -> list[str]:
    """Marks the references a pick took; tells the owner about the ones no moment fitted."""
    answer = plan["answer"]
    fits = {f["pattern"]: f for f in answer.get("pattern_fit", [])}
    lines = []
    for ref in refs:
        pick = next((p for p in answer["shorts"] if p.get("pattern") == ref["pattern"]), None)
        if pick:
            store.update_ref(ref["id"], status="used", week=week, pick=pick["name"])
            lines.append(f"reference {ref['id']}: {week}'s {pick['name']} follows {ref['pattern']}")
            continue
        p = patterns.load(root, ref["pattern"]) or {"title": ref["pattern"]}
        play = fits.get(ref["pattern"], {}).get("play", "")
        bot.text(f"🧩 None of {week}'s moments fits <b>{esc(p['title'])}</b>, so it waits for next week."
                 + (f" To make it, play a shift where {esc(play)}" if play else ""))
        lines.append(f"reference {ref['id']}: no moment fits {ref['pattern']}")
    return lines


def library_text(root: Path) -> str:
    found = patterns.active(root, most=50)
    if not found:
        return "No patterns yet. Send me a video to learn from (as a video, under 20 MB); its caption is your note."
    lines = ["<b>Patterns</b> (name one in a ✏️ note to use it)"]
    for p in found:
        lines.append(f"• <b>{esc(p['title'])}</b> <code>{esc(p['name'])}</code> · used {len(p['used_by'])}×")
    return clip("\n".join(lines), 4000)
