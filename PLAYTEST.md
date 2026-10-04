# Playtesting

How strangers play Kehai and what comes back: the loop, what a tester sees, what's recorded,
where it goes, and the steps to run a round. Decided with the owner on 2026-10-04.

> **Status:** step 1 (the playtest mode in the game) is built. Steps 2–4 are next; see
> [Building it](#building-it).

---

## The loop

```
you make tester codes ─► build a playtest round ─► testers download it (itch.io or a link)
        ▲                                                    │
        │                                                    ▼
   fix, next round ◄─ report, Telegram, Claude's summary ◄─ they play; the session uploads
```

- **Async.** Testers play on their own, whenever they like. You don't hear them think; you get
  everything they did as a 3D replay you can rotate and fly around.
- **Up to three shifts, fresh career.** Each tester starts as a new employee with an Aiko who
  knows nothing about them. After shift 3 the game suggests stopping; they can keep going.
- **Questions:** four one-tap questions and one optional line in the game (once per tester), and
  a Google Form with the longer questions afterwards, linked by their tester code.
- **Rounds.** Every playtest build belongs to a round (`round1`, `round2`…), so reports never mix
  a round with the fixes that came after it.

## What a tester goes through

1. **You send:** the itch.io page and password (or the direct link), their **tester code**
   (like `T07`), and the form link.
2. **They download and open it.** Mac: the first launch is blocked until **System Settings →
   Privacy & Security → Open Anyway**. Windows: SmartScreen warns, **More info → Run anyway**.
3. **First launch:** they type their code, then answer one question:

   > *This build records what happens in the game while you play: where you go, what you press,
   > what Aiko does, and how smoothly it runs. Not your camera, microphone or screen. When you
   > finish, it sends that to the developer, who watches your session as a 3D replay to see what
   > was confusing.* **[Send my sessions] [Keep them on this computer]**

4. **They play.** Shift+F7 marks a bug and opens a one-line box for what went wrong.
5. **After shift 3**, once: *"That's 3 shifts. Thank you! You can stop here and answer four quick
   questions, or keep playing as long as you like."*
6. **Finishing** (from that, or by quitting): the questions below (once per tester), then
   *Sending your session…*, then *Thank you, T07*, with a button to the longer questions.
   Offline, the session waits and goes the next time the game opens. After a crash, the next
   launch sends what was left: the session log up to its last two seconds, and the replay of the
   shift it crashed in as an unfinished `.krec.part`.

### The in-game questions

| # | Question | Answers |
|---|---|---|
| 1 | Aiko felt… | Scary · Unfair · Annoying · I didn't notice her |
| 2 | Did you know what to do? | Yes · Mostly · No |
| 3 | Did you get lost in the store? | Never · Sometimes · Often |
| 4 | Would you play more? | Yes · Maybe · No |
| 5 | Anything break or confuse you? | One optional line |

Every one-tap question can be skipped. The game's list is `PlaytestScreens.Questions`; a test
keeps this table in step with it.

### The form (draft, for you to make in Google Forms)

First field: **Tester code** (pre-filled from the game's link). Then, all optional:

1. What were you trying to do in your first minute? Did you work it out?
2. When did you first notice Aiko? What did you think she was?
3. Describe a moment that felt unfair, if there was one.
4. Describe a moment that felt great, if there was one.
5. What did you never figure out?
6. Was anything hard to read or hear (text, sounds, the dark)?
7. Which games did it remind you of?
8. Would you wishlist it? Why or why not?
9. Anything else?

To link it: in the form, **⋮ → Get pre-filled link**, type `{code}` in the Tester code field, copy
the link, and put it in `KEHAI_PLAYTEST_FORM_URL` (the game replaces `{code}` with theirs).

## What's recorded

Only in a playtest build (one with `StreamingAssets/playtest.json`); a normal build records
shifts for the player's own reports and sends nothing.

| What | Where it comes from |
|---|---|
| The whole session as 3D replays: each shift (`shift_NN_*.krec`) and the stretches between them, from launch to the first clock-in and after each clock-out (`interlude_NN_*.krec`) | `ReplayRecorder` |
| Each shift's data and clip markers, including Shift+F7 bug marks | `ShiftRecorder`, `ClipMarkerRecorder` |
| `session.jsonl`, one line per event: the computer (OS, CPU, GPU, RAM, screen, quality); every menu and panel opened and closed (main menu, settings, pause, task list, F1 map, F2 replay, review, playtest screens); the keys that matter (E with what it was aimed at, or "nothing"; Q, C, F1, F2, F7, F8–F10, L, Esc); where the tester stood and looked, twice a second, with the replay file and time it belongs to; frame rate every 5 s; errors; focus lost and regained; shifts; bug notes; answers | `PlaytestSession` |
| Aiko's thought logs and her ledger (what she learned about this tester) | `aiko_logs/`, `aiko_ledger.json` |
| The game's own log (and the one before, after a crash) | `Player.log` |

Never recorded: the camera image (webcam blinking stays off unless they turn it on, and even then
only eye openness is read, live), the microphone, the screen, their name, anything outside the game.

On the tester's computer, sessions live in `<persistent data>/playtest/` (`sessions/` as they
happen, `outbox/` packed and waiting). `<persistent data>` is
`~/Library/Application Support/TokenLimit/Kehai` on a Mac and
`%USERPROFILE%\AppData\LocalLow\TokenLimit\Kehai` on Windows.

## Where it goes

```
game ── PUT /sessions/<round>/<code>/<launch time>.zip (X-Kehai-Key) ──► Cloudflare Worker ──► R2 (private)
                                                                                              │
your Mac (the scheduled "work" job, every 5 minutes) ◄── pulls new sessions, deletes them from R2
   └─► ~/TokenLimit/playtests/<round>/<code>/<launch time>/   (unzipped)
        ├─► the combined report (HTML, on your Mac)
        ├─► Telegram: who played, how long, how it ended, the report
        ├─► Claude: a summary of the session (what confused them; Aiko too harsh or too soft)
        └─► each bug note to Telegram with its replay moment; your tap files it as a public
            GitHub issue in Claude's neutral words, with no tester code and no replay link
```

The build carries an upload key that can only add sessions, never read them. The Worker (step 2)
will take only zips, under 50 MB.

## Running a round

1. **Settings.** Copy `tools/playtest/.env.example` to `tools/playtest/.env` (it stays out of
   git) and fill in the round, the upload address and key (step 2), and the form link.
2. **Build.** **Kehai → Build → Playtest → macOS** and **→ Windows**, or headless with the editor
   closed:

   ```bash
   Unity -batchmode -nographics -projectPath . -executeMethod KehaiBuild.MacOS -playtest round1
   Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiBuild.Windows -playtest round1
   ```

   Each lands in `Builds/playtest-round1/` with a zip beside it to hand out.
3. **Hand out** the zips (itch.io restricted page, or a direct link), a tester code each, and
   the form link.
4. **Watch them arrive** (step 3): Telegram, the combined report, the replays.
5. **Fix, and start `round2`** with new testers.

**Trying it yourself:** in the editor, **Kehai → Playtest → Simulate a Playtest Build in the
Editor** runs the playtest mode in play mode (no upload; sessions are packed into the outbox),
and **Kehai → Playtest → Forget the Tester on This Computer** starts over from the code screen.
A built game takes `-playtest-config <file>` to use a different `playtest.json`. Headless,
`-playtest-code` and `-playtest-consent send|keep` stand in for the screens.

## Building it

- [x] **1. The playtest mode in the game:** `PlaytestConfig`, `PlaytestSession`,
  `PlaytestScreens`, `PlaytestFiles` (`_Game/Playtest/`); interludes in `ReplayRecorder`;
  `KehaiBuild -playtest <round>`.
- [ ] **2. The upload service:** a Cloudflare Worker and a private R2 bucket
  (`tools/playtest/worker`). *Needs: a Cloudflare account and an API token.*
- [ ] **3. Your Mac's side:** pull new sessions in the scheduled `work` job, the combined report,
  Telegram, Claude's summaries, bug notes to approve as GitHub issues. *Needs:
  `TELEGRAM_BOT_TOKEN` and `ANTHROPIC_API_KEY` in `tools/marketing/.env`.*
- [ ] **4. The page and the form:** itch.io page text, the Google Form, the tester brief.

## Decisions

| Date | Decision |
|---|---|
| 2026-10-04 | Async sessions; the record is the in-game 3D replay, not a screen recording |
| 2026-10-04 | Upload after consent, to Cloudflare R2 through a Worker; the Mac pulls every 5 minutes |
| 2026-10-04 | Quick questions in the game, the long ones in a Google Form, joined by the tester code |
| 2026-10-04 | Up to 3 shifts suggested, a fresh career per tester; tester codes, not names |
| 2026-10-04 | Builds handed out on a restricted itch.io page and as direct links |
| 2026-10-04 | Bug notes become public GitHub issues only after the owner approves each one, reworded, without tester codes or replay links |
| 2026-10-04 | The combined report is an HTML page on the owner's Mac |
