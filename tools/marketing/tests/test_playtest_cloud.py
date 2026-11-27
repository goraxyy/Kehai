"""playtest.py report --cloud (C3.2): the report from the upload service's rows."""
from __future__ import annotations

import json

import httpx

from km.playtest import cloud, inbox

T = "A1" * 16
SID = f"round1/{T}/20261127_101500-{'ab' * 16}"
GONE = f"round1/{T}/20260501_000000-{'cd' * 16}"

TABLES = {
    "sessions": [
        {"_row": 1, "id": SID, "tester": T, "round": "round1", "launch": SID.split("/")[2], "build": "0.5.2-beta.1", "minutes": 21.53,
         "ended": "finished", "computer": "MacBookAir", "fps": 58.2, "consent_version": "v2", "consent_training": 1,
         "zip_received": "x", "summary_received": "x", "expires": "2027-05-27T00:00:00Z", "deleted": None},
        {"_row": 2, "id": GONE, "tester": T, "round": "round1", "launch": GONE.split("/")[2], "deleted": "expired"},
    ],
    "shifts": [{"_row": 1, "session": SID, "n": 1, "caught": 0}, {"_row": 2, "session": SID, "n": 2, "caught": 1}],
    "answers": [{"_row": 1, "session": SID, "taps": json.dumps(["Scary", None, "Never", "Yes"]), "a1": "Find the mop", "a2": None, "a3": None}],
    "moments": [{"_row": 1, "session": SID, "n": 1, "shift": 2, "replay_s": 133.5, "note": "stuck on the shelf <b>"}],
    "events": [{"_row": i, "tester": T, "kind": k} for i, k in enumerate(["launch", "consent", "first_shift"], 1)],
    "summaries": [{"_row": 1, "session": SID, "text": "Got stuck once; caught in shift 2."}],
    "downloads": [{"_row": 1, "day": "2026-11-27", "platform": "mac", "count": 3}],
}


def service(calls):
    def handler(request: httpx.Request) -> httpx.Response:
        table = request.url.path.rsplit("/", 1)[1]
        after, limit = int(request.url.params["after"]), int(request.url.params["limit"])
        calls.append((table, after))
        page = [r for r in TABLES[table] if r["_row"] > after][:limit]
        return httpx.Response(200, json={"rows": page, "next": page[-1]["_row"] if len(page) == limit else None})
    return inbox.Remote("https://pt.example.dev", "admin-key", transport=httpx.MockTransport(handler))


def test_rows_are_read_a_page_at_a_time():
    calls = []
    got = cloud.rows(service(calls), "shifts", limit=1)
    assert [r["n"] for r in got] == [1, 2]
    assert calls == [("shifts", 0), ("shifts", 1), ("shifts", 2)]


def test_the_cloud_report_shows_the_funnel_sessions_answers_and_how_to_open_them(tmp_path):
    out = cloud.write(service([]), tmp_path / "cloud.html")
    page = out.read_text()
    assert "1 session(s) from 1 tester(s)" in page and "1 removed" in page, "the expired one is only counted"
    assert "agreed to send <b>1</b>" in page and "finished <b>0</b>" in page
    assert "Scary 1" in page and "Never 1" in page
    assert "Got stuck once" in page and "“Find the mop”" in page
    assert "stuck on the shelf &lt;b&gt;" in page, "tester text is escaped"
    assert f"playtest.py open {SID}" in page
    assert "<td>3</td><td>0</td>" in page, "downloads by day and platform"
    assert GONE not in page
