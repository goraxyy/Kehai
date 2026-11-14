"""Read-only authenticated loopback serving of exactly one private HTML file."""
from __future__ import annotations

import base64
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import secrets

from tools.reports.common import atomic_write, private_path


def password(path):
    p = private_path(path)
    if not p.exists():
        atomic_write(p, secrets.token_urlsafe(32) + '\n')
    # Credentials are only read from an owner-only file, never CLI arguments or URL strings.
    if os.name != 'nt' and p.stat().st_mode & 0o077:
        raise ValueError('password file must have mode 0600')
    value = p.read_text(encoding='utf-8').strip()
    if len(value) < 24:
        raise ValueError('password file must contain at least 24 characters')
    return value


def handler(document, secret):
    expected = 'Basic ' + base64.b64encode(('owner:' + secret).encode()).decode()
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if not hmac.compare_digest(self.headers.get('Authorization', '').encode(), expected.encode()):
                self.send_response(401)
                self.send_header('WWW-Authenticate', 'Basic realm="Kehai private", charset="UTF-8"')
                self.send_header('Cache-Control', 'no-store')
                self.end_headers()
                return
            # No directory listing, arbitrary files, upload endpoints or query-string tokens.
            if self.path != '/':
                self.send_error(404)
                return
            data = Path(document).read_bytes()
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(data)))
            self.send_header('Cache-Control', 'no-store')
            self.send_header('X-Content-Type-Options', 'nosniff')
            self.send_header('Referrer-Policy', 'no-referrer')
            self.send_header('Content-Security-Policy', "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'")
            self.end_headers()
            self.wfile.write(data)

        def log_message(self, *_):
            pass  # Do not write request paths or Authorization headers to a log.
    return Handler


def make_server(document, token_file, host='127.0.0.1', port=8765):
    if host not in {'127.0.0.1', 'localhost'}:
        raise ValueError('private server accepts loopback only; remote hosting is not configured')
    p = private_path(document)
    if not p.is_file():
        raise ValueError('render a private HTML file first')
    return ThreadingHTTPServer((host, port), handler(p, password(token_file)))
