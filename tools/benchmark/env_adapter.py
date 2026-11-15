"""Bundled adapter for the local Kehai eval TCP server.

Uses tools.eval.kehai_env.KehaiEnv, preserving raw server evidence. Requires the benchmark
career capability; never relabels the external nearest-job baseline as scripted/trained bots.
Does not launch Unity. Start an evaluation build explicitly with -kehai-env 5555 first.
"""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path

from tools.eval.kehai_env import KehaiEnv, KehaiError
from tools.reports.common import private_path, read_json, source, write_json
from .suite import definition, schedule
from .stats import number
from .analyze import identity, validate


class CapabilityError(ValueError):
    pass


def preflight(env, request):
    try:
        caps = env.request({'cmd': 'benchmark_capabilities'})
    except KehaiError as ex:
        raise CapabilityError('eval server lacks benchmark career capability; existing reset/step cannot run the scheduled SimulatedPlayers. See docs/agents/integration/0.5.0-reports.md') from ex
    if caps.get('benchmark_protocol') != 1 or caps.get('four_shift_careers') is not True:
        raise CapabilityError('server must advertise benchmark_protocol=1 and four_shift_careers=true')
    for field, needed in [('arms', {request['arm']}), ('players', {c['player'] for c in request['careers']}),
                          ('layouts', {s['layout'] for c in request['careers'] for s in c['shifts']})]:
        missing = needed - set(caps.get(field, []))
        if missing:
            raise CapabilityError(f'build capability unavailable: {field} {sorted(missing)}; no substitute players/models allowed')
    if caps.get('build_edition') not in {'training', 'testing'} or caps.get('career_semantics') != 'gameplay':
        raise CapabilityError('requires an evaluation-enabled Training/Testing build with validated Gameplay career semantics; retail Gameplay must not enable evaluation')
    provenance = request.get('model_provenance') or {}
    if provenance.get('commit') and caps.get('build_commit') != provenance['commit']:
        raise CapabilityError('build commit missing or different from requested source provenance')
    if provenance.get('sha256') and caps.get('model_sha256') != provenance['sha256']:
        raise CapabilityError('runtime model hash missing or different from requested model file')
    return caps


def normalize(raw, request, career, ledger):
    executed = raw.get('executed') or {}
    shifted = next((s for s in career['shifts'] if s['shift'] == executed.get('shift')), None)
    if shifted is None:
        raise ValueError('server returned an unrequested shift')
    wanted = {'seed': career['seed'], 'player': career['player'], 'shift': shifted['shift'],
              'layout': shifted['layout'], 'edition': career['edition'], 'ledger': ledger, 'arm': request['arm'],
              'traits': career['traits'], 'karen_present': shifted['karen']}
    for key, value in wanted.items():
        if key == 'traits':
            actual = executed.get(key)
            # Unity stores persona traits as float32; accept serialization roundoff only.
            if isinstance(actual, dict) and actual.keys() == value.keys() and all(
                number(actual[k]) and math.isclose(actual[k], v, rel_tol=0, abs_tol=1e-6)
                for k, v in value.items()
            ):
                continue
        if executed.get(key) != value:
            raise ValueError(f'actual execution mismatch/unavailable: {key}; refusing to label this as the requested career')
    m = raw.get('metrics')
    if not isinstance(m, dict):
        raise ValueError('actual EpisodeMetrics object missing')
    if m.get('seed') != career['seed'] or m.get('shift') != shifted['shift']:
        raise ValueError('EpisodeMetrics seed/shift differs from execution')
    measured = {k: None for k in definition()['metrics'] if k not in {'catch_rmst_s', 'adaptation_rmse_delta'}}
    # These counters have explicit fields in EpisodeMetrics. Absent fields remain null.
    if number(m.get('karen_catches')) and m['karen_catches'] >= 0:
        measured['caught'] = int(m['karen_catches'] > 0)
    if type(m.get('timed_out')) is bool:
        measured['stuck'] = int(m['timed_out'])
    if number(m.get('karen_fairness_violations')) and m['karen_fairness_violations'] >= 0:
        measured['fairness_violations'] = m['karen_fairness_violations']
    samples = raw.get('metric_samples') or {}
    sample_count = samples.get('panic_setpoint_rmse')
    if number(m.get('panic_setpoint_rmse')) and number(sample_count) and sample_count > 0:
        measured['panic_setpoint_rmse'] = m['panic_setpoint_rmse']
    # EpisodeMetrics' per-tactic uses are actual counts. Aggregate counters/top-tactic prose
    # do not recover a complete distribution; only declared complete counts are accepted.
    counts = m.get('trick_counts')
    if counts is None and raw.get('trick_counts_complete') is True:
        counts = {k[5:]: v for k, v in m.items() if k.startswith('uses_')}
    job_keys = ('spills_mopped', 'shelves_restocked', 'bins_bagged', 'bags_disposed', 'customers_served', 'directions_given')
    if all(number(m.get(k)) for k in job_keys) and number(m.get('sim_seconds')) and m['sim_seconds'] > 0:
        measured['bot_jobs_per_min'] = sum(m[k] for k in job_keys)*60/m['sim_seconds']
    row = {**{k: wanted[k] for k in ('seed', 'player', 'shift', 'layout', 'edition', 'ledger')},
           'model': request['model'], 'arm': request['arm'], 'definition_sha256': request['definition_sha256'],
           'metrics': measured, 'trick_counts': counts, 'metric_sources': {
               'caught': 'EpisodeMetrics.karen_catches > 0', 'stuck': 'EpisodeMetrics.timed_out (overtime cap reached)',
               'bot_jobs_per_min': 'sum of six recorded completed job counters / sim_seconds * 60'},
           'unavailable': ['true-cell probabilities/calibration/classifier require their own recorded evidence',
                           'neural decision telemetry cannot be inferred from EpisodeMetrics']}
    # Never turn -1 (no catch) into a catch time. Uncaught follow-up is right censored.
    if raw.get('pacing') is False and number(m.get('time_to_catch_s')) and m['time_to_catch_s'] >= 0 and measured['caught'] == 1:
        row.update(pacing=False, survival={'seconds': m['time_to_catch_s'], 'caught': True})
    elif raw.get('pacing') is False and measured['caught'] == 0 and number(m.get('sim_seconds')):
        row.update(pacing=False, survival={'seconds': m['sim_seconds'], 'caught': False})
    return row


def execute(request, output, env):
    if request.get('fixture'):
        raise ValueError('real eval adapter refuses synthetic requests')
    canonical = schedule(request['purpose'], request['half'])
    if request['purpose'] != 'evaluate' or request['careers'] != canonical['careers'] or request['definition_sha256'] != canonical['definition_sha256']:
        raise ValueError('bundled adapter accepts only frozen nonlearning evaluation schedules')
    output = private_path(output)
    evidence = output.with_name(output.stem + '-evidence')
    evidence.mkdir(mode=0o700)
    caps = preflight(env, request)
    write_json(evidence / 'capabilities.json', caps)
    fd = os.open(output, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    with os.fdopen(fd, 'w', encoding='utf-8') as out:
        for career in request['careers']:
            for ledger in definition()['ledger_conditions']:
                message = {'cmd': 'benchmark_career', 'benchmark_protocol': 1, 'definition_sha256': request['definition_sha256'],
                           'model': request['model'], 'arm': request['arm'], 'half': request['half'], 'purpose': 'evaluate',
                           'career': career, 'career_semantics': 'gameplay', 'ledger': ledger, 'horizon_seconds': definition()['horizon_seconds']}
                reply = env.request(message)
                stem = f"{career['seed']}-{ledger}"
                write_json(evidence / (stem + '.json'), reply)
                actual = reply.get('rows', [])
                if len(actual) != 4:
                    raise ValueError('server did not return all four actual career shifts; raw response preserved')
                normalized = [normalize(r, request, career, ledger) for r in actual]
                if len({r['shift'] for r in normalized}) != 4:
                    raise ValueError('server repeated a shift; raw response preserved')
                for row, raw in zip(normalized, actual):
                    row['build_edition'] = caps['build_edition']
                    row['raw_source'] = source(evidence / (stem + '.json'))
                    if isinstance(raw.get('recording'), dict):
                        path = evidence / f"{stem}-shift{row['shift']}.json"
                        # Preserve raw data verbatim; do not invent absent seed/layout/frame data.
                        write_json(path, raw['recording'])
                        row['recording_source'] = source(path)
                    if isinstance(raw.get('decision_trace'), dict):
                        path = evidence / f"{stem}-shift{row['shift']}-decisions.json"
                        write_json(path, raw['decision_trace'])
                        row['decision_source'] = source(path)
                    validate([row], request)
                    out.write(json.dumps(row, allow_nan=False)+'\n')
                out.flush()
                os.fsync(out.fileno())


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--port', type=int, default=5555)
    ap.add_argument('--timeout', type=float, default=600)
    ap.add_argument('request')
    ap.add_argument('rows')
    a = ap.parse_args(argv)
    try:
        with KehaiEnv(port=a.port, timeout=a.timeout) as env:
            execute(read_json(a.request), a.rows, env)
        return 0
    except (ValueError, OSError, KeyError, KehaiError) as ex:
        ap.exit(1, f'benchmark adapter: {ex}\n')


if __name__ == '__main__':
    raise SystemExit(main())
