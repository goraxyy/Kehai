"""dataset.py against a local stand-in for the upload service's read-only routes.

    python -m unittest discover -s tools/playtest/tests        (no installs; also on Windows)
"""
from __future__ import annotations

import hashlib
import io
import json
import sys
import tempfile
import threading
import unittest
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import dataset  # noqa: E402

KEY = "dataset-key-0123456789abcdef"
T = "A1" * 16
GOOD = f"round1/{T}/20261127_101500-{'ab' * 16}"
BADHASH = f"round1/{T}/20261127_111500-{'cd' * 16}"
UNSAFE = f"round1/{T}/20261127_121500-{'ef' * 16}"
OTHER = "round2/T07/20261128_090000"


def zip_of(files: dict[str, bytes]) -> bytes:
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for name, data in files.items():
            z.writestr(name, data)
    return buf.getvalue()


class Cloud:
    """What the service holds, and every request it got."""

    def __init__(self):
        self.zips = {
            GOOD: zip_of({"session.jsonl": b'{"k":"start"}\n', "shifts/shift_01.krec": b"rec"}),
            BADHASH: zip_of({"session.jsonl": b"{}\n"}),
            UNSAFE: zip_of({"session.jsonl": b"{}\n", "../../escape.txt": b"x"}),
            OTHER: zip_of({"session.jsonl": b"{}\n"}),
        }
        self.expires = {k: "2027-05-27T00:00:00Z" for k in self.zips}
        self.deleted: list[dict] = []
        self.requests: list[tuple[str, str]] = []
        self.cut_short: set[str] = set()   # these die half way, once

    def manifest(self, round_):
        rows = []
        for sid, data in sorted(self.zips.items()):
            if round_ and not sid.startswith(round_ + "/"):
                continue
            sha = hashlib.sha256(data).hexdigest() if sid != BADHASH else "0" * 64
            rows.append({"id": sid, "round": sid.split("/")[0], "build": "0.5.2", "tester": sid.split("/")[1], "schema": 2,
                         "consent_version": "v2", "zip_sha256": sha, "zip_bytes": len(data), "collected": "2026-11-27T10:00:00Z",
                         "expires": self.expires[sid], "complete": True})
        return {"generated": "2026-11-27T12:00:00Z", "sessions": rows}


def serve(cloud: Cloud):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def send(self, code, body: bytes, kind="application/json"):
            self.send_response(code)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            cloud.requests.append(("GET", self.path))
            if self.headers.get("Authorization") != "Bearer " + KEY:
                return self.send(401, b"no\n", "text/plain")
            path, _, query = self.path.partition("?")
            params = dict(p.split("=", 1) for p in query.split("&") if "=" in p)
            if path == "/dataset/manifest":
                return self.send(200, json.dumps(cloud.manifest(params.get("round"))).encode())
            if path == "/dataset/deletions":
                return self.send(200, json.dumps({"deletions": cloud.deleted}).encode())
            if path.startswith("/dataset/sessions/") and path.endswith(".zip"):
                sid = path[len("/dataset/sessions/"):-4]
                if sid not in cloud.zips:
                    return self.send(404, b"gone\n", "text/plain")
                data = cloud.zips[sid]
                if sid in cloud.cut_short:
                    cloud.cut_short.discard(sid)
                    self.send_response(200)
                    self.send_header("Content-Length", str(len(data)))
                    self.end_headers()
                    self.wfile.write(data[: len(data) // 2])
                    self.close_connection = True
                    return
                return self.send(200, data, "application/zip")
            self.send(404, b"nothing here\n", "text/plain")

        def do_DELETE(self):   # the dataset tool must never send one
            cloud.requests.append(("DELETE", self.path))
            self.send(405, b"no\n", "text/plain")

        do_PUT = do_POST = do_DELETE

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


class DatasetTests(unittest.TestCase):
    def setUp(self):
        self.cloud = Cloud()
        self.server = serve(self.cloud)
        self.service = dataset.Service(f"http://127.0.0.1:{self.server.server_address[1]}", KEY, timeout=10)
        self.dir = Path(tempfile.mkdtemp(prefix="kehai-dataset-"))
        self.lines: list[str] = []

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        import shutil
        shutil.rmtree(self.dir, ignore_errors=True)

    def fetch(self, **kw):
        return dataset.fetch(self.service, self.dir, log=self.lines.append, **kw)

    def downloads(self):
        return [p for m, p in self.cloud.requests if p.startswith("/dataset/sessions/")]

    def test_a_snapshot_is_fixed_checked_and_only_ever_read(self):
        snap = self.fetch(round_="round1")
        status = {s["id"]: s["status"] for s in snap["sessions"]}
        self.assertEqual(status, {GOOD: "done", BADHASH: "rejected", UNSAFE: "rejected"}, "round2 isn't in a round1 snapshot")
        self.assertTrue((self.dir / "sessions" / dataset.flat(GOOD) / "shifts" / "shift_01.krec").is_file())
        self.assertFalse((self.dir / "escape.txt").exists() or (self.dir.parent / "escape.txt").exists())
        self.assertFalse((self.dir / "sessions" / dataset.flat(UNSAFE)).exists(), "an unsafe zip isn't unpacked at all")
        why = {s["id"]: s.get("why", "") for s in snap["sessions"]}
        self.assertIn("hash", why[BADHASH])
        self.assertIn("unsafe", why[UNSAFE])
        saved = json.loads((self.dir / "snapshot.json").read_text())
        self.assertEqual(saved["filters"]["round"], "round1")
        self.assertRegex(saved["sessions"][0]["zip_sha256"], "^[0-9a-f]{64}$")
        self.assertEqual({m for m, _ in self.cloud.requests}, {"GET"}, "it never writes, deletes or publishes")

    def test_a_rerun_carries_on_without_fetching_anything_twice(self):
        self.fetch(round_="round1")
        before = len([d for d in self.downloads() if GOOD in d])
        self.cloud.zips[f"round1/{T}/20261127_131500-{'aa' * 16}"] = zip_of({"session.jsonl": b"{}\n"})   # arrives later
        self.cloud.expires[f"round1/{T}/20261127_131500-{'aa' * 16}"] = "2027-05-27T00:00:00Z"
        snap = self.fetch(round_="round1")
        self.assertEqual(len([d for d in self.downloads() if GOOD in d]), before, "done is done")
        self.assertEqual(len(snap["sessions"]), 3, "the same fixed list: a later session isn't added")
        self.assertIn("carrying on", " ".join(self.lines))
        self.assertEqual(len(self.fetch(round_="round1", new=True)["sessions"]), 4, "--new makes a new list")

    def test_a_download_cut_short_is_fetched_again(self):
        self.cloud.cut_short.add(GOOD)
        first = self.fetch(round_="round1", only=[GOOD])
        self.assertEqual(first["sessions"][0]["status"], "rejected", "half a zip fails its check")
        self.assertFalse((self.dir / "sessions" / dataset.flat(GOOD)).exists(), "and isn't unpacked")
        second = self.fetch()
        self.assertEqual(second["sessions"][0]["status"], "done", "the rerun fetches it whole")
        self.assertTrue((self.dir / "sessions" / dataset.flat(GOOD) / ".verified").is_file())

    def test_deleted_and_expired_sessions_are_removed_here_too(self):
        self.fetch(only=[GOOD, OTHER])
        self.cloud.deleted = [{"id": GOOD, "reason": "request", "deleted_at": "2026-11-28T00:00:00Z"}]
        removed = dataset.reconcile(self.service, self.dir, log=self.lines.append)
        self.assertEqual(removed, 1)
        self.assertFalse((self.dir / "sessions" / dataset.flat(GOOD)).exists())
        self.assertFalse((self.dir / "zips" / (dataset.flat(GOOD) + ".zip")).exists())
        self.assertTrue((self.dir / "sessions" / dataset.flat(OTHER)).exists())
        snap = json.loads((self.dir / "snapshot.json").read_text())
        status = {s["id"]: (s["status"], s.get("why")) for s in snap["sessions"]}
        self.assertEqual(status[GOOD], ("removed", "request"))

        # Past its date, a local copy goes before the folder is used again, even with no word from the cloud.
        snap["sessions"] = [{**s, "expires": "2026-01-01T00:00:00Z"} if s["id"] == OTHER else s for s in snap["sessions"]]
        (self.dir / "snapshot.json").write_text(json.dumps(snap))
        self.cloud.deleted = []
        self.fetch()
        self.assertFalse((self.dir / "sessions" / dataset.flat(OTHER)).exists())
        self.assertFalse(any(GOOD in d for d in self.downloads()[2:]), "a removed session isn't fetched again")

    def test_a_wrong_key_says_so(self):
        bad = dataset.Service(self.service.url, "wrong-key-0123456789abcdef", timeout=10)
        with self.assertRaises(dataset.DatasetError) as e:
            dataset.fetch(bad, self.dir, log=self.lines.append)
        self.assertIn("401", str(e.exception))
        self.assertFalse((self.dir / "snapshot.json").exists())

    def test_unsafe_paths(self):
        for name in ["../x", "a/../../x", "/etc/passwd", "C:/x", "a\\..\\x", ""]:
            self.assertTrue(dataset.unsafe_entry(name), name)
        for name in ["session.jsonl", "shifts/shift_01.krec", "karen_logs/a.json"]:
            self.assertFalse(dataset.unsafe_entry(name), name)


if __name__ == "__main__":
    unittest.main()
