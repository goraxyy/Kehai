#!/usr/bin/env bash
# Records demonstrations for the behavioural cloning that starts training (README.md). Runs the
# training player with no trainer, so each learner falls back to its heuristic:
#   tools/rl/record_demos.sh [decisions]             rung E's tactic choices (the bandit's), behaviour Karen
#   tools/rl/record_demos.sh employee [decisions]    the scripted players' choices (efficient, skittish,
#                                                    reckless; never cautious), behaviour Employee (C2.5)
#   tools/rl/record_demos.sh vital|basic [decisions] the planner's (action, trick) picks, KarenVital / KarenBasic (C5.5)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
mode="karen"
case "${1:-}" in employee|vital|basic) mode="$1"; shift ;; esac
steps="${1:-2000}"
app="${KEHAI_TRAIN_APP:-$root/Builds/KehaiTrain/macOS/Kehai.app}"
exe=("$app"/Contents/MacOS/*)
[ -x "${exe[0]}" ] || { echo "No training player at $app — Kehai → RL → Build Training Player → macOS." >&2; exit 1; }

if [ "$mode" = "employee" ]; then
  mkdir -p "$here/demos/employee"
  "${exe[0]}" -batchmode -nographics -logFile "$here/demos/employee/record.log" \
    -kehai-ablation -ablation-rungs E -ablation-profiles efficient,skittish,reckless -ablation-interleave \
    -ablation-careers 1000 -ablation-shifts 6 -ablation-start 1 -ablation-seconds 150 -kehai-no-records \
    -bot-mind trainer -bot-record-demos "$here/demos/employee" -bot-demo-steps "$steps" >/dev/null
  ls -l "$here"/demos/employee/*.demo
  exit 0
fi
if [ "$mode" = "vital" ] || [ "$mode" = "basic" ]; then
  mkdir -p "$here/demos/$mode"
  extra=(); [ "$mode" = "vital" ] && extra=(-karen-hunter on)
  "${exe[0]}" -batchmode -nographics -logFile "$here/demos/$mode/record.log" \
    -kehai-ablation -ablation-rungs E -ablation-interleave -ablation-careers 1000 \
    -ablation-shifts 6 -ablation-start 1 -ablation-seconds 150 -kehai-no-records \
    -karen-brain "$mode" -karen-record-demos "$here/demos/$mode" -karen-demo-steps "$steps" "${extra[@]}" >/dev/null
  ls -l "$here"/demos/$mode/*.demo
  exit 0
fi

mkdir -p "$here/demos"
"${exe[0]}" -batchmode -nographics -logFile "$here/demos/record.log" \
  -kehai-ablation -ablation-rungs F -ablation-interleave -ablation-careers 1000 \
  -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records \
  -karen-record-demos "$here/demos" -karen-demo-steps "$steps" >/dev/null
ls -l "$here"/demos/*.demo
