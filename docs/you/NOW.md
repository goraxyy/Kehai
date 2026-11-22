# Now

Updated 2026-11-21. One page: what's being built, what waits on you, what's next. Agents keep it
current. The rules for agents are in [`docs/agents/RULES.md`](../agents/RULES.md).

## Being built

| What | State | Where |
|---|---|---|
| **Kehai 0.5.2** | being built: sight and pursuit sandboxes, witnessed hiding, placement checks and the Unity-free test lane (draft [PR #92](https://github.com/goraxyy/Kehai/pull/92)); the plan's focus items are in [PR #91](https://github.com/goraxyy/Kehai/pull/91). Every played test of 0.5.0–0.5.2 runs once, recorded, after 0.5.2 merges | [the plan](plans/0.5.2-plan.md), [the sandboxes](../agents/game/SANDBOX.md) |
| **Kehai 0.5.1** | merged 18 November (PR #89) | [the plan](plans/0.5.1-plan.md) |
| **Karen 0.4.0:** two trained Karens, players that feel real | **merged into `main` 14 November as code (PR #83).** 430 of 430 tests pass, and the game plays as `main` with every switch off. The belief fits (C4.10) ran on 13 November: fitted senses (S1) and P1 beat today's; P2 and P3 lost, even to today's step ([results](../agents/results/KAREN_RESULTS.md#c410-the-belief-fits-13-november)). Nothing is trained: every training and test run comes after the building, once ([TESTING.md](TESTING.md)) | [the plan](plans/0.4.0-build-plan.md) |
| **Kehai 0.4.1:** your 17 items of 12 November: her eyes (eye to eye, twice as far and as quick), six cameras and two camera tricks, closing on the PA, 20 s fixes, fog rooms, doors, the F1 map, the soft look, the eyelids, the trick videos tab, the docs | merged as PR #85 on 14 November | [what players notice](../agents/CHANGELOG.md), [what changed in Karen](../agents/karen/Karen.md#19-kehai-041-her-eyes-the-stores-cameras-closing-time) |
| **Kehai 0.5.0** | source delivered in PR #82; owner chose merge now, further checks later on 14 November. Completed native checks and open follow-ups are in the [build status](plans/0.5.0/implementation-status.md) | [PR #82](https://github.com/goraxyy/Kehai/pull/82) |
| **Finish the test build / playtest v2** | local upload worker in progress, including retry protection for original ZIPs; the consented end-to-end run awaits cloud setup | [the build plan](plans/playtest-v2.md) |

## Waiting on you

| What | Why it waits |
|---|---|
| 0.5.1, after it merges: in `~/Developer/Kehai`, with the store scene open, run **Kehai → Cold Storage → Move Existing Islands Closer** and **Kehai → Store Finish → Apply Door, Locker and Exterior Fit**, save the scene, then make a new `kehai-assets` release for the Windows PC | they move the cold-room islands, delete the dead office camera and save the door fix into the scene |
| 0.5.1: turn webcam blinking on, consent, calibrate | the camera helper is built and found, but nobody has started your camera |
| 0.5.1: coffee machine on the kitchen counter (now) or the dining table? | one line to change |
| After the 0.5.2 merge, play the recorded shift covering 0.5.1 | witnessed hiding, the 60 s pursuit, the stun and the new light are tested in code, not played |
| Import the four new models: **Kehai → Props → Import Karen's Props** | until then the PA microphone, the camera box, the cameras and the screens are plain boxes |
| Play a shift on 0.4.1 | her eyes are twice as far and twice as quick, and no test has measured that |
| 0.5.0 scope is settled; later art review and external storage setup remain | adaptive ML bots are selected; long training remains after the build |
| Keep the blur in her senses, or play the sharper fitted S1 | S1 trusts every sighting and noise almost fully: she'd play sharper |
| Yes or no: delete the old 480 MB belief dataset (Q5) | the new recordings replace it |
| Cloudflare account/R2 and browser authorization; optional Telegram setup | [account steps](plans/playtest-v2.md#13-setting-up-the-accounts); no real-player upload connection is configured on this Mac |

The playtest and tuning questions are answered: see the
[playtest answers](plans/playtest-v2.md#3-questions-for-you),
[tuning answers](plans/0.2.0-tuning-plan.md#3-your-decisions) and [decision log](DECISIONS.md).
No tester code is sent or typed. The game assigns an internal ID automatically; collection
runs independently of your computers and training starts later, manually, on Windows.

## Next, in order

Build everything first, then test and train once (your call, 14 November).

1. **Kehai 0.4.1:** merged as PR #85.
2. **Kehai 0.5.0 source delivery:** PR #82. The owner authorised merging before further tests.
   Remaining checks and setup stay open in the [build status](plans/0.5.0/implementation-status.md),
   with heavy work deferred until Windows access.
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

- **14 November:** Karen 0.4.0 merged as code (PR #83). Kehai 0.4.1 reconciled with it and opened as a PR.
- **12–13 November:** Kehai 0.4.1 built.
- **11 November:** the docs split merged as PR #80.
- **11 November:** `main` passes all 360 EditMode tests and all 222 marketing tests.
- **10 November:** every open PR cleared. Merged: the 0.2.0 tuning plan (#75), playtest v2 and the
  learning plan (#76), the website draft (#77), Karen 0.3.0's tricks (#78), the 0.4.0 plan (#79).
  Closed: the old motion model (#68), redesigned inside 0.4.0.

## Known gaps

Kept in one place: [HANDOVER.md §5](../agents/HANDOVER.md#5-whats-next-in-order).
