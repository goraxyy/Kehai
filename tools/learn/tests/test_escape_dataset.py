import json
import zipfile

import dataset


def record():
    return dict(version=1, player="employee", seed=12, shift=2, layout="today", t=4.0,
                **{"from": "clock", "path": ["left"], "end": "shift_end"})


def test_escape_sidecar_is_separate_and_retries_are_idempotent(tmp_path):
    source = tmp_path / "learning_trial.escapes.jsonl"
    source.write_text(json.dumps(record())+"\n")
    root = str(tmp_path / "data")
    assert dataset.add(root, [str(source)]) == 1
    assert dataset.add(root, [str(source)]) == 0
    folder = tmp_path / "data/players/employee"
    assert not (folder / "learning.jsonl").exists()
    assert len((folder / "escapes.jsonl").read_text().splitlines()) == 1


def test_session_import_uses_session_identity_for_escape_answers(tmp_path):
    archive = tmp_path / "session.zip"
    with zipfile.ZipFile(archive, "w") as z:
        z.writestr("meta.json", json.dumps({"player_id": "consented-player"}))
        z.writestr("learning/learning_trial.escapes.jsonl", json.dumps(record())+"\n")
    dataset.add_zip(str(tmp_path / "data"), str(archive))
    folder = tmp_path / "data/players/consented-player"
    assert json.loads((folder / "escapes.jsonl").read_text())["player"] == "consented-player"
    assert not (folder / "learning.jsonl").exists()


def test_forgotten_player_escape_answers_are_not_reimported(tmp_path):
    (tmp_path / "forgotten.txt").write_text("employee\n")
    assert dataset.add_escape_rows(str(tmp_path), [record()]) == 0
    assert not (tmp_path / "players").exists()
