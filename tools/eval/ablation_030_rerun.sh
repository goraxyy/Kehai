#!/usr/bin/env bash
# C0.1's gated half: 0.3.0's last ablation, run once (your go; about 1.5 h, editor closed). It is
# also 0.4.0's baseline. The same arguments as 0.3.0's (KAREN_RESULTS.md): rungs C and E, four
# players with the cautious one held out of tuning, 3 careers of shifts 1–10, 150 s shifts, seed 1000.
#   tools/eval/ablation_030_rerun.sh [out folder]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
out="${1:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/ablation030_rerun}"
KEHAI_TIMEOUT_MIN=240 "$root/tools/belief/run_unity.sh" --ablation "$out" -ablation-rungs CE \
  -ablation-profiles efficient,skittish,reckless,cautious -ablation-careers 3 -ablation-shifts 10 -ablation-start 1 \
  -ablation-seconds 150 -ablation-seed 1000 -kehai-timing
python3 "$root/tools/eval/analyze_ablation.py" "$out"/kehai_eval/ablation_*.jsonl
