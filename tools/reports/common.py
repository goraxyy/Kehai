"""Shared private-output and JSON helpers. No credentials or environment dumps."""
from __future__ import annotations

import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

DEFAULT_HOME = Path.home() / 'TokenLimit' / 'kehai-private'


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), allow_nan=False).encode()).hexdigest()


def private_path(path):
    """Refuse generated private data anywhere inside a git checkout, even ignored dirs."""
    p = Path(path).expanduser().resolve()
    for parent in (p, *p.parents):
        if (parent / '.git').exists():
            raise ValueError(f'private output must be outside git checkouts: {p}')
    return p


def write_json(path, value):
    atomic_write(path, json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + '\n')


def atomic_write(path, text):
    p = private_path(path)
    p.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    fd, tmp = tempfile.mkstemp(dir=p.parent, prefix='.' + p.name)
    try:
        with os.fdopen(fd, 'w', encoding='utf-8') as f:
            f.write(text)
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, p)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def read_lines(path):
    p = Path(path)
    if not p.exists():
        return []
    rows = []
    for n, line in enumerate(p.read_text(encoding='utf-8').splitlines(), 1):
        if line.strip():
            try:
                rows.append(json.loads(line))
            except ValueError as e:
                raise ValueError(f'{p}:{n}: invalid JSON (not silently skipped)') from e
    return rows


def source(path):
    p = Path(path).expanduser().resolve()
    if not p.is_file():
        return {'path': str(p), 'status': 'unavailable', 'reason': 'file missing'}
    return {'path': str(p), 'sha256': hashlib.sha256(p.read_bytes()).hexdigest(),
            'bytes': p.stat().st_size, 'modified': dt.datetime.fromtimestamp(p.stat().st_mtime, dt.timezone.utc).isoformat()}


def git(repo, *args):
    result = subprocess.run(['git', '-C', str(repo), *args], capture_output=True, text=True, timeout=15)
    if result.returncode:
        raise ValueError(result.stderr.strip() or 'git command failed')
    return result.stdout.strip()
