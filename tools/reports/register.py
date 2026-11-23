"""Append-only run events, portable command wrapper and offline Windows-register import.

python3 -m tools.reports.register run --kind fit --manifest data.json --input config.yaml -- python3 fit.py
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import errno
import os
from pathlib import Path
import platform
import re
import stat
import subprocess
import time
import uuid

from .common import DEFAULT_HOME, digest, git, now, private_path, read_json, read_lines, source


def _set_lock(fd, acquire):
    if os.name == 'nt':
        import msvcrt

        os.lseek(fd, 0, os.SEEK_SET)
        msvcrt.locking(fd, msvcrt.LK_NBLCK if acquire else msvcrt.LK_UNLCK, 1)
    else:
        import fcntl

        fcntl.flock(fd, (fcntl.LOCK_EX | fcntl.LOCK_NB) if acquire else fcntl.LOCK_UN)


def _pid_is_dead(pid):
    if os.name == 'nt':
        import _winapi

        try:
            handle = _winapi.OpenProcess(_winapi.SYNCHRONIZE, False, pid)
        except OSError as err:
            return err.winerror == 87  # ERROR_INVALID_PARAMETER, no such nonzero PID
        try:
            return _winapi.WaitForSingleObject(handle, 0) == _winapi.WAIT_OBJECT_0
        finally:
            _winapi.CloseHandle(handle)
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return True
    except OSError:
        pass
    return False


def _open_shared_on_windows(path, flags):
    import _winapi
    import msvcrt

    # Allow the old writer to delete its marker while we read it.
    share_read_write_delete = 0x7
    handle = _winapi.CreateFile(path, _winapi.GENERIC_READ, share_read_write_delete,
                                0, _winapi.OPEN_EXISTING, 0, 0)
    try:
        return msvcrt.open_osfhandle(handle, flags | os.O_NOINHERIT)
    except OSError:
        _winapi.CloseHandle(handle)
        raise


def _entry_version(info):
    return info.st_dev, info.st_ino, info.st_ctime_ns, info.st_mtime_ns, info.st_size


def _recover_dead_lock(lock):
    """Remove a legacy lock only while holding the guard and after proving its owner died."""
    holder = lock / 'holder'
    try:
        directory = lock.lstat()
        if not directory.st_ino or not stat.S_ISDIR(directory.st_mode) or getattr(directory, 'st_reparse_tag', 0):
            return False
        marker = holder.lstat()
        if not marker.st_ino or not stat.S_ISREG(marker.st_mode) or marker.st_size > 1024:
            return False
        opener = _open_shared_on_windows if os.name == 'nt' else None
        with open(holder, opener=opener) as f:
            # The newline distinguishes a complete PID from an interrupted write.
            match = re.fullmatch(r'(.+) pid=([1-9][0-9]{0,9})\n', f.read(1024))
        if not match or match[1] != platform.node():
            return False
        pid = int(match[2])
        if pid > (0xffffffff if os.name == 'nt' else 0x7fffffff) or not _pid_is_dead(pid):
            return False
        # An older writer could have released and another acquired during the PID check.
        if (list(lock.iterdir()) != [holder]
                or _entry_version(lock.lstat()) != _entry_version(directory)
                or _entry_version(holder.lstat()) != _entry_version(marker)):
            return False
        holder.unlink()
        lock.rmdir()
    except (OSError, UnicodeError):
        return False
    return True


def _wait_for_lock(path, end):
    remaining = end - time.monotonic()
    if remaining <= 0:
        raise ValueError(f'lock busy: {path}')
    time.sleep(min(.025, remaining))


@contextmanager
def _legacy_lock(lock, end):
    while True:
        try:
            lock.mkdir(mode=0o700)
            break
        except FileExistsError:
            if not _recover_dead_lock(lock):
                _wait_for_lock(lock, end)
    holder = lock / 'holder'
    try:
        holder.write_text(f'{platform.node()} pid={os.getpid()}\n')
        yield
    finally:
        holder.unlink(missing_ok=True)
        lock.rmdir()


@contextmanager
def locked(path, timeout=10):
    """Serialize with current and legacy writers, recovering proven dead holders."""
    p = private_path(path)
    p.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    lock = p.with_name(p.name + '.lock')
    guard = lock.with_name(lock.name + '.guard')
    fd = os.open(guard, os.O_CREAT | os.O_RDWR, 0o600)
    try:
        end = time.monotonic() + timeout
        while True:
            try:
                _set_lock(fd, True)
                break
            except OSError as err:
                if err.errno not in {errno.EACCES, errno.EAGAIN, errno.EWOULDBLOCK}:
                    raise
                _wait_for_lock(guard, end)
        try:
            with _legacy_lock(lock, end):
                yield
        finally:
            _set_lock(fd, False)
    finally:
        # Keep the same file for every writer, including those already waiting on it.
        os.close(fd)


def append(path, event):
    if not event.get('id') or event.get('state') not in {'running', 'completed', 'failed', 'interrupted', 'imported'}:
        raise ValueError('run event needs id and a valid state')
    import json
    encoded = json.dumps(event, sort_keys=True, allow_nan=False) + '\n'
    with locked(path):
        old = read_lines(path)
        same = [r for r in old if r['id'] == event['id'] and r['state'] == event['state']]
        if same:
            if same[0] == event:
                return False
            raise ValueError('conflicting event for this run id/state')
        previous = [r for r in old if r['id'] == event['id']]
        terminal = {'completed', 'failed', 'interrupted', 'imported'}
        if previous and (previous[-1]['state'] in terminal or event['state'] == 'running'):
            raise ValueError('run already ended or has a running event; cannot rewrite its outcome')
        fd = os.open(private_path(path), os.O_APPEND | os.O_CREAT | os.O_WRONLY, 0o600)
        with os.fdopen(fd, 'a', encoding='utf-8') as f:
            f.write(encoded)
            f.flush()
            os.fsync(f.fileno())
    return True


def latest(path):
    result = {}
    for event in read_lines(path):
        result[event['id']] = event
    return list(result.values())


def audit(manifest, purpose):
    """Conservative Karen-side data gate; unknown metadata cannot prove isolation."""
    from tools.benchmark.suite import heldout, allowed
    warnings = []
    rows = manifest.get('careers', [])
    if not rows:
        warnings.append('missing learning-record/data manifest careers')
    counts = {}
    for row in rows:
        name = row.get('player', 'unknown')
        counts[name] = counts.get(name, 0) + len(row.get('shifts', []))
        if purpose in {'record', 'fit', 'train', 'tune'}:
            try:
                allowed(row, 'train' if purpose in {'record', 'fit', 'train'} else 'tune')
            except ValueError as e:
                warnings.append(str(e))
        elif heldout(row):
            pass  # Evaluation is allowed; held-out groups are labelled in the report.
    warnings.extend(f'too few shifts for {p}: {n} < 4' for p, n in counts.items() if n < 4)
    if not manifest.get('learning_records'):
        warnings.append('learning records unavailable')
    return warnings


def run(args):
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        raise ValueError('supply a command after --')
    manifest = read_json(args.manifest) if args.manifest else {}
    warnings = audit(manifest, args.kind)
    if args.kind in {'record', 'fit', 'train', 'tune'} and warnings:
        raise ValueError('data gate refused run: ' + '; '.join(warnings))
    try:
        commit, dirty = git(args.repo, 'rev-parse', 'HEAD'), bool(git(args.repo, 'status', '--porcelain'))
    except ValueError:
        commit, dirty = None, None
    inputs = [source(p) for p in args.input]
    if args.manifest:
        inputs.append(source(args.manifest))
    for path in manifest.get('learning_records', []):
        inputs.append(source(Path(args.manifest).resolve().parent / path))
    if any(p.get('status') == 'unavailable' for p in inputs):
        raise ValueError('a declared input/learning record is missing')
    event = {'schema': 1, 'id': args.id or uuid.uuid4().hex, 'kind': args.kind,
             'computer': platform.node(), 'platform': platform.platform(), 'commit': commit,
             'dirty': dirty, 'command': command, 'cwd': str(Path(args.repo).resolve()),
             'data_snapshot': digest(sorted((p.get('sha256'), p.get('bytes')) for p in inputs)), 'inputs': inputs, 'seeds': sorted({c['seed'] for c in manifest.get('careers', []) if 'seed' in c}),
             'start': now(), 'end': None, 'result_card': None, 'warnings': warnings, 'state': 'running'}
    append(args.register, event)
    try:
        code = subprocess.call(command, cwd=args.repo)
        event = dict(event, state='completed' if code == 0 else 'failed', exit_code=code, end=now(),
                     result_card=source(args.card) if args.card else None)
    except KeyboardInterrupt:
        code, event = 130, dict(event, state='interrupted', exit_code=130, end=now())
    except OSError as e:
        code, event = 127, dict(event, state='failed', exit_code=127, end=now(), error=str(e))
    append(args.register, event)
    print(event['id'], event['state'])
    return code


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--register', default=str(DEFAULT_HOME / 'runs.jsonl'))
    sub = ap.add_subparsers(dest='action', required=True)
    r = sub.add_parser('run')
    r.add_argument('--kind', choices=['record', 'fit', 'train', 'tune', 'test', 'benchmark'], required=True)
    r.add_argument('--id')
    r.add_argument('--repo', default='.')
    r.add_argument('--manifest')
    r.add_argument('--input', action='append', default=[])
    r.add_argument('--card')
    r.add_argument('command', nargs=argparse.REMAINDER)
    i = sub.add_parser('import')
    i.add_argument('file')
    sub.add_parser('list')
    a = ap.parse_args(argv)
    try:
        if a.action == 'run':
            return run(a)
        if a.action == 'import':
            for row in read_lines(a.file):
                append(a.register, row)
        else:
            import json
            print(json.dumps(latest(a.register), indent=2, allow_nan=False))
        return 0
    except (ValueError, OSError) as e:
        ap.error(str(e))


if __name__ == '__main__':
    raise SystemExit(main())
