#!/usr/bin/env python3
"""Writes the C# parity test's fixture (C5.7): a small ML-Agents actor with random weights,
exported by export_brain.py into the test folder, with the trainer's own chances on 1,000
inputs beside it. BrainNetParityTests checks the C# network gives the same chances, to 4
decimals. Rerun after changing export_brain.py or BrainNet.cs:

    tools/rl/.venv/bin/python tools/learn/make_brain_fixture.py
"""
import os
import sys

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import export_brain as eb  # noqa: E402

from mlagents_envs.base_env import ActionSpec, ObservationSpec, DimensionProperty, ObservationType  # noqa: E402
from mlagents.trainers.torch_entities.networks import SimpleActor  # noqa: E402
from mlagents.trainers.settings import NetworkSettings  # noqa: E402

OUT = os.path.join(eb.ROOT, "Assets", "!_Project", "_Tests", "Editor")
N_IN, BRANCHES, N = 6, (4, 3), 1000


def tiny_actor(seed=11):
    torch.manual_seed(seed)
    spec = [ObservationSpec(shape=(N_IN,), dimension_property=(DimensionProperty.NONE,), observation_type=ObservationType.DEFAULT, name="obs")]
    actor = SimpleActor(spec, NetworkSettings(normalize=True, hidden_units=8, num_layers=2), ActionSpec(0, BRANCHES))
    norm = actor.network_body.observation_encoder.processors[0].normalizer
    with torch.no_grad():
        norm.running_mean.copy_(torch.linspace(-0.5, 0.5, N_IN))
        norm.running_variance.copy_(torch.linspace(1.0, 4.0, N_IN) * 10.0)
        norm.normalization_steps.fill_(10)
        for p in actor.parameters():
            p.add_(torch.randn_like(p) * 0.3)
    return actor


def main():
    actor = tiny_actor()
    state = actor.state_dict()
    card = {"version": "fixture", "trained": False, "trained_against": "nothing: random weights", "inputs_version": 0,
            "outputs": [[f"a{i}" for i in range(BRANCHES[0])], [f"b{i}" for i in range(BRANCHES[1])]], "command": "make_brain_fixture.py"}
    r = eb.export(state, "TinyBrain", card, os.path.join(OUT, "_tmp_fixture"), namespace="Kehai.Karen.TestBrains")
    rng = np.random.default_rng(3)
    x = rng.uniform(-2.0, 2.0, size=(N, N_IN)).astype(np.float32)
    theirs = eb.trainer_logits(state, x.astype(np.float64), 8, 2, True, list(BRANCHES))
    probs = [eb.softmax(t) for t in theirs]
    gen = open(os.path.join(OUT, "_tmp_fixture", "TinyBrain.g.cs")).read()
    body = gen.rstrip().rstrip("}").rstrip()          # drop the namespace's closing brace
    extra = ["", "    // The trainer's own chances for these inputs (make_brain_fixture.py).",
             "    public static class TinyBrainParity", "    {",
             f"        public const int Inputs = {N_IN};",
             "        public static readonly float[] X = " + eb.arr(x) + ";",
             "        public static readonly float[] P0 = " + eb.arr(probs[0]) + ";",
             "        public static readonly float[] P1 = " + eb.arr(probs[1]) + ";",
             "    }", "}", ""]
    with open(os.path.join(OUT, "BrainNetFixture.g.cs"), "w") as fh:
        fh.write(body + "\n" + "\n".join(extra))
    for f in os.listdir(os.path.join(OUT, "_tmp_fixture")):
        os.remove(os.path.join(OUT, "_tmp_fixture", f))
    os.rmdir(os.path.join(OUT, "_tmp_fixture"))
    print(f"wrote {os.path.join(OUT, 'BrainNetFixture.g.cs')} (python parity {r['parity']:.2e})")


if __name__ == "__main__":
    main()
