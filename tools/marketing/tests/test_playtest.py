"""PLAYTEST.md, step 3: sessions pulled from the upload service (mocked) or dropped in by hand,
read into facts, summarised (Claude's answer replayed), announced in Telegram (outbox mode), bug
notes filed only after the owner's tap (gh stood in for), and the combined report."""
import io
import json
import os
import re
import stat
import zipfile

import httpx
import pytest

from km import paths
from km import playtest
from km.playtest import inbox, notify, report, session, summary
from km.store import Store
from km.telegram import Bot

LAUNCH, STEM = "20261004_101500", "shift_01_20261004_101530"


def session_zip(code="T07", bugs=("the mop went through the wall",), answers=True, extra=None) -> bytes:
    log = [{"t": 0, "k": "start", "utc": "2026-10-04T14:15:00Z", "round": "round1", "build": "0.1.0 abc1234",
            "code": "", "sends": False, "platform": "OSXPlayer", "os": "Mac OS X 26.5", "cpu": "Apple M2 (8 threads)",
            "ramMB": 8192, "gpu": "Apple M2", "screen": "1440x900", "quality": "PC"},
           {"t": 3, "k": "code", "code": code}, {"t": 5, "k": "consent", "sends": True},
           {"t": 6, "k": "panel", "name": "main menu", "open": False},
           {"t": 7, "k": "key", "key": "E", "target": "nothing"}, {"t": 8, "k": "key", "key": "E", "target": "nothing"},
           {"t": 9, "k": "panel", "name": "task list", "open": True}]
    log += [{"t": 10 + i, "k": "pos", "p": [10 + i, 1, 10], "yaw": 0, "pitch": 0, "move": "walk", "shift": False}
            for i in range(10)]
    log += [{"t": 30, "k": "shift", "n": 1, "state": "start", "stem": STEM}]
    log += [{"t": 31 + i, "k": "pos", "p": [20, 1, 10 + i], "yaw": 90, "pitch": 0, "move": "still" if i > 5 else "walk",
             "shift": True, "shiftTime": 1 + i, "rec": STEM + ".krec", "recTime": 1 + i} for i in range(10)]
    log += [{"t": 60 + i, "k": "bug", "note": note, "shiftTime": 30 + i, "rec": STEM + ".krec", "recTime": 30 + i}
            for i, note in enumerate(bugs)]
    log += [{"t": 200, "k": "shift", "n": 1, "state": "end", "stem": STEM},
            {"t": 100, "k": "fps", "avg": 58.2, "worstMs": 41.0}, {"t": 105, "k": "fps", "avg": 31.5, "worstMs": 90.0},
            {"t": 120, "k": "error", "type": "Exception", "message": "NullReferenceException: boom"}]
    if answers:
        log.append({"t": 210, "k": "answers", "karen": "Scary", "knew": "Mostly", "lost": "Sometimes", "more": "Yes",
                    "broke": "the map was confusing"})
    log.append({"t": 215, "k": "end", "reason": "finished", "shifts": 1, "careerShifts": 1})
    record = {"shift": 1, "length": 170.0, "clockedOut": True,
              "plan": {"bounds": [0, 0, 100, 100], "floor": [[0, 0, 100, 0, 100, 100, 1]],
                       "shapes": [{"k": "Shelf", "c": [10, 10, 20, 10, 20, 20, 10, 20]}], "rooms": [], "pins": [], "sections": {}},
              "analysis": {"numbers": {"Times Karen spotted you": 4, "Catches": 1, "Warning sounds": 3, "Customers served": 2,
                                       "Lowest energy (%)": 40.5},
                           "findings": ["You clocked out after 2:50."], "jobs": [["mopped a spill", 2]]}}
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("session.jsonl", "\n".join(json.dumps(l) for l in log) + "\n")
        z.writestr(f"shifts/{STEM}.json", json.dumps(record))
        z.writestr(f"shifts/{STEM}.krec", b"krec")
        z.writestr("shifts/interlude_01_20261004_101500.krec", b"krec")
        z.writestr("Player.log", "log")
        for name, data in (extra or {}).items():
            z.writestr(name, data)
    return buf.getvalue()


@pytest.fixture
def world(tmp_path, monkeypatch):
    for k in ("TELEGRAM_BOT_TOKEN", "TELEGRAM_CHAT_ID", "ANTHROPIC_API_KEY", "KEHAI_LLM_REPLAY", "KEHAI_DRY_RUN",
              "KEHAI_PLAYTEST_UPLOAD_URL", "KEHAI_PLAYTEST_ADMIN_KEY", "KEHAI_ISSUES_REPO", "KEHAI_GH"):
        monkeypatch.delenv(k, raising=False)
    monkeypatch.setattr("km.env.ENV_FILE", tmp_path / "no.env")
    monkeypatch.setattr("km.env._loaded", False)
    monkeypatch.setattr("km.playtest.ENV_FILE", tmp_path / "no-playtest.env")
    where = tmp_path / "playtests"
    monkeypatch.setenv("KEHAI_PLAYTESTS", str(where))
    marketing = tmp_path / "marketing"
    store = Store(marketing)
    bot = Bot(marketing, store)
    yield marketing, where, bot
    store.close()


def drop(where, code="T07", **kw):
    (where / "inbox").mkdir(parents=True, exist_ok=True)
    path = where / "inbox" / f"round1_{code}_{LAUNCH}.zip"
    path.write_bytes(session_zip(code, **kw))
    return path


def test_a_session_unpacks_into_round_code_launch_and_nothing_escapes(world, tmp_path):
    _, where, _ = world
    folder = inbox.unpack(session_zip(extra={"../escaped.txt": "x", "/abs.txt": "x"}), "round1", "T07", LAUNCH, where)
    assert folder == where / "round1" / "T07" / LAUNCH
    assert (folder / "session.jsonl").exists() and (folder / "shifts" / f"{STEM}.krec").exists()
    assert not (where / "round1" / "T07" / "escaped.txt").exists() and not (tmp_path / "escaped.txt").exists()
    with pytest.raises(inbox.BadSession):
        inbox.unpack(b"not a zip", "round1", "T08", LAUNCH, where)
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("hello.txt", "no session here")
    with pytest.raises(inbox.BadSession):
        inbox.unpack(buf.getvalue(), "round1", "T09", LAUNCH, where)
    assert not (where / "round1" / "T09").exists()


def test_facts_say_how_they_got_on(world):
    _, where, _ = world
    folder = inbox.unpack(session_zip(), "round1", "T07", LAUNCH, where)
    f = session.facts(folder)
    assert (f["code"], f["round"], f["ended"]) == ("T07", "round1", "finished")
    assert f["first_clock_in_s"] == 30 and f["walked_before_clock_in_m"] == pytest.approx(9.0)
    assert f["e_on_nothing"] == 2 and f["task_list_opened"] and not f["map_opened"]
    s = f["shifts"][0]
    assert (s["n"], s["spotted"], s["catches"], s["warnings"], s["clocked_out"]) == (1, 4, 1, 3, True)
    assert f["fps"] == {"avg": 44.9, "lowest": 31.5, "worst_frame_ms": 90.0}
    assert f["errors"] == 1 and f["error_messages"] == ["NullReferenceException: boom"]
    assert f["answers"]["karen"] == "Scary" and f["bugs"][0]["note"] == "the mop went through the wall"
    assert f["replays"] == ["interlude_01_20261004_101500.krec", f"{STEM}.krec"]
    assert {p[2] for p in f["path"]} == {0, 1}, "the path knows shifts from the time between"
    assert session.plan(folder)["bounds"] == [0, 0, 100, 100]


def test_pull_takes_from_the_service_and_deletes_only_what_it_unpacked(world):
    _, where, _ = world
    blobs = {"round1/T07/20261004_101500.zip": session_zip(), "round1/T08/20261004_101600.zip": b"garbage",
             "stray.txt": b"?"}
    deleted = []

    def handler(request: httpx.Request) -> httpx.Response:
        assert request.headers["Authorization"] == "Bearer admin-key"
        key = request.url.path.removeprefix("/admin/sessions").lstrip("/")
        if request.method == "GET" and not key:
            return httpx.Response(200, json={"sessions": [{"key": k, "bytes": len(v)} for k, v in blobs.items()]})
        if request.method == "GET":
            return httpx.Response(200, content=blobs[key])
        if request.method == "DELETE":
            deleted.append(key)
            return httpx.Response(204)
        return httpx.Response(405)

    service = inbox.Remote("https://pt.example.dev", "admin-key", transport=httpx.MockTransport(handler))
    new, notes = inbox.pull(where, service)
    assert new == [where / "round1" / "T07" / "20261004_101500"]
    assert deleted == ["round1/T07/20261004_101500.zip"], "the bad zip stays on the service to look at"
    assert any("T08" in n for n in notes) and any("stray.txt" in n for n in notes)
    new, _ = inbox.pull(where, service)
    assert new == [] and deleted[-1] == "round1/T07/20261004_101500.zip", "already here: just removed there"


def test_a_new_session_is_announced_and_each_bug_note_waits_for_the_owner(world):
    marketing, where, bot = world
    drop(where, bugs=("the mop went through the wall", "stuck behind the till"))
    lines = playtest.cycle(marketing, bot)
    assert any("T07 (round1)" in l for l in lines) and (where / "report.html").exists()
    sent = [m["params"] for m in bot.sent()]
    assert "Playtest: T07" in sent[0]["text"] and "First clock-in after 0:30" in sent[0]["text"]
    assert "Karen felt Scary" in sent[0]["text"] and "NullReference" not in sent[0]["text"]
    bug_messages = sent[1:]
    assert len(bug_messages) == 2
    keyboard = bug_messages[0]["reply_markup"]["inline_keyboard"][0]
    assert [b["callback_data"] for b in keyboard] == [f"pt:f:T07.{LAUNCH}.1", f"pt:n:T07.{LAUNCH}.1"]
    bugs = json.loads((where / "round1" / "T07" / LAUNCH / "bugs.json").read_text())
    assert [b["status"] for b in bugs] == ["asked", "asked"]
    assert bugs[0]["title"] == "Playtest: the mop went through the wall", "no key: the plain template"
    assert (where / "inbox" / "done" / f"round1_T07_{LAUNCH}.zip").exists()


def fake_gh(tmp_path, monkeypatch) -> tuple:
    calls = tmp_path / "gh-calls.txt"
    gh = tmp_path / "gh"
    gh.write_text(f"#!/bin/sh\necho \"$@\" >> '{calls}'\n"
                  "case \"$1 $2\" in 'issue create') echo https://github.com/goraxyy/Kehai/issues/99;; esac\n")
    gh.chmod(gh.stat().st_mode | stat.S_IEXEC)
    monkeypatch.setenv("KEHAI_GH", str(gh))
    return calls


def test_a_bug_note_is_filed_only_after_the_tap_and_only_once(world, tmp_path, monkeypatch):
    marketing, where, bot = world
    calls = fake_gh(tmp_path, monkeypatch)
    drop(where, bugs=("the mop went through the wall", "stuck behind the till"))
    playtest.cycle(marketing, bot)
    assert not calls.exists(), "nothing is filed by itself"
    from km.approvals import Approvals
    approvals = Approvals(marketing, Store(marketing), bot)

    def tap(data, message=500):
        return approvals.handle({"callback_query": {"id": "cb", "data": data, "message": {"message_id": message, "chat": {"id": "owner"}}}})

    assert tap(f"pt:f:T07.{LAUNCH}.1").endswith("filed https://github.com/goraxyy/Kehai/issues/99")
    assert "issue create --repo goraxyy/Kehai --title Playtest: the mop went through the wall" in calls.read_text()
    assert "already filed" in tap(f"pt:f:T07.{LAUNCH}.1")
    assert tap(f"pt:n:T07.{LAUNCH}.2").endswith("dismissed")
    assert calls.read_text().count("issue create") == 1
    bugs = json.loads((where / "round1" / "T07" / LAUNCH / "bugs.json").read_text())
    assert [(b["status"], b["issue"]) for b in bugs] == [("filed", "https://github.com/goraxyy/Kehai/issues/99"), ("dismissed", None)]
    assert "gone" in tap("pt:f:T99.20990101_000000.1")
    html = report.write(where).read_text()
    assert "issues/99" in html and "dismissed" in html


def test_a_dry_run_says_what_it_would_file(world, tmp_path, monkeypatch):
    marketing, where, bot = world
    calls = fake_gh(tmp_path, monkeypatch)
    monkeypatch.setenv("KEHAI_DRY_RUN", "1")
    drop(where)
    playtest.cycle(marketing, bot)
    assert notify.tap(bot, {"id": "cb", "message": {"message_id": 1}}, ["pt", "f", f"T07.{LAUNCH}.1"]).endswith("(dry run: not filed)")
    assert not calls.exists()


SUMMARY = {
    "headline": "Found the time clock in 30 s, then fought the mop.",
    "understood": "They clocked in after half a minute and worked through the task list.",
    "confusions": [{"when": "0:07", "what": "Pressed E on nothing twice before finding the time clock."}],
    "karen": {"verdict": "about right", "why": "Spotted four times, caught once, and they called her scary."},
    "bugs": [{"i": 1, "title": "Mop passes through a wall", "body": "During shift 1 the mop went through a wall. Build 0.1.0, macOS."}],
    "suggestions": ["Light the time clock."],
}


def test_claude_summarises_a_session_and_writes_its_issues(world, tmp_path, monkeypatch):
    marketing, where, bot = world
    from helpers import replay
    monkeypatch.setenv("KEHAI_LLM_REPLAY", str(replay(tmp_path / "replay", {"playtest_summary": SUMMARY})))
    drop(where)
    playtest.cycle(marketing, bot)
    folder = where / "round1" / "T07" / LAUNCH
    assert summary.load(folder)["karen"]["verdict"] == "about right"
    sent = [m["params"]["text"] for m in bot.sent()]
    assert "Found the time clock in 30 s" in sent[0]
    assert "Mop passes through a wall" in sent[1], "the issue Claude wrote, not the tester's words"
    assert "Light the time clock." in (where / "report.html").read_text()


def test_issues_never_name_the_tester_or_link_anywhere():
    facts = {"code": "T07", "bugs": [{"i": 1}]}
    ok = {**SUMMARY}
    assert summary.checks(ok, facts) == []
    named = {**SUMMARY, "bugs": [{"i": 1, "title": "Bug from T07", "body": "x"}]}
    linked = {**SUMMARY, "bugs": [{"i": 1, "title": "Bug", "body": "see https://example.com"}]}
    missing = {**SUMMARY, "bugs": []}
    assert any("tester code" in p for p in summary.checks(named, facts))
    assert any("link" in p for p in summary.checks(linked, facts))
    assert any("one entry per bug note" in p for p in summary.checks(missing, facts))


def test_the_report_totals_answers_and_shows_each_session(world):
    marketing, where, bot = world
    drop(where, code="T07")
    drop(where, code="T08", answers=False, bugs=())
    playtest.cycle(marketing, bot)
    html = (where / "report.html").read_text()
    assert "2 session(s) from 2 tester(s) in 1 round(s)" in html
    assert "found the time clock <b>2/2</b>" in html and "<svg" in html
    assert "Scary <b>1</b>" in html and "the map was confusing" in html
    assert "Kehai → Replay → Open Shift…" in html


def test_the_report_asks_the_questions_playtest_md_lists():
    md = (paths.REPO / "docs" / "production" / "PLAYTEST.md").read_text(encoding="utf-8")
    for _, question, options in report.QUESTIONS:
        row = next((l for l in md.splitlines() if question in l), "")
        assert row, f"PLAYTEST.md doesn't list {question!r}"
        for o in options:
            assert o in row, f"PLAYTEST.md's {question!r} lacks {o!r}"
