#!/bin/bash
# What every scheduled job runs, from n8n or from launchd: one run_job.py subcommand, from this
# repo's tools/marketing, with the tools it needs on PATH (n8n's and launchd's environments have
# almost none).
#
#   tools/marketing/n8n/job.sh produce | telegram | work | publish | housekeeping | report | long
#
# KEHAI_DRY_RUN=1 (in ~/TokenLimit/n8n/env, read on every run) adds --dry-run to the jobs that
# have one. Under launchd (KEHAI_LOG_DIR set) the output goes to <KEHAI_LOG_DIR>/<date>.log, and a
# run that did nothing (the usual Telegram check) leaves no line there.
set -uo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
export PATH="$HOME/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin"
cd "$here" || exit 1
settings="${KEHAI_N8N_HOME:-$HOME/TokenLimit/n8n}/env"
if [ -z "${KEHAI_DRY_RUN:-}" ] && [ -f "$settings" ]; then
  KEHAI_DRY_RUN="$(sed -n 's/^KEHAI_DRY_RUN=//p' "$settings" | tail -1)"
fi
args=("$@")
if [ "${KEHAI_DRY_RUN:-0}" = "1" ]; then
  case "$1" in produce|publish|long|report) args+=(--dry-run) ;; esac
fi
if [ -z "${KEHAI_LOG_DIR:-}" ]; then
  exec uv run --quiet run_job.py "${args[@]}"
fi

started="$(date '+%H:%M:%S')"
out="$(uv run --quiet run_job.py "${args[@]}" 2>&1)"
code=$?
last="$(printf '%s\n' "$out" | tail -1)"
if [ "$code" -eq 0 ] && [ "$out" = "$last" ] && [[ "$last" == *'"summary": []'* ]]; then
  exit 0
fi
mkdir -p "$KEHAI_LOG_DIR"
{ echo "--- $started $* (exit $code)"; printf '%s\n' "$out"; } >> "$KEHAI_LOG_DIR/$(date '+%Y-%m-%d').log"
exit "$code"
