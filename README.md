# Kehai

> *kehai (気配): Japanese for "the sense that someone is there"*

A first-person night shift in a convenience store, made in **Unity 6**. Stock shelves, mop
spills, empty the bins, serve at the till and walk lost customers to what they're looking for.
Meanwhile **Karen**, the store's AI, hunts you with what she can see and hear. She remembers
where you hide, and she moves when you blink. Her touch kills.

> *"Employee wellbeing is a tracked metric. I am optimising it."*

**Status:** a playable prototype on its way to a vertical slice; not released. What's left is in
[`docs/project/RELEASE_PLAN.md`](docs/project/RELEASE_PLAN.md).

---

## This repository

It holds the **code and documents only**: C# scripts (with their `.meta` files), the shader, the
Python, Swift and TypeScript tools, and the docs. Scenes, models, audio, prefabs and project
settings stay on the developer's Mac, so this isn't a runnable Unity project on its own. Data
that would normally live in assets (Karen's tactics, the products, the shelf plan) is written in
code so it can be read here.

The staff-room and office model libraries, layouts and organized Models_Island are generated
by the **Kehai → Staff Room** and **Kehai → Office** editor menus. See
[`docs/store/INTERIOR_MODELS.md`](docs/store/INTERIOR_MODELS.md) for their inventory, dimensions and saving steps.

---

## The game

- **The shift:** clock in, do the jobs, clock out once the shelves are full, the spills mopped
  and the bins emptied. Energy drains as you go; coffee helps.
- **The store:** a 60 × 45 m sales-floor maze of 189 shelf bays, 80 products in 13 sections,
  thousands of shelf slots, power cuts, a radio and automatic doors.
- **Karen:** three minds (a body that only knows what it senses, a director that paces the
  tension, a ledger that remembers you across shifts), a map of where she thinks you are, and
  six live tricks (eight choices for the learner): Ask the customers, Bin tamper, Cut, Ambush,
  Throw (powder, detergent or tin), and Blackout. Block door is built and tested, awaiting
  activation as the seventh trick and ninth choice; other tricks remain disabled drafts.
  Hunt runs at 7.5 m/s against your 6.5 m/s sprint, about 15% faster. Tricks no longer promise
  advance warnings. The 0.2.0 comparison with the previous build is still pending. Design:
  [`docs/karen/Karen.md`](docs/karen/Karen.md).
- **The blink:** opt-in webcam tracking lets Karen act while your eyes are shut. Nothing is
  recorded, and nothing leaves your computer ([`tools/blink`](tools/blink/README.md)).
- **Seeing what happened:** a live map (**F1**), a replay of the shift (**F2**), an HTML report
  after every shift, and a full 3D replay (**R**) you can watch from any camera.

## Controls

| Key | Action |
|---|---|
| WASD, mouse | Move, look |
| Left Shift (hold) | Sprint (loud) |
| Left Ctrl (hold) | Crouch (quiet) |
| E / hold E | Use / mop, clear, unplug |
| Q / hold Q | Put down / throw |
| 1–4, mouse wheel | Hand slot |
| C | Task list |
| Esc | Pause and settings |
| F1 / F2 | Live map / replay |
| F8 / F9 / F10 / B | Webcam blink on-off / calibrate / test / keyboard blink |

Every key: [`docs/game/CONTROLS.md`](docs/game/CONTROLS.md), or **Esc → Keys** in the game.

---

## Build and test

With the project closed in the editor (Unity allows one instance per project):

```bash
# EditMode tests
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml

# Builds (or Kehai → Build in the editor)
Unity -batchmode -nographics -projectPath . -executeMethod KehaiBuild.MacOS
Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiBuild.Windows

# One simulated shift against the full Karen
Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play \
  -kehai-ablation -ablation-careers 1 -ablation-shifts 1 -ablation-rungs F -ablation-profiles efficient
```

- **macOS:** `Builds/macOS/Kehai.app`. It's signed only ad hoc, so another Mac blocks the first
  launch: **System Settings → Privacy & Security → Open Anyway**.
- **Windows:** `Builds/Windows/Kehai.exe`, with the rest of its folder. Not code-signed:
  **More info → Run anyway**. No webcam helper yet; **B** blinks.
- Each build writes `build.txt` with its version and commit. `-playtest <round>` makes a
  playtest build ([`docs/game/PLAYTEST.md`](docs/game/PLAYTEST.md)).

---

## Layout

```
Assets/!_Project/
├── _Core/            the shift, tasks, burnout, settings, pause; the build script
├── _Game/
│   ├── AI/           Karen: core, perception, belief, decisions and tactics, world, diagnostics
│   ├── Blink/        blink sources, tracker, calibration
│   ├── Eval/         headless environment, simulated players, ablation runner
│   ├── Level/        shelves, planogram, doors, power, radio, bins, spills, the endless maze
│   ├── Map/          the store map Karen uses
│   ├── Replay/       recording shifts and the 3D replay
│   ├── Playtest/     playtest builds: session log, consent, questions, upload
│   ├── Characters/   customers
│   ├── Items/        products and tools
│   └── Player/       movement, hands, menus
└── _Tests/Editor/    EditMode tests
tools/
├── blink/            webcam helpers (Apple Vision, MediaPipe) and model training
├── eval/             Python clients for the AI test environment; ablation analysis
├── marketing/        the video automation (Python, Remotion)
└── playtest/         the playtest upload service (Cloudflare)
```

Unity 6 (6000.5), C#, URP, NavMesh, TextMeshPro · Swift for the macOS blink helper · Python
for the tools.

---

## Documents

Sorted by topic under `docs/`. Start with the handover, then the 0.2.0 plan.

| folder | file | what it is |
|---|---|---|
| `project/` | [`HANDOVER.md`](docs/project/HANDOVER.md) | Start here: where everything is, the state of the work, what's next |
| | [`RELEASE_PLAN.md`](docs/project/RELEASE_PLAN.md) | Everything left until release, in order |
| | [`DECISIONS.md`](docs/project/DECISIONS.md) | The big technical choices and why |
| | [`IDEAS.md`](docs/project/IDEAS.md) | Blink, the endless maze, the AI test environment, ablation, the thought log, search and memory ideas |
| | [`CHANGELOG.md`](docs/project/CHANGELOG.md) | Changes players will notice |
| `plans/` | [`0.2.0-build-plan.md`](docs/plans/0.2.0-build-plan.md) | Karen 0.2.0, task by task: who builds what, with boxes to tick (approved, not built) |
| | [`0.2.0-design-notes.md`](docs/plans/0.2.0-design-notes.md) | Why 0.2.0 is built that way: today's code, the options, the sources, your answers |
| | [`learned-motion-model.md`](docs/plans/learned-motion-model.md) | The learned motion model, draft PR #68 |
| | [`karen-capture.md`](docs/plans/karen-capture.md) | What catches you, and new fair ways to kill (approved, not started) |
| | [`marketing-agents.md`](docs/plans/marketing-agents.md) | Hands-off video publishing |
| `karen/` | [`Karen.md`](docs/karen/Karen.md) | Karen's full design, and where the code differs |
| | [`KAREN_RESULTS.md`](docs/karen/KAREN_RESULTS.md) | What testing Karen found |
| `store/` | [`STORE.md`](docs/store/STORE.md) | The building, the products, how the shelves are stocked, Karen's map |
| | [`SCENE_MAP.md`](docs/store/SCENE_MAP.md) | Where everything is in the Unity scene (generated) |
| | [`STORE_MAP.md`](docs/store/STORE_MAP.md) | Karen's map of the store: regions, links, chokepoints (generated) |
| | [`LEVEL_DESIGN.md`](docs/store/LEVEL_DESIGN.md) | Plans for the parking lot, the outside, the lobby, the restrooms and the staff rooms |
| | [`INTERIOR_MODELS.md`](docs/store/INTERIOR_MODELS.md) | The staff room, office and model library as built |
| `game/` | [`CONTROLS.md`](docs/game/CONTROLS.md) | Every key |
| | [`PLAYTEST.md`](docs/game/PLAYTEST.md) | How playtests run |
| `marketing/` | [`MARKETING.md`](docs/marketing/MARKETING.md) | The pitch, the videos, the automation |
| `research/` | [`whiteboard-2026-11-05.md`](docs/research/whiteboard-2026-11-05.md) | Whiteboard notes: how Alien: Isolation and Hello Neighbor hunt you, and Kehai's three pillars |
| `archive/` | [`notmerged.md`](docs/archive/notmerged.md) | Old: the 20 October status, how Karen adapts, the tech list |
| | [`reactive-bots.md`](docs/archive/reactive-bots.md) | Old: the reactive simulated players plan, built in PR #62 |
| | [`readchanges.md`](docs/archive/readchanges.md) | Old: the first redesign plan, replaced by the 0.2.0 plan |
