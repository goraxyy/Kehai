# Karen — ablation results

The ablation ladder from [`IDEAS.md`](IDEAS.md#4-ablation), run headless against three scripted players.
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

## How well she knows where the player is (belief accuracy)

Added 2026-10-15. Nothing above measures whether Karen's belief is *right*: detections and catches
mix the belief with everything she does about it. This metric scores the belief on its own, and is
the number a learned motion model (replacing the hand-written predict step) would have to beat.

**What is measured** (`Eval/Scripts/BeliefAccuracy.cs`). Five times a second of sim time the
harness, not Karen, looks up the player's true cell on her map (4713 cells of 1.5 m) and scores her
belief on it:

- **log p**: the belief's log-probability of the true cell, in nats, floored at 1e-6; 0 is
  certainty, and a uniform guess scores −8.46. This is the per-step log-likelihood a learned
  predict step would be trained to raise.
- **top-k**: how often the true cell is among her k likeliest (k = 1, 5, 10).
- **error**: metres from her likeliest cell to the true one.

Each is split by whether her sight has the player that frame (`SightSensor.SeesNow`). Out of view
is the number that matters.

Rungs A and B keep no belief, so they are scored on a **last-seen baseline**: a Gaussian round her
last fix on the player (the punch at the time clock, then every frame her sight has them),
σ = 1.5 m + 1 m per second since. Ranking cells by it is ranking them by distance from the fix, so
its top-k and error don't depend on those two constants; only its log p does. It is fed by the same
sight sensor as the grid, including faint glimpses the grid ignores (below awareness 0.2). The
baseline is scored on every rung, so C–F can be compared with it within the same shift.

**The run.** A short first run, not the ablation's final word: 1 career of shifts 4–5 × 3 profiles
× 6 rungs, 150 s shifts, on the local store as of 2026-10-15 (including level work not yet merged).
Of the 36 episodes, 9 are dropped: after a timed-out shift the bot's walk back to the time clock
timed out, so the next shift never started and the harness logged a copy of the last one (a harness
bug, to fix before the full run). That leaves 27 shifts, 3–5 per rung, and 56,334 samples, none off
the map. Mean ± standard error over shifts.

| rung | belief | shifts | out of view | log p | top-1 | top-5 | top-10 | error (m) |
|---|---|---|---|---|---|---|---|---|
| A | last-seen | 4 | 68% | −7.34 ± 0.31 | 29% | 32% | 32% | 22.6 ± 10.3 |
| B | last-seen | 4 | 96% | −7.61 ± 0.36 | 11% | 13% | 18% | 19.3 ± 4.0 |
| C | grid | 4 | 61% | −5.88 ± 1.51 | 20% | 31% | 39% | 13.7 ± 5.4 |
| D | grid | 5 | 71% | −6.74 ± 0.90 | 11% | 17% | 21% | 18.3 ± 3.4 |
| E | grid | 5 | 67% | −6.64 ± 0.93 | 12% | 20% | 24% | 16.2 ± 3.7 |
| F | grid | 5 | 63% | −6.88 ± 0.77 | 10% | 15% | 19% | 19.7 ± 3.5 |

The grid against last-seen, in the same shifts (grid minus last-seen):

| rung | Δ log p out of view | Δ top-5 out of view (pts) | Δ error out of view (m) | Δ top-1 in view | Δ error in view (m) |
|---|---|---|---|---|---|
| C | +0.73 ± 0.77 | −7.9 ± 3.2 | −0.4 ± 1.5 | −60 ± 12 pts | +3.0 ± 1.6 |
| D | +0.12 ± 0.50 | −8.7 ± 3.2 | +2.6 ± 2.7 | −49 ± 13 pts | +3.3 ± 1.3 |
| E | +0.03 ± 0.51 | −14.6 ± 11.9 | +0.4 ± 2.2 | −60 ± 5 pts | +2.7 ± 0.6 |
| F | +0.19 ± 0.49 | −5.3 ± 1.3 | −0.2 ± 1.3 | −48 ± 13 pts | +2.7 ± 1.0 |

**What it shows.**

1. **Out of view she knows something, not much.** The grid's mean log p of −5.9 to −6.9 is, as a
   geometric average, 5–13 times the probability a uniform guess puts on the true cell. But her
   likeliest cell is still 14–20 m from the player, and the true cell is among her five likeliest
   only 15–31% of the time.
2. **C knows where you are better than the patrols, but the belief grid isn't why.** Paired against
   C, the patrols' last-seen belief is 12–17 m further off out of view (B: +12.3 ± 3.5 m). Within
   C–F's own shifts, though, last-seen does as well as the grid: log p within noise, error within
   noise, and last-seen's top-5 is *better* by 5–15 points. C's edge comes from what she does
   (searching, following), which keeps her sightings fresh. The hand-written predict step adds
   nothing measurable over remembering the last sighting, which is the gap a learned motion model
   has to open.
3. **In view the grid is about 3 m off, while last-seen is exact.** The sight sensor reports where the
   player is, but the grid takes each sighting as a Gaussian of σ 0.8–3 m, only once awareness
   passes 0.2, so its top-1 in view is 40–52%. Her pursuit steers by the sensor's `LastSeenPosition`
   for about 3 s after a sighting and only then by the grid's peak, so this matters less to her
   behaviour than to anything that reads the grid itself, such as a motion model built on it.
4. **The skittish player is lost.** Out of view her log p against it is −8.0 to −8.4 at every rung
   (uniform is −8.46) and the error 19–30 m, for the grid and last-seen alike: it hides at the far
   end of the store and nothing she senses says where.
5. **D, E and F don't separate from C** (Δ log p out of view ≈ −0.8 ± 1.4).

With 3–5 shifts per rung these are first numbers; the ± are wide. The next step is the full
3-career run once the clock-in bug is fixed.

Reproduce: `Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation -ablation-careers 1 -ablation-shifts 2 -ablation-start 4 -ablation-seconds 150`,
then `python3 tools/eval/analyze_ablation.py <kehai_eval>/ablation_<time>.jsonl`; the
belief tables are its "How well she knows where the player is" section.
