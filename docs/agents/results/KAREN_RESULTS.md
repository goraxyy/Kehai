# Karen — ablation results

The ablation ladder from [`IDEAS.md`](../../you/IDEAS.md#4-ablation), run headless against four simulated
players that react to her. Each rung adds one mechanism, so a difference between neighbouring
rungs is that mechanism. This is the run of 2026-10-16, against today's Karen: her touch kills,
she is slightly faster than the player, and the Director's blink dash is part of every rung from C.

> **Karen 0.2.0** changes her eyes, where she works, the Hunt, her tricks and her learning
> ([Karen.md §16](../karen/Karen.md#16-karen-020)). Everything above [Karen 0.2.0, so far](#karen-020-so-far)
> is about the Karen before it. 0.2.0's test results and its ablation against `main` (6 November)
> are in that section.
>
> **Karen 0.3.0** rebuilds her tricks. Its ablation against `main` (7 November) is in
> [Karen 0.3.0: the ablation](#karen-030-the-ablation-7-november).

| rung | Karen | learns |
|---|---|---|
| A | random patrol; chases what she sees | — |
| B | a fixed tour of the sales floor, last-seen search, the old random sabotage | — |
| C | belief grid (incl. negative evidence), utility goals, the tactic library, the Director's pacing and blink dash | nothing — every tactic scored by its authored prior |
| D | + the bandit | within a shift; starts each shift from the prior |
| E | + the persistent Ledger | across shifts: tactic values, where you dwell and hide, your routes |
| F | + a trained PPO policy picks the tactic ([`tools/rl`](../../../tools/rl/README.md)) | offline, from training runs |

**Rung F is not in this run.** It is built, but no policy has been trained yet. Without a model it
falls back to the bandit's pick, which would make it a second copy of E.

## How it was run

- **Players.** `SimulatedPlayer` in four profiles ([`tools/eval/README.md`](../../../tools/eval/README.md#the-simulated-players)):
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

## Karen 0.2.0, so far

Two kinds of result. The first four parts come from the EditMode tests: her own code, run on
scripted situations, not whole shifts. All 290 tests pass (6 November, with everything both
builders made). The last two are the ablation against `main`, run on 6 November: whole shifts
against the simulated players, paired by seed.

### Her eyes: the walk-by set

Six scripted passes plus the 15 m target, stepped at 60 frames a second through her own eye
(`AwarenessTests`). Lit is 0.9, dim 0.3. "Seen" is her awareness reaching its threshold; "moving at
you" adds her appraisal (5 times a second) and two frames, at worst, so it's worked out, not timed.

| You are | Score | Seen | Moving at you by | Where she expects you |
|---|---|---|---|---|
| Walking, 10 m, dead centre | 0.461 | **0.67 s** (target 0.7) | 0.90 s | 0.33 s |
| Walking, 10 m, 45° off | 0.295 | 1.03 s | 1.27 s | 0.52 s |
| Walking, 10 m, 70° off | 0.059 | 5.12 s | 5.35 s | 2.57 s |
| Walking, 15 m, 45° off | 0.218 | **1.40 s** (target 1.5) | 1.63 s | 0.70 s |
| Walking, 25 m, dead centre | 0.186 | 1.63 s | 1.87 s | 0.82 s |
| Walking, 36 m, dead centre | 0.106 | 2.88 s | 3.12 s | 1.45 s |
| Crouched, a third showing, 10 m, 45° off, dim | 0.037 | **never** | never | never |

- **Every target is met.** The crouched case scores under the 0.04 floor, even where she expects
  you. With two thirds of you showing, she sees you.
- **The gain (1.65) is close to as low as it can go:** a tenth lower misses a target (tested).
- **The bigger sweep** (`AwarenessInTheStoreTests`, 164 spots across her store, facing four ways):
  a cell in view is looked at every 4.3 ticks, against 5.1 before. A sweep casts 108 rays on
  average (120 at most) in 1.3 ms in the editor, 10 sweeps a second. Before, it cast about a third
  of its 70. Whether that matters in play is for the ablation.

### The combo learner: the planted check

A simulation, not the store (`ComboTests`): a ten-minute shift is a choice every 10 s between
Blackout, Ambush, Bin tamper and doing nothing, each with its own cooldown. Ambush pays +0.30
while the lights are out and +0.02 otherwise; Blackout +0.10 and Bin tamper +0.08, all with noise.
The choosing and learning are the game's own code (`HtnPlanner.Rank`, `KarenLedger`), with a ledger
kept between shifts as on rung E.

**Written down before the run** (20 seeds, 8 shifts each):

| Criterion | Result |
|---|---|
| Blackout → Ambush is found within 8 shifts in every seed | **Holds:** found in every seed, on shift 3, 4 or 5 (4 is typical) |
| Where no chain pays more than its parts, none is found in any seed | **Holds:** none found |
| Over shifts 4–8, she earns more with her last trick than without, paired by seed, by more than 2 standard errors | **Holds:** +0.23 panic a shift, SE 0.017 |

**What it can't show:**
- This is the world it was built for, so it only shows the learner can find a chain that's there.
  Whether real chains exist in the store, and pay, is the ablation's question.
- It flattens her two-step choice (an action, then its trick) into one, so it tests the learner,
  not the actions' scores.
- After 8 shifts a found chain reads +0.22, under the planted +0.30, because values drift back
  between shifts by design.
- An earlier threshold (0.05 more than the trick alone) gave false finds in a Python prototype of
  the same world: 0.25 a career on average. With 0.1 it gave none, so it's 0.1.

### Cut against the plain sprint

Scripted runners that keep going after she loses sight (`KarenActionsTests`):
- **On a 25 m dash, Cut never fires:** she's never a second ahead of you anywhere.
- **On 45–60 m runs (8 starts),** it's never worse than the sprint, and clearly closer in 1 of 8
  (3.4 m against 13.4 m). On average she ends 11.3 m from you, against the sprint's 12.5 m.
- **The first version did worse than the sprint** (11.0 m against 8.0 m): it picked spots on routes
  she doubted. Cut now waits until her best guess holds 30% of the weight.

### The ablation: what was fixed before it ran

**Primary metrics, fixed before any run** (the build plan's §5):
1. Seconds from first being visible to her moving at you, over the walk-by set (above, worked out
   from the tests; the ablation times it in play).
2. Metres between her and your real position 5 s after she loses sight in a hunt, paired by seed
   (`gap_after_loss_m`). Lower means she stays on you.
3. Fairness violations: must stay 0, under rules 1, 2 and 4–8.
4. For the combo learner: setpoint error against the same learner without her last trick, paired
   by seed.

**Secondary:** detections, first detection and catches; seconds outside her area (must be 0);
belief accuracy; Hunts and seconds sprinting; catches where you hadn't seen her in the 5 s before;
catches within 5 s of a throw; how often Cut's spot was right; sightings from beyond 18 m; and
how often the bots turned back and dodged.

**The runs** (one after another, about an hour, with the editor closed):

| Run | What | Shifts |
|---|---|---|
| 0.2.0 | rungs C and E, all four players, 3 careers, shifts 3–8 | 144 |
| `main` | the same seeds; only the measuring code added | 144 |
| 0.2.0, combo learner off | rung E only, `-karen-combo off` | 72 |

- **Players:** efficient, skittish and reckless; the cautious player is held out, reported on its
  own and never used to tune.
- **Compared** with `tools/eval/compare_builds.py`, pairing by rung, player, seed and shift.
- **A known difference:** on `main`, "outside her area" counts the pavement too, because `main` has
  no Pavement area.

### The ablation: results (6 November)

**How it ran:**

| Run | Code | Shifts | Real time |
|---|---|---|---|
| 0.2.0 | `a5d822e` (Block door live, from shift 7) | 144 | 22 min |
| `main`, first run | `origin/main`'s scripts, plus only the measuring code | 91 of 144: it stopped at its 150-minute limit | 150 min |
| `main`, the rest | the same clone: the reckless and cautious players only, same seeds | 72 | 53 min |
| 0.2.0, combo learner off | `a5d822e`, rung E, `-karen-combo off` | 72 | 12 min |

- **`main` is joined from its two runs:** efficient and skittish from the first, reckless and
  cautious (whole careers) from the second. That gives 143 pairs with 0.2.0, the held-out cautious
  player included.
- **`main` is slow.** Its bots live longer: its shifts run 292 s on average, against 0.2.0's 103 s.
  A shift runs on until the bot clocks out, is caught, or is 240 s past closing. It's also 2 to 4.6
  times slower per simulated second (0.16–0.32 s of real time, against 0.07), for a reason not yet
  found. Its log repeats a Unity warning, "Particle Velocity curves must all be in the same mode",
  49,034 times; 0.2.0's never does. Both step at the same fixed frame rate, so this changes how long
  a run takes, not what happens in it.
- **The same seed doesn't give the same shift.** The 18 reckless shifts that both `main` runs played
  all came out different. The first shift of each career matched to within a few thousandths of a
  second; later ones drifted apart, by whole catches. Tiny timing differences grow over a career.
  Pairing still controls the layout and the bot's dice, and the standard errors come from the paired
  differences, so they include this noise. But "paired" means "same start", not "same shift".

**The primary metrics:**

| # | Metric | Result |
|---|---|---|
| 1 | Seconds from first visible to moving at you | **Not timed in play.** `HuntMetrics` doesn't record it: a gap in the measuring code. Only the walk-by set's worked-out times stand (0.90 s at 10 m, dead centre) |
| 2 | Metres to you 5 s after she loses sight in a hunt | **Can't be answered: 0 pairs.** She lost sight of a bot during a hunt in 13 of 144 shifts on 0.2.0 (8.5 m on average) and in 5 of 144 on `main` (13.5 m), never in the same pair. She rarely loses you, because she catches you first |
| 3 | Fairness violations | **0** in all 379 shifts run |
| 4 | The combo learner's setpoint error, with minus without | −0.01 ± 0.01 (training players, 54 pairs); −0.02 ± 0.01 (held out, 18 pairs). **She found no combo in any shift**, so this isn't the combo room at work: no effect shown |

**0.2.0 against `main`,** `main` → 0.2.0, with the paired difference ± one standard error:

| | Rung C, training (53) | Rung E, training (54) | Rung C, held out (18) | Rung E, held out (18) |
|---|---|---|---|---|
| Catches per shift | 0.45 → 0.98 (+0.53 ± 0.07) | 0.54 → 1.00 (+0.46 ± 0.07) | 0.28 → 1.00 (+0.72 ± 0.11) | 0.50 → 0.94 (+0.44 ± 0.12) |
| First detection (s) | 31.9 → 8.8 (−23.2 ± 3.6) | 35.8 → 8.6 (−27.2 ± 4.5) | 38.3 → 9.1 (−29.2 ± 7.8) | 61.1 → 8.4 (−52.7 ± 12.1) |
| Detections | 7.6 → 5.7 (−1.9 ± 1.0) | 8.6 → 4.3 (−4.3 ± 0.6) | 7.3 → 3.6 (−3.7 ± 1.1) | 6.2 → 2.9 (−3.3 ± 1.0) |
| Hunts | 0.21 → 1.79 (+1.58 ± 0.26) | 0.37 → 1.50 (+1.13 ± 0.18) | 0 → 1.17 (+1.17 ± 0.35) | 0.17 → 1.22 (+1.06 ± 0.34) |
| Seconds sprinting | 0.30 → 1.29 (+0.99 ± 0.29) | 0.37 → 2.02 (+1.65 ± 0.37) | 0.05 → 1.28 (+1.23 ± 0.48) | 0.16 → 0.84 (+0.69 ± 0.35) |
| Seconds outside her area | 9.1 → 0 (−9.1 ± 2.6) | 7.3 → 0 (−7.3 ± 2.4) | 9.3 → 0 (−9.3 ± 3.7) | 0 → 0 |
| Sightings from beyond 18 m | 0 → 0.81 | 0 → 0.83 | 0 → 0.89 | 0 → 0.78 |
| Catches you never saw coming | 0.08 → 0.30 (+0.23 ± 0.07) | 0.22 → 0.31 (+0.09 ± 0.08) | 0.06 → 0.39 (+0.33 ± 0.14) | 0.11 → 0.28 (+0.17 ± 0.15) |
| Belief error out of view (m) | 21.2 → 10.9 (−10.3 ± 1.5) | 21.1 → 10.9 (−10.3 ± 1.2) | 24.4 → 11.4 (−13.0 ± 2.9) | 26.3 → 15.4 (−10.8 ± 3.9) |
| Setpoint error | 0.29 → 0.23 (−0.06 ± 0.02) | 0.29 → 0.20 (−0.09 ± 0.02) | 0.26 → 0.23 (−0.03 ± 0.03) | 0.29 → 0.20 (−0.09 ± 0.03) |

**By player** (rungs C and E together):

| Player | Pairs | Catches per shift | Shift length (s) | Finished the shift |
|---|---|---|---|---|
| efficient | 36 | 0.42 → 1.00 | 305 → 103 | 31% → 0% |
| skittish | 36 | 0.33 → 0.97 | 330 → 136 | 3% → 0% |
| reckless | 35 | 0.74 → 1.00 | 232 → 82 | 11% → 0% |
| cautious (held out) | 36 | 0.39 → 0.97 | 296 → 94 | 6% → 3% |

**What it shows:**
- **She's far deadlier,** for every player, the held-out one included. She catches the bot in
  94–100% of shifts, against 28–54% on `main`. A bot finished its shift in 1 of 144 shifts on
  0.2.0, against 18 of 144 on `main`. A 0.2.0 shift ends about 100 s in.
- **She sees you sooner:** about 9 s to first detection, against 32–61 s. This is the bigger cone
  and the expectation boost, as designed, but over whole shifts it adds up to a catch almost every
  time.
- **She stays inside:** 0 s outside her area, on every rung and player.
- **Catches you never saw coming rose with the catches, not as a share:** 32% of 0.2.0's catches,
  against 28% of `main`'s. More catches, not sneakier ones.
- **Her belief error out of view halves,** but it isn't like for like: her area is about half the
  size, and the outside is one cell.
- **The better setpoint error isn't shown to be better pacing.** Setpoint error rises with shift
  length (a correlation of 0.47 on 0.2.0, 0.12 on `main`), and 0.2.0's shifts are a third as long.
  Where both shifts of a pair lasted under 150 s, the difference is −0.02 ± 0.04 for the training
  players (16 pairs) and −0.10 ± 0.06 held out (8 pairs): within noise.
- **Cut never fired** in 144 shifts (once in the 72 with the combo learner off). Chases end in a
  catch before there's a route worth cutting.
- **Throw is rare:** 20 throws in 144 shifts, 19 of them on rung E, and 5 hits. The bots saw 7
  and dodged 2.
- **Her tricks:** 1.3 a shift on 0.2.0 (Bin tamper most, then Ambush, Blackout, the throws, and
  Block door 7 times on shifts 7–8), against 33 on `main`, whose most used was Follow (933 times
  in its first run's 91 shifts).
- **The bots react in play:** they turned back from her 183 times in 144 shifts. That was built
  in C5.1 and unit-tested; this is the first time it's seen in whole shifts.
- **The combo learner found nothing** in 144 rung-E shifts. With a catch about 100 s in and 1.3
  tricks a shift, two tricks rarely come within 20 s of each other.

**What it means for 0.2.0:** against these bots she's too strong. Almost every shift ends in a
catch within two minutes, so the Hunt's later parts (losing you, Cut, combos) hardly get a chance
to run, and primary metric 2 can't be measured. How far to turn her down, and with what (her
reach, her gain, the sprint, how soon a sighting becomes a hunt), is the owner's call; a playtest
(Y5.1) would show whether people fare better than the bots.

## Karen 0.3.0: the ablation (7 November)

0.3.0 rebuilds her tricks ([Karen.md §17](../karen/Karen.md#17-karen-030-the-tricks-come-back)). The plan
fixed four primary metrics before any run ([build plan §7](../archive/0.3.0-build-plan.md#7-tests-and-metrics)).

**How it ran:**

| Run | Code | Shifts | Real time | Used for |
|---|---|---|---|---|
| 0.3.0 | `5de838e` | 240 | 82 min | the results |
| `main` | `d5d14f3` (`origin/main`) | 240 | 21 min | the results |
| 0.3.0, second run | `5de838e` plus the bot's Tool theft fix and the stuck-shift record, every action logged | 83 of 240: it stopped when the session ended | 40 min | finding why shifts got stuck |
| 0.3.0, replay | the same, plus the fall detector and the fog fix (below) | 40: the efficient player, careers 1–2 | 27 min | the same |

- The same arguments for both builds:
  - rungs C and E;
  - four players, the cautious one held out;
  - 3 careers of shifts 1–10;
  - 150 s shifts;
  - seed 1000.
- Pairs are by rung, player, seed and shift (`compare_builds.py`). 0.3.0's own numbers come from
  `tricks030.py`.
- **Only training players' shifts were used to find causes.** The two diagnosis runs played only
  the training players' stuck shifts, so nothing was tuned on the cautious player.
- **The same seed still doesn't give the same shift.** The replay matched the second run for 2
  shifts, then drifted (130 s against 71 s on shift 3). Seeded shifts can be paired, but not
  replayed one for one.

**The primary metrics:**

| # | Metric | Result |
|---|---|---|
| 1 | Every live trick works end to end | **Pass in the trick check:** every forced trick started and landed. The test player fixed every chore but two: Spill and Shelf sweep landed, but the shift ended before it got to them, in some runs (both were fixed in others). **Tool theft didn't land in the first check of 8 November** (she stopped 2.2 m from the rack, out of reach), and is fixed. **In the ablation, three never ran:** the understudy (0 of 72 shifts where it was unlocked), the overtime (0 of 24) and Cut (0 of 240) |
| 2 | Fairness violations: 0 | **Pass:** 0 in all 240 + 240 shifts, and in the diagnosis runs |
| 3 | No stuck shifts | **Fail:** 13 of 240 on 0.3.0, against 2 of 240 on `main`. Causes below |
| 4 | Setpoint error against `main` | **Slightly worse:** +0.01 ± 0.01 (rung C, training), +0.02 ± 0.01 (E, training), +0.01 ± 0.02 (C, held out), +0.04 ± 0.02 (E, held out) |

**0.3.0 against `main`,** `main` → 0.3.0, with the paired difference ± one standard error:

| | Rung C, training (90) | Rung E, training (90) | Rung C, held out (30) | Rung E, held out (30) |
|---|---|---|---|---|
| Catches per shift | 0.99 → 0.97 (−0.02 ± 0.02) | 0.99 → 0.96 (−0.03 ± 0.02) | 0.97 → 0.93 (−0.03 ± 0.06) | 0.97 → 0.87 (−0.10 ± 0.07) |
| Detections | 4.3 → 3.6 (−0.6 ± 0.4) | 4.9 → 5.3 (+0.4 ± 0.6) | 3.2 → 3.9 (+0.7 ± 0.7) | 5.3 → 5.3 (0.0 ± 1.0) |
| Hunts | 1.54 → 0.98 (−0.57 ± 0.16) | 1.42 → 1.50 (+0.08 ± 0.17) | 1.33 → 1.00 (−0.33 ± 0.28) | 2.03 → 1.37 (−0.67 ± 0.39) |
| Seconds hunting | 2.0 → 1.0 (−1.0 ± 0.3) | 2.2 → 1.8 (−0.4 ± 0.4) | 1.2 → 0.4 (−0.8 ± 0.4) | 1.9 → 0.8 (−1.2 ± 0.5) |
| Catches you never saw coming | 0.39 → 0.38 | 0.34 → 0.36 | 0.37 → 0.33 | 0.50 → 0.33 (−0.17 ± 0.13) |
| Throws | 0 → 0.01 | 0.34 → 0.24 (−0.10 ± 0.07) | 0.07 → 0 | 0.20 → 0.10 |
| Belief error out of view (m) | 12.1 → 12.1 | 13.1 → 14.7 (+1.6 ± 1.6) | 14.9 → 16.0 (+1.1 ± 3.4) | 19.1 → 22.2 (+3.1 ± 3.7) |
| Bot turned back from her | 1.07 → 1.04 | 1.10 → 1.64 (+0.54 ± 0.31) | 0.47 → 0.40 | 0.60 → 0.90 (+0.30 ± 0.28) |
| Setpoint error | 0.24 → 0.25 | 0.23 → 0.25 | 0.23 → 0.24 | 0.22 → 0.26 |

| | `main` | 0.3.0 |
|---|---|---|
| Tricks per shift (started, not landed: see the check after the second run), training players | 1.1 | 3.6 |
| Tricks per shift, held out | 1.2 | 5.3 |
| Shift length, training players | 99 s | 119 s |
| Shift length, held out | 110 s | 150 s |
| Shifts that ran past closing | 63 | 80 |
| …that got stuck | 2 | 13 |
| …that the bot clocked out of | 2 | 0 |

**Her tricks in play** (0.3.0, both rungs; `poll_witnesses`, her own move, about 2.3 a shift):

| Trick | Training: uses a shift | Shifts it ran in | Held out: uses a shift | Shifts it ran in |
|---|---|---|---|---|
| Spill | 0.88 | 101 of 180 | 1.12 | 41 of 60 |
| Shelf sweep | 0.63 | 83 | 0.97 | 35 |
| Phantom chime | 0.57 | 50 | 0.95 | 23 |
| Ambush | 0.39 | 54 | 0.62 | 23 |
| Bin tamper | 0.27 | 40 | 0.45 | 17 |
| Blackout | 0.23 | 38 | 0.35 | 17 |
| Fog | 0.23 | 36 | 0.42 | 16 |
| Tool theft | 0.17 | 27 | 0.28 | 14 |
| Camera bolt-on | 0.08 | 14 | 0.10 | 6 |
| Throw (oil, flour, tin) | 0.13 | 24 | 0.07 | 4 |
| Block door | 0.02 | 4 | 0.03 | 2 |
| Cut, the understudy, the overtime | 0 | 0 | 0 | 0 |

**0.3.0's own measurements** (the plan's secondary list):

| Measurement | Result |
|---|---|
| Catches while a room was fogged | 19 of 173 catches (0.11 a shift), training players |
| Catches while Ambush waited by a chore | 0 |
| Seconds her cameras saw you | About 0: her cameras went up in 20 shifts and saw a bot in 2 of them, for 7 s in all |
| Seconds to answer each chore | Fog: 75 s ± 14 (17 times, training players). The others never came up: the clock was never stopped, Kenji never came, no box was moved, and the bot never had to fetch the WC mop |
| Does placement memory improve over a career? | **Not measurable here:** what her places paid was 0.05–0.37 on shifts 1–3 and 0.18–0.38 on shifts 8–10, over 1–12 shifts each. Fewer tricks are unlocked early, so the two aren't the same mix |

**Why shifts got stuck.** Five stuck shifts of training players were recorded in the two
diagnosis runs, with what was still open and what she did after the doors closed:

| Cause | Stuck shifts | What the record shows |
|---|---|---|
| **She keeps making chores after the doors close** | 3 (the efficient player, seed 1000, shifts 8–10, both rungs) | 14–15 tricks in the 240 s after closing, 6–7 of them new chores that stop the clock-out (spills, shelf sweeps, bins, Tool theft). Still open at the end: 2–6 bays, 2–3 spills, a bin or a bag. The bot kept working and she kept ahead of it |
| **The bot fell through the floor in Aisle 4/B** | 1 (efficient, seed 1100, shift 3, rung C) | Its height went from 1.08 m to −52 m between two samples 3 s apart, at about (62–66, −141). After that, every action failed with "no path". The first run's same shift (rungs C and E) shows the same pattern: about 390 actions in 390 s |
| **The bot wedged itself on the staff-room furniture** | 1 (efficient, seed 1000, shift 5, rung E) | Blocked 42 times by a dining chair's seat and 36 by the coffee table, where the NavMesh says there's room. She also made 5 chores after closing |

- **`main` has the first cause, milder:** its 2 stuck shifts had Bin tamper 5 and 6 times. 0.3.0
  has five tricks that make such chores, against `main`'s one.
- **The first run didn't record what was open.** 7 of its 13 stuck shifts had Tool theft in them,
  and the bot then couldn't pick a tool up with full hands. That is fixed in the bot
  (`KehaiEnv.FindTool`), but its effect isn't measured: in the diagnosis runs the bot never once
  had to fetch the mop, though Tool theft started in 11 of the second run's shifts. A use counts
  a plan started, not a mop hidden.

**Two more findings:**
- **The fog flooded the log.** Each fog cloud set its rising speed as a range and its other two
  directions as plain numbers. Unity wants all three alike, so it logged "Particle Velocity curves
  must all be in the same mode" every frame: 16.6 million lines in the first run, against 20,000
  for `main`'s whole log. Fixed in `KarenProps.cs` (the replay logged none). It's the same warning
  0.2.0's results found 49,034 times on the `main` of then.
- **0.3.0 runs 3.6 times slower than `main`:** 0.15 s of real time per simulated second, against
  0.04. It's slower from shift 1, when few tricks are unlocked (0.13 against 0.04), and the
  replay was as slow without the warnings. So the cost is in something that always runs, and it
  isn't found yet. A suspect: Shelf sweep checks about 200 bays at each decision.

**What it means for 0.3.0:**
- Her tricks run in play (3.6 a shift against 1.1), all live tricks work when forced, and she
  stays fair.
- She's no stronger for it: catches are slightly down (0.02–0.10 a shift) and she hunts less.
- **The shift has to stay finishable.** As built, she can add chores after closing faster than
  anyone can clear them, and the clock-out waits for every chore. A person would be stuck the
  same way. **The owner chose the fix (8 November):** no new chore after the doors close; she
  can still hunt. That's rule 10 ([Karen.md §17.3](../karen/Karen.md#173-the-new-fairness-rules)), and the
  ablation runs again to check it.
- **Separately:** a hole in Aisle 4/B's floor (or something that pushes a body through it), the
  slowdown, and the understudy and the overtime, which the learner never picked.

### The second run (8 November): rule 10, every trick from shift 1

**What changed** (`a25d8a5`), on the owner's calls of 8 November:
- **Rule 10:** no new chore once the doors close ([Karen.md §17.3](../karen/Karen.md#173-the-new-fairness-rules)).
- **Every trick from shift 1:** the career table is off for now.
- **Tool theft fixed:** she stopped 2.2 m from the rack, out of reach.

The same seeds and arguments as the first run, paired with the same `main` run. 240 shifts in
56 minutes. One shift was numbered 11 instead of 6 (rung C, efficient, seed 1200), so it has no
pair: 239 pairs.

**The primary metrics:**

| # | Metric | First run | Second run |
|---|---|---|---|
| 2 | Fairness violations | 0 | **0** |
| 3 | Stuck shifts (`main`: 2) | 13 | **9:** rung C 3 + 2 held out, rung E 3 + 1 held out. Still a fail |
| 4 | Setpoint error against `main` | 0.01–0.04 worse | +0.03 ± 0.01 (C, training), −0.01 ± 0.02 (C, held out), +0.02 ± 0.01 (E, training), +0.02 ± 0.02 (E, held out) |

**Rule 10 holds:** 0 tricks that leave a chore after closing, in the 85 shifts that ran past
closing. After closing she only waited in ambush (Ambush) or threw (Throw).

**Why 9 shifts still got stuck:** the backlog at closing.
- With every trick open from shift 1, the bot reached closing with 1–9 bays to restock and 1–6
  spills.
- It failed many of its restock and mop attempts: in one shift, restock 14 times, serve 18 and
  mop 9.
- The 240 s the test allows after closing wasn't enough to clear it.
- This record doesn't say why the attempts failed; the next one does (`failed_why`).
- In one shift a single bay was left: the bot failed to restock 8 times and to clock out 4.

**0.3.0 against `main`** (paired, as above):
- **Catches:** about the same, 0.93–0.97 a shift against 0.97–0.99.
- **Hunts:** fewer (−0.23 to −0.90 a shift), and fewer seconds hunting (about half, for the training players).
- **Detections:** up on rung C (+1.37 ± 0.59 training).
- **Belief error out of view:** 1–4 m worse, within about one standard error.

**Her tricks:**
- 3.4 a shift for the training players and 4.5 held out, against 1.1 and 1.2 on `main`.
- Spill ran in 144 of 180 training shifts and Shelf sweep in 107. Then came Ambush (0.39 a shift),
  Fog (0.36) and Phantom chime (0.31).
- 19% of catches came while a room was fogged (33 of 173). The fog was reset 37 times, 83 s after
  it came, on average.

**Two more bugs it found:**
- **The understudy and the overtime could never run in play.** "Once a shift" was written as a
  99,999 s cooldown, and a trick's last use starts at −9,999 s. Both stayed on cooldown for the
  first 25 hours of play. The trick check forces tricks past cooldowns, so it never noticed.
  Fixed: no cooldown, since the once-a-shift count already stops a second use. A new test checks
  that no live trick starts a shift on cooldown.
- **Tool theft started in 25 shifts, but the bot never once had to fetch the mop.** The count was
  of plans started, not landed. Landings are now recorded (`landed_<trick>`).

**Speed:** 0.099 s of real time per simulated second, against 0.152 in the first run (the fog
fix) and 0.044 for `main`. One cost was the shelf-bay lookup: Shelf sweep found each bay's stand
point by name, walking the scene's whole name registry, for about 200 bays several times a
decision. It's now worked out once per bay, which made the check below 14% faster (0.079 against
0.092 for the same player). The rest of the slowdown isn't found.

### A check after the second run (8 November): 60 shifts, the skittish player

With the cooldown fix and the cached stand points (rungs C and E, 3 careers, the same seeds):
- **The understudy and the overtime run now.** Kenji came in 5 shifts, and was sent away 115 s
  after he came, on average. The overtime started 5 times and stopped the clock once; the clock
  was restarted 246 s later.
- **Stuck shifts: 7 of 60,** against 4 for this player in the second run. Catches were 0.88 a
  shift, against 0.93. Their failure reasons, now recorded (the top 8 a shift), are the test
  player's own:
  - "couldn't get close enough" to a bay, the tills or a spill: 69 times;
  - interrupted by its fear: 60;
  - walks that timed out: 9.
- **Most of her hands-on tricks never land in play.** Started against landed (a trick lands at its
  first effect):

| Trick | Started | Landed |
|---|---|---|
| Spill | 55 | 1 |
| Shelf sweep | 51 | 9 |
| Bin tamper | 13 | 0 |
| Tool theft | 10 | 0 |
| Camera bolt-on | 12 | 0 |
| Block door | 7 | 0 |
| The overtime (walks to the clock) | 5 | 1 |
| Fog | 23 | 20 |
| Phantom chime | 30 | 30 |
| Blackout | 10 | 10 |
| The understudy | 5 | 5 |
| Throw (oil, flour, tin) | 9 | 5 |

- **Why:** a hands-on trick walks 10–30 m to its spot first. Karen decides again twice a second,
  and a loud noise forces a fresh decision. When the new decision is a different action, the walk
  is dropped and the trick never happens. The building's tricks land within about a second, so
  they always do.
- **Why the trick check didn't show it:** a forced trick runs to its end.
- **So every count of tricks above is of tricks started.** The chores left at closing were mostly
  the shoppers': they spill and take stock too.
- **The fix, if the owner wants it:** once she's on her way to a hands-on trick, she finishes it,
  unless she starts a hunt or the trick can't be done any more.

### After "finish what she starts" (8 November): the same 60 shifts

The owner chose the fix ([Karen.md §17.3](../karen/Karen.md#173-the-new-fairness-rules)). The same check
again: the skittish player, rungs C and E, 3 careers, the same seeds.

| | Before | After |
|---|---|---|
| Spill, landed of started | 1 of 55 | **39 of 42** |
| Shelf sweep | 9 of 51 | **48 of 48** |
| Bin tamper | 0 of 13 | **7 of 7** |
| Camera bolt-on | 0 of 12 | **13 of 16** |
| The overtime | 1 of 5 | **4 of 4** |
| Tool theft | 0 of 10 | 1 of 3 |
| Block door | 0 of 7 | 0 of 2 |
| Fog, Phantom chime, Blackout, the understudy | 65 of 68 | 114 of 120 |
| Stuck shifts | 7 | **3** |
| Catches a shift | 0.88 | 0.95 |
| Tricks started a shift | 4.4 | 5.4 |
| Shift length | 159 s | 199 s |
| Setpoint error | 0.264 | 0.263 |
| Fairness violations, chores after closing | 0, 0 | 0, 0 |

- **Her chores get answered now.**
  - Kenji was sent away 4 times, 34 s after he came, on average.
  - The stopped clock was restarted 4 times, after 108 s.
  - The fog was reset 18 times, after 82 s.
- **The 3 stuck shifts are the test player's.**
  - In two of them it "couldn't get close enough" to mop a spill in the office (Office/G), 5 and 7
    times. The trick check's spill at Office/G also went unmopped twice, so a spot in the office
    may be out of a player's reach, not just the bot's. To check in the playtest.
  - The other was a bay it couldn't get close enough to.
- **Too few to judge:** Tool theft and Block door (5 starts between them). The trick check lands
  both.
- **The trick check after it:** all 12 tricks landed, 11 chores were fixed (Bin tamper's shift
  ended first), and 0 fairness violations.
- **Speed:** 0.083 s of real time per simulated second, against 0.079 before. She does more now.

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
