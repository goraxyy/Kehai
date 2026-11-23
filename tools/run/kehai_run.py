"""kehai-run: one command that runs a Unity job safely, end to end (0.5.2 plan, Phase 1).

    tools/run/kehai-run tests [--filter Sandbox]        the EditMode suite (or part of it)
    tools/run/kehai-run ablation -- -ablation-careers 1 -ablation-shifts 2 -ablation-rungs CE
    tools/run/kehai-run unity -- -executeMethod EvalBatch.Play -kehai-trick-check
    tools/run/kehai-run status                          what's running, and what ran last
    tools/run/kehai-run doctor [--clean]                leftovers from runs that died

What a run does, in order (the plan's testing page, section 2):
  1. checks: no Unity open, enough free disk, the heavy-work lock free (a dead holder's is taken over);
  2. a run card, written before anything is touched: the clone's path, the editor prefs as they
     are, the product name and its data folder; so a crash can be cleaned up later;
  3. an APFS clone of the main project's Assets, Library, Packages and ProjectSettings only, the
     commit laid on top, and every file the commit doesn't have removed (no stale code);
  4. Unity, silent (batch mode), with a time limit and a log, a progress line kept as it runs;
  5. results and the clone's game data copied out (every test shift recorded, Q1), the prefs put
     back to the values in the card, the clone, its data folder and its prefs file removed, the
     lock freed; start and end written to the run register; one silent notice.
Ctrl-C, closing the terminal or a session ending run step 5. A crash or `kill -9` can't: the next
run (or `doctor --clean`) reads the dead run's card and does step 5 for it.

Everything that touches the Mac goes through Settings, so the tests run it with a stand-in Unity,
temporary folders and a prefs file instead of the real ones.
"""
from __future__ import annotations

import argparse
import json
import os
import shlex
import shutil
import signal
import socket
import subprocess
import sys
import time
import uuid
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import TYPE_CHECKING

CLONES = 'clones.noindex'   # where the project copies go, under the run home

if __package__ in (None, ''):
    sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from tools.run import lock as heavy  # noqa: E402

if TYPE_CHECKING:
    from tools.run.sight_clips import SightReview

UNITY = '/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity'
UNITY_PATTERN = 'Unity.app/Contents/MacOS/Unity'
PREFS_DOMAIN = 'com.unity3d.UnityEditor5.x'
PREFS_KEYS = ('LastUsedProjectPath', 'kProjectBasePath', 'kWorkspacePath')
UNSET = '__unset__'
CLONED = ('Assets', 'Library', 'Packages', 'ProjectSettings')
INTERRUPTED = 130


@dataclass
class Settings:
    repo: Path                     # the checkout whose commit is tested
    main: Path                     # the project with the private files (art, Library, settings)
    home: Path                     # cards, run folders and the register live here
    unity: str = UNITY
    lock: Path = heavy.DEFAULT
    prefs_file: Path | None = None  # tests: a JSON file instead of `defaults`
    unity_pattern: str = UNITY_PATTERN
    data_root: Path = Path.home() / 'Library' / 'Application Support' / 'TokenLimit'
    prefs_root: Path = Path.home() / 'Library' / 'Preferences'
    min_free_gb: float = 6.0
    notify: bool = True

    @property
    def cards(self) -> Path:
        return self.home / 'cards'

    @property
    def runs(self) -> Path:
        return self.home / 'out'

    @property
    def register(self) -> Path:
        return self.home / 'runs.jsonl'

    @staticmethod
    def from_env(repo: Path | None = None) -> 'Settings':
        repo = repo or Path(__file__).resolve().parents[2]
        e = os.environ
        return Settings(
            repo=Path(e.get('KEHAI_RUN_REPO', repo)),
            main=Path(e.get('KEHAI_RUN_MAIN', Path.home() / 'Developer' / 'Kehai')),
            home=Path(e.get('KEHAI_RUN_HOME', Path.home() / 'TokenLimit' / 'runs')),
            unity=e.get('KEHAI_RUN_UNITY', UNITY),
            lock=Path(e.get('KEHAI_RUN_LOCK', heavy.DEFAULT)),
            prefs_file=Path(e['KEHAI_RUN_PREFS_FILE']) if e.get('KEHAI_RUN_PREFS_FILE') else None,
            unity_pattern=e.get('KEHAI_RUN_UNITY_PATTERN', UNITY_PATTERN),
            data_root=Path(e.get('KEHAI_RUN_DATA_ROOT', Path.home() / 'Library' / 'Application Support' / 'TokenLimit')),
            prefs_root=Path(e.get('KEHAI_RUN_PREFS_ROOT', Path.home() / 'Library' / 'Preferences')),
            min_free_gb=float(e.get('KEHAI_RUN_MIN_FREE_GB', '6')),
            notify=e.get('KEHAI_RUN_NOTIFY', '1') != '0',
        )


@dataclass
class Card:
    id: str
    job: str
    pid: int
    started: str
    clone: str
    out: str
    product: str
    data: str
    prefs_plist: str
    prefs: dict = field(default_factory=dict)
    commit: str = ''
    created: float = 0.0
    export_dir: str = ''

    def path(self, s: Settings) -> Path:
        return s.cards / f'{self.id}.json'

    def save(self, s: Settings) -> None:
        s.cards.mkdir(parents=True, exist_ok=True)
        tmp = self.path(s).with_suffix('.tmp')
        tmp.write_text(json.dumps(asdict(self), indent=1))
        tmp.replace(self.path(s))

    @staticmethod
    def load(p: Path) -> 'Card':
        return Card(**json.loads(p.read_text()))


# ---- the Mac's parts --------------------------------------------------------------------------

def read_prefs(s: Settings) -> dict:
    if s.prefs_file is not None:
        try:
            stored = json.loads(s.prefs_file.read_text())
        except (OSError, ValueError):
            stored = {}
        return {k: stored.get(k, UNSET) for k in PREFS_KEYS}
    values = {}
    for k in PREFS_KEYS:
        out = subprocess.run(['defaults', 'read', PREFS_DOMAIN, k], capture_output=True, text=True)
        values[k] = out.stdout.strip() if out.returncode == 0 else UNSET
    return values


def write_prefs(s: Settings, values: dict) -> None:
    if s.prefs_file is not None:
        try:
            stored = json.loads(s.prefs_file.read_text())
        except (OSError, ValueError):
            stored = {}
        for k, v in values.items():
            if v == UNSET:
                stored.pop(k, None)
            else:
                stored[k] = v
        s.prefs_file.write_text(json.dumps(stored))
        return
    for k, v in values.items():
        if v == UNSET:
            subprocess.run(['defaults', 'delete', PREFS_DOMAIN, k], capture_output=True)
        else:
            subprocess.run(['defaults', 'write', PREFS_DOMAIN, k, v], capture_output=True)


def unity_open(s: Settings) -> bool:
    probe = subprocess.run(['pgrep', '-f', s.unity_pattern], capture_output=True, text=True)
    if probe.returncode == 1 and not probe.stderr.strip():
        return False
    if probe.returncode == 0:
        return True
    raise OSError('cannot inspect the process list: ' + (probe.stderr.strip() or str(probe.returncode)))


def free_gb(path: Path) -> float:
    return shutil.disk_usage(path).free / 1e9


def git(repo: Path, *args: str) -> str:
    return subprocess.run(['git', '-C', str(repo), *args], capture_output=True, text=True, check=True).stdout.strip()


def notify(s: Settings, text: str) -> None:
    """One silent banner on the Mac (no sound is named, so none plays)."""
    if not s.notify or sys.platform != 'darwin':
        return
    safe = text.replace('\\', ' ').replace('"', "'")
    subprocess.run(['osascript', '-e', f'display notification "{safe}" with title "Kehai"'], capture_output=True)


def record(s: Settings, card: Card, state: str, **extra) -> None:
    """Start and end of every run in the run register (tools/reports/register.py)."""
    from tools.reports.register import append
    event = {'id': card.id, 'state': state, 'kind': card.job, 'commit': card.commit,
             'computer': socket.gethostname(), 'time': time.strftime('%Y-%m-%dT%H:%M:%S%z'), **extra}
    try:
        append(Path(card.out) / 'run.jsonl', event)
        append(s.register, event)
    except (ValueError, OSError) as err:
        print(f'kehai-run: the register refused the event ({err})', file=sys.stderr)


# ---- one run ----------------------------------------------------------------------------------

def make_clone(s: Settings, clone: Path, ref: str) -> None:
    """The four folders, cloned (cheap on APFS), then the commit laid on, then its deletions."""
    if clone.exists():
        shutil.rmtree(clone)
    clone.mkdir(parents=True)
    for name in CLONED:
        src = s.main / name
        if src.exists():
            subprocess.run(['cp', '-Rc' if sys.platform == 'darwin' else '-R', str(src), str(clone)], check=True)
    archive = subprocess.Popen(['git', '-C', str(s.repo), 'archive', ref], stdout=subprocess.PIPE)
    subprocess.run(['tar', '-x', '-C', str(clone)], stdin=archive.stdout, check=True)
    archive.stdout.close()
    if archive.wait() != 0:
        raise RuntimeError(f'git archive {ref} failed')
    # Files the main folder tracks that the commit doesn't: removed, so they can't compile.
    try:
        main_head = git(s.main, 'rev-parse', 'HEAD')
        gone = git(s.repo, 'diff', '--name-only', '--diff-filter=D', main_head, ref).splitlines()
    except subprocess.CalledProcessError:
        gone = []
    for rel in gone:
        for p in (clone / rel, clone / (rel + '.meta')):
            if p.is_file():
                p.unlink()
    shutil.rmtree(clone / 'Temp', ignore_errors=True)


def set_product(clone: Path, product: str) -> None:
    p = clone / 'ProjectSettings' / 'ProjectSettings.asset'
    if not p.exists():
        return
    lines = p.read_text().splitlines(keepends=True)
    p.write_text(''.join(f'  productName: {product}\n' if ln.startswith('  productName: ') else ln for ln in lines))


def cleanup(s: Settings, card: Card, log=print) -> None:
    """Step 5 for `card`: prefs back, clone, data folder and prefs file gone, lock freed."""
    write_prefs(s, card.prefs)
    shutil.rmtree(card.clone, ignore_errors=True)
    shutil.rmtree(card.data, ignore_errors=True)
    try:
        Path(card.prefs_plist).unlink()
    except OSError:
        pass
    heavy.release(s.lock, card.pid)
    try:
        card.path(s).unlink()
    except OSError:
        pass
    log(f'kehai-run: cleaned up after {card.id} ({card.job})')


def janitor(s: Settings, log=print) -> list[str]:
    """Clean up after every run whose process has died (a crash, kill -9, a power cut)."""
    cleaned = []
    if not s.cards.exists():
        return cleaned
    for p in sorted(s.cards.glob('*.json')):
        try:
            card = Card.load(p)
        except (OSError, ValueError, TypeError):
            continue
        if heavy.alive(card.pid, card.started or None):
            continue
        export_error = copy_out(card)
        cleanup(s, card, log)
        if export_error:
            log('kehai-run: ' + export_error)
        record(s, card, 'interrupted', exit=INTERRUPTED, note='cleaned up by a later run: its process had died')
        cleaned.append(card.id)
    return cleaned


def copy_out(card: Card) -> str:
    """What the run produced in its data folder: shift records, replays, eval output (Q1)."""
    data = Path(card.data)
    out = Path(card.out)
    for name in ('shift_records', 'kehai_eval', 'karen_logs', 'replays', 'sandbox_checks'):
        if (data / name).exists():
            shutil.copytree(data / name, out / name, dirs_exist_ok=True)
    if card.export_dir:
        export = Path(card.export_dir)
        try:
            if (export / 'unity.log').is_file():
                shutil.copy2(export / 'unity.log', out / 'unity.log')
            for name in ('shift_records', 'kehai_eval'):
                if (out / name).is_dir():
                    shutil.copytree(out / name, export / name, dirs_exist_ok=True)
        except OSError as err:
            return f'output export to {export} failed ({err}). Retained results: {out}'
    return ''


def unity_args(job: str, clone: Path, out: Path, extra: list[str], test_filter: str | None,
               *, league: Path | None = None, eval_timeout: str | None = None,
               export_dir: Path | None = None) -> list[str]:
    base = ['-batchmode', '-nographics', '-projectPath', str(clone)]
    logging = ['-logFile', str((export_dir or out) / 'unity.log')]
    if job in ('league', 'ablation'):
        mode = ['-league', str(clone / league)] if league is not None else ['-kehai-ablation']
        eval_args = ['-executeMethod', 'EvalBatch.Play', *mode]
        if eval_timeout is not None:
            return base + eval_args + ['-eval-timeout-min', eval_timeout] + logging + extra
        return base + logging + eval_args + extra
    base += logging
    if job in ('trick-clip', 'sandbox-clip'):
        base.remove('-nographics')
        entry = 'PursuitSandboxChecks.Render' if job == 'sandbox-clip' else 'Kehai.Replay.ReplayRenderBatch.Run'
        return base + ['-noaudio', '-executeMethod', entry,
                       '-silent-video', '-out', str(out / 'clip.mp4')] + extra
    if job == 'tests':
        args = base + ['-runTests', '-testPlatform', 'EditMode', '-testResults', str(out / 'results.xml')]
        if test_filter:
            args += ['-testFilter', test_filter]
        return args + extra
    return base + extra


class Stopped(Exception):
    pass


def _stop(signum, frame):
    raise Stopped(signal.Signals(signum).name)


def progress_line(log: Path) -> str:
    try:
        with open(log, 'rb') as f:
            f.seek(0, 2)
            f.seek(max(0, f.tell() - 20000))
            tail = f.read().decode('utf-8', 'replace').splitlines()
    except OSError:
        return ''
    last_nonblank = ''
    for line in reversed(tail):
        line = line.strip()
        last_nonblank = last_nonblank or line
        if ('EvalBatch progress' in line or 'Running tests' in line or 'Test run' in line
                or line.startswith('Placement audit:')
                or (line.startswith('Placement audit ') and ':' in line)):
            return line[:200]
    return last_nonblank[:200]


def run(s: Settings, job: str, extra: list[str], ref: str = 'HEAD', timeout_min: float = 90.0,
        test_filter: str | None = None, dry_run: bool = False, log=print,
        sight: SightReview | None = None, *, league: Path | None = None,
        eval_timeout: str | None = None, export_dir: Path | None = None,
        clone_root: Path | None = None) -> int:
    if '-kehai-no-records' in extra and (job in ('tests', 'league', 'ablation') or 'EvalBatch.Play' in extra):
        log('kehai-run: test shifts must keep their records and replays.')
        return 2
    rid = time.strftime('%Y%m%d-%H%M%S-') + uuid.uuid4().hex[:6]
    commit = git(s.repo, 'rev-parse', '--verify', (sight.commit if sight else ref) + '^{commit}')
    if league is not None:
        repo = Path(os.path.abspath(s.repo))
        try:
            league = Path(os.path.abspath(repo / league)).relative_to(repo)
        except ValueError:
            log('kehai-run: the league must be a path inside the checkout.')
            return 1
        entry = git(repo, '--literal-pathspecs', 'ls-tree', commit, '--', league.as_posix())
        if not entry.startswith(('100644 blob ', '100755 blob ')):
            log(f'kehai-run: league {league} must be a file committed at {commit}.')
            return 1
    product = 'Kehai-run-' + rid
    out = s.runs / rid
    # .noindex: Spotlight skips the folder. It indexed every fresh copy otherwise, loading the
    # Mac enough to fail timing tests (22 November).
    clone = (clone_root or s.home / CLONES) / rid
    args = [s.unity] + unity_args(job, clone, out, extra, test_filter,
                                 league=league, eval_timeout=eval_timeout, export_dir=export_dir)
    commands = [(out, args)] if sight is None else [
        (out / case.name, [s.unity] + unity_args('sandbox-clip', clone, out / case.name, case.shot(), None))
        for case in sight.cases
    ]
    if dry_run:
        for _, command in commands:
            log(shlex.join(command))
        return 0
    # The archive in the clone has no .git directory. Let Unity stamp the ref we actually exported.
    env = {**os.environ, 'KEHAI_RUN_CODE_COMMIT': commit}
    if sight:
        from tools.reports.common import private_path
        from tools.run.sight_clips import verify_sight_movie, video_tools
        try:
            private_path(out)
            encoder, probe = video_tools(s.main)
        except ValueError as err:
            log(f'kehai-run: {err}')
            return 3
        env['KEHAI_FFMPEG'] = str(encoder)
    try:
        if unity_open(s):
            log('kehai-run: Unity is open. Close the editor first, or use the PC.')
            return 1 if export_dir is not None else 3
    except OSError as err:
        log(f'kehai-run: {err}')
        return 1 if export_dir is not None else 3
    s.home.mkdir(parents=True, exist_ok=True)
    disk = next(p for p in (clone_root, *clone_root.parents) if p.exists()) if clone_root else s.home
    if free_gb(disk) < s.min_free_gb:
        log(f'kehai-run: less than {s.min_free_gb:g} GB free; not starting.')
        return 1 if export_dir is not None else 4
    try:
        ok, dead = heavy.take(s.lock, f'kehai-run {job} {commit} ({rid})')
    except OSError as err:
        log(f'kehai-run: cannot take the lock: {err}')
        return heavy.BUSY_EXIT
    if not ok:
        info = heavy.holder(s.lock)
        log(f"kehai-run: another heavy job holds the lock: {info.get('job', 'unknown')} (pid {info.get('pid', '?')})")
        return heavy.BUSY_EXIT
    if dead:
        log(f"kehai-run: took over a lock left by a stopped job: {dead.get('job', 'unknown')} (pid {dead.get('pid', '?')})")
    try:
        janitor(s, log)
        out.mkdir(parents=True)
    except Exception:
        heavy.release(s.lock)
        raise
    card = Card(id=rid, job=job, pid=os.getpid(), started=heavy.start_time(os.getpid()) or '', clone=str(clone),
                out=str(out), product=product, data=str(s.data_root / product),
                prefs_plist=str(s.prefs_root / f'unity.TokenLimit.{product}.plist'),
                prefs=read_prefs(s), commit=commit, created=time.time())
    card.save(s)
    if sight:
        record(s, card, 'running', source=str(sight.source), shots=[cmd[1:] for _, cmd in commands])
    else:
        record(s, card, 'running', args=args[1:])
    old = {sig: signal.signal(sig, _stop) for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
    code, state, note = 1, 'failed', ''
    began = time.time()
    proc = None
    results = {}
    try:
        if sight:
            sight.write(out, results)
        log(f'kehai-run: {rid}: cloning the project')
        make_clone(s, clone, commit)
        set_product(clone, product)
        if export_dir is not None:
            export_dir.mkdir(parents=True, exist_ok=True)
            (export_dir / 'unity.log').write_text('')
            card.export_dir = str(export_dir.absolute())
            card.save(s)
        log_path = (export_dir or out) / ('review.md' if sight else 'unity.log')
        log(f'kehai-run: {rid}: running Unity ({job}), at most {timeout_min:g} min; {log_path}')
        limit = (time.time() if eval_timeout is not None else began) + timeout_min * 60
        code = 0
        for i, (destination, command) in enumerate(commands):
            if sight and time.time() >= limit:
                code, note = 124, f'timed out after {timeout_min:g} min'
                break
            destination.mkdir(exist_ok=True)
            unity_log = (export_dir or destination) / 'unity.log'
            if sight:
                name = sight.cases[i].name
                log(f'kehai-run: rendering {name}; log {unity_log}')
            proc = subprocess.Popen(command, start_new_session=True, env=env)
            while proc.poll() is None:
                if time.time() > limit:
                    os.killpg(proc.pid, signal.SIGKILL)
                    proc.wait()
                    note = f'timed out after {timeout_min:g} min'
                    break
                (out / 'progress').write_text(f'{job} {rid} {time.time() - began:.0f}s {progress_line(unity_log)}\n')
                time.sleep(1.0)
            code = proc.returncode if not note else 124
            if export_dir is not None and code < 0:
                code = 128 - code
            if sight:
                if code == 0:
                    try:
                        verify_sight_movie(destination, sight.cases[i], probe)
                        results[name] = None
                    except (OSError, ValueError, subprocess.TimeoutExpired) as err:
                        log(f'kehai-run: {name}: {err}')
                        (destination / 'verification.txt').write_text(str(err) + '\n')
                        results[name] = f'[Verification failed]({name}/verification.txt)'
                else:
                    results[name] = f'Render exited {code}'
                sight.write(out, results)
            if note:
                break
        if sight:
            verified = sum(result is None for result in results.values())
            failed_checks = sum(not case.passed for case in sight.cases)
            if not note:
                code = int(verified != len(sight.cases) or failed_checks > 0)
                note = f'{verified}/{len(sight.cases)} clips verified, {failed_checks} bot checks failed'
            log(f'kehai-run: review {out / "review.md"}')
        if code == 0 and job in ('trick-clip', 'sandbox-clip'):
            from tools.run.trick_clip import verify_movie
            verify_movie(out)
        state = 'completed' if code == 0 else 'failed'
    except Stopped as why:
        note = f'stopped ({why})'
        code, state = INTERRUPTED, 'interrupted'
        if proc is not None and proc.poll() is None:
            try:
                os.killpg(proc.pid, signal.SIGKILL)
            except OSError:
                pass
            proc.wait()
    except Exception as err:  # noqa: BLE001: any failure still cleans up
        note = f'{type(err).__name__}: {err}'
        code, state = 1, 'failed'
    finally:
        for sig, handler in old.items():
            signal.signal(sig, handler)
        # If preservation fails, leave the source data and card for recovery.
        export_error = copy_out(card)
        cleanup(s, card, log)
        if export_error:
            note = f'{note}. {export_error}' if note else export_error
            if code == 0:
                code, state = 1, 'failed'
    minutes = (time.time() - began) / 60
    record(s, card, state, exit=code, minutes=round(minutes, 1), note=note)
    summary = f'{job} on {commit}: {state}' + (f' ({note})' if note else '') + f', {minutes:.0f} min. Results: {export_dir or out}'
    log('kehai-run: ' + summary)
    notify(s, summary)
    return code


# ---- status and doctor ------------------------------------------------------------------------

def status(s: Settings, log=print) -> None:
    info = heavy.holder(s.lock)
    log('The heavy-work lock: ' + (f"held by pid {info.get('pid')} for {info.get('job')}" if info else 'free'))
    for p in sorted(s.cards.glob('*.json')) if s.cards.exists() else []:
        card = Card.load(p)
        state = 'running' if heavy.alive(card.pid, card.started or None) else 'DIED (doctor --clean cleans it)'
        prog = Path(card.out) / 'progress'
        log(f'{card.id} {card.job} {state}: ' + (prog.read_text().strip() if prog.exists() else ''))
    try:
        from tools.reports.register import latest
        last = sorted(latest(s.register), key=lambda e: e.get('time', ''))[-3:]
        for e in last:
            log(f"last: {e['id']} {e.get('kind')} {e['state']} {e.get('note', '')}".rstrip())
    except (OSError, ValueError, ImportError):
        pass


def doctor(s: Settings, clean: bool = False, log=print) -> int:
    """Leftovers: dead runs' cards (cleaned with --clean), and other verify folders (listed only)."""
    found = 0
    for p in sorted(s.cards.glob('*.json')) if s.cards.exists() else []:
        card = Card.load(p)
        if not heavy.alive(card.pid, card.started or None):
            found += 1
            log(f'dead run {card.id} ({card.job}): clone {card.clone}')
    if clean:
        try:
            if unity_open(s):
                log('kehai-run: close Unity before recovering interrupted runs.')
                return 3
            with heavy.held('recover interrupted runs', s.lock, log=log):
                janitor(s, log)
        except (heavy.Busy, OSError) as err:
            log(f'kehai-run: {err}')
            return 3
    for p in sorted(s.data_root.glob('Kehai-verify*')) if s.data_root.exists() else []:
        found += 1
        log(f'not made by kehai-run, left alone (ask before deleting): {p}')
    info = heavy.holder(s.lock)
    if info and not heavy.alive(int(info.get('pid') or 0), info.get('started')):
        found += 1
        log(f"a stale lock: {info.get('job')} (pid {info.get('pid')}); the next run takes it over")
    if not found:
        log('nothing left behind')
    return 0


def main(argv=None) -> int:
    p = argparse.ArgumentParser(prog='kehai-run', description=__doc__.split('\n\n')[0])
    sub = p.add_subparsers(dest='cmd', required=True)
    for name in ('tests', 'ablation', 'league', 'unity'):
        r = sub.add_parser(name)
        r.add_argument('--ref', default='HEAD', help='the commit to test (default HEAD)')
        r.add_argument('--timeout-min', type=float)
        r.add_argument('--dry-run', action='store_true', help='print the Unity command and stop')
        if name == 'tests':
            r.add_argument('--filter', help='a -testFilter for Unity')
        if name == 'league':
            r.add_argument('--league', type=Path, required=True, help='a league file in the selected commit')
        if name in ('league', 'ablation'):
            r.add_argument('--eval-timeout-min', help='EvalBatch timeout, with a five-minute runner grace period')
            r.add_argument('--export-dir', type=Path, help='write the live log and export eval and shift output here')
            r.add_argument('--clone-root', type=Path, help='parent directory for the runner-managed clone')
        r.add_argument('rest', nargs=argparse.REMAINDER, help='after --: passed to Unity')
    clip = sub.add_parser('trick-clip', help='film one trick from a retained run')
    clip.add_argument('report', type=Path, help='the trick check TSV in the run output')
    clip.add_argument('trick', help='the tactic ID in the report')
    clip.add_argument('--dry-run', action='store_true')
    room_clip = sub.add_parser('sandbox-clip', help='film a sight or pursuit recording from a retained run')
    room_clip.add_argument('recording', type=Path)
    room_clip.add_argument('--start', type=float, default=0)
    room_clip.add_argument('--end', type=float, default=12)
    room_clip.add_argument('--dry-run', action='store_true')
    sight_clips = sub.add_parser('sight-clips', help='film the eight cases in a retained sight report')
    sight_clips.add_argument('report', type=Path)
    sight_clips.add_argument('--timeout-min', type=float, default=30, help='total time limit (default 30)')
    sight_clips.add_argument('--dry-run', action='store_true')
    offline = sub.add_parser('offline', help='compile this worktree and run engine-independent checks')
    offline.add_argument('--unity-app', type=Path, default=Path(UNITY).parents[2])
    offline.add_argument('--references', type=Path, help='imported Unity project (read only)')
    sub.add_parser('status')
    d = sub.add_parser('doctor')
    d.add_argument('--clean', action='store_true')
    a = p.parse_args(argv)
    s = Settings.from_env()
    if a.cmd == 'sight-clips':
        from tools.run.sight_clips import read_review
        try:
            if not 0 < a.timeout_min < float('inf'):
                raise ValueError('--timeout-min must be positive and finite')
            sight = read_review(a.report)
            return run(s, a.cmd, [], sight=sight, timeout_min=a.timeout_min, dry_run=a.dry_run)
        except (OSError, ValueError, subprocess.CalledProcessError) as err:
            p.error(str(err))
    if a.cmd in ('trick-clip', 'sandbox-clip'):
        from tools.run.trick_clip import read_shot, sandbox_shot
        try:
            if a.cmd == 'sandbox-clip':
                commit, shot = sandbox_shot(a.recording, a.start, a.end)
            else:
                commit, shot = read_shot(a.report, a.trick)
        except (OSError, ValueError) as err:
            p.error(str(err))
        return run(s, a.cmd, shot, ref=commit, dry_run=a.dry_run)
    if a.cmd == 'offline':
        from tools.run.offline import check
        return check(Path(__file__).resolve().parents[2], a.unity_app, a.references or s.main)
    if a.cmd == 'status':
        status(s)
        return 0
    if a.cmd == 'doctor':
        return doctor(s, a.clean)
    rest = a.rest[1:] if a.rest[:1] == ['--'] else a.rest
    eval_timeout = getattr(a, 'eval_timeout_min', None)
    timeout = a.timeout_min
    if timeout is None:
        timeout = 90.0
        if eval_timeout is not None:
            try:
                minutes = float(eval_timeout)
            except ValueError:
                minutes = 0.0  # Match EvalBatch's treatment of an unparseable timeout.
            timeout = minutes + 5 if minutes > 0 else float('inf')
    clone_root = getattr(a, 'clone_root', None)
    return run(s, a.cmd, rest, ref=a.ref, timeout_min=timeout,
               test_filter=getattr(a, 'filter', None), dry_run=a.dry_run,
               league=getattr(a, 'league', None), eval_timeout=eval_timeout,
               export_dir=getattr(a, 'export_dir', None),
               clone_root=clone_root.absolute() if clone_root is not None else None)


if __name__ == '__main__':
    sys.exit(main())
