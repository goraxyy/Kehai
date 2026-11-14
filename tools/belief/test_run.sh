#!/usr/bin/env bash
# C4.11: the three tests of §11 (her senses, what you know of her, the prediction model), once, on
# the test seeds (about 3 h, editor closed, your go). The planner acts on today's step; every arm
# runs beside it as a shadow, without the Director's hint. Then the write-up's numbers.
#   tools/belief/test_run.sh <someone else's karen_ledger.json, for P3's personal part> [out folder]
# The Ledger must be a real other player's (a bot career's from the recordings will do): without
# one, "P3 with someone else's Ledger" would read an empty Ledger, which is everyone, not someone.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
other="${1:?give the path of another player karen_ledger.json (see the comment above)}"
[ -f "$other" ] || { echo "no such Ledger: $other" >&2; exit 1; }
out="${2:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief040/test}"
py="$root/tools/rl/.venv/bin/python"; [ -x "$py" ] || py="$HOME/Developer/Kehai/tools/rl/.venv/bin/python"
extra=(-belief-other-ledger "$other")
"$here/run_unity.sh" "$root/tools/rl/leagues/test_prediction.json" "$out" -belief-shadows p0,p1,p2,p3,p3x,s1,s2 -kehai-timing "${extra[@]}"
"$py" "$root/tools/eval/analyze_prediction.py" "$out"/kehai_eval/league_test_prediction_*.jsonl
