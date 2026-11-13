"""C4.10's fits run end to end on a small synthetic trace, and move the right way."""
import json
import math

import numpy as np

import features as feat
import kernels
import make_fixture
import synthetic
import train


def store():
    return synthetic.build(make_fixture.COLS, make_fixture.ROWS, make_fixture.OPEN)


def write_trace(tmp_path, seed=1, shift=3, agent="efficient", n=40):
    m, d = store()
    (tmp_path / "map.json").write_text(json.dumps(d))
    g = kernels.Grid(m)
    g.desire = m.distances([5, 40])
    g.reset_to_prior()
    lines = [{"op": "start", "t": 100.0, "seed": seed, "shift": shift, "agent": agent},
             {"op": "layers", "t": 100.0, "desire_sources": [5, 40]},
             {"op": "state", "t": 100.0, "b": g.b.tolist(), "prior": g.prior.tolist(), "lines": [], "line_clock": 0, "last_exit": 0}]
    places = [dict(kind="Job", cell=5, waited=5.0, x=float(m.x[5]), z=float(m.z[5]), answer_s=-1, habit=0),
              dict(kind="Clock", cell=40, waited=0.0, x=float(m.x[40]), z=float(m.z[40]), answer_s=-1, habit=0),
              dict(kind="Stay", cell=-1, waited=0.0, x=0, z=0, answer_s=-1, habit=0)]
    truth = 20
    for i in range(n):
        t = 100.0 + i * 0.1
        if i % 10 == 0:
            lines.append(dict(make_fixture.FEATURES, op="features", t=t, places=places))
        if i % 8 == 0:
            # A noise near where the player is, its σ too wide: the senses' fit should want it narrower.
            lines.append({"op": "obs", "t": t, "ch": "Hearing", "x": float(m.x[truth]), "z": float(m.z[truth]), "conf": 0.6, "sigma": 6.0,
                          "cell": truth, "outside": False, "raw": {"d": 4.0, "carry": 20.0, "kind": 0}})
        lines.append({"op": "predict", "t": t, "dt": 0.1, "speed": 4.0})
        lines.append({"op": "truth", "t": t, "cell": truth, "sees": False, "x": float(m.x[truth]), "z": float(m.z[truth])})
    (tmp_path / f"trace_{seed}_{shift}_{agent}.jsonl").write_text("\n".join(json.dumps(x) for x in lines) + "\n")
    return m


def test_fit_senses_improves_the_training_score(tmp_path):
    write_trace(tmp_path)
    nums, val, base = train.fit_senses([str(tmp_path)], [str(tmp_path)], "s1")
    assert set(train.SENSE_NAMES) <= set(nums)
    assert val >= base - 1e-9, "the fit is never worse than today's numbers on the data it was fitted on"


def test_fit_p1_runs(tmp_path):
    write_trace(tmp_path)
    gains, val = train.fit_p1([str(tmp_path)], [str(tmp_path)], {"id": "s0"})
    assert set(gains) == {"moving", "desire", "habit", "danger", "speed"} and not math.isnan(val)


def test_stage1_learns_where_the_legs_go_and_matches_its_own_trace(tmp_path):
    m = write_trace(tmp_path, seed=7, shift=3, agent="efficient")
    clock = (float(m.x[40]), float(m.z[40]))
    legs = [{"found": "logged", "seed": 7, "shift": 3, "agent": "efficient", "t0": 0.5, "from": [float(m.x[20]), float(m.z[20])],
             "to": list(clock), "speed": 4.0, "metres": 10.0} for _ in range(30)]
    other = [dict(l, seed=8) for l in legs]          # another career's shift 3: not this trace's
    w = train.stage1(legs + other, [str(tmp_path)])
    assert w["legs_used"] == 30, "only the legs of this trace's own career"
    assert w["kind"][feat.KINDS.index("Clock")] > w["kind"][feat.KINDS.index("Job")]
    w2, val = train.stage2(w, [str(tmp_path)], [str(tmp_path)], {"id": "s0"})
    assert not math.isnan(val)


def test_todays_sense_numbers_sit_inside_the_search_bounds():
    # The search starts at today's numbers; a bound that clips them makes every step it tries
    # worse than the start, and the fit silently returns today's numbers.
    import kernels
    import train
    names = train.SENSE_NAMES + train.S2_EXTRA
    lo, hi = train.sense_bounds(names)
    for k, a, b in zip(names, lo, hi):
        v = kernels.S0[k]
        assert a <= v <= b, f"{k} = {v} outside [{a}, {b}]"
