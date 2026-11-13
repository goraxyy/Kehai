# Decisions

The big choices behind how Kehai is built: what we picked, why, and what it gave us. Numbers
are from the dev Mac (M2, 8 GB), in the editor, at 1080p.

1. [The repository holds code only](#1-the-repository-holds-code-only)
2. [Rendering](#2-rendering)
3. [Shelves as data](#3-shelves-as-data)
4. [The endless maze](#4-the-endless-maze)
5. [Karen plays fair](#5-karen-plays-fair)
6. [Replays record, they don't re-simulate](#6-replays-record-they-dont-re-simulate)
7. [Quality without rebuilding the pipeline](#7-quality-without-rebuilding-the-pipeline)
8. [The pipelines](#8-the-pipelines)
9. [Reproducible interior models and safe room circulation](#9-reproducible-interior-models-and-safe-room-circulation)
10. [Karen 0.2.0](#10-karen-020)
11. [Still open](#11-still-open)

---

## 1. The repository holds code only

Git gets scripts (with their `.meta` files) and documents. Scenes, models, textures, audio and
project settings stay on the Mac: they're big, binary, and some art isn't ours to publish.

**So** anything that isn't art is made in code: Karen installs herself when the store loads, the
menus and HUD are built in code, and the product catalogue, the planogram and the store layout
are C#. **The cost:** scene wiring can't be reviewed, so settings that matter are guarded by
tests (one fails if the GPU drawer below is switched off).

---

## 2. Rendering

**The GPU Resident Drawer, with GPU occlusion culling (#34).** Unity's newer drawer sends
ordinary meshes to the GPU in batches, and occlusion culling skips whatever the shelves hide.

| | camera cost, mean of 4 views |
|---|---|
| before | 18.9 ms |
| GPU Resident Drawer | 5.1 ms |
| + GPU occlusion culling | **4.5 ms** |

Chosen over hand-made instancing or static batching, which can't skip hidden objects one by one.

**Each room lit by its own lights (#30, #31).** 240 ceiling lights with shadows would be too
slow, so without shadows they shone through walls. Each room is now on its own rendering layer,
and its lights only reach that layer.

**Still leaking, found by turning one room's lights off and looking (#52).** Three ways light
still got in:
- The layers were per part of the building, not per walled room. The storage room and the cold
  room are both "Stockroom", so the cold room's lights (17 m reach) lit the storage room through
  the wall between them, and the same between the two staff rooms. Every walled room StoreMap
  finds now has its own layer; there are seven, which is exactly the named layers URP keeps.
- Which room a wall faces came from the area table, whose lines don't sit on the walls: the west
  wall is half a metre inside the table's line, so its inner face was counted as outside, lit by
  the sun and not by its room. Each face now takes the room of the nearest floor on its side.
- An outside wall keeps the outside layer, for its outer face, only if the sun can reach that
  face. The west and north walls' outer faces never see the sun; on them the layer only let the
  sun light the inner face.
Rooms with their lights off now measure as dark as with the sun switched off too.

**How we measure.** The same 4 views every time (a long run, the drinks aisle, produce from the
door, the back wall), best of 3. Two rules learned the hard way: let a few frames pass before
timing (the drawer picks objects up a frame late), and take a picture with every measurement
(it proves the thing timed was actually drawn).

---

## 3. Shelves as data

**Before (#30):** 16,500 shelf slots, each a GameObject with a collider and a script, plus a
GameObject for every item on them: about 33,000 objects. The scene was 92 MB and took 16 s to
save.

**Now (#35, #36):** a slot is a plain record (`ShelfSlot`), rebuilt at load from the planogram,
which is code. An item becomes a real object only when someone takes it off a shelf, and goes
back to being data when it's put back. Nothing about the stock is saved in the scene: **92 MB →
6.2 MB.**

**Drawing the stock: three tries.**

| how | extra cost |
|---|---|
| instanced draw calls per cell (#35) | 7.3 ms |
| one instanced call per product (#38) | 4.2–4.9 ms |
| **one bare render object per slot, drawn by the GPU Resident Drawer (#39, #40)** | **2.2–2.4 ms** |

Instanced calls lost because they draw everything in view, even behind two rows of shelves.
Gotcha: copies made with `InstantiateAsync` are never picked up by the drawer (19.6 ms); plain
`Instantiate` works.

**Aiming without colliders.** The player's ray is tested against the boxes of nearby slots
(`ShelfAim`) instead of 16,500 trigger colliders.

**Which way a shelf faces comes from the model.** The shelf code used to guess: front and back by
each bay's own middle. Pillars where a perpendicular bay joins the run then stocked a row facing
into that bay, and the "b" and "E" end pieces faced their own back panel. The models already
mark the sides that sell, with their price strips, so the strips decide. Gotcha: in Play the
shelves are statically batched, and every strip's mesh becomes the scene's combined mesh, so its
place is read from its renderer's box instead.

**Planned by aisle, not by bay.** Each bay used to pick one of its section's two layouts from
its position, front and back by the way it was turned, so one side of an aisle changed product
at every bay, and at every pillar turned the other way. Now each side of an aisle is planned as
one run of brand blocks.

---

## 4. The endless maze

Built in #37, in its own scene (**Kehai → Endless → Open an Endless Maze**). **Parked** for
now: it's last in the build list and left out of builds.

**Seeded chunks.** The world is a grid of 25 m chunks. Each chunk is worked out from
`hash(seed, x, z)` alone, so the maze never ends, comes back the same when you return, and a
seed is a whole world (which the AI tests need).

**The generator.**
1. Each border between two chunks is decided by the border's own hash, so both sides agree.
2. Inside, Kruskal's algorithm opens just enough walls to join every cell.
3. **Braiding:** most dead ends get an extra opening, so it's mostly loops. A chase with no way
   round is frustrating, not tense, and Karen's herding needs loops.

Tested: every cell reachable, under 8% dead ends (3% measured), the same seed gives the same
chunk.

**Streaming.** Chunks near the player are built one per frame and taken down when far away.
Shelves, lights and floors come from pools. A chunk you leave keeps only what changed. One
NavMesh follows the player, built off the main thread. Crossing a border first stalled for up
to 240 ms; five fixes brought it to 35–55 ms, a few frames at most.

**At full size:** 25 chunks, 96,000 slots, 626 lights, 9.2 ms.

**Open:** whether it becomes its own game mode or part of the store ([IDEAS.md](../you/IDEAS.md#2-the-endless-maze));
shoppers, Karen and tasks in it; aisle signs; a floating origin for very long walks.

---

## 5. Karen plays fair

- **She knows only what she perceives:** sight, sound, traces, what customers tell her, the
  building's sensors, and places she looked and found nothing. Her guess of where you are is a
  probability map. **A test fails if her code reads the player's real position.**
- **She works only inside** (0.2.0): the building, the backstreet and the pavement. She never
  hunts you outside.
- **No trick warns you first any more** (0.2.0). Until then, every trick played a warning at
  least 0.8 s before it acted. Why it went: [§10](#10-karen-020).
- **She's faster than you in a hunt:** 7.5 m/s against your 6.5 since 0.2.0 (it was 1.05 ×, 6.8
  m/s, from 2026-10-16). She gains a metre a second, so breaking her line of sight is how you get
  away.
- **Her touch kills** (changed 2026-10-16), and the shift starts again, so she can't catch you
  again straight away.
- **Cover is eye to eye** (0.4.1, the owner's call). An eye sees you only along a clear line to
  your eyes. Before, any of three rays (feet, chest, head) would do, so feet showing under a rack
  gave you away and nobody could say what "hidden" meant. One rule a player can reason about:
  can she see my eyes?
- **The store's cameras are screens, not eyes** (0.4.1). They report to her only while she
  stands at the office screens (Camera watch). Always-on, they would have been six permanent
  eyes and the trick would mean nothing.
- **Her eyes were doubled without a test** (0.4.1, the owner's call: "still improve her eyes,
  even with no tests yet"): 72 m, half strength at 32 m, gain 3.3. 0.3.0's ablation baseline and
  0.4.0's fitted senses used the old eyes. Open until an ablation runs with the new ones.

**Why:** the game is about being sensed, not seen through walls. It also makes the AI tests
honest: an agent that beats her beats a fair opponent.

**Tested on the real game.** An agent plays through 18 actions using the real player body; a
150 s shift runs headless in 5–15 s. That's what the ablation runs on ([IDEAS.md](../you/IDEAS.md)).

---

## 6. Replays record, they don't re-simulate

A replay saves what happened (positions, events, lights, her belief map), not the inputs.
Re-simulating would need the whole game to be perfectly deterministic. A 6-minute shift is about
380 KB. To play it back, the real store loads with every game script switched off and the
recording moves things, so a replay looks exactly like the game.

---

## 7. Quality without rebuilding the pipeline

**Before (#48):** Low and Medium were a tuned copy of the pipeline asset, swapped in. Unity tears
the whole render pipeline down and builds it again for that, here from inside the settings
page's drawing: the aisle signs' Japanese went missing and stayed missing, and the shorter
shadows let room lights through the walls. **Now:** render scale, anti-aliasing and the sun's
shadow resolution are set on the asset in use, which needs no rebuild; the shadows' reach is
left alone. In the editor the asset's own values go back when Play stops.

---

## 8. The pipelines

- **launchd, not a server.** The marketing and playtest jobs run on macOS's own scheduler. A
  time the Mac slept through runs when it wakes, and nothing stays in memory (8 GB).
- **Limits built in.** Every Claude call is priced and checked against a $15 monthly cap. One
  heavy job at a time. Every step has a dry run.
- **Playtest uploads can't leak.** Sessions go to a private Cloudflare bucket. The key inside
  testers' builds can only add files; reading and deleting need a key that stays on the Mac.

---

## 9. Reproducible interior models and safe room circulation

The staff room and office use generated low-poly meshes and local prefab assets, with their
recipes committed as C#. Rigid parts are combined by material; moving leaves stay separate.
Furniture is authored in metres and placed against measured wall faces. The model island uses
fixed category plots with walking aisles and reserved space for later rooms.

Locker/cabin interaction uses the aimed E system and checks the door sweep for obstructions.
Room doors retain the existing HingeDoor integration for Karen, locking and recording.
Existing coffee, radio, punch-clock and tool objects keep their gameplay references.

**Problem: a lowered ceiling broke the ground-floor map.** Its upper face entered the navigation
bake, moving the navigation bounds' midpoint above the floor. Map sampling then found only a
handful of cells. Ceilings are now excluded from the bake, and StoreMap samples at the lowest
navigation level. The added third west room also requires another named rendering layer so
its lights can stay separate. Details and limits: [INTERIOR_MODELS.md](store/INTERIOR_MODELS.md).

---

## 10. Karen 0.2.0

A redesign of how Karen sees, where she works, how she hunts and how she learns. The plan is
[`0.2.0-build-plan.md`](archive/0.2.0-build-plan.md), the reasoning
[`0.2.0-design-notes.md`](archive/0.2.0-design-notes.md), and what changed in the code
[Karen.md §16](karen/Karen.md#16-karen-020). The "what it gave" column is what the tests show;
the ablation over whole shifts is below the table.

| Decision | Why | The cost | What it gave so far |
|---|---|---|---|
| **She works only inside**, and outside is one cell of her belief that remembers which exit you used | Over half her map (56% of 4,708 cells) was the street, parking lot and alleys. She could sweep the parking lot while you worked inside | She can't follow you out; she sets up for your return instead | Her grid shrinks to about 2,000–2,700 cells. Tests: she never picks a spot outside, and her body can't walk round the building |
| **Guesses at your route, not a second grid** (D1) | Cut, Throw, Ambush and Hunt need a spot and a time. A grid run forward blurs into no spot to race to | Only during a hunt, and only 3–6 guesses | Tests: the right guess comes out on top, turning back raises "turned back", ruled-out guesses fall |
| **Hunt sprints at 7.5 m/s**, keeps running on her guesses, and the chase and Follow merge into it | At 1.05 × your sprint a chase in the open barely closed, and she stopped where she last saw you | A corner or a door has to save you. Hunt is paced harder: 0.6 tension, a 90 s cooldown, never while you settle in or in a recovery | Tests cover each pacing and hand-over rule |
| **Cut and Throw**, two new tricks inside Hunt | Hunting on guesses needs a way to use them: get ahead of you, or slow you | Throw had to be capped so an item alone never makes a catch certain; its ranges come from that rule | Cut rarely fires: never on a 25 m dash, and on 45–60 m runs it's closer in 1 of 8 starts, never worse. A first version did worse than the plain sprint |
| **A wider, longer cone, with a lower gain** | Awareness felt slow; the targets are seen within 0.7 s at 10 m and 1.5 s at 15 m off-centre, and never crouched in the dim | Long aisles stop being safe, and her "nothing here" sweep had to grow: 1.3 ms and 108 rays a sweep in the editor | Every target met: 0.67 s, 1.40 s, never. Over whole shifts she sees the bot within about 9 s, against 31–36 s before |
| **No warnings** (Q17) | The owner chose surprise over the promise. A warning's 1 s was a 6.5 m head start | Fairer games warn before what can kill you, which was the recommended option; with none, a catch can come from nowhere. The ablation counts catches you never saw coming | The sounds stay as atmosphere |
| **Most tricks become drafts:** kept in the code, off in play | The owner's short list of tricks to rework or remove. Kept in the library, so ids, Ledger records and rung F's numbering don't move | The learner has little to choose from until they return; Corner, Loom and Lure have no live trick, and tier 4 is empty | 13 of 36 live, 14 since Block door went live at sync 2 (6 November). Ambush now unlocks on shift 3 (was 8), so it runs in tests for the first time |
| **Combos learned, not built in** | Combos welded into tricks (Blackout's wait, Funnel) can't be judged apart from their parts | The combo room starts empty and has to earn every chain | In a planted test world she finds the planted chain in every seed by shift 3–5, finds none where none pays, and earns more than without it (+0.23 a shift, SE 0.017). Not yet shown in the store |
| **Her footsteps and floor cone for the developer only** | The bots heard her steps within 12 m, so the tests measured an easier game than players get | Players have little left to notice her by: seeing her, and the sounds of her tricks | The bots hear what players hear |

**What the ablation found** (6 November, against `main`, paired by seed;
[KAREN_RESULTS.md](results/KAREN_RESULTS.md#the-ablation-results-6-november)):
- **Together, the changes make her far deadlier:** a catch in 94–100% of shifts against 28–54%,
  for every simulated player, the held-out one included. A bot finished its shift in 1 of 144
  shifts, against 18 on `main`. She sees it within about 9 s, against 32–61 s.
- **What holds:** 0 fairness violations in 379 shifts, and 0 s outside her area.
- **What hardly runs:** Cut never fired, Throw is rare (20 in 144 shifts), and the combo learner
  found no combo. Shifts end in a catch about 100 s in, before those get a chance.
- **What couldn't be measured:** the gap 5 s after she loses you, because she almost never does.

---

## 11. Still open

- **Karen 0.2.0 is too deadly against the simulated players.** How far to turn her down, and how,
  is open ([§10](#10-karen-020)); a playtest would show how people fare.
- **Karen's learning** doesn't beat planning alone against scripted players, even now that they
  react to her tricks: it calms her slightly and tailors a little to the player, but doesn't track
  the setpoint better. It needs people, or a trained policy ([KAREN_RESULTS.md](results/KAREN_RESULTS.md)).
- **Karen 0.3.0 can make the shift unfinishable:** after the doors close she keeps making chores
  (spills, shelf sweeps, bins, Tool theft) faster than they can be cleared, and the clock-out
  waits for every one. 13 of 240 test shifts got stuck, against 2 on `main`
  ([KAREN_RESULTS.md](results/KAREN_RESULTS.md#karen-030-the-ablation-7-november)). **Decided
  8 November (the owner):** no new chore once the doors close, rule 10
  ([Karen.md §17.3](karen/Karen.md#173-the-new-fairness-rules)). Built; the ablation runs
  again to check it.
- **Karen's hands-on tricks rarely landed in play** (0.3.0's second run). She re-decided on her
  walk to the spot and dropped the trick: Spill landed 1 time in 55 starts. **Decided 8 November
  (the owner):** she finishes what she starts, unless she starts a hunt
  ([Karen.md §17.3](karen/Karen.md#173-the-new-fairness-rules)).
- **A body can fall through the floor in Aisle 4/B** (the test player did, once in 83 shifts), and
  **0.3.0 runs 3.6 times slower** than `main` in the tests, for reasons not yet found.
- **Karen can stand on a stuck player** without catching them.
- **Hitches:** the GPU drawer's first growth (130–210 ms, once); the maze's NavMesh rebuild
  (about 20 ms per border crossed).
- **Memory:** the editor in Play with the maze reaches about 6 GB of the Mac's 8.
