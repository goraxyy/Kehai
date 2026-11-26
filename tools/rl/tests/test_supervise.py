"""C5.2: training that restarts itself, on a stand-in training command (no Unity, no trainer)."""
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import supervise  # noqa: E402

SUPERVISE = Path(__file__).resolve().parents[1] / 'supervise.py'
# A stand-in for train_editor.sh: <run-id> <config> <league> [-- learn flags]. It records each
# attempt's flags, makes its checkpoint folder, then crashes (kill -9) until its budget is spent.
STANDIN = '''import json, os, signal, sys, time
from pathlib import Path
results = Path(os.environ["RESULTS"]); attempts = results / "attempts.jsonl"
with open(attempts, "a") as f: f.write(json.dumps(sys.argv[1:]) + "\\n")
(results / sys.argv[1]).mkdir(parents=True, exist_ok=True)
n = len(attempts.read_text().splitlines())
if os.environ.get("HANG"): time.sleep(60)
if n <= int(os.environ.get("CRASHES", "0")): os.kill(os.getpid(), signal.SIGKILL)
sys.exit(int(os.environ.get("EXIT", "0")))
'''


def setup(tmp_path, **env):
    tmp_path.mkdir(parents=True, exist_ok=True)
    script = tmp_path / 'train.py'
    script.write_text(STANDIN)
    results = tmp_path / 'results'
    results.mkdir()
    os.environ.update({'RESULTS': str(results), 'CRASHES': '0', 'EXIT': '0', 'HANG': '', **env})
    return [sys.executable, str(script), 'smoke_1', 'smoke.yaml', 'league.json'], results


def attempts(results):
    return [json.loads(l) for l in (results / 'attempts.jsonl').read_text().splitlines()]


def test_resume_goes_after_the_commands_own_separator():
    assert supervise.with_resume(['t.sh', 'r', '--', '--debug']) == ['t.sh', 'r', '--', '--debug', '--resume']
    assert supervise.with_resume(['t.sh', 'r', '-x', '1']) == ['t.sh', 'r', '-x', '1', '--', '--resume']
    assert supervise.with_resume(['t.sh', 'r', '--', '--resume']) == ['t.sh', 'r', '--', '--resume']
    assert supervise.run_id(['t.sh', '--dry-run', 'run_7', 'c.yaml']) == 'run_7'
    assert supervise.run_id(['bash', 'tools/rl/train_editor.sh', 'run_8', 'c.yaml']) == 'run_8'
    assert supervise.run_id(['/usr/bin/python3', 'x/train.py', 'run_9']) == 'run_9'


def test_a_training_killed_mid_run_resumes_and_finishes(tmp_path):
    command, results = setup(tmp_path, CRASHES='1')
    code = supervise.Supervisor(command, 3, results, log=lambda *_: None).run()
    assert code == 0
    runs = attempts(results)
    assert len(runs) == 2
    assert '--resume' not in runs[0]
    assert runs[1][-2:] == ['--', '--resume'], 'the second attempt carries on from the checkpoint'
    log = (results / 'smoke_1.supervise.log').read_text()
    assert 'restarting from the last checkpoint' in log


def test_at_most_three_restarts(tmp_path):
    command, results = setup(tmp_path, CRASHES='10')
    code = supervise.Supervisor(command, 3, results, log=lambda *_: None).run()
    assert code == -signal.SIGKILL
    assert len(attempts(results)) == 4, 'the first try and three restarts'
    assert 'giving up' in (results / 'smoke_1.supervise.log').read_text()


def test_a_stopped_or_busy_run_is_never_restarted(tmp_path):
    for exit_code in (130, 75):
        command, results = setup(tmp_path / str(exit_code), EXIT=str(exit_code))
        code = supervise.Supervisor(command, 3, results, log=lambda *_: None).run()
        assert code == exit_code
        assert len(attempts(results)) == 1


def test_ctrl_c_stops_the_training_and_the_supervisor(tmp_path):
    command, results = setup(tmp_path, HANG='1', CRASHES='10')
    p = subprocess.Popen([sys.executable, str(SUPERVISE), '--results', str(results), '--', *command],
                         env=os.environ.copy(), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    deadline = time.time() + 20
    while not (results / 'attempts.jsonl').exists() and time.time() < deadline:
        time.sleep(0.1)
    p.send_signal(signal.SIGINT)
    out, _ = p.communicate(timeout=30)
    assert p.returncode == 130, out
    assert len(attempts(results)) == 1, 'no restart after you stop it'
    assert 'stopped by you' in out
