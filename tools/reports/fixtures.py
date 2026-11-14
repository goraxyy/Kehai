"""Create clearly labelled synthetic CLI inputs outside git; never claimed as real telemetry."""
import argparse
from pathlib import Path
from .common import atomic_write, source, write_json


def create(root):
    root = Path(root)
    net = {'kind': 'brain', 'name': 'SYNTHETIC FIXTURE', 'mean': [0, 0], 'std': [1, 2],
           'hidden': [{'out': 2, 'w': [1., -.5, .3, 2.], 'b': [.1, -.2]}],
           'heads': [{'out': 2, 'w': [1., -.3, -.2, .7], 'b': [0., .2]}]}
    write_json(root / 'brain.json', net)
    trace = {'seed': 20000, 'shift': 2, 'model_sha256': source(root / 'brain.json')['sha256'],
             'decisions': [{'t': 0, 'inputs': [.2, -.4], 'branch': 0, 'choice': 0, 'probabilities': [.6, .4]}]}
    write_json(root / 'trace.json', trace)
    record = {'model': 'SYNTHETIC FIXTURE', 'seed': 20000, 'shift': 2, 'layout': 'today',
              'plan': {'bounds': [0, 0, 10, 10], 'floor': [[0, 0, 10, 0, 10, 10, 1]],
                       'shapes': [{'c': [2, 2, 3, 2, 3, 4, 2, 4]}]},
              'frames': [[0, 1, 1, 0, 0, 0, 1, 1, 5, 5, 0, 0, 0, 0, 0, 0, 0, 1, [], [], [], [], [], [], [], 0, 0],
                         [1, 2, 1, 0, 0, 0, 1, 1, 4, 5, 0, 0, 0, 0, 0, 0, 0, 1, [], [], [], [], [], [], [], 0, 0]]}
    write_json(root / 'shift.json', record)
    atomic_write(root / 'results.xml', '<test-run total="3" passed="2" failed="1" skipped="0"/>')
    write_json(root / 'manifest.json', {'learning_records': ['shift.json'], 'careers': [
        {'seed': 10000, 'player': 'efficient', 'shifts': [{'shift': i, 'layout': 'today'} for i in range(1, 5)]}]})
    return root


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('directory')
    a = ap.parse_args(argv)
    print(create(a.directory))


if __name__ == '__main__':
    main()
