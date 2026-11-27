"""playtest.py open (C3.3): one session into a self-emptying cache; and the scheduled pull is opt-in (C3.1)."""
from __future__ import annotations

import io
import os
import time
import zipfile

import httpx
import pytest

from km import playtest
from km.playtest import cache, inbox

T = "A1" * 16
LAUNCH = f"20261127_101500-{'ab' * 16}"


def session_zip(extra: bytes = b"") -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        z.writestr("session.jsonl", '{"k":"start"}\n')
        z.writestr("shifts/shift_01.krec", b"rec" + extra)
    return buf.getvalue()


def test_open_fetches_once_then_reuses_and_never_deletes_in_the_cloud(tmp_path):
    calls = []

    def handler(request: httpx.Request) -> httpx.Response:
        calls.append((request.method, request.url.path))
        return httpx.Response(200, content=session_zip())

    service = inbox.Remote("https://pt.example.dev", "admin-key", transport=httpx.MockTransport(handler))
    folder = cache.open_session(service, f"round1/{T}/{LAUNCH}", tmp_path)
    assert folder == tmp_path / "round1" / T / LAUNCH
    assert (folder / "shifts" / "shift_01.krec").is_file()
    cache.open_session(service, f"round1/{T}/{LAUNCH}.zip", tmp_path)
    assert calls == [("GET", f"/admin/sessions/round1/{T}/{LAUNCH}.zip")], "fetched once, only read"
    with pytest.raises(ValueError):
        cache.open_session(service, "../../etc", tmp_path)


def test_the_cache_empties_itself_after_seven_days_or_past_its_size(tmp_path):
    def put(tester: str, age_days: float, size: int):
        folder = tmp_path / "round1" / tester / "20261127_101500"
        folder.mkdir(parents=True)
        (folder / "data").write_bytes(b"x" * size)
        t = time.time() - age_days * 86400
        os.utime(folder, (t, t))
        return folder

    old = put("T01", 8, 10)
    a = put("T02", 3, 600)
    b = put("T03", 2, 600)
    c = put("T04", 1, 600)
    gone = cache.prune(tmp_path, limit=1300, keep=a)
    assert old in gone and not old.exists(), "not opened for a week"
    assert not b.exists() and a.exists() and c.exists(), "past the limit the least recently opened goes, but never the one being opened"
    assert not (tmp_path / "round1" / "T01").exists(), "no empty folders left"


def test_the_scheduled_pull_is_off_unless_asked(monkeypatch):
    monkeypatch.setenv("KEHAI_PLAYTEST_AUTO_PULL", "0")
    assert playtest.auto_pull() is False
    monkeypatch.setenv("KEHAI_PLAYTEST_AUTO_PULL", "1")
    assert playtest.auto_pull() is True
