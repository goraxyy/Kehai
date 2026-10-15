#!/usr/bin/env bash
# Records rung F's tactic choices (the bandit's) as demonstrations, for the behavioural
# cloning that starts training (README.md). Runs the training player with no trainer, so
# rung G falls back to its heuristic, which is the bandit's pick.
#   tools/rl/record_demos.sh [decisions]        (default 2000, about five minutes)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
steps="${1:-2000}"
app="${KEHAI_TRAIN_APP:-$root/Builds/KehaiTrain/macOS/Kehai.app}"
exe=("$app"/Contents/MacOS/*)
[ -x "${exe[0]}" ] || { echo "No training player at $app — Kehai → RL → Build Training Player → macOS." >&2; exit 1; }

mkdir -p "$here/demos"
"${exe[0]}" -batchmode -nographics -logFile "$here/demos/record.log" \
  -kehai-ablation -ablation-rungs G -ablation-interleave -ablation-careers 1000 \
  -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records \
  -karen-record-demos "$here/demos" -karen-demo-steps "$steps" >/dev/null
ls -l "$here"/demos/*.demo
