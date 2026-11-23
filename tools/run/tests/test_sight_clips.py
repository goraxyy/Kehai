import gzip
import json
import shlex
import struct
from pathlib import Path
from subprocess import CompletedProcess
from unittest.mock import patch

from tools.run import kehai_run as kr
from tools.run.sight_clips import read_review, verify_sight_movie
from tools.run.tests.test_run import Fixture


class SightClipTests(Fixture):
    def setUp(self):
        super().setUp()
        retained = self.t / 'recorded run'
        self.commit = kr.git(self.s.repo, 'rev-parse', 'HEAD^')
        self.report = retained / 'sandbox_checks' / 'sight_20261121_223500_455.json'
        self.report.with_suffix('').mkdir(parents=True)
        (retained / 'run.jsonl').write_text(json.dumps({'commit': self.commit}) + '\n')
        self.report.with_suffix('.md').write_text('# Bot results\n')
        self.rows = []
        for i in range(8):
            name = f'case-{i}'
            replay = self.report.with_suffix('') / f'{name}.krec'
            # Header and footer fixture for the offline preflight. No actor samples or video.
            version = 4 if i % 2 == 0 else 5
            raw = b'KREC' + struct.pack('<H', version) + b'\x05Kehai\x06' + name.encode()
            raw += b'\x00' * 5 + b'\x0dSight sandbox' + bytes(64)
            replay.write_bytes(gzip.compress(raw + struct.pack('<BfB', 255, 3.5 + i / 2, 0)))
            self.rows.append({'name': name, 'expected': 'Unseen', 'pass': i != 0, 'max_awareness': 0.0,
                              'seconds_to_see': -1, 'replay': f'{replay.parent.name}/{replay.name}'})
        self.report.write_text(json.dumps(self.rows))

    def test_dry_run_plans_every_case_at_recorded_duration_without_running_anything(self):
        sight = read_review(self.report)
        self.assertEqual(sight.commit, self.commit)
        lines = []
        with patch.object(kr, 'unity_open', side_effect=AssertionError('dry run must stay offline')):
            self.assertEqual(kr.run(self.s, 'sight-clips', [], sight=sight, dry_run=True, log=lines.append), 0)
        self.assertEqual(len(lines), 8)
        projects = set()
        for line, case in zip(lines, sight.cases):
            args = shlex.split(line)
            projects.add(args[args.index('-projectPath') + 1])
            self.assertEqual(Path(args[args.index('-krec') + 1]), case.replay)
            self.assertEqual(float(args[args.index('-to') + 1]), case.duration)
            self.assertIn('PursuitSandboxChecks.Render', args)
            self.assertIn('-silent-video', args)
            self.assertIn('-noaudio', args)
            self.assertNotIn('-nographics', args)
        self.assertEqual(len(projects), 1)
        self.assertFalse(self.s.home.exists())

    def test_preflight_rejects_incomplete_or_misdirected_inputs(self):
        for rows in [self.rows[:-1], [*self.rows[:-1], self.rows[0]],
                     [*self.rows[:-1], {**self.rows[-1], 'replay': '../outside.krec'}]]:
            self.report.write_text(json.dumps(rows))
            with self.assertRaises(ValueError):
                read_review(self.report)
        self.report.write_text(json.dumps(self.rows))
        last = self.report.parent / self.rows[-1]['replay']
        raw = gzip.decompress(last.read_bytes())
        for broken in (raw[:-6], raw.replace(b'Sight sandbox', b'Other sandbox')):
            last.write_bytes(gzip.compress(broken))
            with self.assertRaisesRegex(ValueError, 'case-7'):
                read_review(self.report)
        last.unlink()
        with self.assertRaisesRegex(ValueError, 'case-7'):
            read_review(self.report)

    def test_zero_renderer_exit_without_video_reports_all_failures_on_one_clone(self):
        sight = read_review(self.report)
        with patch('tools.run.sight_clips.video_tools', return_value=(Path('/ffmpeg'), Path('/ffprobe'))), \
             patch.object(kr, 'make_clone', wraps=kr.make_clone) as clone:
            code = kr.run(self.s, 'sight-clips', [], sight=sight, log=lambda _: None)
        self.assertEqual(code, 1)
        self.assertEqual(clone.call_count, 1)
        self.assertEqual(clone.call_args.args[2], self.commit)
        self.assert_clean()
        out = next(self.s.runs.iterdir())
        review = (out / 'review.md').read_text()
        self.assertEqual(review.count('Verification failed'), 8)
        self.assertIn('| case-0 | Unseen | Fail |', review)
        self.assertNotIn('[MP4]', review)
        self.assertEqual(len(list(out.glob('*/unity.log'))), 8)

    def test_checks_sidecar_and_decoded_streams(self):
        case = read_review(self.report).cases[0]
        movie = self.t / 'clip.mp4'
        movie.write_bytes(b'probe fixture')
        sidecar = self.t / 'clip.json'
        info = {'krec': case.replay.name, 'video': movie.name, 'audio': None, 'from': 0, 'frames': 106,
                'shot': 'topdown', 'width': 1280, 'height': 720, 'fps': 30}
        sidecar.write_text(json.dumps(info))
        video = {'codec_type': 'video', 'codec_name': 'h264', 'width': 1280, 'height': 720,
                 'nb_read_frames': '106'}
        with patch('tools.run.sight_clips.subprocess.run') as probe:
            def streams(items):
                probe.return_value = CompletedProcess([], 0, json.dumps({'streams': items}), '')

            streams([video])
            verify_sight_movie(self.t, case, Path('/test/ffprobe'))
            self.assertIn('-count_frames', probe.call_args.args[0])
            self.assertEqual(probe.call_args.kwargs['cwd'], Path('/test'))
            for bad in ([video, {'codec_type': 'audio'}], [{**video, 'nb_read_frames': '105'}]):
                streams(bad)
                with self.assertRaises(ValueError):
                    verify_sight_movie(self.t, case, Path('/test/ffprobe'))
            sidecar.write_text(json.dumps({**info, 'krec': 'wrong.krec'}))
            with self.assertRaisesRegex(ValueError, 'sidecar'):
                verify_sight_movie(self.t, case, Path('/test/ffprobe'))
