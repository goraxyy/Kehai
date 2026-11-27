"""kehai-run train (0.5.2, C1.4): the trainer beside the batch editor, on stand-ins for both.

    python3 -m unittest tools.run.tests.test_train
"""
import json
import os
import shlex
import subprocess
import sys
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT))
from tools.run import kehai_run as kr  # noqa: E402
from tools.run.tests.test_run import Fixture, sh  # noqa: E402

GIT = ('git', '-c', 'user.email=t@t', '-c', 'user.name=t')
TRAINER = '''import json, os, signal, subprocess, sys, time
from pathlib import Path
worker = None
if os.environ.get("TRAINER_WORKER"):
    # Like ML-Agents' environment worker: a child in the trainer's process group.
    worker = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(120)"])
Path(os.environ["TRAINER_SEEN"]).write_text(json.dumps({"args": sys.argv[1:], "cwd": os.getcwd(), "pid": os.getpid(),
                                                        "worker": worker.pid if worker else None}))
time.sleep(float(os.environ.get("TRAINER_SLEEP", "0")))
if os.environ.get("TRAINER_CRASH"): os.kill(os.getpid(), signal.SIGKILL)
sys.exit(int(os.environ.get("TRAINER_EXIT", "0")))
'''


def alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


class TrainTests(Fixture):
    def setUp(self):
        super().setUp()
        league = self.s.repo / 'tools' / 'rl' / 'leagues' / 'smoke.json'
        league.parent.mkdir(parents=True)
        league.write_text('{}')
        sh('git', 'add', 'tools/rl/leagues/smoke.json', cwd=self.s.repo)
        sh(*GIT, 'commit', '-q', '-m', 'league', cwd=self.s.repo)
        self.league = Path('tools/rl/leagues/smoke.json')
        script = self.t / 'trainer.py'
        script.write_text(TRAINER)
        self.seen = self.t / 'trainer.json'
        os.environ['TRAINER_SEEN'] = str(self.seen)
        self.trainer = kr.Trainer([sys.executable, str(script), 'config.yaml', '--run-id', 'r1'],
                                  cwd=self.s.repo, head_start=0.3)

    def train(self, **kw):
        lines = []
        code = kr.run(self.s, 'train', ['-karen-set', 'catchDial=0.15'], league=self.league,
                      trainer=self.trainer, log=lines.append, timeout_min=kw.pop('timeout_min', 2.0), **kw)
        return code, lines

    def unity_args(self):
        out = next((self.s.home / 'out').iterdir())
        return json.loads((out / 'unity.log').read_text().splitlines()[1])['args']

    def test_the_trainer_finishing_ends_the_run_and_stops_unity(self):
        os.environ.update(TRAINER_SLEEP='2', STANDIN_SLEEP='60')
        began = time.time()
        code, lines = self.train()
        self.assertEqual(0, code, lines)
        self.assertLess(time.time() - began, 30, 'Unity was stopped, not waited for')
        self.assertIn('the trainer finished', self.events()[-1]['note'])
        args = self.unity_args()
        i = args.index('-executeMethod')
        self.assertEqual(['-executeMethod', 'EvalBatch.Play', '-league'], args[i:i + 3])
        self.assertTrue(args[i + 3].endswith('/tools/rl/leagues/smoke.json'))
        self.assertEqual('-kehai-no-records', args[i + 4])
        self.assertEqual(['-karen-set', 'catchDial=0.15'], args[-2:])
        trainer = json.loads(self.seen.read_text())
        self.assertEqual(['config.yaml', '--run-id', 'r1'], trainer['args'])
        self.assertEqual('off', self.events()[0]['records']['learning'])
        self.assert_clean()

    def test_a_failing_trainer_fails_the_run(self):
        os.environ.update(TRAINER_SLEEP='1', TRAINER_EXIT='3', STANDIN_SLEEP='60')
        code, lines = self.train()
        self.assertEqual(3, code, lines)
        self.assertEqual('failed', self.events()[-1]['state'])
        self.assert_clean()

    def test_a_trainer_that_dies_hard_leaves_no_worker_behind(self):
        # The real smoke run (26 November): after a kill -9 the trainer's environment worker kept
        # its port, and every resume failed with "worker 0 is still in use".
        os.environ.update(TRAINER_SLEEP='2', TRAINER_CRASH='1', TRAINER_WORKER='1', STANDIN_SLEEP='60')
        code, lines = self.train()
        self.assertNotEqual(0, code, lines)
        worker = json.loads(self.seen.read_text())['worker']
        deadline = time.time() + 5
        while alive(worker) and time.time() < deadline:
            time.sleep(0.1)
        self.assertFalse(alive(worker), "the trainer's worker is gone with it")
        self.assert_clean()

    def test_unity_ending_first_stops_the_trainer(self):
        os.environ.update(TRAINER_SLEEP='120', STANDIN_SLEEP='0')
        code, lines = self.train()
        self.assertEqual(0, code, lines)
        self.assertFalse(alive(json.loads(self.seen.read_text())['pid']), 'the trainer is not left running')
        self.assert_clean()

    def test_a_trainer_that_dies_at_once_never_starts_unity(self):
        os.environ.update(TRAINER_SLEEP='0', TRAINER_EXIT='2')
        self.trainer.head_start = 3.0   # time for Python to start and exit on a busy Mac (real: 20 s)
        code, lines = self.train()
        self.assertEqual(1, code, lines)
        out = next((self.s.home / 'out').iterdir())
        self.assertFalse((out / 'unity.log').exists())
        self.assertIn('before Unity started', self.events()[-1]['note'])
        self.assert_clean()

    def test_train_editor_dry_run_prints_the_old_unity_command(self):
        script = self.s.repo / 'tools' / 'rl' / 'train_editor.sh'
        script.write_text((ROOT / 'tools' / 'rl' / 'train_editor.sh').read_text())
        runner = self.s.repo / 'tools' / 'run' / 'kehai_run.py'
        runner.parent.mkdir(parents=True)
        runner.symlink_to(ROOT / 'tools' / 'run' / 'kehai_run.py')
        result = subprocess.run(['bash', str(script), '--dry-run', 'basic_r2_a', 'karen_basic.yaml', str(self.league),
                                 '-karen-set', 'catchDial=0.15', '--', '--resume'],
                                cwd=self.s.repo, env=self.cli_env(), capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        trainer_line, unity_line = result.stdout.strip().splitlines()[-2:]
        self.assertIn('--run-id basic_r2_a', trainer_line)
        self.assertIn('--timeout-wait 900', trainer_line)
        self.assertTrue(trainer_line.rstrip().endswith('--resume'))
        args = shlex.split(unity_line)
        # train_editor.sh before C1.4: -batchmode -nographics -projectPath <clone> -executeMethod
        # EvalBatch.Play -league <clone>/<league> -kehai-no-records -logFile <log> <game flags>
        self.assertEqual(['-batchmode', '-nographics', '-projectPath'], args[1:4])
        self.assertEqual(['-executeMethod', 'EvalBatch.Play', '-league'], args[5:8])
        self.assertTrue(args[8].endswith('tools/rl/leagues/smoke.json'))
        self.assertEqual(['-kehai-no-records', '-logFile'], args[9:11])
        self.assertEqual(['-karen-set', 'catchDial=0.15'], args[12:])
        # No game flags and no trainer flags: macOS's bash 3.2 called the empty arrays unbound
        # (found by the real smoke run, 26 November).
        bare = subprocess.run(['bash', str(script), '--dry-run', 'smoke', 'karen_basic.yaml', str(self.league)],
                              cwd=self.s.repo, env=self.cli_env(), capture_output=True, text=True)
        self.assertEqual(0, bare.returncode, bare.stdout + bare.stderr)
        bare_args = shlex.split(bare.stdout.strip().splitlines()[-1])
        self.assertEqual('-logFile', bare_args[-2], 'the Unity command ends at its log: no game flags')
        self.assert_clean()


if __name__ == '__main__':
    unittest.main()
