# Playtest v2 build plan: finish the test build

> **Updated 12 November 2026. Planning PR only; implementation is not started by this PR.**
> The original pipeline is built ([current behavior](../../agents/game/PLAYTEST.md)); its live
> connection is not configured on this Mac. V2 has a merged homepage draft, but no live signup,
> download page, no-code flow or cloud-retention implementation. GitHub main `9eb37cd`, open PRs,
> local worktrees and the available build chat were checked: no v2 implementation work found.
> The separate 0.4.0 branch builds learning tools; it leaves real-player integration for later.
> This updates the existing v2 plan rather than starting a competing pipeline.
>
> **Owner's workflow:** send a game ZIP or download link with short opening instructions; the
> game explains the rest, creates an ID and uploads after consent. Both owner computers may be
> off. Download selected sessions later on Windows; training starts only when the owner starts it.
> Karen's training itself belongs to the [0.4.0 plan](0.4.0-build-plan.md).

**How to mark progress:** tick a task only after its “Done when” evidence exists. A plan or code
review is not a live deployment. Record verification beside the task in its implementation commit.

| Area | Verified starting point | Remaining work |
|---|---|---|
| Game | v1 consent, recordings, questions, queued upload and build ZIPs exist | no-code identity, illustrated guidance, v2 feedback and receipt handling |
| Cloud | v1 Worker/R2 code exists, tested with stand-ins | connect account, v2 routes/rows, retained sessions, live verification |
| Owner tools | v1 import/report/replay tools exist | opt-in processing, Windows dataset download, no delete-after-pull |
| Website | homepage draft merged in PR #77 | working downloads, privacy, instructions, signup and deployment |
| First round | no imported sessions found on this Mac | real Mac/Windows download-to-upload checks before sending to players |

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
  inside the game, timestamped bug notes, and a funnel that shows where
  people drop off.
- **Little disk on your Mac:** the cloud keeps everything; the Mac only holds what it's using.
- **Instructions travel with the game:** an illustrated START HERE guide plus a short first-launch walkthrough.
- **Collect now, train later on Windows:** ingestion does not launch analysis, fitting, training,
  model publication or a required job on either owner computer.

## 2. Decisions so far

The original choices were made on 6 November; delivery and deferred Windows training were clarified on 12 November.

| Decision | Why |
|---|---|
| **Cloudflare only:** R2 for files, D1 for rows, a Worker in front, Pages for the website | one account for uploads, the website and marketing videos; measure session sizes before estimating capacity |
| **Everything is kept in the cloud;** the Mac keeps no session files | your Mac is low on disk |
| **No tester code:** the game makes a random ID | the code was only a label (any code was accepted) and a step to get wrong |
| **No Google Form:** three optional typed answers in the game (wording chosen in Q2) | the form was a second step most people skip; answers now arrive with the session |
| **No itch.io:** a `/playtest` page on your own website | one less account, and the same site does marketing |
| **No Apple Developer Program** | your choice. Mac testers do one "Open Anyway" step, shown with pictures on the page |
| **Steam later** | Steam Playtest replaces the download page when strangers test |
| **No Claude API key** | the Claude steps run on your Claude plan through `claude -p` (the default since PR #71) |
| **The consent covers training Karen** | the [0.4.0 plan](0.4.0-build-plan.md) uses real shifts later; consent version travels with each session |
| **A typed code is unnecessary** | an automatic persistent ID groups sessions; it is a label, not authentication or proof that two computers belong to one person |
| **The ZIP can be sent directly** | the website is convenient, not a prerequisite for opening a received ZIP |
| **Training later on another Windows computer** | store first; downloading, reporting, training and publishing are independent actions |
| **No automatic local jobs required on arrival** | Claude and Telegram are optional; neither computer needs to be on to receive sessions |

## 3. Questions for you

Answers updated 12 November. “Open” means the owner asked for an explanation, not approval.
Build independent plumbing first; confirm retention and the contact before public distribution.
Telegram and Claude remain optional for collection and the Windows handoff.

| # | Question | Answer / remaining choice |
|---|---|---|
| Q1 | A domain for the website? | **Free for now.** Use the assigned Pages address; no domain purchase |
| Q2 | The three typed questions | **Owner delegated the wording.** Use: “What were you trying to do, and what was confusing?”, “When did Karen feel scary, unfair or annoying? Describe a moment.”, “What would you change first?” All optional |
| Q3 | An optional nickname box at the end? | **Yes** |
| Q4 | “Was that fair? Y / N” for 4 seconds after each catch? | **No.** Do not implement an after-catch fairness prompt |
| Q5 | How long to keep the data? | **Open: explain first.** Retain means keep stored before automatic deletion. Replays, small feedback rows and training records can have different lifetimes. Suggested starting point: 12 months for raw sessions; decide the lifetime of the other data separately. Training later requires that its source records still exist |
| Q6 | A “new build available” line in the game? | **Yes** |
| Q7 | Which contact email handles deletion requests? | **Open: explain first.** Show an address where a tester can send their automatic ID and ask to remove their records; no tester email is required to play. Choose an existing inbox or a dedicated game-support address |
| Q8 | Keep rounds (`round1`, `round2`…)? | **Open: explain first.** A round labels a batch of tests before/after fixes, like named experiment batches. Build versions remain recorded too. Suggested: yes, assigned by the developer; players type nothing |

## 4. What a tester does

| | Now (4 October) | v2 |
|---|---|---|
| What you send | itch link, password, a code, the form link | **the game ZIP or one download link**, plus a short opening message |
| Download | password-protected itch page | your website, which offers their Mac or Windows build |
| First launch | Mac: "Open Anyway". Windows: "Run anyway". Then type `T07` | the same warnings; **nothing to type** |
| Consent | two buttons | the same, one click: **Play and send** or **Play without sending** |
| Playing | Shift+F7 marks a bug | the same; no after-catch question |
| The end | 4 taps in the game, then a 9-question Google Form | 4 taps, 3 optional typed answers and an optional nickname, **all in the game** |

**Step by step:**
1. They get your ZIP directly, or open the download link and choose their platform. The ZIP includes an illustrated START HERE guide that opens offline.
2. They open the game. On a Mac, the first time: **System Settings → Privacy & Security → Open Anyway**.
3. A short, skippable walkthrough explains the first task, controls and bug reporting; consent offers **Play and send** or **Play without sending**. Help stays available from the menu.
4. They play up to three shifts.
5. They answer, and the game sends it automatically if they agreed. The end screen distinguishes sent, queued offline and kept locally; it shows their ID for deletion requests. No upload website or tester account is needed.

## 5. What's recorded, and where it's kept

Keep the existing [recordings](../../agents/game/PLAYTEST.md#whats-recorded), add the v2 feedback and 0.4.0 learning records, and change where they are kept. All retention below remains subject to Q5 and deletion requests.

| What | Size | Where | Kept |
|---|---|---|---|
| The session zip: 3D replays, the session log, Karen's logs and Ledger, the game's log | measure on actual recordings | R2 `sessions/<round>/<id>/<launch>.zip` | proposed in Q5 |
| The session row: computer, frame rate, minutes played, how it ended | tiny | D1 `sessions` | agree in Q5 |
| A row per shift: length, clocked out, caught, times spotted, closest she got, trick sounds, lowest energy | tiny | D1 `shifts` | agree in Q5 |
| Answers: the 4 taps, the 3 typed answers, the nickname | tiny | D1 `answers` | agree in Q5 |
| Moments: bug notes, each with its shift and replay time | tiny | D1 `moments` | agree in Q5 |
| The funnel: download, first launch, agreed, first shift, finished | tiny | D1 `events` | agree in Q5 |
| Page visits | — | Cloudflare Web Analytics (no cookies) | Cloudflare's own |
| Claude's summary of each session | a few KB | D1 `summaries`, written by an optional later analysis run | agree in Q5 |
| Email sign-ups from the website | tiny | D1 `signups` | until they unsubscribe |
| Karen's learning records ([0.4.0](0.4.0-build-plan.md)) | measure on actual recordings | preserved in the session zip; derived datasets made on demand | agree in Q5 |

## 6. How it fits together

```
the website (Pages)  ── download button ──► Worker /download/<mac|windows> ──► R2 builds/   (counts it)
                     ── email sign-up   ──► Worker /signup                ──► D1 signups

the game  ── launch, consent, each shift, the end ──► Worker /events     ──► D1 events (the funnel)
          ── the session zip ───────────────────────► Worker /sessions   ──► R2 sessions/
          ── its summary ───────────────────────────► Worker /summaries  ──► D1 sessions, shifts, answers, moments

cloud ingestion (always available, no owner computer required)
          ── stores the raw session and consent/build/schema metadata durably
          ── accepts the structured rows with retry-safe session IDs
          ── does not launch a trainer or require immediate local analysis

you, later, on Windows
          ── choose a round/build and download a fixed dataset snapshot
          ── verify file hashes and schema versions, then import into tools/learn
          ── start the chosen training command manually; evaluate; publish only if the checks pass

optional analysis / notifications (when explicitly run or enabled)
          ── read facts, ask Claude for a summary, update D1/report, notify Telegram
          ── opening a replay downloads only that session to a bounded local cache
```

The game carries an upload-only key. Listing and downloading are authenticated; for the Windows
handoff, add a read-only dataset credential so the training PC need not hold the delete/admin key.
The admin credential stays on the owner's administration machine. No credentials go into git,
invitation messages, exported manifests or logs. Downloading/importing never deletes cloud data.

A **dataset snapshot** is a fixed list of session files, versions and hashes. Kehai uses it to
repeat a training experiment on the same examples; it is like a saved photo album selection that
stays unchanged when new photos arrive. It is not a model, and fetching it does not train one.

## 7. The build

**Who:** one builder, plus **[You]** for accounts, decisions and the real-device checks. Existing
C/B task IDs are retained for references; they do not assign concurrent agents. Work in an
isolated branch/worktree, following [RULES.md](../../agents/RULES.md). Tick and verify each phase
before proceeding. Training stays owner-started. Per-model publication approval is no longer
required; evaluation/compatibility checks still apply ([owner answers](../../agents/HANDOVER.md#12-november-owner-answers)).

### Phase 0: accounts and answers [You]

- [ ] **Y0.1 Cloudflare:** an account, with R2 turned on ([§13](#13-setting-up-the-accounts)).
- [ ] **Y0.2 Optional Telegram:** configure only if wanted; otherwise record “deferred” and leave collection unblocked ([§13](#13-setting-up-the-accounts)).
- [ ] **Y0.3 Answer [§3](#3-questions-for-you).**
- [ ] **C0.1 [C] Connect:** after Y0.1, `npx wrangler login` (you click **Allow**), then the bucket,
  the D1 database and upload/admin secrets; add the read-only Windows credential in C1.6. If Y0.2 is chosen, `run_job.py telegram-setup` fills in your chat id.

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
- [ ] **C1.3 Keeping to Q5.** A cloud scheduled run applies the agreed lifetimes to replays, rows and learning records. It requires no local computer; deletion requests override retention.
  - Files: `tools/playtest/worker/src/cron.js`, `wrangler.toml`.
- [ ] **C1.4 Deploy and prove receipt** (after C0.1). Done when: a real consented session from
  a packaged build reaches R2 and D1 while local processing is stopped. Match its session ID,
  checksum and row counts. The current editor simulation intentionally disables uploads.
- [ ] **C1.5 Durable, retry-safe ingestion.** Session ZIP receipt and structured-row receipt may
  arrive in either order. Track partial completion, retry without duplicate sessions/moments,
  reject conflicting overwrites, and reconcile interrupted uploads. Enforce the existing size
  limit visibly; report oversized sessions instead of retrying forever.
  - Done when: duplicate, reordered, interrupted, rejected and offline/retry cases preserve one
    coherent session; no training or local job is invoked. No local pull deletes the cloud copy.
- [ ] **C1.6 Dataset access.** Add authenticated read-only manifest/download access for C3.7,
  including consent, hashes, schema versions, round and build; keep delete/publish admin-only.
  - Done when: the read-only key can fetch eligible sessions but cannot write, delete or publish;
    unauthenticated requests cannot list or download private data.

### Phase 2: the game [C]

- [ ] **C2.1 No code.** On first launch `TesterState` generates and persists a high-entropy random
  ID; nobody types it. Keep existing tester IDs compatible, and use a separate unique session ID
  per launch. Reinstallation/new computers are not silently assumed to be the same person.
  This is a grouping label, not authentication.
  - Files: `PlaytestConfig.cs`, `PlaytestScreens.cs`.
  - Done when: a fresh install goes from launch to the consent page with nothing typed.
- [ ] **C2.2 Consent.** The text adds: *"…and may be used to train Karen."*
  - Files: `PlaytestScreens.cs`, `PLAYTEST.md`.
  - Done when: the consent version and permission travel with every upload; decline sends no
    session, answers or identifiable event pings. A privacy-safe download count is separate.
    Previously collected v1 data is not assumed to permit training without compatible consent.
- [ ] **C2.3 The end page.** The 4 taps, the 3 typed answers (Q2) and the nickname (Q3), all
  optional. The form button goes, and so does `formUrl` (from `PlaytestConfig` and `KehaiBuild`).
  - Files: `PlaytestScreens.cs`, `PlaytestConfig.cs`, `KehaiBuild.cs`, `PlaytestTests.cs`.
- **C2.4 Removed from scope by the owner (12 November):** no after-catch fairness prompt.
  This is not completed implementation. Keep timestamped bug notes and optional end feedback.
- [ ] **C2.5 The funnel.** After consent, send allowed launch/consent/shift/end events; buffer
  pre-consent session events locally and discard them on decline. Queue consented events offline.
  - Files: `PlaytestFiles.cs` (`PlaytestUploader`), `PlaytestSession.cs`.
- [ ] **C2.6 The summary.** When the session is packed, a small JSON goes to `/summaries` with the
  zip: the computer, the frame rate, the per-shift numbers (from `ShiftAnalysis`), the answers and
  the moments.
  - Files: `PlaytestSession.cs`, `PlaytestFiles.cs`.
- [ ] **C2.7 A new build** (Q6). At launch the game asks `/latest`; if there's a newer build, the
  main menu says so, with a button to the download page.
  - Files: `PlaytestSession.cs`, `MainMenu.cs`.

- [ ] **C2.8 First-launch illustrated walkthrough.** Brief steps for opening/playing, the first
  clock-in/task, essential controls, Shift+F7, finishing and sending. Use the actual game/control
  sources; teach controls without revealing Karen's tricks. Skip/reopen from Help, support mouse
  and keyboard, and keep the consent choice explicit.
  - Done when: a new tester can reach the first task unaided; text fits at 1280×720 and the
    minimum supported window size, with legible contrast and no audio-only instructions.
- [ ] **C2.9 Bundle START HERE.** Add an offline illustrated HTML guide plus a plain-text fallback
  to each ZIP: extract all files, open the correct app, platform opening warnings, then the
  in-game walkthrough. Keep the Mac helper beside the app and Windows data beside the executable.
  Source/generated assets follow the repository's code-and-docs rule.
  - Files: `KehaiBuild.cs`, new guide source under `tools/playtest/`.
  - Done when: both freshly downloaded ZIPs include working guides with no external assets or
    broken links; the build label/round agrees with `build.txt` and `playtest.json`.
- [ ] **C2.10 Honest completion and retries.** Show sent only on server acknowledgement; distinguish
  queued, rejected and local-only sessions. Preserve unsent data after a crash/restart.
  - Done when: offline, timeout, oversized upload, crash recovery and consent-declined paths are
    tested; none claims success or asks a tester to manually upload as the normal flow.

**Phase 2 check:** run the EditMode suite using the repository's Unity/clone rules. No second
Unity while the owner's editor is open.

- [ ] **B2.1 The look of the playtest pages** (after the phase 2 logic checks): the consent and end pages,
  readable at 1280×720, in the main menu's style. A quick pass; you redesign before launch.
  - Files: `PlaytestScreens.cs` (layout only).

### Phase 3: optional review and later Windows training handoff

- [ ] **C3.1 Decouple receipt from analysis.** Disable v1's automatic pull/delete behavior for
  the v2 service. Default to manual analysis; any periodic summary/Telegram job is explicitly
  opt-in and independent of collection. Process in temporary storage and retain raw cloud data.
  - Files: `tools/marketing/km/playtest/inbox.py`, `summary.py`, `notify.py`, a new `cloud.py`,
    the `run_job.py work` hook and settings.
  - Done when: uploads work with both owner computers off; starting the existing scheduled job
    neither drains cloud storage nor starts training. An explicit later analysis can catch up.
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
- [ ] **C3.6 Replace the invitation workflow.** Retire code generation and the form/itch templates
  from the v2 path. Put one short copyable ZIP/link + opening-instructions message in the README.
  Preserve v1 import compatibility while older builds exist.
  - Done when: a complete round needs no per-person code list, form or invitation account, and
    `pytest tools/marketing` passes with a stand-in Worker.
- [ ] **C3.7 Windows dataset download, on demand.** Add a cross-platform CLI outside the Mac-only
  scheduler, under `tools/playtest/`. Select round/build/session IDs and output directory; write
  a fixed manifest with consent/schema/build IDs, sizes and SHA-256 hashes. Resume interrupted
  downloads, verify before import, reject unsafe ZIP paths and deduplicate by session ID.
  - Done when: on a real Windows PC, PowerShell can list/select/download a snapshot with the
    read-only key, stop/restart it, and re-run without duplication. It needs no Unity, `open`,
    launchd, Telegram or Claude for downloading. It never deletes cloud sessions or starts training.
- [ ] **C3.8 Training handoff and deletion.** Integrate with 0.4.0's `tools/learn/dataset.py`
  after that branch lands; it already has add/split/forget/table locally, not a cloud downloader.
  Preserve player grouping and held-out splits, exclude incompatible/unconsented sessions, and
  include the learning records required by the selected learner. Replays alone are not a training
  dataset. Export deletion records so a previously downloaded dataset can honor removals.
  - Done when: fixture and real consented sessions import on Windows; missing learning records
    produce a clear rejection/report. A revoked player is excluded from new snapshots and removed
    from a local dataset on reconciliation. Record the exact tested PowerShell commands in the
    README; command names are to be implemented, not advertised as working today.
  - Training starts manually under the 0.4.0 plan. Publication follows its checks without a
    per-model owner approval; it is never triggered just by receiving a session.
    The handoff test is not permission to run a full training job.

### Phase 4: the website and download instructions

- [x] **B4.0 Homepage draft.** `tools/website/index.html`, merged in PR #77. Its signup is a
  preview and sends nothing; this checkbox does not mean the site is deployed.
- [ ] **B4.1 The remaining pages and connections.** In `tools/website/`, plain HTML, CSS and a
  little JavaScript, no framework. Reuse the existing homepage.
  - `index.html`: connect the email signup to C1.2's `/signup`.
  - `playtest.html`: one button that offers the visitor's Mac or Windows build (C1.2's `/download`),
    the Open Anyway / Run anyway steps with pictures, and what's recorded.
  - `privacy.html`: what's recorded, why, for how long, and how to have it deleted (Q5, Q7).
  - Pictures and clips aren't in git: they live in `~/TokenLimit/website/assets/` and go up with
    each deploy.
  - Files: `tools/website/**`.
- [ ] **B4.2 [B] Page visits:** the Cloudflare Web Analytics snippet (no cookies, no banner needed).
- [ ] **C4.1 [C] Deploy.** `tools/website/deploy.sh` runs `wrangler pages deploy` with the assets
  folder; use the assigned free Pages address (Q1).
  - Done when: the site opens on your phone, the button downloads the right build, and a test
    sign-up lands in D1.

**Phase 4 check:** run the combined game/service/download journey against `wrangler dev`, then the deployed service with a real packaged build.

### Phase 5: finish the test build, then the first round

- [ ] **Y5.1 Your go for the builds.** Then [C] builds Mac and Windows (`KehaiBuild -playtest
  round1`, editor closed) and publishes them (C3.5).
- [ ] **Y5.2 Try it as a tester on both platforms.** Download/extract the actual Mac and Windows
  ZIPs; follow the bundled guide, skip/reopen Help, consent, play, mark a bug, answer and quit.
  Keep both owner processing jobs off during upload; later check the replay, answers and metadata.
  - Done when: record platform/build/session IDs and results; test decline and offline retry too.
- [ ] **C5.1 Prove the later handoff.** Leave a received session in the cloud, then fetch/import
  it on Windows in a separate later run with analysis off. Verify it remains in R2 afterward.
- [ ] **C5.2 Release checklist and handover.** README covers account setup, updating round/version,
  making both ZIPs, guide assets, verifying the service, sending one message, viewing a replay,
  later Windows download, retry/deletion and how to start training separately. No keys or player
  data are committed. All preceding required checks pass before calling the test build finished.
- [ ] **Y5.3 Send the ZIP or link** to your friends at the real-player round in [NOW.md](../NOW.md#next-in-order), after both the vertical slice and finished test build. No outreach is sent by this planning PR.

## 8. Tests

| Part | How |
|---|---|
| The Worker | `node --test` against stand-ins for R2 and D1 (today's pattern), then `wrangler dev` on this Mac |
| The game | the EditMode suite on an APFS clone: no code page, the end page, no after-catch prompt, the summary's shape, the funnel queue offline |
| The Mac side | `pytest` with a stand-in Worker (`httpx.MockTransport`, as today) |
| Instructions | Real Mac/Windows ZIPs; offline guide, first-launch walkthrough, skip/reopen, legibility and correct controls |
| End to end | Packaged build (or an explicit upload-enabled test config) → local Worker → live service; the default editor simulation does not upload |
| Deferred processing | Both owner jobs off at receipt, later manual analysis; no pull deletes cloud data and no upload starts training |
| Windows handoff | PowerShell downloader with read-only key: fixed manifest, hashes, resume, consent/schema filtering, player splits and deletion reconciliation |

## 9. Costs

| | Free | Enough for |
|---|---|---|
| R2 (files) | 10 GB | roughly 1,000 sessions at 10 MB each, before allowing for builds; actual sizes unmeasured |
| D1 (rows) | 5 GB, 100,000 rows written a day | far more than friends will make |
| Workers | 100,000 requests a day | thousands of testers a day |
| Pages and Web Analytics | free | the website |
| Domain (Q1) | — | about $10 a year for a .com |
| Telegram, and Claude on your plan | free | — |

R2 activation may ask for a payment method. Storage and request allowances are separate;
recheck [Cloudflare pricing](https://developers.cloudflare.com/r2/pricing/) before deployment.
The estimates above are planning figures, not an account balance or a measured session capacity.

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
- **Training or publishing Karen:** [0.4.0](0.4.0-build-plan.md). This plan carries consented
  records to the Windows computer; it does not authorize training, model selection or publication.
- **0.4.1 gameplay changes:** still in the owner's Notes app; they need their own plan first.

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
3. Use the free Pages address for now (Q1); no domain purchase is needed.
4. Tell Claude. Claude runs `npx wrangler login`; a browser tab opens and you click **Allow**.
   Claude does the rest (C0.1).

**Telegram, optional** (about 3 minutes; not required for uploads or Windows downloads):
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
| Read-only dataset key (new in C1.6) | Worker secret store and a private environment setting on the Windows PC; never in an exported dataset | builder generates it; owner installs it on Windows |
| Upload and admin keys | `tools/playtest/.env`, and Cloudflare's secret store | Claude generates them |
| The service's address | `tools/playtest/.env` → `KEHAI_PLAYTEST_UPLOAD_URL` | Claude, after deploying |
| Cloudflare login | saved on this Mac by `wrangler login` | you click Allow |
