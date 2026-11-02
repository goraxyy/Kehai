# tools/belief — a learned motion model for Karen's belief

Karen's belief grid (`AI/Scripts/Belief/BeliefGrid.cs`) is a Bayes filter over ~4,700 floor
cells of 1.5 m, run 10 times a second:

```
correct   b(c) ∝ b(c) · P(evidence | player in c)      sightings, noises, traces, testimony, sweeps
predict   b'   = T · b                                  how a person moves in 0.1 s
```

The predict step `T` was a hand-written guess. This folder learns it from player trajectories,
trains it **through the filter** on the metric it is judged by, and ships it as generated C#.
Results: [`docs/KAREN_RESULTS.md`](../../docs/KAREN_RESULTS.md#a-learned-motion-model-for-her-belief).

## The models

| id | predict step | C# |
|---|---|---|
| m0 | the shipped hand step, bit for bit | `HandMotion` |
| m1 | the hand step, gains fitted to data, plus keeping away from Karen | `HandMotion` + `MotionWeights.M1Gains` |
| m2 | log-linear: leave rate and edge scores linear in the features | `LearnedMotion` (LogLinear) |
| m3 | m2 plus an MLP per cell, 48 → 32 → 32 → 9 | `LearnedMotion` (Mlp) |
| m4 | m3 with a heading in the state: still or one of 8 directions | `LearnedMotion` (Heading) |

Every model gives each state a **rate of leaving** (the chance of leaving in a tick is
1 − exp(−rate·dt), so any tick length works) and a **choice among its edges** (a softmax).
Mass only moves along walkable edges and the total is kept. m4's state is cell × 9: from
(cell, heading) she may step along an edge (and then head that way) or stop; a learned 9 × 8
table adds to each direction's score by the heading you had, so going on straight can be
likelier than turning. Evidence depends on position only, so every heading of a cell is
multiplied alike, and heading is inferred from successive sightings (as a Kalman filter infers
velocity).

**Features** (`MotionFeatures.cs`, mirrored in `features.py`): only what Karen may know. Per
cell (24): degree, open space, area, chokepoint, distance to the time clock and the front
doors (per map); distance to unfinished work, habit, jobs left, all done, shift clock,
overtime, light, power (every 2 s); her walking distance, her 30 s trail, chasing, time since
her last strong fix, believed speed (2 Hz). Per edge (9): the change in distance to work, to
her, to the clock and the doors; habit, trail, door and open space at the far end; diagonal.
Models read features, never cell ids (the maze changes before shift 7).

## The pipeline

```bash
PY=../rl/.venv/bin/python          # numpy + torch; shares the RL venv
EV="$HOME/Library/Application Support/TokenLimit/Kehai/kehai_eval"

# 1. Traces: Karen acts on m0; an m0 shadow runs without the Director's hint (for the parity gate).
Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation \
      -ablation-rungs CDE -ablation-profiles efficient -ablation-careers 1 -ablation-seed 3000 \
      -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records \
      -belief-shadows m0 -belief-trace "$EV/belief_traces/train" -ablation-out "$EV/belief_runs/train"

# 2. The parity gate (nothing is trained until it passes), then pack the traces.
$PY replay.py parity "$EV/belief_traces/train"
$PY trace.py pack "$EV/belief_traces/train" "$EV/belief_data/train" --delete

# 3. Fit m1; train m2–m4 (stage 1 on trajectories, stage 2 through the filter); choose on validation.
$PY tune_hand.py --train "$EV/belief_data/train" --val "$EV/belief_data/val"
$PY train.py stage1 m3 --train "$EV/belief_data/train" --val "$EV/belief_data/val"
$PY train.py stage2 m3 --train "$EV/belief_data/train" --val "$EV/belief_data/val"
$PY train.py select --card '{"data": "…"}'

# 4. Into the game, and the C# parity gate (EditMode: MotionModelTests).
$PY export_cs.py --weights out/weights.npz
```

| file | what |
|---|---|
| `trace.py` | reads `BeliefTrace`'s files; `pack` keeps a compact `.npz` per shift |
| `features.py` | the features, exactly as C# computes them (float32 in, Mathf's double exp) |
| `kernels.py` | m0–m4 in PyTorch; `pack()` is the order `MotionWeights.g.cs` holds them in |
| `replay.py` | the filter re-run from a trace with any kernel; `parity` is the gate |
| `tune_hand.py`, `train.py` | fitting m1, training m2–m4, choosing |
| `export_cs.py` | writes `MotionWeights.g.cs`: weights, model card, the C# parity fixture |
| `eval_offline.py` | the horizon curve (the model alone), and replayed scores |
| `from_shift_records.py` | real players' shift records → trajectories for the horizon curve |
| `make_fixture.py` | the synthetic map both languages build, a trace writer and a reference filter for the tests |
| `tests/` | `pytest tests` (`uv pip install --python ../rl/.venv/bin/python pytest`) |

## The two parity gates

1. **Replay ↔ C#** (`replay.py parity`). The trace holds every likelihood exactly as C#
   multiplied it, so only the predict step can differ. With the m0 kernel the replay must give
   C#'s logged log p on every 5 Hz sample (to 1e-3): with the Director's hint against her own
   grid, without it against the m0 shadow. Learned shadows are checked the same way, their
   kernels at probe cells (to 1e-5), and the features at probe cells (exactly). The replay sums
   as C# does (a float running sum over cells; with headings, in double); summing more
   accurately than C# would leave C#'s own round-off, about 1e-4 of the total, as the gap.
2. **PyTorch ↔ C# kernels** (`MotionModelTests.LearnedKernels_MatchPyTorch`). `export_cs.py`
   writes what each PyTorch kernel makes of a fixed belief on a synthetic map into the generated
   file; the EditMode test runs the C# kernels on the same state and holds them to 1e-5.

## Training

- **Stage 1, trajectories.** Every 3 s a rollout starts at the player's true cell (for m4, also
  the heading they're walking in) and the kernel alone rolls it forward up to 30 s; each 5 Hz
  sample raises log p of where they really are. No evidence, no leak.
- **Stage 2, through the filter.** Each training shift's evidence is replayed with the model
  as the predict step and no Director's hint; the loss is −log p of the true cell at each 5 Hz
  sample, weight 1 out of view and 0.25 in view: the metric itself. Truncated backprop every
  20 s, the state carried on (a learnable histogram filter, Jonschkowski & Brock 2016).
- **m1** is fitted by stage 2 alone, on the same training shifts.
- **Selection** reads validation only: early stopping, and which of m2–m4 is "best". The test
  set is played once, in Unity, by shadow filters (`docs/KAREN_RESULTS.md`).

## Data

Traces live outside the repo, in `kehai_eval/belief_traces/` (about 1.4 MB a shift), and are
deleted once packed into `kehai_eval/belief_data/`. Nothing here is committed but code: the
weights go into the game as generated C#, `out/` and data are ignored.

| split | players | seeds | shifts |
|---|---|---|---|
| train | efficient, skittish, reckless | 3000–3500 | 216 |
| validation | the same | 2000, 2100 | 72 |
| test | + cautious (held out) | 1000, 1100, 1200 | 144 |

Each is rungs C, D and E, career shifts 4–7, 150 s. The model card at the top of
`MotionWeights.g.cs` says what the shipped weights were trained on and how they scored.

## Cost

A learned kernel is rebuilt whenever the features refresh (2 Hz). `m3/8` refreshes the MLP's
part for one eighth of the cells per believe tick instead, so each cell's is at most 8 ticks old;
the replay does the same, tick for tick. Costs per believe tick are in the results.

## Limits

- The players are scripted bots by one author: a model can learn their code.
- One store, plus its mutations.
- Where evidence is fresh, motion matters little; the results split by time since she lost sight.
- The published belief numbers before this work include the Director's truth hint; the motion
  comparison runs without it.
