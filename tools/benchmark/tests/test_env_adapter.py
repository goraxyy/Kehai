"""Protocol evidence only: no Unity process or played-game claims."""
import copy
import json
from pathlib import Path
import socketserver
import struct
import tempfile
import threading
import unittest

from tools.benchmark.env_adapter import CapabilityError, execute, normalize, preflight
from tools.benchmark.analyze import score
from tools.benchmark.suite import definition, schedule
from tools.eval.kehai_env import KehaiEnv, KehaiError
from tools.reports.common import read_json, read_lines


def request():
    return dict(schedule(), model='protocol-test', arm='planner', fixture=False,
                model_provenance={'commit': 'a' * 40, 'dirty': False})


def float32(value):
    return struct.unpack('f', struct.pack('f', value))[0]


def raw_row(req, career, shift, ledger):
    return {'executed': {'seed': career['seed'], 'player': career['player'],
                        'shift': shift['shift'], 'layout': shift['layout'],
                        'edition': 'gameplay', 'ledger': ledger, 'arm': req['arm'],
                        'traits': {k: float32(v) for k, v in career['traits'].items()},
                        'karen_present': shift['karen']},
            'actual_persona': {k: float32(.5) for k in definition()['persona_ranges']['trained-range']},
            'metrics': {'seed': career['seed'], 'shift': shift['shift'], 'karen_catches': 0,
                        'timed_out': False, 'karen_fairness_violations': 0,
                        'panic_setpoint_rmse': 0, 'sim_seconds': 150, 'time_to_catch_s': -1,
                        **{k: 1 for k in ('spills_mopped', 'shelves_restocked', 'bins_bagged',
                                         'bags_disposed', 'customers_served', 'directions_given')}}}


class Protocol:
    def __init__(self, req):
        self.req, self.messages = req, []
        self.caps = {'ok': True, 'benchmark_protocol': 1, 'four_shift_careers': True,
                     'build_edition': 'training', 'career_semantics': 'gameplay',
                     'build_commit': 'a' * 40, 'arms': ['planner', 'Vital'],
                     'players': list(definition()['players']), 'layouts': definition()['layouts']}

    def request(self, message):
        self.messages.append(message)
        if message['cmd'] == 'benchmark_capabilities':
            return self.caps
        if message['cmd'] == 'close':
            return {'ok': True}
        if message['cmd'] != 'benchmark_career':
            raise AssertionError('unexpected command')
        self.last_reply = {'ok': True, 'rows': [raw_row(self.req, message['career'], s, message['ledger'])
                                               for s in message['career']['shifts']]}
        return self.last_reply


class Adapter(unittest.TestCase):
    def setUp(self):
        self.req = request()
        self.career = next(c for c in self.req['careers'] if c['player'] == 'trained-range')
        self.raw = raw_row(self.req, self.career, self.career['shifts'][1], 'retained')

    def normalized(self, raw=None):
        return normalize(raw or self.raw, self.req, self.career, 'retained')

    def test_float32_traits_accepted_without_altering_identity(self):
        self.assertNotEqual(self.raw['executed']['traits'], self.career['traits'])
        row = self.normalized()
        self.assertEqual(row['seed'], self.career['seed'])
        self.assertEqual(row['layout'], self.career['shifts'][1]['layout'])

    def test_trait_names_values_and_nonfinite_fail_closed(self):
        for key, value in [('caution', self.career['traits']['caution'] + 1e-5),
                           ('caution', float('nan')), ('caution', True), ('extra', .5)]:
            raw = copy.deepcopy(self.raw)
            raw['executed']['traits'][key] = value
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, 'traits'):
                self.normalized(raw)
        raw = copy.deepcopy(self.raw); raw['executed']['traits'].pop('pace')
        with self.assertRaisesRegex(ValueError, 'traits'): self.normalized(raw)

    def test_every_other_execution_field_remains_exact(self):
        for field, value in [('seed', 123), ('player', 'efficient'), ('shift', 9),
                             ('layout', 'rings'), ('edition', 'testing'), ('ledger', 'wiped'),
                             ('arm', 'Vital'), ('karen_present', False)]:
            raw = copy.deepcopy(self.raw); raw['executed'][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError): self.normalized(raw)
        raw = copy.deepcopy(self.raw); raw['metrics']['seed'] += 1
        with self.assertRaisesRegex(ValueError, 'seed/shift'): self.normalized(raw)

    def test_missing_metrics_stay_unknown_and_rmse_needs_sample_count(self):
        row = self.normalized()
        self.assertIsNone(row['metrics']['panic_setpoint_rmse'])
        self.assertIsNone(row['metrics']['true_mass_5s'])
        self.assertIsNone(row['trick_counts'])
        raw = copy.deepcopy(self.raw); raw['metric_samples'] = {'panic_setpoint_rmse': 10}
        self.assertEqual(self.normalized(raw)['metrics']['panic_setpoint_rmse'], 0)
        raw['metric_samples']['panic_setpoint_rmse'] = True
        self.assertIsNone(self.normalized(raw)['metrics']['panic_setpoint_rmse'])
        raw['metrics'] = {'seed': self.career['seed'], 'shift': 2}
        self.assertTrue(all(v is None for v in self.normalized(raw)['metrics'].values()))

    def test_only_complete_trick_counts_and_measured_jobs_normalized(self):
        self.assertAlmostEqual(self.normalized()['metrics']['bot_jobs_per_min'], 2.4)
        raw = copy.deepcopy(self.raw); raw['metrics']['uses_dash'] = 5
        self.assertIsNone(self.normalized(raw)['trick_counts'])
        raw['trick_counts_complete'] = True
        self.assertEqual(self.normalized(raw)['trick_counts'], {'dash': 5})
        raw['metrics'].pop('spills_mopped')
        self.assertIsNone(self.normalized(raw)['metrics']['bot_jobs_per_min'])

    def test_survival_requires_pacing_and_never_uses_negative_catch_sentinel(self):
        self.assertNotIn('survival', self.normalized())
        raw = copy.deepcopy(self.raw); raw['pacing'] = False
        self.assertEqual(self.normalized(raw)['survival'], {'seconds': 150, 'caught': False})
        raw['metrics']['karen_catches'] = 1
        self.assertNotIn('survival', self.normalized(raw))
        raw['metrics']['time_to_catch_s'] = 27
        self.assertEqual(self.normalized(raw)['survival'], {'seconds': 27, 'caught': True})

    def test_preflight_refuses_old_server_and_unavailable_capabilities(self):
        class Old:
            def request(self, _): raise KehaiError('unknown command')
        with self.assertRaisesRegex(CapabilityError, 'lacks benchmark'): preflight(Old(), self.req)
        for field, value in [('benchmark_protocol', 0), ('four_shift_careers', False),
                             ('build_edition', 'gameplay'), ('career_semantics', 'testing'),
                             ('build_commit', 'b' * 40), ('players', ['efficient']),
                             ('arms', []), ('layouts', ['today'])]:
            env = Protocol(self.req); env.caps[field] = value
            with self.subTest(field=field), self.assertRaises(CapabilityError): preflight(env, self.req)

    def test_requested_model_hash_cannot_be_inferred(self):
        req = dict(self.req, model_provenance={'sha256': 'actual-model-hash'})
        with self.assertRaisesRegex(CapabilityError, 'model hash'): preflight(Protocol(req), req)
        env = Protocol(req); env.caps['model_sha256'] = 'actual-model-hash'
        preflight(env, req)

    def test_only_frozen_nonlearning_schedules_executed(self):
        for mutate in ('fixture', 'definition_sha256', 'careers'):
            req = copy.deepcopy(self.req)
            if mutate == 'fixture': req[mutate] = True
            elif mutate == 'careers': req[mutate].pop()
            else: req[mutate] = 'wrong'
            env = Protocol(req)
            with tempfile.TemporaryDirectory() as tmp, self.assertRaises(ValueError):
                execute(req, Path(tmp) / 'rows.jsonl', env)
            self.assertEqual(env.messages, [])

    def test_invalid_reply_preserved_before_normalization_error(self):
        env = Protocol(self.req)
        original = env.request
        def bad(message):
            reply = original(message)
            if message['cmd'] == 'benchmark_career': reply['rows'][0]['executed']['seed'] = -1
            return reply
        env.request = bad
        with tempfile.TemporaryDirectory() as tmp:
            p = Path(tmp) / 'rows.jsonl'
            with self.assertRaisesRegex(ValueError, 'seed'): execute(self.req, p, env)
            self.assertEqual(read_json(Path(tmp)/'rows-evidence/20000-retained.json'), env.last_reply)
            self.assertEqual(read_lines(p), [])

    def test_full_tcp_protocol_float32_and_raw_evidence(self):
        protocol = Protocol(self.req)
        class Handler(socketserver.StreamRequestHandler):
            def handle(self):
                for line in self.rfile:
                    message = json.loads(line)
                    reply = protocol.request(message)
                    self.wfile.write((json.dumps(reply) + '\n').encode()); self.wfile.flush()
                    if message['cmd'] == 'close': break
        with socketserver.TCPServer(('127.0.0.1', 0), Handler) as server:
            worker = threading.Thread(target=server.serve_forever, daemon=True); worker.start()
            try:
                with tempfile.TemporaryDirectory() as tmp:
                    p = Path(tmp) / 'rows.jsonl'
                    with KehaiEnv(port=server.server_address[1], timeout=5) as env:
                        execute(self.req, p, env)
                    actual = read_lines(p)
                    self.assertEqual(len(actual), 400)
                    self.assertEqual(score(self.req, actual)['missing_rows'], 0)
                    self.assertEqual(len(list((Path(tmp)/'rows-evidence').glob('*.json'))), 101)
                    self.assertEqual(actual[0]['build_edition'], 'training')
                    self.assertIsNone(actual[0]['metrics']['panic_setpoint_rmse'])
                    messages = [m for m in protocol.messages if m['cmd'] == 'benchmark_career']
                    self.assertEqual(len(messages), 100)
                    for m in messages:
                        self.assertEqual(m['career_semantics'], 'gameplay')
                        self.assertEqual(m['horizon_seconds'], 150)
                        self.assertEqual(m['purpose'], 'evaluate')
            finally:
                server.shutdown(); worker.join(timeout=5)


if __name__ == '__main__': unittest.main()
