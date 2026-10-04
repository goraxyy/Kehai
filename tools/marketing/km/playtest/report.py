"""The combined playtest report: <playtests>/report.html, one page for every round, tester and session.

Per round: the answers totalled, how many found the time clock and how fast, who opened the task
list, how sessions ended, and every tester in one table. Per session: Claude's summary, the map of
where they walked (before the first clock-in and between shifts in grey, during shifts in crimson),
each shift's numbers, the bug notes and what became of them, the computer, and the 3D replays with
how to open them. Self-contained: no scripts, no fonts, nothing fetched.
"""
from __future__ import annotations

import datetime as dt
import html
import json
import statistics
from collections import Counter
from pathlib import Path

from . import sessions
from .notify import clock
from .session import plan

QUESTIONS = [("aiko", "Aiko felt…", ["Scary", "Unfair", "Annoying", "I didn't notice her"]),
             ("knew", "Did you know what to do?", ["Yes", "Mostly", "No"]),
             ("lost", "Did you get lost in the store?", ["Never", "Sometimes", "Often"]),
             ("more", "Would you play more?", ["Yes", "Maybe", "No"])]

# The bars' colours, by the job they do (validated for colour blindness and contrast, light and
# dark): "Aiko felt…" has unordered answers, so categorical slots in a fixed order; the other three
# run good → middle → bad, so a two-pole scale (blue, grey, red). Skipped answers aren't in the bar.
FILLS = {"aiko": ["var(--c1)", "var(--c2)", "var(--c3)", "var(--c4)"]}
SCALE = ["var(--good)", "var(--mid)", "var(--bad)"]


def e(text) -> str:
    return html.escape("" if text is None else str(text))


def load(folder: Path, name: str):
    try:
        return json.loads((folder / name).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def write(where: Path) -> Path:
    found = []
    for folder in sessions(where):
        facts = load(folder, "facts.json")
        if facts is None:
            from .session import write_facts
            facts = write_facts(folder)
        found.append((folder, facts, load(folder, "summary.json"), load(folder, "bugs.json") or []))
    out = where / "report.html"
    where.mkdir(parents=True, exist_ok=True)
    out.write_text(page(found), encoding="utf-8")
    return out


# ---- the page --------------------------------------------------------------------------------

CSS = """
:root{--bg:#f6f5f2;--card:#fff;--ink:#151518;--dim:#6b6b73;--line:#e2e0db;--accent:#dc143c;--path:#9a9aa3;--floor:#ecebe7;--shelf:#cfcdc6}
@media (prefers-color-scheme:dark){:root{--bg:#151518;--card:#1e1e22;--ink:#ededea;--dim:#9a9aa3;--line:#2e2e34;--accent:#ff4d6d;--path:#7b7b84;--floor:#26262b;--shelf:#3a3a41}}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}
main{max-width:1100px;margin:0 auto;padding:24px 16px 64px}h1{font-size:28px;margin:0 0 4px}h2{font-size:22px;margin:40px 0 12px}
h3{font-size:16px;margin:20px 0 8px}.dim{color:var(--dim)}.card{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px;margin:12px 0}
.chips{display:flex;flex-wrap:wrap;gap:8px}.chip{background:var(--card);border:1px solid var(--line);border-radius:999px;padding:4px 12px}
.chip b{font-variant-numeric:tabular-nums}table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}
th,td{text-align:left;padding:6px 8px;border-bottom:1px solid var(--line);vertical-align:top}th{font-weight:600;color:var(--dim);font-size:13px}
.scroll{overflow-x:auto}.bar{display:flex;height:22px;border-radius:6px;overflow:hidden;background:var(--line)}
.bar span{display:block;height:100%;color:#fff;font-size:12px;line-height:22px;padding-left:6px;white-space:nowrap;overflow:hidden}
.q{margin:10px 0}.legend{font-size:13px;color:var(--dim)}details{background:var(--card);border:1px solid var(--line);border-radius:10px;margin:10px 0}
summary{cursor:pointer;padding:12px 16px;font-weight:600}details>div{padding:0 16px 16px}svg{width:100%;height:auto;max-height:520px;background:var(--card)}
code{font-size:13px;word-break:break-all}.tag{display:inline-block;border-radius:4px;padding:0 6px;font-size:12px;border:1px solid var(--line)}
.filed{color:#2e8b57}.asked{color:var(--accent)}ul{margin:4px 0;padding-left:20px}
.bar{display:flex;gap:2px;height:14px;background:none;border-radius:0}.bar span{border-radius:0;padding:0}
.bar span:first-child{border-radius:4px 0 0 4px}.bar span:last-child{border-radius:0 4px 4px 0}.bar span:only-child{border-radius:4px}
.key{display:flex;flex-wrap:wrap;gap:4px 14px;font-size:13px;margin-top:4px}.key i{display:inline-block;width:10px;height:10px;border-radius:2px;margin-right:5px;vertical-align:-1px}
:root{--c1:#2a78d6;--c2:#eb6834;--c3:#1baf7a;--c4:#eda100;--good:#2a78d6;--mid:#d6d5d0;--bad:#e34948}
@media (prefers-color-scheme:dark){:root{--c1:#3987e5;--c2:#d95926;--c3:#199e70;--c4:#c98500;--good:#3987e5;--mid:#4a4a4f;--bad:#e66767}}
"""


def page(found: list) -> str:
    rounds: dict[str, list] = {}
    for item in found:
        rounds.setdefault(item[1]["round"], []).append(item)
    testers = {f["code"] for _, f, _, _ in found}
    parts = [f"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>"
             f"<title>Kehai playtests</title><style>{CSS}</style></head><body><main>",
             "<h1>Kehai playtests</h1>",
             f"<div class='dim'>{len(found)} session(s) from {len(testers)} tester(s) in {len(rounds)} round(s) · "
             f"updated {dt.datetime.now():%Y-%m-%d %H:%M}</div>"]
    if not found:
        parts.append("<p class='card'>No sessions yet. They arrive from the upload service every five minutes, "
                     "or drop a session zip into <code>inbox/</code> next to this page.</p>")
    for name in sorted(rounds, reverse=True):
        parts.append(round_section(name, rounds[name]))
    parts.append("</main></body></html>")
    return "".join(parts)


def median(values) -> float | None:
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def round_section(name: str, items: list) -> str:
    facts = [f for _, f, _, _ in items]
    by_tester: dict[str, list] = {}
    for item in items:
        by_tester.setdefault(item[1]["code"], []).append(item)
    first = [min((f["first_clock_in_s"] for _, f, _, _ in group if f["first_clock_in_s"] is not None), default=None)
             for group in by_tester.values()]
    found_clock = [x for x in first if x is not None]
    task_list = sum(1 for group in by_tester.values() if any(f["task_list_opened"] for _, f, _, _ in group))
    ended = Counter(f["ended"] for f in facts)
    chips = [("testers", len(by_tester)), ("sessions", len(items)),
             ("minutes played", f"{sum(f['minutes'] for f in facts):.0f}"),
             ("found the time clock", f"{len(found_clock)}/{len(by_tester)}"),
             ("median time to clock in", clock(median(found_clock)) if found_clock else "–"),
             ("opened the task list", f"{task_list}/{len(by_tester)}"),
             ("crashed", ended.get("crashed or killed", 0))]
    out = [f"<h2>{e(name)}</h2><div class='chips'>" +
           "".join(f"<span class='chip'>{e(k)} <b>{e(v)}</b></span>" for k, v in chips) + "</div>"]

    answers = [a for f in facts if (a := f.get("answers"))]
    out.append(f"<div class='card'><h3>Answers ({len(answers)})</h3>")
    for key, question, options in QUESTIONS:
        counts = Counter(a.get(key) for a in answers if a.get(key))
        total = sum(counts[o] for o in options)
        fills = FILLS.get(key, SCALE)
        segs = "".join(f"<span style='flex:{counts[o]};background:{fills[i]}' title='{e(o)}: {counts[o]} of {total}'></span>"
                       for i, o in enumerate(options) if counts.get(o))
        legend = "".join(f"<span><i style='background:{fills[i]}'></i>{e(o)} <b>{counts.get(o, 0)}</b></span>" for i, o in enumerate(options))
        skipped = f" <span class='dim'>({counts['skipped']} skipped)</span>" if counts.get("skipped") else ""
        out.append(f"<div class='q'>{e(question)}{skipped}<div class='bar'>{segs}</div><div class='key'>{legend}</div></div>")
    broke = [(f["code"], a["broke"]) for f in facts if (a := f.get("answers")) and a.get("broke")]
    if broke:
        out.append("<h3>Anything break or confuse you?</h3><ul>" +
                   "".join(f"<li><b>{e(c)}</b>: {e(t)}</li>" for c, t in broke) + "</ul>")
    out.append("</div>")

    rows = []
    for code, group in sorted(by_tester.items()):
        fs = [f for _, f, _, _ in group]
        a = next((f["answers"] for f in reversed(fs) if f.get("answers")), {}) or {}
        fps = [f["fps"]["avg"] for f in fs if f["fps"]["avg"]]
        lows = [f["fps"]["lowest"] for f in fs if f["fps"]["lowest"]]
        rows.append("<tr>" + "".join(f"<td>{e(v)}</td>" for v in [
            code, len(fs), f"{sum(f['minutes'] for f in fs):.0f} min", sum(len(f["shifts"]) for f in fs),
            clock(min((f["first_clock_in_s"] for f in fs if f["first_clock_in_s"] is not None), default=None)),
            "yes" if any(f["task_list_opened"] for f in fs) else "no", sum(f["e_on_nothing"] for f in fs),
            ", ".join(sorted({f["ended"] for f in fs})),
            f"{statistics.mean(fps):.0f} / {min(lows):.0f}" if fps and lows else "–", sum(f["errors"] for f in fs),
            a.get("aiko", "–"), a.get("knew", "–"), a.get("lost", "–"), a.get("more", "–"),
            sum(len(f["bugs"]) for f in fs)]) + "</tr>")
    heads = ["Tester", "Sessions", "Played", "Shifts", "First clock-in", "Task list", "E on nothing", "Ended",
             "fps avg / low", "Errors", "Aiko felt", "Knew what to do", "Lost", "Play more", "Bugs"]
    out.append("<div class='card scroll'><table><tr>" + "".join(f"<th>{h}</th>" for h in heads) + "</tr>" +
               "".join(rows) + "</table></div>")

    out.append("<h3>Sessions</h3>")
    for folder, f, summary, bugs in sorted(items, key=lambda i: (i[1]["code"], i[1]["launch"])):
        out.append(session_section(folder, f, summary, bugs))
    return "".join(out)


def session_section(folder: Path, f: dict, summary: dict | None, bugs: list) -> str:
    when = dt.datetime.strptime(f["launch"], "%Y%m%d_%H%M%S").strftime("%Y-%m-%d %H:%M")
    title = (f"{e(f['code'])} · {when} · {f['minutes']:.0f} min · {len(f['shifts'])} shift(s) · {e(f['ended'])}"
             + (f" · {len(f['bugs'])} bug(s)" if f["bugs"] else ""))
    out = [f"<details><summary>{title}</summary><div>"]
    if summary:
        out.append(f"<p><b>{e(summary['headline'])}</b></p><p>{e(summary['understood'])}</p>")
        if summary["confusions"]:
            out.append("<h3>Where it looks confusing</h3><ul>" +
                       "".join(f"<li><b>{e(c['when'])}</b> {e(c['what'])}</li>" for c in summary["confusions"]) + "</ul>")
        out.append(f"<p>Aiko: <b>{e(summary['aiko']['verdict'])}</b>. {e(summary['aiko']['why'])}</p>")
        if summary["suggestions"]:
            out.append("<h3>What to change</h3><ul>" + "".join(f"<li>{e(s)}</li>" for s in summary["suggestions"]) + "</ul>")
    else:
        out.append("<p class='dim'>No summary: Claude runs once ANTHROPIC_API_KEY is in tools/marketing/.env.</p>")

    first = f["first_clock_in_s"]
    out.append("<p>" + (f"First clock-in after <b>{clock(first)}</b>, walking {f['walked_before_clock_in_m']:.0f} m first."
                        if first is not None else f"<b>Never clocked in</b> (walked {f['walked_before_clock_in_m']:.0f} m).") +
               f" Opened: {e(', '.join(f'{k} ×{v}' for k, v in sorted(f['opened'].items())) or 'nothing')}."
               f" E on nothing ×{f['e_on_nothing']}. Stood still {clock(f['still_s'])}. Window left ×{f['focus_lost']}.</p>")
    out.append(map_svg(folder, f))

    if f["shifts"]:
        heads = ["Shift", "Length", "Clocked out", "Spotted", "Caught", "Warnings", "Served", "Lowest energy", "What happened"]
        rows = "".join("<tr>" + "".join(f"<td>{v}</td>" for v in [
            e(s.get("n")), clock(s.get("length_s")), "yes" if s.get("clocked_out") else "no", e(s.get("spotted", "–")),
            e(s.get("catches", "–")), e(s.get("warnings", "–")), e(s.get("served", "–")),
            f"{s['lowest_energy']:.0f}%" if isinstance(s.get("lowest_energy"), (int, float)) else "–",
            "<br>".join(e(x) for x in s.get("findings", [])[:3])]) + "</tr>" for s in f["shifts"])
        out.append("<div class='scroll'><table><tr>" + "".join(f"<th>{h}</th>" for h in heads) + f"</tr>{rows}</table></div>")

    if f["bugs"]:
        status = {b["i"]: b for b in bugs}
        items = []
        for b in f["bugs"]:
            s = status.get(b["i"])
            tag = ("<span class='tag filed'>filed</span> " + e(s["issue"]) if s and s["status"] == "filed"
                   else f"<span class='tag {e(s['status'])}'>{e(s['status'])}</span>" if s else "")
            items.append(f"<li>{e(b['note'] or '(no note)')} <span class='dim'>· {e(b.get('rec') or '')} at "
                         f"{clock(b.get('rec_time'))}</span> {tag}</li>")
        out.append("<h3>Bug notes</h3><ul>" + "".join(items) + "</ul>")

    c = f["computer"]
    out.append(f"<h3>Computer</h3><p class='dim'>{e(c.get('os'))} · {e(c.get('device'))} · {e(c.get('cpu'))} · "
               f"{e(c.get('ramMB'))} MB · {e(c.get('gpu'))} ({e(c.get('graphics'))}) · {e(c.get('screen'))} · "
               f"quality {e(c.get('quality'))} · frame rate {e(f['fps']['avg'])} avg, {e(f['fps']['lowest'])} low, "
               f"worst frame {e(f['fps']['worst_frame_ms'])} ms · build {e(f.get('build'))}</p>")
    if f["error_messages"]:
        out.append(f"<p><b>{f['errors']} error(s)</b></p><ul>" + "".join(f"<li><code>{e(m)}</code></li>" for m in f["error_messages"]) + "</ul>")
    if f["replays"]:
        out.append("<h3>3D replays</h3><p class='dim'>In Unity: <b>Kehai → Replay → Open Shift…</b> and pick one. "
                   "<code>interlude_</code> files are the time before a clock-in or between shifts; "
                   "<code>.krec.part</code> was cut short by a crash.</p><ul>" +
                   "".join(f"<li><code>{e(folder / 'shifts' / r)}</code></li>" for r in f["replays"]) + "</ul>")
    out.append("</div></details>")
    return "".join(out)


def map_svg(folder: Path, f: dict) -> str:
    """The store from above, and where they walked."""
    p = plan(folder)
    path = f["path"]
    if not p and not path:
        return ""
    if p:
        x0, y0, x1, y1 = p["bounds"]
    else:
        xs, ys = [q[0] for q in path], [q[1] for q in path]
        x0, y0, x1, y1 = min(xs) - 5, min(ys) - 5, max(xs) + 5, max(ys) + 5
    w, h = x1 - x0, y1 - y0

    def X(x): return f"{x - x0:.1f}"
    def Y(y): return f"{y1 - y:.1f}"          # north up

    parts = [f"<svg viewBox='0 0 {w:.1f} {h:.1f}' role='img' aria-label='Where {e(f['code'])} walked'>"]
    if p:
        for t in p.get("floor", []):
            parts.append(f"<polygon points='{X(t[0])},{Y(t[1])} {X(t[2])},{Y(t[3])} {X(t[4])},{Y(t[5])}' fill='var(--floor)'/>")
        for s in p.get("shapes", []):
            c = s.get("c") or []
            pts = " ".join(f"{X(c[i])},{Y(c[i + 1])}" for i in range(0, len(c) - 1, 2))
            parts.append(f"<polygon points='{pts}' fill='var(--shelf)'/>")
    for flag, colour in ((0, "var(--path)"), (1, "var(--accent)")):
        run: list[str] = []
        for q in path + [[None, None, -1]]:
            if q[2] == flag:
                run.append(f"{X(q[0])},{Y(q[1])}")
            elif run:
                if len(run) > 1:
                    parts.append(f"<polyline points='{' '.join(run)}' fill='none' stroke='{colour}' stroke-width='0.6' "
                                 f"stroke-linejoin='round' opacity='0.85'/>")
                run = []
    if path:
        parts.append(f"<circle cx='{X(path[0][0])}' cy='{Y(path[0][1])}' r='1.4' fill='var(--ink)'><title>Start</title></circle>")
    parts.append("</svg>")
    return ("<div class='legend'>Grey: before the first clock-in and between shifts. Crimson: during shifts. "
            "The dot: where they started.</div>" + "".join(parts))
