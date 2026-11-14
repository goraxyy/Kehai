"""C6.2: the ladders are computed the way §11 writes them."""
import json
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import analyze_ladders as al  # noqa: E402


def write(folder, arm, rows):
    d = os.path.join(folder, arm, "kehai_eval")
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, "league_x.jsonl"), "w") as f:
        f.write("\n".join(json.dumps(r) for r in rows) + "\n")


def rows(ttc=None, rmse=0.2, catches=0, n=12):
    out = []
    for c in range(n):
        for s in range(1, 7):
            out.append({"seed": 30000 + c, "agent": "a", "shift": s, "sim_seconds": 150,
                        "time_to_catch_s": (ttc + c % 3) if ttc is not None else -1, "panic_setpoint_rmse": rmse + 0.01 * (c % 2),
                        "karen_catches": catches, "karen_fairness_violations": 0, "timed_out": False, "catch_causes": [], "uses_spill": 2})
    return out


def test_vital_faster_and_basic_closer_pass(tmp_path):
    f = str(tmp_path)
    write(f, "catch_hunter", rows(ttc=90))
    write(f, "catch_vital", rows(ttc=60))
    for arm, r in (("fear_E", 0.25), ("fear_F", 0.18), ("fear_F_wiped", 0.22), ("fear_F_other", 0.23), ("fear_C", 0.3), ("ref_planner", 0.25)):
        write(f, arm, rows(rmse=r, catches=0 if arm != "fear_F" else 0.3))
    o = al.analyse(f)
    assert o["vital_time_to_catch"]["passed"] and o["basic_setpoint"]["passed"]
    assert o["adaptation_vs_fear_F_wiped"]["passed"] and o["adaptation_vs_fear_F_other"]["passed"]
    assert o["basic_catches_per_shift"]["passed"]
    assert "pass" in al.markdown(o)


def test_shifts_with_no_catch_count_as_the_whole_shift():
    assert al.time_to_catch({"time_to_catch_s": -1, "sim_seconds": 150}) == 150
    assert al.time_to_catch({"time_to_catch_s": 40, "sim_seconds": 150}) == 40
