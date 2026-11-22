"""Resolve a trick marker and its recording after kehai-run has removed the clone."""
from __future__ import annotations

import csv
import json
import math
import re
from pathlib import Path, PureWindowsPath


def read_shot(report: Path, tactic: str) -> tuple[str, list[str]]:
    report = report.resolve()
    if report.parent.name != 'kehai_eval':
        raise ValueError('choose the TSV inside the retained run’s kehai_eval folder')
    out = report.parent.parent
    commit = recorded_commit(out)
    with report.open(newline='', encoding='utf-8-sig') as source:
        rows = [row for row in csv.DictReader(source, delimiter='\t') if row.get('id') == tactic]
    if len(rows) != 1:
        raise ValueError(f'expected one row for {tactic}, found {len(rows)}')
    row = rows[0]
    try:
        start, landed = float(row['started']), float(row['landed'])
    except (KeyError, ValueError, TypeError) as err:
        raise ValueError(f'{tactic} has invalid trick markers') from err
    if not (math.isfinite(start) and math.isfinite(landed) and 0 <= start <= landed):
        raise ValueError(f'{tactic} needs a recorded start and landing')
    # The TSV can contain an absolute path from the deleted clone's product data.
    name = PureWindowsPath(row.get('krec') or '').name
    replay = out / 'shift_records' / name
    if replay.suffix != '.krec' or not replay.is_file() or not replay.stat().st_size:
        raise ValueError(f'missing retained recording for {tactic}: {replay}')
    subject = 'you' if tactic in ('blackout', 'fog', 'phantom_chime', 'camera_break', 'understudy') else 'karen'
    begin = max(0.0, landed - 6)
    return commit, ['-krec', str(replay), '-from', f'{begin:.2f}', '-to', f'{begin + 14:.2f}',
                           '-shot', 'chase', '-subject', subject, '-layers', 'none',
                           '-size', '1280x720', '-fps', '30']


def recorded_commit(out: Path) -> str:
    events = [json.loads(line) for line in (out / 'run.jsonl').read_text().splitlines() if line]
    commits = {event.get('commit') for event in events if isinstance(event, dict)}
    if len(commits) != 1 or not re.fullmatch('[0-9a-f]{40}', next(iter(commits)) or ''):
        raise ValueError('run.jsonl needs one recorded commit')
    return commits.pop()


def sandbox_shot(replay: Path, start: float, end: float) -> tuple[str, list[str]]:
    replay = replay.resolve()
    folder = replay.parent.parent
    if folder.name != 'sandbox_checks' or replay.suffix != '.krec' or not replay.is_file():
        raise ValueError('choose a retained .krec under sandbox_checks/<check run>/')
    if not (math.isfinite(start) and math.isfinite(end) and 0 <= start < end):
        raise ValueError('choose a finite interval with 0 <= start < end')
    return recorded_commit(folder.parent), ['-krec', str(replay), '-from', str(start), '-to', str(end),
                                            '-shot', 'chase', '-layers', 'none', '-size', '1280x720', '-fps', '30']


def verify_movie(out: Path) -> dict:
    movie = out / 'clip.mp4'
    if not movie.is_file() or not movie.stat().st_size:
        raise ValueError('the renderer produced no clip.mp4')
    sidecar = json.loads((out / 'clip.json').read_text())
    if not isinstance(sidecar, dict) or 'audio' not in sidecar or sidecar['audio'] is not None:
        raise ValueError('the renderer did not confirm a silent clip')
    return sidecar
