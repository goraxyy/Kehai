# Kehai — Research & Systems Ideas

Working notes for the route we picked: **Kehai as an agent-eval environment (1)**,
**Aiko with ablations (2)**, and an **interpretable thought log (4)** — plus the infinite
maze and the blink mechanic that feed into them.

Companion documents: [`Aiko.md`](Aiko.md) is the antagonist design spec,
[`RELEASE_PLAN.md`](../production/RELEASE_PLAN.md) is the production route. This file is the part
aimed at a research audience.

> **Status.** Everything here except the infinite maze is built: the eval environment and
> its clients ([`tools/eval/`](../../tools/eval/README.md)), all six Aiko rungs and the ablation
> runner (results in [`AIKO_RESULTS.md`](../results/AIKO_RESULTS.md)), the thought log with its
> overlay and replay scrubber, and the blink pipeline from keyboard to webcam
> ([`tools/blink/`](../../tools/blink/README.md)). The store itself is exported for people and
> agents in [`STORE_MAP.md`](STORE_MAP.md). Where each piece lives: `Aiko.md` §15. The infinite
> maze, and far more stock, are planned in
> [Scaling](#scaling-a-lot-of-stock-and-a-maze-with-no-end): steps 1 and 2 of 3 are built.

---

## The through-line

These three are not separate projects. They compose:

```
        infinite maze  ──────────────► procedural variation
                                              │  (stops agents memorising layouts)
                                              ▼
   ┌──────────────────────────────────────────────────────┐
   │   1. Kehai as an eval environment                   │
   │      observation · action space · task metrics        │
   └────────────────────┬─────────────────────────────────┘
                        │  the same interface
                        ▼
   ┌──────────────────────────────────────────────────────┐
   │   2. Aiko  ──► ablations ──► results table            │
   └────────────────────┬─────────────────────────────────┘
                        │  emits, every decision
                        ▼
   ┌──────────────────────────────────────────────────────┐
   │   4. Thought log  ──► replayable, scrubbable          │
   └──────────────────────────────────────────────────────┘
                        ▲
        blink mechanic ─┘  asymmetric information channel
```

The eval interface is the keystone. Build it first and Aiko gets a measurement harness
for free; build it last and you will retrofit everything.

---

## 1. Kehai as an agent-eval environment

### Why this one is worth the most

Labs are short of long-horizon, multi-objective, *embodied* eval environments. Most
agent benchmarks are text or single-task. Kehai's structure is unusually well suited:
tasks **interfere** with each other. A spill happens while you are restocking. The queue
builds while you mop. The shift clock runs through all of it. Current agents fail
specifically at interleaving under time pressure, and this environment produces that
failure naturally rather than artificially.

### What has to exist

- [x] **Observation** — a serializable snapshot of world state. Start with structured
      text (agents read it far better than pixels, and it keeps the loop fast):
      position, facing, held items, visible shelves and their fill state, spills, queue
      length and wait times, bin fullness, burnout, time remaining, task list
      → `EnvWorld.Observe`: JSON plus a prose rendering; the HUD as the player sees it
      (lies included), Aiko only when in view, her footsteps only when close
- [x] **Action space** — a discrete verb set matching what the player can do:
      `move_to(target)`, `pick_up(item)`, `place_on(slot)`, `mop(spill)`, `serve(customer)`,
      `bag_trash(bin)`, `dispose(bag)`, `drink_coffee()`, `clock_out()`
      → 17 verbs in `KehaiEnv.Act`, each run through the real body (`AgentDriver` drives
      `PlayerMotor` and `PlayerInteract`, so walls, doors, stamina and noise are the player's)
- [x] **Step/reset** — run a shift headless, deterministically, from a seed
      → `KehaiEnv.ResetEpisode`/`Act`, over a local socket via `EnvServer`
- [x] **Metrics** — shift completion, tasks completed, customers lost, spills left
      standing, average customer wait, burnout at clock-out, wall-clock and step count
      → `EpisodeMetrics`, plus Aiko's side and the failure taxonomy below
- [~] **Determinism** — same seed produces the same shift. Non-negotiable for ablations
      → one seed fixes `UnityEngine.Random` and Aiko's RNG, `Time.captureDeltaTime` fixes every
      frame, scene reloads are synchronous and the planner has no wall-clock cut-off in eval runs.
      Verified: the first shift after launch replays identically for the same seed. Not yet:
      later shifts in the same process drift (engine-side — NavMesh carving and crowd updates run
      on worker threads), so rungs are compared on paired seeds and means over several careers
- [x] **Headless + time-scaled** — evaluation cannot run at 1× real time
      → `-batchmode -nographics`; a 150 s shift runs in 5–15 s of wall time

### Design notes

**Text observations first, pixels later.** Text keeps iteration fast and isolates
*planning* failure from *perception* failure. A pixel mode is a good second paper, not a
first one.

**Run faster than real time.** `Time.timeScale` plus a headless batch-mode build. If a
shift is 5 minutes at 1×, you need 50–100 shifts per config for an ablation; at 20× that
is an afternoon instead of a week.

**Fixed seeds per difficulty tier.** Hold a held-out set of seeds never used during
development, or you will tune against your own test set.

### The artifact

A repo with the harness, a results table across models, and — the part that actually
signals maturity — **a failure taxonomy**. Categories worth reporting:

| Failure mode | What it looks like |
|---|---|
| Starvation | Fixates on one task, ignores the others until the clock runs out |
| Thrashing | Switches task every few steps, completes nothing |
| Interference blindness | Restocks a shelf while a customer times out at the till |
| Clock blindness | Fails to reserve time for the clock-out walk |
| Resource mismanagement | Runs burnout to empty, loses sprint, misses everything |
| Spurious completion | Clocks out believing tasks are done when they are not |

"Model X completes 6/10 shifts; failures are 70% interference blindness" is a far more
interesting sentence than a score.

→ `FailureTaxonomy` labels every episode from its action trace. `tools/eval/run_baseline.py`
is a scripted floor; `tools/eval/llm_agent.py` has Claude play a shift through one `act` tool.

---

## 2. Aiko with ablations

Full design in [`Aiko.md`](Aiko.md). The research contribution is not the antagonist —
it is **evidence that the adaptation does something**.

### The ablation ladder

Each rung adds exactly one mechanism, so any difference is attributable:

| # | Configuration | Tests |
|---|---|---|
| A | Random patrol | Floor |
| B | Scripted patrol, fixed routes | Does structure beat noise? |
| C | + belief grid (occupancy, negative information) | Does modelling where you *aren't* help? |
| D | + bandit tactic selection | Does within-shift learning help? |
| E | + persistent Ledger across shifts | Does across-shift learning help? |
| F | + blink channel | Does perceptual asymmetry help? |

### Metrics

Pick before running, not after:

- Time-to-first-detection
- Shift completion rate (the player's, under each config)
- Tactic diversity (entropy over the tactic distribution — catches degenerate camping)
- Panic Index trace (from `Aiko.md`) — the intended reward signal
- Player-reported tension, 1–5, if you run humans

### The trap to avoid

It is very easy to spend three months building Aiko and have zero numbers. **Commit to
producing the table at rung C** — belief grid vs scripted — before building D and E.
A two-rung ablation that exists beats a six-rung one that does not.

### Scope decision, unresolved

`Aiko.md` is more ambitious than everything shipped so far combined, and is currently
zero lines of code. Decide explicitly which slice ships. My suggestion: rungs B–D are a
complete, defensible story on their own.

→ **Resolved: all six rungs ship**, switched by `AikoRung` (`-aiko-rung A..F`). The ladder is
run end to end by `AblationRunner` against three simulated players (efficient, skittish,
reckless), paired by seed so rungs see the same shifts; the table is in `AIKO_RESULTS.md`.

---

## 4. Interpretable thought log

### What it emits

One structured record per decision, not prose:

```json
{
  "t": 184.3, "shift": 3,
  "belief": { "player_most_likely": "aisle_7", "confidence": 0.62,
              "ruled_out": ["backroom", "aisle_1..3"] },
  "options": [
    { "tactic": "sweep_aisle_7", "utility": 0.71, "why": "highest posterior" },
    { "tactic": "camp_coffee",   "utility": 0.66, "why": "burnout low, likely visit" },
    { "tactic": "cut_power",     "utility": 0.44, "why": "pacing setpoint below target" }
  ],
  "chose": "camp_coffee",
  "because": "sweep was tried twice this shift and failed; bandit down-weighted it",
  "panic_index": 0.38, "target": 0.55
}
```

### Why it is worth building early

- It makes the ablations **legible** — you can see *why* rung D beats rung C, not just that it does
- It is the debugging tool for Aiko; you will want it regardless
- It doubles as a player-facing feature: scrub the shift, watch what it was thinking
- Interpretability framing lands well with the audience we are aiming at

### Build notes

- Emit to a ring buffer in memory, flush to JSONL on shift end
- Keep it structured — the temptation is to write sentences, but sentences cannot be plotted
- `because` is the one free-text field, and it should name the *mechanism*, not narrate
- A replay scrubber over the log is a weekend of UI and enormously worth it

→ Built as specified: `ThoughtLog` (ring buffer, JSONL per shift in `aiko_logs/`), F1 for the
live overlay, F2 for the replay scrubber (with belief snapshots and where the player really
was), and the post-shift performance review that reads from it.

---

## Scaling: a lot of stock, and a maze with no end

The shop is meant to hold far more stock than it does now, and the maze is meant to extend
without limit. That changes how stock has to exist in the game. This is the plan, in three
steps, each one measured.

### Where it started (2026-10-07)

- **Shelves.** 189 bays, cut into 16,502 slots ([`MERCHANDISING.md`](MERCHANDISING.md) §4).
  Each slot was a GameObject with a trigger collider. Each item on it was another, with a
  renderer, a rigidbody and a collider. That's about 33,000 objects for the stock alone.
- **The scene.** 92 MB on disk, and 16 s to save.
- **Lookups.** Each time a shopper looked for something, it scanned all 16,502 slots. The
  replay recorder scanned them all on every tick.
- **Drawing.** The render cost was 18.9 ms on average. That is the camera render alone in the
  editor at 1080p, over four fixed views: down a long run, the drinks aisle, produce from the
  door, and along the back wall.

### Step 1: the GPU Resident Drawer (done)

URP can draw ordinary MeshRenderers through BatchRendererGroup instancing. It culls on the GPU
and, with occlusion culling on, skips what the shelves hide.

| | render cost, mean of the four views |
|---|---|
| off | 18.9 ms |
| on | 5.1 ms |
| on, with GPU occlusion culling | **4.5 ms** |

Pictures with it on and off are identical, and the room lighting measures the same.

**Where the settings live.** They're local project settings, which never go to git:

- `PC_RPAsset`: the GPU Resident Drawer set to Instanced Drawing, with GPU occlusion
  culling on.
- Graphics settings: *BatchRendererGroup Variants* set to **Keep All**. Without it the
  editor quietly leaves the drawer off, and builds lack its shaders.

`PerformanceSettingsTests` fails if any of these is turned off.

**What it doesn't fix.** The drawer makes the objects cheap to draw, but they still exist: the
memory, the scene's size and load time, the linear lookups. A streamed maze can't be made of
33,000 hand-placed objects.

### Step 2: shelves as data (built)

Stock stops being GameObjects. A slot becomes a record:

- where it is and which way it faces;
- what the planogram wants on it;
- what is on it now.

Each bay owns its slots. The things that need GameObjects get them only while they need them:

- **Drawing.** Each stocked slot gets a bare render object: the product's mesh and
  materials, with no collider or script, hidden and never saved. Copies come from one
  template per product and room, and the GPU Resident Drawer draws them, culling them one by
  one, hidden ones included. It works in the editor too, so the shelves look stocked there
  without baking anything into the scene.

  The first version drew each product with instanced calls (`Graphics.RenderMeshInstanced`),
  per 10 m cell and then per view. In Play that cost 5 to 7 ms over the four views, because
  instanced calls can't skip what the shelves hide. The render objects cost 2.4 ms (4.8 ms
  against 2.4 ms with no stock, measured with frames between switching it and timing it).
  They're made with `Instantiate`, 0.6 s for all 16,500: copies made by `InstantiateAsync` are
  never taken up by the GPU Resident Drawer, and cost 17 ms drawn the ordinary way.
- **Aiming.** The player's ray finds the first solid thing in reach. Then it's tested against
  the slot boxes on its way there, taken from a spatial grid. No slot needs a collider.
- **Items in hand.** An item becomes a GameObject only when it leaves a shelf: taken by the
  player or a shopper, or knocked off by Aiko. It's made at the slot's exact pose. Put back on
  a shelf, it's destroyed and the slot records what's on it. (A pool turned out not to be
  needed: items leave shelves a few times a minute, not a few hundred times a second.)
- **Lookups.** A spatial grid and per-product lists replace the linear scans. Replays record
  slot changes from a change list instead of reading every slot.
- **Migration.** One editor pass clears what the old way left in the scene:
  - the baked `GridSlots`;
  - the shelf prefabs' unused six-to-a-board slots;
  - the till counter's slot, which becomes a marker for a data slot.

**Done when:**

- no GameObject stands for shelved stock;
- the scene is back near 20 MB;
- the render cost is no worse than step 1's;
- shoppers, Aiko's shelf sweep, the eval's restock action and replays behave as before, by
  their tests and by a Play-mode check.

**Where it got to (2026-10-07).** Built (`ShelfSlot`, `ShelfStock`, `ShelfDrawer`, `ShelfAim`):

- **Shelved stock.** No GameObject stands for it. The editor shows the shelves stocked from
  the planogram, with nothing baked.
- **The scene.** It went from 92 MB to 6.2 MB, and the whole scene is now 4,662 GameObjects.
  It's smaller than before the grid (19.5 MB), because the shelf prefabs' unused slots went
  too.
- **Tests.** All 128 EditMode tests pass, including the new `ShelfStockTests`: aiming, taking
  and putting back, a bay counting its empty slots. Both replay tests pass too: a bot shift
  recorded and played back, and the 10-second round trip.

Still to measure: the render cost against step 1's 4.5 ms, and a Play-mode check of the
player, shoppers and Aiko's shelf sweep. The editor stopped answering the tools that drive it
before those ran.

### Step 3: chunks, then a maze without an end

This builds the design in [Infinite maze](#infinite-maze) below on top of step 2.

- **3a. Chunks.** The floor is cut into 25 m chunks, and shelf data, drawing and lookups are
  kept per chunk. A chunk's stock is generated from its position (the planogram already is).
  Only changes from that default are stored, so a chunk can be dropped and rebuilt exactly as
  it was left.
- **3b. A seeded maze.** A chunk's bays come from `hash(seed, chunk_x, chunk_z)`:
  - edge-matched tiles, so aisles meet across chunk edges;
  - braided, so there are loops and few dead ends;
  - floor, ceiling, ceiling lights and lamps for each chunk, taken from pools;
  - the aisle signs and room lighting rules applied per chunk.
- **3c. Streaming.** Chunks within a radius of the player load; the rest unload, keeping their
  changes.
  - Each chunk's NavMesh builds asynchronously (`NavMeshBuilder.UpdateNavMeshDataAsync`), with
    links to its neighbours.
  - Shoppers in a chunk that unloads leave the store.
  - Tasks count the shift's area, not whatever happens to be loaded.
  - Aiko plans on a bounded window: see the conflict below. The resolution is a store that is
    finite per shift and endless across shifts, and that still holds.
  - The world shifts back toward the origin once the player is far out (a floating origin).

**Still open: how the drawn store meets the generated maze.** The hand-built store stays
the default. The generated maze runs first as its own mode, which the eval can also ask
for. Whether the generated maze replaces the sales floor, or opens out of it, is a design
call for later, once it can be walked.

### Order

1. ~~GPU Resident Drawer, with GPU occlusion culling~~ done, 18.9 → 4.5 ms
2. ~~Shelves as data~~ built: the scene is 92 → 6.2 MB. Render cost and Play-mode check to come
3. Chunks (3a), a seeded maze (3b), streaming (3c)

---

## Infinite maze

### Why it helps the research, not just the game

Procedural layouts stop agents memorising a fixed map, which is the standard criticism of
game-derived benchmarks. It converts "solved this level" into "solved this class of
level". That makes the eval environment considerably more defensible.

### Approach

**Chunked, seeded, deterministic.** The world is a grid of chunks (say 5×5 of the
existing 5 m squares = 25×25 m). Each chunk's layout is generated from
`hash(world_seed, chunk_x, chunk_z)` — so it is:

- **infinite** — nothing is stored, chunks are generated on demand
- **stable** — walk away and back, the same aisles are there
- **reproducible** — a seed fully determines a world, which the eval needs

### Which algorithm

| Option | Fit |
|---|---|
| **Wang / edge-matched tiles** | **Best fit.** Hand-author a tile set whose edges match; connectivity guaranteed by construction; trivially cheap; aisle runs look deliberate |
| Wave Function Collapse | Prettier, learns adjacency from an example layout, but can hit contradictions and needs care to chunk without seams |
| Perfect maze (DFS/Kruskal) | **Avoid.** Perfect mazes are all dead ends — miserable to shop in and unfair to be chased through |
| BSP rooms + corridors | Reads as a dungeon, not a shop |

**Braid the maze.** Whatever generates it, remove most dead ends by knocking through
walls to create loops. A chase with no escape route is frustrating rather than tense, and
Aiko's herding tactics need loops to be interesting.

### The hard parts, in order

1. **Runtime NavMesh.** This is the real work. `NavMeshSurface.BuildNavMesh()` per chunk
   at runtime, with links stitching neighbours. Async so it does not hitch. Customers
   whose chunk unloads must despawn cleanly
2. **Floating origin.** Past a few kilometres, float precision degrades visibly. Shift the
   world back toward origin when the player strays far
3. **Object pooling.** Streaming means spawn/despawn churn. Pool shelves, lights, speakers,
   and the items that are off their shelves. The 16,500 shelved items are data after step 2
   of the scaling plan above, so they don't churn at all
4. **State of unloaded chunks.** A spill in a chunk you walked away from — does it persist?
   Cheapest answer: keep a small per-chunk state record (spills, shelf fill, bins) and
   restore on load. Without this the task counts flicker as you move
5. **Task scoping.** `ShelfUnit.NotFullCount` and friends currently count the whole scene.
   With streaming they must count *the shift's assigned area*, not the loaded window,
   or the task list becomes meaningless

### Conflict worth flagging

`Aiko.md`'s herding relies on an **aisle graph with articulation points and min-cuts** —
that needs a *bounded* graph. On an infinite map, Aiko must operate on a bounded window:
the current shift's store footprint. Practical resolution: **the store is finite per
shift; the maze is infinite across shifts.** Each shift generates a bounded store from a
seed. That keeps Aiko's graph analysis valid, gives the eval its procedural variation,
and sidesteps most of the streaming work above.

I would take that resolution. It gets nearly all the benefit for a fraction of the cost.

---

## Blink mechanic

Pipeline, latency budget and the dark-room problem are the main constraints — see the
detailed table below.

### Options

| Approach | Inference | End-to-end | Accuracy | Cost |
|---|---|---|---|---|
| **MediaPipe Face Landmarker** | 5–15 ms | ~100–150 ms | High, slightly smoothed | Free |
| **EAR on landmarks** | 5–15 ms | ~100–150 ms | Medium, calibration-sensitive | Free |
| **Own eye-crop CNN** | **<2 ms** | **~60–100 ms** | High after fine-tune | Free to train |
| Apple Vision (macOS) | ~5 ms | ~80–120 ms | High | Free, platform-locked |
| Tobii Eye Tracker 5 | ~5 ms | ~30–60 ms | Very high, works in the dark | ~$230–280 |

End-to-end assumes a 30 fps camera. **Camera frame rate dominates everything else** — at
30 fps there is a 33 ms floor between samples before any processing happens.

### Training your own — the realistic path

This is a tiny binary classification problem, not an LLM. It is cheap.

**Architecture.** 24×24 or 32×32 grayscale eye crop → 3–4 conv layers → open/closed
probability. On the order of 50k–200k parameters.

**Pipeline at runtime.** Face detect every 5–10 frames (cheap amortised) → track the eye
box between detections → crop → CNN every frame. This is why it beats full face mesh on
latency: you are not re-running a 468-landmark model 60 times a second.

**Data.** Public eye-state datasets exist and are free for research — MRL Eye Dataset is
the large one (tens of thousands of labelled open/closed eye crops, infrared included,
which also helps the dark-room case). CEW and blink-video sets like Eyeblink8 and ZJU are
smaller. *Check each licence before shipping commercially — several are research-only,
and this is the same trap as dlib's 68-point predictor.*

**Distillation shortcut — recommended.** Record yourself for a few minutes, label the
frames automatically with MediaPipe's blink blendshape, then train the tiny CNN against
those labels. You get a model that is fast, matched to your camera and your face, and
required no manual annotation. This is the highest-value trick here.

**Cost.** Essentially **$0**. A model this size trains in minutes on a laptop GPU, or
half an hour on CPU. If you want a cloud GPU for convenience it is a couple of dollars,
not a budget item. The expensive resource is your labelling time, and distillation
removes it.

**Deployment.** Export ONNX → Unity Sentis. No native plugin, no per-platform build.

### Per-player calibration

Ship a 10-second calibration regardless of approach: look at the camera, blink a few
times. Record that player's open and closed baseline and normalise. Eye geometry varies
enough between people that a global threshold will not hold.

### Cheap latency win

A high-frame-rate camera helps more than any model change. The PS3 Eye is a
well-known trick in CV and VR tracking circles — 60 fps at 640×480, 120 fps at lower
resolution, for very little money secondhand. Caveat: it needs third-party drivers on
modern macOS and Windows, so it is a developer tool rather than something to ask players
for.

### Architecture

```
IBlinkSource ─┬─ KeyboardBlinkSource   (dev + accessibility fallback)
              ├─ WebcamBlinkSource     (the CV path)
              └─ ReplayBlinkSource     (recorded traces — test without a face)
                      │
                 BlinkTracker  (calibration, smoothing, confidence)
                      │
          ┌───────────┴────────────┐
          ▼                        ▼
   Eyelids.Closed01        OnBlinkStart / OnEyesClosedFor(t) → Aiko
```

Build the keyboard source first — it proves the whole chain including Aiko's reactions.
Then replay traces, so Aiko's blink behaviour can be tested without sitting in front of a
camera blinking on cue. The webcam goes in last; it is the least certain part and the
easiest to swap in once everything above it works.

→ Built in that order (`Blink/Scripts/`). The webcam source comes two ways: `UdpBlinkSource`
reads the Python sidecar (`tools/blink/blink_server.py` — MediaPipe blendshapes, eye aspect
ratio, or your own ONNX model), and `SentisBlinkSource` runs the same model in-engine once the
inference package is added. The own-training path is `record_dataset.py` (distillation from
MediaPipe, with consent) → `train_eye_cnn.py` (tiny CNN → ONNX). Keys: B blinks, F8 consent,
F9 calibration. The tracker predicts the reopening from the measured pipeline latency, and
rung F's `blink_advance` tactic moves inside what's left of the closure.

### Non-negotiables

- **Blink is a modifier, never a requirement.** Everything must be playable with the
  camera off. This covers the dark-room failure, refused permissions, missing hardware
  and accessibility in one decision
- **Opt-in, local-only, never recorded**, and said plainly in-product. It is biometric
  processing and a real trust barrier
- **Exploit the window, do not chase the latency.** Blinks are stereotyped at roughly
  300 ms, so on detecting one ~120 ms in you can *predict* the reopening and schedule
  Aiko's move inside the remaining closure. Far more robust than trying to react faster

---

## Suggested order

1. ~~**Eval interface** (observation, action, metrics, determinism, headless) — unlocks everything~~ done
2. ~~**Thought log** schema — trivial now, painful to retrofit~~ done
3. ~~**Aiko rungs B–D** + the ablation table~~ done, A–F
4. **Seeded finite store per shift** — procedural variation without the streaming cost
   (partly: `MazeMutation` moves bays between shifts from shift 7, validated for reachability)
5. ~~**Blink**: keyboard → replay → webcam~~ done
6. Infinite streaming maze: wanted (2026-10-07), and planned in
   [Scaling](#scaling-a-lot-of-stock-and-a-maze-with-no-end). Steps 1 and 2 are built; chunks
   come next

The first three are the ones a research audience actually reads. Everything after is
upside.
