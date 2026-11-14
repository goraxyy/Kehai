"""C3.7: packs are signed and checked; the gate refuses worse and passes better."""
import json

import pytest

import pack
import publish
import rsa_sign

KEY = rsa_sign.generate(1024)      # made at test time; no key of any kind is in git


def start_part(**tricks):
    return {"kind": "start", "name": "LedgerStart", "card": {"version": "t"}, "tricks": tricks, "kinds": {}, "answers": {}}


def test_sign_and_verify_round_trip():
    text = pack.build([start_part(spill=[0.1])], 3, KEY)
    ok, why, body = pack.check(text, KEY)
    assert ok, why
    assert body["pack_version"] == 3


def test_tampered_old_inputs_and_unknown_trick_are_refused():
    text = pack.build([start_part(spill=[0.1])], 3, KEY)
    tampered = text.replace("0.1", "0.9", 1)
    assert pack.check(tampered, KEY)[0] is False
    old = pack.build([start_part(spill=[0.1])], 3, KEY, inputs_version=1)
    ok, why, _ = pack.check(old, KEY)
    assert not ok and "inputs version" in why
    ok, why, _ = pack.check(text, KEY, known_tricks={"blackout"})
    assert not ok and "spill" in why
    other = rsa_sign.generate(1024)
    assert pack.check(text, other)[0] is False, "signed by another key"


def test_vital_never_goes_in_a_pack():
    with pytest.raises(ValueError):
        pack.body([{"kind": "brain", "name": "KarenBrainVital"}], 1)


def evaluation(values, higher=True, **extra):
    d = {"metrics": {"logp": {"higher": higher, "by_career": {str(i): v for i, v in enumerate(values)}}},
         "fairness_violations": 0, "stuck_shifts": 1}
    d.update(extra)
    return d


def test_the_gate_passes_better_and_refuses_worse():
    current = evaluation([1.0, 1.1, 0.9, 1.0, 1.05, 0.95])
    better = evaluation([1.5, 1.6, 1.4, 1.55, 1.5, 1.45])
    worse = evaluation([0.5, 0.6, 0.4, 0.5, 0.55, 0.45])
    same = evaluation([1.0, 1.1, 0.9, 1.0, 1.05, 0.95])
    assert publish.gate(better, current)[0]
    assert not publish.gate(worse, current)[0]
    assert not publish.gate(same, current)[0], "not better by two standard errors"
    assert not publish.gate(dict(better, fairness_violations=1), current)[0]
    assert not publish.gate(dict(better, stuck_shifts=2), current)[0]
    assert not publish.gate(dict(better, basic_catches_per_shift=0.5), current)[0]
    assert publish.gate(dict(better, basic_catches_per_shift=0.3), current)[0]


def test_stage_writes_locally_and_points_current(tmp_path):
    p = tmp_path / "c.kpack"
    p.write_text(pack.build([start_part()], 1, KEY))
    better = evaluation([2.0, 2.1, 1.9, 2.0])
    current = evaluation([1.0, 1.1, 0.9, 1.0])
    out = publish.stage(str(p), better, current, str(tmp_path / "packs"))
    assert out.endswith("1.kpack")
    assert (tmp_path / "packs" / "current").read_text().strip() == "1"
    with pytest.raises(SystemExit):
        publish.stage(str(p), current, better, str(tmp_path / "packs"))
