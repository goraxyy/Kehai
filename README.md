# Kehai

> *kehai (気配): Japanese for "the sense that someone is there"*

A first-person night shift in a convenience store, made in **Unity 6**. Stock shelves, mop
spills, empty the bins, serve at the till and walk lost customers to what they're looking for.
Meanwhile **Karen**, the store's AI, hunts you with only what she can see and hear. She remembers
where you hide, and she moves when you blink. She rarely kills you: she gives you overtime.

> *"Employee wellbeing is a tracked metric. I am optimising it."*

**Status:** a playable prototype on its way to a vertical slice; not released. What's left is in
[`docs/RELEASE_PLAN.md`](docs/RELEASE_PLAN.md).

---

## This repository

It holds the **code and documents only**: C# scripts (with their `.meta` files), the shader, the
Python, Swift and TypeScript tools, and the docs. Scenes, models, audio, prefabs and project
settings stay on the developer's Mac, so this isn't a runnable Unity project on its own. Data
that would normally live in assets (Karen's tactics, the products, the shelf plan) is written in
code so it can be read here.

---

## The game

- **The shift:** clock in, do the jobs, clock out once the shelves are full, the spills mopped
  and the bins emptied. Energy drains as you go; coffee helps.
- **The store:** a 150 × 150 m maze with 13 aisles, 80 products on 16,500 shelf slots, 240
  ceiling lights, power cuts, a radio and automatic doors.
- **Karen:** three minds (a body that only knows what it senses, a director that paces the
  tension, a ledger that remembers you across shifts), a map of where she thinks you are, and 35
  tricks. Every trick has a warning, and she can never outrun a sprint. Design:
  [`docs/Karen.md`](docs/Karen.md).
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

Every key: [`docs/CONTROLS.md`](docs/CONTROLS.md), or **Esc → Keys** in the game.

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
  playtest build ([`docs/PLAYTEST.md`](docs/PLAYTEST.md)).

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

| file | what it is |
|---|---|
| [`RELEASE_PLAN.md`](docs/RELEASE_PLAN.md) | Everything left until release, in order |
| [`CONTROLS.md`](docs/CONTROLS.md) | Every key |
| [`Karen.md`](docs/Karen.md) | Karen's full design, and where the code differs |
| [`STORE.md`](docs/STORE.md) | The building, the products, how the shelves are stocked, Karen's map |
| [`LEVEL_DESIGN.md`](docs/LEVEL_DESIGN.md) | Plans for the parking lot, the outside, the lobby, the restrooms and the staff rooms |
| [`DECISIONS.md`](docs/DECISIONS.md) | The big technical choices and why |
| [`IDEAS.md`](docs/IDEAS.md) | Blink, the endless maze, the AI test environment, ablation, the thought log |
| [`MARKETING.md`](docs/MARKETING.md) | The pitch, the videos, the automation |
| [`PLAYTEST.md`](docs/PLAYTEST.md) | How playtests run |
| [`KAREN_RESULTS.md`](docs/KAREN_RESULTS.md) | What testing Karen found |
| [`CHANGELOG.md`](docs/CHANGELOG.md) | Changes players will notice |
