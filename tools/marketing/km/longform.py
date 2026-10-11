"""The monthly long video, with the owner's ✋ gates in Telegram (docs/MARKETING.md).

outline ✋ → (the outline's shots render) script ✋ → (voice, the edit, the English cut)
rough cut ✋ → (packaging, the Russian audio, subtitles) the final files on Drive for YouTube
Studio, and the owner taps "Uploaded ✅".

At each gate: ✅ goes on, ✏️ asks what to change and does the stage again with that note. The
state is in the store's meta under `long.<month>`; the slow parts run as `long` jobs (the worker).
"""
from __future__ import annotations

import datetime as dt
import json
import subprocess
from pathlib import Path

from . import media, subtitles
from .production import draft_file, edit_path, render, script
from .settings import settings
from .telegram import buttons, esc

NEXT = {"outline": "shots", "script": "cut", "cut": "final"}
LABEL = {"outline": "outline", "script": "script", "cut": "rough cut"}


def edit_id(month: str) -> str:
    return f"long-{month}"


class Long:
    def __init__(self, root: Path, store, bot):
        self.root, self.store, self.bot = root, store, bot

    def state(self, month: str) -> dict:
        return json.loads(self.store.get(f"long.{month}", "{}"))

    def save(self, month: str, **state) -> None:
        self.store.set(f"long.{month}", json.dumps({**self.state(month), **state}))

    def folder(self, month: str) -> Path:
        return self.root / "long" / month

    # ---- starting --------------------------------------------------------------------------

    def start(self, month: str, dry_run: bool = False) -> str:
        if self.state(month):
            return f"{month}: already under way ({self.state(month).get('stage')})"
        r = script(self.root, "long_video.py", "outline", "--month", month, dry_run=dry_run)
        if dry_run or not r.ok:
            return f"{month}: outline {'dry run' if dry_run else 'failed'} (exit {r.code})"
        self.store.add_video(edit_id(month), "long", status="awaiting", week=month)
        self.gate(month, "outline")
        return f"{month}: outline sent for approval"

    # ---- the gates -------------------------------------------------------------------------

    def gate(self, month: str, stage: str) -> None:
        keyboard = buttons([[(f"✅ Approve the {LABEL[stage]}", f"lg:a:{month}:{stage}"), ("✏️ Change", f"lg:e:{month}:{stage}")]])
        here = self.folder(month)
        if stage == "outline":
            o = json.loads((here / "outline.json").read_text(encoding="utf-8"))["answer"]
            lines = [f"🎬 <b>Long video, {month}: the outline</b>", f"<i>{esc(o['logline'])}</i>",
                     "Titles: " + " · ".join(esc(t) for t in o["title_ideas"])]
            for s in o["sections"]:
                lines.append(f"\n<b>{esc(s['name'])}</b> ({s['minutes']} min): {esc(s['purpose'])}")
                lines += [f"• {esc(b)}" for b in s["beats"][:6]]
            shots = sum(len(s["shots"]) for s in o["sections"])
            lines.append(f"\n{shots} shots to render after you approve.")
            message = self.bot.text("\n".join(lines), keyboard)
        elif stage == "script":
            s = json.loads((here / "script.json").read_text(encoding="utf-8"))["answer"]
            md = here / "script.md"
            md.write_text("\n".join(f"## {sec['name']}\n\n" + "\n\n".join(
                f"**{l['speaker']}** — {l['text']}  \n_on screen: {l['cue']}_" for l in sec["lines"]) for sec in s["sections"]),
                encoding="utf-8")
            words = sum(len(l["text"].split()) for sec in s["sections"] for l in sec["lines"])
            self.bot.document(md, f"🎬 Long video, {month}: the script ({words} words, about {words / 150:.0f} min).")
            message = self.bot.text(f"Approve the script for {month}?", keyboard)
        else:
            from .approvals import sendable
            film = draft_file(self.root, edit_id(month), "en")
            message = self.bot.video(sendable(self.root, film), f"🎬 Long video, {month}: the rough cut (English).", keyboard)
        self.save(month, stage=stage, waiting=True, message_id=message)
        self.store.update(edit_id(month), status="awaiting")

    def tap(self, cb: dict, parts: list[str]) -> str:
        if len(parts) != 4:
            return "bad long button"
        _, act, month, stage = parts
        state = self.state(month)
        if not state.get("waiting") or state.get("stage") != stage:
            self.bot.answer(cb["id"], "That step is already done")
            return f"long {month}: stale {stage}"
        if act == "a":
            self.save(month, waiting=False)
            self.bot.set_buttons(cb["message"]["message_id"], None)
            self.bot.answer(cb["id"], "Approved ✅")
            self.store.update(edit_id(month), status="writing")
            self.store.queue("long", edit_id(month), month=month, stage=NEXT[stage])
            self.bot.text(f"✅ The {LABEL[stage]} for {month} is approved; next comes here when it's ready.")
            return f"long {month}: {stage} approved"
        if act == "e":
            self.bot.answer(cb["id"], "What should change?")
            asked = self.bot.text(f"✏️ What should change in the {LABEL[stage]} for {month}? Reply to this message.",
                                  force_reply="What should change?")
            self.store.remember_prompt(asked, edit_id(month), f"long:{month}:{stage}")
            return f"long {month}: asked for a note on the {stage}"
        if act == "up":
            self.store.update(edit_id(month), status="posted", posted_at=dt.datetime.now().isoformat(timespec="seconds"))
            self.save(month, stage="done", waiting=False)
            self.bot.set_buttons(cb["message"]["message_id"], None)
            self.bot.answer(cb["id"], "🎉")
            return f"long {month}: uploaded"
        return f"long {month}: unknown {act}"

    def note(self, prompt: dict, text: str) -> str:
        _, month, stage = prompt["purpose"].split(":")
        state = self.state(month)
        if not state.get("waiting") or state.get("stage") != stage:
            self.bot.text("That step has moved on; the note wasn't used.")
            return f"long {month}: late note"
        self.save(month, waiting=False)
        if state.get("message_id"):
            self.bot.set_buttons(state["message_id"], None)
        self.store.update(edit_id(month), status="revising")
        self.store.queue("long", edit_id(month), month=month, stage=f"redo-{stage}", note=text)
        self.bot.text(f"✏️ Got it: doing the {LABEL[stage]} again with your note.")
        return f"long {month}: redo {stage}"

    # ---- the slow parts (a `long` job) -----------------------------------------------------

    def work(self, job: dict) -> tuple[int, str]:
        p = job["payload"]
        month, stage, note = p["month"], p["stage"], p.get("note")
        here = self.folder(month)
        vid = edit_id(month)
        steps: list[tuple[str, callable]] = []

        def run(name, *args):
            steps.append((name, lambda: script(self.root, name, *args)))

        if stage == "redo-outline":
            run("long_video.py", "outline", "--month", month, "--note", note)
            after = "outline"
        elif stage == "shots":
            run("render_picks.py", str(here / "outline.json"))
            run("long_video.py", "script", "--month", month)
            after = "script"
        elif stage == "redo-script":
            run("long_video.py", "script", "--month", month, "--note", note)
            after = "script"
        elif stage in ("cut", "redo-cut"):
            if stage == "cut":
                run("long_video.py", "voice", "--month", month)
                run("long_video.py", "edit", "--month", month)
            else:
                run("revise.py", vid, "--note", note)
            run("translate.py", vid)
            run("voice.py", vid)
            steps.append(("render en", lambda: render(self.root, edit_path(self.root, vid), "en")))
            after = "cut"
        elif stage == "final":
            return self.final(month)
        else:
            return 2, f"unknown long stage {stage}"
        for name, step in steps:
            r = step()
            if not r.ok:
                return r.code, f"{name}: {r.output.splitlines()[-1] if r.output else r.code}"
        self.gate(month, after)
        return 0, f"long {month}: {after} sent for approval"

    def final(self, month: str) -> tuple[int, str]:
        vid = edit_id(month)
        for r in (script(self.root, "package.py", vid), render(self.root, edit_path(self.root, vid), "ru")):
            if not r.ok:
                return r.code, r.output.splitlines()[-1] if r.output else str(r.code)
        out = self.root / "long" / month / "final"
        out.mkdir(parents=True, exist_ok=True)
        edit = json.loads(edit_path(self.root, vid).read_text(encoding="utf-8"))
        files = [draft_file(self.root, vid, "en")]
        for lang in edit["languages"]:
            files.append(subtitles.write(edit, lang, out / f"{vid}.{lang}.srt"))
        ru_audio = out / f"{vid}.ru.m4a"
        exe = media.ffmpeg()
        r = subprocess.run([str(exe), "-y", "-loglevel", "error", "-i", str(draft_file(self.root, vid, "ru").resolve()),
                            "-vn", "-c:a", "copy", str(ru_audio.resolve())], cwd=exe.parent, capture_output=True, text=True)
        if r.returncode != 0:
            return 1, f"couldn't take the Russian audio out: {r.stderr[-200:]}"
        files += [ru_audio, edit_path(self.root, vid).parent / "package.json"]
        where = "in " + str(out)
        try:
            for f in files:
                media.archive(f, f"long/{month}/{f.name}")
            where = f"on Drive in TokenLimit Marketing/long/{month}"
        except media.NotSetUp:
            for f in files:
                if f.parent != out:
                    (out / f.name).write_bytes(f.read_bytes())
        pack = json.loads((edit_path(self.root, vid).parent / "package.json").read_text(encoding="utf-8"))["languages"]["en"]
        checklist = ("In YouTube Studio: upload the English video; title and description below; Subtitles → add the "
                     ".en.srt and .ru.srt; Languages → add the Russian audio track (.ru.m4a); thumbnail text: "
                     f"«{esc(pack['thumbnail_text'])}».")
        self.bot.text(f"🎬 <b>Long video {month}: ready to upload</b> ({where})\n{checklist}\n\n"
                      f"<b>Title</b>: {esc(pack['youtube']['title'])}\n<pre>{esc(pack['youtube']['description'])}</pre>",
                      buttons([[("Uploaded ✅", f"lg:up:{month}:final")]]))
        self.save(month, stage="final", waiting=True)
        self.store.update(vid, status="approved", approved_at=dt.datetime.now().isoformat(timespec="seconds"))
        return 0, f"long {month}: final files ready"


def due(today: dt.date) -> str | None:
    """The month whose long video starts today (on `long.day`, for the month before)."""
    if today.day != settings()["long"]["day"]:
        return None
    first = today.replace(day=1)
    return (first - dt.timedelta(days=1)).strftime("%Y-%m")
