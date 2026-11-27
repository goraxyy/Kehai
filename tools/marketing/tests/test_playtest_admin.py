"""playtest.py forget (C3.4) and publish (C3.5), against a stand-in for the upload service."""
from __future__ import annotations

import subprocess

import httpx
import pytest

from km.playtest import admin, inbox

TESTER = "A1" * 16


def service(handler) -> inbox.Remote:
    return inbox.Remote("https://pt.example.dev", "admin-key", transport=httpx.MockTransport(handler))


def test_forget_deletes_the_tester_in_the_cloud_and_every_local_folder(tmp_path):
    calls = []

    def handler(request: httpx.Request) -> httpx.Response:
        calls.append((request.method, request.url.path))
        assert request.headers["Authorization"] == "Bearer admin-key"
        return httpx.Response(200, json={"tester": TESTER, "sessions": 2})

    for round_, launch in (("round1", "20261127_101500"), ("round2", "20261201_090000")):
        (tmp_path / round_ / TESTER / launch).mkdir(parents=True)
    (tmp_path / "round1" / "T07" / "20261127_101500").mkdir(parents=True)

    lines = admin.forget(tmp_path, service(handler), TESTER.lower())
    assert calls == [("DELETE", f"/admin/tester/{TESTER}")], "the ID is upper case, as the game makes it"
    assert "2 session(s)" in lines[0] and "2 session folder(s)" in lines[1]
    assert not (tmp_path / "round1" / TESTER).exists() and not (tmp_path / "round2" / TESTER).exists()
    assert (tmp_path / "round1" / "T07").exists(), "another tester stays"
    with pytest.raises(ValueError):
        admin.forget(tmp_path, service(handler), "../etc")


def test_forget_without_the_service_still_clears_this_mac(tmp_path):
    (tmp_path / "round1" / "T07" / "x").mkdir(parents=True)
    lines = admin.forget(tmp_path, None, "T07")
    assert "nothing deleted in the cloud" in lines[0]
    assert not (tmp_path / "round1" / "T07").exists()


def test_publish_puts_both_zips_then_sets_the_current_build(tmp_path):
    mac, win = tmp_path / "Kehai-playtest-round1-macOS.zip", tmp_path / "Kehai-playtest-round1-Windows.zip"
    mac.write_bytes(b"PK"), win.write_bytes(b"PK")
    puts, posted = [], []

    def run(cmd, cwd=None):
        puts.append(cmd[5])
        assert cmd[:5] == ["npx", "wrangler", "r2", "object", "put"] and "--remote" in cmd
        return subprocess.CompletedProcess(cmd, 0)

    def handler(request: httpx.Request) -> httpx.Response:
        posted.append((request.url.path, request.content))
        return httpx.Response(200, json={"round": "round1", "version": "0.5.2-beta.1", "published": "2026-11-27T10:00:00Z"})

    out = admin.publish(service(handler), "round1", "0.5.2-beta.1", mac, win, run=run)
    assert puts == ["kehai-playtests/builds/round1/0.5.2-beta.1/mac.zip", "kehai-playtests/builds/round1/0.5.2-beta.1/windows.zip"]
    assert posted[0][0] == "/admin/latest" and b'"version":"0.5.2-beta.1"' in posted[0][1].replace(b" ", b"")
    assert out["version"] == "0.5.2-beta.1"


def test_publish_stops_before_setting_the_build_if_a_put_fails(tmp_path):
    mac, win = tmp_path / "m.zip", tmp_path / "w.zip"
    mac.write_bytes(b"PK"), win.write_bytes(b"PK")
    posted = []
    with pytest.raises(RuntimeError):
        admin.publish(service(lambda r: posted.append(r) or httpx.Response(200, json={})), "round1", "0.5.2", mac, win,
                      run=lambda cmd, cwd=None: subprocess.CompletedProcess(cmd, 1))
    assert posted == [], "testers never see a build that isn't all there"
    with pytest.raises(ValueError):
        admin.publish(service(lambda r: httpx.Response(200)), "round 1", "0.5.2", mac, win)


def test_the_builds_zips_are_found_by_round(tmp_path):
    folder = tmp_path / "playtest-round1"
    folder.mkdir()
    (folder / "Kehai-playtest-round1-macOS.zip").write_bytes(b"PK")
    (folder / "Kehai-playtest-round1-Windows.zip").write_bytes(b"PK")
    mac, win = admin.tester_zips(tmp_path, "round1")
    assert mac.name.endswith("-macOS.zip") and win.name.endswith("-Windows.zip")
    assert admin.tester_zips(tmp_path, "round2") == (None, None)
