# Plan: playtest v2, one link, no codes, everything in the cloud

> **Status:** a draft for your approval (6 November 2026). Nothing is built. It changes the
> playtest pipeline built on 4 October ([PLAYTEST.md](../game/PLAYTEST.md)). Karen's learning,
> which will use this data, has its own plan ([karen-learning.md](karen-learning.md)).

1. [Goals](#1-goals)
2. [Decisions so far](#2-decisions-so-far)
3. [Questions for you](#3-questions-for-you)
4. [What a tester does](#4-what-a-tester-does)
5. [What's recorded, and where it's kept](#5-whats-recorded-and-where-its-kept)
6. [How it fits together](#6-how-it-fits-together)
7. [The build](#7-the-build)
8. [Tests](#8-tests)
9. [Costs](#9-costs)
10. [Privacy](#10-privacy)
11. [Not in this plan](#11-not-in-this-plan)
12. [Terms](#12-terms)
13. [Setting up the accounts](#13-setting-up-the-accounts)

---

## 1. Goals

- **Least friction for testers:** one link, one click to agree, nothing to type, no second website.
- **Most data for you:** everything they did (the 3D replays and logs, as today), their answers
  inside the game, a "was that fair?" at the moments that matter, and a funnel that shows where
  people drop off.
- **Little disk on your Mac:** the cloud keeps everything; the Mac only holds what it's using.

## 2. Decisions so far

All made on 6 November.

| Decision | Why |
|---|---|
| **Cloudflare only:** R2 for files, D1 for rows, a Worker in front, Pages for the website | free up to about 3,000 sessions; never pauses; one account for uploads, the website and the marketing videos. Supabase's free plan holds 1 GB and pauses after 7 quiet days |
| **Everything is kept in the cloud;** the Mac keeps no session files | your Mac is low on disk |
| **No tester code:** the game makes a random ID | the code was only a label (any code was accepted) and a step to get wrong |
| **No Google Form:** three optional typed answers in the game | the form was a second step most people skip; answers now arrive with the session |
| **No itch.io:** a `/playtest` page on your own website | one less account, and the same site does marketing |
| **No Apple Developer Program** | your choice. Mac testers do one "Open Anyway" step, shown with pictures on the page |
| **Steam later** | Steam Playtest replaces the download page when strangers test |
| **No Claude API key** | the Claude steps run on your Claude plan through `claude -p` (the default since PR #71) |
| **The consent covers training Karen** | [karen-learning.md](karen-learning.md) trains on real shifts |

## 3. Questions for you

Answer these before the build starts.

| # | Question | My suggestion |
|---|---|---|
| Q1 | A domain for the website, and which name? | Yes, bought in Cloudflare (about $10 a year for a .com). Until then the free `kehai.pages.dev` address works |
| Q2 | The three typed questions | "What did you think you had to do at first?", "When did Karen feel unfair, if ever?", "Anything else?" (the old form's 1, 3 and 9) |
| Q3 | An optional nickname box at the end? | Yes, so you can tell your friends apart |
| Q4 | "Was that fair? Y / N" for 4 seconds after each catch? | Yes. It can be ignored, and the game doesn't pause |
| Q5 | How long to keep the replays | 12 months; the rows (numbers and answers) are kept for good |
| Q6 | A "new build available" line in the game? | Yes: the game asks the Worker at launch |
| Q7 | Which email goes on the privacy page for deletion requests? | Yours, or a new one for Kehai |
| Q8 | Keep rounds (`round1`, `round2`…)? | Yes: they stop one round's report mixing with the fixes after it |

## 4. What a tester does

| | Now (4 October) | v2 |
|---|---|---|
| What you send | itch link, password, a code, the form link | **one link** |
| Download | password-protected itch page | your website, which offers their Mac or Windows build |
| First launch | Mac: "Open Anyway". Windows: "Run anyway". Then type `T07` | the same warnings; **nothing to type** |
| Consent | two buttons | the same, one click: **Play and send** or **Play without sending** |
| Playing | Shift+F7 marks a bug | the same, plus "Was that fair? Y / N" after a catch (Q4) |
| The end | 4 taps in the game, then a 9-question Google Form | 4 taps, 3 optional typed answers and an optional nickname, **all in the game** |

**Step by step:**
1. They open your link and click the one big button.
2. They open the game. On a Mac, the first time: **System Settings → Privacy & Security → Open Anyway**.
3. One screen: **Play and send** (or Play without sending).
4. They play up to three shifts.
5. They answer, and it's sent. The thank-you screen shows their ID, in case they ever want their data deleted.

## 5. What's recorded, and where it's kept

What the game records doesn't change ([PLAYTEST.md](../game/PLAYTEST.md#whats-recorded)). Where it goes does.

| What | Size | Where | Kept |
|---|---|---|---|
| The session zip: 3D replays, the session log, Karen's logs and Ledger, the game's log | a few MB | R2 `sessions/<round>/<id>/<launch>.zip` | 12 months (Q5) |
| The session row: computer, frame rate, minutes played, how it ended | tiny | D1 `sessions` | for good |
| A row per shift: length, clocked out, caught, times spotted, closest she got, trick sounds, lowest energy | tiny | D1 `shifts` | for good |
| Answers: the 4 taps, the 3 typed answers, the nickname | tiny | D1 `answers` | for good |
| Moments: fairness taps and bug notes, each with its shift and replay time | tiny | D1 `moments` | for good |
| The funnel: download, first launch, agreed, first shift, finished | tiny | D1 `events` | for good |
| Page visits | — | Cloudflare Web Analytics (no cookies) | Cloudflare's own |
| Claude's summary of each session | a few KB | D1 `summaries`, written by your Mac | for good |
| Email sign-ups from the website | tiny | D1 `signups` | until they unsubscribe |
| Karen's learning records ([karen-learning.md](karen-learning.md)) | a few KB a shift | in the zip, and R2 `datasets/learning/` | for good |

## 6. How it fits together

```
the website (Pages)  ── download button ──► Worker /download/<mac|windows> ──► R2 builds/   (counts it)
                     ── email sign-up   ──► Worker /signup                ──► D1 signups

the game  ── launch, consent, each shift, the end ──► Worker /events     ──► D1 events (the funnel)
          ── the session zip ───────────────────────► Worker /sessions   ──► R2 sessions/
          ── its summary ───────────────────────────► Worker /summaries  ──► D1 sessions, shifts, answers, moments

your Mac, every 5 minutes (the scheduled "work" job)
          ── lists new sessions, downloads each into a temporary folder
          ── reads its facts, asks Claude for a summary, sends the Telegram message, updates the report
          ── writes the summary to D1, copies the learning records to R2 datasets/, deletes the folder
          ── "open replay" downloads one session's replays into a small cache, deleted after 7 days
```

The build carries the upload key, which can only add. Reading, listing and deleting take the admin
key, which only your Mac has. That's today's rule, kept.

## 7. The build

**Who:** [C] Claude, [B] the second builder (ChatGPT), [You]. [B] works on its own branch and
worktree, as in 0.2.0; at each sync [C] merges it and runs the tests. No file is given to both
builders in the same phase.

### Phase 0: accounts and answers [You]

- [ ] **Y0.1 Cloudflare:** an account, with R2 turned on ([§13](#13-setting-up-the-accounts)).
- [ ] **Y0.2 Telegram:** a bot, with its token in `tools/marketing/.env` ([§13](#13-setting-up-the-accounts)).
- [ ] **Y0.3 Answer [§3](#3-questions-for-you).**
- [ ] **C0.1 [C] Connect:** after Y0.1, `npx wrangler login` (you click **Allow**), then the bucket,
  the D1 database and the two secrets. After Y0.2, `run_job.py telegram-setup` fills in your chat id.

### Phase 1: the upload service [C]

- [ ] **C1.1 The tables.** D1 tables `testers`, `sessions`, `shifts`, `answers`, `moments`,
  `events`, `summaries` and `signups`, as migrations.
  - Files: `tools/playtest/worker/migrations/`, `wrangler.toml`.
  - Done when: `wrangler d1 migrations apply` makes them locally and in the cloud.
- [ ] **C1.2 The routes.**
  - With the upload key (add only): `PUT /sessions/…` (as today), `POST /summaries`, `POST /events`.
  - Open: `GET /download/<mac|windows>` (sends the current build from R2 and counts a download),
    `GET /latest` (the current build, for Q6), `POST /signup` (an email, with a rate limit and a
    hidden field that catches bots).
  - With the admin key: list and download sessions (as today); `GET /admin/rows/<table>`;
    `POST /admin/summaries`; `DELETE /admin/tester/<id>`, which removes that tester's files and
    rows. The delete-after-pull goes: files stay until Q5's limit.
  - Files: `tools/playtest/worker/src/` (split into a file per route), `wrangler.toml`.
  - Done when: `node --test` passes against stand-ins for R2 and D1, and `wrangler dev` serves it
    on this Mac.
- [ ] **C1.3 Keeping to Q5.** A daily scheduled run deletes replays past the limit and keeps the rows.
  - Files: `tools/playtest/worker/src/cron.js`, `wrangler.toml`.
- [ ] **C1.4 Deploy** (after C0.1). Done when: the address says *Kehai playtest*, and a session
  sent from the editor's simulated playtest shows up in R2 and D1.

### Phase 2: the game [C]

- [ ] **C2.1 No code.** On first launch `TesterState` makes a random 6-character ID (letters and
  digits without look-alikes such as 0/O and 1/I). The code page goes. A computer that already has
  a code keeps it.
  - Files: `PlaytestConfig.cs`, `PlaytestScreens.cs`.
  - Done when: a fresh install goes from launch to the consent page with nothing typed.
- [ ] **C2.2 Consent.** The text adds: *"…and may be used to train Karen."*
  - Files: `PlaytestScreens.cs`, `PLAYTEST.md`.
- [ ] **C2.3 The end page.** The 4 taps, the 3 typed answers (Q2) and the nickname (Q3), all
  optional. The form button goes, and so does `formUrl` (from `PlaytestConfig` and `KehaiBuild`).
  - Files: `PlaytestScreens.cs`, `PlaytestConfig.cs`, `KehaiBuild.cs`, `PlaytestTests.cs`.
- [ ] **C2.4 "Was that fair?"** (Q4). After a catch: Y or N, for 4 seconds, logged with the shift
  and the replay time.
  - Files: `PlaytestSession.cs`, `PlaytestScreens.cs`.
- [ ] **C2.5 The funnel.** Small pings at launch, consent, each shift's start and end, and the end.
  Queued when offline, like sessions.
  - Files: `PlaytestFiles.cs` (`PlaytestUploader`), `PlaytestSession.cs`.
- [ ] **C2.6 The summary.** When the session is packed, a small JSON goes to `/summaries` with the
  zip: the computer, the frame rate, the per-shift numbers (from `ShiftAnalysis`), the answers and
  the moments.
  - Files: `PlaytestSession.cs`, `PlaytestFiles.cs`.
- [ ] **C2.7 A new build** (Q6). At launch the game asks `/latest`; if there's a newer build, the
  main menu says so, with a button to the download page.
  - Files: `PlaytestSession.cs`, `MainMenu.cs`.

**Sync 2:** [C] runs the EditMode tests on a clone; [B] merges `main` in.

- [ ] **B2.1 [B] The look of the playtest pages** (after sync 2): the consent and end pages,
  readable at 1280×720, in the main menu's style. A quick pass; you redesign before launch.
  - Files: `PlaytestScreens.cs` (layout only).

### Phase 3: your Mac [C]

- [ ] **C3.1 Pull without keeping.** Each new session goes into a temporary folder: its facts,
  Claude's summary, the Telegram message and the report; then the summary goes to D1, the learning
  records go to R2 `datasets/learning/`, and the folder is deleted. Nothing is deleted from R2.
  - Files: `tools/marketing/km/playtest/inbox.py`, `summary.py`, `notify.py`, a new `cloud.py`.
- [ ] **C3.2 The report from D1.** Every session, every round, read from D1 instead of local
  folders. Each session links to its replay (C3.3).
  - Files: `km/playtest/report.py`.
- [ ] **C3.3 `playtest.py open <session>`.** Downloads that session's replays into
  `~/TokenLimit/playtests/cache/` and shows them in Finder, ready for **Kehai → Replay → Open
  Shift…**. The cache empties itself after 7 days, or past 2 GB.
  - Files: `tools/marketing/playtest.py`, a new `km/playtest/cache.py`.
- [ ] **C3.4 `playtest.py forget <id>`.** Deletes a tester everywhere, for privacy requests.
- [ ] **C3.5 `playtest.py publish <round>`.** Uploads the round's Mac and Windows zips to R2
  `builds/` and sets `/latest`. Builds are made only on your go.
- [ ] **C3.6 Retire** `make_codes.py`, `TESTER_MESSAGE.md` and `ITCH_PAGE.md`. One message to send
  your friends goes in `tools/playtest/README.md`.
  - Done when: `pytest tools/marketing` passes, with a stand-in for the Worker.

### Phase 4: the website [B], with [C]'s routes

- [ ] **B4.1 [B] The pages.** In `tools/website/`, plain HTML, CSS and a little JavaScript, no
  framework. A quick pass; you redesign before launch.
  - `index.html`: the one-line pitch, a short clip, the email sign-up (C1.2's `/signup`).
  - `playtest.html`: one button that offers the visitor's Mac or Windows build (C1.2's `/download`),
    the Open Anyway / Run anyway steps with pictures, and what's recorded.
  - `privacy.html`: what's recorded, why, for how long, and how to have it deleted (Q5, Q7).
  - Pictures and clips aren't in git: they live in `~/TokenLimit/website/assets/` and go up with
    each deploy.
  - Files: `tools/website/**`.
- [ ] **B4.2 [B] Page visits:** the Cloudflare Web Analytics snippet (no cookies, no banner needed).
- [ ] **C4.1 [C] Deploy.** `tools/website/deploy.sh` runs `wrangler pages deploy` with the assets
  folder; the domain (Q1) is attached in the dashboard.
  - Done when: the site opens on your phone, the button downloads the right build, and a test
    sign-up lands in D1.

**Sync 4:** [C] merges the website and runs everything once, end to end, against `wrangler dev`.

### Phase 5: the first round [You], with [C]

- [ ] **Y5.1 Your go for the builds.** Then [C] builds Mac and Windows (`KehaiBuild -playtest
  round1`, editor closed) and publishes them (C3.5).
- [ ] **Y5.2 Try it as a tester:** from the website, on your Mac, and on a Windows PC if you can
  borrow one.
- [ ] **Y5.3 Send the link** to your friends.

## 8. Tests

| Part | How |
|---|---|
| The Worker | `node --test` against stand-ins for R2 and D1 (today's pattern), then `wrangler dev` on this Mac |
| The game | the EditMode suite on an APFS clone: no code page, the end page, the fairness tap, the summary's shape, the funnel queue offline |
| The Mac side | `pytest` with a stand-in Worker (`httpx.MockTransport`, as today) |
| End to end | **Kehai → Playtest → Simulate a Playtest Build** in the editor, sending to `wrangler dev`, then to the real service |

## 9. Costs

| | Free | Enough for |
|---|---|---|
| R2 (files) | 10 GB | about 3,000 sessions, plus the builds |
| D1 (rows) | 5 GB, 100,000 rows written a day | far more than friends will make |
| Workers | 100,000 requests a day | thousands of testers a day |
| Pages and Web Analytics | free | the website |
| Domain (Q1) | — | about $10 a year for a .com |
| Telegram, and Claude on your plan | free | — |

R2 may ask for a card when you turn it on; it only charges past the free 10 GB.

## 10. Privacy

- **Asked first.** Nothing is sent before the consent page, and "Play without sending" still works.
- **No names.** A random ID; the nickname is optional. Never the camera image, the microphone or
  the screen (as today).
- **Said plainly** on the privacy page: what's recorded, why (fixing the game, training Karen), how
  long (Q5), and how to ask for deletion: email (Q7) with the ID from the thank-you screen.
- **Deleted on request** with one command (C3.4).

## 11. Not in this plan

- **Steam Playtest.** Later: it replaces the download page, the warnings and new-build downloads.
- **Apple signing.** You've decided not to pay for it.
- **Recording the tester's voice** ("think aloud"): rich, but it breaks the "never the microphone"
  promise; a separate decision.
- **Karen's learning:** [karen-learning.md](karen-learning.md). This plan only carries its data
  and the consent wording.

## 12. Terms

| Term | What it is | How Kehai uses it | Real-world example |
|---|---|---|---|
| Worker | a small program on Cloudflare's servers | receives uploads, counts downloads, stores sign-ups | the code behind a website's contact form |
| R2 | Cloudflare's file storage | sessions, builds, learning datasets | Dropbox, used by code |
| D1 | Cloudflare's small SQL database | the rows: sessions, shifts, answers, the funnel | the database inside a phone app |
| Pages | free website hosting | the website | Squarespace, without the editor |
| Funnel | how many people get through each step | download → launch → agree → finish | a shop seeing how many carts reach checkout |
| `wrangler dev` | Cloudflare's service running on your own Mac | testing before anything goes live | a dress rehearsal |

## 13. Setting up the accounts

**Cloudflare** (about 10 minutes):
1. Sign up at <https://dash.cloudflare.com/sign-up> and confirm your email.
2. In the left menu, open **R2** and turn on the free plan (it may ask for a card).
3. Optional (Q1): **Domain Registration → Register Domains**, and buy the name.
4. Tell Claude. Claude runs `npx wrangler login`; a browser tab opens and you click **Allow**.
   Claude does the rest (C0.1).

**Telegram** (about 3 minutes):
1. Open <https://t.me/BotFather> and send `/newbot`. Pick a name, then a username ending in `bot`.
2. Paste the token it sends after `TELEGRAM_BOT_TOKEN=` in `~/Developer/Kehai/tools/marketing/.env`:
   ```bash
   open -e ~/Developer/Kehai/tools/marketing/.env
   ```
3. Open your new bot in Telegram and press **Start**. Tell Claude, which fills in your chat id.

**Where the keys go** (git ignores both `.env` files; no key is ever written into this repository):

| Key | Where | Who |
|---|---|---|
| Telegram token | `tools/marketing/.env` → `TELEGRAM_BOT_TOKEN` | you |
| Telegram chat id | `tools/marketing/.env` → `TELEGRAM_CHAT_ID` | Claude |
| Upload and admin keys | `tools/playtest/.env`, and Cloudflare's secret store | Claude generates them |
| The service's address | `tools/playtest/.env` → `KEHAI_PLAYTEST_UPLOAD_URL` | Claude, after deploying |
| Cloudflare login | saved on this Mac by `wrangler login` | you click Allow |
