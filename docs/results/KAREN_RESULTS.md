# Karen — ablation results

The ablation ladder from [`IDEAS.md`](../design/IDEAS.md) §2, run headless against three scripted players.
Each rung adds one mechanism, so a difference between neighbouring rungs is that mechanism.

| rung | Karen | learns |
|---|---|---|
| A | random patrol; chases what she sees | — |
| B | a fixed tour of the sales floor, last-seen search, the old random sabotage | — |
| C | belief grid (incl. negative evidence), utility goals, the tactic library, the Director's pacing | nothing — every tactic scored by its authored prior |
| D | + the bandit | within a shift; starts each shift from the prior |
| E | + the persistent Ledger | across shifts: tactic values, where you dwell and hide, your routes |
| F | + the blink channel | as E, plus acting inside your blinks |

## How it was run

- **Players.** `SimulatedPlayer` in three profiles (`Karen.md` §13): *efficient* (nearest job first,
  mostly ignores her), *skittish* (drops what it's doing when it sees or hears her, runs to the far
  end of the store and hides), *reckless* (sprints everywhere, helps everyone, ignores noise). They
  act through the same verbs and the same senses an external agent gets — Karen only when in view,
  her footsteps only when close.
- **Careers.** A career is four consecutive shifts in one session — career shifts 4 to 7, so most
  tactics are unlocked and the maze mutates once, before shift 7. The Ledger is wiped at the start
  of each career and kept between its shifts from rung E up. Rung F's blinks come from the synthetic trace (17/min, human shape).
- **Pairing.** For a given profile and career every rung gets the same seed — same customers, same
  spills — so rung-to-rung differences are computed per (seed, shift) pair.
- **Time.** Fixed 1/15 s step, 150 s shifts plus overtime, `-batchmode -nographics`.
- **Caveat on replay.** The first shift after launch replays exactly for the same seed; later shifts
  in one process drift a little (engine-side threading in NavMesh carving and crowd updates), which
  adds noise on top of the pairing. Means ± standard errors below are over shifts.
- **What the numbers are not.** These are scripted players, not people: "panic" is Karen's own
  Panic Index estimate from the players' movement, and the bots' completion rates say as much
  about the bots as about her. The comparisons between rungs are the point.

Reproduce: `Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation -ablation-careers 3 -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -eval-timeout-min 180`,
then `python3 tools/eval/analyze_ablation.py <kehai_eval>/ablation_<time>.jsonl`.

## What it shows

**1. The belief grid and planner (rung C) is the big step.** Against the same shifts, C detects the
player about 11 more times per shift than the patrols (Δ −11.8 ± 1.1 for A, −10.8 ± 1.1 for B),
finds them in half the time (first detection 51 ± 7 s against 104 ± 21 s and 100 ± 15 s), and finds
them in every shift. It also catches them far less (0.19 catches per shift against 0.83 and 0.97):
C's Director rations the chase and spends the pressure on tactics instead (≈ 42 tier-1+ tactics per
shift, 3.1 bits of tactic entropy). The blind patrols get their catches by bumping into people.

**2. The learning rungs (D, E, F) don't separate from C against these players.** Paired against C,
none of detections, catches or mean panic moves by more than about 1.5 standard errors. The only
hints are D's slightly better setpoint tracking (RMSE −0.019 ± 0.010) and a couple more jobs done
by the player at D and F (+2.2 ± 1.3, +2.0 ± 1.0). Over a career, E's detections rise from shift 4 to
6 (12.2 → 14.9 → 15.8) and fall back after the maze moves at shift 7 (11.2) while D's drift down
(12.4 → 9.2) — suggestive, not significant. The blink channel is used (`blink_advance` 28 times
against the efficient player) without a measurable effect.

**3. The Ledger does not converge to a different tactic mix per player — the negative result
`Karen.md` §13 asks us to look for.** The Jensen–Shannon divergence between one profile's mix and
the others' is no larger at the learning rungs (E: 0.016–0.068) than at C, where nothing is learnt
(0.030–0.084). The likeliest reason is the players, not the bandit's arithmetic: the scripted bots
react only to *seeing or hearing Karen herself* (and only the skittish one does much about it); PA
decoys, phantom chimes, fog and falsified HUDs change nothing they do, so the Panic Index barely
moves after most tactics and the reward is close to zero. The next experiment is players that
respond to tells — or people.

**4. The Director holds panic below its setpoint.** Mean Panic Index is 0.33–0.36 at C–F, and the
setpoint error (≈ 0.29) is no better than the patrols' (A is 0.027 ± 0.007 *lower*). The pacing
controller is conservative against players this unflappable.

**5. The player profiles behave like themselves.** Efficient clocks out most often under C (50%),
reckless is detected as often but never caught (it outruns her), skittish never finishes a shift
from C up — it spends the shift hiding (3–7 jobs a shift against 12–15). Most tactic use is `follow`
and `favour` (the bots run their energy down, and below 15% she turns helpful).

**6. Fairness held.** 0 violations of the tell rules in 216 shifts. Getting there was the most
useful thing this ablation did: its first runs caught `chase` and `mimicry` acting before their
tell, `favour` with no tell at all, PA lines logged before their chime, a tell one frame short of
0.8 s, and the simplest rungs re-catching a player the moment a lecture ended. All fixed.

**7. The failure taxonomy mostly describes the bots.** Clock blindness (78–89%) and resource
mismanagement (67–86%) dominate at every rung: the scripted players don't reserve time for the
walk to the time clock and sprint their energy to zero, whoever is hunting them.

## The tables

216 simulated shifts: 6 rungs × 3 player profiles, 9 careers of shifts 4–7. Mean ± standard error.

### Headline, by rung (all profiles)

| rung | shifts | player clocked out | Karen detected them | first detection (s) | catches / shift | tactics / shift | tactic entropy (bits) | mean Panic Index | setpoint RMSE | customers lost / shift | fairness violations |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **A** | 36 | 17% | 83% | 104 ± 21 | 0.83 ± 0.19 | 0.0 ± 0.0 | 0.00 ± 0.00 | 0.41 ± 0.02 | 0.27 ± 0.01 | 2.1 ± 0.3 | 0 |
| **B** | 36 | 11% | 92% | 100 ± 15 | 0.97 ± 0.25 | 0.0 ± 0.0 | 0.00 ± 0.00 | 0.37 ± 0.01 | 0.29 ± 0.01 | 2.0 ± 0.3 | 0 |
| **C** | 36 | 22% | 100% | 51 ± 7 | 0.19 ± 0.09 | 41.8 ± 1.5 | 3.06 ± 0.09 | 0.33 ± 0.01 | 0.30 ± 0.01 | 1.4 ± 0.3 | 0 |
| **D** | 36 | 22% | 100% | 71 ± 9 | 0.14 ± 0.07 | 38.2 ± 1.4 | 3.25 ± 0.09 | 0.36 ± 0.01 | 0.28 ± 0.01 | 1.6 ± 0.3 | 0 |
| **E** | 36 | 17% | 100% | 53 ± 7 | 0.17 ± 0.06 | 40.8 ± 1.3 | 3.24 ± 0.08 | 0.35 ± 0.01 | 0.29 ± 0.01 | 1.6 ± 0.3 | 0 |
| **F** | 36 | 14% | 100% | 62 ± 7 | 0.25 ± 0.08 | 41.2 ± 1.4 | 3.49 ± 0.06 | 0.35 ± 0.01 | 0.29 ± 0.01 | 1.6 ± 0.3 | 0 |

### Each rung against rung C, paired by seed and shift

Positive = more than C. Pairs share the same customers and spills; Karen is the only difference.

| rung | pairs | Δ detections | Δ catches | Δ mean panic | Δ setpoint RMSE | Δ jobs done | Δ clocked out |
|---|---|---|---|---|---|---|---|
| A | 36 | -11.8 ± 1.1 | +0.64 ± 0.15 | +0.077 ± 0.016 | -0.027 ± 0.007 | +4.8 ± 1.7 | -0.06 ± 0.09 |
| B | 36 | -10.8 ± 1.1 | +0.78 ± 0.24 | +0.042 ± 0.015 | -0.007 ± 0.009 | +5.2 ± 1.5 | -0.11 ± 0.09 |
| D | 36 | -1.8 ± 1.3 | -0.06 ± 0.10 | +0.028 ± 0.018 | -0.019 ± 0.010 | +2.2 ± 1.3 | +0.00 ± 0.09 |
| E | 36 | +0.1 ± 1.4 | -0.03 ± 0.09 | +0.021 ± 0.015 | -0.010 ± 0.010 | +0.9 ± 1.1 | -0.06 ± 0.09 |
| F | 36 | -1.6 ± 1.1 | +0.06 ± 0.09 | +0.019 ± 0.016 | -0.005 ± 0.010 | +2.0 ± 1.0 | -0.08 ± 0.09 |

### By rung and player profile

| rung | profile | clocked out | detections / shift | catches / shift | mean panic | setpoint RMSE | jobs done / shift |
|---|---|---|---|---|---|---|---|
| A | efficient | 0% | 2.7 ± 0.5 | 1.92 ± 0.36 | 0.44 ± 0.03 | 0.26 ± 0.01 | 15.0 ± 2.8 |
| A | skittish | 8% | 1.2 ± 0.3 | 0.58 ± 0.23 | 0.43 ± 0.02 | 0.27 ± 0.01 | 12.2 ± 2.5 |
| A | reckless | 42% | 1.0 ± 0.2 | 0.00 ± 0.00 | 0.35 ± 0.03 | 0.28 ± 0.02 | 13.1 ± 1.4 |
| B | efficient | 0% | 4.9 ± 1.0 | 2.75 ± 0.37 | 0.39 ± 0.01 | 0.27 ± 0.01 | 15.2 ± 2.5 |
| B | skittish | 8% | 1.8 ± 0.4 | 0.17 ± 0.11 | 0.39 ± 0.03 | 0.29 ± 0.02 | 11.0 ± 2.7 |
| B | reckless | 25% | 1.2 ± 0.2 | 0.00 ± 0.00 | 0.33 ± 0.02 | 0.30 ± 0.02 | 15.3 ± 1.3 |
| C | efficient | 50% | 15.7 ± 2.2 | 0.50 ± 0.23 | 0.32 ± 0.02 | 0.28 ± 0.01 | 12.1 ± 1.9 |
| C | skittish | 0% | 9.0 ± 1.1 | 0.08 ± 0.08 | 0.36 ± 0.02 | 0.30 ± 0.01 | 3.1 ± 0.9 |
| C | reckless | 17% | 15.7 ± 2.0 | 0.00 ± 0.00 | 0.31 ± 0.02 | 0.31 ± 0.02 | 10.7 ± 1.5 |
| D | efficient | 33% | 13.8 ± 1.5 | 0.33 ± 0.19 | 0.37 ± 0.03 | 0.27 ± 0.02 | 15.2 ± 1.4 |
| D | skittish | 0% | 7.8 ± 0.7 | 0.08 ± 0.08 | 0.34 ± 0.02 | 0.30 ± 0.01 | 6.8 ± 1.4 |
| D | reckless | 33% | 13.4 ± 2.1 | 0.00 ± 0.00 | 0.35 ± 0.01 | 0.27 ± 0.01 | 10.3 ± 1.5 |
| E | efficient | 25% | 15.9 ± 1.4 | 0.50 ± 0.15 | 0.34 ± 0.02 | 0.29 ± 0.01 | 13.6 ± 1.7 |
| E | skittish | 0% | 9.9 ± 0.9 | 0.00 ± 0.00 | 0.37 ± 0.01 | 0.29 ± 0.01 | 4.1 ± 1.0 |
| E | reckless | 25% | 14.8 ± 2.0 | 0.00 ± 0.00 | 0.34 ± 0.02 | 0.28 ± 0.02 | 11.0 ± 1.4 |
| F | efficient | 17% | 13.9 ± 1.7 | 0.67 ± 0.19 | 0.34 ± 0.02 | 0.30 ± 0.01 | 15.0 ± 2.1 |
| F | skittish | 0% | 7.7 ± 0.6 | 0.08 ± 0.08 | 0.35 ± 0.01 | 0.30 ± 0.01 | 5.8 ± 1.1 |
| F | reckless | 25% | 13.9 ± 1.6 | 0.00 ± 0.00 | 0.36 ± 0.01 | 0.28 ± 0.02 | 11.0 ± 1.1 |

### Across a career (does she get better at this player?)

Mean detections per shift, by the shift's place in the career. Rung E keeps its Ledger between shifts; C and D start every shift from the prior.

| rung | shift 4 | shift 5 | shift 6 | shift 7 |
|---|---|---|---|---|
| A | 2.7 ± 0.7 | 1.4 ± 0.3 | 1.6 ± 0.4 | 0.9 ± 0.3 |
| B | 1.9 ± 0.6 | 2.3 ± 0.7 | 2.3 ± 0.9 | 4.0 ± 1.3 |
| C | 18.4 ± 1.9 | 10.0 ± 1.3 | 10.9 ± 1.6 | 14.4 ± 3.0 |
| D | 12.4 ± 2.1 | 13.1 ± 2.1 | 11.8 ± 2.1 | 9.2 ± 1.8 |
| E | 12.2 ± 1.9 | 14.9 ± 1.9 | 15.8 ± 2.2 | 11.2 ± 1.3 |
| F | 14.4 ± 2.5 | 8.6 ± 1.2 | 12.9 ± 1.8 | 11.4 ± 1.2 |

### What she reached for, per profile

Most-used tactics (tier 1+) over all careers; the last column is the Jensen–Shannon divergence between this profile's tactic mix and the other two profiles' (0 = identical, 1 = disjoint). Computed from each shift's top four tactics, so it understates the long tail.

| rung | profile | top tactics | JS vs other profiles |
|---|---|---|---|
| C | efficient | follow ×214, spill ×44, favour ×38, shelf_sweep ×32, stalk ×15 | 0.040 |
| C | skittish | follow ×66, favour ×61, spill ×45, shelf_sweep ×36, stalk ×17 | 0.084 |
| C | reckless | follow ×217, favour ×61, spill ×41, shelf_sweep ×30, stalk ×14 | 0.030 |
| D | efficient | follow ×173, favour ×38, spill ×33, shelf_sweep ×30, phantom_chime ×8 | 0.033 |
| D | skittish | follow ×61, favour ×58, spill ×41, shelf_sweep ×27, stalk ×15 | 0.082 |
| D | reckless | follow ×153, favour ×47, shelf_sweep ×38, spill ×38, phantom_chime ×7 | 0.032 |
| E | efficient | follow ×209, favour ×33, spill ×24, shelf_sweep ×20, phantom_chime ×14 | 0.049 |
| E | skittish | follow ×81, favour ×46, spill ×41, shelf_sweep ×21, pa_task_readback ×16 | 0.068 |
| E | reckless | follow ×179, favour ×50, spill ×35, shelf_sweep ×20, pa_task_readback ×17 | 0.016 |
| F | efficient | follow ×176, favour ×38, spill ×32, blink_advance ×28, pa_task_readback ×14 | 0.080 |
| F | skittish | favour ×59, follow ×52, spill ×35, shelf_sweep ×19, pa_task_readback ×17 | 0.105 |
| F | reckless | follow ×142, favour ×49, spill ×32, pa_task_readback ×20, shelf_sweep ×16 | 0.032 |

### Player failures (the eval taxonomy applied to the simulated players)

| rung | starvation | thrashing | interference_blindness | clock_blindness | resource_mismanagement | spurious_completion |
|---|---|---|---|---|---|---|
| A | 3% | 0% | 67% | 83% | 67% | 22% |
| B | 3% | 0% | 75% | 89% | 86% | 22% |
| C | 0% | 3% | 69% | 78% | 83% | 31% |
| D | 0% | 0% | 72% | 78% | 75% | 25% |
| E | 3% | 0% | 69% | 83% | 75% | 31% |
| F | 0% | 0% | 67% | 86% | 78% | 25% |
