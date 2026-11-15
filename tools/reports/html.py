"""Self-contained HTML and SVG. All source text is escaped; no CDN or remote assets."""
from html import escape
import json


def e(value):
    return escape(str(value), quote=True)


def embedded(value):
    # JSON inside a script element must not be able to terminate that element.
    return json.dumps(value, allow_nan=False).replace('<', '\\u003c').replace('>', '\\u003e').replace('&', '\\u0026')


def page(title, body, script=''):
    return f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{e(title)}</title><style>
body{{font:16px system-ui,sans-serif;background:#101922;color:#e4edf4;max-width:1250px;margin:auto;padding:28px}}
a{{color:#89d9f8}} h1,h2,h3{{font-weight:550}} section,article{{background:#1b2a36;padding:18px;border-radius:8px;margin:18px 0}}
table{{border-collapse:collapse;width:100%;font-size:14px}}td,th{{text-align:left;padding:8px;border-bottom:1px solid #4d6270}}
pre{{white-space:pre-wrap;overflow-wrap:anywhere}}svg,canvas{{max-width:100%;background:#14212c}}small,.muted{{color:#b1c3cf}}
.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:18px}}input[type=range]{{width:100%}}
.bad{{color:#ffb4aa}}code{{overflow-wrap:anywhere}}details{{margin:12px 0}}
</style><h1>{e(title)}</h1><p class="muted">Private local report • unavailable means not measured • no external requests</p>{body}
{('<script>' + script + '</script>') if script else ''}</html>'''


def details(title, value):
    return f'<details><summary>{e(title)}</summary><pre>{e(json.dumps(value, indent=2, allow_nan=False))}</pre></details>'


def chart(title, points):
    """Categorical means with ±1 SE. Missing points stay on the x axis and break the line."""
    vals = [v + offset * (se or 0) for _, v, se in points if v is not None for offset in (-1, 1)]
    if not vals:
        return f'<article><h3>{e(title)}</h3><p>Unavailable — no observations.</p></article>'
    lo, hi = min(vals), max(vals)
    span = hi - lo or max(abs(hi)*.2, 1)
    lo, hi = lo - span*.15, hi + span*.15
    def y(v):
        return 185 - 150 * (v-lo)/(hi-lo)
    body, previous = '', None
    for i, (label, value, se) in enumerate(points):
        x = 70 + i * 420 / max(1, len(points)-1)
        body += f'<text x="{x}" y="220" fill="#e4edf4" font-size="10" text-anchor="middle">{e(label)}</text>'
        if value is None:
            body += f'<text x="{x}" y="100" fill="#ffb4aa" text-anchor="middle">missing</text>'
            previous = None
            continue
        if previous:
            body += f'<path d="M{previous[0]},{previous[1]} L{x},{y(value)}" stroke="#72c9c1"/>'
        if se is not None:
            body += f'<path d="M{x},{y(value-se)} V{y(value+se)} M{x-5},{y(value-se)} h10 M{x-5},{y(value+se)} h10" stroke="#eee"/>'
        body += f'<circle cx="{x}" cy="{y(value)}" r="4" fill="#72c9c1"><title>{e(label)}: {value:.6g}; SE {e(se)}</title></circle>'
        previous = (x, y(value))
    body += f'<text x="2" y="30" fill="#b1c3cf" font-size="11">{hi:.4g}</text><text x="2" y="185" fill="#b1c3cf" font-size="11">{lo:.4g}</text>'
    return f'<article><h3>{e(title)}</h3><svg role="img" aria-label="{e(title)}" viewBox="0 0 560 240">{body}</svg><small>Error bars: ±1 standard error. Missing error bar: SE unavailable.</small></article>'


def scoreboard(cards):
    # Never put different definitions, arms, or halves on the same comparison chart.
    partitions = {}
    for c in cards:
        key = (c['benchmark'], c['definition_sha256'], c['arm'], c['half'], bool(c.get('synthetic')))
        partitions.setdefault(key, []).append(c)
    if not cards:
        return '<p>Benchmark unavailable — no measured score files supplied. No historical scores invented.</p>'
    body = ''
    for (version, sha, arm, half, synthetic), cs in partitions.items():
        body += f'<section><h2>{e(version)} · {e(arm)} · {e(half)}</h2><code>{e(sha)}</code>'
        if synthetic:
            body += '<p class="bad">SYNTHETIC FIXTURE — validates tooling only; not measured game performance.</p>'
        for group in sorted({g for c in cs for g in c['groups']}):
            body += f'<h3>{e(group)}</h3><div class="grid">'
            for metric in sorted({m for c in cs for m in c['groups'].get(group, {})}):
                points = [(c['model'], c['groups'].get(group, {}).get(metric, {}).get('mean'), c['groups'].get(group, {}).get(metric, {}).get('se')) for c in cs]
                body += chart(metric, points)
            body += '</div>'
        for c in cs:
            body += details(f"{c['model']} — counts, missing data, gates and provenance", c)
        body += '</section>'
    return body
