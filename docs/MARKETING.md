# Marketing

What we say about Kehai, the videos we make, and the automation that makes most of them. The
to-do list is in [RELEASE_PLAN.md](RELEASE_PLAN.md) (Setup and Milestone 5).

---

## Pitch

**One line.** A night shift in a convenience store, where the manager is an AI that learns how
you work and uses it against you.

**For a chat (20 seconds).** You stock shelves, mop spills and help customers in a maze of a
store. Karen, the store's AI, hunts you using only what she can see and hear. She remembers
where you hide, and she moves when you blink. She rarely kills you: she gives you overtime.

**For a slide.**
- **What:** first-person horror in a store at night. Do your jobs; don't get caught.
- **Why it's different:** an AI opponent that really learns your habits, plays fair (every
  trick has a warning), and can act while your eyes are shut (optional webcam).
- **Status:** playable on Mac and Windows; playtests next; then a Steam page.

**Karen's line:** *"Employee wellbeing is a tracked metric. I am optimising it."*

**Hooks, strongest first** (open every video with one):
1. **She moves when you blink.**
2. **My manager learned where I hide.**
3. **She doesn't kill you. She gives you overtime.**
4. **She plays fair:** every trick has a warning.

**Who it's for:** players of short indie horror and store sims (*The Convenience Store*,
*Supermarket Simulator*, *The Exit 8*, *Alien: Isolation*); stealth fans; developers and AI
people, who share technical videos.

**Steam tags:** Horror, Psychological Horror, Simulation, Stealth, First-Person, Singleplayer,
Atmospheric, Indie.

---

## What we make

| type | length | where | how often |
|---|---|---|---|
| **Fun moments** | short, 15–40 s | YouTube Shorts, TikTok, Instagram Reels | 2 a week |
| **Educational** | long, 8–15 min | YouTube | 1 a month, taking turns with updates |
| **Updates** | long explainer, plus a short "what's new" | YouTube; the short everywhere shorts go | when a feature lands |
| **Reddit** | a post | the subreddits below | with each update and good moment |

### 1. Fun moments (short)
A catch, a near miss, a trick and its warning, a blackout, a customer lost in the maze. Hook in
the first 2 seconds, one idea, captions on, end on a question or a loop. The automation makes
these from your shifts (below).

### 2. Educational (long)
How games work, shown through how Kehai was made. Topics:
- **How games stay fast:** GPU occlusion culling, 18.9 → 4.5 ms ([DECISIONS.md](DECISIONS.md#2-rendering))
- **16,500 items without 33,000 objects:** shelves as data; a 92 MB scene down to 6 MB
- **How to make an endless maze:** seeds, chunks, spanning trees, braiding
- **How Karen guesses where you are:** a belief map from sight, sound and witnesses
- **How webcam blink detection works**, and why the ~300 ms of a blink is enough
- **Recording a whole shift in 1 MB:** replays from snapshots
- **I made an AI play my horror game:** the agent test environment
- **Does Karen's learning actually help?** The ablation test, honestly ([IDEAS.md](IDEAS.md#4-ablation))

### 3. Updates (long + short)
New features, explained: Karen's adaptability (the patterns she learns about you), new tactics,
the blink mechanic, the endless maze, the products and shelves. The long video explains it;
the short shows it in 30 seconds.

### 4. Reddit
Native video or GIF, an honest title, and replies to every comment. Read each subreddit's rules
first. r/IndieDev, r/indiegames, r/Unity3D, r/gamedev (its weekly threads), r/HorrorGaming,
r/playmygame (for builds), r/DestroyMyGame (blunt feedback before the Steam page). Technical
posts (optimization, the maze) suit r/gamedev and r/Unity3D.

**Home base:** every profile links to the website (email sign-up), the Discord, and later the
Steam page. Social sites bring people; the email list and Discord keep them.

---

## The automation

Most shorts are made for you; you approve each one in Telegram. Code and commands:
[`tools/marketing/README.md`](../tools/marketing/README.md).

1. **You play.** Each shift records its best moments (`F7` marks one yourself).
2. **Saturday:** Claude picks the week's 2 shorts from those moments.
3. **Overnight:** Unity renders the shots from the 3D replay; Claude writes the short (hook,
   scenes, captions, voice-over) and translates it into Russian; Azure speaks it; Remotion edits
   the video.
4. **Telegram:** the preview arrives with ✅ ❌ ✏️ 🇷🇺 ↩️. ✏️ asks what to change, in your words.
5. **Posting days:** approved shorts go to Buffer (YouTube, Instagram, X or Bluesky). TikTok and
   the Russian version come to Telegram to post by hand.
6. **Monthly:** a long video, in stages you approve (outline, script, cut). Today it covers the
   month's best moments and changes; turning it into the educational and update videos above is
   on the to-do list.
7. **Reference videos:** send the bot a video you like (with a note). It's studied into a
   *pattern* (structure, pacing, format), and one of the next week's shorts follows it.
   `/patterns` lists them.
8. **Sundays:** a weekly report.

**Costs:** Claude about $10 a month, capped at $15; a reference study about $0.20. Azure,
Buffer (3 channels), R2 and Drive stay on free tiers. **It runs** on the Mac (launchd), with
nothing open in between; it stays in dry-run mode until the keys are in.

---

## Setup

Keys go in `tools/marketing/.env` (never committed; copy `.env.example`). Open it with
`open -e tools/marketing/.env` and paste each value after its `=`.

1. **Claude API** (`ANTHROPIC_API_KEY`): [platform.claude.com](https://platform.claude.com) →
   Billing (buy credits) → Workspaces (make `tokenlimit-marketing`, Limits → $15) → API keys.
2. **Azure Speech** (`AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`): a free Azure account, then
   [create a Speech resource](https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices)
   on the **Free F0** tier, region East US → Keys and Endpoint.
3. **Telegram** (`TELEGRAM_BOT_TOKEN`): [@BotFather](https://t.me/BotFather) → `/newbot`. Press
   Start in your bot, then `uv run run_job.py telegram-setup` prints `TELEGRAM_CHAT_ID`.
4. **Buffer** (`BUFFER_API_KEY`): connect YouTube, Instagram (a Business or Creator account) and
   X or Bluesky, set the posting times, then
   [publish.buffer.com/settings/api](https://publish.buffer.com/settings/api).
5. **Cloudflare R2** (`KEHAI_PUBLIC_URL`, `RCLONE_CONFIG_R2_*`): a bucket named `kehai-public`
   with its Public Development URL on, and an API token (Object Read & Write, that bucket).
6. **Google Drive:** `brew install rclone`, then `rclone config create gdrive drive scope=drive.file`.
7. **Then** set `KEHAI_DRY_RUN=0` in `~/TokenLimit/n8n/env`.

---

## Still to decide

- [ ] The handle and the website domain
- [ ] X or Bluesky for Buffer's third channel (the free plan has 3)
- [ ] The voices: pick from samples once Azure is in (`uv run voice_samples.py`)
- [ ] Russian: English goes to Buffer; post the Russian versions by hand, or open Russian
      accounts later
- [ ] Posting days and times (the automation posts on Tuesdays and Fridays)
- [ ] How long to keep local video files once they're archived

---

## What to measure

- **Before the Steam page:** email and Discord sign-ups; per video: views, profile visits, link
  clicks.
- **After:** **wishlists per week**, and where they came from (one tracking link per platform).

After 4 weeks, do more of what works and drop what doesn't.
