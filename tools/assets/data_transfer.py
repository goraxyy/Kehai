"""Transfer selected game data through a private GitHub release until the PC link is ready."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile
from zipfile import ZIP_DEFLATED, BadZipFile, ZipFile

from tools.reports.common import private_path


ARCHIVE = 'kehai-game-data.zip'
MANIFEST = 'manifest.json'
CHUNK = 1024 * 1024
LABEL = re.compile(r'[A-Za-z0-9][A-Za-z0-9_.-]*\Z')
WINDOWS_DEVICES = {'CON', 'PRN', 'AUX', 'NUL', *(f'{p}{i}' for p in ('COM', 'LPT') for i in range(1, 10))}


def member_parts(name):
    parts = name.split('/')
    for part in parts:
        if (not part or part in {'.', '..'} or part[-1] in {'.', ' '} or
                any(c in '<>:"\\|?*' or ord(c) < 32 for c in part) or
                part.split('.')[0].upper() in WINDOWS_DEVICES or
                part.lower() in {'.git', '.git-assets'} or part.lower().startswith('.env')):
            raise ValueError(f'unsupported data path: {name!r}')
    return parts


def entries(inputs):
    seen = set()
    for label, source in inputs:
        if not LABEL.fullmatch(label) or label.casefold() in seen:
            raise ValueError(f'use a unique data label made of letters, numbers, dots, dashes or underscores: {label!r}')
        seen.add(label.casefold())
        if source.is_symlink() or not (source.is_file() or source.is_dir()):
            raise ValueError(f'expected a regular file or folder: {source}')
        paths = sorted(source.rglob('*')) if source.is_dir() else [source]
        names = set()
        for path in paths:
            relative = path.relative_to(source).as_posix() if source.is_dir() else path.name
            name = f'{label}/{relative}'
            member_parts(name)
            if name.casefold() in names:
                raise ValueError(f'data paths would collide on Windows: {name}')
            names.add(name.casefold())
            if path.is_symlink() or not (path.is_file() or path.is_dir()):
                raise ValueError(f'expected a regular file or folder: {path}')
            if path.is_file():
                yield name, path


def copy_and_hash(source, target):
    digest = hashlib.sha256()
    length = 0
    while chunk := source.read(CHUNK):
        target.write(chunk)
        digest.update(chunk)
        length += len(chunk)
    return {'bytes': length, 'sha256': digest.hexdigest()}


def pack(archive, inputs):
    files = {}
    target_path = archive.resolve()
    if any(source.resolve() in (target_path, *target_path.parents) for _, source in inputs):
        raise ValueError('the archive must be outside each selected data source')
    with ZipFile(archive, 'x', compression=ZIP_DEFLATED) as zipped:
        for name, path in entries(inputs):
            with path.open('rb') as source, zipped.open(name, 'w', force_zip64=True) as target:
                before = os.fstat(source.fileno())
                files[name] = copy_and_hash(source, target)
                after = os.fstat(source.fileno())
                if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                    raise ValueError(f'data was changing during the copy: {path}')
        if not files:
            raise ValueError('select at least one data file')
        zipped.writestr(MANIFEST, json.dumps({'schema': 1, 'files': files}, sort_keys=True))


def unpack(archive, destination):
    destination = private_path(destination)
    if destination.exists():
        raise ValueError(f'choose a new restore folder: {destination}')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(archive) as zipped:
        members = zipped.infolist()
        info = {member.filename: member for member in members}
        if len(info) != len(members) or MANIFEST not in info or info[MANIFEST].file_size > 16 * CHUNK:
            raise ValueError('invalid archive manifest')
        manifest = json.loads(zipped.read(MANIFEST))
        files = manifest.get('files') if isinstance(manifest, dict) and manifest.get('schema') == 1 else None
        if not isinstance(files, dict) or not files or set(info) != {*files, MANIFEST}:
            raise ValueError('archive does not match its data manifest')
        spelling = {}
        for name, recorded in files.items():
            parts = member_parts(name)
            member = info[name]
            if len(parts) < 2 or member.is_dir() or stat.S_ISLNK(member.external_attr >> 16) or not isinstance(recorded, dict) or recorded.get('bytes') != member.file_size:
                raise ValueError(f'invalid archive data file: {name!r}')
            for depth in range(1, len(parts) + 1):
                prefix = '/'.join(parts[:depth])
                if spelling.setdefault(prefix.casefold(), prefix) != prefix:
                    raise ValueError(f'archive paths would collide on Windows: {prefix}')
                if depth < len(parts) and prefix in files:
                    raise ValueError(f'archive uses a file as a folder: {prefix}')
        if sum(info[name].file_size for name in files) > shutil.disk_usage(destination.parent).free:
            raise ValueError('not enough space for the restored data')
        with tempfile.TemporaryDirectory(prefix='.kehai-data-', dir=destination.parent) as scratch:
            restored = Path(scratch) / 'data'
            for name, recorded in files.items():
                target = restored.joinpath(*name.split('/'))
                target.parent.mkdir(parents=True, exist_ok=True)
                with zipped.open(name) as source, target.open('xb') as output:
                    if copy_and_hash(source, output) != recorded:
                        raise ValueError(f'data verification failed: {name}')
            if destination.exists():
                raise ValueError(f'restore folder appeared while unpacking: {destination}')
            restored.rename(destination)


def require_private_repo(repo):
    result = subprocess.run(['gh', 'repo', 'view', repo, '--json', 'nameWithOwner,visibility'],
                            check=True, capture_output=True, text=True)
    record = json.loads(result.stdout)
    if record.get('visibility') != 'PRIVATE' or record.get('nameWithOwner', '').casefold() != repo.casefold():
        raise ValueError('select the exact owner/name of a private repository')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    upload = commands.add_parser('upload')
    upload.add_argument('--include', action='append', required=True, metavar='LABEL=PATH')
    download = commands.add_parser('download')
    download.add_argument('--destination', type=Path, required=True, help='a new private folder')
    for command in (upload, download):
        command.add_argument('--repo', required=True, help='private GitHub owner/name')
        command.add_argument('--tag', required=True, help='the private data release tag')
    args = parser.parse_args(argv)
    try:
        if not all(LABEL.fullmatch(part) for part in args.repo.split('/')) or args.repo.count('/') != 1:
            raise ValueError('use the private repository’s owner/name')
        if args.tag in {'.', '..'} or not LABEL.fullmatch(args.tag):
            raise ValueError('use a release tag such as game-data-20261122')
        require_private_repo(args.repo)
        with tempfile.TemporaryDirectory(prefix='kehai-data-release-') as scratch:
            archive = Path(scratch) / ARCHIVE
            if args.command == 'upload':
                sources = []
                for item in args.include:
                    label, separator, path = item.partition('=')
                    if not separator or not path:
                        raise ValueError('--include expects LABEL=PATH')
                    sources.append((label, Path(path).expanduser()))
                pack(archive, sources)
                subprocess.run(['gh', 'release', 'create', args.tag, str(archive), '--repo', args.repo,
                                '--title', args.tag, '--notes', 'Kehai game data for private transfer.'], check=True)
            else:
                subprocess.run(['gh', 'release', 'download', args.tag, '--repo', args.repo,
                                '--pattern', ARCHIVE, '--dir', scratch], check=True)
                unpack(archive, args.destination)
                print(f'Verified game data restored to {args.destination}')
    except (ValueError, OSError, BadZipFile, subprocess.CalledProcessError) as error:
        parser.error(str(error))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
