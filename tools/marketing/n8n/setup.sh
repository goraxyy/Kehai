#!/bin/bash
# Sets up the pipeline's schedule (docs/MARKETING.md), on this Mac only. Two ways to run it:
#
#   tools/marketing/n8n/setup.sh --launchd         macOS runs each job at its time (LaunchAgents com.tokenlimit.kehai.job.*);
#                                                  nothing stays in memory between runs, and a time the Mac slept through runs
#                                                  when it wakes. n8n, if installed, is only for looking and "Run now".
#   tools/marketing/n8n/setup.sh --launch-agent    n8n runs the schedule and starts at login (com.tokenlimit.kehai.n8n)
#   tools/marketing/n8n/setup.sh                   again, the same way as last time (n8n by hand, if never chosen)
#   tools/marketing/n8n/setup.sh --remove-agent    n8n no longer starts at login
#   tools/marketing/n8n/setup.sh --remove-launchd  the job agents are gone; n8n runs the schedule again (when started)
#   tools/marketing/n8n/run.sh                     start n8n by hand (http://127.0.0.1:5678)
#
# The choice is KEHAI_SCHEDULER in ~/TokenLimit/n8n/env (n8n | launchd), with the other settings
# (KEHAI_DRY_RUN=1 makes produce, publish and long dry runs). n8n lives in ~/TokenLimit/n8n (its app
# in app/, its database and key in .n8n/), on Node 24 (Homebrew's node@24: n8n 2.41 needs Node 24 or
# newer; the Mac's default Node stays as it is), and listens on 127.0.0.1 only. The workflows hold
# no secrets: every step reads tools/marketing/.env. Stop n8n before running this.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tools="$(cd "$here/.." && pwd)"
home_n8n="${KEHAI_N8N_HOME:-$HOME/TokenLimit/n8n}"
settings="$home_n8n/env"
node24="/opt/homebrew/opt/node@24/bin"
agent="$HOME/Library/LaunchAgents/com.tokenlimit.kehai.n8n.plist"
version="2.41.6"           # the n8n these workflows were tested with
logs="${KEHAI_MARKETING:-$HOME/TokenLimit/marketing}/logs"
n8n="$home_n8n/app/node_modules/.bin/n8n"

say() { echo "setup: $*"; }
remove_agent() {
  if [ -f "$agent" ]; then
    launchctl bootout "gui/$(id -u)" "$agent" 2>/dev/null || true
    rm -f "$agent"
    say "n8n no longer starts at login"
  fi
}

choice="${1:-}"
case "$choice" in
  ""|--launchd|--launch-agent|--remove-agent|--remove-launchd) ;;
  *) say "unknown option $choice (see the top of this file)"; exit 2 ;;
esac
if [ "$choice" = "--remove-agent" ]; then
  remove_agent
  exit 0
fi

mkdir -p "$home_n8n" "$logs"
timezone="$(python3 -c "import json;print(json.load(open('$tools/pipeline.json'))['timezone'])")"
if [ ! -f "$settings" ]; then
  cat > "$settings" <<ENV
# The Kehai pipeline's schedule settings (read by run.sh, job.sh and the LaunchAgents). No secrets here.
KEHAI_SCHEDULER=n8n
N8N_USER_FOLDER=$home_n8n
N8N_LISTEN_ADDRESS=127.0.0.1
N8N_HOST=127.0.0.1
N8N_PORT=5678
N8N_PROTOCOL=http
N8N_SECURE_COOKIE=false
NODES_EXCLUDE=[]
GENERIC_TIMEZONE=$timezone
N8N_DIAGNOSTICS_ENABLED=false
N8N_VERSION_NOTIFICATIONS_ENABLED=false
N8N_TEMPLATES_ENABLED=false
N8N_PERSONALIZATION_ENABLED=false
EXECUTIONS_DATA_PRUNE=true
EXECUTIONS_DATA_MAX_AGE=336
KEHAI_TOOLS=$tools
KEHAI_DRY_RUN=1
ENV
  say "wrote $settings (dry runs on: set KEHAI_DRY_RUN=0 there when the keys are in .env)"
fi
grep -q '^KEHAI_SCHEDULER=' "$settings" || echo "KEHAI_SCHEDULER=n8n" >> "$settings"
case "$choice" in
  --launchd) scheduler=launchd ;;
  --launch-agent|--remove-launchd) scheduler=n8n ;;
  *) scheduler="$(sed -n 's/^KEHAI_SCHEDULER=//p' "$settings" | tail -1)" ;;
esac
# This repo, the timezone and the choice may have changed since the file was written: keep them current.
sed -i '' -e "s|^KEHAI_TOOLS=.*|KEHAI_TOOLS=$tools|" -e "s|^GENERIC_TIMEZONE=.*|GENERIC_TIMEZONE=$timezone|" \
  -e "s|^KEHAI_SCHEDULER=.*|KEHAI_SCHEDULER=$scheduler|" "$settings"
set -a; . "$settings"; set +a

# ---- n8n: needed to run the schedule the n8n way; with launchd, only kept up to date if it's there.
if [ "$scheduler" = "n8n" ] || [ -x "$n8n" ]; then
  [ -x "$node24/node" ] || { say "Node 24 isn't installed: brew install node@24"; exit 3; }
  export PATH="$node24:$PATH"
  # The CLI and a running n8n don't mix: stop the one the LaunchAgent keeps up (it's started again below).
  [ -f "$agent" ] && launchctl bootout "gui/$(id -u)" "$agent" 2>/dev/null || true
  if lsof -nP -iTCP:"${N8N_PORT:-5678}" -sTCP:LISTEN >/dev/null 2>&1; then
    say "n8n is running: stop it first (Ctrl+C where run.sh runs), then run this again"
    exit 1
  fi
  if [ ! -x "$n8n" ]; then
    say "installing n8n into $home_n8n/app (about 2.6 GB)"
    mkdir -p "$home_n8n/app"
    (cd "$home_n8n/app" && { [ -f package.json ] || npm init -y >/dev/null; } && npm install --no-fund --no-audit "n8n@$version")
  fi
  ids=()
  for f in "$here"/workflows/*.json; do
    ids+=("$(python3 -c "import json,sys;print(json.load(open(sys.argv[1]))['id'])" "$f")")
  done
  say "importing the workflows into n8n"
  # A published workflow can't be overwritten from the CLI: unpublish ours (only ours) first.
  for id in "${ids[@]}"; do "$n8n" unpublish:workflow --id="$id" >/dev/null 2>&1 || true; done
  "$n8n" import:workflow --separate --input="$here/workflows/" 2>&1 | grep -E "Import|error|Error" | tail -3
  if [ "$scheduler" = "n8n" ]; then
    for id in "${ids[@]}"; do "$n8n" publish:workflow --id="$id" >/dev/null 2>&1 && say "published $id" || say "couldn't publish $id"; done
  else
    say "left unpublished: launchd runs the schedule, so n8n's would run every job twice (\"Run now\" still works)"
  fi
fi

# ---- the schedule ----------------------------------------------------------------------------
if [ "$scheduler" = "launchd" ]; then
  remove_agent
  say "the jobs, run by launchd:"
  python3 "$here/launchd.py" install --logs "$logs" | sed 's/^/  /'
  say "done. Logs: $logs/scheduled/ (and logs/jobs/ for each step). Change the times in the workflows' Schedule nodes, then run this again."
  exit 0
fi

python3 "$here/launchd.py" remove | grep -v "no job agents" | sed 's/^/setup: /' || true
if [ "$choice" = "--launch-agent" ]; then
  mkdir -p "$(dirname "$agent")"
  cat > "$agent" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key><string>com.tokenlimit.kehai.n8n</string>
  <key>ProgramArguments</key>
  <array><string>/bin/bash</string><string>$here/run.sh</string></array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>$logs/n8n.log</string>
  <key>StandardErrorPath</key><string>$logs/n8n.log</string>
</dict>
</plist>
PLIST
  launchctl bootout "gui/$(id -u)" "$agent" 2>/dev/null || true
  launchctl bootstrap "gui/$(id -u)" "$agent"
  say "n8n starts at login now (LaunchAgent com.tokenlimit.kehai.n8n); open http://127.0.0.1:5678"
elif [ -f "$agent" ]; then
  launchctl bootstrap "gui/$(id -u)" "$agent" 2>/dev/null || true
  say "done; n8n started again (it starts at login)"
else
  say "done. n8n runs the schedule while it runs: start it with $here/run.sh, or again with --launch-agent to start it at login"
fi
