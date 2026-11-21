# Testing and training with less friction

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 1: "Improve testing and training: reduce
friction in use, performance, efficiency and comfort." The questions (Q numbers) are in the plan's
[open questions](../0.5.2-plan.md#11-open-questions).

**Measured on 17 November without running Unity.** Every other number is marked *estimate*.
How each one was measured is in [§11](#11-how-this-was-measured).

1. [What gets in the way today](#1-what-gets-in-the-way-today)
2. [One command that runs a test safely](#2-one-command-that-runs-a-test-safely)
3. [A warm clone](#3-a-warm-clone)
4. [Testing with your editor open](#4-testing-with-your-editor-open)
5. [The batch slowdown](#5-the-batch-slowdown)
6. [Running on the Windows PC from this Mac](#6-running-on-the-windows-pc-from-this-mac)
7. [Runs that survive interruption](#7-runs-that-survive-interruption)
8. [Knowing when a run ends](#8-knowing-when-a-run-ends)
9. [Art and data to Windows](#9-art-and-data-to-windows)
10. [Working within the usage limits](#10-working-within-the-usage-limits)
11. [How this was measured](#11-how-this-was-measured)

---

## 1. What gets in the way today

| What you saw | What the code and the disk show (17 November) |
|---|---|
| A dead job's lock blocked every later job | **Seven copies of the lock code, two behaviours.** The marketing tools (`km/lock.py`, `render_shot.sh`, `editor/render.mjs`) take over a lock whose holder has died. `tools/belief/run_unity.sh`, `tools/rl/train_editor.sh`, `tools/rl/record_demos_editor.sh` and `tools/tricks/record_tricks.sh` only try `mkdir` and give up. `record_tricks.sh` has no clean-up on exit at all, so a stopped trick check always leaves the lock behind |
| A 6.5 GB clone left on disk | Each script deletes its clone in an `EXIT` trap. A trap can't run when the process is killed outright (a crash, `kill -9`, a session or the Mac stopping), and nothing cleans up afterwards |
| The editor prefs left pointing at the clone | The same trap restores them. It also always writes `~/Developer/Kehai`, whatever they said before, so it would undo a switch to another project (HANDOVER names `.claude/worktrees/0.5.0-build` as the current 0.5.0 project) |
| Hand-run set-up and clean-up | The recipe is five steps in [HANDOVER §6](../../../agents/HANDOVER.md#6-how-to-work-on-it), copied into four scripts. Build sessions also wrote their own one-off runners: 61 `kehai`-named files and folders sit in `/private/tmp`, mostly logs and scripts |
| The run register can be left stuck too | Its own short lock "is never automatically stolen if stale", and a killed wrapper leaves only a start event ([its README](../../../../tools/reports/README.md)) |
| Two full copies of your private files | The main folder and `.claude/worktrees/0.5.0-build` (4.4 GB, HANDOVER's "current 0.5.0 project"). Compared on 17 November: the same store scene, the same art files (bar two ML-Agents timer files), and project settings that differ only in the version number (0.1.0 against 0.5.0). Which one is the source decides what a test clones (Q20) |
| Leftovers | Three old verify data folders (`Kehai-verify`, `-041`, `-main`: 25 MB in all) and their three prefs files. Two of them may belong to the 0.5.1 session working now, so nothing was touched |
| Slow test starts | The clone copies **all** of `~/Developer/Kehai`, worktrees included, then deletes `.claude/worktrees`: 78,202 files copied for nothing (41 s, §11). In 0.5.1's first test run (15 November), Unity spent **87.7 s** importing and compiling before **22 tests ran in 10 s**; the scripts compiled twice, the second time because "define symbols changed" |
| The clone can test the wrong code | `git archive HEAD \| tar -x` lays the branch over a copy of the main folder. It adds and overwrites files but never deletes, so a file the branch removed or renamed stays in the clone and still compiles |
| Batch mode slower than expected | See [§5](#5-the-batch-slowdown): the "3.6 times" is old; the gap still unexplained was last measured at 2.25 times |
| The same seed, different results | One shift with the same seed ran 183, 200 or 208 s ([KAREN_RESULTS](../../../agents/results/KAREN_RESULTS.md#karen-040-what-the-build-measured-1112-november)). 0.4.0's build notes blame timers that read the wall clock (`Time.unscaledTime`) in the blink and PA code while batch mode runs on a fixed step |
| Runs lost when interrupted | An ablation starts again from nothing (its list of finished shifts lives only in memory). Training can continue, but only by hand (`--resume`). The belief fits resume by stage, added on 13 November after a session's death lost the first try |
| Nothing says when a run ends | No run sends a notice. The marketing pipeline has a Telegram sender, but its bot token isn't set on this Mac (checked that it's empty, not read) |
| Windows by hand | `kehai-assets` has one release (11 November): two zips, one of project files and one of game data (sizes on the [scenes page](scenes.md#7-art-and-data-to-windows)). The repo itself holds only a README: no history, no Git LFS. This Mac has no SSH entry for the PC and no `git-lfs` |
| Sessions hit the usage limit | Uncommitted work lost and subagents killed; three Opus subagents at once used it up fastest. Long jobs were children of the session that started them |
| Missing doc | `tools/learn/` has no README |

---

## 2. One command that runs a test safely

**What it does, for example `kehai-run tests` (the EditMode suite):**

1. **Checks first:** is your editor open (then it stops and says so, or offers the Windows
   lane, [§4](#4-testing-with-your-editor-open)); is there enough free disk for the clone; is the lock free.
2. **Takes the lock** with the holder's process id *and* its start time, so a reused process id
   isn't mistaken for a live holder. A lock whose holder has died is taken over, and the log says so.
3. **Writes a run card** before touching anything: the clone's path, the editor prefs' values
   now, the product name and data folder. This is what makes clean-up possible after a crash.
4. **Makes the clone** of only `Assets`, `Library`, `Packages` and `ProjectSettings`, lays the
   branch on, and **removes files the branch doesn't have**.
5. **Runs Unity** silent, with a time limit, a log file and a progress line every 30 s.
6. **Cleans up:** copies the results out, puts the prefs back to the values from step 3, deletes
   the clone, its data folder and prefs file, frees the lock.
7. **When stopped:** Ctrl-C, closing the terminal or a session ending runs step 6. A crash or
   `kill -9` can't; the **next run** (or `kehai-run doctor`) reads the dead run's card and does step 6 for it.
8. **Records and tells:** start and end go into the run register (`tools/reports/register.py`,
   which exists), and one notice goes out ([§8](#8-knowing-when-a-run-ends)).

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. One command for every job (my pick)** | `tools/run/`, Python standard library; the four shell scripts call it | one lock, one clean-up, one place to fix; testable without Unity (a stand-in "Unity" that exits, fails or hangs) | a rewrite of four scripts' plumbing; their flags must stay the same |
| B. A shared shell file each script sources | fix the lock and trap once in `lib.sh` | small change | shell can't read a dead run's card well; still four scripts to keep alike |
| C. Keep the recipe by hand, with a checklist | — | nothing to build | the same leftovers again |

**Heavy steps stay one at a time and watched** (your rule). The command runs one job; when it
ends it can *print* the next step's command, but it never starts it. Whether a progress line and
a notice at the end count as "watched" is Q4.

**Cost:** a few hours of agent work; testing it needs no Unity time (the stand-in).

---

## 3. A warm clone

**What it is:** a test copy of the project that's kept between runs instead of made fresh and
thrown away each time. Unity's cache in it (`Library`) stays at the same path, so the next run
only re-imports and recompiles what changed.

**Today:** every run clones (49 s), Unity imports and compiles at a new path (87.7 s in 0.5.1's
run), then the clone is deleted (29 s). The timings are in [§11](#11-how-this-was-measured).

| Option | How | Pros | Cons |
|---|---|---|---|
| A. A fresh clone each run (today) | as now, but only the four folders | nothing kept on disk | about 2 min of copying and deleting per run (measured), plus Unity's full start |
| **B. A warm clone on this Mac, opt-in (my pick, if Q6 allows)** | `kehai-run --warm`: one clone at a fixed path, refreshed by sync (the branch's tracked files, your private files from the main folder, deletions included) | estimate: a start of 20–40 s instead of about 90 s; no copy or delete | keeps disk: *estimate* 2–5 GB, because Unity rewrites its cache and the copy stops sharing space with the original; a sync bug could test stale files (the sync is checked against `git ls-files`) |
| C. A warm clone on Windows only | the same, on the PC's 2 TB | no disk cost on the Mac | needs [§6](#6-running-on-the-windows-pc-from-this-mac) first |
| D. Fix the second compile only | find why "define symbols changed" at every batch start, and pin it | estimate: 10–20 s a run, in every option | needs Unity to check |

**My pick:** D in every case; B on the Mac if you agree to the disk, else C.
**Your answer (Q6, 21 November):** C, on the PC, until you have the external SSD.

---

## 4. Testing with your editor open

**The limit:** this Mac has 8 GB, so two Unitys can't run at once, and the rules forbid it.

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. A Unity-free lane on the Mac (my pick)** | run the tests that need no scene under plain .NET. `tools/build/check_csharp.py` already compiles the game and its tests without Unity, and 0.5.0 already ran its career, guide and trick rules this way | a few minutes, not the full suite's time; *estimate* under 1 GB of memory; your editor stays open | only part of the suite: **at most about 196 of the suite's ~530 test methods** sit in files that never make a scene or a game object (a quick count, so an upper bound); anything that calls into the engine fails outside Unity, so each test is labelled |
| **B. A Windows lane** | the same command with `--on pc` runs the full suite on the PC | the whole suite; the Mac untouched | needs [§6](#6-running-on-the-windows-pc-from-this-mac) and the project up to date on the PC ([§9](#9-art-and-data-to-windows)) |
| C. In your open editor | through the editor's bridge, Unity's Test Runner runs the tests of the project you have open | no second Unity | tests only what's open, not a branch; takes your editor over for the run (Play-mode tests enter Play, muted); only when you ask (Q7) |
| D. A second Unity on the Mac | — | — | no: 8 GB, and the rule |

**My pick:** A for every change, B for the full suite and long jobs, C only when you ask.
**Your answer (Q7, 21 November):** A yes; C no.

---

## 5. The batch slowdown

**What the "3.6 times" compared,** exactly:

| When | Compared | Result |
|---|---|---|
| 7 November | 0.3.0's first ablation against that week's `main` | 0.15 s against 0.04 s of real time per simulated second: **3.6×** |
| 8 November | 0.3.0 after the fog's log flood was fixed and the shelf-bay lookup cached | 0.099 against 0.044: **2.25×** |
| 12 November | 0.4.0's branch against `main` (which already had 0.3.0) | 6.4 and 7.3 ms against 6.1 ms a frame: 1.05× and 1.2× |

Sources: [KAREN_RESULTS](../../../agents/results/KAREN_RESULTS.md#the-second-run-8-november-rule-10-every-trick-from-shift-1)
and the [0.4.0 plan's C0.2](../0.4.0-build-plan.md#phase-0-before-building).

- **So what's unexplained** is today's line (0.3.0 on) against 0.2.0, last measured at 2.25×,
  and never measured on 0.4.1 or 0.5.0.
- Her own code is about a quarter of a batch frame. Unity's own timing markers read nothing in
  a batch editor (its profiler is off), so the rest isn't broken down.
- **Ruled out today:** the log. The 12 November ablation (240 shifts, 66 min) logged about
  2,000 messages in all.
- **Why it matters:** the editor in batch mode runs every ablation and test, and today's
  training too. Once the Training player connects (0.5.0's C7.3), training moves to built
  players, which may not have the gap at all.

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Re-measure, then hunt (my pick)** | one shift (rung E, efficient player, seed 1000) on the 0.2.0 tag, on `main`, and on `main` as a development player; if the gap is still over 1.3×, split the commits between 0.2.0 and 0.3.0 in halves until one shows it | the first step may show nothing's left; each half-split is one short run | Unity time on the PC (the estimate is in the plan's [computers table](../0.5.2-plan.md#4-who-builds-what-and-on-which-computer)) |
| B. The profiler in batch mode | Unity's `-profiler-enable` and `-profiler-log-file` flags | shows where the frame goes | the docs don't say they capture in batch mode: a thing to try once, not a promise |
| C. Run ablations in a built player, not the editor | the eval player exists (Kehai → Eval → Build Eval Player) | skips the editor's own cost | the cause stays unknown; builds take time |
| D. Leave it | multiply every time by the factor | no work | if 2.25× still holds, the training times in [TESTING.md](../../TESTING.md#the-times) could be about twice what they need |

**Where:** Windows (it can run while your Mac editor is open), or the Mac with the editor closed.
**Model:** Opus 5.5: a deep bug that two sessions didn't find.

---

## 6. Running on the Windows PC from this Mac

**Today:** `tools/rl/train.ps1` exists, but getting there is by hand: copy `tools/rl` and a zipped
player, unzip, run in PowerShell, bring `results/` back. Nothing shows progress on the Mac.

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. A private network plus SSH (my pick)** | Tailscale on both computers, and Windows' own OpenSSH Server switched on. `kehai-run --on pc <job>` starts the job there, detached, and returns; `kehai-run status` shows it | works from anywhere; free (Personal plan, 6 users, [Tailscale's docs](https://tailscale.com/docs/account/manage-plans/free-plans-discounts), April 2026; check, as some sites say it's for personal use only); scripted | one app on each computer; you do the set-up (Claude writes the steps) |
| B. SSH on your home network only | the same, without Tailscale | no extra app | only at home; the PC's address can change |
| C. A GitHub runner on the PC | GitHub's self-hosted runner, attached to the **private** `kehai-assets` repo, never the public `Kehai` repo (a stranger's pull request could run code on your PC) | logs and an email from GitHub; start a job with `gh workflow run` | a service running on your PC; GitHub's time limit per job (check before relying on it) |
| D. Remote desktop | Windows Remote Desktop, Chrome Remote Desktop or Parsec | nothing to build; you see the screen | all by hand; no progress on the Mac |

**Progress, with A:** each run keeps a one-line progress file: the job, time so far, the last
progress line (the ablation's "EvalBatch progress", the trainer's step count), steps done of the
total and a time to finish. `kehai-run status` reads both computers; the status page's
collector (`tools/status`) already reads the run register and the lock. Training curves come
from TensorBoard on the PC, opened from the Mac over the private network.

**Example:** you type `kehai-run --on pc train employee_r1`, close the laptop, and the next
morning `kehai-run status` says `employee_r1: 410,000 of 1,000,000 steps, 9 h 12 min, about
13 h left`.

---

## 7. Runs that survive interruption

| Job | If it stops halfway today | Code |
|---|---|---|
| Ablation | starts again from nothing: a new file each run, and the list of finished shifts is only in memory | `AblationRunner.cs` |
| Training | continues, by hand (`--resume`); a checkpoint every 50,000 steps, the last 10 (Employee) or 20 (Vital) kept | `tools/rl/*.yaml` |
| Belief fits | resume by stage (since 13 November) | `tools/belief/` |
| Belief recording | starts again: `record.sh` runs two whole leagues, then its checks | `tools/belief/record.sh` |
| EditMode suite | starts again | — |

| Option (the ablation) | How | Pros | Cons |
|---|---|---|---|
| **A. Resume at careers (my pick)** | `-ablation-resume <file>` reads what's written, skips finished careers, plays any half-finished career again from its start | each career already starts with a fresh Ledger (`freshLedger = true`), so it's a clean unit; seeds stay paired | a half-finished career is played twice |
| B. Resume at shifts | continue mid-career | nothing replayed | a career's shifts share her Ledger and the store, which would have to be saved and restored exactly |
| C. One career per job | a queue of small jobs, files joined at the end (`analyze_ablation.py` already drops repeated rows) | each job is short | many Unity starts (about 90 s each, measured) |

**Also:**
- **Training restarts itself** after a crash from its last checkpoint, at most three times,
  never after you stop it.
- **Jobs live apart from the session:** started by `kehai-run` under the Mac's own scheduler
  (launchd) or Windows' Task Scheduler, so a session hitting its limit doesn't kill them.
- **Exact repeats aren't possible today, resumed or not.** The [eval README](../../../../tools/eval/README.md)
  says the first shift after Unity starts replays exactly, and later shifts drift a little (the
  NavMesh and crowd updates run on several threads). So a resumed run is checked like any other:
  the same rows, none twice, and results within the spread of two runs that weren't stopped.
- **The jitter's cause is found first, then fixed** ([§1](#1-what-gets-in-the-way-today)): 0.4.0's
  notes only *suspect* the wall-clock timers. Any fix that changes play is checked against `main`
  the usual way, and is Opus work.
- **Recordings resume by stage:** `record.sh`'s steps (the training league, the validation league,
  the checks) skip what's already done.

**Model:** Opus 5.5 for the ablation's resume and the jitter (pairing and repeatability), Sonnet 5.5 for the wrappers.

---

## 8. Knowing when a run ends

**The notice, for example:** "Done: EditMode suite on 0.5.2 (`a1b2c3d`), all passed, 14 min.
Nothing left behind. Next step waits for your go: `kehai-run trick-check`." (Made-up numbers.)

| Option | Pros | Cons |
|---|---|---|
| **A. Telegram (my pick, if you make the bot)** | on your phone anywhere; the marketing pipeline already sends Telegram messages, so it's reused | you create the bot once (about 5 minutes, in Telegram); the token stays in a `.env`, never in git |
| B. A banner on the Mac | nothing to set up | only seen at the Mac; must be silent (no sound named) |
| C. Email | anywhere | needs an email account set up for sending |
| D. A third-party push service | quick | sends run names to an outside company |

**My pick:** A and B together; B alone until the bot exists.
**Your answer (Q9, 21 November):** A and B.

---

## 9. Art and data to Windows

This is the same question as version control for the scenes and art, so it's answered once, on
the [scenes and version control page](scenes.md#7-art-and-data-to-windows). In short: project
files through the private repo with Git LFS (no hand-made zips), and game data (datasets, results)
copied directly over the private network of [§6](#6-running-on-the-windows-pc-from-this-mac).

---

## 10. Working within the usage limits

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Rules for agents, in `RULES.md` (my pick, Q11)** | the list below | every session follows them | a little slower when one session could have done more at once |
| B. Leave it to each session | — | nothing to write | the same losses |
| C. A reminder hook | Claude Code runs a small check at the end of each turn and warns about uncommitted work | catches what a rule forgets | must never commit by itself (it could commit junk) |

**The rules (A):**
- **At most one Opus subagent at a time,** and two subagents in all. Mechanical subagents use Sonnet.
- **Commit every finished step** on the local branch; push the branch at the end of each phase (no PR until it's done, as now).
- **A step is at most about an hour of agent work,** and ends in a commit and a three-line note in
  the shared notes folder: the branch and last commit, what's running, the next step.
- **Long jobs run under `kehai-run`,** detached, never as a child of the session.
- **Check the usage before a long step,** and stop at a safe point when it's nearly used.
- **The session's model fits the phase:** tooling phases in a Sonnet 5.5 session, Karen's in an
  Opus 5.5 session, one at a time.

---

## 11. How this was measured

All on 17 November, on this Mac, with no Unity running. Another session was active, so the
timings are one try each, not averages.

| Number | How |
|---|---|
| Clone of the four needed folders (about 73,000 files): 49 s | `time cp -Rc Assets Library Packages ProjectSettings <scratch>` |
| Clone of `.claude/worktrees` (78,202 files): 41 s | `time cp -Rc .claude/worktrees <scratch>` |
| Deleting both: 29 s | `time rm -rf <scratch>` |
| 0.5.1's start: 87.7 s importing (20.1 s compiling), 22 tests in 10.0 s | the 0.5.1 session's log and results file from 15 November |
| Free disk: 18 GiB | `df -h /` (it moves by about a gigabyte as other sessions work, so the clone's own disk cost wasn't measurable) |
| Seven lock copies, four without take-over | `grep` for `heavy.lock` in `tools/`, then each one read |
| About 196 of ~530 test methods | a count of test attributes in files that never mention a scene, a game object or `AddComponent` |
| The ablation's log: about 2,000 messages | counted in the 12 November run's `unity.log` |
| Leftovers | `ls` of `~/Library/Application Support/TokenLimit` and `~/Library/Preferences` |
| `kehai-assets` | `gh release view` and `gh api` on the private repo |
| The two private copies | `cmp` of the two scenes, `diff` of the project settings, and the two lists of non-code files under `Assets` |
| Each career's fresh Ledger | read in `AblationRunner.cs` |
