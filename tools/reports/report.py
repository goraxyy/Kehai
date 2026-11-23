"""Render a private run report, including legacy belief-fit cards and optional replay.

python3 -m tools.reports.report --card path/to/weights.part.json --out /private/tmp/run.html
"""
from __future__ import annotations

import argparse
from pathlib import Path

from .common import DEFAULT_HOME, atomic_write, read_json, read_lines, source
from .html import chart, details, e, page, scoreboard
from .register import audit, latest


def retained_input(reference):
    if not isinstance(reference, dict) or not reference.get('path') or not reference.get('sha256'):
        raise ValueError('retained benchmark file has no provenance')
    path = Path(reference['path'])
    if source(path).get('sha256') != reference['sha256']:
        raise ValueError(f'missing or changed benchmark file: {path.name}')
    return path


def benchmark_history(events):
    """One completed score per release and suite, paired in recorded completion order."""
    from tools.benchmark.analyze import compare

    groups, warnings = {}, []
    completed = [row for row in events if row.get('kind') == 'benchmark' and row.get('state') == 'completed'
                 and row.get('result_card')]
    for row in sorted(completed, key=lambda row: (row.get('end') or '', row['id'])):
        try:
            card = read_json(retained_input(row['result_card']))
            if any(card[key] != row[key] for key in ('model', 'arm', 'half')):
                raise ValueError('score and run register identify different evaluations')
            key = (card['benchmark'], card['definition_sha256'], card['arm'], card['half'], bool(card.get('synthetic')))
            groups.setdefault(key, {}).setdefault(card['model'], card)
        except (KeyError, TypeError, ValueError, OSError) as error:
            warnings.append(f"{row['id']}: {error}")

    def inputs(card):
        provenance = card['provenance']
        request = read_json(retained_input(provenance['request']))
        if any(request[key] != card[key] for key in ('benchmark', 'definition_sha256', 'model', 'arm', 'half')):
            raise ValueError('score and retained request identify different evaluations')
        return request, read_lines(retained_input(provenance['rows']))

    cards, pairs = [], []
    for group in groups.values():
        versions = list(group.values())
        cards.extend(versions)
        for first, second in zip(versions, versions[1:]):
            label = f"{first['model']} → {second['model']} · {first['arm']} · {first['half']}"
            if first.get('synthetic'):
                label += ' · synthetic fixture'
            try:
                pairs.append((label, compare(*inputs(first), *inputs(second))))
            except (KeyError, TypeError, ValueError, OSError) as error:
                warnings.append(f'{label}: {error}')
    return cards, pairs, warnings


def build(run=None, manifest=None, card=None, curves=None, scores=None, comparison=None, history=None):
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
    if history is not None:
        cards, pairs, unavailable = history
        body += '<h2>Karen benchmark over versions</h2><p>First completed score per release and suite, in completion order.</p>'
        if cards:
            body += scoreboard(cards)
        else:
            body += '<p>No benchmark scores could be retrieved from the register.</p>'
        if pairs:
            body += '<h3>Paired changes and standard errors (later minus earlier)</h3>'
            body += ''.join(details(label, result) for label, result in pairs)
        body += ''.join(f'<p class="bad">{e(issue)}</p>' for issue in unavailable)
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
    ap.add_argument('--benchmark-history', action='store_true', help='show registered scores and paired changes across Karen versions')
    ap.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    try:
        events = latest(a.register) if a.run or a.benchmark_history else []
        run = next((r for r in events if r['id'] == a.run), None) if a.run else None
        if a.run and run is None:
            raise ValueError('run id not found')
        card = None
        if a.card:
            card = {'provenance': source(a.card), 'recorded': read_json(a.card) if a.card.endswith('.json') else Path(a.card).read_text()}
        atomic_write(a.out, build(run, read_json(a.manifest) if a.manifest else None, card,
                                 read_lines(a.curves) if a.curves else None,
                                 [read_json(p) for p in a.score], read_json(a.comparison) if a.comparison else None,
                                 benchmark_history(events) if a.benchmark_history else None))
        print(a.out)
        return 0
    except (ValueError, OSError, KeyError) as ex:
        ap.error(str(ex))


if __name__ == '__main__':
    raise SystemExit(main())
