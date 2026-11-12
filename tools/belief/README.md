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
tools/belief/train_all.sh                                       # C4.10: fit, choose, export (a few hours, CPU)
tools/belief/test_run.sh                                        # C4.11: the three tests of §11 (about 3 h, editor closed)
```

**A trace** starts with a `state` line (her belief, the prior it leaks back to, the exit lines),
then every operation of the filter with its inputs: `layers`, `obs` (as applied, with the raw
inputs her sense model turned into a bump), `sweep`, `bias`, `features`, `predict`, `her_noise`,
and every 25 predicts a `check` with her whole belief. `truth` lines (the player's cell and
destination, what they sensed of her) are for fitting and judging only; the game never reads them.

**Rules:** the held-out players and the test seeds are never fitted on (`EvalSplits`); the Director's
hint is left out of every fit (it uses the truth); the senses are fitted first and frozen for every
later arm, then the mirror map, then the predict step.
