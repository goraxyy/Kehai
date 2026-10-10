"""Telegram for playtests: one message per session (who, how long, how it ended, where they
struggled, their answers, Claude's headline), then one per bug note with the issue it would become.
📝 files that issue in the public repo (`gh issue create`, label "playtest"); ✖ drops it. Nothing
is published without the tap, and the text is the one shown. Buttons: pt:f:<bug> and pt:n:<bug>.

KEHAI_DRY_RUN=1 (the pipeline's trial mode) says what it would file instead of filing.
KEHAI_ISSUES_REPO picks the repo (goraxyy/Kehai); KEHAI_GH the gh to run (tests use a stand-in).
"""
from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from ..telegram import buttons, esc
from . import root, setting

LABEL = "playtest"


class IssueError(Exception):
    """gh couldn't file the issue."""


def clock(seconds: float | None) -> str:
    if seconds is None:
        return "–"
    s = int(seconds)
    return f"{s // 60}:{s % 60:02d}"


def session_text(facts: dict, summary: dict | None) -> str:
    shifts = facts["shifts"]
    lines = [f"🎮 <b>Playtest: {esc(facts['code'])}</b> · {esc(facts['round'])} · {facts['minutes']:.0f} min · "
             f"{len(shifts)} shift{'s' if len(shifts) != 1 else ''} · {esc(facts['ended'])}"]
    if facts["first_clock_in_s"] is None:
        lines.append(f"Never clocked in (walked {facts['walked_before_clock_in_m']:.0f} m)")
    else:
        lines.append(f"First clock-in after {clock(facts['first_clock_in_s'])} (walked {facts['walked_before_clock_in_m']:.0f} m first)")
    lines.append(" · ".join([
        "task list opened ✓" if facts["task_list_opened"] else "never opened the task list",
        f"E on nothing ×{facts['e_on_nothing']}",
        f"stood still {clock(facts['still_s'])}",
    ]))
    spotted = sum(s.get("spotted", 0) or 0 for s in shifts)
    catches = sum(s.get("catches", 0) or 0 for s in shifts)
    if shifts:
        lines.append(f"Karen: spotted them {spotted}× · caught {catches}×")
    a = facts.get("answers")
    if a:
        lines.append("Answers: " + " · ".join(f"{k} {esc(v)}" for k, v in
                                               (("Karen felt", a.get("karen")), ("knew what to do:", a.get("knew")),
                                                ("lost:", a.get("lost")), ("play more:", a.get("more"))) if v))
        if a.get("broke"):
            lines.append(f"“{esc(a['broke'])}”")
    fps = facts["fps"]
    computer = facts["computer"]
    lines.append(f"{fps['avg'] or '–'} fps (low {fps['lowest'] or '–'}) · {facts['errors']} error(s) · "
                 f"{esc(computer.get('os') or '?')} · {esc(computer.get('cpu') or '?')}")
    if summary:
        lines.append(f"<i>{esc(summary['headline'])}</i>")
        verdict = summary.get("karen", {}).get("verdict")
        if verdict and verdict != "unclear":
            lines.append(f"Karen looks {esc(verdict)}.")
    if facts["bugs"]:
        lines.append(f"🐞 {len(facts['bugs'])} bug note(s) follow.")
    lines.append(f"<code>{esc(str(root() / facts['round'] / facts['code'] / facts['launch']))}</code>")
    return "\n".join(lines)


def where_in(facts: dict, bug: dict) -> str:
    shift = next((s for s in reversed(facts["shifts"]) if s["start_t"] <= (bug["t"] or 0)), None)
    if shift and (bug["t"] or 0) <= shift.get("end_t", 1e12):
        return f"during shift {shift['n']}, {clock(bug['shift_time'])} in"
    return "between shifts" if facts["shifts"] else "before the first clock-in"


def template(facts: dict, bug: dict) -> tuple[str, str]:
    """The issue when there's no summary: the owner reads it before it's filed."""
    note = (bug["note"] or "").strip()
    where = where_in(facts, bug)
    title = f"Playtest: {note[:70]}" if note else f"Playtest: a bug marked {where} (no note)"
    computer = facts["computer"]
    body = (f"Found in a playtest ({facts['round']}, build {facts.get('build') or '?'}, "
            f"{computer.get('platform') or '?'}, {computer.get('os') or '?'}).\n\n"
            f"**What happened** ({where}): {note or '(the tester marked the moment without a note)'}\n\n"
            f"_Marked with Shift+F7. The replay is {bug.get('rec') or 'not named'} at {clock(bug.get('rec_time'))}._")
    return title, body


def announce(bot, folder: Path, facts: dict, summary: dict | None) -> None:
    bot.text(session_text(facts, summary))
    proposals = {b["i"]: b for b in (summary or {}).get("bugs", [])}
    record = []
    for bug in facts["bugs"]:
        bug_id = f"{facts['code']}.{facts['launch']}.{bug['i']}"
        p = proposals.get(bug["i"])
        title, body = (p["title"], p["body"]) if p else template(facts, bug)
        text = (f"🐞 <b>{esc(facts['code'])}</b>, {esc(where_in(facts, bug))}: “{esc(bug['note'] or '(no note)')}”\n\n"
                f"Issue it would file:\n<b>{esc(title)}</b>\n{esc(body)}")
        message = bot.text(text, buttons([[("📝 File it", f"pt:f:{bug_id}"), ("✖ Not a bug", f"pt:n:{bug_id}")]]))
        record.append({"id": bug_id, "i": bug["i"], "note": bug["note"], "title": title, "body": body,
                       "message_id": message, "status": "asked", "issue": None})
    if record:
        (folder / "bugs.json").write_text(json.dumps(record, indent=1, ensure_ascii=False), encoding="utf-8")


def find(bug_id: str) -> tuple[Path, list[dict], int] | None:
    code, _, rest = bug_id.partition(".")
    launch, _, _ = rest.partition(".")
    for path in root().glob(f"*/{code}/{launch}/bugs.json"):
        bugs = json.loads(path.read_text(encoding="utf-8"))
        for i, b in enumerate(bugs):
            if b["id"] == bug_id:
                return path, bugs, i
    return None


def file_issue(title: str, body: str) -> str:
    if setting("KEHAI_DRY_RUN") == "1":
        return "(dry run: not filed)"
    gh = os.environ.get("KEHAI_GH", "gh")
    repo = setting("KEHAI_ISSUES_REPO") or "goraxyy/Kehai"
    subprocess.run([gh, "label", "create", LABEL, "--repo", repo, "--color", "DC143C",
                    "--description", "Found in a playtest"], capture_output=True, text=True, timeout=60)   # there already: fine
    r = subprocess.run([gh, "issue", "create", "--repo", repo, "--title", title, "--body", body, "--label", LABEL],
                       capture_output=True, text=True, timeout=60)
    if r.returncode != 0:
        raise IssueError((r.stderr or r.stdout).strip() or f"gh exited {r.returncode}")
    return r.stdout.strip().splitlines()[-1]


def tap(bot, cb: dict, parts: list[str]) -> str:
    """📝 or ✖ under a bug note."""
    if len(parts) != 3 or parts[1] not in ("f", "n"):
        bot.answer(cb["id"], "?")
        return f"unknown playtest button {':'.join(parts)}"
    act, bug_id = parts[1], parts[2]
    found = find(bug_id)
    if not found:
        bot.answer(cb["id"], "That bug note isn't here any more")
        return f"playtest bug {bug_id}: gone"
    path, bugs, i = found
    bug = bugs[i]
    if bug["status"] != "asked":
        bot.answer(cb["id"], f"Already {bug['status']}")
        return f"playtest bug {bug_id}: already {bug['status']}"
    if act == "n":
        bug["status"] = "dismissed"
        bot.answer(cb["id"], "Not a bug")
        said = f"playtest bug {bug_id}: dismissed"
    else:
        try:
            bug["issue"] = file_issue(bug["title"], bug["body"])
        except (IssueError, OSError, subprocess.SubprocessError) as e:
            bot.answer(cb["id"], "Couldn't file it")
            bot.text(f"🐞 Couldn't file <b>{esc(bug['title'])}</b>: {esc(e)}. Tap 📝 again to retry.")
            return f"playtest bug {bug_id}: filing failed: {e}"
        bug["status"] = "filed"
        bot.answer(cb["id"], "Filed")
        bot.text(f"📝 Filed: {esc(bug['issue'])}")
        said = f"playtest bug {bug_id}: filed {bug['issue']}"
    bot.set_buttons(cb["message"]["message_id"], None)
    path.write_text(json.dumps(bugs, indent=1, ensure_ascii=False), encoding="utf-8")
    return said
