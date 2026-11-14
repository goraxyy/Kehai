# Your decisions

Your calls, one dated line each, newest first. Agents add a line when you decide something, then
bring the detailed docs in line. The technical choices and their reasons are in
[`docs/agents/DECISIONS.md`](../agents/DECISIONS.md).

| Date | Your call | Detail |
|---|---|---|
| 2026-11-14 | Reconcile 0.4.1 with `main` now that 0.4.0 is merged, push it and open its PR; then build 0.5.0 | this list; the 0.4.1 pull request |
| 2026-11-14 | Build first, test later: 0.4.1, then 0.5.0, then the vertical slice and the test build; every test and training run comes after, once ([TESTING.md](TESTING.md)) | [NOW.md](NOW.md#next-in-order) |
| 2026-11-14 | Confirmed: no yes needed for each published model (the 12 November call), now in 0.4.0's plan too | [0.4.0 plan, D15](plans/0.4.0-build-plan.md#41-defaults-i-picked-change-any) |
| 2026-11-14 | Close 0.4.0 as code: push it and open its PR now. Its training and test runs wait for 0.4.1 (and 0.5.0's layouts, if approved), then run once on the final game | [0.4.0 plan](plans/0.4.0-build-plan.md) |
| 2026-11-13 | Record the belief data now, with the hand-written bot standing in for Employee-1; record again after Employee-1 is trained | [0.4.0 plan](plans/0.4.0-build-plan.md), C4.9 |
| 2026-11-12 | Her eyes: twice the radius, twice as quick, and cover judged eye to eye (a low shelf doesn't hide you if she sees your eyes; under a rack she doesn't see you without that line). Built with no test yet | [Karen.md §19.1](../agents/karen/Karen.md#191-her-eyes) |
| 2026-11-12 | Six store cameras shown on the office screens; two tricks, Camera watch and Camera break | [Karen.md §19.2](../agents/karen/Karen.md#192-the-stores-cameras) |
| 2026-11-12 | Closing time is announced on the PA. Until then customers keep coming, and the shift can't end with one inside | [Karen.md §19.5](../agents/karen/Karen.md#195-closing-time) |
| 2026-11-12 | Fixing things in the backstreet (lights, fridges, cameras) takes a 20 s hold | [Karen.md §19.4](../agents/karen/Karen.md#194-fog-and-the-backstreets-fixes) |
| 2026-11-12 | Fog only in the cold room and the stockroom, seen only from inside | same |
| 2026-11-12 | The eyelids are open or shut, at a real blink's speed. The look is softer. The F1 map shows the building. The README has no controls and no build section | [CHANGELOG.md](../agents/CHANGELOG.md) |
| 2026-11-12 | Keep Karen's vision targets; bots must not use her spotlight as a perception cue | [tuning decisions](plans/0.2.0-tuning-plan.md#3-your-decisions) |
| 2026-11-12 | Keep playtest session data for six months; use tokenlimitlabs@gmail.com for deletion requests; keep developer-assigned testing rounds; nobody sends or types a tester code | [playtest decisions](plans/playtest-v2.md#3-questions-for-you) |
| 2026-11-12 | No per-model publication approval; training still starts manually on Windows and publication must pass the existing evaluation/compatibility checks | [agent handover](../agents/HANDOVER.md#12-november-owner-answers) |
| 2026-11-12 | Touch remains lethal outside Hunt; do not reduce Karen's sprint below 7.5 m/s | [tuning decisions](plans/0.2.0-tuning-plan.md#3-your-decisions) |
| 2026-11-12 | Use a free website address for now; choose the feedback questions for me; include optional nickname and new-build notice; no after-catch fairness prompt | [playtest decisions](plans/playtest-v2.md#3-questions-for-you) |
| 2026-11-12 | Testers get the game ZIP/link and illustrated instructions, with no typed code; sessions stay in the cloud and training runs later, manually, on a separate Windows computer | [playtest v2 build plan](plans/playtest-v2.md) |
| 2026-11-12 | After 0.4.0: 0.4.1 from Notes, vertical slice, finish the test build, then the remaining list | [NOW.md](NOW.md#next-in-order) |
| 2026-11-12 | Run 0.3.0's ablation again as 0.4.0's baseline | same, C0.1 |
| 2026-11-11 | The docs are split by reader: `docs/you/` for you, `docs/agents/` for agents | [RULES.md](../agents/RULES.md) |
| 2026-11-11 | Build Karen 0.4.0. Add fitted senses, a map of what you know about her, and a central model that keeps improving | [0.4.0 plan](plans/0.4.0-build-plan.md) |
| 2026-11-11 | No language-model layer in the 0.4.0 plan. A style vector and learned pacing stay ideas | [IDEAS.md](IDEAS.md) |
| 2026-11-10 | The long training runs come later, started by you | [0.4.0 plan §4.2](plans/0.4.0-build-plan.md#42-questions-only-you-can-answer), Q1 |
| 2026-11-10 | Basic should catch a player in 0.20–0.35 of shifts | same, Q2 |
| 2026-11-10 | Close the old motion model (PR #68) and reuse its tools | same, Q5 |
| 2026-11-10 | Clear every open PR; where two clash, the later one wins | [NOW.md](NOW.md) |
| 2026-11-08 | She finishes what she starts, unless she starts a hunt. No new chore once the doors close | [Karen.md §17](../agents/karen/Karen.md#17-karen-030-the-tricks-come-back) |
| 2026-11-07 | One builder (Claude) unless you say otherwise | [RULES.md](../agents/RULES.md) |
| 2026-11-06 | Karen learns from every player, and adapts to each one | [0.4.0 plan §6](plans/0.4.0-build-plan.md#6-part-2-everyone-and-you) |
| 2026-11-06 | Playtests run on Cloudflare only: no code, no form, no itch.io page | [playtest v2](plans/playtest-v2.md) |
| 2026-11-01 | Videos publish hands-off, from bot footage, with a 24-hour veto for you | [marketing agents plan](plans/marketing-agents.md) |
| 2026-10-10 | The game is Kehai and she is Karen. The old names stay dead | [RULES.md](../agents/RULES.md) |
| standing | Karen's ML work is planned first, then built as one PR | [RULES.md](../agents/RULES.md) |
| standing | Quick pass now, redesign before launch. Code, saves and tests stay solid | [RELEASE_PLAN.md](RELEASE_PLAN.md) |
| standing | The repository holds code and docs only | [RULES.md](../agents/RULES.md) |
