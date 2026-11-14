"""Render a private run report, including legacy belief-fit cards and optional replay.

python3 -m tools.reports.report --card path/to/weights.part.json --out /private/tmp/run.html
"""
from __future__ import annotations

import argparse
from pathlib import Path

from .common import DEFAULT_HOME, atomic_write, read_json, read_lines, source
from .html import chart, details, e, page, scoreboard
from .register import audit, latest


def build(run=None, manifest=None, card=None, curves=None, scores=None, comparison=None):
    warnings = audit(manifest or {}, (run or {}).get('kind', 'test'))
    if not card:
        warnings.append('result/model card unavailable')
    if not curves:
        warnings.append('training curves unavailable; no values inferred from checkpoints')
    body = '<section><h2>Availability and isolation checks</h2>'
    body += ''.join(f'<p class="bad">{e(w)}</p>' for w in warnings) or '<p>No missing items detected in supplied metadata.</p>'
    body += '<p>These checks describe supplied metadata; they do not prove unlisted data were excluded.</p></section>'
    body += details('Run provenance', run or {'status': 'unavailable', 'reason': 'no registered run supplied'})
    if manifest:
        counts = {}
        for c in manifest.get('careers', []):
            p = c.get('player', 'unknown')
            counts[p] = counts.get(p, 0)+len(c.get('shifts', []))
        body += details('Data counts per player (declared manifest shifts)', counts)
    if card:
        body += '<section><h2>Recorded result / model card</h2>' + details('Original card — includes negative results', card) + '</section>'
    if card and isinstance(card.get('recorded'), dict):
        legacy = card['recorded']
        scores = scores or []
        if legacy.get('senses', {}).get('scores'):
            body += chart('Historical senses · validation log true-cell probability', [(k, v, None) for k, v in legacy['senses']['scores'].items()])
        motion = legacy.get('motion', {})
        if any(isinstance(v, dict) and 'val' in v for v in motion.values()):
            body += chart('Historical motion · validation log true-cell probability', [(k, v['val'], None) for k, v in motion.items() if isinstance(v, dict) and 'val' in v])
            body += '<p>Historical aggregate validation scores only. Career-level uncertainty, held-out benchmark results and training curves are unavailable in this file. Higher log probability is better; losing models remain visible.</p>'
    if curves:
        from tools.benchmark.stats import number
        if any('step' not in r or not number(r['step']) or any(v is not None and not number(v) for k, v in r.items() if k != 'step') for r in curves):
            raise ValueError('curve rows need numeric step and finite metric values or null')
        metrics = sorted({k for r in curves for k in r if k != 'step'})
        body += '<section><h2>Recorded curves</h2><div class="grid">'
        for metric in metrics:
            body += chart(metric, [(str(r.get('step', '?')), r.get(metric), None) for r in curves])
        body += '</div><p>Curve points are recorded values, no uncertainty inferred.</p></section>'
    body += '<section><h2>Change against current model</h2>'
    body += details('Paired comparison (second minus first)', comparison) if comparison else '<p>Unavailable — no paired current-model results supplied.</p>'
    body += '</section><h2>Held-out results and scoreboard</h2>' + scoreboard(scores or [])
    body += '<section><h2>Replay and network</h2><p>Use the replay command with recorded shift JSON. Neural inputs and saliency require an exact matching decision trace and supported model; they cannot be recovered from positions.</p></section>'
    return page('Training run report', body)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--register', default=str(DEFAULT_HOME / 'runs.jsonl'))
    ap.add_argument('--run')
    ap.add_argument('--manifest')
    ap.add_argument('--card', help='JSON/part.json or historical Markdown card; rendered without claiming a new run')
    ap.add_argument('--curves', help='JSONL: step and metric values')
    ap.add_argument('--score', action='append', default=[])
    ap.add_argument('--comparison')
    ap.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    try:
        run = next((r for r in latest(a.register) if r['id'] == a.run), None) if a.run else None
        if a.run and run is None:
            raise ValueError('run id not found')
        card = None
        if a.card:
            card = {'provenance': source(a.card), 'recorded': read_json(a.card) if a.card.endswith('.json') else Path(a.card).read_text()}
        atomic_write(a.out, build(run, read_json(a.manifest) if a.manifest else None, card,
                                 read_lines(a.curves) if a.curves else None,
                                 [read_json(p) for p in a.score], read_json(a.comparison) if a.comparison else None))
        print(a.out)
        return 0
    except (ValueError, OSError, KeyError) as ex:
        ap.error(str(ex))


if __name__ == '__main__':
    raise SystemExit(main())
