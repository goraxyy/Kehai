# Plan: a learned motion model for Karen's belief grid

> **Status:** approved 2026-10-18. About 70% built, paused before training: draft PR #68 (`feat/learned-motion`); how to resume is in that branch's `tools/belief/HANDOVER.md`.

## Context

Karen's belief grid (4,713 cells of 1.5 m) is a Bayes filter. Every believe tick (10 Hz) it runs:
1. **correct**: multiply in evidence (sight, hearing, traces, testimony, sweeps that found nothing);
2. **predict**: spread the probability along the floor, using the hand-written `BeliefGrid.Predict`.

The predict step is a guess about how people move, with 5 hand-set gains. The 2026-10-16 ladder
showed online learning (bandit, Ledger) doesn't beat planning. So the next ML piece has to beat
hand-built baselines on clean metrics. This one is the most standard applied-ML problem in the
project: learn the motion model from player trajectories and score it with the existing
belief-accuracy metric.

**Numbers to beat** (rung C, out of view, `docs/agents/results/KAREN_RESULTS.md`): log p −7.40 (a uniform guess
scores −8.46), top-5 10.0%, error 18.6 m. The grid's peak is about 4–5 m nearer the player than
last-seen.

Decisions already made with you: build the **ladder up to heading**; ship the weights as
**generated C#**; train **through the filter**; add **real-player hooks and shadow filters**.
It lands as **one PR**.

## What the code says (this shapes the plan)

| finding | where | consequence |
|---|---|---|
| Predict is separate from correct, and it preserves mass | `BeliefGrid.Predict` | A learned step can replace it without touching evidence. |
| The `Danger` layer is never filled | `BeliefGrid.cs:36`, nothing writes it | The hand step ignores where Karen is, though every bot backs away from her. A fair baseline needs this input too. |
| The Director nudges belief toward the **true** region after 45 s without a fix (0.15 of the mass a minute, up to 0.5) | `KarenDirector.MaybeBias` | Today's out-of-view numbers include some leaked truth. Motion comparisons must run without it. |
| Shift records already store the player at 5 Hz, plus Karen, jobs and power | `ShiftRecording.cs` | Real-player trajectories arrive through the playtest pipeline as they are. Karen's evidence stream is not in them. |
| Ablation runs pass `-kehai-no-records` | `AblationRunner` | Training data needs its own eval-only logger. |
| The repo holds code only, and the Inference Engine sits behind `KAREN_MLAGENTS` | memory, `SentisBlinkSource.cs` | Weights become a generated `.cs` file, and inference is plain C#. |
| The maze changes before shift 7 and the map is rebuilt, so cell ids change | `MazeMutation`, `StoreMap.Rebuild` | The model must read features, never cell ids. |
| The omniscience test already scans `AI/Scripts/Belief/` | `KarenTests.BodyBeliefAndDecisions_NeverReadThePlayer` | New motion code is checked for player reads automatically. |

## How it fits (nothing else in the filter changes)

```
evidence (unchanged)  →  b *= likelihood, normalise
predict (swappable)   →  b' = T_θ · b      T_θ = an IMotionModel
```

- The model only builds the kernel **T**. For each cell it gives a **leave rate**: the chance of
  leaving in this tick is `1 − exp(−rate·dt)`, so it works at any tick length. It also gives a
  **choice over neighbours** (a softmax over edges). Mass can therefore only move along walkable
  edges, and the total stays 1.
- Hearing, traces, testimony, sweeps (negative evidence), Bias, the uncertainty leak, collapse and
  reset all stay as they are.
- **Heading (M4).** The state becomes cell × 9 headings (8 directions + still): 42k numbers.
  - Evidence depends only on position, so every heading of a cell is multiplied by the same
    likelihood.
  - Everything that reads the grid (`this[cell]`, `RegionMass`, peak, entropy) reads the sum over
    headings, so no caller changes.
  - No new sense is needed: heading is inferred from successive sightings, the way a Kalman
    filter infers velocity from positions.

## The motion ladder

| id | predict step | question it answers |
|---|---|---|
| last-seen | Gaussian round the last fix (exists) | floor to beat |
| **M0** | today's hand step, exactly | the shipped baseline |
| **M1** | the hand step, tuned on training data, plus Karen-avoidance (fills `Danger` from her position and trail) | the fair baseline: same inputs as the learned models, structure set by hand |
| **M2** | learned log-linear: leave rate and edge scores are linear in features | does learning beat hand tuning? |
| **M3** | learned small MLP per cell (≈40 inputs → 32 → 32 → 9 outputs: stay + 8 directions) | does capacity help? |
| **M4** | M3 with heading in the state | does remembering which way you were going help? |

**Features** (all things Karen may know; the omniscience test enforces it):

| refreshed | features |
|---|---|
| once per map | edge direction, cell degree, open space within 3 m, area (floor, stockroom, office, outdoors…), door, chokepoint, Δ distance to the time clock and exits |
| every 2 s (with desire) | Δ distance to unfinished work (as today), habit prior, jobs left, all jobs done, shift clock, overtime armed |
| 2 Hz | Δ walking distance from Karen, her trail over the last 30 s, her mood (chasing or not), light level and power, time since her last strong fix, believed speed (`Energy.SpeedEstimate`) |

## Data

- **Belief trace logger** (eval only, `-belief-trace <dir>`), one gzipped binary file per shift:
  - the map once per map version: cells, positions, edges, regions, area flags;
  - each believe tick:
    - dt;
    - every likelihood as computed in C# (floor + sparse cell values), so Python never redoes
      the geometry;
    - the sweep (cells, probabilities);
    - Director bias events (region, amount), kept for parity only;
    - resets and collapses;
    - the fast features;
  - every 2 s: the desire field and the slow features;
  - truth, from the harness side only: the player's cell, velocity and motion state.
- **Size.** About 1 MB a shift gzipped, so under 1 GB in all (20 GB free). Traces live in
  `kehai_eval/belief_traces/`, outside the repo, and are deleted once the dataset is built.
- **Collection.** Karen acts on M0. Rungs C, D and E are all collected, because the players react
  to what she does.
- **Splits.** Every split has different shoppers and spills; the store is the same.

| split | profiles | careers (seeds) | rungs | shifts |
|---|---|---|---|---|
| train | efficient, skittish, reckless | 6 (3000–3500) | C D E | 216 |
| validation (model choice, M1 tuning) | same three | 2 (2000–2100) | C D E | 72 |
| **test** (run once) | all four; **cautious held out**: never trained on, never used for choosing | 3 (1000/1100/1200, the published ladder seeds) | C D E | 144 |

  Test slices are reported separately: cautious; shift 7 (after the maze changes); and time since
  she lost sight (0–5, 5–15, 15–45, 45+ s).
- **Real players.** Playtest builds run one shadow filter beside Karen's own and add the belief
  scores of both to the uploaded session. Karen keeps acting on M0 until the results say
  otherwise.
  - `tools/belief/from_shift_records.py` turns shift records into trajectories for stage-1
    training and for the horizon metric.
  - Your ~13 recorded shifts are a **smoke test only**: one person, the developer, and some were
    recorded on older layouts. Only records whose positions all land on today's map are used.

## Training (`tools/belief/`, PyTorch on CPU, under 2 GB of RAM)

1. **Replay.** Re-run Karen's filter in PyTorch from the trace, with any kernel.
   - **Parity gate:** with the M0 kernel, the replay must reproduce the C# grid's logged log p on
     every sample (to about 1e-3), with and without the bias.
   - No step below starts until this passes.
2. **M1 tuning.** Search the hand gains (moving share, desire gain, habit gain, avoidance, leak,
   speed scale) to minimise validation loss through the replay. This is cheap once the replay
   exists.
3. **Stage 1: trajectory pretraining.** Start from the true cell (and, for M4, the true heading),
   roll the kernel forward 1–30 s, and maximise log p of where the player really is.
   - Minutes for M2; under an hour for M3/M4.
4. **Stage 2: through the filter.** Replay each whole shift, with the Director's hint left out
   (the pure filter).
   - The loss is −log p of the true cell at each 5 Hz sample: out of view weight 1, in view 0.25.
     This is the metric itself.
   - Truncated backprop every 20 s, with the filter state carried forward.
   - This is a learnable histogram filter (Jonschkowski & Brock, 2016).
   - Overnight for M3/M4.
5. **Selection.** Early stopping and the choice of model use validation only. The test set is
   not opened before the test run.
6. **Export.** `export_cs.py` writes `MotionWeights.g.cs`: weights as `static readonly float[]`,
   with a model card in the header (data seeds, profiles, commit, validation score).
   - **C# parity gate:** the C# kernel equals the PyTorch kernel to 1e-5 on a fixed state.

**Stretch, only if needed.** If closed-loop belief comes out clearly worse than the shadow belief
(her own actions change what she sees), collect one round with Karen acting on the learned model
and retrain.

## Evaluation

**A. Belief quality on identical evidence (shadow filters, the main result).**
- In each test shift, Karen acts on M0. Shadow grids M0-pure, M1, M2, M3 and M4 receive the same
  evidence and run without the Director's hint. All are scored by `BeliefAccuracy`, as is
  last-seen.
- Differences are paired per shift on the **same samples**, so there is no behaviour confound
  and the variance is low.
- **Primary metric (written into the docs before the test run):** out-of-view log p, best model
  minus **M1**. It wins if the 95% bootstrap interval (resampled by career) is above 0.
- **Secondary:** top-1/5/10 and error; by profile (cautious marked held out); after the
  mutation; by time since she lost sight. Each model is also compared with M0 and last-seen.
- **Horizon curve (Python, test trajectories):** log p k seconds ahead, k = 1–30 s, for each
  model. This is the motion model on its own, without evidence.

**B. Effect on the game (closed loop).**
- Rung C, Karen acting on the best model against rung C on M0, paired by seed (48 pairs: 4
  profiles × 3 careers × 4 shifts). The M0 arm reuses run A.
- Reported: Δ detections, Δ first detection, Δ catches, Δ mean panic, and Karen's own belief
  accuracy. Fairness violations must stay at 0.
- No win threshold: the Director rations chases, so catches may not move. Whatever happens is
  reported.

**C. Cost.** Milliseconds per believe tick for each model in the editor. Karen's budget is
under 0.7 ms a frame (`Karen.md` §12.4). If M4 is over, the kernel refresh is sliced across
frames.

**After the results.** The `beliefMotion` flag stays `hand` by default. Switching it on at C and
above is your call once you've seen the numbers.

## Files

New:
- `AI/Scripts/Belief/MotionModel.cs`: `IMotionModel`, `MotionContext`, and `HandMotion` (today's
  code moved verbatim, plus M1's parameter set).
- `AI/Scripts/Belief/MotionFeatures.cs`: the three refresh rates above, with the per-map cache
  rebuilt on `MazeChanged`.
- `AI/Scripts/Belief/LearnedMotion.cs`: log-linear, MLP and heading kernels in plain C#.
- `AI/Scripts/Belief/MotionWeights.g.cs`: generated, with a model card.
- `Eval/Scripts/BeliefShadows.cs`: side-by-side grids fed the same evidence.
- `Eval/Scripts/BeliefTrace.cs`: the trace logger.
- `tools/belief/`: `trace.py`, `replay.py`, `kernels.py`, `tune_hand.py`, `train.py`,
  `export_cs.py`, `eval_offline.py`, `from_shift_records.py`, `make_fixture.py`, `tests/`,
  `requirements.txt` (numpy, torch; can share `tools/rl/.venv`), and `README.md` (method and
  model card).

Changed:
- `BeliefGrid.cs`:
  - `Predict` delegates to `Motion`;
  - optional heading layers, with marginals behind the same API;
  - a small tap for the trace logger.
- `KarenBrain.Believe`: fans the evidence out to the shadows and the trace. Only the primary
  gets `Director.MaybeBias`.
- `KarenConfig`: `beliefMotion`.
- `AblationRunner`: `-belief-motion`, `-belief-shadows`, `-belief-trace`.
- `BeliefAccuracy.cs`: scores each shadow, adds the time-since-lost-sight buckets, writes jsonl
  keys `shadow_<id>_*`.
- `ShiftRecorder` (playtest scores).
- `tools/eval/analyze_ablation.py`: a new section, "Motion models on the same evidence".
- Docs:
  - `KAREN_RESULTS.md`: a new section;
  - `Karen.md` §4.2 and §15;
  - `tools/eval/README.md`: the flags.

The work goes on one branch in a worktree (`feat/learned-motion`), as one PR.

## Order of work and rough cost

| # | step | my time | machine time |
|---|---|---|---|
| 1 | `IMotionModel` + `HandMotion` (bit-identical), heading-capable grid, shadows, flags, tests | 1 day | — |
| 2 | features + M1 avoidance | ½ day | — |
| 3 | trace logger | ½ day | — |
| 4 | Python replay + **parity gate** | 1 day | — |
| 5 | collect train + validation (288 shifts) | — | ~3 h |
| 6 | M1 tuning, train M2–M4 | 1 day | overnight |
| 7 | export, **C# parity gate**, perf | ½ day | — |
| 8 | test run (144 shifts) + closed loop (48) | — | ~2.5 h |
| 9 | analyzer, results doc, playtest hook | 1 day | — |

About 5–6 days of work and two nights of runs. Unity runs one process at a time (8 GB). No
training runs while Unity is running, and no batch Unity while your editor is open without
asking first.

## How it reads as an applied-ML result

- **The claim.** A learned motion model for a game AI's Bayes filter, trained end to end on the
  metric it's judged by. It is compared on identical evidence against:
  - the shipped hand step;
  - a hand step **tuned on the same data with the same inputs**;
  - last-seen.
- **The test.** Tested once on held-out seeds and a **held-out player type**, with the
  closed-loop game effect measured separately.
- **Honest limits, stated up front:**
  - the players are scripted bots by one author, so the model may learn their code;
  - there is one store, plus its mutations;
  - motion may matter little where evidence is rich (the time-since-lost-sight buckets show
    this);
  - today's published numbers include the Director's truth hint;
  - a negative result (M2–M4 ≈ M1) is reported as it comes. That would be the finding: "tuning
    the hand step is enough."

## Verification

- **EditMode tests:**
  - `HandMotion` matches the old `Predict` bit for bit on a fixed sequence;
  - every model keeps Σb = 1 and moves mass only along edges;
  - heading marginals and corrections match the position-only grid when headings are uniform;
  - the shapes of the generated weights;
  - an M0 shadow without bias equals a primary with bias switched off;
  - the existing omniscience test covers the new `Belief/` files.
- **Python tests** on a synthetic fixture: replay, kernels, export round trip. Plus the two
  parity gates on real traces.
- **End to end:**
  - a 1-career smoke ablation with shadows and trace on;
  - `analyze_ablation.py` prints the new tables;
  - the full test and closed-loop runs feed the `KAREN_RESULTS.md` section;
  - a playtest-mode session in the editor shows both belief scores in the packed session.
