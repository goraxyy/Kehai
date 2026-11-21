import json
import tempfile
import unittest
from pathlib import Path

from tools.run.kehai_run import unity_args
from tools.run.trick_clip import read_shot, verify_movie


class TrickClipTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix='trick clip ')
        self.addCleanup(temp.cleanup)
        self.run = Path(temp.name).resolve()
        self.commit = 'a' * 40
        (self.run / 'run.jsonl').write_text(json.dumps({'commit': self.commit}) + '\n')
        (self.run / 'kehai_eval').mkdir()
        self.report = self.run / 'kehai_eval' / 'trick_check.tsv'
        (self.run / 'shift_records').mkdir()
        self.replay = self.run / 'shift_records' / 'shift 1.krec'
        self.replay.write_bytes(b'retained')
        self.row = 'blackout\t2.0\t4.5\tC:\\deleted clone\\shift 1.krec\n'
        self.header = 'id\tstarted\tlanded\tkrec\n'
        self.report.write_text(self.header + self.row)

    def test_resolves_copied_recording_and_pins_graphical_render(self):
        ref, shot = read_shot(self.report, 'blackout')
        self.assertEqual(ref, self.commit)
        args = unity_args('trick-clip', self.run / 'clone', self.run, shot, None)
        for key, value in [('-krec', str(self.replay)), ('-from', '0.00'), ('-to', '14.00'),
                           ('-subject', 'you'), ('-out', str(self.run / 'clip.mp4'))]:
            self.assertEqual(args[args.index(key) + 1], value)
        self.assertIn('-silent-video', args)
        self.assertNotIn('-nographics', args)
        self.assertIn('-nographics', unity_args('tests', self.run, self.run, [], None))

    def test_requires_unique_landed_row_and_recording(self):
        for rows in ['', self.row * 2, self.row.replace('4.5', '-1'),
                     self.row.replace('2.0', 'nan'), self.row.replace('shift 1', 'other')]:
            with self.subTest(rows=rows), self.assertRaises(ValueError):
                self.report.write_text(self.header + rows)
                read_shot(self.report, 'blackout')

    def test_rejects_inconsistent_run_commit(self):
        (self.run / 'run.jsonl').write_text('{"commit":"main"}\n')
        with self.assertRaises(ValueError):
            read_shot(self.report, 'blackout')

    def test_requires_movie_and_silent_sidecar(self):
        with self.assertRaises(ValueError):
            verify_movie(self.run)
        (self.run / 'clip.mp4').write_bytes(b'fixture')
        for audio in [{}, {'audio': 'clip.wav'}]:
            (self.run / 'clip.json').write_text(json.dumps(audio))
            with self.assertRaises(ValueError):
                verify_movie(self.run)
        (self.run / 'clip.json').write_text('{"audio":null}')
        verify_movie(self.run)
