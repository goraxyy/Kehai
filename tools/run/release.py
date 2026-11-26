"""kehai-run release: tag a version in every repo, and rebuild any tagged version from scratch
(0.5.2 plan, C10.4; the versions page, section 3).

    tools/run/kehai-run release tag 0.5.2-beta.1            tag the code (and the art, once it has a repo)
    tools/run/kehai-run release tag 0.5.2-beta.1 --push     ... and push the tags (only on the owner's go)
    tools/run/kehai-run release check v0.5.2-beta.1         lay the tag into a scratch folder and compare, no Unity
    tools/run/kehai-run release rebuild 0.5.2-beta.1 --platform macos
                                                            the same, then build it through kehai-run (editor closed)

The tag is annotated. Its message records the code commit, the art commit ("none" until the art
repo exists, Phase 7) and the Unity version, because ProjectSettings isn't in the code repo.

A rebuild lays the tag on a kehai-run clone, then the art at the tag's art commit (from
`.git-assets` in the main folder), removes any code under Assets/!_Project that the tag doesn't
have, and compares every file with the tag before Unity starts. A tag with no art commit builds
with today's art only when asked (--art-today), and the stamp then says the art is unversioned.
"""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

ART_GIT = '.git-assets'
UNVERSIONED = 'unversioned'
CODE_ROOT = 'Assets/!_Project'
CODE_SUFFIXES = ('.cs', '.asmdef', '.asmref', '.shader', '.hlsl', '.cginc', '.compute')
# The game's version (RELEASE_PLAN, Versions): MAJOR.MINOR.PATCH, a playtest build with -beta.N.
# KehaiBuild's ChannelLabel makes the same two forms; -rc.N waits until KehaiBuild knows it.
VERSION = re.compile(r'\A(?P<base>(?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2})(?:-beta\.(?P<beta>[1-9][0-9]*))?\Z')


class ReleaseError(Exception):
    pass


def parse(version: str) -> tuple[str, str | None]:
    """'0.5.2-beta.1' -> ('0.5.2', '1'); a leading v is allowed."""
    m = VERSION.match(version[1:] if version.startswith('v') else version)
    if not m:
        raise ReleaseError(f'{version!r} is not a release version: MAJOR.MINOR.PATCH, or with -beta.N '
                           '(0.5.2, 0.5.2-beta.1)')
    return m['base'], m['beta']


def tag_name(version: str) -> str:
    base, beta = parse(version)
    return f'v{base}' + (f'-beta.{beta}' if beta else '')


def channel_args(version: str) -> list[str]:
    """A beta is the testing channel, a plain version prod (the versions page, section 5)."""
    base, beta = parse(version)
    if beta:
        return ['-channel', 'testing', '-release-version', base, '-beta', beta]
    return ['-channel', 'prod', '-release-version', base]


def _git(*args: str, cwd: Path | None = None, env: dict | None = None, stdin: str | None = None) -> str:
    p = subprocess.run(['git', *args], cwd=cwd, env=env, input=stdin, capture_output=True, text=True)
    if p.returncode != 0:
        raise ReleaseError(f"git {' '.join(args)}: {p.stderr.strip() or p.stdout.strip()}")
    return p.stdout.rstrip('\n')


def code_git(repo: Path, *args: str, **kw) -> str:
    return _git('-C', str(repo), *args, **kw)


def art_git(main: Path, work_tree: Path, *args: str, **kw) -> str:
    return _git(f'--git-dir={main / ART_GIT}', f'--work-tree={work_tree}', *args, **kw)


def has_art_repo(main: Path) -> bool:
    return (main / ART_GIT).is_dir()


def unity_version(main: Path) -> str:
    p = main / 'ProjectSettings' / 'ProjectVersion.txt'
    for line in p.read_text().splitlines() if p.exists() else []:
        if line.startswith('m_EditorVersion:'):
            return line.split(':', 1)[1].strip()
    return 'unknown'


@dataclass
class TagInfo:
    name: str
    code: str
    art: str
    unity: str


def _has_tag(lister: Callable[..., str], name: str) -> bool:
    try:
        lister('rev-parse', '--verify', '--quiet', f'refs/tags/{name}')
        return True
    except ReleaseError:
        return False


def tag(repo: Path, main: Path, version: str, *, push: bool = False, remote: str = 'origin',
        base: str | None = None, log=print) -> TagInfo:
    """Tag HEAD (clean, and on main) in the code repo, and the art repo's HEAD once it exists."""
    name = tag_name(version)
    if code_git(repo, 'status', '--porcelain', '--untracked-files=no'):
        raise ReleaseError('the code checkout has uncommitted changes; commit or drop them first')
    head = code_git(repo, 'rev-parse', 'HEAD')
    base = base or f'{remote}/main'
    try:
        code_git(repo, 'merge-base', '--is-ancestor', head, base)
    except ReleaseError:
        raise ReleaseError(f'HEAD {head[:7]} is not on {base}: a release is tagged on main') from None
    if _has_tag(lambda *a: code_git(repo, *a), name):
        raise ReleaseError(f'{name} already exists in the code repo; a version is tagged once')
    art = 'none'
    if has_art_repo(main):
        if art_git(main, main, 'status', '--porcelain', '--untracked-files=no'):
            raise ReleaseError('the art repo has uncommitted changes; commit or drop them first')
        if _has_tag(lambda *a: art_git(main, main, *a), name):
            raise ReleaseError(f'{name} already exists in the art repo')
        art = art_git(main, main, 'rev-parse', 'HEAD')
    info = TagInfo(name, head, art, unity_version(main))
    message = f'Kehai {name[1:]}\n\ncode: {info.code}\nart: {info.art}\nunity: {info.unity}\n'
    code_git(repo, 'tag', '-a', name, '-m', message, head)
    if art != 'none':
        art_git(main, main, 'tag', '-a', name, '-m', message, art)
    shown = art[:7] if art != 'none' else 'none (no art repo yet)'
    log(f'kehai-run: tagged {name}: code {head[:7]}, art {shown}, Unity {info.unity}')
    if push:
        code_git(repo, 'push', remote, f'refs/tags/{name}')
        if art != 'none':
            art_git(main, main, 'push', remote, f'refs/tags/{name}')
        log(f'kehai-run: pushed {name}')
    else:
        log(f'kehai-run: the tag is local; to publish it: git push {remote} refs/tags/{name}'
            + (f' (and the same in {ART_GIT})' if art != 'none' else ''))
    return info


def read_tag(repo: Path, ref: str) -> TagInfo:
    """A release tag's facts. Any other ref (a commit) has no art commit and an unknown Unity."""
    try:
        code = code_git(repo, 'rev-parse', '--verify', '--quiet', f'{ref}^{{commit}}')
    except ReleaseError:
        raise ReleaseError(f'no tag or commit {ref} in the code repo (tag a version with: kehai-run release tag)') from None
    fields: dict[str, str] = {}
    if code_git(repo, 'cat-file', '-t', ref) == 'tag':
        for line in code_git(repo, 'cat-file', 'tag', ref).splitlines():
            key, sep, value = line.partition(': ')
            if sep and key in ('code', 'art', 'unity'):
                fields[key] = value.strip()
    if fields.get('code', code) != code:
        raise ReleaseError(f"{ref}'s message names code {fields['code'][:7]}, but it points at {code[:7]}")
    return TagInfo(ref, code, fields.get('art', 'none'), fields.get('unity', 'unknown'))


def lay_art(main: Path, clone: Path, art: str) -> None:
    """The art repo's files at `art`, through a scratch index (the main folder's index isn't
    touched, and LFS files should be smudged as a checkout would: not yet tried with LFS), then the
    art files that exist now but not at `art` removed."""
    with tempfile.TemporaryDirectory(prefix='kehai-art-index-') as d:
        env = {**os.environ, 'GIT_INDEX_FILE': str(Path(d) / 'index')}
        art_git(main, clone, 'read-tree', art, env=env)
        art_git(main, clone, 'checkout-index', '-a', '-f', env=env)
    for rel in art_git(main, main, 'diff', '--name-only', '--diff-filter=D', 'HEAD', art).splitlines():
        p = clone / rel
        if p.is_file():
            p.unlink()


def stray_code(clone: Path, tracked: set[str]) -> list[str]:
    """Code under Assets/!_Project that the tag doesn't have (left from the main folder)."""
    root = clone / CODE_ROOT
    if not root.is_dir():
        return []
    return [rel for p in sorted(root.rglob('*'))
            if p.is_file() and (rel := p.relative_to(clone).as_posix()).endswith(CODE_SUFFIXES) and rel not in tracked]


def strip_stray_code(clone: Path, tracked: set[str]) -> list[str]:
    removed = stray_code(clone, tracked)
    for rel in removed:
        (clone / rel).unlink()
        meta = clone / (rel + '.meta')
        if meta.is_file() and rel + '.meta' not in tracked:
            meta.unlink()
    return removed


def _tree(lister: Callable[..., str], commit: str) -> dict[str, tuple[str, str]]:
    out = {}
    for entry in lister('ls-tree', '-r', '-z', commit).split('\0'):
        if entry:
            meta, path = entry.split('\t', 1)
            mode, _, sha = meta.split()
            out[path] = (mode, sha)
    return out


def _compare(tree: dict[str, tuple[str, str]], clone: Path, hasher: Callable[..., str]) -> list[str]:
    problems, files = [], []
    for path, (mode, sha) in tree.items():
        p = clone / path
        if mode == '160000':
            continue                    # a submodule: none in these repos
        if mode == '120000':
            if not p.is_symlink():
                problems.append(f'{path}: missing (a link at the tag)')
        elif not p.is_file():
            problems.append(f'{path}: missing')
        else:
            files.append((path, sha))
    if files:
        # Hashed as git would, clean filters on, so an LFS file hashes back to its pointer.
        hashes = hasher('hash-object', '--stdin-paths', stdin=''.join(f'{path}\n' for path, _ in files)).split('\n')
        problems += [f'{path}: differs from the tag' for (path, sha), got in zip(files, hashes) if got != sha]
    return problems


@dataclass
class CheckResult:
    files: int
    removed: list[str]
    problems: list[str]

    def text(self, info: TagInfo, art: str) -> str:
        lines = [f'release check of {info.name}: code {info.code[:7]}, art {art}, Unity {info.unity}',
                 f'{self.files} files compared with the tag']
        lines += [f'removed (code the tag lacks): {r}' for r in self.removed]
        lines += [f'PROBLEM: {p}' for p in self.problems]
        lines.append('result: ' + ('the same files as at the tag' if not self.problems
                                   else f'{len(self.problems)} problems'))
        return '\n'.join(lines) + '\n'


def prepare(repo: Path, main: Path, clone: Path, info: TagInfo, use_art: bool) -> CheckResult:
    """After kehai-run's clone at the tag: the art laid, stray code removed, every file compared."""
    code_tree = _tree(lambda *a: code_git(repo, *a), info.code)
    if use_art:
        lay_art(main, clone, info.art)
    removed = strip_stray_code(clone, set(code_tree))
    git_dir = code_git(repo, 'rev-parse', '--absolute-git-dir')
    problems = _compare(code_tree, clone,
                        lambda *a, **kw: _git(f'--git-dir={git_dir}', f'--work-tree={clone}', *a, cwd=clone, **kw))
    files = len(code_tree)
    if use_art:
        art_tree = _tree(lambda *a: art_git(main, clone, *a), info.art)
        problems += _compare(art_tree, clone, lambda *a, **kw: art_git(main, clone, *a, cwd=clone, **kw))
        files += len(art_tree)
    problems += [f'{r}: code the tag lacks is still there' for r in stray_code(clone, set(code_tree))]
    return CheckResult(files, removed, problems)


def resolve(repo: Path, main: Path, ref: str, art_today: bool) -> tuple[TagInfo, bool, str]:
    """The tag's facts, whether its art is laid from the art repo, and what the stamp calls the art."""
    info = read_tag(repo, ref)
    if info.art != 'none':
        if not has_art_repo(main):
            raise ReleaseError(f'{ref} names art {info.art[:7]}, but the main folder has no {ART_GIT}')
        return info, True, info.art[:7]
    if not art_today:
        raise ReleaseError(f"{ref} has no art commit (the art has no repo until Phase 7); "
                           "add --art-today to use today's art, stamped as unversioned")
    return info, False, UNVERSIONED


def check(s, ref: str, art_today: bool = False, keep: bool = False, log=print) -> int:
    """Lay a tag into a scratch folder the way a rebuild does and compare. No Unity, so no lock."""
    from tools.run.kehai_run import CLONES, make_clone
    try:
        info, use_art, art = resolve(s.repo, s.main, ref, art_today)
    except ReleaseError as err:
        log(f'kehai-run: {err}')
        return 2
    # Under clones.noindex, as kehai-run's own clones: Spotlight skips it.
    (s.home / CLONES).mkdir(parents=True, exist_ok=True)
    clone = Path(tempfile.mkdtemp(prefix='release-check-', dir=s.home / CLONES))
    try:
        make_clone(s, clone, info.code)
        result = prepare(s.repo, s.main, clone, info, use_art)
        log(result.text(info, art).rstrip())
        if keep:
            log(f'kehai-run: kept {clone}')
        return 0 if not result.problems else 1
    except (ReleaseError, RuntimeError, OSError, subprocess.CalledProcessError) as err:
        log(f'kehai-run: {err}')
        return 1
    finally:
        if not keep:
            shutil.rmtree(clone, ignore_errors=True)


def build_args(version: str, platform: str, out: Path) -> list[str]:
    target = ['-buildTarget', 'Win64'] if platform == 'windows' else []
    return ['-noaudio', *target, '-executeMethod', 'KehaiBuild.Build', '-platform', platform,
            *channel_args(version), '-kehai-build-out', str(out)]


def default_out(version: str, platform: str) -> Path:
    channel = 'testing' if parse(version)[1] else 'prod'
    name = 'Kehai.exe' if platform == 'windows' else 'Kehai.app'
    return Path.home() / 'TokenLimit' / 'builds' / tag_name(version)[1:] / f'rebuild-{channel}-{platform}' / name


def rebuild(s, version: str, platform: str, out: Path | None = None, art_today: bool = False,
            timeout_min: float = 60.0, dry_run: bool = False, log=print) -> int:
    """Check the tag's files in a kehai-run clone, then build it there. Unity only if all match."""
    from tools.run.kehai_run import run
    try:
        name = tag_name(version)
        info, use_art, art = resolve(s.repo, s.main, name, art_today)
    except ReleaseError as err:
        log(f'kehai-run: {err}')
        return 2
    now = unity_version(s.main)
    if info.unity not in ('unknown', now):
        log(f'kehai-run: {name} was made with Unity {info.unity}; this Mac has {now}. Install that version first.')
        return 2
    out = (out or default_out(version, platform)).absolute()

    def lay(clone: Path, run_out: Path) -> dict:
        result = prepare(s.repo, s.main, clone, info, use_art)
        (run_out / 'release_check.txt').write_text(result.text(info, art))
        if result.problems:
            raise ReleaseError(f'{len(result.problems)} files differ from {name}; see release_check.txt')
        log(f'kehai-run: {result.files} files match {name}')
        return {'KEHAI_RUN_ART_COMMIT': art}

    return run(s, 'unity', build_args(version, platform, out), ref=name, timeout_min=timeout_min,
               dry_run=dry_run, log=log, prepare=lay)
