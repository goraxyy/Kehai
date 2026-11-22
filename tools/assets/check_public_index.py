#!/usr/bin/env python3
"""Keep art and private Unity project files outside the public Git index."""

import os
from pathlib import Path, PurePosixPath
import subprocess
import sys


UNITY_CODE = {".cs", ".asmdef", ".asmref", ".shader", ".compute", ".hlsl", ".cginc", ".md", ".json"}
PRIVATE_FOLDERS = {"packages", "projectsettings", ".git-assets"}
ART = {
    ".asset", ".blend", ".fbx", ".gltf", ".glb", ".hdr", ".jpeg", ".jpg", ".mat", ".mp3",
    ".obj", ".ogg", ".onnx", ".png", ".prefab", ".psd", ".tga", ".tif", ".tiff", ".unity", ".wav",
}
KEY_FILES = {".key", ".p12", ".pem", ".pfx"}


def git(repo, *args, env=None):
    return subprocess.check_output(["git", *args], cwd=repo, env=env)


def paths(data):
    return {os.fsdecode(path) for path in data.split(b"\0") if path}


def private_reason(name, public):
    path = PurePosixPath(name.casefold())
    if path.parts[0] in PRIVATE_FOLDERS:
        return "private Unity project files"
    if path.suffix in KEY_FILES or (path.name.startswith(".env") and not path.name.endswith(".example")):
        return "local settings or keys"
    if path.suffix in ART:
        return "art or Unity content"
    if path.parts[0] != "assets":
        return None
    if path.suffix in UNITY_CODE:
        return None
    if path.suffix == ".meta":
        asset = str(path.with_suffix(""))
        if asset in public and PurePosixPath(asset).suffix in UNITY_CODE:
            return None
        if any(candidate.startswith(asset + "/") and PurePosixPath(candidate).suffix in UNITY_CODE for candidate in public):
            return None
    return "Assets may only contain code, its metadata and related docs"


def check(repo):
    public = {name.casefold(): name for name in paths(git(repo, "ls-files", "-z"))}
    changed = paths(git(repo, "diff", "--cached", "--name-only", "--no-renames", "--diff-filter=ACMRT", "-z"))
    problems = []
    for name in sorted(changed):
        reason = private_reason(name, public)
        if reason:
            problems.append(f"{name!r}: {reason}")

    private = repo / ".git-assets"
    if private.is_dir():
        # Git can set an alternate index during a path-limited public commit.
        private_env = os.environ.copy()
        for key in git(repo, "rev-parse", "--local-env-vars").decode().splitlines():
            private_env.pop(key, None)
        names = paths(git(repo, f"--git-dir={private}", f"--work-tree={repo}", "ls-files", "-z", env=private_env))
        for name in sorted(names):
            if name.casefold() in public:
                problems.append(f"{name!r}: in both the public and private indexes")
    return problems


def main():
    try:
        repo = Path(os.fsdecode(git(Path.cwd(), "rev-parse", "--show-toplevel").rstrip(b"\n")))
        problems = check(repo)
    except subprocess.CalledProcessError:
        print("Public asset check could not read a Git index.", file=sys.stderr)
        return 1
    for problem in problems:
        print(problem, file=sys.stderr)
    return int(bool(problems))


if __name__ == "__main__":
    sys.exit(main())
