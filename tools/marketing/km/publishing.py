"""Posting approved shorts, one per posting day (pipeline.json: Tuesdays and Fridays).

The oldest approved short goes to each Buffer channel's queue (Buffer's own schedule picks the
time: Q14), from a public copy of the file (km/media.publish). TikTok, and any platform Buffer
can't take (no key yet, no public host yet, no channel connected, or a refused post), comes to
Telegram instead: the file once, then each platform's text with a "Posted ✅" button to tap when
it's up. A short is `posted` once every platform is.
"""
from __future__ import annotations

import datetime as dt
import json
from pathlib import Path

from . import alerts, media
from .buffer import Buffer, BufferError, text_for
from .production import draft_file, edit_path
from .settings import day_name, now, settings
from .telegram import buttons, esc

NAMES = {"youtube": "YouTube", "instagram": "Instagram", "twitter": "X", "bluesky": "Bluesky", "tiktok": "TikTok",
         "threads": "Threads", "facebook": "Facebook", "mastodon": "Mastodon", "linkedin": "LinkedIn"}


def package(root: Path, vid: str) -> dict:
    return json.loads((edit_path(root, vid).parent / "package.json").read_text(encoding="utf-8"))["languages"]


def manual(root: Path, store, bot, video: dict, lang: str, services: list[str], why: str) -> None:
    """Sends the file and each platform's text to Telegram, to post by hand."""
    vid = video["id"]
    block = package(root, vid)[lang]
    from .approvals import sendable
    film = draft_file(root, vid, lang)
    bot.video(sendable(root, film), f"📤 <b>{esc(vid)}</b> ({lang.upper()}) to post by hand: {esc(why)}")
    for service in services:
        text = text_for(service, block)
        if service == "youtube":
            text = f"Title: {block['youtube']['title']}\n\n{text}\n\nTags: {', '.join(block['youtube']['tags'])}"
        bot.text(f"<b>{NAMES.get(service, service)}</b>\n<pre>{esc(text)}</pre>",
                 buttons([[(f"Posted on {NAMES.get(service, service)} ✅", f"pd:{vid}:{service}:{lang}")]]))
        store.set_post(vid, service, lang, "manual", error=why)


def mark_posted(store, vid: str, service: str, lang: str) -> bool:
    """Marks one platform done; True when every platform of the short is."""
    store.set_post(vid, service, lang, "posted")
    posts = store.posts(vid)
    if posts and all(p["status"] == "posted" for p in posts):
        store.update(vid, status="posted", posted_at=dt.datetime.now().isoformat(timespec="seconds"))
        return True
    return False


def publish(root: Path, store, bot, force: bool = False, dry_run: bool = False) -> list[str]:
    s = settings()
    today = now().date()
    if not force and day_name(today) not in s["posting"]["days"]:
        return [f"{day_name(today)} isn't a posting day"]
    waiting = store.videos("approved", kind="short")
    if not waiting:
        if store.get("publish.nothing") != today.isoformat():
            store.set("publish.nothing", today.isoformat())
            bot.text("📭 A posting day, but no approved short is waiting.")
        return ["nothing approved to post"]
    video = waiting[0]
    vid = video["id"]
    if dry_run:
        return [f"would post {vid} to {', '.join(s['posting']['buffer'])}" +
                (" and send it for TikTok by hand" if s["posting"]["tiktok"] == "manual" else "")]
    out = []
    buffer = Buffer(root)
    for lang in s["posting"]["languages"]:
        services = [x for x in s["posting"]["buffer"] if (store.post(vid, x, lang) or {}).get("status") not in ("scheduled", "posted")]
        by_hand: list[str] = []
        why = ""
        if services:
            try:
                if not buffer.live:
                    raise media.NotSetUp("BUFFER_API_KEY isn't set yet")
                url = media.publish(draft_file(root, vid, lang), f"{vid}/{vid}.{lang}.mp4")
                channels = buffer.channels()
            except (media.NotSetUp, BufferError, RuntimeError) as e:
                by_hand, why = services, f"Buffer isn't ready ({e})"
            else:
                block = package(root, vid)[lang]
                extra = {"title": block["youtube"]["title"], "youtube": s["posting"]["youtube"], "instagram": s["posting"]["instagram"]}
                for service in services:
                    channel = channels.get(service)
                    if not channel:
                        by_hand.append(service)
                        why = why or f"no {NAMES.get(service, service)} channel connected in Buffer"
                        continue
                    try:
                        post_id = buffer.queue_video(channel["id"], service, text_for(service, block), url, extra)
                        store.set_post(vid, service, lang, "scheduled", buffer_id=post_id, media_url=url)
                        out.append(f"{vid} {lang}: queued on {service}")
                    except BufferError as e:
                        by_hand.append(service)
                        why = why or f"Buffer refused the {NAMES.get(service, service)} post ({e})"
        if s["posting"]["tiktok"] == "manual" and (store.post(vid, "tiktok", lang) or {}).get("status") != "posted":
            by_hand.append("tiktok")
            why = why or "TikTok is posted by hand"
        if by_hand:
            manual(root, store, bot, video, lang, by_hand, why)
            out.append(f"{vid} {lang}: by hand on {', '.join(by_hand)} ({why})")
            if "Buffer isn't ready" in why and store.get("publish.not_ready") != today.isoformat():
                store.set("publish.not_ready", today.isoformat())
                alerts.alert(root, "publish", f"Posting by hand for now: {why}.")
    store.update(vid, status="scheduled", scheduled_at=dt.datetime.now().isoformat(timespec="seconds"))
    queued = [p["platform"] for p in store.posts(vid, "scheduled")]
    if queued:
        bot.text(f"📅 <b>{esc(vid)}</b> is in Buffer's queue for {', '.join(NAMES.get(q, q) for q in queued)}.")
    return out


def sync(root: Path, store, bot) -> list[str]:
    """Asks Buffer how the queued posts did; a post that went out is posted, one that failed comes by hand."""
    buffer = Buffer(root)
    if not buffer.live:
        return []
    out = []
    for p in store.posts(status="scheduled"):
        if not p["buffer_id"]:
            continue
        try:
            post = buffer.status(p["buffer_id"])
        except BufferError as e:
            out.append(f"{p['video']} {p['platform']}: couldn't ask Buffer ({e})")
            continue
        if post.get("status") == "sent":
            done = mark_posted(store, p["video"], p["platform"], p["lang"])
            out.append(f"{p['video']} {p['platform']}: sent" + (" (all done)" if done else ""))
        elif post.get("status") == "error":
            why = (post.get("error") or {}).get("message", "Buffer couldn't post it")
            video = store.video(p["video"])
            manual(root, store, bot, video, p["lang"], [p["platform"]], why)
            out.append(f"{p['video']} {p['platform']}: failed in Buffer, sent to post by hand")
    return out
