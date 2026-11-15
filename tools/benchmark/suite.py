"""Versioned schedule and fail-closed split checks shared by all new entry points."""
from __future__ import annotations

from pathlib import Path
import hashlib
from tools.reports.common import digest, read_json

DEFINITION = Path(__file__).with_name('v1.json')


def definition():
    return read_json(DEFINITION)


def heldout(career):
    player = str(career.get('player', '')).lower()
    traits = career.get('traits') or {}
    careful = traits.get('caution', -1) >= .75 and traits.get('pace', 1) <= .3
    return ('cautious' in player or 'careful' in player or 'corner' in player or careful or
            any(str(s.get('layout', '')).lower() in {'rings', 'split'} for s in career.get('shifts', [])))


def allowed(career, purpose):
    if purpose not in {'train', 'tune', 'evaluate'}:
        raise ValueError('unknown purpose')
    if purpose == 'evaluate':
        return
    seed = career.get('seed')
    lo, hi = (10000, 20000) if purpose == 'train' else (20000, 30000)
    if type(seed) is not int or not lo <= seed < hi:
        raise ValueError(f'held-out/unknown seed for {purpose}: {seed}')
    player = career.get('player')
    if player not in definition()['players']:
        raise ValueError(f'unknown player metadata: {player}')
    if player.startswith('trained') and not {'caution', 'pace'} <= (career.get('traits') or {}).keys():
        raise ValueError('missing persona traits')
    for trait, value in (career.get('traits') or {}).items():
        if type(value) not in (int, float) or not 0 <= value <= 1:
            raise ValueError(f'invalid persona trait {trait}: {value}')
    shifts = career.get('shifts', [])
    if len(shifts) != 4 or [s.get('shift') for s in shifts] != [1, 2, 3, 4]:
        raise ValueError('four explicit shifts required')
    if any(s.get('layout') not in definition()['layouts'] for s in shifts):
        raise ValueError('missing/unknown layout')
    if heldout(career):
        raise ValueError(f'held-out player/persona/layout in {purpose}: {player}')


def schedule(purpose='evaluate', half='practice'):
    d = definition()
    if half not in {'practice', 'hidden'}:
        raise ValueError('unknown half')
    if purpose != 'evaluate' and half == 'hidden':
        raise ValueError('hidden/test seeds inaccessible to training or tuning')
    out = []
    base = 30000 if half == 'hidden' else 20000
    if purpose == 'train':
        base = 10000
    for index in range(50):
        stratum = d['strata'][index % len(d['strata'])]
        traits = dict(d['players'][stratum['player']])
        for key, (lo, hi) in d.get('persona_ranges', {}).get(stratum['player'], {}).items():
            word = f"{base + index}:{stratum['player']}:{key}".encode()
            unit = int.from_bytes(hashlib.sha256(word).digest()[:8], 'big') / 2**64
            traits[key] = lo + (hi - lo)*unit
        career = {'career': f'{half}-{index:02}', 'seed': base + index, 'player': stratum['player'],
                  'traits': traits, 'edition': 'gameplay',
                  'shifts': [{'shift': i, 'layout': layout, 'karen': i > 1, 'record_habits': True}
                             for i, layout in enumerate(['today', 'today', *stratum['layouts']], 1)]}
        if purpose != 'evaluate' and heldout(career):
            continue
        allowed(career, purpose)
        out.append(career)
    return {'schema': 1, 'benchmark': d['version'], 'definition_sha256': digest(d), 'purpose': purpose,
            'half': half, 'careers': out, 'expected_shifts': 4 * len(out)}
