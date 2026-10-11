#!/usr/bin/env python3
"""The weekly report for the owner: what the pipeline made and spent, and what to try next
(docs/MARKETING.md).

    uv run weekly_report.py [--week 2026-W40] [--dry-run] [--root DIR]

The numbers are counted here, from the working folder: shifts and moments, picks, edits and
drafts, approvals (once Phase 7 records them), Claude's spend against the cap, the voice quota,
disk use against the budget, alerts. Claude turns them into a short read. Platform stats (views,
followers) join when the YouTube key and Buffer are set up. Writes reports/<week>.md and .json.
Exit codes: see km/cli.py.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import shutil
from pathlib import Path

from km import alerts, moments, paths
from km.cli import run, write_json
from km.llm import client, ledger, prompts
from km.llm.steps import step
from km.tts import usage

BUDGET_GB = 10.0
FLOOR_GB = 3.0


def folder_size(path: Path) -> int:
    return sum(f.stat().st_size for f in path.rglob("*") if f.is_file() and not f.is_symlink()) if path.exists() else 0


def made_between(paths_: list[Path], start: dt.datetime, end: dt.datetime) -> list[Path]:
    return [p for p in paths_ if start <= dt.datetime.fromtimestamp(p.stat().st_mtime) < end]


def numbers(root: Path, week: str, start: dt.date, end: dt.date) -> dict:
    s = dt.datetime.combine(start, dt.time())
    e = dt.datetime.combine(end, dt.time())
    docs = [moments.load(f) for f in moments.find_files(moments.records(), start, end)]
    spend_rows = [r for r in ledger.rows(root) if s <= dt.datetime.fromisoformat(r["time"]) < e]
    by_step: dict[str, float] = {}
    for r in spend_rows:
        by_step[r["step"]] = round(by_step.get(r["step"], 0) + float(r["usd"] or 0), 4)
    approvals = []
    log = root / "state" / "approvals.jsonl"
    if log.exists():
        for line in log.read_text(encoding="utf-8").splitlines():
            if line.strip():
                entry = json.loads(line)
                if s <= dt.datetime.fromisoformat(entry["time"]) < e:
                    approvals.append(entry)
    used = folder_size(root)
    free = shutil.disk_usage(root if root.exists() else Path.home()).free
    picks = root / "plans" / week / "picks.json"
    return {
        "week": week,
        "shifts": {"recorded": len(docs), "minutes": round(sum(d["length"] for d in docs) / 60, 1),
                   "moments": sum(len(d["moments"]) for d in docs), "kept": len(moments.kept(docs)),
                   "best": sorted(({"ref": moments.ref(d["stem"], m["rank"]), "score": m["score"], "tags": m["tags"]}
                                   for d in docs for m in d["moments"]), key=lambda m: -m["score"])[:5]},
        "picks": json.loads(picks.read_text(encoding="utf-8"))["answer"]["shorts"] if picks.exists() else [],
        "edits_written": [p.parent.name for p in made_between(list((root / "edits").glob("*/draft.json")), s, e)],
        "drafts_rendered": [p.name for p in made_between(list((root / "drafts").glob("*.mp4")), s, e)],
        "approvals": {"approved": sum(1 for a in approvals if a.get("decision") == "approved"),
                      "rejected": sum(1 for a in approvals if a.get("decision") == "rejected"),
                      "revised": sum(1 for a in approvals if a.get("decision") == "revised"),
                      "recorded": bool(approvals) or log.exists()},
        "claude": {"week_usd": round(sum(by_step.values()), 4), "by_step": by_step,
                   "month_usd": round(ledger.month_spent(root), 4), "cap_usd": ledger.cap(), "calls": len(spend_rows)},
        "voice": {b: {"month_chars": usage.month_chars(root, b), "quota": usage.quota(b)} for b in ("azure", "elevenlabs")},
        "disk": {"working_folder_gb": round(used / 1e9, 2), "budget_gb": BUDGET_GB,
                 "free_gb": round(free / 1e9, 1), "floor_gb": FLOOR_GB},
        "alerts": [{"kind": x["kind"], "message": x["message"]} for x in alerts.pending(root)][-10:],
        "platform_stats": None,
    }


def markdown(week: str, n: dict, ans: dict) -> str:
    lines = [f"*Kehai marketing — {week}*", "", ans["headline"], "", ans["summary"], ""]
    if ans["highlights"]:
        lines += ["*Highlights*"] + [f"• {h}" for h in ans["highlights"]] + [""]
    if ans["problems"]:
        lines += ["*Problems*"] + [f"• {p}" for p in ans["problems"]] + [""]
    if ans["next_week"]:
        lines += ["*Next week*"] + [f"• {x}" for x in ans["next_week"]] + [""]
    c, d = n["claude"], n["disk"]
    lines += [f"Claude: ${c['week_usd']:.2f} this week, ${c['month_usd']:.2f} of ${c['cap_usd']:.0f} this month · "
              f"Disk: {d['working_folder_gb']} of {d['budget_gb']:.0f} GB, {d['free_gb']} GB free"]
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--week")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--root")
    a = ap.parse_args()
    root = paths.root(a.root)
    start, end, week = moments.week_range(a.week)
    n = numbers(root, week, start, end)
    request = client.Request(step=step("weekly_report"), system=prompts.system("weekly_report"),
                             user="\n\n".join([prompts.data_block("numbers", n), f"Write the report for {week}."]),
                             schema=client.schema("weekly_report"), ref=week)
    ans = client.ask(request, root, dry_run=a.dry_run)
    if ans is None:
        print(json.dumps(n, indent=1, ensure_ascii=False))
        return 0
    out = root / "reports" / f"{week}.md"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(markdown(week, n, ans), encoding="utf-8")
    write_json(out.with_suffix(".json"), {"version": 1, "week": week, "numbers": n, "report": ans})
    print(out.read_text(encoding="utf-8"))
    print(f"weekly_report: wrote {out}")
    return 0


if __name__ == "__main__":
    run(main)
