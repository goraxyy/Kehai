#!/usr/bin/env bash
# Run one league or ablation through kehai-run and export the caller's results.
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
dry_run=0
if [ "${1:-}" = "--dry-run" ]; then dry_run=1; shift; fi
if [ "$#" -lt 2 ]; then
  echo "Usage: $0 [--dry-run] <league.json|--ablation> <output folder> [game flags…]" >&2
  exit 1
fi
if [ "$1" = "--ablation" ]; then mode=(ablation); else mode=(league --league="$1"); fi
options=(--export-dir="$2" --eval-timeout-min="${KEHAI_TIMEOUT_MIN:-600}")
shift 2
if [ "$dry_run" = 1 ]; then options+=(--dry-run); fi
if [ -n "${KEHAI_CLONE:-}" ]; then options+=(--clone-root "$KEHAI_CLONE"); fi

# Keep the caller's working directory for relative paths in game flags.
exec python3 "$root/tools/run/kehai_run.py" "${mode[@]}" "${options[@]}" -- "$@"
