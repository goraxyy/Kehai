# Now

Updated 2026-11-14. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Karen 0.4.0:** two trained Karens, players that feel real | approved 11 November. Built: phases 1–5's code and tests (430 of 430 pass; the game plays as `main` with every switch off). The baseline ablation ran on 12 November: fairness 0, but 12 of 240 shifts stuck. The belief recordings (C4.9) ran on 13 November and pass the parity gate. **The belief fits (C4.10) ran on 13 November:** fitted senses (S1) and P1 beat today's; P2 and P3 lost, even to today's step ([results](../agents/results/KAREN_RESULTS.md#c410-the-belief-fits-13-november)). The brains and bots aren't trained: their runs wait for 0.4.1 to merge (your call, 14 November) | [the plan](plans/0.4.0-build-plan.md); closed as code 14 November: branch `0.4.0`, its PR waits for your review |
| **The docs split** (this layout) | merged into `main` (PR #80) | this page |

The plan on `main` is one step behind: the approved version, with fitted senses, the mirror map
and the live model, arrives when the 0.4.0 branch merges.

## Waiting on you

| What | Why it waits |
|---|---|
| Play 5–10 shifts on the 0.4.1 build, once merged (0.4.0 plan, Y1.1) | you're the first human reference for the bots |
| After 0.4.1 merges: the go for 0.4.0's runs, in the order of [TESTING.md](TESTING.md) | they run once, on the final game |
| Keep the blur in her senses, or play the sharper fitted S1 | S1 trusts every sighting and noise almost fully: she'd play sharper |
| Yes or no: delete the old 480 MB belief dataset (Q5) | the new recordings replace it |
| Yes or no: sort the store's doors so a doorway keeps its number (Karen's Ledger mixes up doorways between shifts today) | it changes play against `main` |
| Yes or no: a new model is published only on your yes each time (0.4.0 plan, D15) | a default I picked; you haven't seen it before |
| How far to turn her down ([the tuning plan](plans/0.2.0-tuning-plan.md), Q2–Q5) | only you can answer |
| Review and merge the 0.4.0 PR, and say which of 0.4.0 and 0.4.1 merges first | the second one reconciles 7 shared code files |

## Next, in order

1. **Merge 0.4.0 and 0.4.1,** then 0.4.0's training and test runs, once, on the merged game.
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
