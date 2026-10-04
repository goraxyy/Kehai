#!/usr/bin/env python3
"""Tester codes for a playtest round, and the message to send each tester (PLAYTEST.md).

    python3 tools/playtest/make_codes.py --round round1 --count 6               T01…T06
    python3 tools/playtest/make_codes.py --round round1 --names Ana Ben Chloe   one code per name
    python3 tools/playtest/make_codes.py --round round2 --count 4 --first 7     T07…T10

Codes go into <playtests>/testers.csv (who has which code; it stays on your Mac, never in git or
in the game), and one ready-to-send message per tester into <playtests>/messages/<round>/<code>.txt,
from tools/playtest/TESTER_MESSAGE.md with the links in tools/playtest/.env filled in. A code is
never reused: the next free number is used if one is taken. Standard library only.
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import os
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
TEMPLATE = HERE / "TESTER_MESSAGE.md"
FIELDS = ("code", "name", "round", "created")


def settings() -> dict[str, str]:
    values = {}
    env = HERE / ".env"
    if env.exists():
        for raw in env.read_text(encoding="utf-8").splitlines():
            line = raw.strip()
            if line and not line.startswith("#") and "=" in line:
                k, _, v = line.partition("=")
                values[k.strip()] = v.strip().strip("'\"")
    values.update({k: v for k, v in os.environ.items() if k.startswith("KEHAI_PLAYTEST_")})
    return values


def playtests() -> Path:
    override = os.environ.get("KEHAI_PLAYTESTS")
    return Path(override).expanduser() if override else Path.home() / "TokenLimit" / "playtests"


def taken(sheet: Path) -> set[str]:
    if not sheet.exists():
        return set()
    with sheet.open(newline="", encoding="utf-8") as f:
        return {row["code"] for row in csv.DictReader(f)}


def message(code: str, round_: str, s: dict[str, str]) -> str:
    """The template's body (below its first ---), with the placeholders filled."""
    text = TEMPLATE.read_text(encoding="utf-8").split("\n---\n", 1)[-1].strip() + "\n"
    form = s.get("KEHAI_PLAYTEST_FORM_URL", "").replace("{code}", code)
    fill = {"code": code, "round": round_, "itch_url": s.get("KEHAI_PLAYTEST_ITCH_URL", ""),
            "itch_password": s.get("KEHAI_PLAYTEST_ITCH_PASSWORD", ""), "mac_link": s.get("KEHAI_PLAYTEST_MAC_LINK", ""),
            "windows_link": s.get("KEHAI_PLAYTEST_WINDOWS_LINK", ""), "form_url": form}
    text = re.sub(r"\{(\w+)\}", lambda m: fill.get(m.group(1), m.group(0)) or f"[{m.group(1)} not set]", text)
    # Lines about a way to download that isn't set up are left out.
    return "\n".join(l for l in text.splitlines() if "not set]" not in l or "itch" not in l and "link" not in l) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--round", required=True)
    group = ap.add_mutually_exclusive_group(required=True)
    group.add_argument("--count", type=int)
    group.add_argument("--names", nargs="+")
    ap.add_argument("--first", type=int, default=1, help="the first number to try")
    ap.add_argument("--prefix", default="T")
    a = ap.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9-]+", a.round):
        ap.error("a round is letters, digits and dashes")

    where = playtests()
    sheet = where / "testers.csv"
    used = taken(sheet)
    names = a.names or [""] * a.count
    s = settings()
    out_dir = where / "messages" / a.round
    out_dir.mkdir(parents=True, exist_ok=True)
    prefix = a.prefix.upper()
    if not re.fullmatch(r"[A-Z][A-Z-]{0,8}", prefix):
        ap.error("a prefix is a few letters, like T")
    new_rows, n = [], a.first
    for name in names:
        while f"{prefix}{n:02d}" in used:
            n += 1
        code = f"{prefix}{n:02d}"
        used.add(code)
        new_rows.append({"code": code, "name": name, "round": a.round, "created": dt.date.today().isoformat()})
        (out_dir / f"{code}.txt").write_text(message(code, a.round, s), encoding="utf-8")
        print(f"{code}  {name or ''}".rstrip())
    first = not sheet.exists()
    with sheet.open("a", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        if first:
            w.writeheader()
        w.writerows(new_rows)
    print(f"\n{len(new_rows)} code(s) added to {sheet}; messages in {out_dir}")
    missing = [k for k in ("KEHAI_PLAYTEST_FORM_URL",) if not s.get(k)] + \
              ([] if s.get("KEHAI_PLAYTEST_ITCH_URL") or s.get("KEHAI_PLAYTEST_MAC_LINK") else ["a download (KEHAI_PLAYTEST_ITCH_URL or KEHAI_PLAYTEST_MAC_LINK)"])
    if missing:
        print("Not in tools/playtest/.env yet, so missing from the messages: " + ", ".join(missing))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
