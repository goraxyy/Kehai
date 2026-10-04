#!/usr/bin/env python3
"""Playtest sessions by hand (the scheduled `work` job does `pull` every five minutes). PLAYTEST.md.

    uv run playtest.py pull                 new sessions from the upload service and inbox/: read, summarise, announce, report
    uv run playtest.py import <zip>…        sessions a tester sent as files (named round_code_launch.zip)
    uv run playtest.py redo <folder>…       read and summarise sessions again (after a prompt change), and the report
    uv run playtest.py report [--open]      rebuild <playtests>/report.html
    uv run playtest.py status               what's here, and what's set up
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

from km import paths
from km.playtest import cycle, inbox, notify, report, root, session, sessions, setting, summary
from km.store import Store
from km.telegram import Bot


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--root", help="the marketing working folder (for Telegram and Claude's ledger)")
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("pull")
    p = sub.add_parser("import")
    p.add_argument("zips", nargs="+", type=Path)
    p = sub.add_parser("redo")
    p.add_argument("folders", nargs="+", type=Path)
    p = sub.add_parser("report")
    p.add_argument("--open", action="store_true")
    sub.add_parser("status")
    a = ap.parse_args()

    marketing = paths.root(a.root)
    where = root()
    if a.cmd == "status":
        found = sessions(where)
        print(f"playtests: {where} ({len(found)} session(s))")
        print("upload service: " + ("set up" if inbox.remote() else "not set up (KEHAI_PLAYTEST_UPLOAD_URL and KEHAI_PLAYTEST_ADMIN_KEY in tools/playtest/.env)"))
        print("summaries: " + ("on" if summary.ready() else "off until ANTHROPIC_API_KEY is in tools/marketing/.env"))
        print("issues go to: " + (setting("KEHAI_ISSUES_REPO") or "goraxyy/Kehai") + (" (dry run)" if setting("KEHAI_DRY_RUN") == "1" else ""))
        return 0
    if a.cmd == "report":
        out = report.write(where)
        print(out)
        if a.open:
            subprocess.run(["open", str(out)])
        return 0

    store = Store(marketing)
    bot = Bot(marketing, store)
    if a.cmd == "pull":
        for line in cycle(marketing, bot):
            print(line)
        return 0
    if a.cmd == "import":
        inbox_dir = where / "inbox"
        inbox_dir.mkdir(parents=True, exist_ok=True)
        for z in a.zips:
            (inbox_dir / z.name).write_bytes(z.read_bytes())
        for line in cycle(marketing, bot):
            print(line)
        return 0
    for folder in a.folders:
        folder = folder.resolve()
        facts = session.write_facts(folder)
        got = summary.summarise(marketing, folder, facts)
        print(f"{folder}: {facts['minutes']:.0f} min, {len(facts['shifts'])} shift(s)" + (", summarised" if got else ""))
    print(report.write(where))
    return 0


if __name__ == "__main__":
    sys.exit(main())
