# Now

Updated 2026-11-14. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Karen 0.4.0:** two trained Karens, players that feel real | **merged into `main` 14 November as code (PR #83).** 430 of 430 tests pass, and the game plays as `main` with every switch off. The belief fits (C4.10) ran on 13 November: fitted senses (S1) and P1 beat today's; P2 and P3 lost, even to today's step ([results](../agents/results/KAREN_RESULTS.md#c410-the-belief-fits-13-november)). Nothing is trained: every training and test run comes after the building, once ([TESTING.md](TESTING.md)) | [the plan](plans/0.4.0-build-plan.md) |
| **Kehai 0.4.1:** fog rooms, her sight, cameras, two camera tricks, the PA closing, 20 s holds, the soft UI, the F1 map | built on a local branch; no PR yet. It shares 7 code files with 0.4.0, so it's reconciled with `main` before its PR | local branch `claude/0-4-1-2305a2` |
| **Kehai 0.5.0** | the plan, for review: 34 questions in its §16 | [PR #82](https://github.com/goraxyy/Kehai/pull/82) |
| **Finish the test build / playtest v2** | checkbox plan updated; implementation not started beyond the homepage draft | [the build plan](plans/playtest-v2.md) |

## Waiting on you

| What | Why it waits |
|---|---|
| The word for the 0.4.1 session to reconcile with `main`, push and open its PR | it's built, and only waits for that |
| Answer the 0.5.0 plan's questions ([PR #82](https://github.com/goraxyy/Kehai/pull/82), §16), above all which "agents" (Q9–Q10) | the bots' training waits for the agents choice ([TESTING.md](TESTING.md#the-agents-question)) |
| Keep the blur in her senses, or play the sharper fitted S1 | S1 trusts every sighting and noise almost fully: she'd play sharper |
| Yes or no: delete the old 480 MB belief dataset (Q5) | the new recordings replace it |
| Yes or no: sort the store's doors so a doorway keeps its number (Karen's Ledger mixes up doorways between shifts today) | it changes play against `main`; 0.5.0's layouts plan to do it (its C4.4) |
| Cloudflare account/R2 and browser authorization; optional Telegram setup | [account steps](plans/playtest-v2.md#13-setting-up-the-accounts); no real-player upload connection is configured on this Mac |

The playtest and tuning questions are answered: see the
[playtest answers](plans/playtest-v2.md#3-questions-for-you),
[tuning answers](plans/0.2.0-tuning-plan.md#3-your-decisions) and [decision log](DECISIONS.md).
No tester code is sent or typed. The game assigns an internal ID automatically; collection
runs independently of your computers and training starts later, manually, on Windows.

## Next, in order

Build everything first, then test and train once (your call, 14 November).

1. **Kehai 0.4.1:** reconcile with `main`, its PR, merge.
2. **Kehai 0.5.0:** answer the plan, merge it, then build its PRs (tricks, guide, versions,
   layouts, agents, Karen ML, training view, look, status).
3. **Vertical slice:** a convincing playable shift ([release milestone 1](RELEASE_PLAN.md#milestone-1-vertical-slice)).
4. **Finish the test build:** [playtest v2](plans/playtest-v2.md): illustrated instructions,
   no-code first launch, automatic cloud collection, and a dataset you can download on Windows
   for training later. Uploads do not start training or require the Mac to process them.
5. **Testing and training, once, on the finished game:** in the order of [TESTING.md](TESTING.md).
6. **Real players:** run the first round with the finished test build, review, fix, repeat.
   The live model's download also depends on v2; publication still requires evaluation and
   compatibility checks, but no per-model approval from you.
7. **Hands-off videos:** [the marketing agents plan](plans/marketing-agents.md), phase 2 on.
8. **The release:** the Steam page, the demo, the redesign ([RELEASE_PLAN.md](RELEASE_PLAN.md)).

## Done lately

- **14 November:** Karen 0.4.0 merged as code (PR #83).
- **11 November:** the docs split merged as PR #80.
- **11 November:** `main` passes all 360 EditMode tests and all 222 marketing tests.
- **10 November:** every open PR cleared. Merged: the 0.2.0 tuning plan (#75), playtest v2 and the
  learning plan (#76), the website draft (#77), Karen 0.3.0's tricks (#78), the 0.4.0 plan (#79).
  Closed: the old motion model (#68), redesigned inside 0.4.0.

## Known gaps

Kept in one place: [HANDOVER.md §5](../agents/HANDOVER.md#5-whats-next-in-order).
