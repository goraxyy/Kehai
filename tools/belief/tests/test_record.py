import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("belief_record", Path(__file__).resolve().parents[1] / "record.py")
record = importlib.util.module_from_spec(spec)
spec.loader.exec_module(record)


class RecordTests(unittest.TestCase):
    def test_interrupted_validation_replays_only_validation(self):
        stages = []
        with tempfile.TemporaryDirectory() as folder:
            repo = Path(folder)
            output = repo / "data"

            def run(command, check):
                name = command[0].name
                if name == "run_unity.sh":
                    league, pending = command[1:3]
                    stages.append(league.stem)
                    traces = pending / "traces"
                    traces.mkdir(parents=True)
                    (traces / "map.json").touch()
                    (traces / "trace_1000_1_bot.jsonl").touch()
                    if league.stem == "record_train":
                        (pending / "shift_records").mkdir()
                        (pending / "shift_records" / "shift_01.json").touch()
                    elif stages.count("record_val") == 1:
                        (traces / "trace_partial.jsonl").touch()
                        raise subprocess.CalledProcessError(-9, command)
                elif command[1].name == "replay.py":
                    stages.append("parity_" + command[2].parent.name)
                elif command[1].name == "legs.py":
                    stages.append("legs")
                    command[-1].write_text("{}\n")
                else:
                    self.fail(f"Unexpected command: {command}")

            with patch.object(record, "git_head", return_value="abc"), patch.object(record.subprocess, "run", side_effect=run):
                with self.assertRaises(subprocess.CalledProcessError):
                    record.record(repo, output, Path("python"))
                self.assertTrue((output / "train").is_dir())
                self.assertFalse((output / "val").exists())
                record.record(repo, output, Path("python"))
                record.record(repo, output, Path("python"))

            self.assertEqual(stages, ["record_train", "record_val", "record_val", "parity_train", "parity_val", "legs"])
            self.assertFalse((output / "val" / "traces" / "trace_partial.jsonl").exists())
            self.assertEqual((output / "train" / "legs.jsonl").read_text(), "{}\n")

    def test_leaves_existing_recordings_alone(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            (output / "train").mkdir()
            with patch.object(record, "git_head", return_value="abc"):
                with self.assertRaisesRegex(RuntimeError, "already holds recordings"):
                    record.record(output, output, Path("python"))
            self.assertTrue((output / "train").is_dir())
