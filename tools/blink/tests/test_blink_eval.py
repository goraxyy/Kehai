"""The accuracy tooling: the dataset format, the copy of the game's tracker, the scoring
(matching, timing, the reopen prediction), choosing what to compare, the splits, and the
whole pipeline on the synthetic fixture. numpy only, except the training run (PyTorch)."""
import json
import os

import numpy as np
import pytest

import blink_data as bd
import eval_blink as ev
import label_blinks as lb
import make_fixture
import record_dataset as rd
import train_eye_cnn as tr

FPS = 30.0


def session(tmp_path, n=120, sid="s1", person="p1", signals=None, closed=None, phase=None, labelled=True):
    """A small session on a clean 30 fps clock, saved and loaded back."""
    t = 1.76e9 + np.arange(n) / FPS
    path = str(tmp_path / f"{sid}.npz")
    bd.save_session(path, session=sid, person=person, t=t, x=np.zeros((n, 2, 24, 24), np.uint8),
                    eye_ok=np.ones((n, 2), bool), phase=np.zeros(n) if phase is None else phase,
                    space=np.zeros(n, bool), signals=signals or {"ear": np.zeros(n)}, ms={"mesh": np.full(n, 5.0)})
    if labelled:
        lab = bd.Labels(np.zeros(n, bool) if closed is None else closed, np.zeros(n, bool), np.ones(n, bool))
        bd.save_labels(bd.labels_path(path), sid, lab)
    return bd.load_session(path)


# ---- format -------------------------------------------------------------------------------------


def test_runs_and_masks_round_trip():
    m = np.array([0, 1, 1, 0, 0, 1, 0, 1], bool)
    assert bd.runs(m) == [(1, 3), (5, 6), (7, 8)]
    assert (bd.mask_from_runs(bd.runs(m), len(m)) == m).all()
    assert bd.runs(np.zeros(4, bool)) == []


def test_session_and_labels_round_trip(tmp_path):
    closed = np.zeros(120, bool)
    closed[30:37] = True
    s = session(tmp_path, closed=closed, signals={"ear": np.r_[np.full(60, np.nan), np.zeros(60)]})
    assert s.id == "s1" and s.person == "p1" and s.n == 120 and s.labelled
    assert abs(s.fps - FPS) < 1e-3 and not s.synthetic
    assert s.has("ear") and not s.has("vision")
    assert bd.runs(s.labels.closed) == [(30, 37)]
    with pytest.raises(ValueError):
        bd.load_labels(bd.labels_path(s.path), 99)          # labels for another length


def test_ids_are_pseudonyms_not_paths():
    with pytest.raises(ValueError):
        bd.check_id("../escape", "session")
    with pytest.raises(ValueError):
        bd.check_id("Jane Doe", "person")
    assert bd.check_id("p1", "person") == "p1"


# ---- the game's tracker -------------------------------------------------------------------------


def test_tracker_matches_blink_tracker():
    t = np.arange(8) / 10
    tracked = ev.track(np.array([0, 0, 1, 1, 1, 0, 0, 0.0]), t)
    # Shut at 1.0; on the way down 1 -> 0.4 (still above 0.3, still shut) -> 0.16 (open).
    assert tracked.closed.tolist() == [False, False, True, True, True, True, False, False]
    assert tracked.blinks == [(2, 6, pytest.approx(0.2 + 0.3))]
    # It learns the blink as the reading saw it: shut at 0.2 and back under the close threshold
    # at 0.6 (the dip at 0.3 came back up), so 0.4 s, though the hysteresis only let go at 0.8.
    # 10% of the way from 0.3 s to 0.4 s; the 0.6 s it detected would have made it 0.33.
    t2 = np.arange(16) / 10
    again = ev.track(np.array([0, 0, 1, 0.4, 1, 1, 0.45, 0.2, 0, 0, 1, 1, 0, 0, 0, 0.0]), t2)
    assert again.closed[2:8].all() and not again.closed[8]
    assert again.blinks == [(2, 8, pytest.approx(0.5)), (10, 13, pytest.approx(1.0 + 0.31))]


def test_tracker_uses_the_calibration():
    t = np.arange(5) / 10
    assert not ev.track(np.full(5, 0.45), t).closed.any()
    assert ev.track(np.full(5, 0.45), t, open_level=0.2, closed_level=0.6).closed.all()


def test_calibration_like_fit():
    t = np.arange(300) / FPS
    phase = np.zeros(300, np.int8)
    phase[0:90], phase[90:195] = bd.PHASE_OPEN, bd.PHASE_SHUT
    reading = np.where(phase == bd.PHASE_SHUT, 0.8, 0.1)
    reading[90:120] = 0.1                                   # the first second shut is skipped
    o, s, note = ev.calibrate(reading, t, phase)
    assert (o, s) == (pytest.approx(0.1), pytest.approx(0.8)) and "open 0.10" in note
    assert ev.calibrate(np.full(300, 0.3), t, phase)[:2] == (0.0, 1.0)     # too close: the defaults
    assert "no calibration" in ev.calibrate(reading, t, np.zeros(300))[2]


# ---- scoring ------------------------------------------------------------------------------------


def test_match_is_one_to_one_in_time_order():
    human = [(10, 15), (30, 35), (50, 52)]
    pairs, unmatched = ev.match(human, [(9, 12), (13, 16), (31, 40), (60, 62)])
    assert pairs == [(0, 0), (1, 2)]            # 13-16 is the same blink seen twice
    assert unmatched == [1, 3]
    pairs, _ = ev.match([(10, 12), (14, 16)], [(10, 16)])
    assert pairs == [(0, 0)]                    # one detection for two blinks: the second is missed
    assert ev.match([(10, 12)], [(13, 15)])[0] == []
    assert ev.match([(10, 12)], [(13, 15)], slack=2)[0] == [(0, 0)]


def test_average_precision():
    assert ev.average_precision([0.9, 0.8, 0.7, 0.6], [1, 1, 0, 0]) == pytest.approx(1.0)
    assert ev.average_precision([0.9, 0.8, 0.7, 0.6], [1, 0, 1, 0]) == pytest.approx((1 + 2 / 3) / 2)
    assert ev.average_precision([0.5, 0.5], [1, 0]) == pytest.approx(0.5)     # ties count together


def test_score_session_times_one_blink_exactly(tmp_path):
    n = 120
    closed = np.zeros(n, bool)
    closed[30:37] = True                        # you: shut on frames 30-36, open again on 37
    reading = np.zeros(n)
    reading[31:38] = 1.0                        # the helper: one frame late
    reading[60:63] = 1.0                        # and a blink you didn't make
    s = session(tmp_path, closed=closed, signals={"ear": reading})
    r = ev.score_session(s, reading)
    assert r["human_blinks"] == 1 and r["seen"] == 1 and r["false"] == 1
    assert r["onset_ms"] == [pytest.approx(1000 / 30)]
    assert r["reopen_seen_ms"] == [pytest.approx(2000 / 30)]                   # 0.4 then 0.16: two frames
    assert r["reopen_error_ms"] == [pytest.approx((31 / 30 + 0.3 - 37 / 30) * 1000)]
    assert r["window_ms"] == [pytest.approx(6000 / 30)]
    f = r["frames"]
    assert (f["tp"], f["fn"], f["fp"]) == (6, 1, 2 + 4)    # 31-36 | 30 | 37-38 and 60-63
    summary = ev.summarise([r], 5.0)
    assert summary["blinks"]["recall"] == 1.0 and summary["blinks"]["false_per_min"] > 0


def test_unscored_frames_are_left_out(tmp_path):
    n = 120
    closed = np.zeros(n, bool)
    closed[10:15] = closed[70:75] = True
    phase = np.zeros(n, np.int8)
    phase[:40] = bd.PHASE_OPEN                    # calibration: never scored
    s = session(tmp_path, closed=closed, phase=phase, signals={"ear": closed.astype(float)})
    s.labels.ignore[72] = True                    # you couldn't tell on one frame of the second
    r = ev.score_session(s, closed.astype(float))
    assert r["human_blinks"] == 0 and r["false"] == 0
    assert r["minutes"] == pytest.approx((n - 40 - 1) / FPS / 60)


def test_long_closures_are_kept_out_of_the_reopen_prediction(tmp_path):
    n = 120
    closed = np.zeros(n, bool)
    closed[20:25] = closed[50:80] = True          # a blink and a one-second closure
    s = session(tmp_path, closed=closed, signals={"ear": closed.astype(float)})
    r = ev.score_session(s, closed.astype(float))
    assert r["seen"] == 2 and r["long_closures"] == 1 and len(r["reopen_error_ms"]) == 1


def test_cnn_reading_averages_the_eyes_it_has(tmp_path):
    s = session(tmp_path, n=3)
    s.x[:, 0], s.x[:, 1] = 255, 0
    s.eye_ok[1, 1] = False
    s.eye_ok[2] = False
    reading = ev.cnn_reading(s, lambda b: b.reshape(len(b), -1).mean(axis=1))
    assert reading[0] == pytest.approx(0.5) and reading[1] == pytest.approx(1.0) and np.isnan(reading[2])


def test_choose_scores_every_method_on_the_same_frames(tmp_path):
    a = session(tmp_path, sid="a", signals={"ear": np.zeros(120), "vision": np.zeros(120)})
    b = session(tmp_path, sid="b", signals={"ear": np.zeros(120)})
    c = session(tmp_path, sid="c", labelled=False)
    methods, kept, dropped, notes = ev.choose([a, b, c])
    assert methods == ["ear"] and [s.id for s in kept] == ["a", "b"]
    assert "c" in dropped and any("vision" in n for n in notes)
    methods, kept, dropped, _ = ev.choose([a, b], ["ear", "vision"])
    assert [s.id for s in kept] == ["a"] and "no vision" in dropped["b"]
    methods, kept, dropped, _ = ev.choose([a, b], None, have_cnn=True, cnn_seen={"a"})
    assert methods == ["ear", "cnn"] and [s.id for s in kept] == ["b"] and "trained" in dropped["a"]
    with pytest.raises(SystemExit):
        ev.choose([a], ["cnn"])


# ---- splits and training targets ---------------------------------------------------------------


def test_splits_hold_out_whole_sessions(tmp_path):
    sessions = [session(tmp_path, sid=f"s{i}", labelled=i >= 3) for i in range(6)]
    splits, by, _ = tr.assign_splits(sessions)
    assert by == "session"
    assert splits["test"] == ["s4", "s5"] and splits["val"] == ["s3"]
    everything = splits["train"] + splits["val"] + splits["test"]
    assert sorted(everything) == [f"s{i}" for i in range(6)] and len(set(everything)) == 6
    with pytest.raises(SystemExit):
        tr.assign_splits(sessions[:2])


def test_person_split_keeps_each_person_on_one_side(tmp_path):
    sessions = [session(tmp_path, sid=f"s{i}", person=f"p{i % 4}", labelled=True) for i in range(8)]
    splits, by, _ = tr.assign_splits(sessions)
    assert by == "person"
    owner = {s.id: s.person for s in sessions}
    sides = {name: {owner[sid] for sid in ids} for name, ids in splits.items()}
    assert not (sides["train"] & sides["test"]) and not (sides["train"] & sides["val"]) and not (sides["val"] & sides["test"])
    explicit, _, _ = tr.assign_splits(sessions, "session", test=["s0"], val=["s1"])
    assert explicit["test"] == ["s0"] and explicit["val"] == ["s1"] and "s0" not in explicit["train"]


def test_targets_prefer_your_labels(tmp_path):
    n = 150
    phase = np.zeros(n, np.int8)
    phase[:40], phase[40:100] = bd.PHASE_OPEN, bd.PHASE_SHUT
    teacher = np.where(phase == bd.PHASE_SHUT, 0.9, 0.1)
    teacher[120] = np.nan
    s = session(tmp_path, n=n, phase=phase, signals={"blendshapes": teacher}, labelled=False)
    y, mine = tr.targets(s, "blendshapes")
    assert y[0] == pytest.approx(0.0) and y[60] == pytest.approx(1.0) and np.isnan(y[120]) and not mine.any()
    s.labels = bd.Labels(np.zeros(n, bool), np.zeros(n, bool), np.zeros(n, bool))
    s.labels.reviewed[50:90] = True
    s.labels.ignore[85] = True
    y, mine = tr.targets(s, "blendshapes")
    assert y[60] == 0.0 and mine[60] and np.isnan(y[85]) and y[45] == pytest.approx(1.0)


# ---- recording and labelling ---------------------------------------------------------------------


def test_vision_packets_line_up_by_capture_time():
    log = rd.VisionLog.__new__(rd.VisionLog)
    t = 100 + np.arange(5) / FPS
    log.packets = [(t[0] + 0.004, 0.2, 0.9, t[0] + 0.020), (t[1] - 0.010, 0.7, 0.9, t[1]),
                   (t[3] + 0.001, 0.9, 0.0, t[3] + 0.010)]     # the last saw no face
    closed, ms = log.align(t)
    assert closed[0] == pytest.approx(0.2) and ms[0] == pytest.approx(16, abs=0.01)
    assert closed[1] == pytest.approx(0.7)
    assert np.isnan(closed[2]) and np.isnan(closed[3]) and np.isnan(closed[4])


def test_recording_starts_with_the_games_calibration():
    assert rd.phase_at(1.0, True)[0] == bd.PHASE_OPEN
    assert rd.phase_at(4.0, True)[0] == bd.PHASE_SHUT
    assert rd.phase_at(7.0, True)[0] == bd.PHASE_BLINKS
    assert rd.phase_at(13.0, True)[0] == bd.PHASE_FREE
    assert rd.phase_at(1.0, False)[0] == bd.PHASE_FREE


def test_labeler_marks_saves_and_counts_review(tmp_path):
    s = session(tmp_path, n=150, labelled=False)
    lab = lb.Labeler(s)
    lab.press(10, "closed")
    lab.drag(11)
    lab.release()
    lab.press(14, "closed", extend=True)                    # shift-click fills 11-14
    lab.press(20, "ignore")
    lab.release()
    lab.key(ord(" "))                                       # next page: the first 60 are reviewed
    saved = bd.load_labels(bd.labels_path(s.path), s.n)
    assert bd.runs(saved.closed) == [(10, 15)] and bd.runs(saved.ignore) == [(20, 21)]
    assert bd.runs(saved.reviewed) == [(0, 60)] and lab.page == 1
    assert lab.cell_at(lb.PAD + 1, lb.HEADER + 1) == 60
    lab.press(10 + 60, "closed")
    lab.press(10 + 60, "closed")                            # clicking again opens it
    assert not lab.labels.closed[70]
    assert lab.key(ord("q")) is False


# ---- the whole pipeline -------------------------------------------------------------------------


def test_fixture_through_eval(tmp_path, capsys):
    make_fixture.main(["--out", str(tmp_path), "--sessions", "3", "--labelled", "2", "--minutes", "0.6"])
    out = tmp_path / "result.json"
    ev.main(["--data", str(tmp_path), "--json", str(out)])
    text = capsys.readouterr().out
    assert "SYNTHETIC DATA" in text and "left out synth01" in text
    result = json.loads(out.read_text())
    assert result["sessions"] == ["synth02", "synth03"] and set(result["methods"]) == {"ear", "blendshapes", "vision"}
    for m in result["methods"].values():
        assert m["blinks"]["recall"] > 0.8 and m["frames"]["f1"] > 0.6


def test_training_holds_out_sessions_end_to_end(tmp_path, capsys):
    pytest.importorskip("torch")
    pytest.importorskip("onnx")
    pytest.importorskip("onnxruntime")
    make_fixture.main(["--out", str(tmp_path), "--sessions", "4", "--labelled", "2", "--minutes", "0.6"])
    model = str(tmp_path / "eye_cnn.onnx")
    tr.main(["--data", str(tmp_path), "--out", model, "--epochs", "2"])
    card = json.load(open(model + ".json"))
    assert card["splits"]["test"] == ["synth04"] and card["teacher"] == "blendshapes"
    assert not set(card["splits"]["test"]) & set(card["splits"]["train"] + card["splits"]["val"])
    assert os.path.getsize(model) > 1000
    ev.main(["--data", str(tmp_path), "--onnx", model])
    text = capsys.readouterr().out
    assert "left out synth03: the CNN was trained or selected on it" in text
    assert "\ncnn " in text
