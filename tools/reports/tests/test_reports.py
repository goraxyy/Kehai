import concurrent.futures
import copy
import json
import math
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from tools.reports.common import atomic_write, digest, private_path, read_json, read_lines, source, write_json
from tools.reports.register import append, audit, latest, main
from tools.reports.fixtures import create
from tools.reports.html import page, scoreboard
from tools.reports.network import inspect
from tools.reports.replay import load_record, render
from tools.reports.report import build

ROOT = Path(__file__).resolve().parents[3]


class PrivateOutputs(unittest.TestCase):
    def test_private_files_cannot_enter_any_checkout(self):
        for path in [ROOT/'tools/reports/private.json', ROOT/'Builds/status.html']:
            with self.assertRaisesRegex(ValueError, 'outside git'):
                atomic_write(path, 'private')
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'repo';p.mkdir();(p/'.git').write_text('gitdir: elsewhere')
            with self.assertRaises(ValueError): private_path(p/'out.html')

    def test_owner_only_and_atomic(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'data.json';write_json(p, {'n': 1})
            self.assertEqual(read_json(p), {'n': 1})
            self.assertEqual(p.stat().st_mode & 0o777, 0o600)
            self.assertFalse(list(p.parent.glob('.data.json*')))

    def test_corrupt_register_not_silently_skipped(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'runs';p.write_text('{broken\n')
            with self.assertRaisesRegex(ValueError, ':1: invalid JSON'): read_lines(p)


class Register(unittest.TestCase):
    def test_append_import_idempotence_and_conflict(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'runs';a={'id':'a','state':'running'};b={'id':'a','state':'completed'}
            self.assertTrue(append(p,a));self.assertFalse(append(p,a));append(p,b)
            self.assertEqual(latest(p), [b])
            with self.assertRaises(ValueError): append(p, dict(b, extra='changed'))
            with self.assertRaises(ValueError): append(p, {'id':'a','state':'failed'})
            self.assertFalse(append(p,a)) # importing the same start is harmless, doesn't regress latest
            self.assertEqual(latest(p), [b])

    def test_concurrent_writers_no_lost_lines(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'runs'
            with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
                values=list(executor.map(lambda i:append(p, {'id':str(i),'state':'imported'}), range(16)))
            self.assertEqual(len(read_lines(p)),16)
            self.assertTrue(all(values))

    def test_wrapper_records_failure_and_input_hashes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp); p=root/'runs.jsonl'
            code=main(['--register',str(p),'run','--kind','fit','--manifest',str(root/'manifest.json'),'--',sys.executable,'-c','raise SystemExit(9)'])
            self.assertEqual(code,9)
            start,end=read_lines(p)
            self.assertEqual(end['state'],'failed');self.assertEqual(end['exit_code'],9)
            self.assertEqual(start['data_snapshot'],end['data_snapshot'])
            self.assertEqual(start['seeds'],[10000]);self.assertTrue(start['computer'])
            self.assertTrue(all(i.get('sha256') for i in start['inputs']))

    def test_gate_refuses_before_subprocess(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp);m=read_json(root/'manifest.json');m['careers'][0]['shifts'][2]['layout']='rings';write_json(root/'manifest.json',m)
            with patch('tools.reports.register.subprocess.call') as call:
                with self.assertRaises(SystemExit): main(['--register',str(root/'runs'),'run','--kind','train','--manifest',str(root/'manifest.json'),'--','no-run'])
                call.assert_not_called()
                self.assertFalse((root/'runs').exists())

    def test_interrupt_and_spawn_failure_have_terminal_exit_status(self):
        for failure, code, state in [(KeyboardInterrupt(), 130, 'interrupted'), (OSError('missing executable'), 127, 'failed')]:
            with tempfile.TemporaryDirectory() as tmp, patch('tools.reports.register.subprocess.call', side_effect=failure):
                p = Path(tmp)/'runs.jsonl'
                self.assertEqual(main(['--register', str(p), 'run', '--kind', 'test', '--', 'unused']), code)
                self.assertEqual(read_lines(p)[-1]['exit_code'], code)
                self.assertEqual(latest(p)[0]['state'], state)

    def test_manifest_reports_too_few_shifts_and_missing_records(self):
        m={'careers':[{'seed':10000,'player':'efficient','shifts':[{'shift':1,'layout':'today'}]}]}
        warnings=audit(m,'train')
        self.assertTrue(any('too few' in w for w in warnings));self.assertIn('learning records unavailable',warnings)
        self.assertTrue(any('four explicit shifts' in w for w in warnings))


class ReplayNetwork(unittest.TestCase):
    def test_real_format_nested_frames_and_recorded_map(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp);r=load_record(root/'shift.json')
            self.assertTrue(r['map']['floor']);self.assertTrue(any('per-decision' in w for w in r['missing']))
            html=render([r]);self.assertIn('map.shapes',html)
            self.assertIn('No recorded frame at this time',html)

    def test_exact_model_input_required_and_recurrence_missing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp);r=load_record(root/'shift.json',root/'trace.json',root/'brain.json')
            self.assertIsNotNone(r['decisions'][0]['inspection'])
            write_json(root/'brain.json',dict(read_json(root/'brain.json'),recurrent={'size':1}))
            r=load_record(root/'shift.json',root/'trace.json',root/'brain.json')
            self.assertIsNone(r['decisions'][0]['inspection']) # hash mismatch, no invented activations
            trace=read_json(root/'trace.json');trace['model_sha256']=source(root/'brain.json')['sha256'];write_json(root/'trace.json',trace)
            r=load_record(root/'shift.json',root/'trace.json',root/'brain.json')
            self.assertIn('feed-forward',r['decisions'][0]['missing'])

    def test_analytic_saliency_matches_finite_differences(self):
        with tempfile.TemporaryDirectory() as tmp:
            net=read_json(create(tmp)/'brain.json');x=[.2,-.4]
            result=inspect(net,x,0,0)
            for i, grad in enumerate(result['gradient']):
                plus=x.copy();minus=x.copy();plus[i]+=1e-6;minus[i]-=1e-6
                empirical=(inspect(net,plus,0,0)['logits'][0]-inspect(net,minus,0,0)['logits'][0])/2e-6
                self.assertAlmostEqual(grad,empirical,places=7)
            self.assertEqual([r['input'] for r in result['top_three']],sorted(range(2),key=lambda i:abs(result['gradient'][i]),reverse=True))
            self.assertIn('not causal',result['method'])

    def test_clamping_and_unsupported_indices(self):
        with tempfile.TemporaryDirectory() as tmp:
            net=read_json(create(tmp)/'brain.json')
            self.assertEqual(inspect(net,[6,0],0,0)['gradient'][0],0)
            with self.assertRaisesRegex(ValueError,'nondifferentiable'): inspect(net,[5,0],0,0)
            with self.assertRaises(ValueError): inspect(net,[.1,.2],-1,0)

    def test_paired_replay_refuses_unverified_seed_shift_layout(self):
        with tempfile.TemporaryDirectory() as tmp:
            r=load_record(create(tmp)/'shift.json')
            self.assertIn('Recorded shift replay',render([r,r]))
            for field in ['seed','shift','layout']:
                b=dict(r,**{field:None})
                with self.assertRaisesRegex(ValueError,'matching explicit'):render([r,b])

    def test_probabilities_and_trace_alignment_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp);t=read_json(root/'trace.json');t['decisions'][0]['probabilities']=[.7,.8];write_json(root/'trace.json',t)
            with self.assertRaisesRegex(ValueError,'normalized'):load_record(root/'shift.json',root/'trace.json')
            t['shift']=3;write_json(root/'trace.json',t)
            with self.assertRaisesRegex(ValueError,'differs'):load_record(root/'shift.json',root/'trace.json')

    def test_html_does_not_allow_script_breakout(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=create(tmp);r=load_record(root/'shift.json');r['label']='</script><script>alert(1)</script>'
            html=render([r]);self.assertNotIn('</script><script>alert',html)
            self.assertIn('\\u003c/script',html)

    def test_generated_browser_script_parses(self):
        import shutil
        if not shutil.which('node'):self.skipTest('node unavailable')
        with tempfile.TemporaryDirectory() as tmp:
            r=load_record(create(tmp)/'shift.json');html=render([r]);script=re.search(r'<script>(.*)</script>',html,re.S).group(1)
            f=Path(tmp)/'replay.js';f.write_text(script)
            p=subprocess.run(['node','--check',str(f)],capture_output=True,text=True)
            self.assertEqual(p.returncode,0,p.stderr)


class Reports(unittest.TestCase):
    def test_historical_aggregate_negative_results_and_missing_se(self):
        card={'recorded':{'motion':{'chosen':'p1','p0':{'val':-6.55},'p1':{'val':-6.13},'p2':{'val':-6.81}}}}
        html=build(card=card)
        self.assertIn('p2',html);self.assertIn('Historical motion',html)
        self.assertIn('Career-level uncertainty',html);self.assertIn('training curves unavailable',html)

    def test_scoreboards_do_not_mix_definition_or_fixture(self):
        from tools.benchmark.tests.test_benchmark import request
        from tools.benchmark.fixture_adapter import rows
        from tools.benchmark.analyze import score
        a=request('A');ca=score(a,list(rows(a)));b=request('B');cb=score(b,list(rows(b,.1)))
        cb['definition_sha256']='other-definition'
        html=scoreboard([ca,cb]);self.assertEqual(html.count('SYNTHETIC FIXTURE'),2)
        self.assertIn('missing',html.lower())


if __name__=='__main__':unittest.main()
