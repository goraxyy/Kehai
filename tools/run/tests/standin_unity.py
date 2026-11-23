#!/usr/bin/env python3
"""A stand-in for Unity in kehai-run's tests: reads the same arguments, writes a log, results and
some game data, changes the editor prefs as Unity does, and exits, fails or hangs on request."""
import json
import os
import sys
import time
from pathlib import Path

args = sys.argv[1:]
def arg(name):
    return args[args.index(name) + 1] if name in args else None

project = Path(arg('-projectPath'))
log = Path(arg('-logFile'))
pidfile = os.environ.get('STANDIN_PIDFILE')
if pidfile:
    Path(pidfile).write_text(str(os.getpid()))
seen = {
    'new_file': (project / 'Assets' / 'new.cs').exists(),
    'deleted_file': (project / 'Assets' / 'old.cs').exists(),
    'art': (project / 'Assets' / 'art.png').exists(),
    'source_commit': os.environ.get('KEHAI_RUN_CODE_COMMIT'),
    'product': next((l.split(': ', 1)[1].strip() for l in (project / 'ProjectSettings' / 'ProjectSettings.asset').read_text().splitlines() if l.startswith('  productName: ')), ''),
}
league = arg('-league')
if league:
    seen['league'] = Path(league).read_text()
log.write_text('Running tests for ExecutionSettings\n' + json.dumps(seen) + '\n')
prefs = os.environ.get('STANDIN_PREFS')
if prefs:
    Path(prefs).write_text(json.dumps({'LastUsedProjectPath': str(project), 'kProjectBasePath': str(project)}))
data_root = os.environ.get('STANDIN_DATA_ROOT')
if data_root:
    d = Path(data_root) / seen['product'] / 'shift_records'
    d.mkdir(parents=True, exist_ok=True)
    (d / 'shift_001.json').write_text('{}')
    (d / 'shift_001.krec').write_bytes(b'fixture replay')
    if league or '-kehai-ablation' in args:
        evaluation = d.parent / 'kehai_eval'
        evaluation.mkdir()
        name = 'league_fixture.jsonl' if league else 'ablation_fixture.jsonl'
        (evaluation / name).write_text('{}\n')
trace = arg('-belief-trace')
if trace:
    Path(trace).mkdir(parents=True, exist_ok=True)
    (Path(trace) / 'map.json').write_text('{}')
finish_file = os.environ.get('STANDIN_FINISH_FILE')
if finish_file:
    finish = Path(finish_file)
    while not finish.exists():
        time.sleep(.05)
time.sleep(float(os.environ.get('STANDIN_SLEEP', '0')))
results = arg('-testResults')
if results:
    Path(results).write_text('<test-run result="Passed" total="1" passed="1" failed="0"/>')
sys.exit(int(os.environ.get('STANDIN_EXIT', '0')))
