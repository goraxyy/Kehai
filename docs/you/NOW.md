# Now

Updated 2026-11-13. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Kehai 0.4.1:** your 17 items of 12 November (her eyes, cameras and two tricks, closing on the PA, 20 s fixes, the map, the look, the eyelids, the docs) | built, in a pull request from branch `0.4.1` | [what players notice](../agents/CHANGELOG.md), [what changed in Karen](../agents/karen/Karen.md#19-kehai-041-her-eyes-the-stores-cameras-closing-time) |
| **Karen 0.4.0:** two trained Karens, players that feel real | approved 11 November. A build session is working through phases 0–6. **Nothing is trained yet** | [the plan](plans/0.4.0-build-plan.md), on its own build branch (local, not pushed) |

The docs split (this layout) merged on 11 November as PR #80.

The plan on `main` is one step behind: the approved version, with fitted senses, the mirror map
and the live model, arrives when the 0.4.0 branch merges.

## Waiting on you

| What | Why it waits |
|---|---|
| **0.4.1 and 0.4.0 touch the same files.** A short list of questions, one per overlap | only you can say which side wins; the list is in the 0.4.1 pull request |
| Import the four new models: **Kehai → Props → Import Karen's Props** | until then the PA microphone, the camera box, the cameras and the screens are plain boxes |
| Play a shift on 0.4.1 | her eyes are twice as far and twice as quick, and no test has measured that |
| Play 5–10 shifts on today's build (0.4.0 plan, Y1.1) | you're the first human reference for the bots |
| The go for each 0.4.0 recording, training and test run | "I will do the training later" |
| Yes or no: a new model is published only on your yes each time (0.4.0 plan, D15) | a default I picked; you haven't seen it before |
| How far to turn her down ([the tuning plan](plans/0.2.0-tuning-plan.md), Q2–Q5) | only you can answer; 0.4.1 turned her eyes up |
| The word to push the 0.4.0 build branch and open its PR | nothing leaves this Mac, or lands on `main`, without it |

## Next, in order

1. **Finish the 0.4.0 build,** then your training runs, then its tests and write-up.
2. **Real players:** [playtest v2](plans/playtest-v2.md). It brings people's shifts, and the live
   model's download depends on it.
3. **Hands-off videos:** [the marketing agents plan](plans/marketing-agents.md), phase 2 on.
4. **The release:** the Steam page, the demo, the redesign ([RELEASE_PLAN.md](RELEASE_PLAN.md)).

## Done lately

- **12–13 November:** Kehai 0.4.1 built. 0.3.0's ablation ran again as 0.4.0's baseline (with
  her old eyes).
- **11 November:** `main` passes all 360 EditMode tests and all 222 marketing tests. The docs
  split merged (PR #80).
- **10 November:** every open PR cleared. Merged: the 0.2.0 tuning plan (#75), playtest v2 and the
  learning plan (#76), the website draft (#77), Karen 0.3.0's tricks (#78), the 0.4.0 plan (#79).
  Closed: the old motion model (#68), redesigned inside 0.4.0.

## Known gaps

Kept in one place: [HANDOVER.md §5](../agents/HANDOVER.md#5-whats-next-in-order).
