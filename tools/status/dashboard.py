"""Render the five-section private summary, observed daily growth and missing sources."""
from tools.reports.html import chart, details, e, page, scoreboard


def cells(value, prefix=''):
    if isinstance(value, dict) and 'status' in value:
        status = value['status']
        if status == 'unavailable':
            return f'<article><h3>{e(prefix)}</h3><p class="bad">Unavailable: {e(value.get("reason", "no source"))}</p></article>'
        return '<article><h3>' + e(prefix) + '</h3>' + details('Recorded value and source', value) + '</article>'
    if isinstance(value, dict):
        return ''.join(cells(v, str(k)) for k, v in value.items())
    if isinstance(value, list):
        return ''.join(cells(v, prefix + f' {i+1}') for i, v in enumerate(value))
    return ''


def headline(snapshot):
    def val(item):
        return item.get('value') if item.get('status') == 'available' else None
    plans = val(snapshot['release']['plans'])
    tests = [val(x) for x in snapshot['builds']['tests']]
    register = val(snapshot['training']['register'])
    github = val(snapshot['builds']['github'])
    return {'plan_tasks_done': sum(p['done'] for p in plans) if plans is not None else None,
            'tests_passed': sum(t['passed'] for t in tests) if tests and all(t is not None for t in tests) else None,
            'completed_runs': register['states']['completed'] if register is not None else None,
            'open_prs': github['open_prs'] if github is not None else None}


def render(snapshot, history):
    body = f'<p>Collected {e(snapshot["collected_at"])}. Source timestamps appear with each value.</p>'
    for key, title in [('release', 'Release'), ('builds', 'Builds'), ('training', 'Training'), ('marketing', 'Marketing')]:
        body += f'<section><h2>{title}</h2><div class="grid">' + cells(snapshot[key]) + '</div>'
        if key == 'builds':
            gh = snapshot[key]['github']
            if gh['status'] == 'available':
                links = gh['value']['pr']['rows'] + gh['value'].get('bugs', []) + gh['value'].get('training_issues', [])
                seen = set()
                for pr in links:
                    url = pr['url']
                    if url.startswith('https://github.com/') and url not in seen:
                        seen.add(url)
                        body += f'<p><a href="{e(url)}" rel="noreferrer">#{pr["number"]} {e(pr["title"])}</a> · {e(pr["state"])}</p>'
        if key == 'training':
            r = snapshot[key]['register']
            if r['status'] == 'available' and r['value']['latest']:
                last = r['value']['latest']
                body += f'<h3>Latest run: {e(last["id"])}</h3><p>{e(last["state"])} · {e(last.get("computer", "unknown computer"))} · {e(last.get("start"))}</p>'
            cards = [v['value'] for v in snapshot[key]['benchmark'] if v['status'] == 'available']
            body += scoreboard(cards)
        body += '</section>'
    body += '<section><h2>Growth · last 30 days</h2>'
    days = sorted({r['date']: r for r in history}.values(), key=lambda r: r['date'])
    body += f'<p>{len(days)} observed day(s). Unobserved dates are not filled with zero or synthetic history. Charts use current supplied sources; source changes can change counts.</p>'
    body += '<div class="grid">'
    for metric in headline(snapshot):
        body += chart(metric, [(d['date'], headline(d).get(metric), None) for d in days])
    body += '</div>'
    if len(days) >= 2:
        a, b = headline(days[0]), headline(days[-1])
        changes = {k: b[k]-a[k] if a[k] is not None and b[k] is not None else None for k in a}
        body += details('Observed change over available days (plain arithmetic, no model-generated claims)', changes)
    else:
        body += '<p>Trend unavailable until a second observed day is collected.</p>'
    body += '</section><section><h2>Integration availability</h2>' + cells(snapshot['integration']) + '</section>'
    return page('Kehai · private status', body)
