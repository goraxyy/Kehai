# Private local status

A runnable local fallback for the requested private summary. No verified claude.ai write API
is configured. This tool does not publish externally, install jobs or change accounts. All
snapshots, histories, logs, passwords and HTML must live outside every git checkout.

Run from the repository root:

```sh
PY=/Users/kablanbekali/Developer/Kehai/tools/rl/.venv/bin/python
PRIVATE=/private/tmp/kehai-status-demo
"$PY" -B -m tools.status.cli collect --repo . --home "$PRIVATE"
"$PY" -B -m tools.status.cli render --home "$PRIVATE"
```

Open `$PRIVATE/status.html` locally. Collection prints the snapshot and writes `latest.json`,
`history.jsonl`, and `status.html` with mode 0600. New directories use 0700. The page is
self-contained and makes no external requests. Source timestamps and hashes accompany local
files; unavailable sources have reasons. Collection is read-only with respect to the game/repo.

Optional local evidence:

```sh
"$PY" -B -m tools.status.cli collect --repo . --home "$PRIVATE" \
  --register /private/path/runs.jsonl --score /private/path/score.json \
  --tests /private/path/results.xml --build /private/path/build.txt \
  --marketing-dir /private/path/marketing --heavy-lock /private/path/heavy.lock
```

Repeat `--score`, `--tests` or `--build` for multiple inputs. Test XML accepts NUnit or
JUnit/pytest summaries and rejects inconsistent counts. Inputs are supplied evidence, not
proof they were produced by the current commit. Build text is displayed verbatim with source
hash; code presence does not imply build success. C# counts cover tracked files only and read
the working tree, so newly untracked C# is excluded. Plan progress counts Markdown checkboxes;
it is task progress, not an estimate of game quality. A run/lock's last recorded contents do
not prove its process is alive. Local marketing collection counts JSON files in explicit
drafts/approvals/plans directories only; it does not claim actual posting or engagement.

GitHub is disabled by default. `--github OWNER/REPO` uses an existing `gh` login, reads PRs and
issues, and links PRs/bugs/training issues. No login or credential is required for local use.
Failed/missing `gh` is unavailable. Lists are capped at 1000 per type and flag possible
truncation. YouTube, Buffer, Steam wishlists, cloud playtests and private asset data remain
unavailable until their accounts and collection sources exist. No .env files are read.

The page has Release, Builds, Training, Marketing and Growth sections. History keeps the most
recent snapshot per UTC day over a rolling 30-day window; it never backfills unknown days.
Growth is plain arithmetic on supplied observations, which can change when sources change.
A week of real history requires a week of actual collection. No week-long uptime is claimed.

## Authenticated local serving

```sh
"$PY" -B -m tools.status.cli serve --file "$PRIVATE/status.html" \
  --token-file "$PRIVATE/server-password" --host 127.0.0.1 --port 8765
```

Open `http://127.0.0.1:8765/`, username `owner`, with the generated password from the owner-only
`server-password` file. Read the password locally; do not put it in URLs or command arguments.
Only `/` serves the chosen HTML. Directory access, query tokens, uploads and remote bind
addresses are refused. Responses disable caching and external network connections. Stop with
Ctrl-C. This loopback service is not a phone-accessible remote deployment; HTTPS/remote auth
and claude.ai integration remain unconfigured.

## Scheduler template only

```sh
"$PY" -B -m tools.status.schedule --repo "$PWD" --home "$PRIVATE" --python "$PY" \
  --hour 2 --minute 15 --out "$PRIVATE/com.tokenlimit.kehai-status.plist"
```

This generates a launchd plist with literal absolute arguments and umask 077, and prints manual
install/uninstall guidance. It does **not** install or load it. The private home must exist
before manual installation so log paths can open. `launchd/com.tokenlimit.kehai-status.plist.in`
is a readable template; use the generator to safely encode paths containing spaces. The Mac
must be awake and logged in. This job collects local defaults; additional evidence paths need
explicit arguments in the reviewed plist. No scheduler or hosting service was installed.

## Tests

```sh
"$PY" -B -m unittest discover -s tools/status/tests -v
```

The server test binds an ephemeral loopback port and checks authentication and file isolation.
History tests use synthetic dated fixtures to check retention; they do not claim real elapsed
days. Actual validation counts and remaining limits are in the
[integration note](../../docs/agents/integration/0.5.0-reports.md).
