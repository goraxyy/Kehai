"""C4.11: the prediction tests are computed the way §11 writes them."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import analyze_prediction as ap  # noqa: E402


def rows(better=True):
    out = []
    for c in range(12):
        for s in range(1, 7):
            g = 0.3 + 0.05 * (c % 3)
            out.append({"seed": 30000 + c, "agent": "x", "shift": s,
                        "shadow_p0_logp": -5.0, "shadow_s1_logp": -5.0 + (g if better else -g),
                        "shadow_p1_logp_out": -6.0, "shadow_p2_logp_out": -6.0 + (g if better else -g),
                        "mirror_k1_samples": 10, "mirror_k1_right": 4, "mirror_k2_samples": 10, "mirror_k2_right": 6 + c % 2,
                        "mirror_k0_samples": 10, "mirror_k0_right": 2,
                        "shadow_p3_loss_checks": 4, "shadow_p3_dest_top1": 3, "shadow_p3x_loss_checks": 4, "shadow_p3x_dest_top1": 1 + c % 2,
                        "shadow_p2_dest_top3": 2, "shadow_p2_loss_checks": 4})
    return out


def test_better_arms_pass_and_worse_ones_do_not():
    a = ap.analyse(rows(True))
    assert a["senses"]["passed"] and a["prediction"]["passed"] and a["mirror"]["passed"] and a["personal"]["passed"]
    b = ap.analyse(rows(False))
    assert not b["senses"]["passed"] and not b["prediction"]["passed"]
    assert a["careers"] == 12
    text = ap.markdown(a)
    assert "loses sight" in text and "pass" in text
