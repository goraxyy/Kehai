# Now

Updated 2026-11-11. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Karen 0.4.0:** two trained Karens, players that feel real | approved 11 November. Built: phases 1–5's code and tests (428 of 428 pass; the game plays as `main` with every switch off). **Nothing is trained yet**: every recording, training and test run waits for your go, one command each in the plan | [the plan](plans/0.4.0-build-plan.md), on its own build branch (local, not pushed) |
| **The docs split** (this layout) | merged into `main` (PR #80) | this page |

The plan on `main` is one step behind: the approved version, with fitted senses, the mirror map
and the live model, arrives when the 0.4.0 branch merges.

## Waiting on you

| What | Why it waits |
|---|---|
| Play 5–10 shifts on today's build (0.4.0 plan, Y1.1) | you're the first human reference for the bots |
| The go for 0.3.0's full ablation re-run | a long run; it's also 0.4.0's baseline |
| The go for each 0.4.0 recording, training and test run | "I will do the training later" |
| Yes or no: a new model is published only on your yes each time (0.4.0 plan, D15) | a default I picked; you haven't seen it before |
| How far to turn her down ([the tuning plan](plans/0.2.0-tuning-plan.md), Q2–Q5) | only you can answer |
| The word to push the 0.4.0 build branch and open its PR | nothing leaves this Mac, or lands on `main`, without it |

## Next, in order

1. **Finish the 0.4.0 build,** then your training runs, then its tests and write-up.
2. **Real players:** [playtest v2](plans/playtest-v2.md). It brings people's shifts, and the live
   model's download depends on it.
3. **Hands-off videos:** [the marketing agents plan](plans/marketing-agents.md), phase 2 on.
4. **The release:** the Steam page, the demo, the redesign ([RELEASE_PLAN.md](RELEASE_PLAN.md)).

## Done lately

- **11 November:** `main` passes all 360 EditMode tests and all 222 marketing tests.
- **10 November:** every open PR cleared. Merged: the 0.2.0 tuning plan (#75), playtest v2 and the
  learning plan (#76), the website draft (#77), Karen 0.3.0's tricks (#78), the 0.4.0 plan (#79).
  Closed: the old motion model (#68), redesigned inside 0.4.0.

## Known gaps

Kept in one place: [HANDOVER.md §5](../agents/HANDOVER.md#5-whats-next-in-order).
