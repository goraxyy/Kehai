# Kehai

> *kehai (気配): Japanese for "the sense that someone is there"*

A first-person convenience-store night shift, built in **Unity 6**. You clock in, stock shelves,
mop spills, empty the bins, serve customers and walk lost shoppers to the shelf they're looking
for. Meanwhile **Aiko**, the store's management AI, hunts you with only what she can see and
hear, remembers where you hide, and moves when you blink. She rarely kills you: she gives you
overtime.

> *"Employee wellbeing is a tracked metric. I am optimising it."*

**Status:** prototype on its way to a vertical slice; not released yet. The plan to a Steam
release is in [`RELEASE_PLAN.md`](RELEASE_PLAN.md), and the devlog and early-player plan is in
[`MARKETING.md`](MARKETING.md).

---

## About this repository

This is a **code-only mirror**: the C# scripts (with their `.meta` files), the custom shader,
the Python and Swift tools, and the design documents. It deliberately leaves out the scenes,
models, materials, audio, prefabs, third-party packs and `ProjectSettings`, so it is not a
runnable Unity project on its own. Data that would normally live in assets (Aiko's tactics, the
store's planogram) is written in code so that it can be reviewed here.

---

## The game

- **The shift.** Clock in at the time clock. Customers come in, browse, take stock, leave messes,
  use the bins and queue at the till. You can't clock out until the shelves are full, the spills
  are mopped and the rubbish is out.
- **The jobs.** Restock (carry the stock crate to a shelf), mop (hold **E** with the mop), bag the
  bins and take the bag to the skip out back, serve at the till, and walk customers to the shelf
  they asked about in a 150×150 m maze of a store (189 shelf units, 13 sections, 80 products).
- **Burnout.** Your energy drains over the shift and with sprinting; coffee restores it. At zero
  you can only walk.
- **The building.** Mains power with breakers, 240 ceiling lights, a store radio on 64 speakers,
  automatic doors, a flashlight, and procedural sound for everything Aiko does.

## Aiko, the adaptive antagonist

Designed in [`aiko.md`](aiko.md) (1,200 lines) and implemented in `Assets/!_Project/_Game/AI/`
(about 11,500 lines of C#). She runs three minds:

| Mind | Knows | Controls |
|---|---|---|
| **The Body** | only what it senses | where she walks and what she does to you |
| **The Director** | everything | pacing: tension, breathers, when tricks are allowed |
| **The Ledger** | your history across shifts | which of her tricks she's inclined to try on you |

- **Perception.** Graded sight (distance, angle, light, movement), a noise bus where every action
  has a loudness, customers who tell her they saw you, and building sensors (doors, the till).
- **Belief.** A probability map of where you might be over the store's regions. It sharpens on a
  sighting, spreads out over time, and clears where she looks and doesn't find you.
- **Decisions.** Utility-scored goals and a planner over **35 tactics**: blackouts, fake door
  chimes, PA announcements of where you are, customers she "possesses", shelves she empties behind
  you, fog, cameras, footprint trails, and more.
- **Learning.** A multi-armed bandit (UCB or Thompson sampling, with a habituation penalty)
  chooses between tactics. The persistent Ledger remembers where you dwell and hide and the routes
  you take. Everything she learns fades if you change your habits.
- **Fairness, enforced by tests.** The body and decision code may not read your true position (an
  automated test scans for it). Every tactic has a warning at least 0.8 s ahead: a flicker, a
  chime, a PA crackle. She can never outrun a sprint: her paces are capped below your sprint speed.
- **Visible gaze.** Her field of view is painted on the floor, cut short by shelves and walls:
  blue while she walks her rounds, orange when she's noticed something, red while she hunts or
  can see you.

## The blink channel

Opt-in webcam blink tracking. A small helper program reads your eyes, either Apple Vision
([`tools/blink/mac`](tools/blink/mac), no downloads) or MediaPipe
([`tools/blink/setup_mediapipe.sh`](tools/blink/setup_mediapipe.sh)), and sends the game one
number per frame over localhost. The game calibrates to your eyes, compensates for the helper's
delay, and lets Aiko act inside the ~300 ms of your blink. Nothing is recorded, and nothing
leaves the computer.

**F8** turns it on, **F9** runs a 12-second guided calibration, **F10** is a live test panel, and
**B** blinks from the keyboard with or without a camera. Setup:
[`tools/blink/README.md`](tools/blink/README.md).

## Seeing what happened

- **F1:** a live map of the store (walls, shelves by section, doors) with you, Aiko, her view
  cone and her guess of where you are, customers by what they're doing (a dashed line to the
  shelf one is asking about), sounds as rings, spills, empty shelves and bins, plus a plain-English
  story of what Aiko is doing. **H** adds her belief heat map; **T** the technical view.
- **F2:** a replay of the shift so far on the same map.
- **Shift reports.** Every shift is recorded (positions five times a second, every event) and
  saved as JSON plus a self-contained HTML report: a replay with a timeline, a clickable event
  list, and an analysis (jobs, time per area, when and where she spotted you, the closest she got,
  what she tried most).
- **Clip moments.** While you play, the shift is watched for moments worth a clip (a blink move,
  a catch, a near miss, a blackout, a warning and its trick…), weighted in `ClipMarkers.cs`.
  At clock-out, markers close together become scored moments in `<shift>.markers.json` next to
  the report; they show in gold on the F2 timeline and in the report. **F7** marks a moment
  yourself, **Left Shift + F7** marks a bug.
- **Replay recordings.** Each shift is also recorded for a 3D replay as `<shift>.krec` next to the
  report: everything that moves 30 times a second (the view 60), what was heard and said, the
  lights, the shelves, and her belief map, about 1 MB per 10 minutes (`Replay/Scripts/`).
- **3D replay.** Press **R** after a shift (or Kehai → Replay in the editor) to watch it again in
  the store: play, scrub, 0.1–4×, the clip moments on the timeline; your eyes, a CCTV corner, a
  chase camera, an orbit, top down, or a free camera; **K** saves camera keyframes as a smooth
  path; her mind drawn in (belief map, her guess, view cone, sound rings, thought log). The keys
  are in [CONTROLS.md](CONTROLS.md#3d-replay). Shots render unattended with
  `tools/marketing/render_shot.sh` (the editor closed; ffmpeg for video files).

## Main menu

The game opens on a title screen over the store, with time stopped:

- **Continue** picks your career up at its next shift. Aiko saves it, and what she has learned
  about you, after every shift; a shift you leave halfway isn't saved.
- **New career** wipes what she has learned (it asks first); your settings stay.
- **Settings** and **Controls** open the Esc menu's own pages.

You get back to it from the Esc menu, and with Enter after a career ends. It stays out of the way
of the eval harness and the 3D replay; `-skip-menu` on the command line skips it, and so does
**Kehai → Main Menu → Skip It When Playing in the Editor**.

## Settings (Esc)

Esc pauses the game and opens the settings:

- **Restart this shift:** the store resets, you go back to where you start, and the same shift
  begins again. Aiko still remembers earlier shifts.
- **Main menu:** leave the shift for the title screen.
- **Volume** for everything, and separately for sound effects, Aiko, the radio and the PA.
- **Mouse sensitivity**, and **Aiko's floor cone** on or off.
- **Webcam blinking:** on or off, calibrate, and the blink test.
- A **Keys** tab with every key in the game.

Your choices are remembered between sessions.

## Evaluation harness

Aiko is measured, not just tuned by feel (`Assets/!_Project/_Game/Eval/`, [`tools/eval`](tools/eval)):

- A headless, fixed-timestep simulation that runs faster than real time.
- A socket environment for external agents, with Python clients: scripted baselines and an LLM
  agent (Claude plays the shift).
- Simulated players in three profiles: *efficient*, *skittish* and *reckless*.
- An **ablation ladder**: six versions of Aiko, from a random patrol (A) up to the full system
  with learning and the blink channel (F), on paired seeds.

Results are in [`AIKO_RESULTS.md`](AIKO_RESULTS.md). Across 216 simulated shifts, the belief map
and planner found players twice as fast as the patrols (first detection 51 s against about
100 s) with about 11 more detections a shift, and the fairness rules held with 0 violations. The
learning rungs did not separate from the non-learning one against scripted players; the write-up
says why, and what to test next.

## Controls

The essentials (the full list is in [`CONTROLS.md`](CONTROLS.md), and in the game under
**Esc → Keys** or **Controls** in the main menu):

| Key | Action |
|---|---|
| WASD, mouse | Move, look |
| Left Shift (hold) | Sprint (loud; stands you up from a crouch) |
| Left Ctrl (hold) | Crouch while held (quiet) |
| E / hold E | Use / mop, clear, unplug |
| Q / hold Q | Put down / throw |
| 1–4, mouse wheel | Hand slot |
| C | Task list |
| Esc | Pause: restart the shift, the main menu, volume by kind of sound, mouse, Aiko's floor cone, webcam |
| F1 / F2 | Live map / replay |
| F8 / F9 / F10 / B | Webcam blink on-off / calibrate / test panel / keyboard blink |
| F7 / Left Shift + F7 | Mark a clip moment / mark a bug |

## Running the tests and the evaluation headless

With the project closed in the editor (Unity allows one instance per project):

```bash
# 83 EditMode tests: AI rules, fairness, Aiko's speed cap, the belief map, shift records, the key list,
# the save migration from the game's old name, clip markers, the replay recording and the 3D replay,
# the main menu's choices and the build's scene list
# (two of them load the store and play a few seconds of a bot shift; without -nographics the
# replay test also checks what the cameras draw)
Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml

# One shot of a recorded shift: clip moment 1, the chase camera, her mind drawn in, vertical
tools/marketing/render_shot.sh -krec <shift>.krec -moment 1 -shot chase -layers all -size 1080x1920 -out shot.mp4

# One simulated shift against the full Aiko, which also writes a shift report
Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play \
  -kehai-ablation -ablation-careers 1 -ablation-shifts 1 -ablation-rungs F -ablation-profiles efficient
```

The full ablation command and its analysis script are in [`AIKO_RESULTS.md`](AIKO_RESULTS.md).

## Building the game

**Kehai → Build → macOS** or **→ Windows** in the editor, or headless with the editor closed:

```bash
Unity -batchmode -nographics -projectPath . -executeMethod KehaiBuild.MacOS
Unity -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod KehaiBuild.Windows
```

`-kehai-build-out <path>` chooses where, `-kehai-build-dev` makes a development build. Each
build gets a `build.txt` beside it with the version and commit, and a
`_BurstDebugInformation_DoNotShip` folder that is for reading crash logs and stays with you.

- **macOS:** `Builds/macOS/Kehai.app`, with the webcam blink helper beside it if
  `tools/blink/mac/build.sh` has built it. To share it, zip the app, `BlinkVision` and
  `build.txt`. It is signed only ad hoc (no Apple developer account), so on another Mac macOS
  blocks its first launch: **System Settings → Privacy & Security → Open Anyway**.
- **Windows** (64-bit; needs Windows Build Support (Mono) added to the editor in Unity Hub):
  `Builds/Windows/Kehai.exe`, which needs the rest of that folder beside it (`Kehai_Data`,
  `UnityPlayer.dll` and the others). Zip the whole folder except the DoNotShip one. It isn't
  code-signed, so Windows SmartScreen warns on first launch: **More info → Run anyway**. There is
  no webcam helper for Windows yet, so blinking there is the keyboard's **B**.

The built game runs the eval headless too:

```bash
Builds/macOS/Kehai.app/Contents/MacOS/Kehai -batchmode -nographics -kehai-ablation \
  -ablation-careers 1 -ablation-shifts 1 -ablation-rungs F -ablation-profiles efficient
```

## Playtesting

Playtest builds (`KehaiBuild … -playtest round1`) record a tester's whole session, from launch
to quitting, as 3D replays plus a log of every menu, key and frame-rate dip. After the tester
agrees, they send it back. In-game questions and a Google Form cover what they thought. The
loop, what's recorded and how to run a round are in [`PLAYTEST.md`](PLAYTEST.md).

## Repository layout

```
Assets/!_Project/
├── _Core/Scripts/Runtime/     shift, tasks, burnout, events, settings, pause, restart, fonts, JSON
├── _Core/Editor/              the build script (macOS, Windows), opening the store on launch
├── _Game/
│   ├── AI/Scripts/
│   │   ├── Core/              Aiko's brain, body, Director, Ledger, config, bootstrap
│   │   ├── Perception/        sight, the noise bus, witnesses, traces, the building
│   │   ├── Belief/            the belief grid
│   │   ├── Decision/          goals, the planner, and the 35 tactics
│   │   ├── World/             what she does to the store: lights, PA, props, maze, floor cone
│   │   └── Diagnostics/       F1 map, narrator, shift recorder and analysis, HTML report
│   ├── Blink/Scripts/         blink sources, tracker, calibration, helper launcher, F10 panel
│   ├── Playtest/Scripts/      playtest builds: the session log, consent, questions, packing, upload
│   ├── Eval/                  headless env, simulated players, ablation runner, batch entry
│   ├── Map/                   store map, floor plan, NavMesh walls
│   ├── Level/                 shelves, planogram, doors, power, radio, bins, spills, audio
│   ├── Characters/            customers, requests for directions, speech bubbles
│   ├── Items/                 items, tools and their homes, flashlight
│   └── Player/                movement, interaction, inventory, controls, main menu, settings menu
└── _Tests/Editor/             EditMode tests
tools/
├── blink/                     webcam helpers (Swift / Apple Vision, Python / MediaPipe), training path
└── eval/                      Python clients for the eval env, ablation analysis
```

## Tech stack

- **Unity 6** (6000.5), **C#**, **URP**, NavMesh (`Unity.AI.Navigation`), TextMeshPro
- **Swift** with Apple Vision and AVFoundation for the macOS blink helper
- **Python** for the eval clients, the analysis, and the optional MediaPipe blink helper
- Unity batch mode for headless tests, simulation and ablations

## Documents

| File | What it is |
|---|---|
| [`aiko.md`](aiko.md) | Aiko's full design, and where the code departs from it |
| [`ideas.md`](ideas.md) | Research ideas: the agent-eval environment, the ablation ladder, the thought log, the blink channel |
| [`AIKO_RESULTS.md`](AIKO_RESULTS.md) | The ablation results |
| [`CONTROLS.md`](CONTROLS.md) | Every key |
| [`RELEASE_PLAN.md`](RELEASE_PLAN.md) | Milestones to a Steam release |
| [`PLAYTEST.md`](PLAYTEST.md) | How playtests work: the loop, what's recorded, running a round |
| [`MARKETING.md`](MARKETING.md) | Devlog, platforms and getting early players |
| [`STORE_CATALOG.md`](STORE_CATALOG.md), [`STORE_MAP.md`](STORE_MAP.md) | The planogram and the store's map |
| [`CHANGELOG.md`](CHANGELOG.md) | Changes that affect players' saves or settings |
| [`tools/blink/README.md`](tools/blink/README.md), [`tools/eval/README.md`](tools/eval/README.md) | Tool setup |
