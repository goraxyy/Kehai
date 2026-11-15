"""Strict result contract and stratified paired reports for the frozen suite."""
from collections import defaultdict, Counter

from .suite import definition, schedule, heldout
from .stats import estimate, number, survival_summary, paired_survival
from tools.reports.common import digest, read_lines, source


def identity(r):
    return (r['seed'], r['player'], r['shift'], r['layout'], r['edition'], r['ledger'])


def validate(rows, request):
    expected = {}
    for c in request['careers']:
        for s in c['shifts']:
            for ledger in definition()['ledger_conditions']:
                expected[(c['seed'], c['player'], s['shift'], s['layout'], c['edition'], ledger)] = c
    seen = set()
    for r in rows:
        if bool(r.get('synthetic')) != bool(request.get('fixture')):
            raise ValueError('synthetic fixture results require an explicit fixture request')
        k = identity(r)
        if k not in expected or k in seen:
            raise ValueError('unexpected/duplicate result identity: ' + str(k))
        if r.get('model') != request['model'] or r.get('arm') != request['arm']:
            raise ValueError('result model/arm does not match request')
        if r.get('definition_sha256') != request['definition_sha256']:
            raise ValueError('result benchmark definition digest missing/mismatched')
        if not isinstance(r.get('metrics'), dict):
            raise ValueError('metrics must be an explicit object; missing fields may be null')
        for key, v in r['metrics'].items():
            if v is not None and not number(v):
                raise ValueError(f'nonfinite/nonnumeric metric {key}; use null for unavailable')
            if v is not None and (key in {'caught', 'stuck'} and v not in (0, 1)):
                raise ValueError('caught/stuck metrics must be explicit 0 or 1')
            if v is not None and (key.startswith('true_mass') or key in {'top_trick_share', 'calibration_brier', 'bot_human_accuracy'}) and not 0 <= v <= 1:
                raise ValueError(f'{key} must be a probability in [0,1]')
            if v is not None and key not in {'adaptation_rmse_delta'} and v < 0:
                raise ValueError(f'negative sentinel for {key}; use null')
        if r['metrics'].get('bot_human_accuracy') is not None and not all(r.get('human_reference', {}).get(k) for k in ('consented', 'player_split', 'source_sha256')):
            raise ValueError('bot/human accuracy needs consented human reference and player-split provenance')
        counts = r.get('trick_counts')
        if counts is not None and (not isinstance(counts, dict) or any(type(v) is not int or v < 0 for v in counts.values())):
            raise ValueError('trick_counts must be a mapping of tactic IDs to nonnegative integer counts')
        seen.add(k)
    return expected, {identity(r): r for r in rows}


def groups(request):
    out = defaultdict(list)
    for c in request['careers']:
        layout = 'heldout-layout' if any(s['layout'] in {'rings', 'split'} for s in c['shifts']) else 'seen-layout'
        out[c['player'] + ' / ' + layout].append(c)
    return out


def career_values(careers, indexed, metric, ledger='retained'):
    values = {}
    missing = 0
    for c in careers:
        shifts = c['shifts'] if metric in {'stuck', 'bot_jobs_per_min'} else c['shifts'][1:]
        xs = []
        picks = Counter()
        for s in shifts:
            row = indexed.get((c['seed'], c['player'], s['shift'], s['layout'], c['edition'], ledger), {})
            value = row.get('metrics', {}).get(metric)
            if metric == 'top_trick_share':
                counts = row.get('trick_counts')
                value = 0 if counts is not None else None
                if counts is not None:
                    picks.update(counts)
            if metric == 'adaptation_rmse_delta':
                a = row.get('metrics', {}).get('panic_setpoint_rmse')
                b = indexed.get((c['seed'], c['player'], s['shift'], s['layout'], c['edition'], 'wiped'), {}).get('metrics', {}).get('panic_setpoint_rmse')
                value = a - b if number(a) and number(b) else None
            if number(value):
                xs.append(value)
            else:
                missing += 1
        # No selectively incomplete careers enter the mean or comparison.
        if len(xs) == len(shifts):
            if metric == 'top_trick_share':
                if sum(picks.values()) == 0:
                    missing += 1
                    continue
                values[(c['seed'], c['player'])] = max(picks.values()) / sum(picks.values())
            else:
                values[(c['seed'], c['player'])] = sum(xs) / len(xs)
    return values, missing


def survival_values(careers, indexed, arm):
    result = {}
    if arm != 'Vital':
        return result
    for c in careers:
        obs = []
        for s in c['shifts'][1:]:
            r = indexed.get((c['seed'], c['player'], s['shift'], s['layout'], c['edition'], 'retained'), {})
            event = r.get('survival', {})
            if r.get('pacing') is False and number(event.get('seconds')) and type(event.get('caught')) is bool:
                obs.append((event['seconds'], event['caught']))
        if len(obs) == 3:
            result[(c['seed'], c['player'])] = obs
    return result


def score(request, rows):
    if request['definition_sha256'] != digest(definition()):
        raise ValueError('definition changed; create a new benchmark version')
    canonical = schedule(request['purpose'], request['half'])
    if request['careers'] != canonical['careers'] or request['purpose'] != 'evaluate':
        raise ValueError('scoreboard accepts only the full frozen evaluation schedule')
    expected, idx = validate(rows, request)
    result = {'schema': 1, 'benchmark': request['benchmark'], 'definition_sha256': request['definition_sha256'],
              'model': request['model'], 'arm': request['arm'], 'half': request['half'], 'synthetic': bool(request.get('fixture')),
              'model_provenance': request.get('model_provenance'), 'groups': {},
              'missing_rows': len(expected) - len(idx), 'expected_rows': len(expected),
              'note': 'Means use complete careers only. Error bars are SE, not confidence intervals. Missing SE means fewer than two careers. Negative results retained.'}
    for name, careers in groups(request).items():
        metrics = {}
        for metric in definition()['metrics']:
            if metric == 'catch_rmst_s':
                metrics[metric] = survival_summary(survival_values(careers, idx, request['arm']))
            else:
                values, missing = career_values(careers, idx, metric)
                metrics[metric] = dict(estimate(values.values()), missing_observations=missing, expected_careers=len(careers))
            if metric == 'top_trick_share':
                picks = Counter()
                missing_picks = False
                seeds = {c['seed'] for c in careers}
                selected = [r for r in rows if r['seed'] in seeds and r['player'] == careers[0]['player'] and r['shift'] > 1 and r['ledger'] == 'retained']
                for row in selected:
                    if row.get('trick_counts') is None:
                        missing_picks = True
                    else:
                        picks.update(row['trick_counts'])
                pooled = max(picks.values())/sum(picks.values()) if sum(picks.values()) else None
                metrics[metric]['pooled_top_share'] = pooled
                metrics[metric]['gate'] = 'unavailable' if missing_picks or len(selected) != 3*len(careers) or pooled is None else ('FAIL' if pooled > .4 else 'pass')
            if metric == 'fairness_violations':
                violations = [r['metrics'].get(metric) for r in rows if r['player'] == careers[0]['player'] and
                              any(r['seed'] == c['seed'] for c in careers)]
                metrics[metric]['gate'] = 'FAIL' if any(number(x) and x > 0 for x in violations) else (
                    'pass' if all(number(x) for x in violations) and metrics[metric].get('missing_observations') == 0 and result['missing_rows'] == 0 else 'unavailable')
        result['groups'][name] = metrics
    return result


def compare(request_a, rows_a, request_b, rows_b):
    if any(request_a[k] != request_b[k] for k in ('definition_sha256', 'benchmark', 'half', 'careers', 'arm')) or bool(request_a.get('fixture')) != bool(request_b.get('fixture')):
        raise ValueError('unpaired definitions, halves, arms or schedules; comparison refused')
    score(request_a, rows_a)
    score(request_b, rows_b)
    _, a = validate(rows_a, request_a)
    _, b = validate(rows_b, request_b)
    out = {}
    for name, careers in groups(request_a).items():
        out[name] = {}
        for metric in definition()['metrics']:
            if metric == 'catch_rmst_s':
                out[name][metric] = paired_survival(survival_values(careers, a, request_a['arm']), survival_values(careers, b, request_b['arm']))
                continue
            av, _ = career_values(careers, a, metric)
            bv, _ = career_values(careers, b, metric)
            keys = av.keys() & bv.keys()
            out[name][metric] = dict(estimate(bv[k] - av[k] for k in keys), expected_careers=len(careers), direction='second minus first')
    return out
