"""Freeze schedules, run an explicit adapter, score results, and render a private scoreboard."""
from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import uuid
import sys

from .suite import definition, schedule
from .analyze import score, compare, validate
from tools.reports.common import DEFAULT_HOME, digest, now, private_path, read_json, read_lines, source, write_json, git
from tools.reports.register import append, locked


def execute(a):
    plan = schedule(a.purpose, a.half)
    if a.arm not in definition()['arms']:
        raise ValueError('unknown benchmark arm')
    command = a.runner[1:] if a.runner[:1] == ['--'] else a.runner
    if not command:
        command = [sys.executable, '-m', 'tools.benchmark.env_adapter', '--port', str(a.env_port)]
    home = private_path(a.home)
    home.mkdir(parents=True, exist_ok=True, mode=0o700)
    provenance = source(a.model_file) if a.model_file else {'commit': git(a.repo, 'rev-parse', 'HEAD'), 'dirty': bool(git(a.repo, 'status', '--porcelain'))}
    if provenance.get('status') == 'unavailable':
        raise ValueError('model file missing')
    if a.fixture and a.half == 'hidden':
        raise ValueError('synthetic fixtures cannot consume hidden evaluations')
    request = dict(plan, model=a.model, arm=a.arm, model_provenance=provenance, fixture=a.fixture)
    ident = uuid.uuid4().hex
    run_dir = home / 'benchmark' / ident
    run_dir.mkdir(parents=True, mode=0o700)
    request_path, results = run_dir / 'request.json', run_dir / 'rows.jsonl'
    write_json(request_path, request)
    if a.half == 'hidden':
        claim = home / 'hidden-claims.json'
        with locked(claim):
            claims = read_json(claim) if claim.exists() else {}
            # Model labels identify releases, not attempts. Failures also consume the hidden half.
            key = digest([plan['definition_sha256'], a.model, a.arm])
            if key in claims:
                raise ValueError(f'hidden half already reserved/consumed: {claims[key]}')
            claims[key] = {'run': ident, 'start': now(), 'request': str(request_path)}
            write_json(claim, claims)
    event = {'schema': 1, 'id': ident, 'kind': 'benchmark', 'state': 'running', 'start': now(), 'end': None,
             'command': command, 'request': source(request_path), 'seeds': [c['seed'] for c in plan['careers']],
             'computer': __import__('platform').node(), 'data_snapshot': plan['definition_sha256'],
             'model': a.model, 'arm': a.arm, 'half': a.half, 'result_card': None}
    append(home / 'runs.jsonl', event)
    code = 1
    try:
        code = subprocess.call([*command, str(request_path), str(results)], cwd=a.repo)
        if code == 0 and not results.is_file():
            raise ValueError('adapter wrote no result file')
        if code == 0:
            expected, actual = validate(read_lines(results), request)
            if len(actual) != len(expected):
                raise ValueError(f'adapter returned an incomplete schedule: {len(actual)}/{len(expected)} rows; raw rows preserved for offline scoring')
        if code == 0 and a.purpose == 'evaluate':
            card = score(request, read_lines(results))
            card['provenance'] = {'request': source(request_path), 'rows': source(results)}
            write_json(run_dir / 'score.json', card)
            event['result_card'] = source(run_dir / 'score.json')
        elif code == 0 and not results.is_file():
            raise ValueError('adapter wrote no result file')
    except KeyboardInterrupt:
        code, event['error'] = 130, 'interrupted; hidden reservation retained'
    except (ValueError, OSError, KeyError) as e:
        code, event['error'] = 1, str(e)
    event.update(state='completed' if code == 0 else 'failed', end=now(), exit_code=code)
    append(home / 'runs.jsonl', event)
    print(run_dir)
    return code


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest='action', required=True)
    p = sub.add_parser('schedule')
    p.add_argument('--purpose', choices=['train', 'tune', 'evaluate'], default='evaluate')
    p.add_argument('--half', choices=['practice', 'hidden'], default='practice')
    p.add_argument('--out', required=True)
    p = sub.add_parser('run')
    p.add_argument('--purpose', choices=['train', 'tune', 'evaluate'], default='evaluate')
    p.add_argument('--half', choices=['practice', 'hidden'], default='practice')
    p.add_argument('--model', required=True)
    p.add_argument('--model-file')
    p.add_argument('--arm', default='planner')
    p.add_argument('--env-port', type=int, default=5555, help='local eval server for the bundled default adapter')
    p.add_argument('--fixture', action='store_true', help='synthetic plumbing test; never a measured game score')
    p.add_argument('--home', default=str(DEFAULT_HOME))
    p.add_argument('--repo', default='.')
    p.add_argument('runner', nargs=argparse.REMAINDER)
    p = sub.add_parser('score')
    p.add_argument('request')
    p.add_argument('rows')
    p.add_argument('--out', required=True)
    p = sub.add_parser('compare')
    p.add_argument('first', help='first run directory')
    p.add_argument('second', help='second run directory')
    p.add_argument('--out', required=True)
    p = sub.add_parser('board')
    p.add_argument('scores', nargs='+')
    p.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    try:
        if a.action == 'schedule':
            write_json(a.out, schedule(a.purpose, a.half))
        elif a.action == 'run':
            return execute(a)
        elif a.action == 'score':
            card = score(read_json(a.request), read_lines(a.rows))
            card['provenance'] = {'request': source(a.request), 'rows': source(a.rows)}
            write_json(a.out, card)
        elif a.action == 'compare':
            da, db = Path(a.first), Path(a.second)
            write_json(a.out, compare(read_json(da / 'request.json'), read_lines(da / 'rows.jsonl'),
                                      read_json(db / 'request.json'), read_lines(db / 'rows.jsonl')))
        else:
            from tools.reports.html import scoreboard, page
            from tools.reports.common import atomic_write
            atomic_write(a.out, page('Karen benchmark', scoreboard([read_json(p) for p in a.scores])))
        return 0
    except (ValueError, OSError, KeyError) as e:
        ap.error(str(e))


if __name__ == '__main__':
    raise SystemExit(main())
