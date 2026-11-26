#!/usr/bin/env python3
"""Training that restarts itself after a crash (0.5.2, C5.2).

    python3 tools/rl/supervise.py [--restarts 3] [--results tools/rl/results] -- \
        tools/rl/train_editor.sh basic_r2_a tools/rl/karen_basic.yaml tools/rl/leagues/basic_r2.json

It runs the training command. When it fails, the same command runs again from its last
checkpoint (mlagents-learn's --resume, added after the command's `--`), at most --restarts times.
It never restarts after you stop it: Ctrl-C or a closed terminal stops the training and the
supervisor, and an exit of 130 (kehai-run's "stopped") or 75 (the lock is busy) is final too.
A restart with no checkpoint yet (results/<run-id> missing) runs the command as it was.

The run id is the first argument after the script (train_editor.sh's and train_league.sh's first).
Each attempt is one line in <results>/<run-id>.supervise.log.
"""
from __future__ import annotations

import argparse
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

FINAL = {0, 75, 130}      # finished, lock busy, stopped by the owner (kehai-run's INTERRUPTED)
HERE = Path(__file__).resolve().parent


def with_resume(command: list[str]) -> list[str]:
    """Add --resume as an mlagents-learn flag: after the command's `--`, or a new `--` at the end."""
    if '--resume' in command:
        return command
    return command + ['--resume'] if '--' in command else command + ['--', '--resume']


def run_id(command: list[str]) -> str | None:
    """The first plain argument after the program and its script (bash x.sh, python x.py, x.sh)."""
    i = 0
    while i < len(command) and (i == 0 or command[i].endswith(('.sh', '.py', '.ps1'))):
        i += 1
    rest = [c for c in command[i:] if not c.startswith('-')]
    return rest[0] if rest else None


class Supervisor:
    def __init__(self, command: list[str], restarts: int, results: Path, log=print):
        self.command, self.restarts, self.results, self.log = command, restarts, results, log
        self.stopped = False
        self.child: subprocess.Popen | None = None

    def stop(self, signum, frame) -> None:
        """The owner stopped it: pass it on to the training, and never start another attempt."""
        self.stopped = True
        if self.child is not None and self.child.poll() is None:
            try:
                os.killpg(self.child.pid, signal.SIGINT if signum == signal.SIGINT else signal.SIGTERM)
            except OSError:
                pass

    def note(self, text: str) -> None:
        self.log(f'supervise: {text}')
        rid = run_id(self.command)
        if rid:
            self.results.mkdir(parents=True, exist_ok=True)
            with open(self.results / f'{rid}.supervise.log', 'a') as f:
                f.write(f"{time.strftime('%Y-%m-%dT%H:%M:%S')} {text}\n")

    def run(self) -> int:
        old = {s: signal.signal(s, self.stop) for s in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
        try:
            command, attempt = self.command, 0
            while True:
                attempt += 1
                self.note(f'attempt {attempt}: {" ".join(command)}')
                self.child = subprocess.Popen(command, start_new_session=True)
                code = self.child.wait()
                if self.stopped:
                    self.note(f'attempt {attempt} stopped by you (exit {code}); not restarting')
                    return 130
                if code in FINAL or code < 0 and -code in (signal.SIGINT, signal.SIGTERM):
                    self.note(f'attempt {attempt} ended with exit {code}; done')
                    return 0 if code == 0 else (130 if code < 0 else code)
                if attempt > self.restarts:
                    self.note(f'attempt {attempt} failed (exit {code}); {self.restarts} restarts used, giving up')
                    return code
                rid = run_id(self.command)
                resumable = rid is not None and (self.results / rid).is_dir()
                command = with_resume(self.command) if resumable else self.command
                self.note(f'attempt {attempt} failed (exit {code}); restarting '
                          + ('from the last checkpoint' if resumable else 'from the start (no checkpoint yet)'))
        finally:
            for s, h in old.items():
                signal.signal(s, h)


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    p.add_argument('--restarts', type=int, default=3)
    p.add_argument('--results', type=Path, default=HERE / 'results')
    p.add_argument('command', nargs=argparse.REMAINDER)
    a = p.parse_args(argv)
    command = a.command[1:] if a.command[:1] == ['--'] else a.command
    if not command:
        p.error('give the training command after --')
    return Supervisor(command, a.restarts, a.results).run()


if __name__ == '__main__':
    sys.exit(main())
