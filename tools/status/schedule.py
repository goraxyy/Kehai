"""Generate a user-installable launchd plist. Does not install, load, or deploy anything."""
from __future__ import annotations

import argparse
from pathlib import Path
import plistlib
import sys

from tools.reports.common import atomic_write, private_path

LABEL = 'com.tokenlimit.kehai-status'


def template(repo, home, python, hour=2, minute=15):
    repo, home, python = Path(repo).expanduser().resolve(), private_path(home), Path(python).expanduser().resolve()
    if not (repo / 'tools/status/cli.py').is_file():
        raise ValueError('repository must contain tools/status/cli.py')
    if not python.is_file():
        raise ValueError('supply an existing absolute Python executable')
    if not 0 <= hour <= 23 or not 0 <= minute <= 59:
        raise ValueError('invalid local calendar time')
    return {'Label': LABEL, 'ProgramArguments': [str(python), '-B', '-m', 'tools.status.cli', 'collect', '--repo', str(repo), '--home', str(home)],
            'WorkingDirectory': str(repo), 'StartCalendarInterval': {'Hour': hour, 'Minute': minute},
            'StandardOutPath': str(home / 'collector.stdout.log'), 'StandardErrorPath': str(home / 'collector.stderr.log'),
            'Umask': 0o077, 'ProcessType': 'Background', 'RunAtLoad': False}


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--repo', default='.')
    ap.add_argument('--home', required=True)
    ap.add_argument('--python', default=sys.executable)
    ap.add_argument('--hour', type=int, default=2)
    ap.add_argument('--minute', type=int, default=15)
    ap.add_argument('--out', required=True)
    a = ap.parse_args(argv)
    try:
        spec = template(a.repo, a.home, a.python, a.hour, a.minute)
        # Plist contains only explicit paths and arguments, no credentials or shell expansion.
        atomic_write(a.out, plistlib.dumps(spec, fmt=plistlib.FMT_XML).decode())
        target = Path.home() / 'Library/LaunchAgents' / (LABEL+'.plist')
        text = (f'Generated {a.out}. No job installed.\nCreate the private home directory first: {Path(a.home).expanduser()}\n'
                f'Manual installation: copy the plist to {target}, then use launchctl bootstrap gui/<your-uid> <that-path>.\n'
                'Remove/unload with launchctl bootout gui/<your-uid> <that-path>.\n'
                'The Mac must be awake/logged in. Verify real daily snapshots; no uptime guarantee is implied.\n')
        print(text)
    except (ValueError, OSError) as ex:
        ap.error(str(ex))


if __name__ == '__main__':
    main()
