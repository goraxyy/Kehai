#!/usr/bin/env bash
# The warm starts' demonstrations through the batch editor (one copy) on a verify clone, while the
# training player doesn't connect. Same recordings as record_demos.sh; the editor must be closed.
#   tools/rl/record_demos_editor.sh employee|vital|basic [decisions]
# Writes tools/rl/demos/<employee|vital|basic>/*.demo in this checkout.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
mode="${1:?employee, vital or basic}"; steps="${2:-20000}"
unity=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
lock=~/TokenLimit/marketing/state/heavy.lock
clone="${KEHAI_CLONE:-/private/tmp/kehai-verify-demos}"
if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then echo "Unity is open: close the editor first." >&2; exit 1; fi
# The shared heavy-work lock: a dead job's lock is taken over (0.5.2, C1.1).
python3 "$(cd "$(dirname "$0")/../.." && pwd)/tools/run/lock.py" take "kehai 0.4.0 demos: $mode" --pid $$ --lock "$lock" || exit 75
prefs_before=$(for k in LastUsedProjectPath kProjectBasePath kWorkspacePath; do printf "%s=%s\n" "$k" "$(defaults read com.unity3d.UnityEditor5.x "$k" 2>/dev/null || echo __unset__)"; done)
cleanup() {
  # The editor prefs back to what they were (not to a fixed path: 0.5.2, C1.4).
  while IFS="=" read -r k v; do [ -z "$k" ] && continue; if [ "$v" = "__unset__" ]; then defaults delete com.unity3d.UnityEditor5.x "$k" 2>/dev/null || true; else defaults write com.unity3d.UnityEditor5.x "$k" "$v" 2>/dev/null || true; fi; done <<< "$prefs_before"
  rm -rf "$clone" "$HOME/Library/Application Support/TokenLimit/Kehai-verify"; rm -f "$HOME/Library/Preferences/unity.TokenLimit.Kehai-verify.plist"; python3 "$(cd "$(dirname "$0")/../.." && pwd)/tools/run/lock.py" release --pid $$ --lock "$lock"
}
trap cleanup EXIT
rm -rf "$clone"; cp -Rc ~/Developer/Kehai "$clone"; rm -rf "$clone/Temp" "$clone/.claude/worktrees"
(cd "$root" && git archive HEAD) | tar -x -C "$clone"
sed -i '' 's/^  productName: .*/  productName: Kehai-verify/' "$clone/ProjectSettings/ProjectSettings.asset"
out="$here/demos/$mode"; mkdir -p "$out"
common=(-batchmode -nographics -projectPath "$clone" -executeMethod EvalBatch.Play -kehai-ablation -ablation-interleave
        -ablation-careers 1000 -ablation-shifts 6 -ablation-start 1 -ablation-seconds 150 -kehai-no-records -logFile "$out/record.log")
case "$mode" in
  employee) "$unity" "${common[@]}" -ablation-rungs E -ablation-profiles efficient,skittish,reckless \
              -bot-mind trainer -bot-record-demos "$out" -bot-demo-steps "$steps" ;;
  vital)    "$unity" "${common[@]}" -ablation-rungs E -karen-brain vital -karen-record-demos "$out" -karen-demo-steps "$steps" ;;
  basic)    "$unity" "${common[@]}" -ablation-rungs E -karen-brain basic -karen-record-demos "$out" -karen-demo-steps "$steps" ;;
  *) echo "employee, vital or basic" >&2; exit 1 ;;
esac
ls -l "$out"/*.demo
