# Kehai sound effects: the plan

> **Status:** draft, for review (14 November 2026). Nothing is built. It starts **after 0.5.0**
> (your call). The questions still open are in [§11](#11-open-questions).
>
> **The shape (your call, 14 November):** **Frixel**, a sound designer, does all of the sound work in a
> **separate Unity project**: today's game with only the basic scripts and the scene, with no
> customers and no Karen. When he's done, the work is merged into the game and the test build.
>
> **Your answers so far (14 November):**
> - **Who:** Frixel, a sound designer. He gets the private repo `goraxyy/kehai-assets` as a collaborator
>   (not this repo).
> - **Files:** in `kehai-assets`, never in this repo.
> - **Tracking:** tick boxes. Frixel can't see this repo, so the task list moves to `kehai-assets`
>   ([§8](#8-how-progress-is-tracked)).
> - **Scope:** everything (the player and chores, the store, Karen, UI and music).
> - **Karen's code-made sounds:** layered. Recorded versions play first, and the code-made one stays as the fallback.
> - **The PA voice:** Frixel makes her "mumble": a babble with subtitles, as today.
> - **Weather:** a dry night on shifts 1–3, heavy rain on shift 4.
> - **Budget:** $1,000 for the whole game ([Q9](#11-open-questions): how much goes to sound).
> - **Inspiration:** Chilla's Art, Hello Neighbor, MiSide ([§2.1](#21-the-feel)).
> - **The radio's commercial song** stays for now (when it goes: [Q8](#11-open-questions)).

**Progress** (update the numbers when you tick a task; once the tracker moves to `kehai-assets`,
this table is kept in step with it):

| Phase | What | Tasks | [Frixel] | [C] | [You] | Done |
|---|---|---|---|---|---|---|
| 0 | Getting set up | 6 | 1 | 1 | 4 | 0 / 6 |
| 1 | The sound project's plumbing | 5 | | 5 | | 0 / 5 |
| 2 | The player and the chores | 7 | 6 | | 1 | 0 / 7 |
| 3 | The store, the weather, the machines | 6 | 5 | | 1 | 0 / 6 |
| 4 | Karen | 6 | 5 | | 1 | 0 / 6 |
| 5 | UI and music | 4 | 3 | | 1 | 0 / 4 |
| 6 | Hand-over and merge | 7 | 2 | 3 | 2 | 0 / 7 |
| | **All** | **41** | **22** | **9** | **10** | **0 / 41** |

**Who's who:**

| Tag | Who | Does |
|---|---|---|
| **[Frixel]** | the sound designer | makes, places and mixes every sound in the sound project |
| **[You]** | the owner | makes the sound project, invites, listens at the end of each phase, signs off |
| **[C]** | Claude | the plumbing that keeps the merge clean (Phase 1, if [Q12](#11-open-questions) says so), and the merge |

1. [For Frixel: start here](#1-for-frixel-start-here)
2. [What exists today](#2-what-exists-today) (and [the feel](#21-the-feel))
3. [The rules that affect sound](#3-the-rules-that-affect-sound)
4. [The sound project](#4-the-sound-project)
5. [How the work comes back: the merge](#5-how-the-work-comes-back-the-merge)
6. [Files: where they go, the spec](#6-files-where-they-go-the-spec)
7. [The sound list, by area](#7-the-sound-list-by-area)
8. [How progress is tracked](#8-how-progress-is-tracked)
9. [The work, phase by phase](#9-the-work-phase-by-phase)
10. [Not in this plan](#10-not-in-this-plan)
11. [Open questions](#11-open-questions)
12. [Terms](#12-terms)

---

## 1. For Frixel: start here

**Kehai in one minute:**
- A first-person horror game made in Unity 6. You work a night shift in a small convenience store:
  mop spills, stock shelves, empty bins, serve at the till, walk lost customers to products.
  A career is four shifts.
- **Karen** is the store's AI. She hunts you using **only what she senses**: what she sees, what
  she **hears**, and the traces you leave. Her touch kills.
- So sound does two jobs:
  - **It scares the player.** Her warnings, her steps, the PA, the hum of a store at 3 a.m.
  - **It's a game rule.** Everything you do makes a noise, and Karen hears it. Each of her tricks
    gives its own warning sound, so a player who learns them can tell what's coming.
- Art, levels and UI are placeholder quality on purpose and will be redesigned before launch.
  Sound should be **clean and good enough to show:** it'll be used in videos and the Steam page.

**What you'll do:** in your own copy of the game (the **sound project**, [§4](#4-the-sound-project)),
make, place and mix every sound in [§7](#7-the-sound-list-by-area). Push your work to `kehai-assets`.
When you're done, it's merged into the real game, so follow the merge rules in
[§5](#5-how-the-work-comes-back-the-merge). They're short, and they matter most.

**What you need:**
- Unity **6000.5.10f1** exactly (Unity Hub), and git with **Git LFS** ([Q4](#11-open-questions)).
- Your own sound tools (DAW) for making the files.
- Access to `goraxyy/kehai-assets` (the owner invites you). Then:

```bash
git lfs install
git clone https://github.com/goraxyy/kehai-assets.git
```

## 2. What exists today

The game already makes sound in three ways:

| Kind | How it's made | Examples | Used? |
|---|---|---|---|
| **Free sound files** (CC0) | 40 files from Kenney and OpenGameArt, in `Assets/!_Project/_Game/Audio/`, listed in its `CREDITS.md` | door chime, door creak, till beep, item drop, flashlight click, power down | 11 used; 29 unused (mop, chips bag, scream, customer mumble, most variants) |
| **Code-made sounds** | built from sine waves and noise when the game loads (`ProceduralAudio.cs`) | Karen's 22 live warnings, her steps, the PA chime and her babble, the player's footsteps, the time clock, the breaker, camera beeps | yes |
| **Radio music** | 3 free jazz tracks and one commercial song, through the ceiling speakers (`MusicBox.cs`) | `SupermarketJazz_01–03`, `California Gurls.mp3` | the song isn't licensed ([Q8](#11-open-questions)) |

**What plays a sound, by moment** (✓ yes, · placeholder, ✗ silent):

| Moment | Today |
|---|---|
| Player footsteps | · one code-made thud, 3 variants, the same on every floor |
| Mopping, stocking, carrying a bin bag | ✗ (Karen hears them; the player doesn't) |
| Picking up, dropping, putting down items | ✓ free files |
| Doors, the door chime | ✓ free files |
| The till, serving | ✓ one beep |
| Coffee machine | · code-made pour |
| Customers talking | ✗ (the mumble files are there but unused) |
| Lights, breakers, power cut | · one free file and a code-made clunk |
| The store's background (fridges, lights, street) | ✗ nothing: the store is silent between events |
| Karen's warnings, her reveal sting, her steps | · code-made |
| The PA: chime and her babble | · code-made |
| Getting caught | · the code-made sting only |
| Menus, a task done, shift start and end | ✗ |

**Volume sliders** (Esc → Settings) split sound into four kinds: **Effects**, **Karen** (her
tricks), **Music** (the radio), **Voice** (the PA). Every sound belongs to one of them.

### 2.1 The feel

What to take from each of the owner's three references (the feel only: no sounds from them):

| Reference | What to take |
|---|---|
| **Chilla's Art** (Japanese convenience-store and night-shift horror) | the mundane made uneasy: fridge hum, fluorescent buzz, a door chime at the wrong moment, long quiet stretches |
| **Hello Neighbor** | a hunter you track by ear: her steps, a door somewhere else, a clear sting when she's on you |
| **MiSide** | a voice that's friendly and wrong: her PA mumble should sound helpful, then not |

## 3. The rules that affect sound

- **No audio in the `Kehai` git repo.** Not `.wav`, not `.ogg`, not project files. Audio goes in
  `kehai-assets`. The Kehai repo only holds code and Markdown.
- **What the player hears and what Karen hears are separate.** Karen "hears" through an event in
  code (`NoiseBus`, with a loudness number per kind of noise, [Karen.md §3.2](../../agents/karen/Karen.md#32-hearing--an-event-bus-not-a-physics-query)).
  A louder file doesn't make her hear better, and a quieter one doesn't hide you. So:
  - **match a file's loudness to her number:** a sprint (0.9) should sound loud, a crouch-step
    (0.1) almost silent. Otherwise the game lies to the player.
  - changing what she hears is game design, not sound: ask the owner.
- **Karen's warnings must stay easy to tell apart.** Each means a different trick; two that sound
  alike break the game's fairness ([Q7](#11-open-questions)). A warning is never renamed or removed,
  only re-voiced: old replays store them by number.
- **Her sounds should feel like hers.** Recorded versions keep each code-made one's idea, rhythm and length.
- **Nothing an agent runs makes sound.** Claude's runs are silent by design, so every "does it
  sound right?" check is a person's.
- **Licences:** every file is made by you, CC0, or bought with a licence that allows a commercial
  game. Each gets a row in `CREDITS.md`. Nothing from a commercial song, film or game.

## 4. The sound project

A separate Unity project, made by the owner from today's game ([Y0.3](#phase-0-getting-set-up)):

| | In the sound project |
|---|---|
| **Kept** | the store scene, the player, the chores, doors, lights, power, the radio, the PA box, the volume sliders |
| **Removed** | customers and Karen (her brain, body and tricks) |
| **Added** (Phase 1) | the **sound board**: a menu that plays any sound at any spot, including the ones whose source was removed (Karen's warnings, her steps, the customers), and **sound spots** for room tones and machines |
| **Where it lives** | `kehai-assets/SoundProject/`, a whole Unity project, with Git LFS |
| **Unity version** | 6000.5.10f1, the same as the game |

Because Karen and the customers are gone, their sounds are placed and heard through the sound board,
not in play. The owner hears them in the real game after the merge ([Y6.2](#phase-6-hand-over-and-merge)).

## 5. How the work comes back: the merge

The game keeps changing while Frixel works (0.5.0's layouts, new tricks). So the merge copies
**his files and his tables**, never his scene. What merges and what doesn't:

| What Frixel changes | Merges? | How |
|---|---|---|
| Sound files, **with their `.meta` files** | ✓ | copied folder for folder. The `.meta` keeps each file's ID, so nothing has to be re-linked |
| The **mixer** (an `AudioMixer` asset) | ✓ | copied as it is |
| The **sound table** (which file each sound uses, its slider, volume, range; [C1.1](#phase-1-the-sound-projects-plumbing)) | ✓ | a C# file, merged into the game's code by Claude |
| The **sound spots** (where each room tone, fridge, lamp sits; [C1.2](#phase-1-the-sound-projects-plumbing)) | ✓ | a C# table too, rebuilt in the real scene by a menu, the way `StoreLayout.cs` stocks the shelves |
| Small changes to existing scripts (a new hook in `PlayerMotor`, say) | ✓ with care | listed in the hand-over note, merged by Claude. Keep them few and small |
| **The scene** (objects added, moved, AudioSources placed by hand) | ✗ | can't be merged: the real scene will have changed. Anything placed must be a sound spot |
| **Prefabs** | ✗ | same as the scene |
| Project audio settings (voices, DSP buffer) | ✓ by hand | written in the hand-over note; the owner sets them |

**Frixel's three rules:**
1. Everything you make goes under `Assets/!_Project/_Game/Audio/`.
2. Don't rename, move or delete existing files outside it.
3. Never place a sound by hand in the scene: add a sound spot instead. If something can't be done that way, ask.

Then the merge is mostly copying, and [C1.5](#phase-1-the-sound-projects-plumbing)'s check says
whether anything was changed that can't merge.

## 6. Files: where they go, the spec

```
kehai-assets/
├── SFX.md                                   Frixel's guide and the task list (§8)
├── SoundProject/                            the sound project (§4)
│   └── Assets/!_Project/_Game/Audio/
│       ├── SFX/<Name>/<Name>_01.wav …       one folder per sound
│       ├── Ambience/<Room>/…                loops
│       ├── Karen/<Warning>/…                her warnings, stings, mumble
│       ├── UI/…  Music/…
│       └── CREDITS.md                       one row per file
└── Audio/
    ├── _reference/                          the code-made sounds as WAVs, to listen to (C0.1)
    ├── _source/                             project files and raw recordings
    └── Licences/                            licence texts or receipts for anything bought
```

**The file spec: a proposal.** Confirm or change it in [F0.1](#phase-0-getting-set-up):

| | Proposal | Why |
|---|---|---|
| Format | WAV, 48 kHz, 16-bit (24-bit in `_source/`) | Unity compresses on import; WAV keeps the original clean |
| Channels | **mono** for anything that comes from a place (steps, doors, Karen); **stereo** for ambience beds, music, UI | Unity places mono sounds in 3D; stereo ones play everywhere |
| Variants | at least **3** for anything repeated (steps, drops, mop), named `_01`, `_02`, `_03` | picked at random, so it never sounds like a loop |
| Names | `PascalCase`, the same as the folder: `Mop_01.wav` | matches the existing files |
| Loudness | peaks at or below **−1 dBFS**; sounds of one kind about equally loud; loops at a steady level | the game sets the volume; the files should be even |
| Start and end | no silence at the start; loops seamless | a late start feels like lag; a click at the loop point is heard every time |
| Length | one-shots under 3 s unless the list says otherwise; loops 20–60 s | memory, and fewer obvious repeats |

## 7. The sound list, by area

*Karen hears* is the noise kind she gets in code (blank: she doesn't hear it).

### 7.1 The player and the chores (Phase 2)

| Sound | When it plays | Karen hears | Today | Variants | Task |
|---|---|---|---|---|---|
| Footsteps: tile floor | walking on the sales floor | Footstep (0.35) | code thud | 6+ | F2.1 |
| Footsteps: concrete, metal, wet floor | stockroom and backstreet, the cold room, a spill | Footstep | code thud | 4 each | F2.1 |
| Footsteps in the rain | outside on shift 4 | Footstep | code thud | 4 | F2.1 |
| Sprint steps, crouch steps | running, crouching | Sprint (0.9), CrouchStep (0.1) | code thud | 4 each | F2.1 |
| Breathing when tired | low energy, after a sprint | | ✗ | loop + 3 | F2.2 |
| Mopping | holding the mop on a spill | Mopping (0.5, steady) | ✗ (files unused) | loop + 3 | F2.3 |
| Stocking a shelf | putting a product on a shelf | Stocking (0.45) | item tap | 3 per material (box, can, bottle, bag) | F2.3 |
| Carrying and dumping a bin bag | carrying trash; dropping it in | TrashRustle (0.3, steady) | dump only | loop + 3 | F2.3 |
| Picking up, dropping, kicking items | E, Q, walking into items | DroppedItem (0.7), KickedItem | free files | 3 per material | F2.4 |
| Coffee machine | making a coffee | Coffee (0.6) | code pour | 1 + 3 | F2.4 |
| The till: scan, drawer, serve | serving a customer | Serve | one beep | 3 + 1 + 1 | F2.4 |
| Flashlight on and off | F | | free click | 3 | F2.4 |
| The time clock: in, out, refused | at the clock | TimeClock (0.8) | code tones | 1 each | F2.5 |
| Getting caught | Karen's touch | | code sting | 1–2 | F2.6 |
| Heartbeat | when fear is high | | ✗ | loop | F2.6 |

### 7.2 The store, the weather, the machines (Phase 3)

| Sound | Where | Karen hears | Today | Kind | Task |
|---|---|---|---|---|---|
| Room tone, sales floor | the shop floor | | ✗ | stereo loop | F3.1 |
| Room tone per room | stockroom, cold room, staff room, office, WC, lobby | | ✗ | stereo loop each | F3.1 |
| Outside, a dry night | street, parking lot, backstreet (shifts 1–3) | | ✗ | stereo loop | F3.2 |
| **Heavy rain** (shift 4) | on the roof inside; on the street outside; on the windows; a gust as a door opens | | ✗ | stereo loops + 3 one-shots | F3.2 |
| Fridges and freezers | the drinks wall, the cold room | | ✗ | mono loop | F3.3 |
| Fluorescent light buzz | each lamp | | ✗ | mono loop | F3.3 |
| Power cut and power back | the breakers | Environmental | free file + code clunk | 1 + 1 | F3.4 |
| Doors: open, close, locked rattle, automatic doors | every door | Door, AutoDoor (0.55) | free files | 3 each | F3.4 |
| The door chime | the front door | AutoDoor | free file | 3 | F3.4 |
| Customers: footsteps, mumble, a question, a scream | the shoppers (on the sound board) | footsteps only | ✗ (files unused) | 6+ blips, 3 screams | F3.5 |

### 7.3 Karen (Phase 4)

Every warning is a **tell**: a sound that means one trick is about to happen. The code-made
versions are in `_reference/` ([C0.1](#phase-0-getting-set-up)); what each trick does is in
[Karen.md](../../agents/karen/Karen.md). The 22 live ones:

| Group | Warnings | Task |
|---|---|---|
| The building (7) | lights about to go (ballast whine), a door lock (clunk), automatic doors cycling (motor), the freezer venting fog (hiss), the time clock stopping (buzz), cameras shorting (fizz), the office screens waking (monitor hum) | F4.1 |
| Things she handles (7) | a shelf about to be stripped (rattle), a bucket about to spill (clank), a bin tampered with (lid clatter), the mop lifted (rattle), a camera bolted on (drill), crates dragged (scrape), shelves on castors (grinding) | F4.2 |
| Her presence (7, plus her steps) | her steps stopping (silence falls), her steps from elsewhere, a shopper freezing, the chase (screech), watching your blinks (eye flicker), withdrawal (hold music), the reveal sting | F4.3 |
| The PA (1, plus her voice) | the two-tone chime; her **mumble**: the babble under each subtitle, sounding friendly, then wrong | F4.4 |

### 7.4 UI and music (Phase 5)

| Sound | When | Task |
|---|---|---|
| Menu: hover, click, back, slider tick | the Esc menu, settings | F5.1 |
| A task done, a new task | the task list | F5.1 |
| Shift start, shift end, the report | each shift | F5.2 |
| Radio music | the store's speakers, licensed or CC0, to replace the commercial song ([Q8](#11-open-questions)) | F5.3 |

## 8. How progress is tracked

Tick boxes, as you asked. Frixel can't see this repo, so in [C0.1](#phase-0-getting-set-up) the
guide (§1, §3–§7) and the task list (§9) **move** to `kehai-assets/SFX.md`. That copy becomes the
one place they're kept; this page keeps the decisions, the progress table and a link.

| Step | Who | On the page |
|---|---|---|
| **Start a task** | Frixel | add `(started: date)` after its title |
| **Done** | Frixel | it plays in the sound project, at the right moment and slider, and its files have `CREDITS.md` rows: tick it, and push |
| **Phase check** | you | at the end of each phase, listen (a build or a screen recording from Frixel, [Q3](#11-open-questions)); tick your task, or write what to change under the tasks, dated |
| **The table** | Claude | keeps this page's progress table in step |

## 9. The work, phase by phase

### Phase 0: getting set up

- [ ] **Y0.1 [You] Approve this plan** and answer what's left in [§11](#11-open-questions).
- [ ] **Y0.2 [You] Invite Frixel** to `goraxyy/kehai-assets` (write).
- [ ] **C0.1 Set up `kehai-assets`:** the folders in [§6](#6-files-where-they-go-the-spec), Git LFS
  for audio and Unity files (if [Q4](#11-open-questions) says yes), `SFX.md` ([§8](#8-how-progress-is-tracked)),
  and `_reference/`: every code-made sound saved as a WAV, made silently.
  - Done when: Frixel can clone it, and each of Karen's 22 warnings has a file to listen to.
- [ ] **Y0.3 [You] Make the sound project** ([§4](#4-the-sound-project)) and push it to
  `kehai-assets/SoundProject/`. Claude can do the removing if you want.
  - Done when: it opens on a clean clone and a shift plays with no customers and no Karen.
- [ ] **F0.1 [Frixel] Install and check:** Unity 6000.5.10f1, Git LFS; open the project; confirm or
  change the file spec ([§6](#6-files-where-they-go-the-spec)).
- [ ] **Y0.4 [You] Ask Frixel's computer** (Mac or Windows) and tools ([Q3](#11-open-questions)).

### Phase 1: the sound project's plumbing

Before Frixel starts, so his work merges cleanly ([§5](#5-how-the-work-comes-back-the-merge)).
Built once in the game's code, then copied into the sound project. Who builds it: [Q12](#11-open-questions).

- [ ] **C1.1 The sound table in C#:** one line per sound: its name, files and variants, slider,
  volume and range, 3D or everywhere. Today's inspector fields keep working until each sound moves over.
  - Done when: a test checks every entry finds its files, or falls back to today's sound.
- [ ] **C1.2 Sound spots in C#:** position, sound, range, per room tone, machine and lamp; a menu
  places them in the scene, and the shift picks the weather (rain on shift 4).
- [ ] **C1.3 The sound board:** **Kehai → Audio → Sound Board**: play any sound in the table at a
  chosen spot, including Karen's and the customers'.
- [ ] **C1.4 Hooks and Karen's layering:** play what's silent today (mopping, stocking, the bin bag,
  breathing, heartbeat, UI); a recorded warning plays if it exists, the code-made one if not, in play and in replays.
- [ ] **C1.5 The merge check:** a script that lists what changed in the sound project against the
  game, and flags anything that can't merge (scene, prefabs, files moved).

### Phase 2: the player and the chores

The list is [§7.1](#71-the-player-and-the-chores-phase-2).

- [ ] **F2.1 [Frixel] Footsteps:** tile, concrete, metal, wet, rain; walk, sprint, crouch.
  - Done when: sprint is clearly louder than walk, and crouch almost silent ([§3](#3-the-rules-that-affect-sound)).
- [ ] **F2.2 [Frixel] Breathing when tired.**
- [ ] **F2.3 [Frixel] Mopping, stocking, the bin bag.**
- [ ] **F2.4 [Frixel] Items, the coffee machine, the till, the flashlight.**
- [ ] **F2.5 [Frixel] The time clock:** in, out, refused.
- [ ] **F2.6 [Frixel] Getting caught, the heartbeat.**
- [ ] **Y2.1 [You] Listen to Phase 2.**

### Phase 3: the store, the weather, the machines

The list is [§7.2](#72-the-store-the-weather-the-machines-phase-3).

- [ ] **F3.1 [Frixel] Room tone:** the sales floor and each room.
  - Done when: walking from room to room, the change is heard but not jarring.
- [ ] **F3.2 [Frixel] Outside:** a dry night; heavy rain for shift 4.
  - Done when: the rain is heavy outside and muffled inside, and Karen's warnings still cut through it.
- [ ] **F3.3 [Frixel] Machines:** fridges, freezers, light buzz.
- [ ] **F3.4 [Frixel] Power and doors:** power cut and back, every door, the chime.
- [ ] **F3.5 [Frixel] Customers:** steps, mumble, a question, a scream (on the sound board).
- [ ] **Y3.1 [You] Listen to Phase 3.**

### Phase 4: Karen

Layered: recorded versions first, the code-made ones as the fallback. Start from `_reference/`:
keep each warning's idea, rhythm and length, so a player who learned the old one still knows it.
The list is [§7.3](#73-karen-phase-4).

- [ ] **F4.1 [Frixel] The building's warnings** (7).
- [ ] **F4.2 [Frixel] Warnings from things she handles** (7).
- [ ] **F4.3 [Frixel] Her presence:** steps, silence, the chase, the sting (7, plus her steps).
- [ ] **F4.4 [Frixel] The PA:** the chime, and her mumble.
- [ ] **F4.5 [Frixel] The blind test sheet:** the 22 warnings in random order, numbered, with the
  answers kept apart ([Q7](#11-open-questions)).
- [ ] **Y4.1 [You] Take the blind test, and listen to Phase 4.**
  - Done when: the test passes at the bar [Q7](#11-open-questions) sets.

### Phase 5: UI and music

- [ ] **F5.1 [Frixel] Menu sounds, a task done, a new task.**
- [ ] **F5.2 [Frixel] Shift start, shift end, the report.**
- [ ] **F5.3 [Frixel] Radio music** to replace the commercial song (when: [Q8](#11-open-questions)).
- [ ] **Y5.1 [You] Listen to Phase 5.**

### Phase 6: hand-over and merge

- [ ] **F6.1 [Frixel] Levels pass:** the mixer set so the radio and room tone dip under the PA and
  Karen's warnings, and nothing clips.
- [ ] **F6.2 [Frixel] The hand-over note:** every script he changed and why, the project audio
  settings, and a `CREDITS.md` row for every file.
- [ ] **C6.1 Run the merge check** (C1.5) and fix what it flags with Frixel.
- [ ] **C6.2 Merge into the game:** the files and mixer copied in, the tables and hooks in one pull
  request, the sound spots placed in the real scene, the unused free files removed. The training
  version (0.5.0) stays silent.
  - Done when: every EditMode test passes, and a silent check finds every sound in the table.
- [ ] **C6.3 Merge into the test build** (playtest v2), the same way.
- [ ] **Y6.1 [You] Play two full shifts** with Karen and customers back, the 4th with rain; sign
  off, or list what's left for Frixel.
- [ ] **Y6.2 [You] Merge.**

## 10. Not in this plan

- **The redesign before launch** (Milestone 7, [RELEASE_PLAN.md](../RELEASE_PLAN.md)): new rooms
  will need sound to match. Sound spots in C# make that a matter of new rows.
- **Changing what Karen hears** (her loudness numbers): that's game design, in Karen's own plans.
- **Voice acting.** Her PA stays a mumble with subtitles.
- **Spatial audio plug-ins** (Steam Audio and similar): Unity's own 3D sound first.

## 11. Open questions

| # | Question | Answer, or the default |
|---|---|---|
| Q1 | Name | **Frixel** (14 Nov) |
| Q2 | Access | **`kehai-assets` as a collaborator** (14 Nov); the tracker moves there ([§8](#8-how-progress-is-tracked)) |
| Q3 | Frixel's computer and tools | **you'll ask him** (Y0.4). It decides how you send him builds, and whether he can send screen recordings back |
| Q4 | Git LFS in `kehai-assets`? | **open:** pros and cons below. Default: yes, set up before the first push |
| Q5 | Karen's PA voice | **her mumble, by Frixel** (14 Nov) |
| Q6 | Weather | **a dry night; heavy rain on shift 4** (14 Nov) |
| Q7 | How the warnings are tested | **open:** expanded below. Default: no two confused, 20 of 22 named |
| Q8 | When the commercial song goes | **open:** expanded below. Default: before any build leaves your Mac |
| Q9 | How much of the $1,000 goes to sound? | **open.** Default: free first (CC0, Sonniss's free GDC bundles, self-recorded); up to **$150** on packs, only with your yes per purchase |
| Q10 | References | **Chilla's Art, Hello Neighbor, MiSide** (14 Nov), [§2.1](#21-the-feel) |
| Q11 | When | **after 0.5.0** (14 Nov). Open: before or after the vertical slice? The slice is "a convincing playable shift", and sound is much of convincing. Default: inside the slice |
| Q12 | Who builds Phase 1's plumbing: Claude (before Frixel starts), or Frixel? | **open.** Default: Claude, so the merge stays clean and Frixel only does sound |

**Q4: Git LFS, pros and cons.**
Git LFS keeps big files (audio, the Unity project's scene and models) out of git's normal history:
git stores a small pointer, and the file itself sits on GitHub's LFS storage.

| Pros | Cons |
|---|---|
| Clones and pulls stay fast: you download only the current version of each file, not every old export | **Quota:** GitHub Free includes 10 GiB of LFS storage and 10 GiB of downloads a month. Every clone and pull by you, Frixel and the Windows PC counts. Past it, uploads stop (or you pay about $0.07 per GiB stored and $0.09 per GiB downloaded) |
| Normal git rejects files over 100 MB; LFS takes them | Everyone must run `git lfs install` once; a machine without it gets pointer files instead of sounds |
| The usual setup for Unity projects | Switching it on later means rewriting the repo's history, so decide before the first push |

The sound project with today's art is roughly 200 MB plus audio, so it fits the quota if clones stay few.
**Recommendation: yes,** from the start.

**Q7: the warnings test, expanded.**
- **Why:** each of Karen's 22 warnings means a different trick. A player who learns them can
  react: hear the bucket clank, step away from the spill. If two sound alike, the player can't
  tell which trick is coming. That makes the game unfair, and it's the reason the warnings exist.
- **The test:** Frixel makes a sheet of the 22 warnings in random order, numbered, without names
  (F4.5). You listen and write which trick each one is. Then you check against his answer key.
- **What to decide:**

  | Bar | Means |
  |---|---|
  | A. 22 of 22 | every warning named; strict, and you already know the tricks well |
  | B. **20 of 22, and no pair confused both ways** (default) | a slip is fine; two warnings that swap each other must be redone |
  | C. B, and a second person who's never played names at least half after one listen to the list | checks they can be learned, not only remembered |

**Q8: the commercial song, expanded.**
- `California Gurls.mp3` (Katy Perry) plays on the store radio in the scene. It's under copyright.
  Using it in a game needs a licence from the song's publisher and its record label, and is rarely
  affordable for a small game.
- **Where it would bite:**

  | Moment | Risk |
  |---|---|
  | Private play on your Mac | none in practice |
  | The sound project you send Frixel | a copy of the song leaves your Mac. Small, but avoidable: leave it out of the sound project (the default) |
  | Videos (YouTube, TikTok, Instagram) | the platforms find the song automatically and mute, claim or block the video. Today's bot videos rebuild sound from replays, which don't include the radio, so they're safe; a screen recording of play isn't |
  | Playtest builds for strangers (playtest v2), the demo, Steam | distributing it; Steam can take the game down |
- **What to decide:** when it goes. Default: before any build leaves your Mac. In practice, before
  playtest v2's first round, with F5.3's music in its place.

## 12. Terms

| Term | What it is | How Kehai uses it | Like… |
|---|---|---|---|
| **Tell** | a warning sound that means one thing is about to happen | each of Karen's tricks has its own | the chime before a plane announcement |
| **Room tone** | the quiet background a room has even when nothing happens | each room gets its own loop, so the store is never dead silent | the hum you notice in an empty office when the air con stops |
| **One-shot / loop** | a sound that plays once / one that repeats seamlessly | a door is a one-shot; a fridge is a loop | a notification ping / a game's menu music |
| **Variants** | several takes of one sound, picked at random | 3+ per footstep or drop, so nothing repeats | a phone keyboard's slightly different clicks |
| **Mono 3D / stereo** | one channel placed in the world / two channels heard everywhere | steps and doors are mono 3D; ambience and UI are stereo | a sound you can point to in a VR game / music in headphones |
| **dBFS** | how close a file's loudest peak is to the digital maximum (0) | files peak at −1 dBFS or lower | the red light on a mixing desk |
| **Mixer and ducking** | Unity's mixing desk; ducking turns one sound down while another plays | the radio dips while the PA speaks | music fading in a car when the sat-nav speaks |
| **`.meta` file** | Unity's note beside each file, holding its ID | copying it keeps every link to the sound working after the merge | a library book's barcode: move the book, the catalogue still finds it |
| **Sound spot** | a row in a C# table: a place, a sound, a range | room tones, fridges, lamps; rebuilt in any version of the scene | a store's planogram: the plan, not the shelf |
| **Sound board** | a menu that plays any sound at any spot | hears Karen's and the customers' sounds in a project without them | a DJ's sample pad |
| **CC0** | a licence that gives a sound away, no conditions | most of today's files are CC0 | Kenney's free game sounds, used here already |
| **Git LFS** | a git add-on that stores big files outside the normal history | proposed for `kehai-assets` (Q4) | a cloud drive that lists the folder but fetches a file only when you open it |
| **DAW** | the software sound is made in | Frixel's choice | GarageBand, Reaper, Ableton |
