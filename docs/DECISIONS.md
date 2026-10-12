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
9. [Still open](#9-still-open)

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

**Open:** whether it becomes its own game mode or part of the store ([IDEAS.md](IDEAS.md#2-the-endless-maze));
shoppers, Karen and tasks in it; aisle signs; a floating origin for very long walks.

---

## 5. Karen plays fair

- **She knows only what she perceives:** sight, sound, traces, what customers tell her, the
  building's sensors, and places she looked and found nothing. Her guess of where you are is a
  probability map. **A test fails if her code reads the player's real position.**
- **Every trick has a warning** at least 0.8 s before it.
- **She can't outrun a sprint:** every pace is capped at 94% of your sprint speed.
- **A written warning always comes with a release window**, so she can't catch you again
  straight away.

**Why:** the game is about being sensed, not seen through walls. It also makes the AI tests
honest: an agent that beats her beats a fair opponent.

**Tested on the real game.** An agent plays through 17 actions using the real player body; a
150 s shift runs headless in 5–15 s. That's what the ablation runs on ([IDEAS.md](IDEAS.md)).

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

## 9. Still open

- **Karen's learning** doesn't beat planning alone against scripted players. It needs players who
  react to her tricks, or people ([KAREN_RESULTS.md](KAREN_RESULTS.md)).
- **Karen can stand on a stuck player** without catching them.
- **Hitches:** the GPU drawer's first growth (130–210 ms, once); the maze's NavMesh rebuild
  (about 20 ms per border crossed).
- **Memory:** the editor in Play with the maze reaches about 6 GB of the Mac's 8.
