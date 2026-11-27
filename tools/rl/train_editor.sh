#!/usr/bin/env bash
# Trains one side of the league with ONE copy: the editor itself, in batch mode, through
# kehai-run (0.5.2, C1.4): a safe clone, the heavy-work lock, the editor's prefs put back, and
# the trainer started 20 s before Unity; when either ends, the other is stopped. The editor must
# be closed. Use this while the training player doesn't connect.
#   tools/rl/train_editor.sh [--dry-run] <run-id> <config.yaml> <league.json> [game flags…] [-- mlagents-learn flags…]
# e.g.  tools/rl/train_editor.sh basic_r2_a tools/rl/karen_basic.yaml tools/rl/leagues/basic_r2.json -karen-set catchDial=0.15
# Long: your go each time. Results go to tools/rl/results/<run-id> in this checkout; Unity's and
# the trainer's logs to the run's folder under ~/TokenLimit/runs/out/.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
dry=()
if [ "${1:-}" = "--dry-run" ]; then dry=(--dry-run); shift; fi
run="${1:?run-id}"; config="${2:?config}"; league="${3:?league.json}"; shift 3
game=(); learn=()
while [ $# -gt 0 ]; do if [ "$1" = "--" ]; then shift; learn=("$@"); break; fi; game+=("$1"); shift; done
learn_args=""
[ ${#learn[@]} -gt 0 ] && learn_args="$(printf '%q ' "${learn[@]}")"
# Options before the three names: everything after them goes to Unity. ${a[@]+"${a[@]}"}: macOS's
# bash 3.2 calls an empty array unbound under set -u.
exec env KEHAI_RUN_REPO="$root" python3 "$root/tools/run/kehai_run.py" train ${dry[@]+"${dry[@]}"} "--learn-args=$learn_args" \
  "$run" "$config" "${league#$root/}" -- ${game[@]+"${game[@]}"}
