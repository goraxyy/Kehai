#!/usr/bin/env python3
"""The dataset (0.4.0, build plan §6.3–6.4, C3.3): everyone's shifts, one folder per player.

    python3 tools/learn/dataset.py add <dataset> <shift_records folder | learning .jsonl | session .zip>…
    python3 tools/learn/dataset.py split <dataset> [--held-out 0.2]
    python3 tools/learn/dataset.py forget <dataset> <player-id>
    python3 tools/learn/dataset.py table <dataset>
    python3 tools/learn/dataset.py import <dataset> <snapshot folder>   (playtest v2, C3.8: tools/playtest/dataset.py fetch's folder)

Layout:
    <dataset>/players/<player-id>/shifts/*.json        shift records (the game's ShiftRecording)
    <dataset>/players/<player-id>/learning.jsonl       learning records (one per decision)
    <dataset>/players/<player-id>/ledger.json          the latest Ledger snapshot (for "where people hide" only)
    <dataset>/splits.json                              which players are held out (judging only)
    <dataset>/forgotten.txt                            players removed with `forget`: never added again

The rules it keeps (§6.4):
    split by player, never by shift: a player is wholly in training or wholly held out, chosen by a
    hash of their id, so adding shifts never moves anyone between sides;
    bots are split by who they are: the scripted cautious profile and the careful corner (caution
    ≥ 0.75, pace ≤ 0.3) are always held out;
    one player, one vote: `weights()` gives each player the same total weight however many shifts;
    forgetting works: `forget` deletes a player and the next fit is made without them.
Standard library only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import zipfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import records  # noqa: E402

CORNER_CAUTION, CORNER_PACE = 0.75, 0.3      # EvalSplits.CornerCaution, CornerPace


def safe_id(player: str) -> str:
    keep = "".join(c if c.isalnum() or c in "-_." else "_" for c in player)
    return (keep[:48] + "_" + hashlib.sha1(player.encode()).hexdigest()[:8]) if len(keep) > 56 else keep


def persona_traits(label: str):
    """A bot's 14 traits from its label ("name:t1,t2,…"), or None."""
    if ":" not in label:
        return None
    try:
        t = [float(x) for x in label.split(":", 1)[1].split(",")]
    except ValueError:
        return None
    return t if len(t) == 14 else None


def always_held_out(player: str) -> bool:
    name = player.split(":", 1)[0].lower()
    if name == "cautious":
        return True
    t = persona_traits(player)
    return t is not None and t[1] >= CORNER_CAUTION and t[2] <= CORNER_PACE


def forgotten(dataset: str) -> set:
    p = os.path.join(dataset, "forgotten.txt")
    return set(open(p).read().split()) if os.path.exists(p) else set()


def player_dir(dataset: str, player: str) -> str:
    return os.path.join(dataset, "players", safe_id(player))


def add_shift(dataset: str, path: str, player: str | None = None) -> str | None:
    s = records.load(path)
    who = player or s.player
    pid = safe_id(who)
    if pid in forgotten(dataset):
        return None
    d = os.path.join(player_dir(dataset, who), "shifts")
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(player_dir(dataset, who), "player.txt"), "w") as f:
        f.write(who + "\n")
    shutil.copyfile(path, os.path.join(d, os.path.basename(path)))
    return pid


def add_learning(dataset: str, path: str) -> int:
    n, gone = 0, forgotten(dataset)
    with open(path) as f:
        for line in f:
            if not line.strip():
                continue
            r = json.loads(line)
            who = r.get("player", "unknown")
            if safe_id(who) in gone:
                continue
            d = player_dir(dataset, who)
            os.makedirs(d, exist_ok=True)
            with open(os.path.join(d, "learning.jsonl"), "a") as out:
                out.write(json.dumps(r, sort_keys=True) + "\n")
            n += 1
    return n


def add_escapes(dataset: str, path: str, player: str | None = None) -> int:
    """Typed truth sidecar; never mix escape answers into policy-decision learning rows."""
    rows = [json.loads(line) for line in open(path) if line.strip()]
    return add_escape_rows(dataset, rows, player)


def add_escape_rows(dataset: str, rows: list, player: str | None = None) -> int:
    n, gone = 0, forgotten(dataset)
    for row in rows:
        who = player or row.get("player")
        if not who or row.get("version") != 1 or not isinstance(row.get("path"), list) or not row.get("from"):
            raise ValueError("invalid escape record or missing player identity")
        if safe_id(who) in gone:
            continue
        folder = player_dir(dataset, who)
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "player.txt"), "w") as f:
            f.write(who + "\n")
        # Upload retries must not turn a single escape into repeated evidence.
        target = os.path.join(folder, "escapes.jsonl")
        identity = lambda r: (r.get("seed"), r.get("shift"), r.get("layout"), r.get("t"))
        existing = {identity(json.loads(line)) for line in open(target) if line.strip()} if os.path.exists(target) else set()
        if identity(row) in existing:
            continue
        with open(target, "a") as f:
            f.write(json.dumps(dict(row, player=who), sort_keys=True) + "\n")
        n += 1
    return n


def add_zip(dataset: str, path: str) -> list:
    """A playtest session zip: meta.json (player_id), shift records, learning records, a Ledger."""
    added = []
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        meta = json.loads(z.read("meta.json")) if "meta.json" in names else {}
        who = meta.get("player_id") or os.path.splitext(os.path.basename(path))[0]
        if safe_id(who) in forgotten(dataset):
            return added
        base = player_dir(dataset, who)
        os.makedirs(os.path.join(base, "shifts"), exist_ok=True)
        with open(os.path.join(base, "player.txt"), "w") as f:
            f.write(who + "\n")
        for n in names:
            low = n.lower()
            if low.endswith(".json") and "shift" in low and not low.endswith(".markers.json"):
                with open(os.path.join(base, "shifts", os.path.basename(n)), "wb") as f:
                    f.write(z.read(n))
                added.append(n)
            elif low.endswith(".escapes.jsonl"):
                rows = [json.loads(line) for line in z.read(n).decode("utf-8").splitlines() if line.strip()]
                add_escape_rows(dataset, rows, who)
                added.append(n)
            elif low.endswith(".jsonl") and "learning" in low:
                with open(os.path.join(base, "learning.jsonl"), "ab") as f:
                    f.write(z.read(n))
                added.append(n)
            elif os.path.basename(low) == "karen_ledger.json":
                with open(os.path.join(base, "ledger.json"), "wb") as f:
                    f.write(z.read(n))
                added.append(n)
    return added


def add(dataset: str, sources) -> int:
    count = 0
    for src in sources:
        if src.endswith(".zip"):
            count += len(add_zip(dataset, src))
        elif src.endswith(".escapes.jsonl"):
            count += add_escapes(dataset, src)
        elif src.endswith(".jsonl"):
            count += add_learning(dataset, src)
        else:
            for p in records.find([src]):
                count += add_shift(dataset, p) is not None
    return count


def players(dataset: str) -> list:
    root = os.path.join(dataset, "players")
    if not os.path.isdir(root):
        return []
    out = []
    for pid in sorted(os.listdir(root)):
        txt = os.path.join(root, pid, "player.txt")
        out.append((pid, open(txt).read().strip() if os.path.exists(txt) else pid))
    return out


def split(dataset: str, held_out: float = 0.2) -> dict:
    """Training or held out, by player: always the same side for the same id."""
    result = {"training": [], "held_out": []}
    for pid, who in players(dataset):
        h = int(hashlib.sha256(who.encode()).hexdigest()[:8], 16) / 0xFFFFFFFF
        side = "held_out" if always_held_out(who) or h < held_out else "training"
        result[side].append(pid)
    with open(os.path.join(dataset, "splits.json"), "w") as f:
        json.dump(result, f, indent=1, sort_keys=True)
    return result


def forget(dataset: str, player: str) -> bool:
    pid = player if os.path.isdir(os.path.join(dataset, "players", player)) else safe_id(player)
    d = os.path.join(dataset, "players", pid)
    existed = os.path.isdir(d)
    if existed:
        shutil.rmtree(d)
    os.makedirs(dataset, exist_ok=True)
    with open(os.path.join(dataset, "forgotten.txt"), "a") as f:
        f.write(pid + "\n")
    sp = os.path.join(dataset, "splits.json")
    if os.path.exists(sp):
        s = json.load(open(sp))
        for side in s.values():
            if pid in side:
                side.remove(pid)
        json.dump(s, open(sp, "w"), indent=1, sort_keys=True)
    return existed


def shifts(dataset: str, pid: str) -> list:
    return records.load_all([os.path.join(dataset, "players", pid, "shifts")]) \
        if os.path.isdir(os.path.join(dataset, "players", pid, "shifts")) else []


def learning(dataset: str, pid: str) -> list:
    base = os.path.join(dataset, "players", pid)
    paths = [os.path.join(base, "learning.jsonl")]
    snap = os.path.join(base, "learning.sessions")   # one file per imported playtest session (C3.8)
    if os.path.isdir(snap):
        paths += [os.path.join(snap, f) for f in sorted(os.listdir(snap)) if f.endswith(".jsonl")]
    return [json.loads(x) for p in paths if os.path.exists(p) for x in open(p) if x.strip()]


# ---- playtest v2, C3.8: a downloaded snapshot (tools/playtest/dataset.py fetch) into the dataset ----

SCHEMA_MIN = 2   # the summary schema the game writes since playtest v2


def import_snapshot(dataset: str, folder: str, schema_min: int = SCHEMA_MIN) -> dict:
    """Imports each session the snapshot fetched, under its tester's ID, so splits stay by player.
    Refused, with the reason in the report: no consent to training, an older schema, or no
    learning records (replays alone aren't a training set). Sessions the cloud has removed since are
    removed here: a deletion request forgets the player; an expired session goes on its own.
    Importing again changes nothing: each session is kept once (<dataset>/sessions.json)."""
    snap_path = os.path.join(folder, "snapshot.json")
    if not os.path.exists(snap_path):
        raise SystemExit(f"{folder}: no snapshot.json (make one with tools/playtest/dataset.py fetch)")
    snap = json.load(open(snap_path))
    index_path = os.path.join(dataset, "sessions.json")
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    report = {"imported": [], "already": [], "refused": {}, "removed": [], "forgotten": []}
    gone_players = set()
    for s in snap.get("sessions", []):
        sid, tester = s.get("id", ""), s.get("tester", "")
        flat = sid.replace("/", "_")
        if s.get("status") == "removed":
            if s.get("why") == "request" and tester not in gone_players:
                forget(dataset, tester)
                gone_players.add(tester)
                report["forgotten"].append(tester)
            elif flat in index:
                _remove_session(dataset, index.pop(flat))
                report["removed"].append(sid)
            index.pop(flat, None)
            continue
        if flat in index:
            report["already"].append(sid)
            continue
        why = _refusal(dataset, s, folder, schema_min)
        if why:
            report["refused"][sid] = why
            continue
        index[flat] = _import_session(dataset, s, os.path.join(folder, "zips", flat + ".zip"))
        report["imported"].append(sid)
    for tester in gone_players:   # a forgotten player's sessions leave the index too
        for flat in [f for f, e in index.items() if e.get("tester") == tester]:
            index.pop(flat)
    os.makedirs(dataset, exist_ok=True)
    with open(index_path, "w") as f:
        json.dump(index, f, indent=2, sort_keys=True)
    return report


def _refusal(dataset: str, s: dict, folder: str, schema_min: int):
    if s.get("status") != "done":
        return f"not downloaded ({s.get('status')}{': ' + s['why'] if s.get('why') else ''})"
    if s.get("consent_version") in (None, "", "v1"):
        return "no consent to training (v1)"
    if int(s.get("schema") or 0) < schema_min:
        return f"summary schema {s.get('schema')} is older than {schema_min}"
    if safe_id(s.get("tester", "")) in forgotten(dataset):
        return "this player was forgotten"
    zp = os.path.join(folder, "zips", s["id"].replace("/", "_") + ".zip")
    if not os.path.exists(zp):
        return "its zip is missing from the snapshot folder"
    with zipfile.ZipFile(zp) as z:
        if not any(os.path.basename(n).startswith("learning_") and n.endswith(".jsonl") and not n.endswith(".escapes.jsonl") for n in z.namelist()):
            return "no learning records: replays alone aren't a training set"
    return None


def _import_session(dataset: str, s: dict, zip_path: str) -> dict:
    tester, flat = s["tester"], s["id"].replace("/", "_")
    base = player_dir(dataset, tester)
    os.makedirs(os.path.join(base, "shifts"), exist_ok=True)
    os.makedirs(os.path.join(base, "learning.sessions"), exist_ok=True)
    with open(os.path.join(base, "player.txt"), "w") as f:
        f.write(tester + "\n")
    files = []
    with zipfile.ZipFile(zip_path) as z:
        learning_lines = b""
        for n in z.namelist():
            name, low = os.path.basename(n), n.lower()
            if low.endswith(".json") and name.startswith("shift_") and not low.endswith(".markers.json"):
                target = os.path.join(base, "shifts", f"{flat}__{name}")
                with open(target, "wb") as f:
                    f.write(z.read(n))
                files.append(os.path.relpath(target, dataset))
            elif name.startswith("learning_") and low.endswith(".jsonl") and not low.endswith(".escapes.jsonl"):
                learning_lines += z.read(n).rstrip(b"\n") + b"\n"
            elif name == "karen_ledger.json":
                with open(os.path.join(base, "ledger.json"), "wb") as f:
                    f.write(z.read(n))
        target = os.path.join(base, "learning.sessions", flat + ".jsonl")
        with open(target, "wb") as f:
            f.write(learning_lines)
        files.append(os.path.relpath(target, dataset))
    return {"tester": tester, "files": files, "sha256": s.get("zip_sha256"), "build": s.get("build"), "schema": s.get("schema"),
            "consent_version": s.get("consent_version"), "expires": s.get("expires")}


def _remove_session(dataset: str, entry: dict) -> None:
    for rel in entry.get("files", []):
        p = os.path.join(dataset, rel)
        if os.path.exists(p):
            os.remove(p)


def weights(items_by_player: dict) -> dict:
    """One player, one vote: each item's weight, so every player's items sum to 1."""
    return {pid: (1.0 / len(items) if items else 0.0) for pid, items in items_by_player.items()}


def table(dataset: str) -> list:
    rows = []
    sides = json.load(open(os.path.join(dataset, "splits.json"))) if os.path.exists(os.path.join(dataset, "splits.json")) else {}
    for pid, who in players(dataset):
        side = next((k for k, v in sides.items() if pid in v), "unsplit")
        rows.append({"player": who, "id": pid, "shifts": len(shifts(dataset, pid)), "decisions": len(learning(dataset, pid)), "side": side})
    return rows


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    a1 = sub.add_parser("add"); a1.add_argument("dataset"); a1.add_argument("sources", nargs="+")
    a2 = sub.add_parser("split"); a2.add_argument("dataset"); a2.add_argument("--held-out", type=float, default=0.2)
    a3 = sub.add_parser("forget"); a3.add_argument("dataset"); a3.add_argument("player")
    a4 = sub.add_parser("table"); a4.add_argument("dataset")
    a5 = sub.add_parser("import"); a5.add_argument("dataset"); a5.add_argument("snapshot")
    a = ap.parse_args(argv)
    if a.cmd == "import":
        r = import_snapshot(a.dataset, a.snapshot)
        print(f"imported {len(r['imported'])}, already here {len(r['already'])}, removed {len(r['removed'])}, "
              f"players forgotten {len(r['forgotten'])}, refused {len(r['refused'])}")
        for sid, why in sorted(r["refused"].items()):
            print(f"  refused {sid}: {why}")
        return 1 if r["refused"] else 0
    if a.cmd == "add":
        print(f"added {add(a.dataset, a.sources)} files")
    elif a.cmd == "split":
        s = split(a.dataset, a.held_out)
        print(f"{len(s['training'])} players in training, {len(s['held_out'])} held out")
    elif a.cmd == "forget":
        print("forgotten" if forget(a.dataset, a.player) else "not in the dataset; will never be added")
    else:
        for r in table(a.dataset):
            print(f"{r['side']:9} {r['shifts']:4} shifts {r['decisions']:6} decisions  {r['player']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
