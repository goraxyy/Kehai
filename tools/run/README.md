# kehai-run: one safe command for Unity jobs

0.5.2 plan, Phase 1. It replaces the hand-run recipe in [HANDOVER §6](../../docs/agents/HANDOVER.md#6-how-to-work-on-it).
Python standard library only; run it from anywhere.

```bash
tools/run/kehai-run offline                    # working-tree compile and engine-independent checks
tools/run/kehai-run tests                      # the whole EditMode suite on the current commit
tools/run/kehai-run tests --filter Sandbox     # part of it
tools/run/kehai-run ablation -- -ablation-careers 1 -ablation-shifts 2 -ablation-rungs CE
tools/run/kehai-run unity -- -executeMethod EvalBatch.Play -kehai-trick-check
tools/run/kehai-run ablation --learning off -- -ablation-careers 5 -ablation-shifts 1   # a speed measure
tools/run/kehai-run train --learn-args="--resume" basic_r2_a tools/rl/karen_basic.yaml tools/rl/leagues/basic_r2.json -- -karen-set catchDial=0.15
tools/run/kehai-run trick-clip /path/to/out/RUN/kehai_eval/trick_check_TIME.tsv blackout --dry-run
tools/run/kehai-run tests --dry-run            # print the Unity command, run nothing
tools/run/kehai-run status                     # the lock, what's running, the last runs
tools/run/kehai-run doctor --clean             # clean up after runs that died
```

The offline command reads the imported libraries from `KEHAI_RUN_MAIN`. It compiles the editor runtime, editor tests and Gameplay sources, then executes the edition/career/guide NUnit fixture and the sale/complaint policy checks under .NET. All other native tests need Unity. Use `offline --references <project> --unity-app <Unity.app>` for another installation.

**What a Unity run does:**
1. **Checks:** your editor closed (this Mac can't run two Unitys), enough free disk, the
   heavy-work lock free. A lock whose holder has died is taken over, and it says so.
2. **A run card** in `~/TokenLimit/runs/cards/`, written before anything is touched: the clone's
   path, the editor prefs as they are, the product name and data folder.
3. **A clone** of the main project's `Assets`, `Library`, `Packages` and `ProjectSettings` only
   (not the worktrees), with the commit laid on and every file the commit doesn't have removed.
4. **Unity,** silent, with a time limit (`--timeout-min`, default 90) and a progress line.
5. **Clean-up:** results and the run's game data copied to `~/TokenLimit/runs/out/<run id>/`
   (including replay and shift files the job produced, plus its own `run.jsonl`), the prefs put back to the card's values, the clone, its data
   folder and its prefs file removed, the lock freed. Start and end go into the run register
   (`~/TokenLimit/runs/runs.jsonl`), and a silent banner says how it ended.

Ctrl-C, closing the terminal or a session ending all clean up. A crash or `kill -9` can't: the next
run, or `doctor --clean`, finds the dead run's card, preserves its existing output, and cleans up for it. A dry run only prints the command. An unavailable process list blocks execution and recovery.

For one landed trick, `trick-clip` reads the retained replay under the source run’s `shift_records` and uses the commit in its `run.jsonl`. Use a recording from a commit that includes the silent renderer. The render gets its own run folder with `clip.mp4`, the camera sidecar and the log. Each render with Unity needs a separate go.

## Which records a run keeps

0.5.2 plan, C6.5. Each run prints its choice at the start and writes it into the register's `records`.

| Job | Shift records and replays | Learning records | Why |
|---|---|---|---|
| `tests` | each test's own | each test's own | EditMode tests set their own |
| `ablation`, `league` | **on, always** | on; `--learning off` for a run that only measures | every test shift is recorded (Q1); its shifts may become training data |
| `demos` | off | off | the demonstrations are the output, over thousands of shifts |
| `unity` | on, unless `-kehai-no-records` | as the method sets, or `-karen-learning on/off` | a method chooses its own |
| `trick-clip`, `sandbox-clip`, `sight-clips` | off | off | a render replays a recording |

`-kehai-no-records` turns off both kinds, so it's refused on tests, ablations and leagues. Learning
records cost about a seventh of a batch frame (7.3 against 6.4 ms, 12 November).

**One compile per start** (C6.2): Unity's inference package removes `SENTIS_ANALYTICS_ENABLED`
whenever editor analytics is off, as it is in batch mode. The clone starts without it, so the
scripts compile once. Each run's end event lists why Unity compiled (`compiles`). Two entries mean
something else now changes the defines.

## Releases: tags and rebuilds

0.5.2 plan, C10.4 ([the versions page, §3](../../docs/you/plans/0.5.2/versions.md#3-rebuilding-any-version)).

```bash
tools/run/kehai-run release tag 0.5.2-beta.1                       # tag HEAD of main, locally
tools/run/kehai-run release tag 0.5.2-beta.1 --push                # and push the tags (the owner's go)
tools/run/kehai-run release check v0.5.2-beta.1                    # lay it into a scratch folder and compare; no Unity
tools/run/kehai-run release rebuild 0.5.2-beta.1 --platform macos  # check, then build it (editor closed)
```

- **Versions** are `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH-beta.N`, tagged `v0.5.2` and `v0.5.2-beta.1`.
  A beta builds on the testing channel, a plain version on prod. Dev builds aren't tagged.
- **`tag`** refuses uncommitted changes, a commit that isn't on `origin/main`, and a version
  that is already tagged. The tag is annotated. Its message records the code commit, the art
  commit and the Unity version (`ProjectSettings` isn't in git). Once the art has its repo
  (`.git-assets` in the main folder, Phase 7), the art repo gets the same tag.
- **`check`** and **`rebuild`** clone the project the way every run does, at the tag. Then they:
  1. lay the art at the tag's art commit, through a scratch index, so the main folder's index
     isn't touched (LFS files should come out as a checkout would; not yet tried with LFS);
  2. remove code under `Assets/!_Project` that the tag doesn't have;
  3. compare every file with the tag, in both repos, by git's own hash.

  A single difference stops a rebuild before Unity starts. `release_check.txt` in the run folder
  lists what was compared.
- **Until Phase 7** a tag has no art commit, so `check` and `rebuild` refuse unless you pass
  `--art-today`. With that flag they use today's art, and the stamp reads `art unversioned`.
- **Not yet:** a tag from another Unity version is refused, not installed. `-rc.N` versions wait
  until `KehaiBuild` knows them. `kehai-assets` isn't tagged yet.

## Sight check review clips

Use the JSON report from a retained `SandboxChecks.Sight` run with all eight `.krec` files,
its sibling Markdown report and the run's `run.jsonl`. If you need fresh inputs, the bot command
is `tools/run/kehai-run unity -- -executeMethod SandboxChecks.Sight`. The bot keeps the report
and replays even if a case fails.

```bash
tools/run/kehai-run sight-clips /path/to/out/RUN/sandbox_checks/sight_TIME.json --dry-run
```

The dry run checks the eight report rows and recording headers and durations, then prints the
render commands. With the owner's go and the editor closed, omit `--dry-run`. The command uses
the recording's source commit and one safe clone under the heavy-work lock. It renders the
full length of each recording in sequence with the existing sandbox replay camera and encoder.
It needs a graphics device and `ffmpeg` and `ffprobe` on PATH or in the main project's Remotion
install. `KEHAI_FFMPEG` can point to a specific encoder. The default 30-minute limit is shared
across the batch. Use `--timeout-min` to adjust it.

Open `review.md` in the new private run folder. Each verified row links to a silent 720p MP4,
the renderer's sidecar and its log. The overhead view shows the reconstructed room and recorded
movement. Read the bot report for the measured sight and witnessed entry outcomes. Clip
verification checks the recorded filename and frame count against the sidecar, then decodes the MP4
to check its frames and confirm it has no audio stream. A failed render leaves its log and any
partial output for diagnosis. The command exits successfully when all eight clips verify and all
eight bot checks passed. Keep reports, replays and clips outside git.

## Runner settings

**The lock** is `tools/run/lock.py`: the same directory the marketing tools use
(`~/TokenLimit/marketing/state/heavy.lock`), with the holder's start time added so a reused process
id isn't mistaken for a live holder. The shell scripts use it too:
`python3 tools/run/lock.py take "<job>" --pid $$` (exit 75 when busy) and `release --pid $$`.

**Settings** (environment, mostly for the tests): `KEHAI_RUN_MAIN` (the project with the private
files, default `~/Developer/Kehai`), `KEHAI_RUN_HOME`, `KEHAI_RUN_UNITY`, `KEHAI_RUN_LOCK`,
`KEHAI_RUN_MIN_FREE_GB` (default 6), `KEHAI_RUN_NOTIFY=0`.

**Tests:** `python3 -m unittest tools.run.tests.test_run`. They use a stand-in Unity
(`tests/standin_unity.py`), temporary folders and a prefs file, never the real ones: success, a
failing Unity, a timeout, Ctrl-C, a closed terminal, `kill -9` then the next run, a dry run, an
open editor, and the lock's rules.

**Not yet:** running on the Windows PC (`--on pc`, Phase 3), Telegram notices (Q9), and the warm
clone (Phase 6).
