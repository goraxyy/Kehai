#!/usr/bin/env bash
# Renders one shot of a recorded shift with Unity, unattended (BUILD_PLAN.md, Phase 4).
#
#   tools/marketing/render_shot.sh -krec <file.krec> [-moment 1 | -from <s> -to <s>]
#       [-shot pov|cctv|chase|orbit|topdown|path|<path.json>] [-subject karen|you]
#       [-layers belief,guess,cone,sound,thoughts,actors|all|none] [-alpha] [-dof]
#       [-size 1080x1920] [-fps 60] -out <file.mp4|file.webm|file.mov|folder/> [--dry-run]
#
# The flags go to Kehai.Replay.ReplayRender as they are (ReplayRender.cs explains each one).
# Before Unity starts, this checks that no editor has the project open and takes the heavy-job
# lock, so one Unity render, Remotion render or TTS job runs at a time. --dry-run does the
# checks, has Unity check the shot too, and renders nothing.
#
# Exit codes: 0 done · 1 bad arguments · 2 the render failed · 3 can't render here (the editor
# is open, no ffmpeg, no graphics) · 75 another heavy job holds the lock (try again later).
# ffmpeg: the one Remotion installs with the editor (tools/marketing/editor) is enough.
#
# Environment: KEHAI_PROJECT (default ~/Developer/Kehai), KEHAI_UNITY (the editor binary),
# KEHAI_MARKETING (default ~/TokenLimit/marketing: the lock is in state/, logs in logs/),
# KEHAI_LOCK_WAIT (seconds to wait for the lock; default 0).
set -uo pipefail

PROJECT="${KEHAI_PROJECT:-$HOME/Developer/Kehai}"
UNITY="${KEHAI_UNITY:-/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity}"
WORK="${KEHAI_MARKETING:-$HOME/TokenLimit/marketing}"
LOCK="$WORK/state/heavy.lock"
LOGS="$WORK/logs"
WAIT="${KEHAI_LOCK_WAIT:-0}"

fail() { echo "render_shot: $2" >&2; exit "$1"; }

absolute() { case "$1" in /*) printf '%s' "$1" ;; *) printf '%s/%s' "$PWD" "$1" ;; esac; }

# ---- the arguments ---------------------------------------------------------------------------

dry=0
krec=""
out=""
args=()
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) dry=1 ;;
    -krec) krec="$(absolute "${2:-}")"; args+=("-krec" "$krec"); shift ;;
    -out) out="$(absolute "${2:-}")"; args+=("-out" "$out"); shift ;;
    -shot) shot="${2:-}"; case "$shot" in *.json) shot="$(absolute "$shot")" ;; esac; args+=("-shot" "$shot"); shift ;;
    *) args+=("$1") ;;
  esac
  shift
done

[ -n "$krec" ] || fail 1 "-krec <file.krec> is needed"
[ -f "$krec" ] || fail 1 "no such recording: $krec"
[ -n "$out" ] || fail 1 "-out is needed: a .mp4, .webm or .mov file, or a folder/ for PNG frames"
[ -x "$UNITY" ] || fail 3 "no Unity editor at $UNITY (set KEHAI_UNITY)"
[ -d "$PROJECT/Assets" ] || fail 3 "no Unity project at $PROJECT (set KEHAI_PROJECT)"

# An open editor holds Temp/UnityLockfile; a stale one left by a crash isn't held by anyone.
if [ -e "$PROJECT/Temp/UnityLockfile" ] && lsof -t "$PROJECT/Temp/UnityLockfile" >/dev/null 2>&1; then
  fail 3 "Unity has the project open ($PROJECT). Close the editor first: renders run only when it's closed."
fi

# ffmpeg: KEHAI_FFMPEG, the system's, or the one that comes with the editor's Remotion
# (tools/marketing/editor after npm install); Unity is told which through KEHAI_FFMPEG.
if [ -z "${KEHAI_FFMPEG:-}" ]; then
  for f in "$(command -v ffmpeg 2>/dev/null)" /opt/homebrew/bin/ffmpeg /usr/local/bin/ffmpeg "$(dirname "$0")"/editor/node_modules/@remotion/compositor-*/ffmpeg; do
    if [ -n "$f" ] && [ -x "$f" ]; then export KEHAI_FFMPEG="$f"; break; fi
  done
fi
case "$out" in
  */) ;;
  *) [ -n "${KEHAI_FFMPEG:-}" ] || fail 3 "no ffmpeg: run npm install in tools/marketing/editor (or brew install ffmpeg); a folder/ for -out renders PNG frames without it" ;;
esac

# ---- one heavy job at a time -----------------------------------------------------------------

mkdir -p "$WORK/state" "$LOGS"

take_lock() {
  if mkdir "$LOCK" 2>/dev/null; then echo $$ > "$LOCK/pid"; echo "render_shot $krec" > "$LOCK/job"; return 0; fi
  holder="$(cat "$LOCK/pid" 2>/dev/null || true)"
  if [ -n "$holder" ] && ! kill -0 "$holder" 2>/dev/null; then
    rm -rf "${LOCK:?}"   # its owner is gone
    mkdir "$LOCK" 2>/dev/null && echo $$ > "$LOCK/pid" && echo "render_shot $krec" > "$LOCK/job" && return 0
  fi
  return 1
}

waited=0
until take_lock; do
  [ "$waited" -lt "$WAIT" ] || fail 75 "another heavy job is running ($(cat "$LOCK/job" 2>/dev/null || echo unknown), pid $(cat "$LOCK/pid" 2>/dev/null || echo ?)); try again later"
  sleep 5
  waited=$((waited + 5))
done
trap 'rm -rf "${LOCK:?}"' EXIT

# ---- the render ------------------------------------------------------------------------------

log="$LOGS/render_$(date +%Y%m%d_%H%M%S).log"
[ "$dry" = 1 ] && args+=("-dry-run")
cmd=("$UNITY" -batchmode -projectPath "$PROJECT" -executeMethod Kehai.Replay.ReplayRenderBatch.Run -logFile "$log" "${args[@]}")
echo "render_shot: ${cmd[*]}"

caffeinate -i "${cmd[@]}"
code=$?
grep -E '^ReplayRender' "$log" | tail -8 || true
[ "$code" = 0 ] || echo "render_shot: Unity exited with $code; the log is $log" >&2
exit "$code"
