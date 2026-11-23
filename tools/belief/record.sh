#!/usr/bin/env bash
# C4.9: the recordings for the prediction model, her senses and the mirror map (about 3 h, editor
# closed, your go). Training and validation seeds; the three training profiles and Employee-1's
# personas; rungs C and E; the belief trace and what the player sensed of her (C1.6). Then the
# parity gate on every trace.
#   tools/belief/record.sh [out folder]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
out="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief052}"
py="$root/tools/rl/.venv/bin/python"; [ -x "$py" ] || py="$HOME/Developer/Kehai/tools/rl/.venv/bin/python"
exec python3 "$here/record.py" "$out" "$py"
