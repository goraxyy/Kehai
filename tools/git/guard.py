#!/usr/bin/env python3
"""The public code repo refuses art before a commit (0.5.2, C7.2; docs/agents/RULES.md).

    python3 tools/git/guard.py              check what's staged (the pre-commit hook runs this)
    python3 tools/git/guard.py PATH...      check these paths
    tools/git/install-hooks.sh              make git run it before every commit in this checkout

The repo is code and docs only: C# and shader code with their .meta files, Markdown and the
tools' code. Models, materials, textures, audio, prefabs, scenes, Packages/, ProjectSettings/,
.env files and keys stay out. A refused commit lists each path and why; nothing is unstaged.
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import PurePosixPath

# Kinds of file that are art or Unity data, never code (lower case, with the dot).
ART = {
    '.png', '.jpg', '.jpeg', '.tga', '.psd', '.exr', '.hdr', '.tif', '.tiff', '.bmp', '.gif', '.webp',
    '.fbx', '.obj', '.blend', '.blend1', '.dae', '.3ds', '.max', '.ma', '.mb', '.glb', '.gltf', '.usd', '.usdz',
    '.wav', '.mp3', '.ogg', '.aif', '.aiff', '.flac', '.m4a',
    '.mp4', '.mov', '.webm', '.krec',
    '.mat', '.prefab', '.unity', '.asset', '.controller', '.anim', '.overrideController', '.mask',
    '.lighting', '.terrainlayer', '.cubemap', '.flare', '.physicMaterial', '.physicsMaterial2D',
    '.ttf', '.otf', '.fontsettings', '.spriteatlas', '.spriteatlasv2', '.shadergraph', '.shadersubgraph', '.vfx',
}
FOLDERS = ('Packages/', 'ProjectSettings/', 'Library/', 'UserSettings/')
SECRETS = ('.env', '.pem', '.p12', '.key', '.keystore', '.jks')


def why(path: str) -> str | None:
    """Why `path` may not be committed to the public repo, or None if it may."""
    p = PurePosixPath(path)
    if path.startswith(FOLDERS):
        return f'{path.split("/")[0]}/ stays private (Unity settings and packages)'
    name = p.name.lower()
    if name == '.env' or name.startswith('.env.') and not name.endswith('.example') or p.suffix.lower() in SECRETS:
        return 'keys and .env files are never committed'
    # A .meta follows its asset: Foo.png.meta is art's, Foo.cs.meta is code's. A folder's .meta is fine.
    target = p.with_suffix('') if p.suffix == '.meta' else p
    suffix = target.suffix.lower()
    if suffix in {s.lower() for s in ART}:
        return f'{suffix} is art or Unity data; it belongs in the private project repo'
    return None


def staged() -> list[str]:
    out = subprocess.run(['git', 'diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z'],
                         capture_output=True, text=True, check=True).stdout
    return [p for p in out.split('\0') if p]


def main(argv: list[str]) -> int:
    paths = argv or staged()
    refused = [(p, r) for p in paths if (r := why(p))]
    if not refused:
        return 0
    print('guard: this repo is code and docs only, so this commit was refused:', file=sys.stderr)
    for p, r in refused:
        print(f'  {p}: {r}', file=sys.stderr)
    print('Unstage them (git restore --staged <path>) and commit again.', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
