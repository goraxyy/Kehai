import base64
import contextlib
import copy
import datetime as dt
import http.client
import io
from pathlib import Path
import plistlib
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

from tools.reports.common import atomic_write, read_json, read_lines, write_json
from tools.reports.register import append
from tools.status import collect as collectors
from tools.status.cli import main, remember
from tools.status.dashboard import headline, render
from tools.status.schedule import main as schedule_main, template
from tools.status.serve import make_server, password

ROOT = Path(__file__).resolve().parents[3]


def snapshot():
    with patch('tools.status.collect.git', return_value=''):
        return collectors.collect('/not-a-repository', '/nonexistent-register')


class Collectors(unittest.TestCase):
    def test_nunit_and_junit_counts(self):
        samples = [('<test-run total="4" passed="2" failed="1" skipped="1"/>', 'NUnit'),
                   ('<testsuites><testsuite><testcase/><testcase/><testcase><failure/></testcase>'
                    '<testcase><skipped/></testcase></testsuite></testsuites>', 'JUnit'),
                   ('<testsuite tests="4" failures="1" skipped="1"/>', 'JUnit')]
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'results.xml'
            for xml, fmt in samples:
                p.write_text(xml)
                result = collectors.tests(p)
                self.assertEqual(result['value'], dict(total=4, passed=2, failed=1, skipped=1, format=fmt))
                self.assertIn('sha256', result['source'])

    def test_malformed_missing_and_inconsistent_test_evidence_unavailable(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'results.xml'
            for xml in ['broken', '<test-run total="4"/>', '<test-run total="4" passed="4" failed="1" skipped="0"/>',
                        '<testsuite tests="1" failures="2"/>', '<other/>', '<testsuites/>']:
                p.write_text(xml)
                self.assertEqual(collectors.guarded(lambda: collectors.tests(p))['status'], 'unavailable')
            self.assertEqual(collectors.guarded(lambda: collectors.tests(p.with_name('missing')))['status'], 'unavailable')

    def test_checkbox_counts_build_text_and_local_marketing_have_sources(self):
        with tempfile.TemporaryDirectory() as tmp:
            repo = Path(tmp); plans = repo/'docs/you/plans'; plans.mkdir(parents=True)
            (plans/'one.md').write_text('- [x] done\n  - [X] nested\n- [ ] next\ntext [x] ignored\n')
            self.assertEqual(collectors.plans(repo)['value'][0]['done'], 2)
            self.assertEqual(collectors.plans(repo)['value'][0]['total'], 3)
            p = repo/'build.txt'; p.write_text('edition=testing\n')
            self.assertEqual(collectors.builds([p])['value'][0]['build_txt'], p.read_text())
            self.assertEqual(collectors.builds([])['status'], 'unavailable')
            (repo/'drafts').mkdir(); (repo/'drafts/a.json').write_text('{}')
            (repo/'.env').write_text('not-to-be-read')
            value = collectors.marketing(repo)['value']
            self.assertEqual(value['drafts']['files'], 1)
            self.assertEqual(value['approvals']['status'], 'unavailable')
            self.assertNotIn('not-to-be-read', str(value))

    def test_run_register_and_lock_contents_never_claim_liveness(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'runs.jsonl'; append(p, {'id': 'one', 'state': 'running', 'start': '2026-01-01'})
            value = collectors.runs(p)['value']
            self.assertEqual(value['states']['running'], 1)
            self.assertIn('not proof', value['note'])
            lock = Path(tmp)/'heavy.lock'
            self.assertFalse(collectors.heavy_lock(lock)['value']['present'])
            lock.mkdir(); (lock/'pid').write_text('123')
            self.assertEqual(collectors.heavy_lock(lock)['value']['holder_files'], {'pid': '123'})

    def test_no_account_collection_remains_optional_and_unknown(self):
        with patch('tools.status.collect.subprocess.run') as run:
            self.assertEqual(collectors.github(None)['status'], 'unavailable'); run.assert_not_called()
        s = snapshot()
        for k in ('youtube', 'buffer', 'wishlists'):
            self.assertEqual(s['marketing'][k]['status'], 'unavailable')
        self.assertIsNone(headline(s)['tests_passed'])
        self.assertEqual(s['integration']['claude_ai']['status'], 'unavailable')

    def test_github_failure_does_not_copy_stderr_or_need_credentials(self):
        with patch('tools.status.collect.subprocess.run', return_value=subprocess.CompletedProcess([], 1, '', 'private credential')):
            self.assertNotIn('private credential', str(collectors.github('owner/repo')))
        with self.assertRaises(ValueError): collectors.github('--anything')
        with patch('tools.status.collect.subprocess.run', side_effect=FileNotFoundError('gh')):
            self.assertEqual(collectors.guarded(lambda: collectors.github('owner/repo'))['status'], 'unavailable')

    def test_github_links_include_bugs_and_escape_titles(self):
        pr = {'number': 1, 'title': '<script>pr</script>', 'url': 'https://github.com/o/r/pull/1', 'state': 'OPEN'}
        bug = {'number': 2, 'title': 'bug', 'url': 'https://github.com/o/r/issues/2', 'state': 'OPEN'}
        evil = dict(bug, number=3, url='javascript:alert(1)')
        s = snapshot()
        s['builds']['github'] = collectors.available({'pr': {'rows': [pr]}, 'bugs': [bug, evil],
                                                      'training_issues': [bug], 'open_prs': 1}, 'mock')
        html = render(s, [s])
        self.assertIn('href="https://github.com/o/r/issues/2"', html)
        self.assertEqual(html.count('href="https://github.com/o/r/issues/2"'), 1)
        self.assertNotIn('href="javascript:', html)
        self.assertNotIn('<script>pr</script>', html)


class Persistence(unittest.TestCase):
    def test_history_uses_only_latest_observation_per_day_and_rolls_30_days(self):
        with tempfile.TemporaryDirectory() as tmp:
            s = snapshot(); base = dt.date(2026, 1, 1)
            for day in range(35):
                s = dict(s, date=(base+dt.timedelta(days=day)).isoformat(), collected_at=str(day))
                remember(tmp, s)
            rows = remember(tmp, dict(s, collected_at='replacement'))
            self.assertEqual(len(rows), 30)
            self.assertEqual(rows[0]['date'], '2026-01-06')
            self.assertEqual(rows[-1]['collected_at'], 'replacement')
            self.assertEqual(len(read_lines(Path(tmp)/'history.jsonl')), 30)
            html = render(s, rows)
            self.assertIn('30 observed day(s)', html)

    def test_no_fabricated_growth_for_one_day(self):
        s = snapshot(); html = render(s, [s])
        self.assertIn('Trend unavailable until a second observed day', html)
        for title in ('Release', 'Builds', 'Training', 'Marketing', 'Growth'):
            self.assertIn(title, html)

    def test_collect_render_cli_private_and_missing_sources_work(self):
        with tempfile.TemporaryDirectory() as tmp, contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(main(['collect', '--repo', str(ROOT), '--home', tmp, '--heavy-lock', tmp+'/absent']), 0)
            s = read_json(Path(tmp)/'latest.json')
            self.assertEqual(s['builds']['git']['status'], 'available')
            self.assertEqual(len(read_lines(Path(tmp)/'history.jsonl')), 1)
            self.assertEqual(main(['render', '--home', tmp]), 0)
            self.assertIn('Kehai · private status', (Path(tmp)/'status.html').read_text())
            self.assertEqual((Path(tmp)/'latest.json').stat().st_mode & 0o777, 0o600)
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            main(['collect', '--repo', str(ROOT), '--home', str(ROOT/'tools/status/private')])


class Serving(unittest.TestCase):
    def test_password_created_owner_only_and_weak_or_exposed_files_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'password'; secret = password(p)
            self.assertGreaterEqual(len(secret), 24); self.assertEqual(password(p), secret)
            self.assertEqual(p.stat().st_mode & 0o777, 0o600)
            p.chmod(0o644)
            with self.assertRaisesRegex(ValueError, '0600'): password(p)
            p.chmod(0o600); p.write_text('short')
            with self.assertRaisesRegex(ValueError, '24'): password(p)

    def test_loopback_authentication_exact_file_and_no_directory_access(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp)/'status.html'; atomic_write(p, '<h1>private</h1>')
            token = Path(tmp)/'password'
            for host in ('0.0.0.0', '::', '192.168.1.2'):
                with self.assertRaisesRegex(ValueError, 'loopback'): make_server(p, token, host, 0)
            server = make_server(p, token, port=0)
            worker = threading.Thread(target=server.serve_forever, daemon=True); worker.start()
            try:
                conn = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=5)
                def get(path, auth=None):
                    conn.request('GET', path, headers={'Authorization': auth} if auth else {})
                    response = conn.getresponse(); body = response.read()
                    return response, body
                response, body = get('/')
                self.assertEqual(response.status, 401); self.assertNotIn(b'private</h1>', body)
                auth = 'Basic '+base64.b64encode(('owner:'+password(token)).encode()).decode()
                response, body = get('/', auth)
                self.assertEqual(response.status, 200); self.assertEqual(body, p.read_bytes())
                self.assertEqual(response.getheader('Cache-Control'), 'no-store')
                self.assertIn("connect-src 'none'", response.getheader('Content-Security-Policy'))
                for path in ('/../password', '/password', '/?token=secret'):
                    self.assertEqual(get(path, auth)[0].status, 404)
                self.assertEqual(get('/', 'Basic wrong')[0].status, 401)
                conn.close()
            finally:
                server.shutdown(); worker.join(timeout=5); server.server_close()


class Scheduler(unittest.TestCase):
    def test_generated_plist_has_literal_paths_and_no_installation(self):
        with tempfile.TemporaryDirectory(prefix='status space ') as tmp:
            home = Path(tmp)/'private home'; out = Path(tmp)/'status.plist'
            with patch('subprocess.run') as call, contextlib.redirect_stdout(io.StringIO()) as stdout:
                schedule_main(['--repo', str(ROOT), '--home', str(home), '--python', sys.executable, '--out', str(out)])
                call.assert_not_called()
            spec = plistlib.loads(out.read_bytes())
            self.assertEqual(spec['ProgramArguments'][-1], str(home.resolve()))
            self.assertEqual(spec['WorkingDirectory'], str(ROOT))
            self.assertEqual(spec['Umask'], 0o077); self.assertFalse(spec['RunAtLoad'])
            self.assertEqual(spec['StartCalendarInterval'], {'Hour': 2, 'Minute': 15})
            self.assertIn('No job installed', stdout.getvalue())
            self.assertFalse(home.exists())

    def test_invalid_paths_and_calendar_times_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            for hour, minute in [(24, 0), (-1, 0), (2, 60)]:
                with self.assertRaises(ValueError): template(ROOT, tmp, sys.executable, hour, minute)
            with self.assertRaises(ValueError): template(tmp, tmp, sys.executable)
            with self.assertRaises(ValueError): template(ROOT, ROOT/'private', sys.executable)
            with self.assertRaises(ValueError): template(ROOT, tmp, '/nonexistent/python')


if __name__ == '__main__': unittest.main()
