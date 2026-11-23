import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from tools.benchmark.suite import allowed, definition, heldout, schedule
from tools.benchmark.analyze import compare, score, validate
from tools.benchmark.stats import estimate, rmst, survival_summary
from tools.benchmark.fixture_adapter import rows
from tools.benchmark.cli import main
from tools.reports.common import digest, read_json, read_lines, write_json
from tools.reports.register import latest
from tools.reports.report import benchmark_history, main as report_main


def request(model='fixture', half='practice', arm='planner'):
    return dict(schedule('evaluate', half), model=model, arm=arm, fixture=True)


class Splits(unittest.TestCase):
    def test_frozen_four_shift_400_schedule(self):
        a, b = schedule(), schedule(half='hidden')
        self.assertEqual(a, schedule())
        self.assertEqual(a['expected_shifts']+b['expected_shifts'], 400)
        self.assertFalse({c['seed'] for c in a['careers']} & {c['seed'] for c in b['careers']})
        self.assertTrue(any(c['player'] == 'cautious' for c in b['careers']))
        self.assertTrue(any(c['player'] == 'careful-corner' for c in b['careers']))
        self.assertEqual({s['layout'] for c in a['careers'] for s in c['shifts']}, set(definition()['layouts']))
        for c in a['careers']:
            self.assertEqual([s['karen'] for s in c['shifts']], [False, True, True, True])
            self.assertTrue(all(s['record_habits'] for s in c['shifts']))

    def test_train_and_tune_are_isolated(self):
        for purpose in ['train', 'tune']:
            safe = schedule(purpose)
            self.assertTrue(safe['careers'])
            for c in safe['careers']:
                self.assertFalse(heldout(c))
                allowed(c, purpose)
            with self.assertRaisesRegex(ValueError, 'inaccessible'):
                schedule(purpose, 'hidden')

    def test_planted_seed_player_layout_and_corner_leaks(self):
        good = copy.deepcopy(schedule('tune')['careers'][0])
        for field, value in [('seed', 30000), ('player', 'cautious'), ('traits', {'caution': .9, 'pace': .1})]:
            c = dict(good, **{field: value})
            with self.assertRaises(ValueError):
                allowed(c, 'tune')
        for layout in ['rings', 'split']:
            c = copy.deepcopy(good); c['shifts'][2]['layout'] = layout
            with self.assertRaisesRegex(ValueError, 'held-out'):
                allowed(c, 'tune')

    def test_unknown_metadata_fails_closed(self):
        good = copy.deepcopy(schedule('train')['careers'][0])
        for c in [dict(good, seed=None), dict(good, player='unknown'), dict(good, shifts=[]),
                  dict(good, traits={'caution': float('nan')}), dict(good, player='trained-range', traits={})]:
            with self.assertRaises(ValueError):
                allowed(c, 'train')

    def test_personas_are_seeded_full_and_in_range(self):
        cs = schedule()['careers']
        sampled = [c for c in cs if c['player'] == 'trained-range']
        self.assertEqual(len(sampled[0]['traits']), 14)
        self.assertNotEqual(sampled[0]['traits'], sampled[1]['traits'])
        for c in cs:
            if c['player'] == 'careful-corner':
                self.assertGreaterEqual(c['traits']['caution'], .75)
                self.assertLessEqual(c['traits']['pace'], .3)


class Statistics(unittest.TestCase):
    def test_career_se_and_no_singleton_zero_se(self):
        self.assertAlmostEqual(estimate([1, 3])['se'], 1)
        self.assertIsNone(estimate([1])['se'])
        self.assertIsNone(estimate([])['mean'])

    def test_censoring_not_catch_only_mean(self):
        # Half caught at 10 seconds, half followed uncaught to 150: 10 + .5*140.
        self.assertEqual(rmst([(10, True), (150, False)]), 80)
        self.assertEqual(rmst([(150, False)]), 150)
        self.assertIsNone(rmst([(10, False)]))
        self.assertEqual(rmst([(10, True), (10, False)]), None)
        self.assertEqual(rmst([(10, True), (10, True)]), 10)

    def test_survival_se_by_career(self):
        out = survival_summary({'a': [(10, True), (150, False)], 'b': [(20, True), (150, False)]})
        self.assertEqual(out['careers'], 2)
        self.assertEqual(out['censored'], 2)
        self.assertAlmostEqual(out['mean'], 82.5)
        self.assertAlmostEqual(out['se'], 2.5)

    def test_stratified_means_pairing_and_negative_results(self):
        a, b = request('a'), request('b')
        ar, br = list(rows(a)), list(rows(b, .08))
        result = score(a, ar)
        self.assertEqual(result['missing_rows'], 0)
        self.assertIn('cautious / seen-layout', result['groups'])
        group = result['groups']['efficient / seen-layout']
        self.assertEqual(group['panic_setpoint_rmse']['careers'], 5)
        self.assertAlmostEqual(group['adaptation_rmse_delta']['mean'], -.03)
        self.assertIsNone(group['true_mass_10s']['mean'])
        self.assertEqual(group['top_trick_share']['gate'], 'pass')
        diff = compare(a, ar, b, br)
        self.assertAlmostEqual(diff['efficient / seen-layout']['panic_setpoint_rmse']['mean'], .08)
        self.assertAlmostEqual(diff['efficient / seen-layout']['panic_setpoint_rmse']['se'], 0)

    def test_partial_careers_excluded_and_pair_count_honest(self):
        a, b = request('a'), request('b')
        ar, br = list(rows(a)), list(rows(b))
        br = [r for r in br if not (r['seed'] == 20000 and r['shift'] == 3 and r['ledger'] == 'retained')]
        card = score(b, br)
        self.assertEqual(card['missing_rows'], 1)
        self.assertEqual(card['groups']['efficient / seen-layout']['caught']['careers'], 4)
        self.assertEqual(compare(a, ar, b, br)['efficient / seen-layout']['caught']['careers'], 4)

    def test_unsafe_comparisons_and_duplicates_refused(self):
        a = request(); ar = list(rows(a))
        for b in [request(half='hidden'), request(arm='E'), dict(a, definition_sha256='changed')]:
            with self.assertRaises(ValueError):
                compare(a, ar, b, list(rows(b)))
        with self.assertRaisesRegex(ValueError, 'duplicate'):
            score(a, ar+[ar[0]])
        b = copy.deepcopy(a); b['careers'].pop()
        with self.assertRaisesRegex(ValueError, 'full frozen'):
            score(b, ar)

    def test_fairness_failure_and_invalid_sentinels(self):
        a = request(); ar = list(rows(a))
        ar[0]['metrics']['fairness_violations'] = 1
        self.assertEqual(score(a, ar)['groups']['efficient / seen-layout']['fairness_violations']['gate'], 'FAIL')
        for value in [-1, float('nan'), float('inf')]:
            ar[0]['metrics']['fairness_violations'] = value
            with self.assertRaises(ValueError): score(a, ar)

    def test_bot_human_requires_actual_reference(self):
        a = request(); ar = list(rows(a)); ar[0]['metrics']['bot_human_accuracy'] = .62
        with self.assertRaisesRegex(ValueError, 'consented'):
            score(a, ar)
        ar[0]['human_reference'] = {'consented': True, 'player_split': 'heldout-persons', 'source_sha256': 'actual-reference-hash'}
        score(a, ar)

    def test_missing_wiped_fairness_cannot_pass_safety_gate(self):
        a = request(); ar = list(rows(a))
        next(r for r in ar if r['ledger'] == 'wiped')['metrics']['fairness_violations'] = None
        self.assertEqual(score(a, ar)['groups']['efficient / seen-layout']['fairness_violations']['gate'], 'unavailable')

    def test_synthetic_never_silently_a_real_score(self):
        a = request(); a['fixture'] = False
        with self.assertRaisesRegex(ValueError, 'synthetic'):
            score(a, list(rows(a)))

    def test_vital_survival_requires_pacing_off_and_full_followup(self):
        a = request(arm='Vital'); ar = list(rows(a))
        for r in ar:
            r['pacing'] = False
            r['survival'] = {'seconds': 150, 'caught': False}
        card = score(a, ar)
        self.assertEqual(card['groups']['efficient / seen-layout']['catch_rmst_s']['mean'], 150)
        for r in ar: r['pacing'] = True
        self.assertIsNone(score(a, ar)['groups']['efficient / seen-layout']['catch_rmst_s']['mean'])


class Runner(unittest.TestCase):
    def test_training_report_pairs_two_registered_model_versions(self):
        with tempfile.TemporaryDirectory() as folder:
            home = Path(folder)
            for version in ('Satori 1.0.0', 'Satori 1.1.0'):
                self.assertEqual(0, main(['run', '--fixture', '--home', str(home), '--model', version, '--arm', 'Basic',
                                          '--', sys.executable, '-B', '-m', 'tools.benchmark.fixture_adapter']))
            register = home / 'runs.jsonl'
            events = latest(register)
            cards, pairs, warnings = benchmark_history(events)
            self.assertEqual(['Satori 1.0.0', 'Satori 1.1.0'], [card['model'] for card in cards])
            self.assertEqual([], warnings)
            self.assertEqual(1, len(pairs))
            label, groups = pairs[0]
            self.assertIn('synthetic fixture', label)
            self.assertTrue(any(field['se'] is not None for metrics in groups.values() for field in metrics.values()))

            output = home / 'report.html'
            self.assertEqual(0, report_main(['--register', str(register), '--benchmark-history', '--out', str(output)]))
            report = output.read_text()
            self.assertIn('Satori 1.0.0 → Satori 1.1.0', report)
            self.assertIn('Paired changes and standard errors', report)
            self.assertIn('SYNTHETIC FIXTURE', report)

            rows_path = Path(cards[0]['provenance']['rows']['path'])
            rows_path.write_text('changed')
            cards, pairs, warnings = benchmark_history(events)
            self.assertEqual(2, len(cards))
            self.assertEqual([], pairs)
            self.assertIn('missing or changed benchmark file', warnings[0])

    def test_fixture_cli_end_to_end_and_register(self):
        with tempfile.TemporaryDirectory() as tmp:
            code = main(['run', '--fixture', '--home', tmp, '--model', 'FIXTURE', '--', sys.executable, '-B', '-m', 'tools.benchmark.fixture_adapter'])
            self.assertEqual(code, 0)
            run = next((Path(tmp)/'benchmark').iterdir())
            self.assertTrue(read_json(run/'score.json')['synthetic'])
            self.assertEqual([r['state'] for r in read_lines(Path(tmp)/'runs.jsonl')], ['running', 'completed'])

    def test_hidden_failure_consumes_attempt(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = ['run', '--half', 'hidden', '--home', tmp, '--model', 'release-test', '--', sys.executable, '-c', 'raise SystemExit(7)']
            self.assertEqual(main(args), 7)
            with self.assertRaises(SystemExit): main(args)
            events = read_lines(Path(tmp)/'runs.jsonl')
            self.assertEqual([r['state'] for r in events], ['running', 'failed'])

    def test_zero_exit_with_no_result_is_failed(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(main(['run', '--home', tmp, '--model', 'test', '--', sys.executable, '-c', 'pass']), 1)
            self.assertEqual(read_lines(Path(tmp)/'runs.jsonl')[-1]['state'], 'failed')

    def test_zero_exit_with_partial_results_is_failed_but_raw_rows_preserved(self):
        def partial(command, cwd):
            req = read_json(command[-2])
            Path(command[-1]).write_text(json.dumps(next(rows(req)))+'\n')
            return 0
        with tempfile.TemporaryDirectory() as tmp, patch('tools.benchmark.cli.subprocess.call', side_effect=partial):
            self.assertEqual(main(['run', '--fixture', '--home', tmp, '--model', 'partial', '--', 'fake']), 1)
            event = read_lines(Path(tmp)/'runs.jsonl')[-1]
            self.assertIn('incomplete schedule', event['error'])
            run = next((Path(tmp)/'benchmark').iterdir())
            self.assertEqual(len(read_lines(run/'rows.jsonl')), 1)
            self.assertFalse((run/'score.json').exists())


if __name__ == '__main__':
    unittest.main()
