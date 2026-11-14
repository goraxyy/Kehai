"""Collect, persist observed history, render a private HTML file, or serve on loopback.

Run `collect` daily with your existing scheduler; this command installs no jobs or deployment.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path

from .collect import collect
from .dashboard import render
from tools.reports.common import DEFAULT_HOME, atomic_write, private_path, read_json, read_lines, write_json
from tools.reports.register import locked


def remember(home, snapshot):
    p = private_path(Path(home) / 'history.jsonl')
    with locked(p):
        old = read_lines(p)
        cutoff = (dt.date.fromisoformat(snapshot['date'])-dt.timedelta(days=29)).isoformat()
        days = {r['date']: r for r in old if cutoff <= r['date'] <= snapshot['date']}
        days[snapshot['date']] = snapshot
        rows = sorted(days.values(), key=lambda r: r['date'])
        atomic_write(p, ''.join(json.dumps(r, allow_nan=False)+'\n' for r in rows))
    return rows


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest='action', required=True)
    p = sub.add_parser('collect')
    p.add_argument('--repo', default='.')
    p.add_argument('--home', default=str(DEFAULT_HOME))
    p.add_argument('--register')
    p.add_argument('--score', action='append', default=[])
    p.add_argument('--tests', action='append', default=[])
    p.add_argument('--build', action='append', default=[])
    p.add_argument('--github', help='optional OWNER/REPO; uses existing gh login; no credential requirement')
    p.add_argument('--marketing-dir')
    p.add_argument('--heavy-lock', default=str(Path.home() / 'TokenLimit/marketing/state/heavy.lock'))
    p = sub.add_parser('render')
    p.add_argument('--home', default=str(DEFAULT_HOME))
    p.add_argument('--out')
    p = sub.add_parser('serve')
    p.add_argument('--file', default=str(DEFAULT_HOME / 'status.html'))
    p.add_argument('--token-file', default=str(DEFAULT_HOME / 'server-password'))
    p.add_argument('--host', default='127.0.0.1')
    p.add_argument('--port', type=int, default=8765)
    a = ap.parse_args(argv)
    try:
        if a.action == 'collect':
            home = private_path(a.home)
            snapshot = collect(a.repo, a.register or home / 'runs.jsonl', a.score, a.tests, a.build, a.github, a.marketing_dir, a.heavy_lock)
            write_json(home / 'latest.json', snapshot)
            history = remember(home, snapshot)
            atomic_write(home / 'status.html', render(snapshot, history))
            print(json.dumps(snapshot, indent=2, allow_nan=False))
        elif a.action == 'render':
            home = private_path(a.home)
            atomic_write(a.out or home / 'status.html', render(read_json(home / 'latest.json'), read_lines(home / 'history.jsonl')))
            print(a.out or home / 'status.html')
        else:
            from .serve import make_server
            server = make_server(a.file, a.token_file, a.host, a.port)
            print(f'Private server: http://{a.host}:{server.server_port}/ — username owner; password in {a.token_file}', flush=True)
            try:
                server.serve_forever()
            except KeyboardInterrupt:
                pass
            finally:
                server.server_close()
        return 0
    except (ValueError, OSError, KeyError) as ex:
        ap.error(str(ex))


if __name__ == '__main__':
    raise SystemExit(main())
