"""Career-cluster uncertainty and survival summaries; missing is never zero."""
import math
import statistics
from collections import defaultdict


def number(x):
    return type(x) in (int, float) and math.isfinite(x)


def estimate(values):
    xs = list(values)
    return {'mean': statistics.mean(xs) if xs else None,
            'se': statistics.stdev(xs) / math.sqrt(len(xs)) if len(xs) > 1 else None,
            'careers': len(xs)}


def rmst(observations, horizon=150):
    """KM restricted mean. None when follow-up cannot identify the requested horizon."""
    if not observations:
        return None
    for t, caught in observations:
        if not number(t) or t < 0 or type(caught) is not bool:
            raise ValueError('survival needs finite nonnegative duration and explicit boolean caught')
    risk, survival, previous, area = len(observations), 1., 0., 0.
    for t in sorted({min(t, horizon) for t, _ in observations}):
        area += survival * (t - previous)
        deaths = sum(1 for ti, e in observations if ti == t and e and ti <= horizon)
        leaving = sum(1 for ti, _ in observations if min(ti, horizon) == t)
        if risk:
            survival *= 1 - deaths / risk
        risk -= leaving
        previous = t
    if previous < horizon and survival > 0:
        return None
    return area + survival * (horizon - previous)


def survival_summary(by_career, horizon=150):
    all_obs = [o for obs in by_career.values() for o in obs]
    value = rmst(all_obs, horizon)
    deleted = [rmst([o for k, obs in by_career.items() if k != skip for o in obs], horizon) for skip in by_career]
    se = None
    if len(deleted) > 1 and all(v is not None for v in deleted):
        avg = statistics.mean(deleted)
        se = math.sqrt((len(deleted) - 1) / len(deleted) * sum((x - avg) ** 2 for x in deleted))
    return {'mean': value, 'se': se, 'careers': len(by_career), 'censored': sum(not e for _, e in all_obs),
            'horizon_s': horizon, 'method': 'Kaplan-Meier RMST; career delete-one jackknife SE',
            'missing': 'insufficient follow-up' if value is None else None}


def paired_survival(a, b, horizon=150):
    keys = a.keys() & b.keys()
    def diff(ks):
        av = rmst([o for k in ks for o in a[k]], horizon)
        bv = rmst([o for k in ks for o in b[k]], horizon)
        return None if av is None or bv is None else bv - av
    value = diff(keys)
    deleted = [diff(keys - {k}) for k in keys]
    se = None
    if len(deleted) > 1 and all(v is not None for v in deleted):
        avg = statistics.mean(deleted)
        se = math.sqrt((len(deleted)-1)/len(deleted)*sum((v-avg)**2 for v in deleted))
    return {'mean': value, 'se': se, 'careers': len(keys), 'method': 'paired career jackknife RMST difference'}
