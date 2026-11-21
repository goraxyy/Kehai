#!/usr/bin/env bash
# Trains one side of the league with ONE copy: the editor itself, in batch mode, on a verify clone
# (docs/agents/HANDOVER.md §6), holding the heavy-work lock. The editor must be closed. Use this
# while the training player doesn't connect (12 November: the built player floods TextMeshPro
# NullReferenceExceptions on the null graphics device and never reaches the trainer).
#   tools/rl/train_editor.sh <run-id> <config.yaml> <league.json> [game flags…] [-- mlagents-learn flags…]
# e.g.  tools/rl/train_editor.sh basic_r2_a tools/rl/karen_basic.yaml tools/rl/leagues/basic_r2.json -karen-set catchDial=0.15
# Long: your go each time. Results go to tools/rl/results/<run-id> in this checkout.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
run="${1:?run-id}"; config="${2:?config}"; league="${3:?league.json}"; shift 3
game=(); learn=()
while [ $# -gt 0 ]; do if [ "$1" = "--" ]; then shift; learn=("$@"); break; fi; game+=("$1"); shift; done
unity=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
lock=~/TokenLimit/marketing/state/heavy.lock
clone="${KEHAI_CLONE:-/private/tmp/kehai-verify-train}"
venv="$here/.venv"; [ -x "$venv/bin/mlagents-learn" ] || venv="$HOME/Developer/Kehai/tools/rl/.venv"
if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then echo "Unity is open: close the editor first." >&2; exit 1; fi
# The shared heavy-work lock: a dead job's lock is taken over (0.5.2, C1.1).
python3 "$(cd "$(dirname "$0")/../.." && pwd)/tools/run/lock.py" take "kehai 0.4.0 training: $run" --pid $$ --lock "$lock" || exit 75
prefs_before=$(for k in LastUsedProjectPath kProjectBasePath kWorkspacePath; do printf "%s=%s\n" "$k" "$(defaults read com.unity3d.UnityEditor5.x "$k" 2>/dev/null || echo __unset__)"; done)
cleanup() {
  [ -n "${up:-}" ] && kill "$up" 2>/dev/null || true
  # The editor prefs back to what they were (not to a fixed path: 0.5.2, C1.4).
  while IFS="=" read -r k v; do [ -z "$k" ] && continue; if [ "$v" = "__unset__" ]; then defaults delete com.unity3d.UnityEditor5.x "$k" 2>/dev/null || true; else defaults write com.unity3d.UnityEditor5.x "$k" "$v" 2>/dev/null || true; fi; done <<< "$prefs_before"
  rm -rf "$clone" "$HOME/Library/Application Support/TokenLimit/Kehai-verify"; rm -f "$HOME/Library/Preferences/unity.TokenLimit.Kehai-verify.plist"; python3 "$(cd "$(dirname "$0")/../.." && pwd)/tools/run/lock.py" release --pid $$ --lock "$lock"
}
trap cleanup EXIT
rm -rf "$clone"; cp -Rc ~/Developer/Kehai "$clone"; rm -rf "$clone/Temp" "$clone/.claude/worktrees"
(cd "$root" && git archive HEAD) | tar -x -C "$clone"
sed -i '' 's/^  productName: .*/  productName: Kehai-verify/' "$clone/ProjectSettings/ProjectSettings.asset"
mkdir -p "$here/results"
(cd "$here" && "$venv/bin/mlagents-learn" "$config" --run-id "$run" --results-dir "$here/results" --timeout-wait 900 "${learn[@]}") &
trainer=$!
sleep 20
"$unity" -batchmode -nographics -projectPath "$clone" -executeMethod EvalBatch.Play -league "$clone/${league#$root/}" \
  -kehai-no-records -logFile "$here/results/$run.unity.log" "${game[@]}" &
up=$!
wait "$trainer"
