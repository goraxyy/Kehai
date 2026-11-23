#!/usr/bin/env bash
# Record warm-start demonstrations in the batch editor through kehai-run.
#   tools/rl/record_demos_editor.sh [--dry-run] employee|vital|basic [decisions]
# Writes tools/rl/demos/<employee|vital|basic>/*.demo in this checkout.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
dry_run=0
if [ "${1:-}" = "--dry-run" ]; then dry_run=1; shift; fi
mode="${1:?employee, vital or basic}"; steps="${2:-20000}"
case "$mode" in
  employee|vital|basic) ;;
  *) echo "employee, vital or basic" >&2; exit 1 ;;
esac
options=(--mode "$mode" --steps="$steps" --export-dir "$here/demos/$mode")
if [ "$dry_run" = 1 ]; then options+=(--dry-run); fi
if [ -n "${KEHAI_CLONE:-}" ]; then options+=(--clone-root "$KEHAI_CLONE"); fi
exec python3 "$root/tools/run/kehai_run.py" demos "${options[@]}"
