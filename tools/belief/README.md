# tools/belief: her belief, replayed in Python (Karen 0.4.0)

The prediction model, her senses' numbers and the mirror map are fitted here, offline, from
recorded shifts ([the 0.4.0 plan, §8](../../docs/you/plans/0.4.0-build-plan.md)). It's supervised
learning: the answer is where the player really was, or what they really sensed of her.

| file | what |
|---|---|
| `kernels.py` | her filter (`BeliefGrid.cs`) in numpy, op for op, on today's map: her cells, the one outside cell and its exit lines; the hand step (P0, P1), the destination step (P2, P3), her senses' formulas (`SenseModel.cs`) |
| `features.py` | the destinations' chances and the walk's rates (`MotionFeatures.cs`) |
| `trace.py` | reads a belief trace (`Eval/Scripts/BeliefTrace.cs`): `map.json` and one `.jsonl` per shift |
| `replay.py` | replays a trace; `--parity` is the gate: nothing is trained until the replay matches the game on every check, to 1e-3 |
| `train.py` | the fits, in order: senses (S1, S2), the mirror map, P1, P2 and P3 (stage 1 from legs, stage 2 through the filter); chosen on validation; writes the generated C# and the pack parts |
| `synthetic.py`, `make_fixture.py` | a synthetic map built by the game's own rule, and the C# test's fixture: the C# filter must get Python's beliefs to 1e-5 (`MotionModelTests`) |
| `record.sh`, `train_all.sh`, `test_run.sh`, `run_unity.sh` | C4.9, C4.10, C4.11: one command each (each waits for your go) |

```bash
tools/rl/.venv/bin/python -m pytest -q tools/belief/tests     # the tests (no Unity)
tools/belief/record.sh                                          # C4.9: the recordings (about 3 h, editor closed)
tools/belief/train_all.sh                                       # C4.10: fit, choose, export (about 7–8 h, CPU)
tools/belief/test_run.sh                                        # C4.11: the three tests of §11 (about 3 h, editor closed)
```

If recording stops, rerun the same command from the same commit. It keeps finished leagues,
parity checks and the exported legs. An interrupted league starts again in its own folder.
Recording and fitting default to the new `belief052` folder. To use the older `belief040` data
for a fit, pass its path explicitly to `train_all.sh`.

The league and ablation launcher uses the shared `kehai-run` lock, clone, and run register.
It reads the league file from the checkout's `HEAD`. Use a repo-relative league path or its
absolute path in the checkout. Add `--dry-run` as the first argument to print the Unity command.

```bash
tools/belief/run_unity.sh --dry-run tools/rl/leagues/test_prediction.json /path/to/output
tools/belief/run_unity.sh --dry-run --ablation /path/to/output -ablation-rungs CE
```

The output folder gets the live `unity.log` and any `shift_records/` and `kehai_eval/` produced
by the run, including when Unity exits with an error. Additional game flags are passed through,
and relative paths in them are relative to the caller's directory. `KEHAI_TIMEOUT_MIN` still
sets the EvalBatch timeout (default 600 minutes). The runner allows five more minutes before
stopping an unresponsive Unity process. Set `KEHAI_CLONE` to choose the parent directory for
the per-run clone.

**A trace** starts with a `state` line (her belief, the prior it leaks back to, the exit lines),
then every operation of the filter with its inputs: `layers`, `obs` (as applied, with the raw
inputs her sense model turned into a bump), `sweep`, `bias`, `features`, `predict`, `her_noise`,
and every 25 predicts a `check` with her whole belief. `truth` lines (the player's cell and
destination, what they sensed of her) are for fitting and judging only; the game never reads them.

**Rules:** the held-out players and the test seeds are never fitted on (`EvalSplits`); the Director's
hint is left out of every fit (it uses the truth); the senses are fitted first and frozen for every
later arm, then the mirror map, then the predict step.
