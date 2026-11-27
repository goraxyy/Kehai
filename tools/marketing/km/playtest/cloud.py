"""The report from the cloud's rows (playtest v2, C3.2): `playtest.py report --cloud`.

Every round and session the upload service holds, read from its D1 tables (GET /admin/rows/<table>,
a page at a time), with nothing downloaded but the rows: the funnel (launched, agreed, first shift,
finished), downloads by day, and each session's numbers, answers, bug notes and summary. Each
session says how to open its replays (`playtest.py open <id>`, C3.3). Self-contained HTML.
"""
from __future__ import annotations

import datetime as dt
import html
import json
from collections import Counter, defaultdict
from pathlib import Path

from .inbox import Remote
from .report import CSS, QUESTIONS

TABLES = ("sessions", "shifts", "answers", "moments", "events", "summaries", "downloads")
FUNNEL = (("launch", "launched"), ("consent", "agreed to send"), ("walkthrough", "saw the walkthrough"),
          ("first_shift", "started a shift"), ("end", "finished"))


def e(text) -> str:
    return html.escape("" if text is None else str(text))


def rows(service: Remote, table: str, limit: int = 1000) -> list[dict]:
    out, after = [], 0
    while True:
        r = service.http.get(f"{service.url}/admin/rows/{table}", params={"after": after, "limit": limit})
        r.raise_for_status()
        page = r.json()
        out += page["rows"]
        if page.get("next") is None:
            return out
        after = page["next"]


def gather(service: Remote) -> dict:
    return {t: rows(service, t) for t in TABLES}


def page(data: dict, now: dt.datetime | None = None) -> str:
    now = now or dt.datetime.now()
    live = [s for s in data["sessions"] if not s.get("deleted")]
    by = defaultdict(lambda: defaultdict(list))
    for table in ("shifts", "moments"):
        for r in data[table]:
            by[table][r["session"]].append(r)
    answers = {a["session"]: a for a in data["answers"]}
    summaries = {s["session"]: s["text"] for s in data["summaries"]}
    rounds = defaultdict(list)
    for s in live:
        rounds[s["round"]].append(s)

    parts = [f"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
             f"<title>Kehai playtests (cloud)</title><style>{CSS}</style></head><body><main>",
             "<h1>Kehai playtests, from the cloud</h1>",
             f"<div class='dim'>{len(live)} session(s) from {len({s['tester'] for s in live})} tester(s) in {len(rounds)} round(s) · "
             f"{sum(1 for s in data['sessions'] if s.get('deleted'))} removed (six months, or a request) · updated {now:%Y-%m-%d %H:%M}</div>"]

    # The funnel: each step's testers, by the events their games sent after agreeing.
    who = defaultdict(set)
    for ev in data["events"]:
        who[ev["kind"]].add(ev["tester"])
    parts.append("<h2>The funnel</h2><div class='chips'>" +
                 "".join(f"<span class='chip'>{e(label)} <b>{len(who[k])}</b></span>" for k, label in FUNNEL) + "</div>"
                 "<p class='dim'>Only testers who agreed to send are counted: the game sends nothing for the others.</p>")
    days = Counter()
    for d in data["downloads"]:
        days[(d["day"], d["platform"])] += d["count"]
    if days:
        parts.append("<h2>Downloads</h2><div class='scroll'><table><tr><th>Day</th><th>Mac</th><th>Windows</th></tr>" +
                     "".join(f"<tr><td>{e(day)}</td><td>{days[(day, 'mac')]}</td><td>{days[(day, 'windows')]}</td></tr>"
                             for day in sorted({d for d, _ in days}, reverse=True)) + "</table></div>")

    if not live:
        parts.append("<p class='card'>No sessions in the cloud yet.</p>")
    for name in sorted(rounds, reverse=True):
        items = sorted(rounds[name], key=lambda s: s["id"])
        parts.append(f"<h2>{e(name)}</h2>")
        taps = defaultdict(Counter)
        for s in items:
            a = answers.get(s["id"])
            for (qid, _, _), v in zip(QUESTIONS, json.loads(a["taps"]) if a and a.get("taps") else []):
                if v:
                    taps[qid][v] += 1
        if taps:
            parts.append("<div class='card'>" + "".join(
                f"<div class='q'><b>{e(text)}</b> " + " · ".join(f"{e(o)} {taps[qid][o]}" for o in options) + "</div>"
                for qid, text, options in QUESTIONS) + "</div>")
        parts.append("<div class='scroll'><table><tr><th>Session</th><th>Build</th><th>Minutes</th><th>Shifts</th><th>Caught</th>"
                     "<th>Ended</th><th>Bug notes</th><th>Complete</th></tr>")
        for s in items:
            sh = by["shifts"][s["id"]]
            parts.append(f"<tr><td><a href='#{e(s['id'])}'>{e(s['tester'][:8])}… {e(s['launch'][:15])}</a></td><td>{e(s.get('build'))}</td>"
                         f"<td>{e(round(s['minutes'], 1) if s.get('minutes') is not None else '')}</td><td>{len(sh)}</td>"
                         f"<td>{sum(int(x.get('caught') or 0) for x in sh)}</td><td>{e(s.get('ended'))}</td>"
                         f"<td>{len(by['moments'][s['id']])}</td><td>{'yes' if s.get('summary_received') and s.get('zip_received') else 'zip only' if s.get('zip_received') else 'rows only'}</td></tr>")
        parts.append("</table></div>")
        for s in items:
            a = answers.get(s["id"]) or {}
            parts.append(f"<details id='{e(s['id'])}'><summary>{e(s['id'])}</summary><div>")
            if summaries.get(s["id"]):
                parts.append(f"<p>{e(summaries[s['id']])}</p>")
            parts.append(f"<p class='dim'>{e(s.get('computer'))} · {e(s.get('fps'))} fps · consent {e(s.get('consent_version'))}"
                         f"{', may train Karen' if s.get('consent_training') else ''} · kept until {e(str(s.get('expires', ''))[:10])}</p>")
            for k in ("a1", "a2", "a3"):
                if a.get(k):
                    parts.append(f"<p>“{e(a[k])}”</p>")
            notes = by["moments"][s["id"]]
            if notes:
                parts.append("<ul>" + "".join(f"<li>shift {e(m.get('shift'))} at {e(m.get('replay_s'))} s: {e(m.get('note'))}</li>" for m in notes) + "</ul>")
            parts.append(f"<p>Replays: <code>uv run playtest.py open {e(s['id'])}</code></p></div></details>")
    parts.append("</main></body></html>")
    return "".join(parts)


def write(service: Remote, out: Path) -> Path:
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(page(gather(service)), encoding="utf-8")
    return out
