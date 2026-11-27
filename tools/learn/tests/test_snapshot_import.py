"""Playtest v2, C3.8: a downloaded snapshot (tools/playtest/dataset.py fetch) into the dataset."""
import json
import zipfile

import dataset as ds

T1, T2 = "A1" * 16, "B2" * 16


def session_zip(path, learning=True, shift_name="shift_03_20261127_101500.json"):
    with zipfile.ZipFile(path, "w") as z:
        z.writestr("session.jsonl", '{"k":"start"}\n')
        z.writestr("shifts/" + shift_name, json.dumps({"shift": 3, "player": "Employee", "frames": []}))
        z.writestr("shifts/shift_03_20261127_101500.markers.json", "{}")
        z.writestr("karen_ledger.json", "{}")
        if learning:
            z.writestr("learning/learning_20261127_101500.jsonl", '{"pick":"wait"}\n{"pick":"check"}\n')
            z.writestr("learning/learning_20261127_101500.escapes.jsonl", '{"e":1}\n')


def snapshot(tmp_path, sessions):
    folder = tmp_path / "snap"
    (folder / "zips").mkdir(parents=True, exist_ok=True)
    rows = []
    for sid, tester, opts in sessions:
        flat = sid.replace("/", "_")
        if opts.get("zip", True):
            session_zip(folder / "zips" / (flat + ".zip"), learning=opts.get("learning", True))
        rows.append({"id": sid, "tester": tester, "round": "round1", "build": "0.5.2-beta.1", "schema": opts.get("schema", 2),
                     "consent_version": opts.get("consent", "v2"), "zip_sha256": "0" * 64, "expires": "2027-05-27T00:00:00Z",
                     "status": opts.get("status", "done"), **({"why": opts["why"]} if "why" in opts else {})})
    (folder / "snapshot.json").write_text(json.dumps({"sessions": rows}))
    return folder


def ids(n):
    return f"round1/{T1}/20261127_1{n}1500-{'ab' * 16}"


def test_sessions_go_in_by_player_with_their_learning_records_and_only_once(tmp_path):
    data = tmp_path / "data"
    snap = snapshot(tmp_path, [(ids(0), T1, {}), (ids(1), T1, {}), (f"round1/{T2}/20261127_120000-{'cd' * 16}", T2, {})])
    r = ds.import_snapshot(str(data), str(snap))
    assert len(r["imported"]) == 3 and not r["refused"]
    pid = ds.safe_id(T1)
    assert len(ds.learning(str(data), pid)) == 4, "two sessions of two decisions, the escapes left out"
    shift_files = sorted((data / "players" / pid / "shifts").iterdir())
    assert len(shift_files) == 2, "each session's shift kept apart, none overwritten (the markers left out)"
    assert {p for p, _ in ds.players(str(data))} == {ds.safe_id(T1), ds.safe_id(T2)}
    again = ds.import_snapshot(str(data), str(snap))
    assert not again["imported"] and len(again["already"]) == 3
    assert len(ds.learning(str(data), pid)) == 4


def test_incompatible_and_unconsented_sessions_are_refused_with_a_reason(tmp_path):
    data = tmp_path / "data"
    snap = snapshot(tmp_path, [
        (ids(0), T1, {"learning": False}),
        (ids(1), T1, {"consent": "v1"}),
        (ids(2), T1, {"schema": 1}),
        (ids(3), T1, {"status": "rejected", "why": "its hash is wrong"}),
    ])
    r = ds.import_snapshot(str(data), str(snap))
    assert not r["imported"]
    why = r["refused"]
    assert "replays alone" in why[ids(0)]
    assert "no consent" in why[ids(1)]
    assert "schema 1" in why[ids(2)]
    assert "not downloaded" in why[ids(3)]
    assert ds.main(["import", str(data), str(snap)]) == 1, "the command fails loudly when something is refused"


def test_a_deletion_request_forgets_the_player_and_an_expired_session_goes_alone(tmp_path):
    data = tmp_path / "data"
    t2 = f"round1/{T2}/20261127_120000-{'cd' * 16}"
    snap = snapshot(tmp_path, [(ids(0), T1, {}), (ids(1), T1, {}), (t2, T2, {})])
    ds.import_snapshot(str(data), str(snap))

    rows = json.loads((snap / "snapshot.json").read_text())
    for s in rows["sessions"]:
        if s["id"] == ids(0):
            s.update(status="removed", why="expired")
        if s["id"] == t2:
            s.update(status="removed", why="request")
    (snap / "snapshot.json").write_text(json.dumps(rows))
    r = ds.import_snapshot(str(data), str(snap))
    assert r["removed"] == [ids(0)] and r["forgotten"] == [T2]
    assert len(ds.learning(str(data), ds.safe_id(T1))) == 2, "only the expired session's records went"
    assert ds.safe_id(T2) not in {p for p, _ in ds.players(str(data))}
    assert ds.safe_id(T2) in ds.forgotten(str(data)), "and they're never added again"
    index = json.loads((data / "sessions.json").read_text())
    assert set(index) == {ids(1).replace("/", "_")}
