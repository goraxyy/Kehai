"""Playtest sessions on the owner's Mac (PLAYTEST.md, step 3).

New sessions come from the upload service (or are dropped in by hand), are unpacked, read into
facts, summarised by Claude, added to the combined report and announced in Telegram. Each bug note
waits there for the owner: one tap files it as a GitHub issue in neutral words.

    <playtests>/<round>/<code>/<launch>/   one session: what its zip held, facts.json, summary.json, bugs.json
    <playtests>/inbox/*.zip                 sessions dropped in by hand (a tester who sent the file)
    <playtests>/report.html                 every round, tester and session on one page

<playtests> is KEHAI_PLAYTESTS, else ~/TokenLimit/playtests. The upload address and the admin key
come from tools/playtest/.env (KEHAI_PLAYTEST_UPLOAD_URL, KEHAI_PLAYTEST_ADMIN_KEY).
"""
from __future__ import annotations

import os
from pathlib import Path

from .. import env, paths

ENV_FILE = paths.REPO / "tools" / "playtest" / ".env"
DEFAULT_ROOT = Path.home() / "TokenLimit" / "playtests"


def setting(name: str, default: str | None = None) -> str | None:
    """From the environment, tools/marketing/.env, or tools/playtest/.env, in that order."""
    env.load()
    env.load(ENV_FILE)
    return env.get(name, default)


def root() -> Path:
    override = os.environ.get("KEHAI_PLAYTESTS")
    return Path(override).expanduser().resolve() if override else DEFAULT_ROOT


def sessions(where: Path) -> list[Path]:
    """Every unpacked session, oldest first."""
    found = [p.parent for p in where.glob("*/*/*/session.jsonl")]
    return sorted(found, key=lambda p: p.name)


def cycle(marketing: Path, bot, dry_run: bool = False) -> list[str]:
    """What the scheduled `work` job does each time: pull, read, summarise, announce, report."""
    from .. import alerts
    from . import inbox, notify, report, session, summary

    where = root()
    lines: list[str] = []
    try:
        new, notes = inbox.pull(where, inbox.remote())
    except Exception as e:                       # the service is down: next time
        return [f"playtest: couldn't reach the upload service: {e}"]
    lines += notes
    for folder in new:
        try:
            facts = session.write_facts(folder)
            got = None
            try:
                got = summary.summarise(marketing, folder, facts, dry_run=dry_run)
            except Exception as e:
                alerts.alert(marketing, "playtest", f"Couldn't summarise the playtest session {folder.name}: {e}")
            notify.announce(bot, folder, facts, got)
            lines.append(f"playtest: {facts['code']} ({facts['round']}) {facts['minutes']:.0f} min, "
                         f"{len(facts['shifts'])} shift(s), {facts['ended']}" + (", summarised" if got else ""))
        except Exception as e:
            alerts.alert(marketing, "playtest", f"Couldn't read the playtest session {folder}: {e}")
            lines.append(f"playtest: {folder} failed: {e}")
    if new:
        try:
            lines.append(f"playtest: report at {report.write(where)}")
        except Exception as e:
            alerts.alert(marketing, "playtest", f"Couldn't write the playtest report: {e}")
            lines.append(f"playtest: the report failed: {e}")
    return lines
