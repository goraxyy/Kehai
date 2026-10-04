# The pipeline's schedule: launchd, or n8n

Seven jobs start `run_job.py` on a schedule. Every decision is in the Python (`tools/marketing`),
every message to the owner goes through Telegram, and nothing here holds a secret.

| Job | When | What |
|---|---|---|
| telegram | every minute | the owner's taps and replies; alerts out |
| work | every 5 minutes | queued revisions, undos, the long video's stages, reference videos to study |
| produce | 1, 3 and 5 a.m. | new shifts announced; the week's picks (on `pick_day`, or the first run after it that week); every short along its steps; Unity renders only while the editor is closed |
| publish | 9 a.m. | on posting days: the next approved short to Buffer, TikTok by hand |
| housekeeping | 4:30 a.m. | Buffer statuses, the Drive archive, retention, storage |
| report | Sundays 8 p.m. | the weekly report, to Telegram |
| long | 10 a.m. | on `long.day`: the month's long video starts |

The times live in one place: the **Schedule** node of each workflow in `workflows/*.json`
(`pipeline.json`'s timezone). Two ways to run them:

- **launchd (the default here, `setup.sh --launchd`)**: macOS runs `job.sh <job>` at those times
  from one LaunchAgent per job (`com.tokenlimit.kehai.job.*`, written by `launchd.py`). Nothing stays
  in memory between runs. A time the Mac slept through runs once when it wakes; the every-few-minutes
  jobs carry on after a wake, and Telegram keeps the owner's taps for 24 hours. Output that says
  something goes to `~/TokenLimit/marketing/logs/scheduled/<date>.log` (each step's own output to
  `logs/jobs/`); the quick jobs run at background priority. n8n's copies of the workflows stay
  unpublished, so start n8n only to look around or press **Run now**.
- **n8n (`setup.sh --launch-agent`)**: n8n runs the schedule and stays up (about 400 MB of memory),
  starting at login; it misses whatever time the Mac sleeps through. Its UI shows each run.

```bash
tools/marketing/n8n/setup.sh --launchd        # the jobs from launchd (installs nothing else; keeps n8n's workflows current if it's there)
tools/marketing/n8n/setup.sh                  # again, the same way as last time: after changing a Schedule node, or moving the repo
tools/marketing/n8n/launchd.py show           # what's scheduled, and when
tools/marketing/n8n/run.sh                    # n8n by hand: http://127.0.0.1:5678 (stop it before running setup.sh)
tools/marketing/n8n/setup.sh --launch-agent   # n8n runs the schedule instead, from login (--remove-agent to stop that)
tools/marketing/n8n/setup.sh --remove-launchd # no launchd jobs: n8n runs the schedule while it runs
```

n8n needs Homebrew's `node@24` (n8n 2.41 needs Node 24+; the Mac's default Node stays) and lives
in `~/TokenLimit/n8n`; its first visit asks for an owner account (local only). Edits made in n8n's
editor stay in its database: `setup.sh` puts the repo's workflows back, so copy a change you want
to keep into `workflows/*.json` (or ask Claude to). Scheduled successful runs aren't kept in n8n's
history (the minute-by-minute Telegram check would add 1,440 a day); failures and "Run now" runs are.

Settings, no secrets: `~/TokenLimit/n8n/env`. `KEHAI_SCHEDULER` is the choice above;
**`KEHAI_DRY_RUN=1`** (the default) makes produce, publish and long dry runs, read on every run:
change it to 0 once the keys are in `tools/marketing/.env`.

## Trying it without keys

Without `TELEGRAM_BOT_TOKEN` the bot writes to `<working folder>/state/telegram_outbox.jsonl`,
and you play the owner with `run_job.py fake`:

```bash
uv run run_job.py status
uv run run_job.py fake tap a:2026-w40-found-by-ear:1 --message 1003   # tap ✅ on that preview
uv run run_job.py fake text "make the hook about hearing" --reply-to 1004
uv run run_job.py telegram                                            # handle them
```

`KEHAI_LLM_REPLAY=<folder>` answers the Claude steps from files, `KEHAI_TTS_BACKEND=say` speaks
with the macOS voices, `KEHAI_PICK_NOW=1` picks the week's shorts on the next produce, and
`KEHAI_PIPELINE=<file>` uses other settings (e.g. one short a week for a trial).
