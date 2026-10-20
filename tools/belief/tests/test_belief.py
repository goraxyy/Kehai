"""tools/belief on the synthetic map: trace round trip, the replay against an independent
reference filter (the parity gate in miniature), the kernels' invariants, the weight packs
and the generated C#."""
import re

import numpy as np
import pytest
import torch

import export_cs
import kernels
import replay
from features import FeatureState, synthetic_state
from make_fixture import ReferenceFilter, fixture_outputs, synthetic_map, synthetic_trace
from trace import load_shift, read_trace, save_shift


@pytest.fixture(scope="module")
def shift(tmp_path_factory):
    path = tmp_path_factory.mktemp("trace") / "trace_C_efficient_1000_s04.kbt.gz"
    logged = synthetic_trace(path, ticks=150, seed=3)
    return read_trace(path), logged


def test_synthetic_map_follows_the_linking_rule():
    m = synthetic_map()
    assert m.n == 31
    pairs = set(zip(m.edge_from.tolist(), m.edge_to.tolist()))
    assert all((b, a) in pairs for a, b in pairs), "edges should go both ways"
    for c in range(m.n):
        slots = m.edge_slot[m.edge_start[c]:m.edge_start[c + 1]]
        assert len(set(slots.tolist())) == len(slots), "one edge per direction"


def test_trace_round_trip(shift, tmp_path):
    s, logged = shift
    assert s.shadows == ["m0"] and s.rung == "C" and s.map.n == 31
    np.testing.assert_allclose(s.sa_own, logged["own"], atol=1e-6)
    save_shift(s, tmp_path / "s.npz")
    t = load_shift(tmp_path / "s.npz")
    assert (t.ops == s.ops).all() and t.shadows == s.shadows and t.seed == s.seed
    np.testing.assert_array_equal(t.ev_values, s.ev_values)


def test_replay_reproduces_the_reference_filter(shift):
    """The parity gate in miniature: with the hint against her own grid, without against m0."""
    s, _ = shift
    ok, lines = replay.parity(s, tol=1e-4)
    assert ok, "\n".join(lines)


@pytest.mark.parametrize("mid", ["m0", "m1", "m2", "m3", "m4"])
def test_kernels_keep_mass_and_move_only_along_edges(mid):
    m = synthetic_map()
    fs = synthetic_state(m)
    model = kernels.make(mid)
    if mid in ("m3", "m4"):
        torch.manual_seed(1)
        with torch.no_grad():
            model.w3.normal_(0, 0.3)
    k = model.build(fs)
    hc = model.headings
    for cell in (0, 10, m.n - 1):
        for h in range(hc):
            b = torch.zeros(m.n, hc) if hc > 1 else torch.zeros(m.n)
            b[(cell, h) if hc > 1 else cell] = 1
            to = model.apply(k, b, 0.1333, 3.1).detach()
            assert abs(float(to.sum()) - 1) < 1e-5
            assert (to >= 0).all()
            marg = to.sum(1) if hc > 1 else to
            allowed = {cell, *m.edge_to[m.edge_start[cell]:m.edge_start[cell + 1]].tolist()}
            assert set(torch.nonzero(marg > 0).view(-1).tolist()) <= allowed


def test_m0_kernel_matches_the_loop():
    m = synthetic_map()
    fs = synthetic_state(m)
    rng = np.random.default_rng(0)
    b = rng.random(m.n).astype(np.float32)
    b /= b.sum()
    ref = ReferenceFilter(m, b, fs.desire, fs.habit)
    ref.b = b.copy()
    ref.predict(0.1333, 3.1)
    model = kernels.make("m0")
    moved = model.apply(model.build(fs), torch.as_tensor(b), 0.1333, 3.1)
    u = np.float32(np.clip(np.float32(0.015) * np.float32(0.1333), 0, 1))
    ours = ((1 - u) * moved.numpy() + u * b).astype(np.float32)
    np.testing.assert_allclose(ours, ref.b, rtol=1e-6, atol=1e-9)


def test_packs_round_trip_and_have_the_csharp_sizes():
    # LearnedMotion.PackSize: head 43; MLP 48·32+32+32·32+32+32·O+O; heading 9+72+9+24+1+9.
    sizes = {"m2": 43, "m3": 43 + 1536 + 32 + 1024 + 32 + 288 + 9, "m4": 43 + 1536 + 32 + 1024 + 32 + 320 + 10 + 124}
    for mid, size in sizes.items():
        a = kernels.LearnedKernel(mid)
        torch.manual_seed(2)
        for p in a.parameters():
            p.data.normal_()
        flat = a.pack()
        assert len(flat) == size
        b = kernels.LearnedKernel(mid)
        b.load_pack(flat)
        np.testing.assert_array_equal(b.pack(), flat)


def test_generated_csharp_holds_the_fixture(tmp_path):
    weights = export_cs.untrained()
    out = tmp_path / "MotionWeights.g.cs"
    export_cs.write(weights, {"status": "test"}, False, "m0", out)
    text = out.read_text()
    expected = fixture_outputs(export_cs.models_from(weights))
    for mid in ("m0", "m1", "m2", "m3", "m4"):
        block = re.search(r"Fixture%s =\s*\{([^}]*)\}" % mid.upper(), text).group(1)
        values = np.array([float(v.strip().rstrip("f")) for v in block.split(",") if v.strip()], np.float32)
        np.testing.assert_array_equal(values, expected[mid])
    m3 = re.search(r"static readonly float\[\] M3 =\s*\{([^}]*)\}", text).group(1)
    assert len([v for v in m3.split(",") if v.strip()]) == len(weights["m3"])


def test_features_are_in_range():
    fs = synthetic_state(synthetic_map())
    x, y = fs.cells(), fs.edges()
    assert x.shape == (31, 24) and y.shape == (84, 9)
    assert np.isfinite(x).all() and np.isfinite(y).all()
    assert (np.abs(y[:, [0, 2, 4, 5]]) <= 1).all()
    u = fs.mlp_input(x, y)
    assert u.shape == (31, 48)
    assert (u[:, 40:].sum(1) == np.diff(fs.m.edge_start)).all(), "the mask counts the edges"
