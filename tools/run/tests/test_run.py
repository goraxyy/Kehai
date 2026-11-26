"""kehai-run against a stand-in Unity, in temporary folders, with a prefs file instead of the
Mac's. Nothing here touches the real project, the real lock or the editor's real prefs.

    python3 -m unittest tools.run.tests.test_run
"""
import json
import os
import shlex
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest.mock import patch
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
        clones = self.s.home / 'clones.noindex'
        self.assertFalse(clones.exists() and any(clones.iterdir()), 'no clone left behind')
        self.assertFalse(self.data_root.exists() and any(self.data_root.iterdir()), 'no data folder left behind')
        self.assertFalse(self.s.lock.exists(), 'the lock is free')
        self.assertEqual(ORIGINAL_PREFS, json.loads(self.prefs.read_text()), 'the prefs are as they were, not reset to a fixed path')
        self.assertFalse(any(self.s.cards.glob('*.json')) if self.s.cards.exists() else False, 'no run card left')

    def cli_env(self, **env):
        return {**self.env, 'KEHAI_RUN_REPO': str(self.s.repo), 'KEHAI_RUN_MAIN': str(self.s.main),
                'KEHAI_RUN_HOME': str(self.s.home), 'KEHAI_RUN_UNITY': str(STANDIN), 'KEHAI_RUN_LOCK': str(self.s.lock),
                'KEHAI_RUN_PREFS_FILE': str(self.prefs), 'KEHAI_RUN_UNITY_PATTERN': 'no-such-process-kehai-run-test',
                'KEHAI_RUN_DATA_ROOT': str(self.data_root), 'KEHAI_RUN_PREFS_ROOT': str(self.t / 'prefsroot'),
                'KEHAI_RUN_MIN_FREE_GB': '0', 'KEHAI_RUN_NOTIFY': '0', **env}

    def run_cli(self, *args, **env):
        return subprocess.Popen([sys.executable, '-m', 'tools.run.kehai_run', *args], cwd=ROOT, env=self.cli_env(**env),
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)

    def wait_for(self, predicate, seconds=20):
        end = time.time() + seconds
        while time.time() < end:
            if predicate():
                return True
            time.sleep(0.1)
        return False


class ProgressTests(unittest.TestCase):
    def test_each_job_says_which_records_it_keeps_and_why(self):
        for job in ('ablation', 'league'):
            self.assertEqual(('on', 'on'), tuple(kr.records_policy(job, [])[k] for k in ('shift', 'learning')))
            off = kr.records_policy(job, ['-karen-learning', 'off'])
            self.assertEqual(('on', 'off'), (off['shift'], off['learning']))
            self.assertIn('Q1', off['why'])
        self.assertEqual(('off', 'off'), tuple(kr.records_policy('demos', [])[k] for k in ('shift', 'learning')))
        self.assertEqual('per test', kr.records_policy('tests', [])['shift'])
        self.assertEqual('off', kr.records_policy('unity', ['-kehai-no-records'])['shift'])
        self.assertEqual('on', kr.records_policy('unity', ['-executeMethod', 'X'])['shift'])
        for job in ('tests', 'ablation', 'league', 'demos', 'unity', 'trick-clip', 'sandbox-clip', 'sight-clips'):
            self.assertTrue(kr.records_policy(job, [])['why'], job)

    def test_a_clone_drops_the_define_a_batch_start_removes_and_keeps_the_rest(self):
        with tempfile.TemporaryDirectory() as d:
            asset = Path(d) / 'ProjectSettings' / 'ProjectSettings.asset'
            asset.parent.mkdir()
            asset.write_text('PlayerSettings:\n  productName: Kehai\n  scriptingDefineSymbols:\n'
                             '    Standalone: KAREN_MLAGENTS;SENTIS_ANALYTICS_ENABLED;APP_UI_EDITOR_ONLY\n'
                             '    Android: SENTIS_ANALYTICS_ENABLED\n  additionalCompilerArguments: {}\n'
                             '  other: SENTIS_ANALYTICS_ENABLED\n')
            kr.set_product(Path(d), 'Kehai-run-x')
            self.assertEqual('PlayerSettings:\n  productName: Kehai-run-x\n  scriptingDefineSymbols:\n'
                             '    Standalone: KAREN_MLAGENTS;APP_UI_EDITOR_ONLY\n    Android: \n'
                             '  additionalCompilerArguments: {}\n  other: SENTIS_ANALYTICS_ENABLED\n', asset.read_text())

    def test_compile_reasons_are_read_from_the_log(self):
        with tempfile.TemporaryDirectory() as d:
            log = Path(d) / 'unity.log'
            log.write_text('x\n[ScriptCompilation] Requested script compilation because: AssetDatabase observed changes\n'
                           'y\n[ScriptCompilation] Requested script compilation because: Define symbols changed\n')
            self.assertEqual(['AssetDatabase observed changes', 'Define symbols changed'], kr.compile_reasons(log))
            self.assertEqual([], kr.compile_reasons(Path(d) / 'missing.log'))

    def test_last_nonblank_line_and_placement_audit_markers(self):
        with tempfile.TemporaryDirectory() as tmp:
            log = Path(tmp) / 'unity.log'
            audit_lines = ('Placement audit island: 32 rows and 0 findings so far.',
                           'Placement audit: 32 rows, 0 findings, report placement.tsv')
            for message in audit_lines:
                with self.subTest(message=message):
                    log.write_text(f'{message}\nUnityEngine.Debug:Log (object)\n\n  \n')
                    self.assertEqual(kr.progress_line(log), message)

            log.write_text('Import started\n  Import finished  \n\n  \n')
            self.assertEqual(kr.progress_line(log), 'Import finished')


class BeliefWrapperTests(Fixture):
    def setUp(self):
        super().setUp()
        self.league = Path('tools/rl/leagues/test league.json')
        league = self.s.repo / self.league
        league.parent.mkdir(parents=True)
        league.write_text('{"source": "committed"}')
        sh('git', 'add', str(self.league), cwd=self.s.repo)
        sh('git', '-c', 'user.email=t@t', '-c', 'user.name=t', 'commit', '-q', '-m', 'league', cwd=self.s.repo)
        league.write_text('{"source": "dirty checkout"}')
        self.clone_root = self.t / 'custom clones'

    def wrapper(self, *args, **env):
        return subprocess.run([ROOT / 'tools/belief/run_unity.sh', *args], cwd=self.t, text=True,
                              env=self.cli_env(KEHAI_CLONE=str(self.clone_root), **env),
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)

    def test_dry_run_preserves_league_and_ablation_arguments(self):
        for mode, limit in ((str(self.league), ''), ('--ablation', '240')):
            with self.subTest(mode=mode):
                output = 'output folder'
                extra = ['-evalScene', 'Assets/A B.unity', '-league-careers', '2', '--']
                result = self.wrapper('--dry-run', mode, output, *extra, KEHAI_TIMEOUT_MIN=limit)
                self.assertEqual(result.returncode, 0, result.stdout)
                args = shlex.split(result.stdout)
                clone = Path(args[4])
                self.assertEqual(clone.parent, self.clone_root)
                mode_args = ['-kehai-ablation'] if mode == '--ablation' else ['-league', str(clone / self.league)]
                self.assertEqual(args, [str(STANDIN), '-batchmode', '-nographics', '-projectPath', str(clone),
                                        '-executeMethod', 'EvalBatch.Play', *mode_args,
                                        '-eval-timeout-min', limit or '600',
                                        '-logFile', str(Path(output) / 'unity.log'), *extra])
                self.assertFalse((self.t / output).exists())
        self.assertFalse(self.s.home.exists())
        self.assertFalse(self.clone_root.exists())
        self.assert_clean()

    def test_wrapper_runs_pinned_league_and_exports_even_on_eval_failure(self):
        for mode, code, name in ((self.s.repo / self.league, 0, 'league'), ('--ablation', 7, 'ablation')):
            with self.subTest(mode=mode):
                output = Path(f'{name} output')
                out = self.t / output
                (out / 'kehai_eval').mkdir(parents=True)
                (out / 'kehai_eval' / 'existing.jsonl').touch()
                result = self.wrapper(str(mode), str(output), '-belief-trace', str(output / 'traces'),
                                      KEHAI_TIMEOUT_MIN='240', STANDIN_EXIT=str(code))
                self.assertEqual(result.returncode, code, result.stdout)
                event = self.events()[-1]
                self.assertEqual(event['exit'], code)
                self.assertEqual(event['kind'], name)
                retained = self.s.runs / event['id']
                for path in ('unity.log', 'shift_records/shift_001.json', f'kehai_eval/{name}_fixture.jsonl'):
                    self.assertEqual((out / path).read_bytes(), (retained / path).read_bytes())
                self.assertTrue((out / 'kehai_eval' / 'existing.jsonl').is_file())
                self.assertTrue((out / 'traces' / 'map.json').is_file())
                seen = json.loads((out / 'unity.log').read_text().splitlines()[1])
                self.assertEqual(seen['source_commit'], event['commit'])
                if name == 'league':
                    self.assertEqual(json.loads(seen['league']), {'source': 'committed'})
                self.assert_clean()
                self.assertFalse(any(self.clone_root.iterdir()))

    def test_export_failure_preserves_run_results_and_releases_lock(self):
        out = self.t / 'blocked export'
        out.mkdir()
        (out / 'kehai_eval').touch()
        result = self.wrapper('--ablation', str(out))
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn('output export', result.stdout)
        event = self.events()[-1]
        self.assertEqual(event['state'], 'failed')
        self.assertTrue((self.s.runs / event['id'] / 'kehai_eval/ablation_fixture.jsonl').is_file())
        self.assert_clean()


class DemoWrapperTests(Fixture):
    def setUp(self):
        super().setUp()
        relative = Path('tools/rl/record_demos_editor.sh')
        self.script = self.s.repo / relative
        self.script.parent.mkdir(parents=True)
        shutil.copy2(ROOT / relative, self.script)
        runner = self.s.repo / 'tools/run/kehai_run.py'
        runner.parent.mkdir()
        runner.symlink_to(ROOT / 'tools/run/kehai_run.py')
        self.demos = self.script.parent / 'demos'
        self.clone_root = self.t / 'demo clones'

    def wrapper(self, *args, **env):
        return subprocess.run([self.script, *args], cwd=self.t, text=True,
                              env=self.cli_env(KEHAI_CLONE=str(self.clone_root), **env),
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)

    def test_dry_run_preserves_demo_arguments_for_each_mode(self):
        for mode in ('employee', 'vital', 'basic'):
            with self.subTest(mode=mode):
                count = [] if mode == 'employee' else ['37']
                result = self.wrapper('--dry-run', mode, *count)
                self.assertEqual(result.returncode, 0, result.stdout)
                args = shlex.split(result.stdout)
                clone = Path(args[4])
                out = Path(args[args.index('-logFile') + 1]).parent
                self.assertEqual(out.resolve(), (self.demos / mode).resolve())
                self.assertEqual(clone.parent, self.clone_root)
                if mode == 'employee':
                    recorder = ['-ablation-profiles', 'efficient,skittish,reckless', '-bot-mind', 'trainer',
                                '-bot-record-demos', str(out), '-bot-demo-steps', '20000']
                else:
                    recorder = ['-karen-brain', mode, '-karen-record-demos', str(out), '-karen-demo-steps', '37']
                self.assertEqual(args, [str(STANDIN), '-batchmode', '-nographics', '-projectPath', str(clone),
                                        '-executeMethod', 'EvalBatch.Play', '-kehai-ablation', '-ablation-interleave',
                                        '-ablation-careers', '1000', '-ablation-shifts', '6', '-ablation-start', '1',
                                        '-ablation-seconds', '150', '-kehai-no-records', '-logFile',
                                        str(out / 'record.log'), '-ablation-rungs', 'E', *recorder])
        self.assertFalse(self.demos.exists())
        self.assertFalse(self.clone_root.exists())
        self.assertFalse(self.s.home.exists())

    def test_demos_job_does_not_accept_arbitrary_unity_arguments(self):
        with self.run_cli('demos', '--mode', 'employee', '--export-dir', str(self.demos), '--dry-run',
                          '--', '-executeMethod', 'EvalBatch.Play', '-league', 'test.json') as run:
            output, _ = run.communicate(timeout=10)
            self.assertEqual(run.returncode, 2, output)
            self.assertIn('unrecognized arguments', output)
        self.assertFalse(self.demos.exists())

    def test_collects_demo_files_and_live_logs_for_each_mode(self):
        for mode, behavior in (('employee', 'Employee'), ('vital', 'KarenVital'), ('basic', 'KarenBasic')):
            with self.subTest(mode=mode):
                result = self.wrapper(mode, '37')
                self.assertEqual(result.returncode, 0, result.stdout)
                out = self.demos / mode
                demo = out / f'{behavior}.demo'
                self.assertEqual(demo.read_bytes(), b'demo fixture')
                self.assertIn(demo.name, result.stdout)
                event = self.events()[-1]
                self.assertEqual((event['kind'], event['state']), ('demos', 'completed'))
                retained = self.s.runs / event['id']
                self.assertEqual((out / 'record.log').read_bytes(), (retained / 'unity.log').read_bytes())
                self.assertFalse((retained / 'shift_records').exists())
                self.assertTrue((retained / 'kehai_eval').exists())
                self.assertFalse((out / 'kehai_eval').exists())
                self.assert_clean()
                self.assertFalse(any(self.clone_root.iterdir()))

    def test_preserves_unity_failure_exit_and_fails_when_no_demo_exists(self):
        for mode, unity_code, expected in (('vital', 7, 7), ('basic', 0, 1)):
            with self.subTest(mode=mode):
                result = self.wrapper(mode, STANDIN_EXIT=str(unity_code), STANDIN_SKIP_DEMO='1')
                self.assertEqual(result.returncode, expected, result.stdout)
                event = self.events()[-1]
                self.assertEqual((event['state'], event['exit']), ('failed', expected))
                self.assertTrue((self.demos / mode / 'record.log').exists())
                self.assert_clean()


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
        self.assertEqual(self.events()[0]['commit'], seen['source_commit'], 'Unity can stamp the exact archived commit')
        self.assertTrue(seen['product'].startswith('Kehai-run-'), 'its own product name, so its data stays apart')
        self.assertTrue((out / 'shift_records' / 'shift_001.json').exists(), 'the run\'s records are kept (Q1)')
        self.assertEqual(b'fixture replay', (out / 'shift_records/shift_001.krec').read_bytes())
        self.assertEqual(self.events(), [json.loads(line) for line in (out / 'run.jsonl').read_text().splitlines()])
        self.assertEqual(out.name, self.events()[0]['id'])
        states = [e['state'] for e in self.events()]
        self.assertEqual(['running', 'completed'], states)

    def test_status_shows_live_progress_and_register_outcome_from_stand_in(self):
        finish = self.t / 'finish'
        marker = 'Running tests for ExecutionSettings'

        def status_output():
            with self.run_cli('status') as status:
                output, _ = status.communicate(timeout=10)
                self.assertEqual(status.returncode, 0, output)
                return output

        def progress_seen():
            return any(marker in progress.read_text() for progress in self.s.runs.glob('*/progress'))

        with self.run_cli('tests', '--timeout-min', '0.5', STANDIN_FINISH_FILE=str(finish)) as runner:
            try:
                self.assertTrue(self.wait_for(lambda: runner.poll() is not None or progress_seen()),
                                'the runner recorded progress')
                if runner.poll() is not None:
                    output, _ = runner.communicate()
                    self.fail(f'stand-in runner exited before recording progress:\n{output}')
                start, = self.events()
                self.assertEqual(start['state'], 'running')
                rid = start['id']
                output = status_output()
                self.assertRegex(output, rf'{rid} tests running: tests {rid} \d+s {marker}\n')
                self.assertIn(f'last: {rid} tests running', output)
            finally:
                finish.touch()
                output, _ = runner.communicate(timeout=20)
            self.assertEqual(runner.returncode, 0, output)

        self.assert_clean()
        self.assertEqual([(e['id'], e['state']) for e in self.events()],
                         [(rid, 'running'), (rid, 'completed')])
        output = status_output()
        self.assertIn('The heavy-work lock: free', output)
        self.assertIn(f'last: {rid} tests completed', output)

    def test_unknown_editor_state_stops_before_cleanup(self):
        result = subprocess.CompletedProcess([], 1, '', 'cannot get process list')
        with patch.object(kr, 'git', return_value='abc'), patch('subprocess.run', return_value=result):
            self.assertEqual(3, kr.run(self.s, 'tests', [], log=lambda *_: None))
        self.assertFalse(self.s.home.exists())

    def test_recording_cannot_be_disabled_for_a_test_shift(self):
        for job, args in [('ablation', []), ('league', []), ('tests', []),
                          ('unity', ['-executeMethod', 'EvalBatch.Play'])]:
            self.assertEqual(2, kr.run(self.s, job, args + ['-kehai-no-records'], log=lambda *_: None))
        self.assertFalse(self.s.home.exists())

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

    def test_learning_off_reaches_unity_and_the_register_says_so(self):
        p = self.run_cli('ablation', '--learning', 'off', '--', '-ablation-careers', '1')
        text, _ = p.communicate(timeout=60)
        self.assertEqual(0, p.returncode, text)
        self.assertIn('shift records on, learning records off', text)
        running = self.events()[0]
        self.assertEqual(['-karen-learning', 'off'], running['args'][-2:])
        self.assertEqual('off', running['records']['learning'])
        self.assertEqual('on', running['records']['shift'])
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
