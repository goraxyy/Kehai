"""C1.2: Kaplan–Meier time to catch and the catch-cause table."""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import analyze_ablation as aa  # noqa: E402


def test_kaplan_meier_counts_shifts_with_no_catch_as_not_yet():
    # 4 shifts: caught at 30 and 60; two ran 150 s with no catch.
    curve, median = aa.kaplan_meier([(30, True), (60, True), (150, False), (150, False)])
    assert curve == [(30, 0.75), (60, 0.5)]
    assert median == 60


def test_kaplan_meier_median_is_none_when_most_are_never_caught():
    _, median = aa.kaplan_meier([(30, True), (150, False), (150, False), (150, False)])
    assert median is None


def test_catch_section_reports_causes():
    rows = [
        {"rung": "E", "sim_seconds": 150, "time_to_catch_s": 40, "karen_brain": "planner",
         "catch_causes": [{"action": "Hunt", "tactic": "chase", "seen_3s": True, "clock_m": 4.0}]},
        {"rung": "E", "sim_seconds": 150, "time_to_catch_s": -1, "karen_brain": "planner", "catch_causes": []},
    ]
    out = []
    aa.catch_section({"E": rows}, ["E"], out.append)
    text = "\n".join(out)
    assert "| E | planner | 1 | 100% | 100% | 100% |" in text
    assert "chase ×1" in text
