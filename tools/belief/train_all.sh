#!/bin/bash
# Fits m1 and trains m2–m4, in two tracks side by side (about 5 h on the M2, CPU only).
#
#   python with_lock.py belief-training bash train_all.sh
#
# A finished step is skipped on a rerun, so after a restart run this again and it carries on from
# the step that was cut off. Progress: out/track_A.log, out/track_B.log.
set -uo pipefail
cd "$(dirname "$0")" || exit 1

PY="${PY:-$HOME/Developer/Kehai/tools/rl/.venv/bin/python}"
D="${D:-$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief_data}"
COMMON=(--train "$D/train" --val "$D/val" --threads 3)
mkdir -p out

step() {   # step <the file it leaves in out/> <python arguments…>
  local finished="$1"
  shift
  if [ -f "out/$finished" ]; then
    echo "[skip] out/$finished is there"
    return 0
  fi
  echo "[$(date '+%H:%M:%S')] $1 $2 $3"
  "$PY" "$@"
}

track_a() {
  step m1.json tune_hand.py "${COMMON[@]}" --epochs 4 --hours 2 &&
    step m2_stage1.json train.py stage1 m2 "${COMMON[@]}" --epochs 1 --lr 1e-2 &&
    step m2_stage2.json train.py stage2 m2 "${COMMON[@]}" --epochs 5 --lr 3e-3 --hours 2 &&
    step m3_stage1.json train.py stage1 m3 "${COMMON[@]}" --epochs 1 &&
    step m3_stage2.json train.py stage2 m3 "${COMMON[@]}" --epochs 5 --hours 3
}

track_b() {
  # Stage 1 costs 47 s a shift for m4, hence fewer shifts and sparser rollouts.
  step m4_stage1.json train.py stage1 m4 "${COMMON[@]}" --epochs 1 --limit 108 --start-every 5 &&
    step m4_stage2.json train.py stage2 m4 "${COMMON[@]}" --epochs 4 --hours 4.5
}

track_a >> out/track_A.log 2>&1 &
a=$!
track_b >> out/track_B.log 2>&1 &
b=$!
wait "$a"
code_a=$?
wait "$b"
code_b=$?
echo "track A exit $code_a, track B exit $code_b"
[ "$code_a" -eq 0 ] && [ "$code_b" -eq 0 ]
