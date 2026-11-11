#!/usr/bin/env bash
# Runs one batch league on a verify clone of the owner's project (docs/project/HANDOVER.md §6),
# holding the heavy-work lock, silent, and cleans up after. The editor must be closed.
#   tools/belief/run_unity.sh <league.json> <output folder> [more game flags…]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"; root="$(cd "$here/../.." && pwd)"
league="$1"; out="$2"; shift 2
unity=/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity
lock=~/TokenLimit/marketing/state/heavy.lock
clone="${KEHAI_CLONE:-/private/tmp/kehai-verify-run}"
if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then echo "Unity is open: close the editor first." >&2; exit 1; fi
mkdir "$lock" 2>/dev/null || { echo "the heavy-work lock is held: $(cat $lock/job 2>/dev/null)" >&2; exit 1; }
echo $$ > "$lock/pid"; echo "kehai 0.4.0: $(basename "$league")" > "$lock/job"
cleanup() {
  for k in LastUsedProjectPath kProjectBasePath kWorkspacePath; do defaults write com.unity3d.UnityEditor5.x "$k" "$HOME/Developer/Kehai" 2>/dev/null || true; done
  rm -rf "$clone" "$HOME/Library/Application Support/TokenLimit/Kehai-verify"; rm -f "$HOME/Library/Preferences/unity.TokenLimit.Kehai-verify.plist"; rm -rf "$lock"
}
trap cleanup EXIT
rm -rf "$clone"; cp -Rc ~/Developer/Kehai "$clone"; rm -rf "$clone/Temp" "$clone/.claude/worktrees"
(cd "$root" && git archive HEAD) | tar -x -C "$clone"
sed -i '' 's/^  productName: .*/  productName: Kehai-verify/' "$clone/ProjectSettings/ProjectSettings.asset"
mkdir -p "$out"
"$unity" -batchmode -nographics -projectPath "$clone" -executeMethod EvalBatch.Play -league "$clone/${league#$root/}" \
  -eval-timeout-min "${KEHAI_TIMEOUT_MIN:-600}" -logFile "$out/unity.log" "$@"
data="$HOME/Library/Application Support/TokenLimit/Kehai-verify"
[ -d "$data/shift_records" ] && cp -R "$data/shift_records" "$out/"
[ -d "$data/kehai_eval" ] && cp -R "$data/kehai_eval" "$out/"
echo "done: $out"
