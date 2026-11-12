# Now

Updated 2026-11-12. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Karen 0.4.0:** two trained Karens, players that feel real | the local build branch reports phases 1–5's code and tests built. Long recordings, training and evaluation still wait for you; no production model trained | [the plan](plans/0.4.0-build-plan.md); local branch `claude/karen-0-4-0-build-d81c79`, not pushed |
| **Finish the test build / playtest v2** | checkbox plan updated; implementation not started beyond the homepage draft. No v2 implementation PR or local build work found on 12 November | [the build plan](plans/playtest-v2.md) |

The plan on `main` is one step behind: the approved version, with fitted senses, the mirror map
and the live model, arrives when the 0.4.0 branch merges.

## Waiting on you

| What | Why it waits |
|---|---|
| Play 5–10 shifts on today's build (0.4.0 plan, Y1.1) | you're the first human reference for the bots |
| The go for 0.3.0's full ablation re-run | a long run; it's also 0.4.0's baseline |
| The go for each 0.4.0 recording, training and test run | "I will do the training later" |
| The word to push the 0.4.0 build branch and open its PR | this plan PR does not publish the separate ML branch |
| Bring the 0.4.1 changes from your Notes app into its plan | the notes have not been imported; no feature list has been assumed |
| Cloudflare account/R2 and browser authorization; optional Telegram setup | [account steps](plans/playtest-v2.md#13-setting-up-the-accounts); no real-player upload connection is configured on this Mac |

## Open questions

| Question | Where / suggested answer (not yet your decision) |
|---|---|
| Must each new model wait for your explicit yes before publication? | 0.4.0 build branch, D15; yes by default |
| Keep her current eye targets? | [tuning Q2](plans/0.2.0-tuning-plan.md#3-your-decisions); keep them |
| Should a touch outside a Hunt still kill? | same, Q3; only Hunt and Lie in wait kill |
| May her sprint drop below 7.5 m/s? | same, Q4; try 7.0 only if other changes are insufficient |
| Should bots see her spotlight? | same, Q5; yes |
| Domain, written questions, nickname, fairness prompt, retention, update notice, deletion email and rounds? | [playtest Q1–Q8](plans/playtest-v2.md#3-questions-for-you), with suggested answers there |

Already decided: no typed tester code; collect in the cloud independently of your computers;
training starts later, manually, on Windows. The Basic catch target is already 0.20–0.35.

## Next, in order

1. **Finish the 0.4.0 build,** then your training runs, then its tests and write-up.
2. **Karen 0.4.1:** bring in your Notes app changes, make its checkbox plan, then build them.
3. **Vertical slice:** a convincing playable shift ([release milestone 1](RELEASE_PLAN.md#milestone-1-vertical-slice)).
4. **Finish the test build:** [playtest v2](plans/playtest-v2.md): illustrated instructions,
   no-code first launch, automatic cloud collection, and a dataset you can download on Windows
   for training later. Uploads do not start training or require the Mac to process them.
5. **Real players:** run the first round with the finished test build, review, fix, repeat.
   The live model's download also depends on v2; publishing remains separately gated.
6. **Hands-off videos:** [the marketing agents plan](plans/marketing-agents.md), phase 2 on.
7. **The release:** the Steam page, the demo, the redesign ([RELEASE_PLAN.md](RELEASE_PLAN.md)).

## Done lately

- **11 November:** the docs split merged as PR #80; it no longer waits for approval.
- **11 November:** `main` passes all 360 EditMode tests and all 222 marketing tests.
- **10 November:** every open PR cleared. Merged: the 0.2.0 tuning plan (#75), playtest v2 and the
  learning plan (#76), the website draft (#77), Karen 0.3.0's tricks (#78), the 0.4.0 plan (#79).
  Closed: the old motion model (#68), redesigned inside 0.4.0.

## Known gaps

Kept in one place: [HANDOVER.md §5](../agents/HANDOVER.md#5-whats-next-in-order).
