# Learned motion model: where it stands

Paused on 2026-10-23 before training, and picked up again on 2026-11-01. This file says how to carry on, whoever does.

## State

**Done**
- **The C# side:**
  - `IMotionModel` with `HandMotion` as m0, bit-identical to the old predict step (an EditMode test checks it);
  - M1, and `LearnedMotion` m2–m4, with the `m3/8` refresh slicing;
  - a heading-capable grid, shadow filters, the belief trace logger, the flags (`-belief-motion`, `-belief-shadows`, `-belief-trace`);
  - the playtest hook: the shift record's `belief` block and a session note.
- **Data:** 216 training shifts (seeds 3000–3500) and 72 validation shifts (seeds 2000, 2100): rungs C–E, career shifts 4–7. They're packed in `~/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief_data/{train,val}` (480 MB, outside the repo). Every trace passed the replay ↔ C# parity gate: worst |Δ log p| was 7.6e-6 against a tolerance of 1e-3, and features are exact.
- **Pre-registration:** the primary metric and win rule are in `docs/KAREN_RESULTS.md` (commit `cfe7994`), written before any test run.
- **Reporting:** the analyzer section and the docs (`Karen.md` §4.2/§15, `tools/eval/README.md`, this folder's `README.md`).
- **Helper scripts, now committed here** (the first versions lived in a temporary folder, and a restart wiped them):
  - `with_lock.py` waits for the editor to close, holds the heavy-work lock, and keeps the Mac awake;
  - `train_all.sh` runs the two training tracks, and skips finished steps on a rerun;
  - `test_run.sh` runs the test and the closed loop on a clone, and skips finished processes on a rerun.

**Not done**
- **The weights:** until step 2 below, `MotionWeights.g.cs` holds untrained placeholder weights.

## Carry on, in order

**Rules:** no Unity editor open during any of this (this Mac has 8 GB). `with_lock.py` enforces that and takes the lock. Run everything from `tools/belief`, with `PY=~/Developer/Kehai/tools/rl/.venv/bin/python`. Keep the Mac on power with the lid open: `caffeinate` can't stop a closed lid sleeping on battery.

1. **Train** (about 5 h, CPU). Progress is in `out/track_A.log` and `out/track_B.log`.
   ```bash
   $PY with_lock.py belief-training bash train_all.sh
   ```
   Per-shift cost on 1 thread:
   - stage 2: m1 3 s, m2 5 s, m3 6 s, m4 17 s;
   - stage 1 for m4: 47 s, hence `--limit 108 --start-every 5`.
2. **Choose and export:**
   ```bash
   $PY train.py select --card '{"data": "216 train shifts (efficient, skittish, reckless; seeds 3000-3500; rungs C-E; career shifts 4-7, 150 s), 72 validation (seeds 2000, 2100)", "test": "held out: seeds 1000/1100/1200 and the cautious player"}'
   $PY export_cs.py --weights out/weights.npz
   ```
   Commit the generated `MotionWeights.g.cs`: the clone gets the branch's committed state.
3. **Gate, test, closed loop and analysis** (about 3.5 h of batch Unity, on a clone at `~/TokenLimit/kehai-verify-clone`). `gate` runs the EditMode tests `MotionModelTests`, `ShiftRecordTests` and `KarenRuleTests`; with real weights, these are the C# parity gate.
   ```bash
   $PY with_lock.py belief-test bash test_run.sh prepare gate test closed finish
   ```
   - `test` is 12 Unity processes: rungs C–E × all four profiles (including the held-out cautious one) × seeds 1000, 1100, 1200 × shifts 4–7, with shadows `m0,m1,m2,m3,m4,<best>/8`.
   - `closed` is 12 processes of rung C with `-belief-motion <best>`, on the same seeds.
   - `finish` runs `replay.py parity`, `trace.py pack` and `eval_offline.py horizon`, then writes the analysis to `out/results.md`.
   - **The test set is opened once.** A crashed process may be played again; a result may not be rerun because of what it says.
4. **Report:**
   1. Fill in the "Results" subsection of `docs/KAREN_RESULTS.md` from `out/results.md`.
   2. Measure the cost per believe tick for each model in the editor, against the 0.7 ms budget.
   3. Check a playtest-mode session in the editor.
5. **Clean up:** `bash test_run.sh cleanup` deletes the clone and `~/Library/Application Support/TokenLimit/Kehai-verify`, and points the editor prefs back at `~/Developer/Kehai`.
6. **Ship:** squash the WIP commits if wanted, then mark this PR ready.

The full design is in `docs/plans/learned-motion-model.md`.
