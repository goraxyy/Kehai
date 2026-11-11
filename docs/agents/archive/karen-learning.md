# Plan: Karen learns from everyone, and adapts to you

> **Replaced by 0.4.0 (10 November 2026).** The learner in this plan isn't built. The
> [0.4.0 plan](0.4.0-build-plan.md) replaces it with two trained Karens. It takes in this plan's
> blend of everyone and you, its learning records, its splits by player and its model cards
> ([its §4.3](0.4.0-build-plan.md#43-against-pr-76-pros-and-cons)). [Playtest v2](playtest-v2.md)
> and the two-Karens idea still stand. This file is kept for the record.

> **Status:** a draft for your approval (6 November 2026). Nothing is built. This is Karen ML, so
> once approved it's built as one PR. It would replace the [capture plan](karen-capture.md)
> (approved 21 October, not started), keeping its learner. The idea of two Karens with different
> rewards stays in [IDEAS.md §7](../project/IDEAS.md#7-two-versions-of-karen). It needs real
> players' data, which [playtest v2](playtest-v2.md) brings.

1. [What you asked for](#1-what-you-asked-for)
2. [What she has today](#2-what-she-has-today)
3. [The design: three layers](#3-the-design-three-layers)
4. [Choices for you](#4-choices-for-you)
5. [The data](#5-the-data)
6. [Training](#6-training)
7. [Fairness](#7-fairness)
8. [How it's judged](#8-how-its-judged)
9. [The build](#9-the-build)
10. [Honest limits](#10-honest-limits)
11. [Not in this plan](#11-not-in-this-plan)
12. [Terms](#12-terms)

---

## 1. What you asked for

On 6 November:
- **Learn from players:** everyone's shifts make her generally smarter.
- **Adapt to each player within sessions:** she learns *you* while you play.

## 2. What she has today

| Part | What it learns | From | Kept | State |
|---|---|---|---|---|
| The trick picker (rung D) | which tricks raise *your* panic | your current shift | the shift, then it fades | built. Every trick starts from a hand-set guess (`Tactic.PanicPrior`) |
| The Ledger (rung E) | your routes, hiding spots and tricks, across shifts | your shifts | a file on your computer, fading each shift | built |
| The combo room (0.2.0) | which pairs of tricks work on you | your shifts | in the Ledger | built; found no pairs in the ablation |
| The Panic Index (the reward) | — | your movement: sprints, looking behind, freezing, dropping things, with hand-set weights | — | built; never checked against people |
| Rung F (PPO) | a network that picks tricks | simulated shifts | a model | built, never trained |
| Learned motion model (PR #68) | how players move, for her belief map | bot shifts | generated C# weights | about 70% built, paused |
| The capture plan | what a trick does to fear *and* to the chance of a catch; three kill moves | bot shifts | — | approved, not started |

**What tests found so far:**
- **Learning per player didn't help yet.** In the 16 October ladder, rungs D and E didn't beat the
  planner alone (rung C): panic fell 0.04, and the setpoint error didn't move.
- **Too little evidence per player:** each trick gets about one reward a shift.
- **Nothing general is learned.** Every player starts from the same hand-set guesses, whatever
  the last hundred players showed.

## 3. The design: three layers

| Layer | What she learns | From | When | Ships as |
|---|---|---|---|---|
| **1. Everyone** | what each trick does in each situation (to fear, and to the chance of a catch); where people hide and which routes they take; how fear shows in people's movement | every shift: bots and real players | offline, between builds | generated C# files, each with a model card (the repo keeps no binary models) |
| **2. You, tonight** | which tricks work on you right now; your calm baseline | your live shift | every decision window (10–20 s) | memory |
| **3. You, over time** | your routes, hiding spots, tricks and combos | your past shifts | the end of each shift | your Ledger file |

**How the layers combine (shrinkage).** Her guess for you is a blend of "everyone" and "you",
weighted by how much she has seen of you. With no evidence it's all "everyone"; each time she sees
what a trick does to you, your share grows.

*An illustration (the numbers are made up):*

| Moment | Everyone | You | Her guess for Blackout |
|---|---|---|---|
| your first shift | Blackout scares people (+0.12) | nothing yet | +0.12: she tries it early |
| after 3 blackouts that barely moved you (+0.01 each) | +0.12 | +0.01 | about +0.05 |
| after 8 | +0.12 | +0.01 | about +0.02: she stops leading with it and tries Ambush |

## 4. Choices for you

Answer these before the build starts.

### 4.1 The trick learner

| Option | What | Pros | Cons |
|---|---|---|---|
| A. Today's picker, with learned starting guesses | fit each trick's `PanicPrior` from everyone's data | the smallest change; easy to test | ignores the situation; still about one reward per trick per shift |
| **B. The capture plan's effect model** (recommended) | a Bayesian regression: a shared part that every trick sees, plus a part per trick, with the situation as input (phase, panic, how sure she is, your energy, time since the trick was last used…). It picks by Thompson sampling. Its prior is fitted from everyone | learns from every trick at once, so far more evidence per player; the "everyone" layer plugs straight in; already approved in outline | more code; needs the learning records (§5) |
| C. Rung F (PPO) | a network picks tricks | could find strategies we never wrote | about 10 Mac nights per training run; can't learn from real players directly, only in simulation; never trained once |

### 4.2 The chance of a catch

The effect model can also learn each trick's chance of leading to a catch (the capture plan's
second head).

| Option | Pros | Cons |
|---|---|---|
| **Learn it, but give it no weight** (recommended) | the data is ready for the two-Karens idea; she gets no deadlier | no gain in the game yet |
| Weigh it in tense phases, as the capture plan said | she chases harder when it's tense | she already catches the bots in 97–100% of shifts |

### 4.3 The others

| # | Choice | My suggestion |
|---|---|---|
| 4.3a | **Everyone's habits as a starting map for the Ledger:** where people hide, which routes they take | yes: a new player isn't a blank slate, and your own habits replace it as she learns them |
| 4.3b | **Calibrate the Panic Index** against real players' "Karen felt…" answers and fairness taps | build the tooling now; fit it once about 30 players have played. Until then, report only |
| 4.3c | **The learned motion model (PR #68)** | keep it as its own PR; retrain it on real players once both have landed |

## 5. The data

**Learning records.** Each time she picks a trick, one line:
- **the situation**, only what Karen may know (the same inputs as §4.1 B);
- **the choices** she was allowed, the one she picked, and **how likely she was to pick it**
  (needed to judge a new learner fairly on old data);
- **the outcome:** the fear change over the window, and whether a catch followed within 30 s;
- **a flag** when the Director's 45-second nudge toward your real area was active in the window.
  Those windows are left out of training, because her belief carried a hint from your real
  position ([HANDOVER known gaps](../project/HANDOVER.md#5-whats-next-in-order)).

**Where they go:**
- Written in bot runs and in playtest builds, as `learning.jsonl` in the session zip.
- Your Mac copies each session's records to R2 `datasets/learning/` when it pulls (playtest v2,
  C3.1). A few KB a shift, so nothing piles up on your Mac.
- The Ledger snapshot already in each zip gives everyone's habits (4.3a).

**Splits:** by player, never by shift, so no player is in both training and testing.
- **Bots:** the held-out cautious player is never in training or validation.
- **Real players:** a fixed set of testers (chosen by their random ID before anyone looks) is
  kept for testing only.

## 6. Training

| What | Where | Time | Output |
|---|---|---|---|
| bot data for the first prior | Unity batch on an APFS clone, editor closed, heavy-job lock, **your go** | a few hours | learning records |
| the effect model's prior (4.1 B) | your Mac, Python only, no Unity | minutes | `BanditPrior.g.cs` + its model card |
| everyone's habits (4.3a) | your Mac | minutes | `PopulationHabits.g.cs` |
| the Panic Index calibration (4.3b) | your Mac, once ~30 players have played | minutes | `PanicWeights.g.cs` |
| the motion model (PR #68, separate) | a free cloud notebook (Kaggle or Colab) reading R2, or this Mac | about 5 h | `MotionWeights.g.cs` |

**The model card** says what data the file was fitted on (dates, number of bot and real players),
its validation score, and the command that made it.

**After each playtest round:** refit → check on the held-out players → the ablation → the next
build. The same commands every time (`tools/learn/`).

## 7. Fairness

- **She still only reads what she may know.** The source scan
  (`KarenTests.BodyBeliefAndDecisions_NeverReadThePlayer`) covers the new code, and the learning
  records' inputs come from the same places.
- **"Everyone" is experience, not cheating.** It's what other players did, like a manager who has
  seen many staff. It never holds *your* position: "people often hide in the lockers", not "you're
  in locker 3".
- **The Director's shield is unchanged:** tiers, cooldowns and tension. The learner only ranks
  what the Director allows.
- **She still forgets** (rule 7): your Ledger fades each shift, as today.

## 8. How it's judged

Written before any test run, and kept as written. Seeded and paired, with standard errors.
Negative results are reported as they come.

**Primary metrics:**
1. **Prediction, offline.** On the held-out players (the cautious bot, and the held-out real
   players once there are enough), how well the model predicts the fear change after a trick
   (log-likelihood per window), against today's hand-set guesses. It wins if it's better on both.
2. **Adaptation, in simulation.** Paired by seed, the learned prior against the hand-set one:
   - the setpoint error (how close fear stays to the Director's target) over shifts 1–4;
   - how many shifts until her picks for a player differ from her picks for everyone.
3. **Fairness violations:** must stay at 0.

**Also reported:** catches, seconds to first detection, panic, combos found, per bot, the
held-out cautious one included.

**Real players:** described only in round 1, because there are too few to compare. Round 2 can give
half the testers (split by their random ID) the hand-set prior, written down before the round, with
the small sample stated.

## 9. The build

**Who:** [C] Claude, [B] the second builder (ChatGPT), [You]. As in 0.2.0: [B] has its own
branch and worktree, [C] merges at each sync, and no file belongs to both builders at once.

### Phase 0: before building [You]

- [ ] **Y0.1 Answer [§4](#4-choices-for-you).**
- [ ] **Y0.2 Playtest v2's phases 1–3 built**, so the data has a path to the cloud.

### Phase 1: the data [C]

- [ ] **C1.1 The learning records** (§5) in bot runs and playtest builds.
  - Files: a new `Diagnostics/LearningRecord.cs`; `KarenBrain.cs` (`SettleRewards`), `KarenLedger.cs`.
  - Done when: a shift writes one line per pick, and a test checks the inputs only use what she may know.
- [ ] **C1.2 Picking chances** logged with each pick (50 extra posterior draws under Thompson
  sampling; exact under today's picker).
- [ ] **C1.3 The dataset:** your Mac's pull copies the records to R2 `datasets/learning/`, and
  `tools/learn/fetch.py` downloads them for training into a temporary folder.
  - Files: `tools/marketing/km/playtest/inbox.py` (with playtest v2), a new `tools/learn/`.
- [ ] **C1.4 The bot data run** (your go; the editor closed): rungs D and E, the efficient,
  skittish and reckless bots, new seeds. The cautious bot plays only in the test run.

### Phase 2: the learner [C]

- [ ] **C2.1 The effect model** (§4.1 B): the shared part and the per-trick part, with the
  situation as input, picking by Thompson sampling. Today's picker stays behind
  `banditModel = ucb` for the comparison.
  - Files: a new `Decision/EffectModel.cs`; `KarenLedger.cs`, `KarenConfig.cs`.
- [ ] **C2.2 The prior from everyone:** `tools/learn/fit_prior.py` writes `BanditPrior.g.cs` and its model card.
  - Done when: refitting from the same records gives the same file.
- [ ] **C2.3 You, tonight and over time:** your posterior starts from the prior each career and
  is kept in the Ledger between shifts, fading as today (rule 7).
- [ ] **C2.4 The catch head** (§4.2), learned, with the weight you chose.
- [ ] **C2.5 The thought log** says what she learned, for example:
  `LEARN blackout Δpanic +0.01 (everyone +0.12, you +0.03)`.

### Phase 3: habits and the Panic Index [C]

- [ ] **C3.1 Everyone's habits** (4.3a): `tools/learn/fit_habits.py` writes
  `PopulationHabits.g.cs`; the Ledger starts from it and your own habits take over.
  - Files: `KarenLedger.cs`, the new generated file.
- [ ] **C3.2 The Panic Index calibration** (4.3b): the fitting script and its report; the weights
  stay hand-set until ~30 real players.

**Sync 3:** [C] runs the EditMode tests on a clone; [B] merges back.

- [ ] **B3.1 [B] "Everyone and you" in the shift report:** for each trick she used, what she
  expected from everyone, what she expected from you, and what happened.
  - Files: `ShiftReportHtml.cs`.
- [ ] **B3.2 [B] The same on the F1 map's panel,** live.
  - Files: `KarenDebugOverlay.cs`.

### Phase 4: judging [C]

- [ ] **C4.1 The offline check** (primary 1): `tools/learn/evaluate.py` on the held-out players.
- [ ] **C4.2 The ablation** (primaries 2 and 3), on your go, with the editor closed for a few hours.
- [ ] **C4.3 Results and docs:** `KAREN_RESULTS.md`, negative results included; `Karen.md` §7;
  `DECISIONS.md`; the How Karen Learns guide.

### Phase 5: real players [You], with [C]

- [ ] **Y5.1 Your playtest,** then review and merge.
- [ ] **C5.1 After the first real round:** refit the prior with the real players, check it on
  the held-out ones, and report what changed.

## 10. Honest limits

- **Friends are few.** Ten to thirty players with about three shifts each will refine the prior a
  little, not transform it. Steam playtests bring the numbers.
- **The Panic Index is a guess at fear.** Until it's calibrated, "she learned what scares you"
  means "she learned what makes you move like a scared person".
- **Bots aren't people.** A prior fitted on bots mostly teaches what the tricks do mechanically.
- **Smarter may not mean better to play.** She's already very deadly against the bots. This plan
  doesn't add catch pressure (4.2), but sharper adaptation could still change how she feels; it's
  measured, not assumed.

## 11. Not in this plan

- **Two Karens with different rewards:** [IDEAS.md §7](../project/IDEAS.md#7-two-versions-of-karen).
- **New kill moves** (the capture plan's §3). Cut and Ambush on her guessed routes, from 0.2.0,
  already cover two of the three.
- **Training rung F.**
- **The learned motion model** (PR #68), which stays its own PR.

## 12. Terms

| Term | What it is | How Kehai uses it | Real-world example |
|---|---|---|---|
| Prior | a starting guess before seeing you | "everyone": what each trick did to other players | autocorrect before it knows your words |
| Shrinkage | blending a group's average with one person's own record, by how much evidence there is | her guess for you, between everyone and you | a new restaurant's rating, pulled toward average until it has many reviews |
| Thompson sampling | picking by drawing a plausible value for each option and taking the best draw | how she picks tricks, so unsure ones still get tried | a news app trying new headlines on some readers |
| Picking chance (propensity) | how likely she was to pick what she picked | judging a new learner on old data, fairly | correcting a survey for who chose to answer |
| Model card | a short note saying what a model was trained on and how well it did | one beside each generated file | the nutrition label on a food packet |
| Held-out | data kept away from training, only for the final test | the cautious bot, and a set of real players | an exam with questions the students haven't seen |
