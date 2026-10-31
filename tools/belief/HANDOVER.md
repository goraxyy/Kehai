# Learned motion model: where it stopped

Paused on 2026-10-23, before training. Picked up again from this file.

## State

**Done**
- **The C# side:**
  - `IMotionModel` with `HandMotion` as m0, bit-identical to the old predict step (an EditMode test checks it);
  - M1, and `LearnedMotion` m2–m4, with the `m3/8` refresh slicing;
  - a heading-capable grid, shadow filters, the belief trace logger, the flags (`-belief-motion`, `-belief-shadows`, `-belief-trace`);
  - the playtest hook: the shift record's `belief` block and a session note.
- **Tests:** 53/53 EditMode tests pass on this branch, including bit-identity, the C#↔PyTorch kernel parity and the shift-record belief block.
- **Data:** 216 training shifts (seeds 3000–3500) and 72 validation shifts (seeds 2000, 2100): rungs C–E, career shifts 4–7. They're packed in `~/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief_data/{train,val}` (480 MB, outside the repo). Every trace passed the replay ↔ C# parity gate: worst |Δ log p| was 7.6e-6 against a tolerance of 1e-3, and features are exact.
- **Pre-registration:** the primary metric and win rule are in `docs/KAREN_RESULTS.md` (commit `cfe7994`), written before any test run.
- **Reporting:** the analyzer section and the docs (`Karen.md` §4.2/§15, `tools/eval/README.md`, this folder's `README.md`). A dry run of every training step and the export worked.

**Not done**
- **The weights:** `MotionWeights.g.cs` still holds untrained placeholder weights.
- **Lost in a restart:** the helper scripts and the warm project clone lived in a temporary folder (`/private/tmp/...`), which macOS cleared. The dataset is safe. The lost pieces:
  - `heavy.py`, which held the marketing heavy-work lock while a stop file was absent;
  - `train_tracks.sh`, the two parallel training tracks below;
  - `test_run.sh`, the test run and closed loop described in step 3;
  - the clone itself.

## Resume, in order

**Rules:** no Unity editor open during batch runs (this Mac has 8 GB), and hold the marketing heavy-work lock (`~/TokenLimit/marketing/state/heavy.lock`) while training or running Unity.

1. **Train** (about 5 h, CPU). Run these from `tools/belief` with `PY=~/Developer/Kehai/tools/rl/.venv/bin/python` and `D="$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval/belief_data"`. The two tracks can run in parallel.
   ```bash
   # track A
   $PY tune_hand.py --train "$D/train" --val "$D/val" --epochs 4 --threads 3 --hours 2
   $PY train.py stage1 m2 --train "$D/train" --val "$D/val" --epochs 1 --lr 1e-2 --threads 3
   $PY train.py stage2 m2 --train "$D/train" --val "$D/val" --epochs 5 --lr 3e-3 --threads 3 --hours 2
   $PY train.py stage1 m3 --train "$D/train" --val "$D/val" --epochs 1 --threads 3
   $PY train.py stage2 m3 --train "$D/train" --val "$D/val" --epochs 5 --threads 3 --hours 3
   # track B
   $PY train.py stage1 m4 --train "$D/train" --val "$D/val" --epochs 1 --limit 108 --start-every 5 --threads 3
   $PY train.py stage2 m4 --train "$D/train" --val "$D/val" --epochs 4 --threads 3 --hours 4.5
   ```
   Per-shift cost on 1 thread:
   - stage 2: m1 3 s, m2 5 s, m3 6 s, m4 17 s;
   - stage 1 for m4: 47 s, hence `--limit 108 --start-every 5`.
2. **Choose and export:**
   ```bash
   $PY train.py select --card '{"data": "216 train shifts (efficient, skittish, reckless; seeds 3000-3500; rungs C-E; career shifts 4-7, 150 s), 72 validation (seeds 2000, 2100)", "test": "held out: seeds 1000/1100/1200 and the cautious player"}'
   $PY export_cs.py --weights out/weights.npz
   ```
   Then rerun the EditMode tests `MotionModelTests|ShiftRecordTests|KarenRuleTests`. With real weights, these are the C# parity gate.
3. **Run the test and the closed loop** (about 3.5 h of batch Unity), on an APFS clone of the project with this branch laid on top:
   1. Make the clone: `cp -Rc ~/Developer/Kehai <clone>`, delete its `Temp/` and `.claude/worktrees`, `git archive HEAD | tar -x -C <clone>`, and set `productName: Kehai-verify` in the clone's `ProjectSettings.asset`.
   2. **The test run:** 12 Unity processes. They cover rungs C–E × all four profiles (including the held-out cautious one) × seeds 1000, 1100, 1200 × shifts 4–7. Pass `-belief-shadows m0,m1,m2,m3,m4,<best>/8 -belief-trace <dir>`.
   3. **The closed loop:** 12 processes of rung C with `-belief-motion <best>`, on the same seeds.
   4. Then, on the test traces:
      - `replay.py parity`;
      - `trace.py pack`;
      - `eval_offline.py horizon --data <test> --weights out/weights.npz`.
4. **Report:**
   1. Join the run files and run `python3 tools/eval/analyze_ablation.py joined.jsonl --best <best>`.
   2. Fill in the "Results" subsection of `docs/KAREN_RESULTS.md`.
   3. Check a playtest-mode session in the editor.
5. **Clean up:**
   - delete the clone and `~/Library/Application Support/TokenLimit/Kehai-verify`;
   - point the editor prefs back at `~/Developer/Kehai` (`defaults write com.unity3d.UnityEditor5.x` `LastUsedProjectPath`, `kProjectBasePath`, `kWorkspacePath`).
6. **Ship:** squash the WIP commits if wanted, then mark this PR ready.

The full design is in `docs/plans/learned-motion-model.md` on main.
