# kehai-run: one safe command for Unity jobs

0.5.2 plan, Phase 1. It replaces the hand-run recipe in [HANDOVER §6](../../docs/agents/HANDOVER.md#6-how-to-work-on-it).
Python standard library only; run it from anywhere.

```bash
tools/run/kehai-run tests                      # the whole EditMode suite on the current commit
tools/run/kehai-run tests --filter Sandbox     # part of it
tools/run/kehai-run ablation -- -ablation-careers 1 -ablation-shifts 2 -ablation-rungs CE
tools/run/kehai-run unity -- -executeMethod EvalBatch.Play -kehai-trick-check
tools/run/kehai-run tests --dry-run            # print the Unity command, run nothing
tools/run/kehai-run status                     # the lock, what's running, the last runs
tools/run/kehai-run doctor --clean             # clean up after runs that died
```

**What a run does:**
1. **Checks:** your editor closed (this Mac can't run two Unitys), enough free disk, the
   heavy-work lock free. A lock whose holder has died is taken over, and it says so.
2. **A run card** in `~/TokenLimit/runs/cards/`, written before anything is touched: the clone's
   path, the editor prefs as they are, the product name and data folder.
3. **A clone** of the main project's `Assets`, `Library`, `Packages` and `ProjectSettings` only
   (not the worktrees), with the commit laid on and every file the commit doesn't have removed.
4. **Unity,** silent, with a time limit (`--timeout-min`, default 90) and a progress line.
5. **Clean-up:** results and the run's game data copied to `~/TokenLimit/runs/out/<run id>/`
   (every test shift recorded), the prefs put back to the card's values, the clone, its data
   folder and its prefs file removed, the lock freed. Start and end go into the run register
   (`~/TokenLimit/runs/runs.jsonl`), and a silent banner says how it ended.

Ctrl-C, closing the terminal or a session ending all clean up. A crash or `kill -9` can't: the next
run, or `doctor --clean`, finds the dead run's card and cleans up for it.

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
