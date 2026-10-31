# Plan: Karen learns what scares you *and* what catches you, and gets three fair new ways to kill

> **Status:** approved 2026-10-21, not started. It starts after the learned motion model (PR #68) is merged.

## Context

The 2026-10-16 ladder (240 shifts, `docs/KAREN_RESULTS.md`) showed:
- the belief grid + planner (rung C) is the big win over patrols;
- learning (bandit D, Ledger E) doesn't beat C: panic falls a little (−0.04), setpoint error doesn't move;
- she kills in ~45% of shifts, mostly with no chase at all.

You asked for Karen to be more lethal, more ML-driven and generally better. This plan does three
things, measured on the ablation ladder, as **one PR**:

1. **A better learner.** It learns what each tactic does to panic *and* to your chance of dying,
   and picks for pacing in calm phases and for the kill in tense ones.
2. **Three new ways to kill**, all within the fairness rules: cut you off, wait on your path,
   deadly step-out.
3. **Measurement that can tell**: kill causes logged, 8× bigger runs, results decided by rules
   written down before the test run.

## Decisions made with you

| question | your answer |
|---|---|
| what the learner optimises | effect model + setpoint, **plus capture (kills) as a second goal** |
| when the kill goal counts | only in tense phases: Build, Spike, Crunch; more when the Director wants pressure |
| new kill moves | cut you off · wait on your path · deadly step-out (not the lunge from behind) |
| how lethal | Director-paced: new lethal moves never in Settle or Recover, and they cost tension |
| speed | she is already faster: hurry 4.2 vs your walk 4.0, run 6.8 vs your sprint 6.5 m/s (1.05×); she can run a whole chase, you can't sprint on low energy. A 1.05 / 1.10 / 1.15 sweep is reported; the default stays 1.05 unless you change it |
| order | after the learned motion PR (`feat/learned-motion`) is merged; this branches from main then |

## What the code says (this shapes the plan)

| finding | where | consequence |
|---|---|---|
| A tactic's reward is mean panic over the 20 s after it, minus panic at its start | `KarenBrain.SettleRewards` | Panic drains after a scare (25 s decay) and rises in spikes whatever she does, so the reward mostly says *when* a tactic was used. |
| Tactic choice inside a goal maximises Q̂ (raw Δpanic); goal choice uses Q̂ × pressure | `KarenLedger.BanditScore`, `GoalSet.Score` | The two layers want different things. |
| ~30 tactics a shift across ~30 arms | ladder data | About **1 reward per tactic per shift**: rung D can't learn much in one shift. Pooling across tactics and an offline prior fix this. |
| 2–3 tactics share each 20 s window | ladder data (a tactic every ~9 s) | Credit is mixed; the new model adds "other tactics in the window" as an input. |
| PPO rung F is paid −\|panic − setpoint\| | `PolicyReward.Pacing` | The new learner uses the same pacing goal, so D/E/F become comparable. |
| 15 of 27 kills at C, 10/20 at D, 14/21 at E came in shifts with **no chase** | `ablation_20261016_reactive_ladder.jsonl` | Most kills aren't from chases. The cause isn't logged anywhere: step 1 fixes that. |
| A chase leads 0.5 s of your last velocity, falls back to the belief peak, and quits after 5 s out of sight; every chase then starts a 45 s recovery | `Pursue` (`Primitives.cs`), `KarenBrain.SetChasing` | Room for "cut you off" and a short search of the likely exit. |
| Following, she stops 2.2–2.5 m short of you and can never touch you | `FollowTactic`, `KarenBody.KeepPersonalSpace` | `follow` is half her tactic use but kills nobody. The kill head will learn that; no hand change to follow. |
| After a vanish she waits 8–16 m away, along your last velocity (30% behind you) | `BlinkDash.FindHidingSpot` | Already "ahead", but not along your *route*. ~45% of vanishes end in a reveal (9/17 at C). |
| The step-out sting has 0 s lead, the burst ends ≥ 2.5 m short, then recovery | `BlinkDash.Reveal` | A lethal step-out needs a proper tell: the step-out itself, then ≥ 0.8 s. |
| Min-cut exists on the region graph | `StoreMap.MinCut`, `Herding.Cut` | It picks *things to block*. One body can't block mid-chase, so "cut you off" races you to a chokepoint instead. Min-cut stays with herding. |
| The Director nudges belief toward your **true** region after 45 s without a fix | `KarenDirector.MaybeBias` | New moves read the belief, so some kills could come from leaked truth. One test arm turns the hint off to show how much. |
| A shift takes ~13 s of wall time headless | ladder `wall_seconds` | Big runs are cheap: 400 shifts an arm is ~2.5 h with process start-ups. |
| No way to change a `KarenConfig` field from the command line | `AblationRunner` | Add `-karen-set field=value,...` so each change is an arm, not a new rung. |

## 1. Measurement first (instrumentation)

- **Kill cause**, written per kill to the episode jsonl (`EpisodeMetrics`):
  - what was running: chase, step-out, follow, other plan (by id), lurking, no plan;
  - did she see you in the last 3 s; were you moving toward her (you walked into her);
  - was there a tell in the last 10 s (her plan's, a chase screech, the step-out);
  - time to kill.
- **Chase outcomes**, per chase: start distance, closest distance, how it ended (caught, lost you, timed out), and whether the cut-off target was used.
- **Vanish outcomes**, per vanish: revealed or not, reveal distance, killed after the step-out.
- **Rates that respect "a kill ends the shift"**: kills per 100 s at risk, and time to the first kill (Kaplan–Meier), besides kills per shift.
- **Decision log** (eval only, `-bandit-log <dir>`, gzipped jsonl): one row per learnable decision with the inputs, the legal candidates, the pick, its probability (propensity), and what followed: Δpanic at 10 and 20 s, kill within 30 s, chase started.
- `-karen-set field=value,...` sets any `KarenConfig` field (reflection), so every change below can be switched on per arm.

## 2. The learner: an effect model with two heads

Replaces the per-tactic UCB on rungs D and E. Today's UCB stays behind `banditModel = ucb` for the comparison.

**What it predicts**, for tactic *a* in situation *x*:

| head | predicts | model |
|---|---|---|
| fear | Δpanic over the window after *a* | Bayesian linear regression. A **shared part** learns the drift every tactic sees (panic falling after a spike, rising in one); a **per-tactic part** learns what *a* adds on top. This is LinUCB's "hybrid" model (Li et al. 2010). |
| kill | chance of a kill within 30 s of *a* | Bayesian logistic regression (Laplace approximation, Chapelle & Li 2011) |

- **Inputs** (only things Karen may know; the source-scan test covers `Decision/`):
  - Director: phase, panic, setpoint error, pressure, tension;
  - belief: confidence, entropy, staleness, whether she sees you now, her distance to the belief peak;
  - your energy (her belief of it), task load;
  - the tactic: seconds since she last used it (habituation is learnt, not hand-set), uses this shift, tier, which sense it attacks, cost;
  - learnable tactics already started in the window.
- **Window** for the fear head: 10 or 20 s, chosen on validation data.
- **Prior from the training bots** (empirical Bayes). Fitted offline on logs from efficient, skittish and reckless (never cautious), shipped as generated C# (`BanditPrior.g.cs`, with a model card) like the motion weights. Tactics with no data keep their authored `PanicPrior` with a wide variance. "Designed early, learned late" still holds: the prior is where it starts, and evidence moves it.
- **Learning per player.**
  - D: starts each shift from the prior and updates after each window.
  - E: keeps the posterior between shifts, with the same fading as today (precision × 0.8 per shift, mean 15% back toward the prior), so rule 7 holds.
- **How she picks** (Thompson sampling: draw the weights from the posterior, then score each legal candidate):

  ```
  value(a) = −| setpoint − (panic + Δ̂panic(a)) |  +  w_kill · P̂kill(a)
  w_kill   = 0 in Settle and Recover;  k_kill · (0.5 + max(0, pressure)) in Build, Spike, Crunch
  ```

  - `k_kill` is tuned on dev seeds.
  - The propensity of each pick is estimated by 50 extra posterior draws and logged.
- **Goals use the same score.** In `GoalSet.Score`, `wStress · Q̂ · pressure` becomes `wStress · best value(a)` among the goal's legal tactics. Movement goals (patrol, sweep, investigate) score from the shared drift alone.
- **The Director's shield is unchanged**: tiers, cooldowns, tension. The learner only ranks what the Director allows, so a learnt taste for kills can't start a chase in Recover.
- **Rung F's reward** gets the same kill term (`PolicyReward`), so PPO and the bandit optimise one objective. Training F isn't in this PR.
- **The thought log** shows, for example: `LEARN spill Δpanic +0.06 (predicted +0.04) · kill chance 3% → 2%`.
- **Your question, "which tactics are best for capture?"** becomes a results table: kills within 30 s per 100 uses, per tactic, adjusted for the situation (inverse-propensity weighting on the test arms), with intervals.

## 3. Three new ways to kill

All three read a **belief forecast**:
- `BeliefGrid.Forecast(seconds)` runs the active predict step forward on a copy, with no evidence;
- after the learned motion PR, that is M1 (which avoids Karen) or a learned model if you switch it on;
- the live belief is never changed;
- in a chase, the first second is bent along your last seen velocity.

| move | today | new | fairness |
|---|---|---|---|
| **Cut you off** (`Pursue`) | runs at your last position + 0.5 s of velocity; quits 5 s after losing you | forecast 1–4 s ahead, then run to the most likely cell she can reach **first** (her time ≤ yours + 0.3 s), with a bonus for doorways and chokepoints; falls back to today's lead. After losing you: if ≥ 0.4 of the forecast leaves by one exit, she checks it for up to 8 s in all | sight, belief and map only; the chase keeps its screech tell, tier 4 and the Director's gate |
| **Wait on your path** (`FindHidingSpot`) | hidden spot 8–16 m away along your last velocity, or behind you 30% of the time | hidden spots scored by the forecast's chance that you pass within 6 m in the next 25 s; the 30% "behind you" branch stays | unchanged: only after a told wave, only while she sees you, hidden from you |
| **Deadly step-out** (`Reveal`) | sting, a short burst that stops ≥ 2.5 m short, then recovery | stepping out within 6 m is the tell (sting, and you can see her). After ≥ 0.8 s she lunges up to 5 m toward where she sees you; touch kills. If you've backed off or broken her line of sight, she stops. Recovery starts after it, hit or miss | lethal only in Build, Spike or Crunch and if tension covers its cost (else today's harmless step-out). `FairnessGuard.CheckToldEffect` checks the 0.8 s lead |

Director-paced, as agreed:
- chase, dash and step-out never happen in Settle or Recover;
- the step-out costs tension;
- a new fairness check counts lethal moves in calm phases. It must stay at 0.

## 4. How each change is judged

**Seeds and players.** Learned motion uses seeds 1000–1200 (test), 2000–2100 (val) and 3000–3500 (train). This PR uses new seeds:

| split | players | seeds | used for |
|---|---|---|---|
| prior data | efficient, skittish, reckless | 30 careers from 4000 | fitting the prior, with an exploring logger (30% random legal pick, logged) |
| dev | same three | 10 careers from 5000 | tuning `k_kill`, the window, forecast horizons, step-out reach; the speed sweep |
| **test** (run once) | all four; **cautious held out** of everything above | 25 careers from 7000 | the results |

**Test arms**, paired by seed, shift and player (400 shifts each = 4 players × 25 careers × 4 shifts):

| arm | what | answers |
|---|---|---|
| 1 | C as on main | baseline |
| 2 | C + the three kill moves | lethality, without learning |
| 3 | D with today's UCB | the old learner |
| 4 | D with the effect model | does the new learner beat the old? |
| 5 | E with the effect model | does memory across shifts add anything? |
| 6 | E + effect model + kill moves | the new Karen |
| 7 | arm 6 with the Director's hint off | how much depends on the truth hint |

**Primary results, written into the docs before the test run:**
- **Kill moves (2 vs 1):** kills per 100 s at risk in tense phases go up; lethal moves in calm phases = 0; tell violations = 0.
- **Learner (4 vs 3, 4 vs 1):** setpoint error goes down, *or* tense-phase kill rate goes up, with the other no worse than a set margin (error +0.01, kills −0.05 per shift).
- **Memory (5 vs 4):** same two measures.
- **Stats:** 95% bootstrap intervals, resampled by career; Holm correction across the primaries.

**Power.** From the 2026-10-16 pairs (SD of a paired difference: catches 0.62, setpoint error 0.079, panic 0.119):

| pairs | smallest difference it can see (80% power, 5%): catches · setpoint error · panic |
|---|---|
| 48 (today) | 0.25 · 0.032 · 0.048 |
| 400 (this plan) | 0.09 · 0.011 · 0.017 |

**Also reported:**
- detections and first detection;
- kills per chase, and kills by cause (told vs untold);
- reveals per vanish, and step-out kills;
- follow's share and tactic entropy;
- tailoring (Jensen–Shannon);
- each player profile, with cautious marked held out;
- the E trend across a career;
- the per-tactic kill table;
- hint mass per shift.

**Honest limits, stated up front:**
- the players are bots, so kills learnt on bots may not transfer to people;
- kills are rare (~0.5 a shift), so the kill head will mostly be its prior, and per-player kill learning will be weak;
- more kills means shorter shifts and fewer tactics, which is why rates are per time at risk;
- a negative result is reported as it comes.

## Order of work and cost

Starts once `feat/learned-motion` is merged. One branch in a worktree (`feat/karen-capture`), one PR.

| # | step | my time | machine time |
|---|---|---|---|
| 1 | kill causes, chase and vanish outcomes, time-at-risk rates, decision log, `-karen-set`, analyzer sections | 1 day | — |
| 2 | `Forecast`, cut you off, exit search | 1 day | — |
| 3 | wait on your path, deadly step-out, the calm-phase fairness check | 1 day | — |
| 4 | effect model in C# (two heads, posterior, Thompson, propensities, goals, Ledger persistence, F's reward), tests | 2 days | — |
| 5 | collect prior data (360 shifts) | — | ~2 h |
| 6 | `tools/bandit/`: fit the prior, export C#, **parity gate** (C# predictions = Python to 1e-5) | 1 day | minutes |
| 7 | dev runs: tune, speed sweep | ½ day | ~1 night |
| 8 | test run: 7 arms × 400 shifts | — | ~2 nights |
| 9 | results and docs | 1 day | — |

About **7½ days of work and 3 nights of runs**. Unity runs one process at a time (8 GB). No batch Unity while your editor is open without asking first.

## Files

New:
- `AI/Scripts/Decision/EffectModel.cs`: the two heads, posterior updates, Thompson, propensities.
- `AI/Scripts/Decision/EffectFeatures.cs`: the inputs.
- `AI/Scripts/Decision/BanditPrior.g.cs`: generated, with a model card.
- `Eval/Scripts/DecisionLog.cs`: the decision log.
- `tools/bandit/`: `fit_prior.py`, `export_cs.py`, `uplift.py` (the per-tactic kill table), `tests/`, `README.md`. It shares `tools/rl/.venv`.

Changed:
- `Core/KarenBrain.cs`:
  - `SettleRewards` feeds the effect model;
  - `Kill` records the cause;
  - decisions are logged.
- `Core/KarenLedger.cs`: stores the posterior. `BanditScore` stays for `banditModel = ucb`.
- `Decision/Goals.cs`: the stress term becomes the shared score.
- `Decision/Primitives.cs`: `Pursue` cuts you off and checks the likely exit.
- `Decision/BlinkDash.cs`: `FindHidingSpot` reads the forecast; `Reveal` gets the lethal step-out.
- `Belief/BeliefGrid.cs`: `Forecast`.
- `Core/KarenDirector.cs`: `KillWeight(phase, pressure)`.
- `Core/KarenConfig.cs`: `banditModel`, `killWeight`, `effectWindow`, `interceptHorizon`, `chaseSearchSeconds`, `stepOutReach`, `stepOutCost`, `lethalMoves`.
- `Core/FairnessGuard.cs`: the step-out lead and the calm-phase lethal check.
- `Decision/Policy/TacticPolicy.cs`: the kill term in F's reward.
- `Eval/Scripts/EpisodeMetrics.cs`, `AblationRunner.cs` (`-karen-set`, `-bandit-log`, `-bandit-explore`).
- `tools/eval/analyze_ablation.py`: arms, kill causes, time-at-risk rates, bootstrap by career, Holm.
- `_Tests/Editor/KarenTests.cs`.
- Docs:
  - `KAREN_RESULTS.md`: a new section;
  - `Karen.md` §7.2, §8.6 and §15;
  - `tools/eval/README.md`: the flags.

## Verification

- **EditMode tests:**
  - the posterior update matches the closed form;
  - Thompson only picks legal candidates;
  - `w_kill` is 0 in Settle and Recover;
  - `banditModel = ucb` reproduces today's picks exactly;
  - the posterior fades per shift;
  - the step-out lunge never starts within 0.8 s of the step-out;
  - `Forecast` keeps the total at 1 and leaves the live belief alone;
  - the cut-off and the hiding spot never read the player (the existing source scan covers `Decision/` and `Belief/`).
- **Python tests** for the fit and the export, plus the C# parity gate.
- **Smoke run:** one career with every flag on. `analyze_ablation.py` prints the new tables, with 0 fairness violations.
- **Cost:** milliseconds per decision and per chase tick in the editor. Karen's budget is under 0.7 ms a frame; the forecast is sliced if it's over.
- **In the editor:** a couple of shifts (with you) to see the cut-off, the waiting spot and the step-out, and to read the new LEARN lines.

## Not in this PR

- the lunge from behind (not chosen);
- training rung F (~10 Mac nights; it comes after this, now that F has the same reward);
- a learned Panic Index;
- real-player results.
