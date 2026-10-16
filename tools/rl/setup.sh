#!/usr/bin/env bash
# Makes tools/rl/.venv: Python 3.10.12 and the ML-Agents trainer (README.md).
# Uses uv when it's installed (it fetches Python 3.10.12 itself), else python3.10.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
cd "$here"

# grpcio 1.48.2 has no Apple Silicon wheel and is built here: against macOS's own zlib (its
# bundled copy no longer compiles with the current SDK) and with a setuptools that still has
# pkg_resources (build-constraints.txt).
export GRPC_PYTHON_BUILD_SYSTEM_ZLIB=1

if command -v uv >/dev/null 2>&1; then
  uv venv --python 3.10.12 --allow-existing .venv
  uv pip install --python .venv/bin/python -r requirements.txt --build-constraint build-constraints.txt
elif command -v python3.10 >/dev/null 2>&1; then
  python3.10 -m venv .venv
  .venv/bin/python -m pip install --upgrade pip
  PIP_CONSTRAINT=build-constraints.txt .venv/bin/python -m pip install -r requirements.txt
else
  echo "Needs uv (https://docs.astral.sh/uv/) or Python 3.10.12 — see README.md." >&2
  exit 1
fi

.venv/bin/mlagents-learn --help >/dev/null
echo "Ready: $here/.venv/bin/mlagents-learn"
