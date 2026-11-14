#!/usr/bin/env bash
# C4.9: the recordings for the prediction model, her senses and the mirror map (about 3 h, editor
# closed, your go). Training and validation seeds; the three training profiles and Employee-1's
# personas; rungs C and E; the belief trace and what the player sensed of her (C1.6). Then the
# parity gate on every trace.
#   tools/belief/record.sh [out folder]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
out="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief040}"
py="$root/tools/rl/.venv/bin/python"; [ -x "$py" ] || py="$HOME/Developer/Kehai/tools/rl/.venv/bin/python"
"$here/run_unity.sh" "$root/tools/rl/leagues/record_train.json" "$out/train" -belief-trace "$out/train/traces" -kehai-timing
"$here/run_unity.sh" "$root/tools/rl/leagues/record_val.json" "$out/val" -belief-trace "$out/val/traces"
"$py" "$here/replay.py" "$out/train/traces" --parity
"$py" "$here/replay.py" "$out/val/traces" --parity
"$py" "$root/tools/learn/legs.py" "$out/train/shift_records" --out "$out/train/legs.jsonl"
echo "Recordings in $out. The old 480 MB dataset (kehai_eval/belief_data) goes only with your OK (Q5)."
