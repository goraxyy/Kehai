#!/usr/bin/env bash
# C4.11: the three tests of §11 (her senses, what you know of her, the prediction model), once, on
# the test seeds (about 3 h, editor closed, your go). The planner acts on today's step; every arm
# runs beside it as a shadow, without the Director's hint. Then the write-up's numbers.
#   tools/belief/test_run.sh [out folder] [someone else's ledger for P3's personal part]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
out="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief040/test}"
other="${2:-}"
py="$root/tools/rl/.venv/bin/python"; [ -x "$py" ] || py="$HOME/Developer/Kehai/tools/rl/.venv/bin/python"
extra=(); [ -n "$other" ] && extra=(-belief-other-ledger "$other")
"$here/run_unity.sh" "$root/tools/rl/leagues/test_prediction.json" "$out" -belief-shadows p0,p1,p2,p3,p3x,s1,s2 -kehai-timing "${extra[@]}"
"$py" "$root/tools/eval/analyze_prediction.py" "$out"/kehai_eval/league_test_prediction_*.jsonl
