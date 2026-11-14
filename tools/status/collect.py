"""Read-only filesystem/git collectors and explicitly opted-in GitHub CLI collection."""
from __future__ import annotations

import datetime as dt
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

from tools.reports.common import git, now, read_json, source
from tools.reports.register import latest


def available(value, provenance):
    return {'status': 'available', 'value': value, 'source': provenance, 'collected_at': now()}


def unavailable(reason, provenance=None):
    return {'status': 'unavailable', 'value': None, 'reason': reason, 'source': provenance, 'collected_at': now()}


def guarded(fn, provenance=None):
    try:
        return fn()
    except (OSError, ValueError, KeyError, ET.ParseError, subprocess.SubprocessError) as ex:
        return unavailable(str(ex), provenance)


def checkboxes(path):
    text = Path(path).read_text(encoding='utf-8')
    marks = re.findall(r'^\s*- \[([ xX])\]', text, re.MULTILINE)
    return {'done': sum(m.lower() == 'x' for m in marks), 'total': len(marks)}


def plans(repo):
    files = sorted((repo / 'docs/you/plans').rglob('*.md'))
    if not files:
        return unavailable('no plan documents', str(repo / 'docs/you/plans'))
    return available([{**checkboxes(p), 'path': str(p.relative_to(repo)), 'provenance': source(p)} for p in files], 'docs/you/plans/**/*.md')


def release(repo):
    p = repo / 'docs/you/RELEASE_PLAN.md'
    return available(checkboxes(p), source(p))


def scripts(repo):
    names = git(repo, 'ls-files', '*.cs').splitlines()
    counts = {}
    for name in names:
        p = repo / name
        if not p.is_file():
            continue
        # Tracked code only, never assets or credentials. Paths group the source, not game quality.
        area = next((part for part in p.parts if part in {'AI', 'Core', 'Eval', 'Level', 'Look'}), 'other')
        g = counts.setdefault(area, {'files': 0, 'lines': 0})
        g['files'] += 1
        g['lines'] += len(p.read_text(encoding='utf-8').splitlines())
    return available(counts, {'command': 'git ls-files *.cs', 'commit': git(repo, 'rev-parse', 'HEAD'), 'working_tree_counts': True})


def tests(path):
    root = ET.parse(path).getroot()
    if root.tag == 'test-run':  # NUnit
        required = ('total', 'passed', 'failed', 'skipped')
        if not all(root.get(k) is not None for k in required):
            raise ValueError('NUnit summary counts missing')
        value = {k: int(root.get(k)) for k in required}
        value['format'] = 'NUnit'
    elif root.tag in {'testsuite', 'testsuites'}:  # pytest/JUnit
        cases = list(root.iter('testcase'))
        if not cases and root.get('tests') is None:
            raise ValueError('JUnit has no test cases or summary')
        if cases:
            failed = sum(c.find('failure') is not None or c.find('error') is not None for c in cases)
            skipped = sum(c.find('skipped') is not None for c in cases)
            total = len(cases)
        else:
            total, failed, skipped = int(root.get('tests')), int(root.get('failures', 0))+int(root.get('errors', 0)), int(root.get('skipped', 0))
        value = {'total': total, 'failed': failed, 'skipped': skipped, 'passed': total-failed-skipped, 'format': 'JUnit'}
    else:
        raise ValueError('unsupported test result XML')
    if min(value[k] for k in ('total', 'passed', 'failed', 'skipped')) < 0:
        raise ValueError('invalid test counts')
    if value['passed'] + value['failed'] + value['skipped'] != value['total']:
        raise ValueError('inconsistent test counts')
    return available(value, source(path))


def builds(paths):
    if not paths:
        return unavailable('no build.txt paths supplied; no build success inferred from code')
    return available([{'source': source(p), 'build_txt': Path(p).read_text(encoding='utf-8')} for p in paths], 'explicit build.txt inputs')


def runs(path):
    if not Path(path).exists():
        return unavailable('run register not created yet', str(path))
    rows = latest(path)
    return available({'runs': rows, 'latest': max(rows, key=lambda r: r.get('start', '')) if rows else None,
                      'states': {s: sum(r['state'] == s for r in rows) for s in ('running', 'completed', 'failed', 'interrupted')},
                      'note': 'Running is last recorded state, not proof the process is alive.'}, source(path))


def heavy_lock(path):
    p = Path(path)
    if not p.exists():
        return available({'present': False}, str(p))
    files = {f.name: f.read_text(encoding='utf-8')[:500] for f in p.iterdir() if f.name in {'pid', 'holder', 'owner.json'} and f.is_file()}
    return available({'present': True, 'holder_files': files, 'note': 'Lock contents only; liveness not inferred.'}, str(p))


def github(repo_slug):
    if not repo_slug:
        return unavailable('GitHub collection disabled; optional --github OWNER/REPO uses existing gh authentication')
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repo_slug):
        raise ValueError('GitHub repository must be OWNER/REPO')
    import json
    result = {}
    for what, fields in [('pr', 'number,title,url,state,mergedAt'), ('issue', 'number,title,url,state,labels,closedAt')]:
        cmd = ['gh', what, 'list', '--repo', repo_slug, '--state', 'all', '--limit', '1000', '--json', fields]
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=30)
        if p.returncode:
            # gh stderr may include authentication hints. Do not copy credential material.
            return unavailable('gh unavailable, unauthenticated or request failed; filesystem collectors still work', repo_slug)
        rows = json.loads(p.stdout)
        result[what] = {'rows': rows, 'possibly_truncated': len(rows) == 1000}
    result['open_prs'] = sum(r['state'] == 'OPEN' for r in result['pr']['rows'])
    since = (dt.datetime.now(dt.timezone.utc)-dt.timedelta(days=7)).isoformat()
    result['merged_last_7_days'] = sum(bool(r.get('mergedAt')) and r['mergedAt'] >= since for r in result['pr']['rows'])
    result['bugs'] = [r for r in result['issue']['rows'] if any(l['name'] == 'bug' for l in r.get('labels', []))]
    result['training_issues'] = [r for r in result['issue']['rows'] if any(l['name'] == 'training' for l in r.get('labels', []))]
    return available(result, {'repository': repo_slug, 'transport': 'gh CLI', 'limit_per_type': 1000})


def marketing(directory):
    if directory is None:
        return unavailable('no local marketing records directory supplied')
    p = Path(directory)
    if not p.is_dir():
        return unavailable('local marketing records directory missing', str(p))
    # Count explicit record directories only. Do not read .env, API tokens or unpublished content.
    value = {}
    for folder in ('drafts', 'approvals', 'plans'):
        d = p / folder
        value[folder] = {'files': sum(1 for f in d.rglob('*.json') if f.is_file())} if d.is_dir() else {'status': 'unavailable', 'reason': 'directory absent'}
    return available(value, str(p))


def collect(repo, register, score_paths=(), test_paths=(), build_paths=(), github_repo=None, marketing_dir=None, lock=None):
    repo = Path(repo).resolve()
    return {'schema': 1, 'collected_at': now(), 'date': dt.datetime.now(dt.timezone.utc).date().isoformat(),
            'release': {'milestones': guarded(lambda: release(repo)), 'plans': guarded(lambda: plans(repo)),
                        'scripts': guarded(lambda: scripts(repo)), 'plot': unavailable('no plot source configured'),
                        'assets': unavailable('private asset repository not configured')},
            'builds': {'git': guarded(lambda: available({'commit': git(repo, 'rev-parse', 'HEAD'), 'dirty': bool(git(repo, 'status', '--porcelain')),
                                                       'recent_commits': git(repo, 'log', '-8', '--format=%h %s').splitlines()}, str(repo))),
                       'github': guarded(lambda: github(github_repo)),
                       'tests': [guarded(lambda p=p: tests(p), str(p)) for p in test_paths] or [unavailable('no test result paths supplied')],
                       'versions': guarded(lambda: builds(build_paths))},
            'training': {'register': guarded(lambda: runs(register)),
                         'benchmark': [guarded(lambda p=p: available(read_json(p), source(p)), str(p)) for p in score_paths] or [unavailable('no measured benchmark scores supplied')],
                         'heavy_lock': guarded(lambda: heavy_lock(lock)) if lock else unavailable('heavy-work lock path not configured'),
                         'playtests': unavailable('cloud playtest database not configured')},
            'marketing': {'local_records': guarded(lambda: marketing(marketing_dir)),
                          **{k: unavailable(reason) for k, reason in {
                              'youtube': 'YouTube channel/API account unavailable', 'buffer': 'Buffer channels/account unavailable',
                              'wishlists': 'Steam page/Steamworks reporting unavailable'}.items()}},
            'integration': {'claude_ai': unavailable('no verified external private-page write/auth API; this is a private local file'),
                            'deployment': unavailable('no network hosting, deployment or scheduler installed')}}
