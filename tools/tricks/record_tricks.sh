#!/usr/bin/env bash
# Records the videos for the Esc menu's TRICKS tab (Kehai 0.4.1, TrickGuide.cs).
#
#   tools/tricks/record_tricks.sh [--only id,id,...] [--skip-check] [--seed 4242]
#
# 1. The trick check (TrickCheck.cs): one long shift in batch mode, silent, with a test player,
#    where each of Karen's tricks is forced in turn. It writes a replay of the shift (.krec)
#    and, beside its report, a .tsv saying when each trick started and landed.
# 2. For each trick in that .tsv, the replay renderer (tools/marketing/render_shot.sh) films
#    fourteen seconds round the moment it landed: 1280x720, 30 frames a second, following Karen
#    for the tricks she does by hand and the test player for the ones the building does.
#
# The videos go to <project>/Assets/StreamingAssets/TrickVideos/<id>.mp4. That folder is in
# .gitignore: videos are media and stay on the Mac (and travel to the Windows PC with the rest
# of the art). A build carries them along; the game shows a trick's words without its video.
#
# It needs the editor closed on that project, and takes the heavy-work lock for each Unity run,
# like every other batch job. Nothing it runs makes a sound.
#
# Environment: KEHAI_PROJECT (default ~/Developer/Kehai; point it at an APFS clone to leave the
# real project alone), KEHAI_UNITY, KEHAI_MARKETING (the lock's home), KEHAI_DATA (where that
# project's game data goes: default ~/Library/Application Support/TokenLimit/<productName>).
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PROJECT="${KEHAI_PROJECT:-$HOME/Developer/Kehai}"
UNITY="${KEHAI_UNITY:-/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity}"
WORK="${KEHAI_MARKETING:-$HOME/TokenLimit/marketing}"
LOCK="$WORK/state/heavy.lock"
ONLY=""
SEED=4242
CHECK=1

while [ $# -gt 0 ]; do
  case "$1" in
    --only) ONLY="${2:-}"; shift ;;
    --seed) SEED="${2:-4242}"; shift ;;
    --skip-check) CHECK=0 ;;
    *) echo "record_tricks: unknown argument $1" >&2; exit 1 ;;
  esac
  shift
done

[ -x "$UNITY" ] || { echo "record_tricks: no Unity editor at $UNITY" >&2; exit 3; }
[ -d "$PROJECT/Assets" ] || { echo "record_tricks: no Unity project at $PROJECT" >&2; exit 3; }
PRODUCT="$(sed -n 's/^  productName: //p' "$PROJECT/ProjectSettings/ProjectSettings.asset" | head -1)"
DATA="${KEHAI_DATA:-$HOME/Library/Application Support/TokenLimit/$PRODUCT}"
OUT="$PROJECT/Assets/StreamingAssets/TrickVideos"
mkdir -p "$OUT" "$WORK/state"

# ---- 1. the trick check ----------------------------------------------------------------------
if [ "$CHECK" = 1 ]; then
  if pgrep -f "Unity.app/Contents/MacOS/Unity" >/dev/null; then echo "record_tricks: a Unity is running; close it first" >&2; exit 3; fi
  mkdir "$LOCK" 2>/dev/null || { echo "record_tricks: another heavy job holds the lock ($(cat "$LOCK/job" 2>/dev/null))" >&2; exit 75; }
  echo $$ > "$LOCK/pid"; echo "record_tricks trick check" > "$LOCK/job"
  args=(-batchmode -nographics -projectPath "$PROJECT" -executeMethod EvalBatch.Play -kehai-trick-check -trick-check-seed "$SEED" -eval-timeout-min 60 -logFile "$DATA/trick_check.log")
  [ -n "$ONLY" ] && args+=(-trick-check-only "$ONLY")
  mkdir -p "$DATA"
  echo "record_tricks: running the trick check (about ten minutes)…"
  "$UNITY" "${args[@]}"
  code=$?
  rm -rf "$LOCK"
  [ $code -eq 0 ] || { echo "record_tricks: the trick check failed ($code); see $DATA/trick_check.log" >&2; exit 2; }
fi

TSV="$(ls -t "$DATA"/kehai_eval/trick_check_*.tsv 2>/dev/null | head -1)"
[ -n "$TSV" ] || { echo "record_tricks: no trick_check_*.tsv in $DATA/kehai_eval" >&2; exit 2; }
echo "record_tricks: filming from $TSV"

# Who the camera follows: Karen for what she does with her hands, the test player for what the
# building does to them.
subject() {
  case "$1" in
    blackout|fog|phantom_chime|camera_break|understudy) echo you ;;
    *) echo karen ;;
  esac
}

made=0; missed=""
while IFS=$'\t' read -r id started landed krec; do
  [ "$id" = "id" ] && continue
  [ -n "$ONLY" ] && case ",$ONLY," in *",$id,"*) ;; *) continue ;; esac
  if [ -z "$krec" ] || [ ! -f "$krec" ] || [ "${started%.*}" -lt 0 ] 2>/dev/null; then missed="$missed $id"; continue; fi
  # Fourteen seconds: six before it landed and eight after; from its start if it never landed.
  at="$landed"; case "$landed" in -*) at="$started" ;; esac
  from="$(awk -v a="$at" -v l="$landed" 'BEGIN { f = (l < 0) ? a - 1 : a - 6; if (f < 0) f = 0; printf "%.2f", f }')"
  to="$(awk -v f="$from" 'BEGIN { printf "%.2f", f + 14 }')"
  echo "record_tricks: $id  ${from}–${to} s  ($(subject "$id"))"
  if KEHAI_PROJECT="$PROJECT" KEHAI_LOCK_WAIT=600 "$HERE/../marketing/render_shot.sh" -krec "$krec" -from "$from" -to "$to" \
       -shot chase -subject "$(subject "$id")" -layers none -size 1280x720 -fps 30 -out "$OUT/$id.mp4"; then
    made=$((made + 1))
    rm -f "$OUT/$id.wav" "$OUT/$id.mp4.json" "$OUT/$id.json"
  else
    missed="$missed $id"
  fi
done < "$TSV"

echo "record_tricks: $made video(s) in $OUT"
[ -n "$missed" ] && echo "record_tricks: no video for:$missed (the trick didn't start in the check, or its render failed)"
exit 0
