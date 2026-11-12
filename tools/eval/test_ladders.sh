#!/usr/bin/env bash
# C6.2: both ladders of §11, once, on the test seeds (about two nights, editor closed, your go).
# The catch ladder (pacing off in every arm): the hunter, Vital (each trained run), and for
# reference the planner and Basic with pacing on. The fear ladder (pacing on): C, E, F (Basic),
# F with the Ledger wiped every shift, F reading someone else's Ledger.
#   tools/eval/test_ladders.sh [out folder]
# Vital's and Basic's second runs (D12) are judged by re-exporting each and running its arm again
# with KEHAI_ARM_SUFFIX=_b.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
out="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/test040}"
for arm in catch_hunter catch_vital ref_planner fear_C fear_E fear_F fear_F_wiped fear_F_other; do
  "$root/tools/belief/run_unity.sh" "$root/tools/rl/leagues/test_$arm.json" "$out/$arm${KEHAI_ARM_SUFFIX:-}" -karen-learning on
done
python3 "$here/analyze_ladders.py" "$out"
