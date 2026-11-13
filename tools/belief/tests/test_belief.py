"""C4.2, C4.4, C4.7: the replay's filter keeps mass, moves it only along edges, and its senses' S0
are today's; a trace replays, and the parity gate notices a difference."""
import json
import os
import math

import numpy as np

import features as feat
import kernels
import make_fixture
import replay
import synthetic


def store():
    return synthetic.build(make_fixture.COLS, make_fixture.ROWS, make_fixture.OPEN)


def test_mass_is_kept_and_moves_only_along_edges():
    m, _ = store()
    for motion in (kernels.HandMotion(), kernels.HandMotion(feat.HAND["p1"])):
        g = kernels.Grid(m, motion)
        g.desire = m.distances([5])
        g.b[:] = 0
        g.b[20] = 1
        g.predict(0.1, 4.0)
        assert abs(g.b.sum() - 1) < 1e-9
        neighbours = set(m.edge_to[m.edge_start[20]:m.edge_start[21]]) | {20}
        moved = set(np.flatnonzero(g.b > 1e-3 * g.prior.max()).tolist())
        assert moved <= neighbours | set(np.flatnonzero(g.prior > 0).tolist())


def test_destination_rates_keep_mass():
    m, _ = store()
    op = dict(make_fixture.FEATURES, places=[dict(p) for p in make_fixture.PLACES])
    for p in op["places"]:
        if p["cell"] >= 0:
            p["x"], p["z"] = float(m.x[p["cell"]]), float(m.z[p["cell"]])
    rate, exit_rate, chance, _ = feat.compute(m, op)
    assert np.allclose(chance.sum(axis=1), 1)
    g = kernels.Grid(m, kernels.DestinationMotion(rate, exit_rate))
    g.predict(0.1, 4.0)
    assert abs(g.b.sum() - 1) < 1e-9


def test_s0_remake_is_todays_numbers():
    op = {"ch": "Hearing", "conf": 0.0, "sigma": 0.0, "raw": {"d": 4.0, "carry": 16.0}}
    conf, sigma = kernels.remake(op, kernels.S0, "s1")
    assert abs(conf - math.sqrt(1 - 4 / 16) * 0.9) < 1e-12
    assert abs(sigma - (1 + 4 * 0.12)) < 1e-12
    op = {"ch": "Sight", "conf": 0.7, "sigma": 0.0, "raw": {"a": 0.5}}
    assert abs(kernels.remake(op, kernels.S0)[1] - (3 + (0.8 - 3) * 0.5)) < 1e-12
    s2 = dict(kernels.S0, wall_gain=1.0)
    op = {"ch": "Hearing", "conf": 0, "sigma": 0, "raw": {"d": 10.0, "carry": 16.0, "straight": 5.0}}
    assert kernels.remake(op, s2, "s2")[1] > kernels.remake(op, s2, "s1")[1]


def write_trace(tmp_path, belief_after_predict, b_value=None):
    m, d = store()
    (tmp_path / "map.json").write_text(json.dumps(d))
    g = kernels.Grid(m)
    g.desire = m.distances([5, 40])
    g.reset_to_prior()
    lines = [{"op": "start", "seed": 1, "shift": 1, "agent": "x"}, {"op": "layers", "desire_sources": [5, 40]},
             {"op": "reset", "x": 0, "y": 0, "z": 0, "sigma": 0, "outside": False}]
    for i in range(30):
        lines.append({"op": "predict", "dt": 0.1, "speed": 4.0})
        g.predict(0.1, 4.0)
        lines.append({"op": "truth", "cell": 20, "sees": i < 5, "x": 0, "z": 0})
        if i % 10 == 9:
            b = g.b if b_value is None else np.full(m.n, b_value)
            lines.append({"op": "check", "b": b.tolist()})
    (tmp_path / "trace_1_1_x.jsonl").write_text("\n".join(json.dumps(x) for x in lines) + "\n")


def test_a_trace_replays_and_passes_the_gate(tmp_path):
    write_trace(tmp_path, True)
    res = replay.parity(str(tmp_path))
    assert res and all(ok for *_, ok in res)
    assert res[0][1] == 3
    r = replay.Replay(replay.tr.load_map(str(tmp_path))).run(str(tmp_path / "trace_1_1_x.jsonl"))
    assert len(r.scores) == 30 and r.log_p(True) < 0


def test_the_gate_fails_on_a_different_belief(tmp_path):
    write_trace(tmp_path, True, b_value=0.5)
    assert not all(ok for *_, ok in replay.parity(str(tmp_path)))


def test_nelder_mead_finds_a_minimum():
    import train
    x, v = train.nelder_mead(lambda p: (p[0] - 1.5) ** 2 + (p[1] + 0.5) ** 2, [0.0, 0.0], iters=200)
    assert abs(x[0] - 1.5) < 1e-2 and abs(x[1] + 0.5) < 1e-2


def test_the_generated_tables_name_real_setters():
    import re
    import train
    cs = train.table_cs("PredictionWeights", "PredictionWeights", train.prediction_numbers(dict(train.feat.HAND)), "t")
    names = set(re.findall(r'w\.Set\("([a-z0-9_]+)"', cs))
    src = open(os.path.join(train.ROOT, "Assets", "!_Project", "_Game", "AI", "Scripts", "Belief", "PredictionWeights.cs")).read()
    for n in names:
        if n.startswith("kind_") or n.startswith("speed_"):
            continue
        assert f'case "{n}"' in src, n
    sense = train.sense_cs({"s1": dict(train.kernels.S0)}, {"version": "t"})
    src = open(os.path.join(train.ROOT, "Assets", "!_Project", "_Game", "AI", "Scripts", "Perception", "SenseModel.cs")).read()
    for n in set(re.findall(r's\.Set\("([a-z0-9_]+)"', sense)):
        assert f'case "{n}"' in src, n


def test_fit_mirror_reads_the_answer_sheet(tmp_path):
    import train
    m, d = store()
    (tmp_path / "map.json").write_text(json.dumps(d))
    lines = [{"op": "start", "t": 0}]
    for i in range(200):
        dist = (i % 20) * 1.5
        lines.append({"op": "truth", "t": i * 0.1, "cell": 1, "x": 0.0, "z": 0.0, "karen": [dist, 0.0], "sees": True, "saw": dist < 12, "heard": False})
    (tmp_path / "trace_1_1_x.jsonl").write_text("\n".join(json.dumps(x) for x in lines) + "\n")
    mw = train.fit_mirror([str(tmp_path)])
    assert mw["samples"] == 200
    assert mw["sight_per_metre"] > 0, "noticing her falls with distance"


def test_a_trace_starts_from_its_state_line(tmp_path):
    m, d = store()
    (tmp_path / "map.json").write_text(json.dumps(d))
    g = kernels.Grid(m)
    rng = np.random.default_rng(2)
    g.prior = rng.uniform(0.5, 1.5, m.n)
    g.prior /= g.prior.sum()
    g.b = rng.uniform(0, 1, m.n)
    g.b /= g.b.sum()
    lines = [{"op": "start", "t": 0}, {"op": "state", "t": 0, "b": g.b.tolist(), "prior": g.prior.tolist(), "lines": [], "line_clock": 0, "last_exit": 0}]
    for i in range(50):
        g.predict(0.1, 4.0)
        lines.append({"op": "predict", "t": i, "dt": 0.1, "speed": 4.0})
    lines.append({"op": "check", "t": 50, "b": g.b.tolist()})
    (tmp_path / "trace_1_1_x.jsonl").write_text("\n".join(json.dumps(x) for x in lines) + "\n")
    res = replay.parity(str(tmp_path))
    assert res[0][3], f"largest difference {res[0][2]}"


def test_a_traces_own_region_table_moves_the_hint():
    # The game's doorways can be numbered differently from one map build to the next (StoreMap
    # finds its doors unsorted), so a trace writes its own region table. The replay's hint must
    # land where that table says, not where map.json's does.
    m, _ = store()
    cells_a = [c for c in range(m.n) if m.hers[c]][:3]
    cells_b = [c for c in range(m.n) if m.hers[c]][-3:]
    table = np.full(m.n, -1)
    table[cells_a] = 0
    table[cells_b] = 1
    r = replay.Replay(m)
    r.step({"op": "regions", "region": table.tolist(), "count": 2})
    before = r.grid.b.copy()
    r.step({"op": "bias", "region": 1, "amount": 0.1})
    gained = r.grid.b - before
    assert all(gained[c] > 0 for c in cells_b)
    assert all(gained[c] <= 0 for c in cells_a)
    assert r.m is not m and len(m.region_cells) == m.regions   # map.json's own table is untouched
