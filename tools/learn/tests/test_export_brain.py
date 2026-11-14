"""C5.7 / C2.6: the export turns the trainer's actor into numbers the C# runs identically."""
import json
import os

import numpy as np
import pytest

torch = pytest.importorskip("torch")
pytest.importorskip("mlagents")
import export_brain as eb  # noqa: E402
import make_brain_fixture as fx  # noqa: E402


def test_our_arithmetic_matches_the_trainer(tmp_path):
    actor = fx.tiny_actor(seed=5)
    r = eb.export(actor.state_dict(), "Probe", {"version": "t", "outputs": [["a"] * 4, ["b"] * 3]}, str(tmp_path))
    assert r["parity"] < 1e-5
    cs = (tmp_path / "Probe.g.cs").read_text()
    assert "public static class Probe" in cs and "Kehai.Karen.BrainNet Build()" in cs
    part = json.loads((tmp_path / "Probe.part.json").read_text())
    assert part["kind"] == "brain" and len(part["heads"]) == 2
    assert "Parity with the trainer" in (tmp_path / "Probe.card.md").read_text()


def test_a_wrong_outputs_file_is_refused(tmp_path):
    actor = fx.tiny_actor(seed=5)
    with pytest.raises(SystemExit):
        eb.export(actor.state_dict(), "Probe", {"outputs": [["a"] * 5, ["b"] * 3]}, str(tmp_path))
    with pytest.raises(SystemExit):
        eb.export(actor.state_dict(), "Probe", {"input_size": 99}, str(tmp_path))
    assert not os.path.exists(tmp_path / "Probe.g.cs"), "nothing written when it's refused"


def test_a_checkpoint_on_disk_round_trips(tmp_path):
    actor = fx.tiny_actor(seed=8)
    run = tmp_path / "run" / "Employee"
    run.mkdir(parents=True)
    torch.save({"Policy": actor.state_dict()}, run / "checkpoint.pt")
    torch.save({"Policy": actor.state_dict()}, run / "Employee-1500.pt")
    eb.main([str(tmp_path / "run"), "--as", "EmployeeBrain", "--out", str(tmp_path / "out")])
    assert (tmp_path / "out" / "EmployeeBrain.g.cs").exists()
    part = json.loads((tmp_path / "out" / "EmployeeBrain.part.json").read_text())
    assert part["card"]["steps"] == 1500


def test_forward_clamps_and_normalises():
    net = {"mean": np.zeros(2), "std": np.ones(2), "hidden": [(np.eye(2), np.zeros(2))], "heads": [(np.eye(2), np.zeros(2))]}
    out = eb.forward(net, np.array([[100.0, -100.0]]))[0][0]
    assert out[0] == pytest.approx(5 / (1 + np.exp(-5)))      # clamped to 5, then swish
