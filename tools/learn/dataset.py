#!/usr/bin/env python3
"""The dataset (0.4.0, build plan §6.3–6.4, C3.3): everyone's shifts, one folder per player.

    python3 tools/learn/dataset.py add <dataset> <shift_records folder | learning .jsonl | session .zip>…
    python3 tools/learn/dataset.py split <dataset> [--held-out 0.2]
    python3 tools/learn/dataset.py forget <dataset> <player-id>
    python3 tools/learn/dataset.py table <dataset>

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
    p = os.path.join(dataset, "players", pid, "learning.jsonl")
    return [json.loads(x) for x in open(p) if x.strip()] if os.path.exists(p) else []


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
    a = ap.parse_args(argv)
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
