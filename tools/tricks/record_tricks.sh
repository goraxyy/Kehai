#!/usr/bin/env bash
# Records the videos for the Esc menu's TRICKS tab (Kehai 0.4.1, TrickGuide.cs).
#
#   tools/tricks/record_tricks.sh [--only id,id,...] [--skip-check] [--seed 4242] [--dry-run]
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
# Environment: KEHAI_PROJECT (source project, renderer and video destination), KEHAI_UNITY,
# KEHAI_MARKETING (the lock's home), KEHAI_RUN_HOME (retained checks), KEHAI_DATA (optional
# explicit data folder for --skip-check).
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
PROJECT="${KEHAI_PROJECT:-$HOME/Developer/Kehai}"
UNITY="${KEHAI_UNITY:-/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity}"
WORK="${KEHAI_MARKETING:-$HOME/TokenLimit/marketing}"
LOCK="$WORK/state/heavy.lock"
RUN_HOME="${KEHAI_RUN_HOME:-$HOME/TokenLimit/runs}"
ONLY=""
SEED=4242
CHECK=1
DRY=0

while [ $# -gt 0 ]; do
  case "$1" in
    --only) ONLY="${2:-}"; shift ;;
    --seed) SEED="${2:-4242}"; shift ;;
    --skip-check) CHECK=0 ;;
    --dry-run) DRY=1 ;;
    *) echo "record_tricks: unknown argument $1" >&2; exit 1 ;;
  esac
  shift
done

if [ "$DRY" = 0 ]; then
  [ -x "$UNITY" ] || { echo "record_tricks: no Unity editor at $UNITY" >&2; exit 3; }
  [ -d "$PROJECT/Assets" ] || { echo "record_tricks: no Unity project at $PROJECT" >&2; exit 3; }
fi
OUT="$PROJECT/Assets/StreamingAssets/TrickVideos"

# ---- 1. the trick check ----------------------------------------------------------------------
if [ "$CHECK" = 1 ]; then
  args=(-executeMethod EvalBatch.Play -kehai-trick-check -trick-check-seed "$SEED" -eval-timeout-min 60)
  [ -n "$ONLY" ] && args+=(-trick-check-only "$ONLY")
  run_check() {
    KEHAI_RUN_MAIN="$PROJECT" KEHAI_RUN_UNITY="$UNITY" KEHAI_RUN_LOCK="$LOCK" KEHAI_RUN_HOME="$RUN_HOME" \
      python3 "$HERE/../run/kehai_run.py" unity --timeout-min 65 "$@" -- "${args[@]}"
  }
  if [ "$DRY" = 1 ]; then
    run_check --dry-run
    exit $?
  fi
  receipt="$(mktemp "${TMPDIR:-/tmp}/kehai-trick-check.XXXXXX")" || exit 2
  trap 'rm -f "$receipt"' EXIT
  echo "record_tricks: running the trick check (about ten minutes)…"
  run_check --result-dir-file "$receipt"
  code=$?
  retained="$(cat "$receipt")"
  rm -f "$receipt"
  trap - EXIT
  if [ "$code" != 0 ]; then
    if [ -z "$retained" ] && { [ "$code" = 3 ] || [ "$code" = 75 ]; }; then exit "$code"; fi
    echo "record_tricks: the trick check failed ($code)${retained:+; see $retained/unity.log}" >&2
    exit 2
  fi
  [ -n "$retained" ] || { echo "record_tricks: the runner returned no output folder" >&2; exit 2; }
  reports=("$retained"/kehai_eval/trick_check_*.tsv)
elif [ -n "${KEHAI_DATA:-}" ]; then
  reports=("$KEHAI_DATA"/kehai_eval/trick_check_*.tsv)
else
  PRODUCT="$(sed -n 's/^  productName: //p' "$PROJECT/ProjectSettings/ProjectSettings.asset" | head -1)"
  legacy="$HOME/Library/Application Support/TokenLimit/$PRODUCT"
  reports=("$RUN_HOME"/out/*/kehai_eval/trick_check_*.tsv "$legacy"/kehai_eval/trick_check_*.tsv)
fi

TSV="$(ls -t "${reports[@]}" 2>/dev/null | head -1)"
[ -n "$TSV" ] || { echo "record_tricks: no trick_check_*.tsv found" >&2; exit 2; }
echo "record_tricks: filming from $TSV"
if [ "$DRY" = 0 ]; then mkdir -p "$OUT" || exit 2; fi

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
  # A retained TSV still names the replay from the removed clone's data directory.
  replay="${TSV%/kehai_eval/*}/shift_records/${krec##*[\\/]}"
  if [ -n "$krec" ] && [ -f "$replay" ]; then krec="$replay"; fi
  if [ -z "$krec" ] || [ ! -f "$krec" ] || [ "${started%.*}" -lt 0 ] 2>/dev/null; then missed="$missed $id"; continue; fi
  # Fourteen seconds: six before it landed and eight after; from its start if it never landed.
  at="$landed"; case "$landed" in -*) at="$started" ;; esac
  from="$(awk -v a="$at" -v l="$landed" 'BEGIN { f = (l < 0) ? a - 1 : a - 6; if (f < 0) f = 0; printf "%.2f", f }')"
  to="$(awk -v f="$from" 'BEGIN { printf "%.2f", f + 14 }')"
  echo "record_tricks: $id  ${from}–${to} s  ($(subject "$id"))"
  render=("$HERE/../marketing/render_shot.sh" -krec "$krec" -from "$from" -to "$to"
          -shot chase -subject "$(subject "$id")" -layers none -size 1280x720 -fps 30 -out "$OUT/$id.mp4")
  if [ "$DRY" = 1 ]; then
    printf 'KEHAI_PROJECT=%q KEHAI_LOCK_WAIT=600' "$PROJECT"
    printf ' %q' "${render[@]}"
    printf '\n'
  elif KEHAI_PROJECT="$PROJECT" KEHAI_LOCK_WAIT=600 "${render[@]}"; then
    made=$((made + 1))
    rm -f "$OUT/$id.wav" "$OUT/$id.mp4.json" "$OUT/$id.json"
  else
    missed="$missed $id"
  fi
done < "$TSV"

[ "$DRY" = 1 ] && exit 0
echo "record_tricks: $made video(s) in $OUT"
[ -n "$missed" ] && echo "record_tricks: no video for:$missed (the trick didn't start in the check, or its render failed)"
exit 0
