#!/usr/bin/env bash
# Trains one side of the league (0.4.0, build plan §10): the Employee, Vital or Basic.
#   tools/rl/train_league.sh <run-id> <envs> <config.yaml> <league.json> [game flags…] [-- mlagents-learn flags…]
# e.g.
#   tools/rl/train_league.sh employee_r1 2 tools/rl/employee_ppo.yaml tools/rl/leagues/employee_r1.json
#   tools/rl/train_league.sh vital_r2_a 2 tools/rl/karen_vital.yaml tools/rl/leagues/vital_r2.json
#   tools/rl/train_league.sh vital_r2_b 2 tools/rl/karen_vital.yaml tools/rl/leagues/vital_r2.json -league-seed-offset 5000
#   tools/rl/train_league.sh basic_r2_a 2 tools/rl/karen_basic.yaml tools/rl/leagues/basic_r2.json -karen-set catchDial=0.15
# Each env is one copy of the training player in batch mode (silent, no window). On the 8 GB Mac
# the editor must be closed, and two copies are the most it has room for. Long: your go each time.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
run="${1:?run-id}"; envs="${2:?envs}"; config="${3:?config}"; league="${4:?league.json}"; shift 4
game=(); learn=()
while [ $# -gt 0 ]; do if [ "$1" = "--" ]; then shift; learn=("$@"); break; fi; game+=("$1"); shift; done
player="${KEHAI_TRAIN_PLAYER:-$root/Builds/KehaiTrain/macOS/Kehai}"
cd "$here"
exec "$here/.venv/bin/mlagents-learn" "$config" --run-id "$run" --env "$player" --num-envs "$envs" \
  --no-graphics --timeout-wait 180 --results-dir "$here/results" "${learn[@]}" \
  --env-args -league "$(cd "$(dirname "$league")" && pwd)/$(basename "$league")" -kehai-no-records "${game[@]}"
