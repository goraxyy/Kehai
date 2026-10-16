#!/usr/bin/env bash
# Trains rung F's tactic chooser with PPO (README.md).
#   tools/rl/train.sh <run-id> [envs] [config.yaml] [more mlagents-learn flags, e.g. --resume]
# Each env is one copy of the game in batch mode (silent, no window). The editor must be
# closed on an 8 GB Mac; 2 envs is the most it has room for.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
run="${1:?usage: train.sh <run-id> [envs] [config.yaml] [mlagents-learn flags]}"
envs="${2:-2}"
config="${3:-$here/karen_ppo.yaml}"
shift $(( $# < 3 ? $# : 3 ))
player="${KEHAI_TRAIN_PLAYER:-$root/Builds/KehaiTrain/macOS/Kehai}"

cd "$here"   # the configs' demo_path is relative to tools/rl
exec "$here/.venv/bin/mlagents-learn" "$config" --run-id "$run" --env "$player" --num-envs "$envs" \
  --no-graphics --timeout-wait 180 --results-dir "$here/results" "$@" \
  --env-args -kehai-ablation -ablation-rungs F -ablation-interleave -ablation-careers 100000 \
             -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records
