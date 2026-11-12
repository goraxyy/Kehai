# Plan: hands-off video publishing

Approved by the owner on 2026-11-01. The owner has no time to play shifts or think of ideas, so
agents plan, film, write and post the videos to a set blueprint and style. The pipeline in
[`tools/marketing`](../../../tools/marketing/README.md) stays; this adds what it's missing.

## Decisions

| topic | decision |
|---|---|
| **Cadence** | 2 horror shorts a week; 1 AI short, 1 game long and 1 AI long a month |
| **Approval** | The owner approves the first 6. After that a video posts unless rejected within 24 hours |
| **Footage** | Simulated players overnight, plus any shift the owner plays |
| **Blueprint** | Claude drafts 3 or 4 series with sample scripts; the owner edits once |
| **AI videos** | An honest build log, negative results included |
| **Claude** | The owner's Pro plan through `claude -p`, with the API client kept as a backup |
| **Machine** | Unity work on the Mac, Saturday and Wednesday nights; Telegram, the veto and posting on Railway |
| **Voice** | Synthetic, except the owner records the monthly AI long |
| **Language** | English only for now |
| **Channels** | YouTube, Instagram and TikTok through Buffer's 3 free channels; no X or Bluesky |
| **Handle** | `kehaigame` |

The owner's recurring work: two nights a week with the Mac plugged in and Unity closed, tapping
approvals for the first 6 videos, and about an hour a month recording the AI long.

## How it runs

| where | what happens |
|---|---|
| **Mac, Saturday and Wednesday nights** | Bots play shifts, agents plan and write, Unity renders, the video uploads |
| **Railway, always on** | Telegram, the 24-hour veto, posting through Buffer, collecting view numbers |
| **The owner's phone** | ✅ ❌ ✏️ on previews, and the AI long's voice memo |

If Unity is open, the Mac is asleep or another job holds the lock, the night is skipped and the
owner hears in Telegram. Jobs never close Unity. Training gets the other five nights, and still
only starts when the owner says so.

## The agents

| agent | job | status |
|---|---|---|
| **Showrunner** | Plans the month and the week's videos from blueprints, moments, changelog and results | new |
| **Footage** | Orders bot shifts for what's planned, then picks the best moments | extends `pick_moments` |
| **Writer** | Writes the script and edit from one series recipe | exists, needs blueprints |
| **Reviewer** | Checks frames and script against the blueprint and fact sheet; sends failures back once | new |
| **Packager** | Writes titles and captions per platform | exists |
| **Analyst** | Reads the numbers and proposes blueprint changes for the owner to approve | extends `weekly_report` |

## First blueprint ideas

Starting points only; the research comes in phase 3.

| series | type | recipe |
|---|---|---|
| **Incident Report** | horror short | Karen reads a polite HR report over the run, which suits bot footage because no "I" is claimed |
| **Spot the Tell** | horror short | Freeze on the warning, ask "did you catch it?", then show the trick |
| **What She Knew** | horror short | Split screen of the player's view and her belief map |
| **Build Log** | AI short and long | A question, the test, and the honest result, in the owner's voice on the long |
| **Shift Report** | game long | The month's changes and best moments |

## Build order

Each phase is its own PR from a worktree. Posting stays off until phase 6.

| # | phase | stops if |
|---|---|---|
| 1 | **Truth and engine:** fix the false claims, add the fact sheet, add a `claude -p` backend, keep the API as backup | tests fail |
| 2 | **Proof night (needs the owner's go):** one bot shift becomes one finished short from today's store | it looks bad, recording fails, or sign-in fails overnight |
| 3 | **Blueprints:** research, 3 or 4 recipes with sample scripts, the owner edits once | the owner rejects them |
| 4 | **New agents and long-video formats:** Showrunner, Reviewer, bot runner, AI and game longs, English-only chain | Reviewer lets test errors through |
| 5 | **Accounts and Railway:** the owner creates the accounts, then the posting service gets built | Buffer can't post to a channel |
| 6 | **Launch:** bank 6 shorts, the owner approves them, posting starts, then veto mode | the owner stops it |

If phase 2 looks bad, list the smallest visual fixes rather than polish anything by default.

## Only the owner can do

- Claim `kehaigame` on YouTube, Instagram and TikTok, and create the Telegram bot and the Azure,
  Buffer, Cloudflare and Railway accounts. Then paste the keys in.
- Sign in to Claude Code once.
- Pick the synthetic voices from samples.
- Give the go for the proof night, because it needs Unity closed.

## Risks to check

- **Pro limits:** phase 2 measures one short, and longs are heavier. If it isn't enough: fewer
  videos, or an API key. Marketing yields to dev sessions.
- **The subscription route may change:** Claude's docs say `--bare`, which needs an API key, will
  become the default for `-p`. The API client stays so a switch is one setting.
- **Overnight sign-in** under launchd is untested.
- **Bot footage** may look robotic, and bots only play shifts 4–7, so what unlocks later won't appear.
- **Buffer** is unproven for TikTok and for long videos. Without it: manual posts or YouTube's own API.
- **No music yet:** the library only has samples, so videos use the game's own sound.
- **Disk:** about 14 GB free with cleanup off (`retention.apply`); turn it on before launch.
- **One Telegram listener:** Railway takes over the bot and forwards reference videos to the Mac.
- **YouTube's "inauthentic content" rule** targets templated videos, so series must vary.
