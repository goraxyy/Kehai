# Sandboxes

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), focus items 1, 2 and 4 (21 November). The tasks
are the plan's [Phase 11](../0.5.2-plan.md#phase-11-sandboxes-c-with-you--opus-55-for-karens-sandbox-mode-sonnet-55-for-the-kit--this-mac).
The questions (Q numbers) are in the plan's [open questions](../0.5.2-plan.md#11-open-questions).

1. [What you asked for](#1-what-you-asked-for)
2. [What exists today](#2-what-exists-today)
3. [One island or separate sandboxes](#3-one-island-or-separate-sandboxes)
4. [The four sandboxes](#4-the-four-sandboxes)
5. [The controls and the indicators](#5-the-controls-and-the-indicators)
6. [The bot that records every check](#6-the-bot-that-records-every-check)
7. [How they fit with the other scenes](#7-how-they-fit-with-the-other-scenes)

---

## 1. What you asked for

- **A small island with Karen** to check that tricks and interactions are built right: walls and a
  door to enter, Karen standing still, a button that lets her move, and a restart button that puts
  her back in place, frozen.
- **What's on the island:** a stock fridge, a stock rack, some tall shelves and some short ones.
- **For sight:** Karen has no tricks; she can only walk and see. You hide inside the fridge, crouch
  under the rack and behind the short shelves, and an indicator (a cone) shows whether she sees you.
- **Separate sandboxes** for sight, pursuit after she loses you, all tricks and all interactions,
  each different and built separately (or, you also wondered, one big island with buttons).
- **Every trick and interaction recorded** in its sandbox, played by a bot.

## 2. What exists today

| Piece | What it does now | Use in a sandbox |
|---|---|---|
| Her eye (`SightSensor`) | her real sight: cone, distance, light, the three rays from your eyes, chest and feet, and its awareness that fills and drains; its settings come from `KarenBody.ConfigureEye` | the sight sandbox runs it unchanged, so what you see there is what the game does |
| Hiding places (`HidingPlace`, 0.5.1) | empty spaces set in code; you're hidden at 80% inside | the fridge and the rack get the same authored spaces |
| Witnessed hiding (`WitnessedHide`, 0.5.1) | her memory of seeing you go in | the interactions sandbox |
| Runtime NavMesh | the layouts and the endless maze build their NavMesh at load | a sandbox needs no baked scene |
| The trick check (`TrickCheck`) | forces each trick with a test player, records a replay (`.krec`) and a table of starts and landings | the model for the bot's recorded checks |
| The replay renderer (`render_shot.sh`) | films a replay into a video, silent | a video of each check |
| The F1 map and her gaze on the floor | her belief and where she looks, in the real store | the indicators reuse them |
| A sandbox | **none** | — |

## 3. One island or separate sandboxes

| Option | Pros | Cons |
|---|---|---|
| **A. Separate small sandboxes, one per topic (my pick; your item 2)** | each is small, loads in seconds, and tests one thing; a change to one can't break another; the bot's checks stay short | four builders to keep (they share one kit, so it's mostly data) |
| B. One big island with a station per test and buttons | one place to walk around; you see everything at once | tests can disturb each other (a trick in one corner reaches another); slower to load; harder for a bot to check one thing |
| C. One island whose layout changes with a button | one scene, many setups | the hardest to build; a half-finished change looks like a bug |

**My pick: A** (Q23), with one shared kit (walls, door, fridge, rack, shelves, buttons,
indicators), so each sandbox is a short list of what goes where.

**Changed on 21 November (your call): one sandbox island in the store scene.** See section 8.

## 4. The four sandboxes

| Sandbox | What's in it | Karen's mode | What you check |
|---|---|---|---|
| **Sight (first)** | a walled room about 16 × 12 m with one door; a stock fridge (a cold hiding cabinet), a stock rack, two tall shelves, three short shelves, lamps | **walks and sees only:** no tricks, no hunting, no catching; she walks a fixed loop, or stands and turns | hide in the fridge, crouch under the rack, crouch or stand behind a short shelf, stand in the open near and far: the cone and the meters show whether, and how fast, she sees you |
| Pursuit | a loop of aisles with corners and a hiding spot | sees, hunts and searches, but no tricks; catching only freezes the test | break her line of sight round a corner: where she goes next, how long she keeps looking (0.5.1's 60 s pursuit) |
| Tricks | a stall per trick: the PA, the lights, a fridge, a camera, a spill spot | each trick fired by a button | each trick starts, lands, and can be answered |
| Interactions | the fridge, the rack, lockers, a customer, a store camera, throwable items | the interactions on, tricks off | seen hiding, the fridge check, the kneel check, the stun, and the rest of [the interactions page](interactions.md) |

**Example, the sight sandbox:** you press **Start**. Karen walks her loop. You crouch behind a short
shelf: her cone turns from grey to amber as her awareness fills, and the meter above her head
reads "sees: eyes only, 0.42". You press **Restart**: she's back at her spot, frozen, and the meters
read zero. You climb into the fridge in her view: the fridge's meter reads "hidden 92%", and the
"saw you go in" light comes on.

## 5. The controls and the indicators

**Controls,** as buttons on a panel by the door (press E), and the same as keys:

| Button | Key | Does |
|---|---|---|
| **Start** | F5 | she starts moving (her loop, or her mode's behaviour) |
| **Restart** | F6 | she goes back to her spot and freezes; her awareness and memory are cleared |
| **Freeze** | F5 again | she stops where she is, still seeing, so you can read the meters |
| **Route** | panel only | switches between her walking loop and standing still while turning |

**Indicators,** each one switchable:

| Indicator | Shows |
|---|---|
| **The vision cone** on the floor | her field of view and range; grey when she sees nothing, amber as her awareness fills, red when she's sure |
| **The meter above her head** | her awareness (0–1), and which of your three points she sees (eyes, chest, feet) |
| **The sight lines** | her rays to your eyes, chest and feet: green where clear, red where blocked |
| **The hiding meter** | on each hiding place: how much of you is inside, and "hidden" at 80% |
| **The "saw you go in" light** | lit when her eye has a witnessed entry (0.5.1) |
| Later sandboxes | her belief on the floor (from the F1 map), her hearing rings, the stun timer |

**Indicators are for the sandboxes only:** they never ship, and they never change what she knows.

## 6. The bot that records every check

- **Every check is a scripted bot run:** the bot walks, crouches and hides exactly as a person would
  for that check (for example "crouch under the rack, 4 m from her, in her view"), and the result
  is a row: what was expected, what happened, pass or fail.
- **Every run is recorded:** a replay (`.krec`), the row, and a short video from the replay
  renderer (silent). One command runs a sandbox's whole list and writes a report.
- **What the sight list checks, as an example:** seen in the open at 10 m within the game's target
  time; not seen inside the fridge; not seen crouched under the rack; seen standing behind a short
  shelf (your eyes show over it); witnessed when you go into the fridge in her view, not when you go
  in behind her back.
- **Runs in batch,** silent, with your editor closed (or on the PC once it's ready), under the
  heavy-work lock, and only with your go. Every check shift is recorded, as you asked (Q1).

## 7. How they fit with the other scenes

- **The sandboxes are dev scenes:** they sit beside the Training scene and the Workshop, never ship,
  and aren't part of any edition's build.
- **Built by authoring code, at load:** an empty scene plus a C# builder that places the kit, builds
  the NavMesh and sets Karen up. Nothing about them goes into the public repo but code, as the rules
  ask; no scene file needs to be versioned at all.
- **Real parts where it matters:** her eye, her body and the hiding places are the game's own code.
  The furniture is simple boxes at the real sizes (a quick pass, like the rest of the art); the
  real models can replace them later.

## 8. The sandbox island in the store scene (your call, 21 November)

You'll work in the store scene all the time, so the sandboxes move there: **one island**, the
place you test her tricks and the interactions.

| Part | What it is |
|---|---|
| **Where** | One island beside the store, built by code when the scene loads (nothing saved in the scene file). The models island moves beside it. Walkable ground joins the store, the sandbox island and the models island, so you can walk between all three. |
| **Areas on it** | Sight, pursuit, tricks and interactions, side by side, each with its panel. The tricks area takes over the plates now outside the entrance. |
| **The store meanwhile** | Quiet: no shift runs and the store's own Karen is off. The island's test Karen is the only Karen. You can still walk into the store. |
| **Her map there** | A separate test map that covers the island only, the same kind as the store's (rooms, cells, landmarks), so she can plan, hunt and search there. The store's map is untouched. |
| **Keys** | F5 starts or freezes her, F6 restarts, F4 hides the indicators; Route is on the panel. F7 (clip mark) and F8 (webcam) keep their store jobs. Done 21 November. |

**Example:** you open Kehai → Sandbox, the store loads quiet, you walk out of the front door
to the island, stand at the pursuit area, press F5, let her see you and run behind a shelf.

### The keys, sorted and squashed (a proposal for you to say yes to)

Today the game uses ten F keys. Squashed, it needs seven, and F3, F9 and F10 come free:

| Key | Today | Proposed |
|---|---|---|
| F1 | her map (H, R, T on it; G forces a trick in developer builds) | the same, plus her floor cone on it (was F3) |
| F2 | replay the shift so far | the same |
| F3 | her footsteps and cone (developer builds) | free: into F1's map |
| F4, F5, F6 | the sandbox: indicators, start or freeze, restart | the same, only in the sandbox |
| F7 | mark a clip (Shift: mark a bug) | the same |
| F8 | webcam blinking on or off | one webcam panel: on or off, calibrate, test |
| F9 | calibrate the webcam | free: into F8's panel |
| F10 | the blink test panel | free: into F8's panel |
| L | cut the power (testing) | developer builds only, on F1's map |

