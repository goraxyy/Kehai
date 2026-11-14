"""Post-loss truth labels score the existing replay; they never change its beliefs."""
import json
from pathlib import Path

import numpy as np
import pytest

import features
import kernels
import refit_escapes as refit
import replay
import synthetic
import train


def source(folder, seed=1, agent="efficient", layout="today"):
    folder.mkdir()
    m, data = synthetic.build(3, 5, None)
    (folder / "map.json").write_text(json.dumps(data))
    g = kernels.Grid(m)
    g.reset_to_prior()
    rows = [dict(op="start", t=0, seed=seed, shift=2, agent=agent, layout=layout),
            dict(op="state", t=0, b=g.b.tolist(), prior=g.prior.tolist(), lines=[], line_clock=0, last_exit=0)]
    for t, cell, sees in [(0, 0, True), (1, 0, False), (2, 3, False), (17, 12, False), (20, 6, True)]:
        g.predict(.1, 4)
        rows += [dict(op="predict", t=t, dt=.1, speed=4), dict(op="check", t=t, b=g.b.tolist()),
                 dict(op="truth", t=t, cell=cell, sees=sees, x=float(m.x[cell]), z=float(m.z[cell]))]
    path = folder / f"trace_{seed}_2_{agent}.jsonl"
    path.write_text("".join(json.dumps(r)+"\n" for r in rows))
    return path


def manifest(tmp_path):
    source(tmp_path / "train", seed=1)
    source(tmp_path / "val", seed=2)
    data = dict(version=1, sources=[dict(folder="train", player="bot1", bot=True, split="train"),
                                    dict(folder="val", player="bot2", bot=True, split="val")])
    path = tmp_path / "manifest.json"
    path.write_text(json.dumps(data))
    return path, data


def test_post_loss_weights_do_not_change_the_posterior(tmp_path):
    path = source(tmp_path / "data")
    m, _ = synthetic.build(3, 5, None)
    plain = replay.Replay(m, motion="p1").run(str(path))
    weighted = replay.Replay(m, motion="p1", post_loss_weight=4).run(str(path))
    np.testing.assert_array_equal(plain.grid.b, weighted.grid.b)
    assert weighted.score_weights == [1, 4, 4, 1, 1]
    assert weighted.losses[0]["next"] == 1
    assert weighted.losses[0]["from"] not in weighted.losses[0]["top_three"]
    assert weighted.log_p(True) != plain.log_p(True)


def test_dry_run_extracts_and_reports_without_any_fit(tmp_path, monkeypatch):
    path, _ = manifest(tmp_path)
    monkeypatch.setattr(train, "fit_p1", lambda *a, **k: pytest.fail("dry run fitted a model"))
    result = refit.run(path, tmp_path / "out", dry_run=True)
    assert not result["trained"] and not result["exported"]
    assert result["baseline"]["val"]["losses"] == 1
    rows = [json.loads(x) for x in (tmp_path / "out/escapes.jsonl").read_text().splitlines()]
    assert len(rows) == 2 and rows[0]["next"] == 1
    assert not (tmp_path / "out/candidate").exists()


def test_refit_calls_existing_p1_objective_with_player_balance(tmp_path, monkeypatch):
    path, _ = manifest(tmp_path)
    monkeypatch.setattr(train, "WORKERS", 1)
    calls = []
    def no_optimization(f, x0, **kwargs):
        calls.append(f(x0))  # one real evaluation of P1, no training run
        return x0, calls[-1]
    monkeypatch.setattr(train, "nelder_mead", no_optimization)
    result = refit.run(path, tmp_path / "out")
    assert len(calls) == 1 and np.isfinite(calls[0])
    assert result["gains"] == features.HAND["p1"]
    assert not result["exported"], "a tied candidate is not a gain"


@pytest.mark.parametrize("change", ["player_leak", "cautious", "layout", "no_consent", "duplicate"])
def test_manifest_rejects_unusable_training_inputs(tmp_path, change):
    path, data = manifest(tmp_path)
    if change == "player_leak": data["sources"][1]["player"] = "bot1"
    if change == "no_consent": data["sources"][0]["bot"] = False
    if change == "duplicate": data["sources"].append(data["sources"][0])
    if change in ("cautious", "layout"):
        trace = next((tmp_path / "train").glob("*.jsonl"))
        text = trace.read_text().replace('"efficient"', '"cautious"') if change == "cautious" else trace.read_text().replace('"today"', '"rings"')
        trace.write_text(text)
    path.write_text(json.dumps(data))
    with pytest.raises(ValueError): refit.load_manifest(path)


def test_forgotten_player_is_excluded_before_the_fit(tmp_path):
    path, data = manifest(tmp_path)
    data["dataset"] = "dataset"
    (tmp_path / "dataset").mkdir()
    (tmp_path / "dataset/forgotten.txt").write_text("bot1\n")
    path.write_text(json.dumps(data))
    with pytest.raises(ValueError, match="nonempty"):
        refit.load_manifest(path)


def test_bad_parity_prevents_extraction_and_fitting(tmp_path):
    path, _ = manifest(tmp_path)
    trace = next((tmp_path / "train").glob("*.jsonl"))
    rows = [json.loads(r) for r in trace.read_text().splitlines()]
    next(r for r in rows if r["op"] == "check")["b"][0] = 1.0
    trace.write_text("".join(json.dumps(r)+"\n" for r in rows))
    with pytest.raises(SystemExit, match="parity gate failed"):
        refit.run(path, tmp_path / "out", dry_run=True)
    assert not (tmp_path / "out").exists()


def test_legacy_truth_without_timestamps_keeps_scores_but_invents_no_escape(tmp_path):
    path = source(tmp_path / "legacy")
    rows = [json.loads(line) for line in path.read_text().splitlines()]
    for row in rows:
        row.pop("t", None)
    path.write_text("".join(json.dumps(row)+"\n" for row in rows))
    m, _ = synthetic.build(3, 5, None)
    r = replay.Replay(m, post_loss_weight=4).run(str(path))
    assert len(r.scores) == 5 and np.isfinite(r.log_p(True))
    assert r.losses == [] and r.score_weights == [1]*5
    assert replay.parity(str(path.parent))[0][3]


def test_top_three_ranks_exits_not_unreachable_distant_regions():
    m, _ = synthetic.build(3, 5, None)
    r = replay.Replay(m)
    r.grid.b[:] = 0
    r.grid.b[12] = 1  # all probability in a far region, not an exit from row zero
    r.step(dict(op="truth", t=0, cell=0, sees=True))
    r.step(dict(op="truth", t=1, cell=3, sees=False))
    assert r.losses[0]["top_three"] == [1]
    assert r.losses[0]["next"] == 1
