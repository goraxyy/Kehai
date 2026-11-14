# Testing and training left

Updated 2026-11-14. Everything that still has to run for 0.4.0's two Karens and 0.5.0's plan, in
the order it should run, with time on the Mac and on the Windows PC.

**Your rule: build everything first, then test and train once, on the finished game.** Every long
run waits for your go. The commands are in the [0.4.0 plan, §12](plans/0.4.0-build-plan.md#12-the-work-phase-by-phase)
and the [0.5.0 plan](https://github.com/goraxyy/Kehai/pull/82) (open PR).

## Testing is not training

| | Training | Testing |
|---|---|---|
| **What it is** | A model plays many shifts and changes its own numbers to do better | A finished model plays shifts it has never seen; nothing changes, you only measure |
| **How Kehai uses it** | The Employee bot, Vital and Basic learn by playing (millions of decisions); her senses and P1 are fitted to recordings | The belief tests, the two ladders (catch and fear), the exploit check, the trick check |
| **Real-world example** | A spam filter learning from emails people marked as spam | Checking that filter on a fresh week of email it never saw |

- **Training is slow** (days to weeks). **Testing is quick** (minutes to nights).
- **The held-out players and layouts are for testing only, once.** Testing on them twice, or
  tuning after seeing them, makes the result meaningless.
- **Fitting** is a small kind of training: a few numbers, hours, not days.

## Why it all waits

Each of these changes what she or the bots would learn:

| Build | What it changes |
|---|---|
| 0.4.1 | her sight, six cameras, two camera tricks, three new chores |
| 0.5.0 tricks (Phase 1) | the flash sale and the extras you pick: her list of choices |
| 0.5.0 layouts (Phase 4) | ten store layouts: training draws a layout each career, two are held out |
| 0.5.0 agents (Phase 2) | who plays against her; see [the agents question](#the-agents-question) |
| 0.5.0 versions (Phase 7) | the Training version fixes the training player, so Windows can run 8 copies at once |
| 0.5.0 cheat training (Phase 3) | escape habits in the Ledger, and a refit aimed at the moments after she loses you |

## The times

- **The Mac** is measured where it says so, otherwise estimated.
- **Windows** isn't measured. It assumes a 16-core PC running 8 copies at once, after 0.5.0's
  Training version (Phase 7) makes the training player connect. The PC's cores, RAM and GPU
  aren't known yet ([0.5.0 Q3](https://github.com/goraxyy/Kehai/pull/82)).
- **Across ten layouts, training will take longer** than on one store: how much isn't known.
  The training times below are for one store and are the low end.

---

## Part 1: checks that go with each build

These aren't the testing phase. They're how each PR shows it didn't break anything, and they're quick.

| What | When | Mac | Windows |
|---|---|---|---|
| The C# suite, the trick check, the default game against `main` | every PR that touches the game | 15 min (measured) | 15 min |
| The trick check on every layout (0.5.0 C4.6) | the layouts PR | ~1 h | ~30 min |
| The bots' stuck shifts on every layout (0.5.0 C4.8) | the layouts PR | ~3 h | ~1.5 h |
| Each style's frame time (0.5.0 C8.4) | the look PR | 1 h | 1 h |

---

## Part 2: testing and training, once everything is built

### Stage 1: baselines on the finished game (testing only)

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 1 | **The new baseline:** 0.3.0's ablation on the finished game | 66 min (measured) | ~30 min | **Yes** | Every later result is compared with this; the 12 November one predates 0.4.1's eyes |
| 2 | **The new tricks' check** (0.5.0 C1.5): fairness, stuck shifts, each new trick lands in 80% of its starts | 1.5 h | ~45 min | **Yes** | The tricks must work before she learns to use them |
| 3 | **You play:** 5–10 normal shifts (0.4.0 Y1.1), two with the flash sale forced (0.5.0 Y1.1), one guided first shift (0.5.0 Y6.2). I score them (0.4.0 C1.4) | 1–2 h for you | the same | **Yes** | The only human reference the bots are judged against |

### Stage 2: the bots (training). Holds for the agents decision

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 4 | **Train Employee-1** (0.4.0 C2.7): 20,000 demos, then 1 M decisions, across the training layouts | ~5 h + **~11 days** (measured speed) | ~1–2 days | **Yes**, unless the agents change it | Karen trains against these bots |
| 5 | **Escapes to learn from** (0.5.0 C3.5): recordings where the bots get away, at least 300 lost sightings | ~2 h | ~1 h | **Yes**, for cheat training | She almost never loses a bot today (5–13 of 144 shifts) |

### Stage 3: her belief (fitting, then testing)

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 6 | **Record her belief data** (0.4.0 C4.9) with Employee-1, on the training layouts | ~2–3 h (80 min on one store, measured) | ~1 h | **Yes** | The 13 November fits used the stand-in bot, the old eyes and one store |
| 7 | **Fit again** (0.4.0 C4.10): P1's speed range widened; first **find out why P2 and P3 lost**; the escape refit's first run (0.5.0 C3.4) | 7–8 h (7 h 41 min measured) | ~4 h | **Yes** | P1 was held back by its range; P2 and P3 lost even to today's step |
| 8 | **The belief tests** (0.4.0 C4.11) and the **escape-habits test** (0.5.0 C3.6), on the held-out players, then the models freeze | 4–7 h | ~2–4 h | **Yes**, once | The real test of "S1 and P1 win", and of whether she learns where you run |

### Stage 4: the two Karens (training)

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 9 | **Train Vital-1 and Basic-1** (0.4.0 C5.9), two runs each, across the training layouts | 7 h to 6 nights per run, 4 runs (not measured: her speed falls as she learns) | ~1.5–3 days in all | **Yes** | The two trained Karens themselves |
| 10 | **Rounds 3 and 4** (0.4.0 C5.10): Employee-2, then Vital-2 and Basic-2 | ~1–2 weeks | ~2–4 days | Optional | Each side against the other's newer self. Without it the claim is weaker, not wrong |

### Stage 5: the final tests (testing only, once)

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 11 | **The two ladders** (0.4.0 C6.2) on the training layouts, and the **same ladders on the two held-out layouts** (0.5.0 C4.10) | 2 nights + ~1 night | ~1 night + ~half a night | **Yes**, once | The headline results, and whether she copes with a store she never trained in |
| 12 | **The exploit check** (0.4.0 C6.3): a fresh Survivor trained to beat each frozen Karen | ~3 days each (300,000 decisions at the measured bot speed) | ~10 h each | **Yes** for Vital, nice to have for Basic | One cheap trick that beats her (a spot she never looks) is a failure to fix |
| 13 | **The agents panel** (0.5.0 C2.4): 3–5 language-model players, one career of six shifts each, against the finished Karen | 1–5 h (the API, not the CPU); about $1–2 on Haiku 5.5 | runs from the Mac | **Yes**, if you build the panel | A player she was never trained or tuned against: the fairest test, and a showpiece |
| 14 | **You play both Karens** (0.4.0 Y6.1) and **decide what ships** (Y6.2) | a few shifts each | the same | **Yes** | What it feels like to play against them |

### Stage 6: real players (after playtest v2 ships)

| # | What | Mac | Windows | Necessary? | Why |
|---|---|---|---|---|---|
| 15 | **A round with real players**, then the round routine (0.4.0 C7.1): fit the bots and the starting Ledger to people, refit her senses and predictions, a refresh round of training | your go each round; about a night | about a night | **Yes**, for release | People, not bots, are what she's for. Cheat training's central refit needs their sessions |

### Optional, any time before Stage 2

| What | Mac | Why |
|---|---|---|
| Find 0.3.0's batch slowdown in the Profiler (0.4.0 C0.2) | 1–2 h | It may multiply every run above by up to 3.6 |
| Two copies at once (0.4.0 C0.3) | 20 min | Done by 0.5.0's Training version (C7.3) |

---

## Totals

| | Mac | Windows (8 copies) |
|---|---|---|
| Part 1, inside the builds | about 5 h, spread over the PRs | about 3 h |
| Testing only (1–3, 8, 11, 13, 14) | about 1.5 days of machine time + 3 nights, plus your shifts | about 1 day + 1.5 nights |
| Fitting and recording (5–7) | about 12 h | about 6 h |
| Training (4, 9, 10, 12), one store | **about 5–7 weeks** | **about 1.5–2 weeks** |
| **All** | **about 6–8 weeks, more across layouts** | **about 2–3 weeks, more across layouts** |

**Suggested split:**
- **The Mac:** light, exact jobs (1–3, 5–8, Part 1, the panel).
- **Windows:** training (4, 9, 10, 12) and the ladders (11).

## The agents question

The 0.5.0 plan ([§7](https://github.com/goraxyy/Kehai/pull/82), Q9–Q10) offers five kinds of
"agent". **Hold Stage 2 onward until you've chosen**, because the choice decides what gets trained:

| If you choose | Employee-1 (#4) | What else changes |
|---|---|---|
| **A + B panel** (the plan's suggestion): keep the trained bots, add language-model players as a judge | trained as planned | #13 is added; nothing is redone |
| **C hybrid:** a language model sets the bot's goal every 20–30 s, the trained mind walks | trained, but to follow given goals: a new curriculum | the bots cost money every shift, so Karen's millions of training decisions can't use it. She still trains against plain bots |
| **D:** a language model writes the bots' personas between shifts | trained as planned, on more varied personas | pennies a career |
| **E:** copy people from replays | waits for about 100 real players | Stages 2–5 move after Stage 6 |
| **B alone** as her opponent | not trained | not possible for training: about $5,000 even on Haiku, and months |

**Only A and E are fast and cheap enough for her to train against.** So whatever you choose, she
trains against bots. The agents decide how good those bots are, and who judges her at the end.
