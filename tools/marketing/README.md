# Kehai marketing pipeline

Scripts that turn recorded shifts into finished videos, from picking the moments to the text
that goes with each post. The plan, its phases and the decisions behind them are in
[`BUILD_PLAN.md`](BUILD_PLAN.md); the video editor has its own [`editor/README.md`](editor/README.md).
Phase 7 chains these steps in n8n with approvals in Telegram; each one also runs by hand.

```bash
uv sync                                   # once: Python 3.12, the Anthropic and Azure Speech SDKs
cp .env.example .env                      # then fill in the keys you have (never committed)
uv run pytest                             # the tests (no keys needed)
```

## A week, step by step

| Step | What it does | Writes |
|---|---|---|
| `pick_moments.py --week 2026-W40` | Claude picks the week's 3 shorts from the shifts' clip moments, the shots to film for each, and decides every moment marked kept (F7) | `plans/<week>/picks.json` |
| `render_picks.py plans/<week>/picks.json` | renders those shots with Unity, one at a time (`render_shot.sh`; the editor must be closed) | `shots/<week>/<pick>/*.mp4` + `.json` |
| `write_short.py --picks … --pick <name>` | Claude writes the short against the rendered shots and the asset library | `edits/<id>/` |
| `translate.py <id>` | Claude translates the on-screen text and the script into Russian | the edit, `translations.json` |
| `voice.py <id>` | speaks the script in every language, with word timings for the captions | `audio/<id>/<lang>/*.wav` |
| `node editor/render.mjs edits/<id>/edit.json` | renders one video per language, mastered to -14 LUFS | `drafts/<id>.<lang>.mp4` |
| `package.py <id>` | Claude writes the titles, captions and posts per platform and language | `edits/<id>/package.json` |
| `revise.py <id> --note "…"` / `--undo` | Claude applies the owner's note; the old version is kept | the edit, `versions/` |
| `weekly_report.py` | the week's numbers, and Claude's short read of them | `reports/<week>.md` |
| `study_reference.py <video> --note "…"` | Claude studies a video the owner liked into a pattern (below) | `references/<id>/`, `patterns/<name>.json` |

Once a month, `long_video.py outline | script | voice | edit --month 2026-10` makes the long
video in stages the owner approves one by one (the shots for the outline go through
`render_picks.py long/<month>/outline.json`). `voice_samples.py` speaks the same lines in several
Azure voices so the owner can choose (Q10).

Every path above is in the working folder, `~/TokenLimit/marketing` (or `KEHAI_MARKETING`, or
`--root`). Shift records are read from the game's data folder (`KEHAI_SHIFT_RECORDS`).

## Running it on its own (`run_job.py`, Phase 7)

macOS's launchd (or n8n, if you'd rather keep it running) starts `run_job.py` jobs on a schedule
([`n8n/README.md`](n8n/README.md)); each job works out what to do from the state in
`state/pipeline.db` and the files on disk, so a run that stopped carries on where it was.

- **produce** (nightly): announces new shifts in Telegram; on `pick_day` (or the first run after it
  that week, if the Mac slept through it) picks the week's shorts; takes each short along its steps
  (shots → write → translate → voice → render → package) and sends it for approval. A step that fails is tried again on later runs; one blocked by a missing
  key or the monthly cap waits, and the owner hears once a day.
- **telegram** (every minute): the owner's ✅ ❌ ✏️ 🇷🇺 ↩️ and replies. ✏️ asks what to change;
  the reply becomes a `revise` job. Only the owner's chat is listened to.
- **work** (every 5 minutes): revisions and undos (then the chain again, and a new preview), the
  long video's stages.
- **publish** (posting days): the next approved short to each Buffer channel's queue from a
  public copy (Buffer refuses Drive links); TikTok, and anything Buffer can't take yet, comes to
  Telegram with the text ready and a "Posted ✅" button.
- **housekeeping** (daily): Buffer's post statuses, the Drive archive, retention
  (`pipeline.json`, Q12: only reports until `retention.apply` is on), the storage budget.
- **report** (Sundays) and **long** (monthly, with ✋ gates for the outline, the script and the
  rough cut, then the final files on Drive for YouTube Studio).

`pipeline.json` holds the schedule's settings: shorts per week, the pick day, posting days and
channels, YouTube and Instagram post settings, retention, the storage budget.

## Reference videos and patterns (Phase 8)

Send the bot a video you liked on social media (as a video, under 20 MB: Telegram compresses it),
with what you like about it as the caption. Within a few minutes it comes back as a **pattern**:
what the video does second by second, why it works, and the recipe for doing the same with Kehai's
footage. ✏️ under it studies it again with your correction; ❌ drops it. `/patterns` lists the
library.

- **Reading it** (`km/watch.py`, `km/stt.py`): Claude can't watch video, so it gets frames (close
  through the hook, then about one a second, and each shot's first moment), the cuts and the
  sound's level measured from the file, and a transcript from Azure when that's set up.
- **The pattern** (`patterns/<name>.json`): hook, beats, pacing, format, the editor features that
  rebuild each part (`recipe`), the replay cameras it needs, what the editor can't do yet, the kind
  of shift moment it needs, and what must never be copied (their lines, jokes, sound, branding).
- **The short:** on the pick day the oldest waiting reference takes one of the week's shorts
  (`pipeline.json` → `references.per_week`). The picking gives it a moment that suits it and films
  it with the cameras the pattern needs, or tells you what to play if nothing that week fits. The
  writer follows the recipe; the preview says "Pattern: …". Name a pattern in a ✏️ note to rebuild
  any video in it.
- **Kept:** the reference video, its frames and its audio for 30 days (`retention.references_days`;
  never archived), the pattern for good. About $0.19 to study a 20-second video.

## How the Claude steps work (`km/llm/`)

- **Claude Opus 5.5** for every step; the effort (low to high) is set per step in `km/llm/steps.py`.
  `KEHAI_LLM_MODEL` overrides the model.
- **Structured outputs:** each answer is JSON in a schema from `schemas/llm/`, kept inside what the
  API accepts (every field required, no ranges, no unions). The code then checks what the schema
  can't (ranges, timing, files, lengths, names) and sends a broken answer back once with the
  problems listed.
- **Prompts** live in `prompts/`: `style.md` and `reference.md` (the voice, the rules, the game,
  the editor) open every request and are cached; each step adds its own. Names come from
  `brand.json` through `{{placeholders}}`.
- **Cost:** every call's tokens and dollars go to `logs/llm_costs.csv`. Before a call, this
  month's spend plus a high estimate is checked against `KEHAI_LLM_MONTHLY_USD` ($15): past it,
  nothing is sent and the owner is told (`state/alerts.jsonl`, sent to Telegram in Phase 7). The
  Console's workspace limit is the hard stop behind it. The owner hears at 80% too.
- **Refusals** stop the step and tell the owner; server-side fallback gets the first chance.
- **Dry run** (`--dry-run`, every step): the request is written to `logs/llm_requests/` with its
  estimated cost, and nothing is sent. **Replay** (`KEHAI_LLM_REPLAY=<folder>`): answers come
  from files instead of the API, for tests and for running the pipeline without a key.

## The voice (`km/tts/`)

`azure` (neural voices, the F0 free tier: about 0.5M characters a month, word timings from the
service), `elevenlabs` (switchable), and `say` (the macOS voices: a stand-in that needs no key,
with estimated word timings). The voices per language and role are in `brand.json`. Spoken lines
are cached by what's said and who says it, so a revision only speaks what changed; characters per
month are logged to `logs/tts_usage.csv` and capped per backend.

## Playtests (`km/playtest/`, `playtest.py`)

Not marketing, but it shares this pipeline's Telegram, Claude client and schedule: the `work`
job also pulls new playtest sessions from the upload service, reads and summarises them (the
`playtest_summary` step), announces them in Telegram, and rebuilds the combined report; a bug
note becomes a GitHub issue only when you tap 📝 (`pt:` buttons). `uv run playtest.py status`
shows what's set up. The whole loop is in [`PLAYTEST.md`](../../PLAYTEST.md).

## Exit codes (every script)

0 done or a dry run · 1 something failed (try again later) · 2 bad input, or an answer still
broken after the repair · 3 a key or tool isn't set up · 4 over the monthly spend or voice quota ·
5 Claude declined · 75 another heavy job holds the lock (`state/heavy.lock`).
