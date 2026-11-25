# Your decisions

Your calls, one dated line each, newest first. Agents add a line when you decide something, then
bring the detailed docs in line. The technical choices and their reasons are in
[`docs/agents/DECISIONS.md`](../agents/DECISIONS.md).

| Date | Your call | Detail |
|---|---|---|
| 2026-11-25 | Merge the whole-store mini store without the full test suite again (the mini store check passed); the suite still waits for the next merge | [mini store](plans/0.5.2/mini-store.md) |
| 2026-11-25 | The mini store becomes the whole store at half size: lobby, sales floor, WC, office, staff room, backstreet, stockroom, cold room (not the parking lot), with every thing the tricks and interactions need; the plan's Phase 11 is the mini store only | [mini store](plans/0.5.2/mini-store.md) |
| 2026-11-24 | Merge the mini store without the full test suite this once (no time to keep Unity closed); the suite runs before the next merge | [mini store](plans/0.5.2/mini-store.md) |
| 2026-11-24 | The island is replaced by one mini store sandbox: three rooms (sales floor, cold room, stockroom) from the store's own models, a new building, the real Karen, and a panel to allow her no tricks, one trick or all, and to switch what she can sense | [mini store](plans/0.5.2/mini-store.md) |
| 2026-11-23 | A shelf face you can't stand in front of is display only: it stays stocked, and shoppers and Karen's tricks never empty it, so no task asks for it (freezer 4 too, by name) | [placement](plans/0.5.2/placement.md#4-where-it-stands-23-november), C12.2 |
| 2026-11-23 | Shelves are measured at the board you put stock on (at most 1.6 m), not at the middle of the pack standing on it; everything else keeps the centre rule | [placement](plans/0.5.2/placement.md#1-the-rule), C12.2 |
| 2026-11-23 | The power box's three switches are labelled EAST, WEST and BACK, and each prompt names the lights it runs (Q25: label them) | [placement](plans/0.5.2/placement.md), C12.3 |
| 2026-11-21 | The squashed key map: seven F keys (F1 map with her cone, F2 replay, F4–F6 sandbox, F7 clip, F8 one webcam panel); F3, F9, F10 freed | [sandboxes, section 8](plans/0.5.2/sandboxes.md#8-the-sandbox-island-in-the-store-scene-your-call-21-november) |
| 2026-11-21 | The sandboxes move into the store scene as one island beside the store, the models island beside it, walkable between all three; the store is quiet there (no shift, her store self off); she gets a separate test map of the island; sandbox keys stop clashing, and the keys get sorted and squashed | [sandboxes, section 8](plans/0.5.2/sandboxes.md#8-the-sandbox-island-in-the-store-scene-your-call-21-november) |
| 2026-11-21 | The staff room's radio comes down to 1.4 m on its wall, moved in code at load (it sat at 2.1 m, above eye level) | [0.5.2 plan](plans/0.5.2-plan.md), C12.2 |
| 2026-11-21 | Your focus for 0.5.2: sandboxes for sight, pursuit, tricks and interactions; the named interactions (seen hiding, fridge check, kneel check, hit stun); usable things within reach; the scenes; training; the test pipeline and draft builds. Build the sight sandbox, then pursuit, and keep building | [0.5.2 plan](plans/0.5.2-plan.md) |
| 2026-11-21 | The Karens' names: Hotaru (an easy mode, later), Satori (the game's, `basic`), Oni (the catcher, `vital`); code ids unchanged | [0.5.2 plan](plans/0.5.2-plan.md), Q21 |
| 2026-11-21 | 0.5.2: start now, one PR; test everything once after it merges, recording every test shift. Hunt the batch slowdown first; usage-limit rules into RULES.md; game data by direct copy; Git LFS in private repos with locks; four scenes as a base and layers (Testing, Training, a shared Workshop, dev layers); camera, customer and torch interactions, with what she saw counting for more | [0.5.2 plan](plans/0.5.2-plan.md), Q1–Q20 |
| 2026-11-17 | 0.5.1 ships visible hiding; 0.5.2 extends it | [0.5.2 plan](plans/0.5.2-plan.md), Q12 |
| 2026-11-16 | Finish all fourteen 0.5.1 items now, in one PR from branch `0.5.1` built on the saved 15 November work; Opus for Karen, doors and burnout, Sonnet for the smaller items; no training or long runs | [0.5.1 plan](plans/0.5.1-plan.md) |
| 2026-11-15 | Start 0.5.1 with Astra and extra sight supervision: low shelves do not conceal, hiding interiors and 80% under-stock-shelf coverage do; witnessed hiding allows approach and a catch within 3 m. Place fixtures logically and label clock/puncher and other interactables | [0.5.1 plan](plans/0.5.1-plan.md) |
| 2026-11-15 | Create a 0.5.1 branch and planning PR for fourteen playtest fixes; no building yet. Karen approaches witnessed hiding and catches if reachable; manual doors open both ways and E reverses a closing door | [0.5.1 plan](plans/0.5.1-plan.md) |
| 2026-11-14 | Merge 0.5.0 now with the completed correctness checks; defer further playtesting, platform builds, visual review, remaining setup and long training until later. Keep unverified acceptance tasks open | [build status](plans/0.5.0/implementation-status.md) |
| 2026-11-14 | 0.5.0 uses adaptive trained bots with within-shift memory and across-shift sensed history; no language-model players or judges | [0.5.0](plans/0.5.0/agents.md) |
| 2026-11-14 | Escape learning combines consented central refits on Windows with local counts settled at shift end; no local neural training or player warning | [0.5.0](plans/0.5.0/build-decisions.md) |
| 2026-11-14 | Keep Racetrack, Labyrinth, Fishbone, Rings, Split and Brick bond; train on the first, second, third and sixth; reserve Rings/Split | [0.5.0](plans/0.5.0/maze-and-karen.md) |
| 2026-11-14 | Four successful shifts; Gameplay Karen absent on shift 1, Testing present; today’s map at 1–2, Labyrinth at 3, Brick bond at 4 | [0.5.0](plans/0.5.0/build-decisions.md) |
| 2026-11-14 | Flash sale raises only the shopper cap to 4; end it with a 20-second staff-room microphone hold; add screen loop, room lights, freezer alarm and complaint | [0.5.0](plans/0.5.0/tricks.md) |
| 2026-11-14 | First-shift guidance is text, outlines and prompts only; tired/wry lines, first shift only | [0.5.0](plans/0.5.0/first-shift-lines.md) |
| 2026-11-14 | One gameplay project produces Training, Testing and Gameplay; the realistic Look Lab is a separate private external project with simple products | [0.5.0](plans/0.5.0/versions.md) |
| 2026-11-14 | Progress lives on an owner-only private summary; no mandatory Projects board; unavailable accounts remain unavailable | [0.5.0](plans/0.5.0/dashboard.md) |
| 2026-11-14 | Authorise the complete 0.5.0 implementation in one PR, using suitable models and parallel workers; long training remains after 0.5.0 | [build decisions](plans/0.5.0/build-decisions.md) |
| 2026-11-14 | Approve the implemented 0.5.0 scope and benchmark design; update/open Unity on `0.5.0` and proceed with correctness checks. Long training still waits | [benchmark](plans/0.5.0/benchmark.md) |
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
