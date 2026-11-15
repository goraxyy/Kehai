"""Synthetic runner adapter for CLI plumbing tests. NEVER a game performance result.

Called only with a request marked fixture=true: request.json rows.jsonl.
"""
import json
from pathlib import Path
import sys

from .suite import definition
from tools.reports.common import read_json, atomic_write


def rows(request, delta=0):
    for c in request['careers']:
        for s in c['shifts']:
            for ledger in definition()['ledger_conditions']:
                # Deterministic toy metrics deliberately include a losing version when delta > 0.
                metrics = {'caught': int(c['seed'] % 4 == 0 and s['shift'] > 1), 'stuck': 0,
                           'fairness_violations': 0, 'panic_setpoint_rmse': .1 + delta + (c['seed'] % 5)*.01 + (.03 if ledger == 'wiped' else 0),
                           'bot_jobs_per_min': 2.0}
                yield {'model': request['model'], 'arm': request['arm'], 'definition_sha256': request['definition_sha256'],
                       'seed': c['seed'], 'player': c['player'], 'shift': s['shift'], 'layout': s['layout'],
                       'edition': c['edition'], 'ledger': ledger, 'metrics': metrics,
                       'trick_counts': {'fixture_a': 1, 'fixture_b': 1, 'fixture_c': 1},
                       'synthetic': True}


def main(argv=None):
    args = argv or sys.argv[1:]
    if len(args) != 2:
        raise SystemExit('expected request.json rows.jsonl')
    request = read_json(args[0])
    if not request.get('fixture'):
        raise SystemExit('refusing synthetic results for a real evaluation request; pass --fixture')
    if Path(args[1]).exists():
        raise SystemExit('result file already exists')
    atomic_write(args[1], ''.join(json.dumps(r)+'\n' for r in rows(request)))


if __name__ == '__main__':
    main()
