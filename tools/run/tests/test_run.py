"""kehai-run against a stand-in Unity, in temporary folders, with a prefs file instead of the
Mac's. Nothing here touches the real project, the real lock or the editor's real prefs.

    python3 -m unittest tools.run.tests.test_run
"""
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))
from tools.run import kehai_run as kr  # noqa: E402
from tools.run import lock as heavy  # noqa: E402

STANDIN = Path(__file__).with_name('standin_unity.py')
ORIGINAL_PREFS = {'LastUsedProjectPath': '/Users/someone/Developer/Kehai/.claude/worktrees/0.5.0-build'}


def sh(*args, cwd=None):
    subprocess.run(args, cwd=cwd, check=True, capture_output=True)


class Fixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix='kehai-run-test-')
        t = Path(self.tmp.name)
        self.t = t
        # The main project: art and settings (untracked), and code (tracked).
        main = t / 'main'
        for rel, text in {'Assets/old.cs': 'old', 'Assets/keep.cs': 'keep', 'Library/cache.bin': 'cache',
                          'Packages/manifest.json': '{}', 'ProjectSettings/ProjectSettings.asset': 'x:\n  productName: Kehai\n'}.items():
            (main / rel).parent.mkdir(parents=True, exist_ok=True)
            (main / rel).write_text(text)
        sh('git', 'init', '-q', cwd=main)
        sh('git', '-c', 'user.email=t@t', '-c', 'user.name=t', 'add', 'Assets/old.cs', 'Assets/keep.cs', cwd=main)
        sh('git', '-c', 'user.email=t@t', '-c', 'user.name=t', 'commit', '-q', '-m', 'main', cwd=main)
        (main / 'Assets' / 'art.png').write_text('art')   # private art: never tracked
        # The branch under test: old.cs removed, new.cs added.
        repo = t / 'repo'
        sh('git', 'clone', '-q', str(main), str(repo))
        sh('git', 'rm', '-q', 'Assets/old.cs', cwd=repo)
        (repo / 'Assets' / 'new.cs').write_text('new')
        sh('git', 'add', 'Assets/new.cs', cwd=repo)
        sh('git', '-c', 'user.email=t@t', '-c', 'user.name=t', 'commit', '-q', '-m', 'branch', cwd=repo)
        self.prefs = t / 'prefs.json'
        self.prefs.write_text(json.dumps(ORIGINAL_PREFS))
        self.data_root = t / 'data'
        self.s = kr.Settings(repo=repo, main=main, home=t / 'home', unity=str(STANDIN), lock=t / 'heavy.lock',
                             prefs_file=self.prefs, unity_pattern='no-such-process-kehai-run-test',
                             data_root=self.data_root, prefs_root=t / 'prefsroot', min_free_gb=0, notify=False)
        self.env = {**os.environ, 'STANDIN_PREFS': str(self.prefs), 'STANDIN_DATA_ROOT': str(self.data_root)}
        self.old_env = dict(os.environ)
        os.environ.update(self.env)

    def tearDown(self):
        os.environ.clear()
        os.environ.update(self.old_env)
        self.tmp.cleanup()

    def events(self):
        if not self.s.register.exists():
            return []
        return [json.loads(l) for l in self.s.register.read_text().splitlines() if l.strip()]

    def assert_clean(self):
        clones = self.s.home / 'clones'
        self.assertFalse(clones.exists() and any(clones.iterdir()), 'no clone left behind')
        self.assertFalse(self.data_root.exists() and any(self.data_root.iterdir()), 'no data folder left behind')
        self.assertFalse(self.s.lock.exists(), 'the lock is free')
        self.assertEqual(ORIGINAL_PREFS, json.loads(self.prefs.read_text()), 'the prefs are as they were, not reset to a fixed path')
        self.assertFalse(any(self.s.cards.glob('*.json')) if self.s.cards.exists() else False, 'no run card left')

    def run_cli(self, *args, **env):
        e = {**self.env, 'KEHAI_RUN_REPO': str(self.s.repo), 'KEHAI_RUN_MAIN': str(self.s.main),
             'KEHAI_RUN_HOME': str(self.s.home), 'KEHAI_RUN_UNITY': str(STANDIN), 'KEHAI_RUN_LOCK': str(self.s.lock),
             'KEHAI_RUN_PREFS_FILE': str(self.prefs), 'KEHAI_RUN_UNITY_PATTERN': 'no-such-process-kehai-run-test',
             'KEHAI_RUN_DATA_ROOT': str(self.data_root), 'KEHAI_RUN_PREFS_ROOT': str(self.t / 'prefsroot'),
             'KEHAI_RUN_MIN_FREE_GB': '0', 'KEHAI_RUN_NOTIFY': '0', **env}
        return subprocess.Popen([sys.executable, '-m', 'tools.run.kehai_run', *args], cwd=ROOT, env=e,
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)

    def wait_for(self, predicate, seconds=20):
        end = time.time() + seconds
        while time.time() < end:
            if predicate():
                return True
            time.sleep(0.1)
        return False


class RunTests(Fixture):
    def test_success_cleans_up_records_and_tests_the_branch_not_the_main_folder(self):
        code = kr.run(self.s, 'tests', [], log=lambda *_: None)
        self.assertEqual(0, code)
        self.assert_clean()
        out = next((self.s.home / 'out').iterdir())
        self.assertTrue((out / 'results.xml').exists())
        seen = json.loads((out / 'unity.log').read_text().splitlines()[1])
        self.assertTrue(seen['new_file'], "the branch's new file is in the clone")
        self.assertFalse(seen['deleted_file'], 'a file the branch removed is not in the clone')
        self.assertTrue(seen['art'], 'the private art comes from the main folder')
        self.assertTrue(seen['product'].startswith('Kehai-run-'), 'its own product name, so its data stays apart')
        self.assertTrue((out / 'shift_records' / 'shift_001.json').exists(), 'the run\'s records are kept (Q1)')
        states = [e['state'] for e in self.events()]
        self.assertEqual(['running', 'completed'], states)

    def test_unity_failing_still_cleans_up(self):
        os.environ['STANDIN_EXIT'] = '1'
        self.assertEqual(1, kr.run(self.s, 'tests', [], log=lambda *_: None))
        self.assert_clean()
        self.assertEqual('failed', self.events()[-1]['state'])

    def test_a_timeout_kills_unity_and_cleans_up(self):
        os.environ['STANDIN_SLEEP'] = '30'
        start = time.time()
        self.assertEqual(124, kr.run(self.s, 'tests', [], timeout_min=0.03, log=lambda *_: None))
        self.assertLess(time.time() - start, 15)
        self.assert_clean()
        self.assertIn('timed out', self.events()[-1]['note'])

    def test_ctrl_c_or_a_closed_terminal_cleans_up(self):
        for sig in (signal.SIGINT, signal.SIGHUP):
            pidfile = self.t / f'standin-{sig}.pid'
            p = self.run_cli('tests', STANDIN_SLEEP='30', STANDIN_PIDFILE=str(pidfile))
            self.assertTrue(self.wait_for(pidfile.exists), 'Unity started')
            p.send_signal(sig)
            p.wait(timeout=20)
            output = p.stdout.read()
            p.stdout.close()
            self.assertEqual(kr.INTERRUPTED, p.returncode, output)
            self.assert_clean()
            self.assertEqual('interrupted', self.events()[-1]['state'])

    def test_after_kill_9_the_next_run_cleans_up(self):
        pidfile = self.t / 'standin.pid'
        p = self.run_cli('tests', STANDIN_SLEEP='30', STANDIN_PIDFILE=str(pidfile))
        self.assertTrue(self.wait_for(pidfile.exists))
        p.kill()   # SIGKILL: no handler runs
        p.wait(timeout=10)
        p.stdout.close()
        os.kill(int(pidfile.read_text()), signal.SIGKILL)   # its Unity, in its own session
        self.assertTrue(self.s.lock.exists(), 'a killed run leaves its lock')
        self.assertTrue(any(self.s.cards.glob('*.json')), 'and its card')
        self.assertNotEqual(ORIGINAL_PREFS, json.loads(self.prefs.read_text()), 'and the prefs Unity changed')
        cleaned = kr.janitor(self.s, log=lambda *_: None)
        self.assertEqual(1, len(cleaned))
        self.assert_clean()
        self.assertEqual('interrupted', self.events()[-1]['state'])

    def test_dry_run_prints_the_command_and_leaves_nothing(self):
        lines = []
        self.assertEqual(0, kr.run(self.s, 'tests', [], test_filter='Sandbox', dry_run=True, log=lines.append))
        self.assertIn('-testFilter Sandbox', lines[-1])
        self.assertIn('-batchmode -nographics', lines[-1])
        self.assert_clean()

    def test_an_open_unity_stops_the_run(self):
        sleeper = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)', 'kehai-run-test-open-unity'])
        try:
            self.s.unity_pattern = 'kehai-run-test-open-unity'
            self.assertEqual(3, kr.run(self.s, 'tests', [], log=lambda *_: None))
            self.assertFalse(self.s.lock.exists())
        finally:
            sleeper.kill()
            sleeper.wait()


class LockTests(Fixture):
    def test_a_dead_holders_lock_is_taken_over(self):
        self.s.lock.mkdir()
        (self.s.lock / 'pid').write_text('999999')
        (self.s.lock / 'job').write_text('record_tricks trick check')
        ok, dead = heavy.take(self.s.lock, 'next job')
        self.assertTrue(ok)
        self.assertEqual('record_tricks trick check', dead['job'])
        self.assertEqual(str(os.getpid()), heavy.holder(self.s.lock)['pid'])

    def test_a_live_holder_keeps_it_in_both_formats(self):
        sleeper = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)'])
        try:
            # The marketing tools' format: pid and job only.
            self.s.lock.mkdir()
            (self.s.lock / 'pid').write_text(str(sleeper.pid))
            (self.s.lock / 'job').write_text('render_shot')
            self.assertEqual((False, None), heavy.take(self.s.lock, 'next job'))
            # This module's: with the start time.
            (self.s.lock / 'started').write_text(heavy.start_time(sleeper.pid))
            self.assertEqual((False, None), heavy.take(self.s.lock, 'next job'))
        finally:
            sleeper.kill()
            sleeper.wait()

    def test_a_reused_pid_does_not_hold_the_lock(self):
        self.s.lock.mkdir()
        (self.s.lock / 'pid').write_text(str(os.getpid()))
        (self.s.lock / 'started').write_text('Mon Jan  1 00:00:00 2001')
        ok, dead = heavy.take(self.s.lock, 'next job')
        self.assertTrue(ok, 'a live pid with another start time is a different process')

    def test_busy_exits_75(self):
        sleeper = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)'])
        try:
            self.s.lock.mkdir()
            (self.s.lock / 'pid').write_text(str(sleeper.pid))
            self.assertEqual(heavy.BUSY_EXIT, kr.run(self.s, 'tests', [], log=lambda *_: None))
        finally:
            sleeper.kill()
            sleeper.wait()


if __name__ == '__main__':
    unittest.main()
