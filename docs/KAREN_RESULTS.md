# Karen — ablation results

The ablation ladder from [`IDEAS.md`](IDEAS.md#4-ablation), run headless against four simulated
players that react to her. Each rung adds one mechanism, so a difference between neighbouring
rungs is that mechanism. This is the run of 2026-10-16, against today's Karen: her touch kills,
she is slightly faster than the player, and the Director's blink dash is part of every rung from C.

| rung | Karen | learns |
|---|---|---|
| A | random patrol; chases what she sees | — |
| B | a fixed tour of the sales floor, last-seen search, the old random sabotage | — |
| C | belief grid (incl. negative evidence), utility goals, the tactic library, the Director's pacing and blink dash | nothing — every tactic scored by its authored prior |
| D | + the bandit | within a shift; starts each shift from the prior |
| E | + the persistent Ledger | across shifts: tactic values, where you dwell and hide, your routes |
| F | + a trained PPO policy picks the tactic ([`tools/rl`](../tools/rl/README.md)) | offline, from training runs |

**Rung F is not in this run.** It is built, but no policy has been trained yet. Without a model it
falls back to the bandit's pick, which would make it a second copy of E.

## How it was run

- **Players.** `SimulatedPlayer` in four profiles ([`tools/eval/README.md`](../tools/eval/README.md#the-simulated-players)):
  - *efficient*: nearest job first;
  - *skittish*: over-reacts to everything;
  - *reckless*: sprints, ignores noise;
  - *cautious*: held out, never used to tune her or to train a policy.

  They sense what a person would, through the same observation an external agent gets:
  - her when she's in view;
  - her footsteps close by;
  - her warnings as far as they carry;
  - her blink dash as a person would notice it.

  What they sense frightens them, each profile in its own way (`BotFear`). The fear shows through
  the channels her Panic Index reads: looking round, sprinting, freezing, abandoning jobs,
  detouring, dropping things, hiding. Her touch kills, so they also back away when she's close.
- **Careers.** A career is four consecutive shifts in one session: career shifts 4 to 7, so most
  tactics are unlocked and the maze mutates once, before shift 7. Each profile plays three
  careers. The Ledger is wiped at the start of each career and, from rung E up, kept between its
  shifts.
- **Pairing.** For a given profile and career every rung gets the same seed, so the same customers
  and the same spills. Rung-to-rung differences are therefore computed per (seed, shift) pair.
- **Size.** 240 shifts: 5 rungs × 4 profiles × 3 careers × 4 shifts, so 48 per rung and 12 per
  (rung, profile). It ran as 12 Unity processes, one per career and profile; a single long
  process runs this 8 GB Mac out of memory. The 12 output files were then joined.
- **Control.** The same harness with the old players, which don't react to her tricks
  (`-ablation-static-bots`). Only the skittish one does anything, fleeing when it sees or hears
  her. It ran on career 1's seeds for the three original profiles: 60 shifts.
- **Time.** Fixed 1/15 s step, 150 s shifts plus overtime, `-batchmode -nographics`.
- **Caveat on replay.** The first shift after launch replays exactly for the same seed. Later shifts
  in one process drift a little (engine-side threading in NavMesh carving and crowd updates), which
  adds noise on top of the pairing. Means ± standard errors below are over shifts.
- **What the numbers are not.** These are scripted players, not people. "Panic" is Karen's own
  Panic Index estimate from the players' movement, and the bots' completion rates say as much about
  the bots as about her. The comparisons between rungs are the point.

Reproduce, one process per career (seed 1000, 1100, 1200) and profile:

```bash
Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation -ablation-rungs ABCDE -ablation-profiles cautious -ablation-careers 1 -ablation-seed 1000 -ablation-shifts 4 -ablation-start 4 -ablation-seconds 150 -kehai-no-records -eval-timeout-min 60 -ablation-out <dir>
```

Add `-ablation-static-bots` for the control. Join the `ablation_*.jsonl` files, then run
`python3 tools/eval/analyze_ablation.py <joined>.jsonl`.

## What it shows

**1. The belief grid and planner (rung C) is still the big step.**
- **Detections:** against the same shifts, C detects the player about 6 more times per shift than
  the patrols (Δ −5.9 ± 0.6 for A, −5.7 ± 0.6 for B).
- **Speed:** C finds them in a third of the time (first detection 31 ± 6 s, against 85 ± 15 s and
  101 ± 17 s), and finds them in every shift (the patrols: 79% and 83%).
- **Catches:** C catches less (0.56 a shift, against 0.75 and 0.77). The Director rations chases
  and spends the pressure on tactics instead: about 30 a shift, 2.5 bits of tactic entropy.

**2. Learning (D, E) makes her slightly calmer, not better.**
- **Paired against C:**
  - mean panic falls by 0.039 ± 0.017 at D and 0.032 ± 0.018 at E;
  - catches fall by 0.15 ± 0.09 and 0.12 ± 0.11;
  - detections (+0.5 ± 0.7, +0.4 ± 0.9) and setpoint error (+0.008 ± 0.011, +0.001 ± 0.014) don't
    move.
- **Why panic falls.** The bandit's reward for a tactic is the change in panic in the 20 s after
  it. The Director then steers toward its setpoint: while panic is above it, she prefers the
  tactics she has learnt calm the player. That steering shows: panic is lower. But she doesn't
  track the setpoint any better.
- **No career trend.** Over a career, E's detections go 5.3 → 8.3 → 5.9 → 9.2.

**3. A first sign of tailoring to the player.**
- **The measure.** The Jensen–Shannon divergence between one profile's tactic mix and the others'.
- **By rung:**
  - C: 0.012–0.046 (mean 0.026), where nothing is learnt;
  - D: 0.020–0.051 (mean 0.037);
  - E: 0.037–0.117 (mean 0.061), the rung that keeps a Ledger per player.
- **The held-out player.** The largest value is E against the cautious player (0.117): fewer
  follows (75, against 126–221 for the others) and more phantom chimes (15).
- **Against the last run.** That run used players that didn't react to her, and E tailored no more
  than C. This is suggestive, not proof: the values are small, from each shift's top four tactics,
  and 12 shifts per cell.

**4. Reacting players changed what there is to learn from — but not the reward per tactic.**
This compares the control with the same seeds, 12 pairs per rung (C · D · E):
- **Kills:** the reacting players are killed less: 0.75 · 0.50 · 0.58 a shift, against
  0.92 · 1.00 · 0.75 (at D, −0.50 ± 0.15).
- **Tactics per shift:** they live longer, so she gets 1.5–4 times as many: 24 · 33 · 32,
  against 16 · 8 · 19.
- **Reward per tactic:** the mean |Δ panic| after each tactic didn't rise overall: +0.026 ± 0.018
  at C, −0.007 ± 0.014 at D, −0.032 ± 0.018 at E.

The learners now get many more samples, and the samples differ by player (finding 3). But each
one is no bigger.

**5. Her belief is better than her last sighting at distance, though still near a guess.**
- **Still near a guess out of view.** log p is −7.3 to −7.4, against −8.46 for a uniform guess.
  The true cell is among her five likeliest 10–11% of the time, and she is about 19 m off.
- **But the grid now beats the last sighting on distance.** In the same shifts its likeliest cell
  is 4.0–4.6 m nearer the player than the last-seen baseline's. It ties on log p (+0.06 to +0.12
  ± 0.09) and is slightly worse on top-5 (−0.4 to −3.2 points).
- **Why it changed.** In the last run (one player, which didn't react), last-seen beat the grid on
  every measure. Players who flee and hide after being seen make the last sighting go stale fast,
  and the grid's prediction is what makes up for that. The gap is largest against the skittish
  and cautious players (5–8 m).
- **Learning doesn't change the belief out of view.** In view, D is worse (log p −0.49 ± 0.20).

**6. The player profiles behave like themselves.**
- **Fear.** Mean fear across A–E: reckless 0.05–0.18, efficient 0.08–0.38, cautious 0.11–0.27,
  skittish 0.18–0.49.
- **Panic Index at C:** efficient 0.31, reckless 0.37, skittish 0.44, cautious 0.45.
- **Hiding:** skittish and cautious hide 4–6 times a shift at C–E. Reckless never does.
- **Backing off:** efficient backs away about 90–120 times a shift at C–E. Her `follow` tactic keeps her
  about 2 m behind it, inside its 6 m.
- **Clocking out is rare** (0–10% by rung). Kills are still common: 0.42–0.56 a shift at C–E. And
  most of her tactic use is `follow` and `favour`.

**7. Fairness held.** There were 0 violations of the tell rules in 300 shifts (240 plus the
control).

**8. The failure taxonomy still mostly describes the bots.** Interference blindness (54–71% at C–E)
and resource mismanagement (67–73%) dominate. Clock blindness is lower than in the last run
(42–48%, against 78–89%), probably because shifts that end in a kill never get near the clock.

## The tables

240 simulated shifts: 5 rungs × 4 player profiles × 3 careers of shifts 4–7, reacting players.
Mean ± standard error.

### Headline, by rung (all profiles)

| rung | shifts | player clocked out | Karen detected them | first detection (s) | catches / shift | tactics / shift | tactic entropy (bits) | mean Panic Index | setpoint RMSE | customers lost / shift | fairness violations |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **A** | 48 | 10% | 79% | 85 ± 15 | 0.75 ± 0.06 | 0.0 ± 0.0 | 0.00 ± 0.00 | 0.40 ± 0.02 | 0.30 ± 0.02 | 0.7 ± 0.1 | 0 |
| **B** | 48 | 0% | 83% | 101 ± 17 | 0.77 ± 0.06 | 0.0 ± 0.0 | 0.00 ± 0.00 | 0.42 ± 0.02 | 0.29 ± 0.01 | 0.6 ± 0.1 | 0 |
| **C** | 48 | 2% | 100% | 31 ± 6 | 0.56 ± 0.07 | 29.9 ± 2.4 | 2.51 ± 0.12 | 0.39 ± 0.02 | 0.28 ± 0.01 | 0.9 ± 0.1 | 0 |
| **D** | 48 | 10% | 100% | 25 ± 3 | 0.42 ± 0.07 | 33.2 ± 2.1 | 2.99 ± 0.13 | 0.35 ± 0.01 | 0.29 ± 0.01 | 0.7 ± 0.1 | 0 |
| **E** | 48 | 8% | 100% | 27 ± 4 | 0.44 ± 0.07 | 33.0 ± 2.2 | 2.83 ± 0.13 | 0.36 ± 0.01 | 0.28 ± 0.01 | 0.7 ± 0.1 | 0 |

### Each rung against rung C, paired by seed and shift

Positive = more than C. Pairs share the same customers and spills; Karen is the only difference.

| rung | pairs | Δ detections | Δ catches | Δ mean panic | Δ setpoint RMSE | Δ jobs done | Δ clocked out |
|---|---|---|---|---|---|---|---|
| A | 48 | -5.9 ± 0.6 | +0.19 ± 0.10 | +0.010 ± 0.029 | +0.022 ± 0.018 | -0.7 ± 1.6 | +0.08 ± 0.05 |
| B | 48 | -5.7 ± 0.6 | +0.21 ± 0.09 | +0.029 ± 0.024 | +0.014 ± 0.014 | +2.2 ± 1.9 | -0.02 ± 0.02 |
| D | 48 | +0.5 ± 0.7 | -0.15 ± 0.09 | -0.039 ± 0.017 | +0.008 ± 0.011 | +0.2 ± 1.3 | +0.08 ± 0.05 |
| E | 48 | +0.4 ± 0.9 | -0.12 ± 0.11 | -0.032 ± 0.018 | +0.001 ± 0.014 | +0.2 ± 1.3 | +0.06 ± 0.05 |

### By rung and player profile

| rung | profile | clocked out | detections / shift | catches / shift | mean panic | setpoint RMSE | jobs done / shift |
|---|---|---|---|---|---|---|---|
| A | efficient | 25% | 0.8 ± 0.2 | 0.75 ± 0.13 | 0.38 ± 0.06 | 0.29 ± 0.04 | 7.1 ± 2.6 |
| A | skittish | 0% | 1.1 ± 0.1 | 0.92 ± 0.08 | 0.43 ± 0.04 | 0.32 ± 0.03 | 7.0 ± 2.8 |
| A | reckless | 8% | 0.7 ± 0.1 | 0.50 ± 0.15 | 0.36 ± 0.03 | 0.28 ± 0.02 | 8.8 ± 1.5 |
| A | cautious | 8% | 1.2 ± 0.3 | 0.83 ± 0.11 | 0.43 ± 0.05 | 0.31 ± 0.03 | 6.4 ± 2.3 |
| B | efficient | 0% | 1.2 ± 0.2 | 0.92 ± 0.08 | 0.40 ± 0.05 | 0.32 ± 0.04 | 7.8 ± 3.2 |
| B | skittish | 0% | 1.2 ± 0.4 | 0.42 ± 0.15 | 0.42 ± 0.03 | 0.28 ± 0.01 | 19.7 ± 2.6 |
| B | reckless | 0% | 0.9 ± 0.1 | 0.92 ± 0.08 | 0.42 ± 0.02 | 0.24 ± 0.02 | 7.6 ± 1.8 |
| B | cautious | 0% | 1.2 ± 0.2 | 0.83 ± 0.11 | 0.44 ± 0.04 | 0.33 ± 0.03 | 5.7 ± 2.3 |
| C | efficient | 8% | 9.8 ± 1.5 | 0.50 ± 0.15 | 0.31 ± 0.02 | 0.29 ± 0.02 | 11.7 ± 2.6 |
| C | skittish | 0% | 7.4 ± 1.2 | 0.33 ± 0.14 | 0.44 ± 0.02 | 0.27 ± 0.02 | 5.2 ± 1.7 |
| C | reckless | 0% | 4.7 ± 0.9 | 0.92 ± 0.08 | 0.37 ± 0.03 | 0.26 ± 0.02 | 8.2 ± 2.2 |
| C | cautious | 0% | 5.6 ± 0.7 | 0.50 ± 0.15 | 0.45 ± 0.04 | 0.29 ± 0.02 | 6.8 ± 1.6 |
| D | efficient | 25% | 7.8 ± 1.3 | 0.25 ± 0.13 | 0.32 ± 0.02 | 0.31 ± 0.02 | 11.4 ± 2.9 |
| D | skittish | 0% | 6.7 ± 1.0 | 0.50 ± 0.15 | 0.38 ± 0.02 | 0.27 ± 0.02 | 4.4 ± 1.4 |
| D | reckless | 17% | 8.4 ± 1.7 | 0.58 ± 0.15 | 0.29 ± 0.03 | 0.31 ± 0.02 | 10.1 ± 2.9 |
| D | cautious | 0% | 6.3 ± 0.9 | 0.33 ± 0.14 | 0.41 ± 0.02 | 0.25 ± 0.01 | 6.8 ± 1.4 |
| E | efficient | 17% | 7.5 ± 1.2 | 0.50 ± 0.15 | 0.30 ± 0.03 | 0.28 ± 0.02 | 10.1 ± 3.3 |
| E | skittish | 0% | 7.3 ± 0.9 | 0.58 ± 0.15 | 0.42 ± 0.02 | 0.29 ± 0.02 | 5.1 ± 1.4 |
| E | reckless | 8% | 8.3 ± 1.3 | 0.42 ± 0.15 | 0.31 ± 0.02 | 0.29 ± 0.02 | 10.8 ± 2.7 |
| E | cautious | 8% | 5.7 ± 0.8 | 0.25 ± 0.13 | 0.41 ± 0.02 | 0.26 ± 0.01 | 7.0 ± 1.7 |

### The reward signal, and how frightened the players were

- **Reward signal:** the mean |Δ panic| in the 20 s after each tactic she was rewarded for. This is
  what the bandit and the policy have to learn from.
- **Fear:** the simulated player's own (`BotFear`).
- **Back-offs:** each step back counts.

| rung | profile | shifts | reward signal | mean fear | startles / shift | back-offs / shift | hides / shift | kills / shift |
|---|---|---|---|---|---|---|---|---|
| A | efficient | 12 | — | 0.08 ± 0.03 | 0.1 ± 0.1 | 0.7 ± 0.2 | 0.0 ± 0.0 | 0.75 ± 0.13 |
| A | skittish | 12 | — | 0.18 ± 0.03 | 0.8 ± 0.2 | 2.1 ± 0.5 | 1.1 ± 0.3 | 0.92 ± 0.08 |
| A | reckless | 12 | — | 0.05 ± 0.03 | 0.6 ± 0.1 | 0.3 ± 0.1 | 0.0 ± 0.0 | 0.50 ± 0.15 |
| A | cautious | 12 | — | 0.11 ± 0.02 | 0.8 ± 0.3 | 1.8 ± 0.4 | 1.4 ± 0.3 | 0.83 ± 0.11 |
| B | efficient | 12 | — | 0.10 ± 0.03 | 0.6 ± 0.2 | 2.3 ± 0.4 | 0.0 ± 0.0 | 0.92 ± 0.08 |
| B | skittish | 12 | — | 0.18 ± 0.02 | 0.8 ± 0.3 | 5.2 ± 0.7 | 1.6 ± 0.5 | 0.42 ± 0.15 |
| B | reckless | 12 | — | 0.05 ± 0.01 | 1.2 ± 0.2 | 0.5 ± 0.2 | 0.0 ± 0.0 | 0.92 ± 0.08 |
| B | cautious | 12 | — | 0.11 ± 0.01 | 0.5 ± 0.2 | 2.7 ± 0.6 | 1.2 ± 0.4 | 0.83 ± 0.11 |
| C | efficient | 12 | 0.076 ± 0.009 | 0.32 ± 0.05 | 1.9 ± 0.5 | 98.8 ± 37.4 | 0.1 ± 0.1 | 0.50 ± 0.15 |
| C | skittish | 12 | 0.089 ± 0.008 | 0.49 ± 0.04 | 2.2 ± 0.4 | 83.7 ± 44.2 | 5.8 ± 1.1 | 0.33 ± 0.14 |
| C | reckless | 12 | 0.082 ± 0.015 | 0.18 ± 0.03 | 2.7 ± 0.5 | 43.4 ± 25.4 | 0.0 ± 0.0 | 0.92 ± 0.08 |
| C | cautious | 12 | 0.098 ± 0.012 | 0.27 ± 0.04 | 1.8 ± 0.5 | 48.4 ± 31.7 | 4.4 ± 0.7 | 0.50 ± 0.15 |
| D | efficient | 12 | 0.074 ± 0.007 | 0.28 ± 0.06 | 2.2 ± 0.4 | 87.8 ± 47.7 | 0.0 ± 0.0 | 0.25 ± 0.13 |
| D | skittish | 12 | 0.083 ± 0.006 | 0.45 ± 0.03 | 1.1 ± 0.4 | 49.2 ± 29.3 | 6.2 ± 0.9 | 0.50 ± 0.15 |
| D | reckless | 12 | 0.069 ± 0.014 | 0.16 ± 0.02 | 4.0 ± 0.9 | 19.6 ± 7.5 | 0.0 ± 0.0 | 0.58 ± 0.15 |
| D | cautious | 12 | 0.099 ± 0.009 | 0.26 ± 0.02 | 2.8 ± 0.4 | 24.4 ± 11.1 | 4.9 ± 0.7 | 0.33 ± 0.14 |
| E | efficient | 12 | 0.072 ± 0.009 | 0.38 ± 0.07 | 1.3 ± 0.3 | 121.4 ± 49.7 | 0.0 ± 0.0 | 0.50 ± 0.15 |
| E | skittish | 12 | 0.077 ± 0.008 | 0.47 ± 0.03 | 2.2 ± 0.5 | 37.3 ± 17.3 | 6.1 ± 1.0 | 0.58 ± 0.15 |
| E | reckless | 12 | 0.065 ± 0.009 | 0.16 ± 0.02 | 4.6 ± 0.8 | 57.9 ± 22.6 | 0.0 ± 0.0 | 0.42 ± 0.15 |
| E | cautious | 12 | 0.100 ± 0.007 | 0.26 ± 0.03 | 2.8 ± 0.5 | 35.1 ± 23.5 | 5.5 ± 0.6 | 0.25 ± 0.13 |

Rungs A and B use no tactics, so they have no reward signal.

### Across a career (does she get better at this player?)

Mean detections per shift, by the shift's place in the career. Rung E keeps its Ledger between
shifts; C and D start every shift from the prior.

| rung | shift 4 | shift 5 | shift 6 | shift 7 |
|---|---|---|---|---|
| A | 1.2 ± 0.2 | 1.0 ± 0.2 | 0.7 ± 0.2 | 1.0 ± 0.2 |
| B | 0.7 ± 0.1 | 1.3 ± 0.2 | 1.3 ± 0.3 | 1.2 ± 0.2 |
| C | 6.8 ± 1.2 | 6.8 ± 1.6 | 7.6 ± 1.0 | 6.2 ± 1.2 |
| D | 4.0 ± 0.7 | 8.9 ± 1.3 | 8.8 ± 1.5 | 7.6 ± 1.0 |
| E | 5.3 ± 0.6 | 8.3 ± 0.9 | 5.9 ± 0.8 | 9.2 ± 1.5 |

### What she reached for, per profile

The most-used tactics (tier 1+) over all careers. The last column is the Jensen–Shannon divergence
between this profile's tactic mix and the other profiles' (0 = identical, 1 = disjoint). It is
computed from each shift's top four tactics, so it understates the long tail.

| rung | profile | top tactics | JS vs other profiles |
|---|---|---|---|
| C | efficient | follow ×191, favour ×38, spill ×33, shelf_sweep ×33, stalk ×7 | 0.015 |
| C | skittish | follow ×169, favour ×46, spill ×37, shelf_sweep ×34, stalk ×6 | 0.012 |
| C | reckless | follow ×106, spill ×19, shelf_sweep ×17, favour ×6, stalk ×5 | 0.046 |
| C | cautious | follow ×117, spill ×41, shelf_sweep ×39, favour ×37, pa_task_readback ×6 | 0.032 |
| D | efficient | follow ×178, favour ×44, spill ×27, shelf_sweep ×21, stalk ×6 | 0.048 |
| D | skittish | follow ×95, favour ×42, spill ×33, shelf_sweep ×24, stalk ×5 | 0.029 |
| D | reckless | follow ×110, favour ×34, shelf_sweep ×30, spill ×19, phantom_chime ×5 | 0.020 |
| D | cautious | follow ×106, favour ×44, shelf_sweep ×33, spill ×32, phantom_chime ×11 | 0.051 |
| E | efficient | follow ×156, favour ×33, spill ×20, shelf_sweep ×8, bin_tamper ×5 | 0.042 |
| E | skittish | follow ×126, favour ×39, spill ×29, shelf_sweep ×12, pa_footsteps ×6 | 0.037 |
| E | reckless | follow ×221, favour ×41, spill ×29, shelf_sweep ×15, pa_task_readback ×7 | 0.046 |
| E | cautious | follow ×75, favour ×45, spill ×29, shelf_sweep ×27, phantom_chime ×15 | 0.117 |

### Player failures (the eval taxonomy applied to the simulated players)

| rung | starvation | thrashing | interference_blindness | clock_blindness | resource_mismanagement | spurious_completion |
|---|---|---|---|---|---|---|
| A | 0% | 0% | 38% | 15% | 44% | 4% |
| B | 2% | 0% | 48% | 23% | 46% | 0% |
| C | 4% | 0% | 71% | 42% | 73% | 6% |
| D | 0% | 0% | 54% | 48% | 71% | 19% |
| E | 2% | 0% | 67% | 48% | 67% | 10% |

### Against players that don't react (the control)

Career 1's seeds and the three original profiles: 12 pairs per rung, in which only the players
differ. Δ is reacting minus static.

| rung | pairs | Δ reward signal | Δ killed | Δ tactics / shift | Δ mean panic | Δ jobs | Δ clocked out | Δ shift length (s) |
|---|---|---|---|---|---|---|---|---|
| A | 12 | — | +0.08 ± 0.15 | — | +0.017 ± 0.022 | +0.8 ± 3.2 | -0.08 ± 0.15 | +30 ± 39 |
| B | 12 | — | -0.08 ± 0.08 | — | +0.073 ± 0.026 | +4.3 ± 2.9 | +0.00 ± 0.00 | +64 ± 37 |
| C | 12 | +0.026 ± 0.018 | -0.17 ± 0.17 | +7.9 ± 5.5 | -0.019 ± 0.044 | +0.5 ± 2.3 | +0.00 ± 0.00 | +36 ± 40 |
| D | 12 | -0.007 ± 0.014 | -0.50 ± 0.15 | +24.7 ± 5.2 | -0.084 ± 0.035 | +3.3 ± 3.7 | +0.00 ± 0.00 | +181 ± 43 |
| E | 12 | -0.032 ± 0.018 | -0.17 ± 0.21 | +12.8 ± 7.5 | -0.072 ± 0.047 | +2.0 ± 3.7 | -0.08 ± 0.08 | +107 ± 63 |

| rung | players | reward signal | killed | tactics / shift | mean panic | jobs / shift | clocked out |
|---|---|---|---|---|---|---|---|
| A | static | — | 0.67 ± 0.14 | — | 0.377 ± 0.038 | 9.1 ± 2.9 | 0.25 ± 0.13 |
| A | reacting | — | 0.75 ± 0.13 | — | 0.395 ± 0.051 | 9.8 ± 2.7 | 0.17 ± 0.11 |
| B | static | — | 0.83 ± 0.11 | — | 0.326 ± 0.027 | 11.0 ± 2.9 | 0.00 ± 0.00 |
| B | reacting | — | 0.75 ± 0.13 | — | 0.399 ± 0.021 | 15.3 ± 2.9 | 0.00 ± 0.00 |
| C | static | 0.075 ± 0.008 | 0.92 ± 0.08 | 16.4 ± 4.4 | 0.408 ± 0.034 | 5.5 ± 1.3 | 0.00 ± 0.00 |
| C | reacting | 0.101 ± 0.012 | 0.75 ± 0.13 | 24.3 ± 4.5 | 0.390 ± 0.024 | 6.0 ± 2.0 | 0.00 ± 0.00 |
| D | static | 0.089 ± 0.010 | 1.00 ± 0.00 | 8.4 ± 1.4 | 0.444 ± 0.037 | 5.2 ± 1.7 | 0.00 ± 0.00 |
| D | reacting | 0.082 ± 0.008 | 0.50 ± 0.15 | 33.1 ± 4.4 | 0.359 ± 0.021 | 8.5 ± 3.3 | 0.00 ± 0.00 |
| E | static | 0.096 ± 0.015 | 0.75 ± 0.13 | 18.9 ± 5.5 | 0.418 ± 0.032 | 6.3 ± 2.4 | 0.08 ± 0.08 |
| E | reacting | 0.064 ± 0.008 | 0.58 ± 0.15 | 31.8 ± 5.3 | 0.345 ± 0.028 | 8.3 ± 3.2 | 0.00 ± 0.00 |

## How well she knows where the player is (belief accuracy)

None of the tables above measures whether Karen's belief is *right*: detections and catches mix the
belief with everything she does about it. This metric scores the belief on its own, and is the
number a learned motion model (replacing the hand-written predict step) would have to beat.

**What is measured** (`Eval/Scripts/BeliefAccuracy.cs`). Five times a second of sim time, the
harness (not Karen) looks up the player's true cell on her map, about 4,700 cells of 1.5 m. It then
scores her belief on that cell:

- **log p**: the belief's log-probability of the true cell, in nats, floored at 1e-6. 0 is
  certainty, and a uniform guess scores −8.46. This is the per-step log-likelihood a learned
  predict step would be trained to raise.
- **top-k**: how often the true cell is among her k likeliest (k = 1, 5, 10).
- **error**: metres from her likeliest cell to the true one.

Each is split by whether her sight has the player that frame (`SightSensor.SeesNow`). Out of view
is the number that matters.

**The last-seen baseline.** Rungs A and B keep no belief, so they are scored on a Gaussian round
her last fix on the player: the punch at the time clock, then every frame her sight has them, with
σ = 1.5 m + 1 m per second since.
- Ranking cells by it is ranking them by distance from the fix, so its top-k and error don't
  depend on those two constants; only its log p does.
- It is fed by the same sight sensor as the grid, including faint glimpses the grid ignores (below
  awareness 0.2).
- It is scored on every rung, so C–E can be compared with it within the same shift.

All 240 shifts of the run above.

**Out of view:**

| rung | belief | out of view | log p | top-1 (%) | top-5 (%) | top-10 (%) | error (m) |
|---|---|---|---|---|---|---|---|
| A | last-seen | 94% | −7.01 ± 0.20 | 11.1 ± 2.7 | 12.8 ± 2.7 | 16.0 ± 3.0 | 17.7 ± 1.4 |
| B | last-seen | 96% | −7.22 ± 0.17 | 4.9 ± 1.3 | 6.6 ± 1.4 | 9.9 ± 1.7 | 19.7 ± 1.3 |
| C | grid | 68% | −7.40 ± 0.14 | 2.6 ± 0.6 | 10.0 ± 1.5 | 14.7 ± 1.8 | 18.6 ± 1.0 |
| D | grid | 75% | −7.30 ± 0.16 | 2.4 ± 0.4 | 11.0 ± 1.7 | 15.3 ± 2.1 | 19.1 ± 1.1 |
| E | grid | 69% | −7.34 ± 0.15 | 2.6 ± 0.5 | 10.4 ± 1.6 | 14.4 ± 1.8 | 19.7 ± 1.1 |

**In view**, the grid's top-1 is 43–53% and its error 1.9–2.4 m (C best); last-seen is exact by
construction.

**The grid against last-seen, in the same shifts** (grid minus last-seen):

| rung | Δ log p out of view | Δ top-5 out of view (pts) | Δ error out of view (m) |
|---|---|---|---|
| C | +0.06 ± 0.09 | −0.4 ± 0.6 | −4.6 ± 0.8 |
| D | +0.07 ± 0.09 | −3.2 ± 1.7 | −4.1 ± 0.7 |
| E | +0.12 ± 0.09 | −2.4 ± 0.9 | −4.0 ± 0.7 |

**Against C, paired by seed and shift**, out of view:
- D: Δ log p +0.10 ± 0.16, Δ error +0.4 ± 1.2 m.
- E: Δ log p +0.06 ± 0.14, Δ error +1.1 ± 1.2 m.
- In view, D's log p is 0.49 ± 0.20 lower.

**By player, out of view** (grid · last-seen error, m):

| rung | efficient | skittish | reckless | cautious |
|---|---|---|---|---|
| C | 15.6 · 19.3 | 20.0 · 25.5 | 14.7 · 16.7 | 24.1 · 31.6 |
| D | 16.3 · 18.8 | 21.0 · 27.9 | 14.0 · 14.3 | 24.9 · 31.7 |
| E | 15.3 · 16.5 | 20.7 · 26.9 | 17.1 · 18.8 | 25.7 · 32.4 |

What this shows is in finding 5 above. The player who hides most (cautious) is the one she
knows least about, and the one where the grid's prediction gains most over the last sighting.

## Earlier runs

The tables from the earlier runs are in this file's history (before 2026-10-16). Those runs used
the old Karen (blink as rung F, a catch was a written warning) and players that didn't react to
her tricks.

- **The first ladder (216 shifts, 3 profiles).**
  - It found the same big step from patrols to rung C: detection in about half the time (51 s
    against about 100 s).
  - No separation of the learning rungs from C.
  - No more tailoring at E than at C (Jensen–Shannon 0.016–0.068 against 0.030–0.084). The
    reason was the players: nothing she did, short of being seen or heard, changed their
    behaviour.
- **The first belief run (72 shifts, the efficient player only).** It found the last sighting
  beating the grid on every out-of-view measure.
- **What the ablation achieved early.** The fairness checker caught real bugs in its first runs:
  - `chase` and `mimicry` acting before their tell;
  - `favour` with no tell at all;
  - PA lines logged before their chime;
  - a tell one frame short of 0.8 s;
  - the simplest rungs re-catching a player the moment a lecture ended.

  All were fixed. There have been no violations since.
