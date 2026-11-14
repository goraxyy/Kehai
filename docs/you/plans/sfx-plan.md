# Kehai sound effects: the plan

> **Status:** draft, for review (14 November 2026). Nothing is built. The owner's first answers
> are in; the rest are in [§10](#10-open-questions). This page is also the **sound designer's
> guide**: start with [§1](#1-for-the-sound-designer-start-here).
>
> **Owner's answers so far (14 November):**
> - The teammate is a **sound designer**. They make audio files and **don't use Unity**.
> - Audio files live in the private repo **`goraxyy/kehai-assets`**, never in this one.
> - Progress is tracked with **tick boxes in this plan**.
> - Scope: **everything** (Karen, the store, the player and chores, UI and music).
> - Karen's code-made sounds are **layered**: recorded versions play first, the code sound stays
>   as the fallback.
> - The radio's commercial song (`California Gurls.mp3`) **stays for now**, for private testing.

**Progress** (update the numbers when you tick a task):

| Phase | What | Tasks | [C] | [You] | [S] | Done |
|---|---|---|---|---|---|---|
| 0 | Getting set up | 6 | 2 | 3 | 1 | 0 / 6 |
| 1 | The plumbing (code) | 5 | 5 | | | 0 / 5 |
| 2 | The player and the chores | 6 | | | 6 | 0 / 6 |
| 3 | The store: ambience and machines | 5 | | | 5 | 0 / 5 |
| 4 | Karen | 5 | | | 5 | 0 / 5 |
| 5 | UI and music | 3 | | | 3 | 0 / 3 |
| 6 | The mix, licences, sign-off | 5 | 2 | 2 | 1 | 0 / 5 |
| | **All** | **35** | **9** | **5** | **21** | **0 / 35** |

Every [S] task in phases 2–5 also needs Claude to wire it and you to hear it: see
[§7](#7-how-a-task-moves-claim-deliver-wire-hear). The table counts each task once, under who makes it.

**Who's who:**

| Tag | Who | Does |
|---|---|---|
| **[S]** | the sound designer (name to come, [Q1](#10-open-questions)) | makes, records, edits and delivers the sound files |
| **[C]** | Claude | the code: loads the files, plays them at the right moment, tests, docs |
| **[You]** | the owner | invites, listens in the game, decides, signs off |

1. [For the sound designer: start here](#1-for-the-sound-designer-start-here)
2. [What exists today](#2-what-exists-today)
3. [The rules that affect sound](#3-the-rules-that-affect-sound)
4. [Where the files go, and how they're delivered](#4-where-the-files-go-and-how-theyre-delivered)
5. [The sound list, by area](#5-the-sound-list-by-area)
6. [How you hear your work in the game](#6-how-you-hear-your-work-in-the-game)
7. [How a task moves: claim, deliver, wire, hear](#7-how-a-task-moves-claim-deliver-wire-hear)
8. [The work, phase by phase](#8-the-work-phase-by-phase)
9. [Not in this plan](#9-not-in-this-plan)
10. [Open questions](#10-open-questions)
11. [Terms](#11-terms)

---

## 1. For the sound designer: start here

**Kehai in one minute:**
- A first-person horror game made in Unity 6. You work a night shift in a small convenience store:
  mop spills, stock shelves, empty bins, serve at the till, walk lost customers to products.
- **Karen** is the store's AI. She hunts you using **only what she senses**: what she sees, what
  she **hears**, and the traces you leave. Her touch kills.
- So sound does two jobs:
  - **It scares the player.** Her warnings, her steps, the PA, the hum of a store at 3 a.m.
  - **It's a game rule.** Everything you do makes a noise, and Karen hears it. Her tricks each give
    a warning sound, so a player who learns them can tell what's coming.
- The art, levels and UI are placeholder quality on purpose; the owner redesigns them before
  launch. **Sound can be good enough to show for now,** but it should be clean, because it'll be
  used in videos and the Steam page.

**What you'll do:** make the sounds in [§5](#5-the-sound-list-by-area), export them to the spec in
[§4](#4-where-the-files-go-and-how-theyre-delivered), and push them to `kehai-assets`. You don't
need Unity or any code. Claude wires them in; the owner listens in the game and tells you what to change.

**Where things are:**

| What | Where | You need it? |
|---|---|---|
| This plan | `docs/you/plans/sfx-plan.md` in `goraxyy/Kehai` (code and docs only) | yes: read it, and tick your boxes ([§7](#7-how-a-task-moves-claim-deliver-wire-hear)) |
| The audio files | `goraxyy/kehai-assets`, folder `Audio/` ([§4](#4-where-the-files-go-and-how-theyre-delivered)) | yes: you push here |
| Where each sound came from, and its licence | [`Assets/!_Project/_Game/Audio/CREDITS.md`](../../../Assets/!_Project/_Game/Audio/CREDITS.md) | yes: add a row per file you deliver |
| Karen's design (what each trick does) | [`docs/agents/karen/Karen.md`](../../agents/karen/Karen.md); in plain words, [`HOW_KAREN_WORKS.md`](../HOW_KAREN_WORKS.md) | for Phase 4 |
| The store's rooms | [`docs/agents/store/STORE.md`](../../agents/store/STORE.md) | for Phase 3 |
| A playable build | sent by the owner ([§6](#6-how-you-hear-your-work-in-the-game)) | to hear your work in context |

**Getting access** (the owner does this, [Y0.2](#phase-0-getting-set-up)): an invite to
`goraxyy/kehai-assets` with write access, and to `goraxyy/Kehai` (read, or write so you can tick
boxes; [Q2](#10-open-questions)). Then:

```bash
git clone https://github.com/goraxyy/kehai-assets.git
```

## 2. What exists today

The game already makes sound in three ways:

| Kind | How it's made | Examples | In the game? |
|---|---|---|---|
| **Free sound files** (CC0) | 40 files from Kenney and OpenGameArt, in `Assets/!_Project/_Game/Audio/` (on the owner's Mac only) | door chime, door creak, till beep, item drop, flashlight click, power down | 11 of them are used; 29 sit unused (mop, chips bag, scream, customer mumble, most variants) |
| **Code-made sounds** | built from sine waves and noise when the game loads (`ProceduralAudio.cs`) | all 25 of Karen's warnings, her steps, the PA chime and her "voice", the player's footsteps, the time clock, the breaker, camera beeps | yes |
| **Radio music** | 3 free jazz tracks and one commercial song, played through the ceiling speakers (`MusicBox.cs`) | `SupermarketJazz_01–03`, `California Gurls.mp3` | the commercial song is not licensed ([Q8](#10-open-questions)) |

**What plays a sound, by moment** (✓ yes, · placeholder, ✗ silent today):

| Moment | Today |
|---|---|
| Player footsteps | · one code-made thud, 3 variants, same on every floor |
| Mopping, stocking, carrying a bin bag | ✗ (Karen hears them; the player doesn't) |
| Picking up, dropping, putting down items | ✓ free files |
| Doors (hinged, automatic), the door chime | ✓ free files |
| The till, serving | ✓ one beep |
| Coffee machine | · code-made pour |
| Customers talking | ✗ (mumble files exist, unused) |
| Lights, breakers, power cut | · one free file and a code-made clunk |
| The store's background (fridges, lights, street) | ✗ nothing: the store is silent between events |
| Karen's warnings (25 of them), her reveal sting, her steps | · code-made |
| The PA: chime and her voice | · code-made chime; the voice is babble with subtitles |
| Getting caught | · the code-made sting only |
| Menus, a task done, shift start and end | ✗ |

**Volume sliders** (Esc → Settings) already split sound into four kinds: **Effects**, **Karen**
(her tricks), **Music** (the radio), **Voice** (the PA). Every new sound belongs to one of them.

## 3. The rules that affect sound

- **No audio in the `Kehai` git repo.** Not `.wav`, not `.ogg`, not project files. Audio goes in
  `kehai-assets`. The Kehai repo only holds code and Markdown.
- **What the player hears and what Karen hears are separate.** Karen "hears" through an event in
  code (`NoiseBus`, with a loudness number per kind of noise, [Karen.md §3.2](../../agents/karen/Karen.md#32-hearing--an-event-bus-not-a-physics-query)).
  A louder file doesn't make her hear better, and a quieter one doesn't hide you. So:
  - **match the file's loudness to her number**, roughly: a sprint (0.9) should sound loud, a
    crouch-step (0.1) almost silent. Otherwise the game lies to the player.
  - Changing what she hears is a game-design change, not a sound change: ask the owner.
- **Karen's warnings must stay easy to tell apart.** Each one means a different trick. Two that
  sound alike would break the game's fairness. Her warnings also have fixed numbers (old replays
  store them), so a warning is never renamed or removed, only re-voiced.
- **Her sounds should feel like hers.** Today they're code-made on purpose: nothing else in the
  store sounds like that. Recorded versions should keep that identity (the owner's call:
  [layered](#phase-4-karen)).
- **Nothing an agent runs makes sound.** Claude's test runs and renders are silent by design. Only
  the owner listens in the game, so every "does it sound right?" check is the owner's.
- **Licences:** every file is either made by you, CC0, or bought with a licence that allows a
  commercial game. Each one gets a row in `CREDITS.md`. Nothing from a commercial song, film or game.

## 4. Where the files go, and how they're delivered

`kehai-assets` is empty today (a README only). Claude sets up its folders in Phase 0
([C0.1](#phase-0-getting-set-up)), mirroring the game's own audio folder:

```
kehai-assets/
└── Audio/
    ├── SFX/<Name>/<Name>_01.wav, _02.wav, _03.wav   finished files, one folder per sound
    ├── Ambience/<Room>/...                          loops
    ├── Karen/<Warning>/...                          her warnings and stings
    ├── UI/...
    ├── Music/...
    ├── _source/                                     your project files and raw recordings
    └── Licences/                                    licence texts or receipts for anything bought
```

**The delivery spec: a proposal.** Confirm or change it in [S0.1](#phase-0-getting-set-up):

| | Proposal | Why |
|---|---|---|
| Format | WAV, 48 kHz, 16-bit (24-bit in `_source/`) | Unity compresses on import; WAV keeps the original clean |
| Channels | **mono** for anything that comes from a place (steps, doors, Karen); **stereo** for ambience beds, music, UI | Unity places mono sounds in 3D; stereo ones play "everywhere" |
| Variants | at least **3** for anything repeated (steps, drops, mop), named `_01`, `_02`, `_03` | the game picks one at random, so it never sounds like a loop |
| Names | `PascalCase`, the same as the folder: `Mop_01.wav` | matches the existing files |
| Loudness | peaks at or below **−1 dBFS**; one-shots of the same kind at about the same loudness; loops at a steady level | the game sets the volume; the files should be even |
| Start and end | no silence at the start; loops cut to loop seamlessly | a late start feels like lag; a click at the loop point is heard every time |
| Length | one-shots under 3 s unless the list says otherwise; loops 20–60 s | memory, and fewer obvious repeats |

**Delivering:** push to `kehai-assets`, add a `CREDITS.md` row per file (or send the rows to the
owner if you have no write access to `Kehai`), and mark the task "delivered" ([§7](#7-how-a-task-moves-claim-deliver-wire-hear)).

## 5. The sound list, by area

**How to read it:** *Karen hears* is the noise kind she gets in code (blank = she doesn't hear
it). *Today* is what plays now. The task number links to the phase.

### 5.1 The player and the chores (Phase 2)

| Sound | When it plays | Karen hears | Today | Variants | Task |
|---|---|---|---|---|---|
| Footsteps: tile floor | walking on the sales floor | Footstep (0.35) | code thud | 6+ | S2.1 |
| Footsteps: concrete, metal, wet floor | stockroom and backstreet, the cold room, a spill | Footstep | code thud | 4 each | S2.1 |
| Sprint steps, crouch steps | running, crouching | Sprint (0.9), CrouchStep (0.1) | code thud | 4 each | S2.1 |
| Breathing when tired | low energy, after a sprint | | ✗ | loop + 3 | S2.2 |
| Mopping | holding the mop on a spill | Mopping (0.5, steady) | ✗ (files exist, unused) | loop + 3 | S2.3 |
| Stocking a shelf | putting a product on a shelf | Stocking (0.45) | item tap | 3 per material (box, can, bottle, bag) | S2.3 |
| Carrying and dumping a bin bag | carrying trash; dropping it in the container | TrashRustle (0.3, steady) | dump only | loop + 3 | S2.3 |
| Picking up, dropping, kicking items | E, Q, walking into items | DroppedItem (0.7), KickedItem | free files | 3 per material | S2.4 |
| Coffee machine | making a coffee | Coffee (0.6) | code pour | 1 + 3 | S2.4 |
| The till: scan, drawer, serve | serving a customer | Serve | one beep | 3 + 1 + 1 | S2.4 |
| Flashlight on and off | F | | free click | 3 | S2.4 |
| The time clock: punch in, punch out, refused | at the clock | TimeClock (0.8) | code tones | 1 each | S2.5 |
| Getting caught | Karen's touch | | code sting | 1–2 | S2.6 |
| Heartbeat | when fear is high | | ✗ | loop | S2.6 |

### 5.2 The store: ambience and machines (Phase 3)

| Sound | Where | Karen hears | Today | Kind | Task |
|---|---|---|---|---|---|
| Room tone, sales floor | the shop floor | | ✗ | stereo loop | S3.1 |
| Room tone per room | stockroom, cold room, staff room, office, WC, lobby | | ✗ | stereo loop each | S3.1 |
| Outside: street, parking lot, backstreet | outside and at the doors | | ✗ | stereo loop | S3.2 |
| Weather (rain on the roof, wind) | everywhere, if wanted ([Q6](#10-open-questions)) | | ✗ | stereo loop | S3.2 |
| Fridges and freezers | the drinks wall, the cold room | | ✗ | mono loop | S3.3 |
| Fluorescent light buzz | each lamp | | ✗ (code hum when it's tampered with) | mono loop | S3.3 |
| Power cut and power back | the breakers | Environmental | free file + code clunk | 1 + 1 | S3.4 |
| Doors: hinged open/close, locked rattle, auto doors | every door | Door, AutoDoor (0.55) | free files | 3 each | S3.4 |
| The door chime | the front door | AutoDoor | free file | 3 | S3.4 |
| Customers: footsteps, mumble, a question | the shoppers | (footsteps only) | ✗ (mumble files unused) | 6+ blips | S3.5 |
| A customer's scream | a customer meeting Karen | | ✗ (files unused) | 3 | S3.5 |

### 5.3 Karen (Phase 4)

Every warning is a **tell**: a sound that means one trick is about to happen. The list, with what
each one means, is `TellKind` in `ProceduralAudio.cs`; what each trick does is in
[Karen.md](../../agents/karen/Karen.md). The live ones:

| Group | Warnings | Task |
|---|---|---|
| The building | lights about to go (ballast whine), a door lock (clunk), automatic doors cycling (motor), the freezer venting fog (hiss), the time clock stopping (buzz), cameras shorting (fizz), the office screens waking (monitor hum) | S4.1 |
| Things she handles | a shelf about to be stripped (rattle), a bucket about to spill (clank), a bin being tampered with (lid clatter), the mop lifted (rattle), a camera bolted on (drill), crates dragged (scrape), shelves on castors (grinding) | S4.2 |
| Her presence | her steps, her steps stopping (silence falls), her steps from elsewhere, a shopper freezing, the chase (screech), watching your blinks (eye flicker), withdrawal (hold music), the reveal sting | S4.3 |
| The PA | the two-tone chime, her voice ([Q5](#10-open-questions)) | S4.4 |

Retired warnings (Flicker, CrtTick, SpeakerCrackle) need nothing.

### 5.4 UI and music (Phase 5)

| Sound | When | Task |
|---|---|---|
| Menu: hover, click, back, slider tick | the Esc menu, settings | S5.1 |
| A task done, a new task | the task list | S5.1 |
| Shift start, shift end, the shift report | each shift | S5.2 |
| Radio music | the store's speakers (licensed or CC0 to replace the commercial song, [Q8](#10-open-questions)) | S5.3 |

## 6. How you hear your work in the game

You don't use Unity, so you need another way to hear a sound in its place.

| Way | How | Best for |
|---|---|---|
| **A build** (proposed default) | after each batch is wired, the owner makes a build (Kehai → Build) and sends it; you play it | hearing everything in context |
| **A clip from the owner** | the owner records a short screen capture, with sound, of the moment | quick checks of one sound |
| **A replay render** | the game can rebuild a recorded shift's sound into a video, mixed offline, silently. Whether it uses the new files needs checking ([C1.5](#phase-1-the-plumbing-code)) | Karen's warnings in a real shift |

Which computer the build is for depends on yours ([Q3](#10-open-questions)).

## 7. How a task moves: claim, deliver, wire, hear

Tracking is tick boxes in this plan. Each sound task in phases 2–5 has three boxes:

```
- [ ] **S2.3 [S] Mopping, stocking, bin bag** (claimed: Name, 15 Nov)
  - [ ] made [S]: files in kehai-assets/Audio/SFX/Mop/ ... , rows in CREDITS.md
  - [ ] wired [C]: plays in the game, at the right moment and slider
  - [ ] heard [You]: sounds right in a shift
```

| Step | Who | What changes on the page |
|---|---|---|
| **Claim** | [S] | add `(claimed: your name, date)` after the task's title |
| **Deliver** | [S] | push the files to `kehai-assets`; tick **made** |
| **Wire** | [C] | import, play at the right moment, check silently (the file loads, a test passes); tick **wired** |
| **Hear** | [You] | play a shift; either tick **heard**, or write what to change under the task and untick **made** |
| **Done** | | all three ticked: tick the task itself, and add one to the progress table |

- **Changes go in a pull request** on the `Kehai` repo, like any doc. If you have no write access
  ([Q2](#10-open-questions)), tell the owner and they tick it.
- **One task at a time per person** keeps it clear who's doing what.
- **Feedback stays on the task:** a short line under it, dated.

## 8. The work, phase by phase

**The rules hold:** no audio in this repo; nothing an agent runs makes sound; one Unity at a time,
never while the owner's editor is open.

### Phase 0: getting set up

- [ ] **Y0.1 [You] Approve this plan** and answer the open questions ([§10](#10-open-questions)).
- [ ] **Y0.2 [You] Invite the sound designer** to `goraxyy/kehai-assets` (write) and `goraxyy/Kehai`
  (read or write, [Q2](#10-open-questions)).
- [ ] **C0.1 Set up `kehai-assets`:** the folders in [§4](#4-where-the-files-go-and-how-theyre-delivered),
  a README with the spec, and today's 40 files copied in as the starting point (with Git LFS if
  you say so, [Q4](#10-open-questions)).
  - Done when: the sound designer can clone it and find every existing sound by name.
- [ ] **C0.2 A listening pack:** every code-made sound saved as a WAV (Karen's 25 warnings, her
  steps, the PA, the player's steps), made silently, into `kehai-assets/Audio/_reference/`.
  - Done when: each warning in [§5.3](#53-karen-phase-4) has a file the designer can listen to.
- [ ] **S0.1 [S] Confirm the delivery spec** in [§4](#4-where-the-files-go-and-how-theyre-delivered),
  or change it in this plan.
- [ ] **Y0.3 [You] Send the first build** ([§6](#6-how-you-hear-your-work-in-the-game)), so the
  designer can hear the game as it is.

### Phase 1: the plumbing (code)

Today most sound files are attached to objects in the Unity scene by hand. The designer can't do
that, and the scene isn't in git. So first, one table in code says which file each sound uses.

- [ ] **C1.1 A sound table in C#:** one entry per sound: its name, its files and variants, its
  slider (Effects, Karen, Music, Voice), its volume and range, mono 3D or everywhere. Today's
  inspector fields keep working until each sound moves over.
  - Done when: a test checks every entry finds its files, or falls back to today's sound.
- [ ] **C1.2 An importer:** **Kehai → Audio → Import from kehai-assets** copies the delivered files
  into the project with the right import settings (mono 3D, streaming for long loops).
- [ ] **C1.3 The missing hooks:** play the sounds the game doesn't play yet (mopping, stocking,
  the bin bag, customers, ambience per room, UI, the heartbeat).
  - Done when: each hook has a test that it fires, and every new sound answers to its slider.
- [ ] **C1.4 Karen's layering:** a recorded warning plays if it exists; otherwise the code-made one.
  Replays use the same rule, so old replays still sound right.
- [ ] **C1.5 Check the replay render** uses the table, so the designer can be sent a shift with sound.

### Phase 2: the player and the chores

Each task has the three boxes from [§7](#7-how-a-task-moves-claim-deliver-wire-hear). The list is
[§5.1](#51-the-player-and-the-chores-phase-2).

- [ ] **S2.1 [S] Footsteps:** tile, concrete, metal, wet; walk, sprint, crouch.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
  - Done when: sprint is clearly louder than walk, and crouch almost silent (Karen's numbers, [§3](#3-the-rules-that-affect-sound)).
- [ ] **S2.2 [S] Breathing when tired.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S2.3 [S] Mopping, stocking, the bin bag.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S2.4 [S] Items, the coffee machine, the till, the flashlight.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S2.5 [S] The time clock:** in, out, refused.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S2.6 [S] Getting caught, the heartbeat.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]

### Phase 3: the store: ambience and machines

The list is [§5.2](#52-the-store-ambience-and-machines-phase-3).

- [ ] **S3.1 [S] Room tone:** the sales floor and each room.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
  - Done when: walking from room to room, the change is heard but not jarring.
- [ ] **S3.2 [S] Outside:** street, parking lot, backstreet, weather if wanted.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S3.3 [S] Machines:** fridges, freezers, light buzz.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S3.4 [S] Power and doors:** power cut and back, every door, the chime.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S3.5 [S] Customers:** steps, mumble, a question, a scream.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]

### Phase 4: Karen

The owner's call: **layered**. Recorded versions play first, the code-made ones stay as the
fallback. Start from the listening pack ([C0.2](#phase-0-getting-set-up)): keep each warning's
idea, rhythm and length, so a player who learned the old one still knows it. The list is
[§5.3](#53-karen-phase-4).

- [ ] **S4.1 [S] The building's warnings** (7).
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
  - Done when: each is told apart from the others, by ear, in a blind test by the owner.
- [ ] **S4.2 [S] Warnings from things she handles** (7).
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S4.3 [S] Her presence:** steps, silence, the chase, the sting (8).
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S4.4 [S] The PA:** the chime, and her voice if [Q5](#10-open-questions) says so.
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S4.5 [S] Blind test sheet:** play the warnings in random order; the owner names each one.
  - Done when: the owner gets at least 20 of 22 right ([Q7](#10-open-questions)).

### Phase 5: UI and music

- [ ] **S5.1 [S] Menu sounds, a task done, a new task.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S5.2 [S] Shift start, shift end, the report.**
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]
- [ ] **S5.3 [S] Radio music** to replace the commercial song (when, [Q8](#10-open-questions)).
  - [ ] made [S] · [ ] wired [C] · [ ] heard [You]

### Phase 6: the mix, licences, sign-off

- [ ] **C6.1 The mix:** the radio and room tone duck under the PA and Karen's warnings; nothing
  clips at the listener (the F1 map's meter, `AudioMeter.cs`, read silently).
- [ ] **S6.1 [S] Levels pass:** after playing a build, a list of what's too loud or too quiet.
- [ ] **C6.2 Licences:** every file has a `CREDITS.md` row; the unused free files are removed or used.
- [ ] **Y6.1 [You] Play two full shifts** with sound and sign it off, or list what's left.
- [ ] **Y6.2 [You] Merge.**

## 9. Not in this plan

- **The redesign before launch** (Milestone 7 of [RELEASE_PLAN.md](../RELEASE_PLAN.md)): new rooms
  will need sound to match. This plan covers today's store.
- **Changing what Karen hears** (her loudness numbers): that's game design, in Karen's own plans.
- **Voice acting** beyond the PA ([Q5](#10-open-questions)).
- **Spatial audio plug-ins** (Steam Audio and similar): Unity's own 3D sound first.

## 10. Open questions

The owner's answers of 14 November are at the top of the page. Still open, with a default if
nobody answers:

| # | Question | Default |
|---|---|---|
| Q1 | The sound designer's name, and their tag in this plan | `[S]` until then |
| Q2 | Their access to `goraxyy/Kehai`: read only, or write (so they can tick their own boxes in a PR)? | write, on branches only |
| Q3 | Their computer (Mac or Windows) and sound tools, for which build to send | ask them in S0.1 |
| Q4 | Git LFS in `kehai-assets` for audio? | yes: audio files are large and change often |
| Q5 | Karen's PA voice: keep the babble with subtitles, a recorded actor, or text-to-speech? Her lines change each shift (she says your name), so a recording can't cover every line | keep the babble, re-voiced by [S] |
| Q6 | Weather outside (rain, wind), or a dry night? | a dry night, light wind |
| Q7 | How strict is the blind test for Karen's warnings (S4.5)? | 20 of 22 |
| Q8 | When does the commercial song go: before the Steam page, the demo, or any video? | before anything public |
| Q9 | A budget for paid sound libraries, or free and self-made only? | free (CC0) and self-made only |
| Q10 | Sound references: games or films whose sound you like for Kehai | none: the designer proposes |
| Q11 | When this runs against 0.5.0 and the vertical slice: alongside, or after? | alongside: sound files don't touch Karen's code |

## 11. Terms

| Term | What it is | How Kehai uses it | Like… |
|---|---|---|---|
| **Tell** | a warning sound that means one thing is about to happen | each of Karen's tricks has its own | the "ding" before a plane announcement |
| **Room tone** | the quiet background a room has even when nothing happens | each room gets its own loop, so the store is never dead silent | the hum you notice in an empty office when the air con stops |
| **One-shot / loop** | a sound that plays once / one that repeats seamlessly | a door is a one-shot; a fridge is a loop | a notification ping / a game's menu music |
| **Variants** | several takes of one sound, picked at random | 3+ per footstep or drop, so nothing repeats | a phone keyboard's slightly different clicks |
| **Mono 3D / stereo** | one channel placed in the world / two channels heard everywhere | steps and doors are mono 3D; ambience and UI are stereo | a sound you can point to in a VR game / music in headphones |
| **dBFS** | how close a file's loudest peak is to the digital maximum (0) | files peak at −1 dBFS or lower | the red light on a mixing desk |
| **Ducking** | turning one sound down while another plays | the radio drops while the PA speaks | music fading in a car when the sat-nav speaks |
| **CC0** | a licence that gives a sound away, no conditions | most of today's files are CC0 | Kenney's free game sounds, used here already |
| **Git LFS** | a git add-on that stores big files outside the normal history | proposed for `kehai-assets` | how cloud drives keep a file's big content separate from the folder list |
| **DAW** | the software sound is made in | the designer's choice | GarageBand, Reaper, Ableton |
