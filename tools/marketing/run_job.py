#!/usr/bin/env python3
"""The one entry point the n8n workflows call (BUILD_PLAN.md, Phase 7). Each subcommand is one
scheduled job; it prints what it did and ends with one JSON line for n8n.

    uv run run_job.py produce [--week 2026-W40] [--pick-now] [--dry-run]   new shifts, the week's picks, each short along its steps
    uv run run_job.py telegram                                             the owner's taps and replies; alerts out
    uv run run_job.py work [--max 3]                                       queued jobs: revisions, undos, the long video's stages; new playtest sessions
    uv run run_job.py publish [--force] [--dry-run]                        a posting day: the next approved short out
    uv run run_job.py housekeeping [--apply | --report-only]               Buffer statuses, archive, retention, storage
    uv run run_job.py report                                               the weekly report, to Telegram
    uv run run_job.py long [--month 2026-10] [--force] [--dry-run]         start the month's long video (on its day)
    uv run run_job.py status
    uv run run_job.py telegram-setup                                       who has written to the bot (for TELEGRAM_CHAT_ID)
    uv run run_job.py fake tap <data> --message N | fake text "…" [--reply-to N]   play the owner without a bot

KEHAI_PICK_NOW=1 picks the week's shorts on the next produce whatever the day; KEHAI_PIPELINE
points at another settings file (trial runs). produce, work and long keep the Mac awake while
they run (caffeinate) and take the `pipeline`
lock: a second one finds it held and leaves it for next time (exit 0). Exit codes: km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys

from km import alerts, edits, housekeeping, lock, moments, paths, production
from km.approvals import Approvals, send_alerts
from km.cli import BadInput, run
from km.longform import Long, due
from km.publishing import publish, sync
from km.settings import now, on_or_after, settings
from km.store import Store
from km.telegram import Bot, esc

SUMMARY: list[str] = []


def say(line: str) -> None:
    print(line, flush=True)
    SUMMARY.append(line)


def stay_awake() -> None:
    if sys.platform == "darwin" and not os.environ.get("KEHAI_NO_CAFFEINATE"):
        try:
            subprocess.Popen(["caffeinate", "-i", "-w", str(os.getpid())], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        except OSError:
            pass


# ---- produce --------------------------------------------------------------------------------

def produce(root, store, bot, approvals, week: str | None, pick_now: bool, dry_run: bool) -> None:
    full = housekeeping.too_full(root)
    if full:
        say(f"production waits: {full}")
        return
    today = now().date()
    recent = moments.find_files(moments.records(), today - dt.timedelta(days=14), today + dt.timedelta(days=1))
    for f in recent:
        stem = f.name.removesuffix(".markers.json")
        if store.get(f"shift.seen.{stem}"):
            continue
        doc = moments.load(f)
        best = doc["moments"][0] if doc["moments"] else None
        line = (f"🕐 New shift recorded: {esc(doc['started'])}, {doc['length'] / 60:.0f} min, {len(doc['moments'])} clip moments"
                + (f"; the best: {esc(', '.join(best['tags'][:3]))} (score {best['score']:.0f})" if best else ""))
        if not dry_run:
            bot.text(line)
            store.set(f"shift.seen.{stem}", today.isoformat())
        say(f"new shift {stem}")

    start, end, label = moments.week_range(week)
    plan = root / "plans" / label / "picks.json"
    s = settings()["production"]
    pick_now = pick_now or os.environ.get("KEHAI_PICK_NOW") == "1"
    if not plan.exists() and (pick_now or on_or_after(today, s["pick_day"]) or week):
        docs = [moments.load(f) for f in moments.find_files(moments.records(), start, end)]
        count = sum(len(d["moments"]) for d in docs)
        if count < s["min_moments"]:
            if not dry_run and store.get(f"pick.thin.{label}") is None:
                store.set(f"pick.thin.{label}", today.isoformat())
                bot.text(f"📉 {label}: {len(docs)} shift(s), {count} clip moments: too few to pick {s['shorts_per_week']} "
                         "shorts. Play a shift or two, then /status.")
            say(f"{label}: only {count} moments; no picks")
        else:
            r = production.script(root, "pick_moments.py", "--week", label, "--shorts", str(s["shorts_per_week"]), dry_run=dry_run)
            say(f"{label}: pick_moments exit {r.code}: {r.output.splitlines()[-1] if r.output else ''}")
            if not r.ok and not dry_run:
                alerts.alert(root, "pick", f"Picking {label}'s shorts stopped: {r.output.splitlines()[-1] if r.output else r.code}")
    if plan.exists():
        for pick in json.loads(plan.read_text(encoding="utf-8"))["answer"]["shorts"]:
            if not store.video_for_pick(label, pick["name"]):
                store.add_video(f"{label.lower()}-{pick['name']}", "short", week=label, pick=pick["name"])
                say(f"new video {label.lower()}-{pick['name']}")
    for video in store.videos("writing", "revising", kind="short"):
        plan_path = root / "plans" / video["week"] / "picks.json" if video["week"] else None
        say(production.advance(root, store, bot, video, plan_path if plan_path and plan_path.exists() else None,
                               approvals.present, dry_run))


# ---- work -----------------------------------------------------------------------------------

def work(root, store, bot, approvals, most: int) -> None:
    retries = settings()["production"]["retries"]
    for _ in range(most):
        job = store.take_job()
        if not job:
            break
        vid = job["video"]
        if job["kind"] == "long":
            code, text = Long(root, store, bot).work(job)
        elif job["kind"] in ("revise", "undo"):
            if job["kind"] == "revise":
                r = production.script(root, "revise.py", vid, "--note", job["payload"]["note"])
            else:                                      # back to the version shown before this one
                try:
                    edits.restore_shown(root / "edits" / vid, store.video(vid)["version"] - 1)
                    r = production.Result(0, f"{vid}: back to v{store.video(vid)['version'] - 1}")
                except BadInput as e:
                    r = production.Result(2, str(e))
            code, text = r.code, (r.output.splitlines()[-1] if r.output else "")
            if r.ok:
                video = store.video(vid)
                plan_path = root / "plans" / (video["week"] or "") / "picks.json"
                text = production.advance(root, store, bot, video, plan_path if plan_path.exists() else None, approvals.present)
        else:
            code, text = 2, f"unknown job {job['kind']}"
        what = production.outcome(code, job["kind"])
        if what == "ok":
            store.finish_job(job["id"])
        elif what == "later" or (what == "failed" and job["attempts"] <= retries):
            later = (dt.datetime.now() + dt.timedelta(minutes=15 if what == "later" else 30)).isoformat(timespec="seconds")
            store.finish_job(job["id"], error=text, retry_at=later)
        else:
            store.finish_job(job["id"], status="failed", error=text)
            alerts.alert(root, "job", f"The {job['kind']} of {vid} stopped: {text}", job=job["id"])
            if vid and store.video(vid):
                store.update(vid, status="failed", error=text)
        say(f"job {job['id']} {job['kind']} {vid or ''}: {what}: {text}")
    from km import playtest                         # PLAYTEST.md: sessions from the upload service
    for line in playtest.cycle(root, bot):
        say(line)


# ---- the rest -------------------------------------------------------------------------------

def telegram(root, store, bot, approvals) -> None:
    for update in bot.updates():
        try:
            if bot.from_owner(update):
                say(approvals.handle(update))
            else:
                say(f"update {update['update_id']} isn't from the owner: ignored")
        except Exception as e:                      # one bad update mustn't stop the rest, or come back
            say(f"update {update['update_id']} failed: {e!r}")
            alerts.alert(root, "telegram", f"Couldn't handle a Telegram update: {e!r}")
        finally:
            bot.done(update)
    sent = send_alerts(root, bot)
    if sent:
        say(f"{sent} alert(s) sent")


def report(root, bot) -> int:
    r = production.script(root, "weekly_report.py")
    week = now().strftime("%G-W%V")
    md = root / "reports" / f"{week}.md"
    if r.ok and md.exists():
        bot.text(md.read_text(encoding="utf-8").replace("*", "").replace("&", "&amp;").replace("<", "&lt;"))
        say(f"report {week} sent")
    else:
        say(f"report failed (exit {r.code}): {r.output.splitlines()[-1] if r.output else ''}")
    return 0 if r.ok else r.code


def telegram_setup(bot) -> None:
    if not bot.token:
        say("TELEGRAM_BOT_TOKEN isn't set: make a bot with @BotFather and put its token in .env")
        return
    chats = {}
    for u in bot.call("getUpdates", {"timeout": 0}, token_only=True):
        msg = u.get("message") or {}
        chat = msg.get("chat") or {}
        if chat:
            chats[chat["id"]] = chat.get("username") or chat.get("first_name") or "?"
    if not chats:
        say("Nobody has written to the bot yet: send it /start in Telegram, then run this again.")
    for cid, who in chats.items():
        say(f"chat {cid} ({who}): put TELEGRAM_CHAT_ID={cid} in .env if that's you")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--root")
    sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("produce", parents=[common])
    p.add_argument("--week")
    p.add_argument("--pick-now", action="store_true")
    p.add_argument("--dry-run", action="store_true")
    sub.add_parser("telegram", parents=[common])
    p = sub.add_parser("work", parents=[common])
    p.add_argument("--max", type=int, default=3)
    p = sub.add_parser("publish", parents=[common])
    p.add_argument("--force", action="store_true", help="even if today isn't a posting day")
    p.add_argument("--dry-run", action="store_true")
    p = sub.add_parser("housekeeping", parents=[common])
    group = p.add_mutually_exclusive_group()
    group.add_argument("--apply", action="store_true", help="delete what retention says, whatever pipeline.json says")
    group.add_argument("--report-only", action="store_true")
    sub.add_parser("report", parents=[common])
    p = sub.add_parser("long", parents=[common])
    p.add_argument("--month")
    p.add_argument("--force", action="store_true", help="start even if today isn't long.day")
    p.add_argument("--dry-run", action="store_true")
    sub.add_parser("status", parents=[common])
    sub.add_parser("telegram-setup", parents=[common])
    p = sub.add_parser("fake", parents=[common])
    p.add_argument("what", choices=("tap", "text"))
    p.add_argument("value")
    p.add_argument("--message", type=int)
    p.add_argument("--reply-to", type=int)
    a = ap.parse_args()

    root = paths.root(a.root)
    store = Store(root)
    bot = Bot(root, store)
    approvals = Approvals(root, store, bot)
    code = 0
    try:
        if a.cmd in ("produce", "work", "long"):
            stay_awake()
            try:
                with lock.heavy(root, f"run_job {a.cmd}", wait=0, name="pipeline"):
                    if a.cmd == "produce":
                        produce(root, store, bot, approvals, a.week, a.pick_now, a.dry_run)
                    elif a.cmd == "work":
                        work(root, store, bot, approvals, a.max)
                    else:
                        month = a.month or (due(now().date()) if not a.force else (now().date().replace(day=1) - dt.timedelta(days=1)).strftime("%Y-%m"))
                        say(Long(root, store, bot).start(month, a.dry_run) if month else "not the long video's day")
            except lock.Busy as e:
                say(f"left for next time: {e}")
        elif a.cmd == "telegram":
            telegram(root, store, bot, approvals)
        elif a.cmd == "publish":
            for line in publish(root, store, bot, a.force, a.dry_run):
                say(line)
        elif a.cmd == "housekeeping":
            for line in sync(root, store, bot):
                say(line)
            keeper = housekeeping.Keeper(root, store, apply=True if a.apply else False if a.report_only else None)
            for line in keeper.run():
                say(line)
            sent = send_alerts(root, bot)
            if sent:
                say(f"{sent} alert(s) sent")
        elif a.cmd == "report":
            code = report(root, bot)
        elif a.cmd == "status":
            say(approvals.status())
        elif a.cmd == "telegram-setup":
            telegram_setup(bot)
        elif a.cmd == "fake":
            if bot.live:
                raise BadInput("fake updates are for the outbox mode (no TELEGRAM_BOT_TOKEN)")
            u = bot.fake_tap(a.value, a.message) if a.what == "tap" else bot.fake_text(a.value, a.reply_to)
            say(f"queued a fake update {u['update_id']}: run `run_job.py telegram` to handle it")
    finally:
        print(json.dumps({"job": a.cmd, "code": code, "live_telegram": bot.live, "summary": SUMMARY[-20:]}, ensure_ascii=False))
        store.close()
    return code


if __name__ == "__main__":
    run(main)
