#!/bin/bash
# The held-out test and the closed loop (docs/KAREN_RESULTS.md), in batch Unity on an APFS clone of
# the project with this branch's committed state on top, so the main folder and its scene are
# left alone. Run after train.py select and export_cs.py, with the weights committed.
#
#   python with_lock.py belief-test bash test_run.sh prepare gate test closed finish
#
#   prepare   make the clone and lay the branch on it
#   gate      EditMode tests on the clone, including the C# parity gate
#   test      12 processes: rungs C–E × 4 players × 3 seeds, with every shadow filter
#   closed    12 processes: rung C with Karen acting on the best model
#   finish    parity on the test traces, pack them, the horizon curve, the analysis → out/results.md
#   cleanup   delete the clone and its save data, and point the editor back at the project
#
# A finished process is skipped on a rerun. One that died is cleared and played again, once.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(git -C "$HERE" rev-parse --show-toplevel)"
MAIN="${KEHAI_MAIN:-$HOME/Developer/Kehai}"
CLONE="${KEHAI_CLONE:-$HOME/TokenLimit/kehai-verify-clone}"
UNITY=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
PY="${PY:-$MAIN/tools/rl/.venv/bin/python}"
EV="$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval"
SEEDS=(1000 1100 1200)
PROFILES=(efficient skittish reckless cautious)
OUT="$HERE/out"
failures=0

say() { echo "[$(date '+%H:%M:%S')] $*"; }

best_model() {
  "$PY" -c 'import sys, numpy as np; print(np.load(sys.argv[1])["best"])' "$OUT/weights.npz"
}

# Opening a clone repoints the editor-wide prefs at it.
restore_editor_prefs() {
  for key in LastUsedProjectPath kProjectBasePath kWorkspacePath; do
    defaults write com.unity3d.UnityEditor5.x "$key" "$MAIN"
  done
}

prepare() {
  if [ -n "$(git -C "$ROOT" status --porcelain -- Assets)" ]; then
    say "commit the scripts first: the clone gets this branch's committed state"
    return 1
  fi
  if [ ! -d "$CLONE" ]; then
    say "cloning the project to $CLONE"
    cp -Rc "$MAIN" "$CLONE" || return 1
  fi
  rm -rf "${CLONE:?}/Temp" "${CLONE:?}/.claude" "${CLONE:?}/.git" "${CLONE:?}/Builds"
  git -C "$ROOT" archive HEAD | tar -x -C "$CLONE" || return 1
  sed -i '' 's/^  productName: .*/  productName: Kehai-verify/' "$CLONE/ProjectSettings/ProjectSettings.asset"
  grep -q 'productName: Kehai-verify' "$CLONE/ProjectSettings/ProjectSettings.asset" || return 1
  say "clone ready at commit $(git -C "$ROOT" rev-parse --short HEAD); $(df -h "$HOME" | tail -1 | awk '{print $4}') free"
}

gate() {
  say "EditMode tests on the clone"
  "$UNITY" -batchmode -nographics -projectPath "$CLONE" -runTests -testPlatform EditMode \
    -testFilter '^(MotionModelTests|ShiftRecordTests|KarenRuleTests)\.' \
    -testResults "$OUT/gate.xml" -logFile "$OUT/gate.log"
  local code=$?
  restore_editor_prefs
  "$PY" - "$OUT/gate.xml" <<'EOF'
import sys
import xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
print(f"{run.get('passed')} passed, {run.get('failed')} failed of {run.get('total')}")
for case in run.iter("test-case"):
    if case.get("result") != "Passed":
        print(" ", case.get("result"), case.get("fullname"))
EOF
  [ "$code" -eq 0 ]
}

# play <test|closed> <seed> <profile> <rungs> <extra flags…>
play() {
  local kind="$1" seed="$2" profile="$3" rungs="$4"
  shift 4
  local dir="$EV/belief_runs/$kind/${profile}_$seed"
  local traces="$EV/belief_traces/$kind/${profile}_$seed"
  local expect=$((${#rungs} * 4))
  if [ -f "$dir/finished" ]; then
    say "[skip] $kind $profile $seed"
    return 0
  fi
  local attempt rows flags
  for attempt in 1 2; do
    rm -rf "${dir:?}" "${traces:?}"
    mkdir -p "$dir"
    flags=("$@")
    [ "$kind" = test ] && mkdir -p "$traces" && flags+=(-belief-trace "$traces")
    say "$kind $profile $seed, attempt $attempt"
    "$UNITY" -batchmode -nographics -projectPath "$CLONE" -executeMethod EvalBatch.Play -kehai-ablation \
      -ablation-rungs "$rungs" -ablation-profiles "$profile" -ablation-careers 1 -ablation-seed "$seed" \
      -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records -eval-timeout-min 60 \
      -ablation-out "$dir" -logFile "$dir/unity.log" "${flags[@]}"
    rows=$(cat "$dir"/ablation_*.jsonl 2>/dev/null | wc -l | tr -d ' ')
    say "$kind $profile $seed: $rows of $expect shifts"
    if [ "$rows" -eq "$expect" ]; then
      echo "$attempt" > "$dir/finished"
      return 0
    fi
  done
  failures=$((failures + 1))
  return 1
}

test_run() {
  local best
  best="$(best_model)" || return 1
  say "test run: shadows m0,m1,m2,m3,m4,$best/8"
  for seed in "${SEEDS[@]}"; do
    for profile in "${PROFILES[@]}"; do
      play test "$seed" "$profile" CDE -belief-shadows "m0,m1,m2,m3,m4,$best/8"
    done
  done
  restore_editor_prefs
  [ "$failures" -eq 0 ]
}

closed_loop() {
  local best
  best="$(best_model)" || return 1
  say "closed loop: rung C with Karen acting on $best"
  for seed in "${SEEDS[@]}"; do
    for profile in "${PROFILES[@]}"; do
      play closed "$seed" "$profile" C -belief-motion "$best"
    done
  done
  restore_editor_prefs
  [ "$failures" -eq 0 ]
}

finish() {
  local best folder
  best="$(best_model)" || return 1
  cd "$HERE" || return 1
  say "parity on the test traces"
  "$PY" replay.py parity "$EV/belief_traces/test"/*/ --weights "$OUT/weights.npz" > "$OUT/test_parity.log" 2>&1
  local parity=$?
  tail -n 3 "$OUT/test_parity.log"
  [ "$parity" -eq 0 ] || return 1
  for folder in "$EV/belief_traces/test"/*/; do
    "$PY" trace.py pack "$folder" "$EV/belief_data/test" --delete > /dev/null || return 1
  done
  say "horizon curve"
  "$PY" eval_offline.py horizon --data "$EV/belief_data/test" --weights "$OUT/weights.npz" > "$OUT/test_horizon.md" 2>&1 || return 1
  say "analysis"
  cat "$EV"/belief_runs/test/*/ablation_*.jsonl "$EV"/belief_runs/closed/*/ablation_*.jsonl > "$OUT/test_joined.jsonl"
  python3 "$ROOT/tools/eval/analyze_ablation.py" "$OUT/test_joined.jsonl" --best "$best" > "$OUT/results.md" || return 1
  say "wrote $OUT/results.md"
}

cleanup() {
  rm -rf "${CLONE:?}" "$HOME/Library/Application Support/TokenLimit/Kehai-verify"
  defaults delete unity.TokenLimit.Kehai-verify 2>/dev/null
  restore_editor_prefs
  say "clone and its save data gone; the editor points at $MAIN"
}

[ $# -gt 0 ] || { sed -n '2,15p' "$0"; exit 2; }
for what in "$@"; do
  case "$what" in
    prepare) prepare ;;
    gate) gate ;;
    test) test_run ;;
    closed) closed_loop ;;
    finish) finish ;;
    cleanup) cleanup ;;
    *) say "no step called $what"; exit 2 ;;
  esac || { say "$what failed"; exit 1; }
done
