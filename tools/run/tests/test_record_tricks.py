"""Exercise record_tricks.sh with a temporary project, stand-in Unity and stub renderer."""
import json
import shlex
import shutil
import subprocess
from pathlib import Path

from tools.run.tests.test_run import Fixture, ROOT, STANDIN


class RecordTricksTests(Fixture):
    def setUp(self):
        super().setUp()
        tools = self.s.repo / 'tools'
        self.script = tools / 'tricks/record_tricks.sh'
        self.script.parent.mkdir(parents=True)
        shutil.copy2(ROOT / 'tools/tricks/record_tricks.sh', self.script)
        runner = tools / 'run/kehai_run.py'
        runner.parent.mkdir()
        runner.symlink_to(ROOT / 'tools/run/kehai_run.py')
        self.call = self.t / 'render-args.json'
        self.renderer = tools / 'marketing/render_shot.sh'
        self.renderer.parent.mkdir()
        self.renderer.write_text(
            '#!/usr/bin/env python3\n'
            'import json, os, sys\n'
            'from pathlib import Path\n'
            'args = sys.argv[1:]\n'
            'replay = Path(args[args.index("-krec") + 1])\n'
            'assert replay.read_bytes() == b"fixture replay"\n'
            'movie = Path(args[args.index("-out") + 1])\n'
            'movie.write_bytes(b"renderer fixture")\n'
            'for suffix in (".wav", ".mp4.json", ".json"):\n'
            '    movie.with_suffix(suffix).touch()\n'
            'Path(os.environ["RENDER_CALL"]).write_text(json.dumps(args))\n'
        )
        self.renderer.chmod(0o755)
        self.marketing = self.t / 'marketing'
        self.s.lock = self.marketing / 'state/heavy.lock'
        self.videos = self.s.main / 'Assets/StreamingAssets/TrickVideos'

    def record_tricks(self, *args, **env):
        settings = dict(HOME=str(self.t), KEHAI_PROJECT=str(self.s.main), KEHAI_UNITY=str(STANDIN),
                        KEHAI_MARKETING=str(self.marketing), KEHAI_DATA='', RENDER_CALL=str(self.call))
        return subprocess.run([self.script, *args], cwd=self.t, text=True,
                              env=self.cli_env(**{**settings, **env}), timeout=20,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    def test_dry_run_prints_pinned_check_command_without_writing_outputs(self):
        result = self.record_tricks('--dry-run', '--only', 'fog,blackout', '--seed', '991')
        self.assertEqual(result.returncode, 0, result.stdout)
        args = shlex.split(result.stdout)
        clone = Path(args[4])
        self.assertEqual(clone.parent, self.s.home / 'clones.noindex')
        self.assertEqual(args, [str(STANDIN), '-batchmode', '-nographics', '-projectPath', str(clone),
                                '-logFile', str(self.s.runs / clone.name / 'unity.log'),
                                '-executeMethod', 'EvalBatch.Play', '-kehai-trick-check',
                                '-trick-check-seed', '991', '-eval-timeout-min', '60',
                                '-trick-check-only', 'fog,blackout'])
        self.assertFalse(self.s.home.exists())
        self.assertFalse(self.videos.exists())
        self.assertFalse(self.call.exists())

    def test_new_check_and_skip_check_resolve_retained_replays(self):
        legacy = self.t / 'legacy data'
        old_report = legacy / 'kehai_eval/trick_check_old.tsv'
        old_report.parent.mkdir(parents=True)
        old_replay = legacy / 'old replay.krec'
        old_replay.touch()
        old_report.write_text(f'id\tstarted\tlanded\tkrec\nblackout\t5\t9\t{old_replay}\n')
        result = self.record_tricks('--only', 'blackout', '--seed', '991', KEHAI_DATA=str(legacy))
        self.assertEqual(result.returncode, 0, result.stdout)
        start, end = self.events()
        self.assertEqual(end['state'], 'completed')
        retained = self.s.runs / start['id']
        report = retained / 'kehai_eval/trick_check_fixture.tsv'
        replay = retained / 'shift_records/shift_001.krec'
        self.assertEqual(replay.read_bytes(), b'fixture replay')
        self.assertFalse(Path(report.read_text().splitlines()[1].split('\t')[-1]).exists())
        self.assertTrue(report.with_suffix('.md').is_file())
        seen = json.loads((retained / 'unity.log').read_text().splitlines()[1])
        self.assertEqual(seen['source_commit'], start['commit'])
        self.assertFalse(seen['deleted_file'])
        movie = self.videos / 'blackout.mp4'
        self.assertEqual(json.loads(self.call.read_text()), [
            '-krec', str(replay.resolve()), '-from', '3.00', '-to', '17.00',
            '-shot', 'chase', '-subject', 'you', '-layers', 'none', '-size', '1280x720',
            '-fps', '30', '-out', str(movie),
        ])
        self.assertEqual(list(self.videos.iterdir()), [movie])
        self.assert_clean()

        self.call.unlink()
        result = self.record_tricks('--skip-check', '--only', 'shelf_sweep', '--dry-run')
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn(str(replay), result.stdout)
        self.assertIn('-from 6.50 -to 20.50 -shot chase -subject karen', result.stdout)
        self.assertFalse(self.call.exists())
        self.assertEqual(self.events(), [start, end])

        result = self.record_tricks('--skip-check', '--dry-run', KEHAI_DATA=str(legacy))
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertIn(str(old_report), result.stdout)
        self.assertFalse(self.call.exists())
