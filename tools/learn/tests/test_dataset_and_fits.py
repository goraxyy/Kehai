"""C3.2–C3.5: the dataset (split by player, forget), the starting Ledger (one player one vote,
deterministic), and trait fitting (recovers bots' traits it wasn't told)."""
import json
import os
import zipfile

import numpy as np

import dataset as ds
import fit_personas as fp
import fit_start
import records
import tricks
from fixtures import shift, write

CORNER = "corner-1:" + ",".join(["0.5", "0.9", "0.2"] + ["0.5"] * 11)
FREE = "persona-2:" + ",".join(["0.5", "0.2", "0.8"] + ["0.5"] * 11)


def learning_lines(player, picks):
    return "".join(json.dumps({"player": player, "pick": p, "d_panic_20": d, "phase": ph, "hint": h}) + "\n" for p, d, ph, h in picks)


def build(tmp_path):
    src = tmp_path / "src"
    for i, who in enumerate(["reckless:a", "cautious:b", CORNER, FREE] + [f"person-{k}" for k in range(8)]):
        bot = who if ":" in who else ""
        write(str(src), f"s{i}.json", shift(player=who if not bot else "Employee", bot=bot, seed=i))
    d = str(tmp_path / "ds")
    ds.add(d, [str(src)])
    (tmp_path / "l.jsonl").write_text(
        learning_lines("person-0", [("flush:blackout", 0.2, 3, False)] * 9 + [("flush:blackout", 5.0, 3, True)]) +
        learning_lines("person-1", [("flush:blackout", 0.0, 3, False)]) +
        learning_lines("person-2", [("disrupt:spill", 0.1, 2, False)]))
    ds.add(d, [str(tmp_path / "l.jsonl")])
    return d


def test_split_is_by_player_and_holds_out_the_cautious_and_the_corner(tmp_path):
    d = build(tmp_path)
    s = ds.split(d, held_out=0.3)
    assert not set(s["training"]) & set(s["held_out"]), "no player on both sides"
    assert ds.safe_id("cautious:b") in s["held_out"]
    assert ds.safe_id(CORNER) in s["held_out"]
    assert ds.safe_id(FREE) in s["training"] + s["held_out"]
    assert ds.split(d, held_out=0.3) == s, "the same side every time"


def test_forget_removes_a_player_for_good(tmp_path):
    d = build(tmp_path)
    ds.split(d)
    assert ds.forget(d, "person-3")
    assert ds.safe_id("person-3") not in [p for p, _ in ds.players(d)]
    write(str(tmp_path / "again"), "x.json", shift(player="person-3", seed=99))
    ds.add(d, [str(tmp_path / "again")])
    assert ds.safe_id("person-3") not in [p for p, _ in ds.players(d)], "never added again"
    assert all(ds.safe_id("person-3") not in v for v in json.load(open(os.path.join(d, "splits.json"))).values())


def test_session_zip(tmp_path):
    z = tmp_path / "session.zip"
    with zipfile.ZipFile(z, "w") as f:
        f.writestr("meta.json", json.dumps({"player_id": "tester-7"}))
        f.writestr("shift_records/shift_1.json", json.dumps(shift(player="Employee #9", seed=1)))
        f.writestr("learning/learning_1.jsonl", learning_lines("tester-7", [("flush:blackout", 0.1, 2, False)]))
        f.writestr("karen_ledger.json", "{}")
    d = str(tmp_path / "ds")
    ds.add(d, [str(z)])
    assert [w for _, w in ds.players(d)] == ["tester-7"]
    assert len(ds.shifts(d, "tester-7")) == 1 and len(ds.learning(d, "tester-7")) == 1


def test_one_player_one_vote_and_the_hint_left_out(tmp_path):
    d = build(tmp_path)
    json.dump({"training": [ds.safe_id(f"person-{k}") for k in range(8)], "held_out": []}, open(os.path.join(d, "splits.json"), "w"))
    start = fit_start.fit(d, "training", {"blackout": "sight", "spill": "work"})
    # person-0: ten blackouts at 0.2 (one more at 5.0 while the hint fired, left out); person-1: one at 0.0.
    assert start["tricks"]["blackout"][0] == 0.1, "each player's mean counts once"
    assert start["tricks"]["blackout"][3 + 1] == 0.1
    assert start["kinds"]["sight"][0] == 0.1
    assert start["tricks"]["spill"][2 + 1] == 0.1


def test_fitting_twice_gives_the_same_file(tmp_path):
    d = build(tmp_path)
    ds.split(d, held_out=0.0)
    for out in ("a", "b"):
        fit_start.main([d, "--out", str(tmp_path / out)])
    for f in ("LedgerStart.g.cs", "LedgerStart.part.json", "LedgerStart.card.md"):
        assert (tmp_path / "a" / f).read_bytes() == (tmp_path / "b" / f).read_bytes(), f
    assert "LedgerStartTable" in (tmp_path / "a" / "LedgerStart.g.cs").read_text()


def test_pr76_check_compares_with_the_hand_set_guesses(tmp_path):
    d = build(tmp_path)
    json.dump({"training": [ds.safe_id("person-0")], "held_out": [ds.safe_id("person-1")]}, open(os.path.join(d, "splits.json"), "w"))
    start = fit_start.fit(d, "training", {"blackout": "sight"})
    r = fit_start.check(d, start, {"blackout": 0.5})
    assert r["players"] == 1
    assert r["fitted"] < r["hand"]


def test_the_tricks_are_read_from_the_game():
    t = tricks.load()
    assert t["blackout"]["kind"] == "sight"
    assert t["spill"]["kind"] == "work"
    assert all(isinstance(v["prior"], float) for v in t.values())


def test_trait_fitting_recovers_what_shows(tmp_path):
    rng = np.random.default_rng(0)
    traits = rng.uniform(0, 1, size=(200, 14))
    # What shows in a shift depends on pace, caution and habit (as the bots' reflexes do), plus noise.
    m = np.stack([2.0 + traits[:, 0], 0.8 * traits[:, 2] ** 2, 0.3 * traits[:, 1], 0.5 - 0.3 * traits[:, 1],
                  3 * traits[:, 1] * traits[:, 1], 2 * traits[:, 1], 0.2 * traits[:, 5], 0.2 + 0.6 * traits[:, 3]], 1)
    m += rng.normal(0, 0.02, m.shape)
    err = fp.leave_out_error(traits, m)
    for name in ("diligence", "caution", "pace", "habit"):
        assert err[fp.TRAITS.index(name)] < 0.15, f"{name}: {err[fp.TRAITS.index(name)]:.3f}"
    model = fp.Model(traits, m)
    assert {"caution", "pace", "habit"} <= set(model.identifiable())
    assert "grudge" not in model.identifiable(), "a trait the measures can't see is said so"


def test_player_measures_and_ranges(tmp_path):
    s1 = records.load(write(str(tmp_path), "a.json", shift(player="p", seed=1, sprint=0.5)))
    s2 = records.load(write(str(tmp_path), "b.json", shift(player="p", seed=2, sprint=0.5)))
    m = fp.player_measures([s1, s2])
    assert len(m) == len(fp.MEASURES) and 0.3 < m[1] < 0.7
    r = fp.ranges(np.array([[0.2] * 14, [0.6] * 14]))
    assert r["low"][0] == 0.1 and r["high"][0] == 0.7
