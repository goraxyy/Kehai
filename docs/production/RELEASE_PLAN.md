# Kehai — Release Plan

> Working title: **Kehai** · Studio: **TokenLimit** · Engine: Unity 6 (URP)
> Current version: `0.1.0` · Build targets: StandaloneOSX, StandaloneWindows64 · Plan last updated: 2026-10-04

A milestone-by-milestone route from the current prototype to a Steam release. Each
milestone has an **exit test** — a single question that has to be answerable with "yes"
before the next milestone starts. Milestones overlap in calendar time; they do not
overlap in exit tests.

**Legend**

| Mark | Meaning |
|------|---------|
| `[x]` | Done and verified |
| `[~]` | Partially done — detail in the note beside it |
| `[ ]` | Not started |

---

## Where the project actually stands

Everything below marked `[x]` was checked against the project on 2026-09-20, not
assumed. The honest summary:

**Built and working.** A full shift loop — clock in at the puncher, serve customers,
restock, mop, bin the trash, clock out — inside a 150×150 m roofed store with 189 shelf
units, 240 ceiling lights and 64 speakers. Plus the burnout meter, the mains/blackout
system, the store radio, the flashlight, and a first SFX pass.

**The three biggest gaps.** There is no antagonist in the level (`EnemyAI.cs` exists but
has zero scene instances, and `AIKO.md` is a design spec with no implementation). There
are no menus of any kind — no main menu, pause, or options. And **no build has ever been
produced**, so nothing has been tested outside the editor.

*Update — the antagonist gap is closed:* Aiko (`AIKO.md`, all of it; status in §15 there)
now installs into the store when it loads and replaces `EnemyAI.cs`. She has been exercised
headless by simulated players across every ablation rung (`AIKO_RESULTS.md`), not yet by a
person at the keyboard.

*Update 2026-10-02 — the other two gaps are closed:* the game has a main menu, a pause menu
with its settings, and a saved career; and the first macOS build has been made (see
"First build" below).

---

## Milestone 0 — Prototype

**Exit test: can one person play a complete shift start to finish without the editor's help?**
Status: **reached**.

### Core player
- [x] First-person controller — walk, sprint, crouch, jump
- [x] Mouse look with adjustable sensitivity
- [x] Interaction system on a single verb (`E`), drop/throw on `Q` with charge-up
- [x] 4-slot inventory with HUD, number keys + scroll to switch
- [x] Carryable items with physics, impact sounds, and throw arcs
- [x] Bulky-tool carry path (mop, crate, bin) separate from the inventory
- [x] Hover outlines on every interactable

### Shift loop
- [x] Clock in / clock out at the puncher
- [x] Shift timer with an inspector-adjustable duration (currently 300 s)
- [x] Live task checklist UI (`C` to toggle)
- [x] Shift cannot be closed while tasks are outstanding
- [~] Between-shift progression — shift index exists and feeds starting energy, but
      nothing persists across a session

### Jobs
- [x] Restocking — stock crate refills a shelf unit
- [x] Mopping — hold `E` with the mop, spill shrinks and fades as it cleans
- [x] Trash — bag a full bin, carry it to the skip out back
- [x] Serving — customers queue at the till and wait to be served
- [x] Directions — customers ask where a product is, you escort them to the shelf

### Customers
- [x] NavMesh spawn → browse → take stock → queue → be served → leave
- [x] Chance-based spills, bin usage, mess
- [x] Speech bubbles and overhead cone markers
- [x] Freeze in place during a blackout

### Systems
- [x] Burnout meter — drains over the shift, coffee restores it, gates sprinting
- [x] Burnout vision — vignette + Gaussian blur ramping below 50 % energy
- [x] Mains power — `L` kills lights and music, breaker box restores
- [x] Store radio — random track from a playlist, 64 synced speakers, distance falloff
- [x] Flashlight — carryable, toggles with `E`

### Level
- [x] 150×150 m maze-shaped store blockout with roof
- [x] Ceiling light grid (240 cone lights on the 5 m square grid)
- [x] Baked NavMesh
- [x] Five prop models placed on Models_Island

### Pipeline
- [x] Code-only GitHub mirror with PR workflow (#1–#6)
- [x] `AIKO.md` — 1,097-line design spec for the adaptive antagonist
- [x] First SFX pass — door chime, till beep, door creaks, flashlight, item impacts,
      pickup, power-down

---

## Milestone 1 — Vertical Slice

**Exit test: does 10 minutes of this game convince a stranger the full game is worth playing?**

The slice is one shift, at final quality, with the antagonist in it. It is the thing you
show publishers, put in a trailer, and cut a demo from. Nothing here is about scope —
it is about one slice being *finished*.

### Antagonist — the biggest single gap
- [x] Get `EnemyAI` into the scene with patrol points and a NavMesh route
      *superseded: Aiko installs herself from code; `EnemyAI.cs` is retired*
- [x] Tune the existing FSM (Patrol / Sabotage / Search / Chase) until it reads as
      deliberate rather than random
      *superseded: belief grid, utility goals and a tactic library replace the FSM*
- [x] Line-of-sight and hearing that the player can reason about
      *graded sight, a noise bus with per-action loudness, and a tell before every tactic*
- [x] A losing state — what actually happens when it catches you
      *a written warning: a 30 s lecture, overtime, and a recovery window; burnout ending*
- [x] Pick the Aiko scope: full `AIKO.md` architecture, or a cut-down version for ship
      *Decided: the full architecture, every rung switchable for the ablation*
- [ ] Play-test and tune Aiko with people — so far she has only been measured against
      scripted players

### Feel and readability
- [ ] Player animation — hands, held-item poses, footsteps
- [ ] Customer animation — walk cycle, idle, browse
- [ ] Interaction feedback pass — prompts, hit confirms, task-complete stingers
- [ ] Audio pass 2 — footsteps, ambience, room tone, UI sounds
- [x] Audio mixer with music / SFX / master groups (needed before options menu)
      *done in code rather than an AudioMixer asset: `SoundSettings` puts every sound in a group
      (effects, Aiko, music, voice) under a master volume, with a slider for each in Esc*
- [ ] Lighting and mood pass on the slice area
- [ ] Camera polish — head bob, FOV on sprint, damping

### The shell the game currently has none of
- [x] Main menu
      *`MainMenu.cs`: Continue, New career, Settings, Controls, Quit, over the paused store*
- [x] Pause menu
      *Esc: restart the shift, leave for the main menu, the settings, the keys*
- [~] Options — sensitivity, volume sliders, resolution, quality
      *sensitivity, volume by kind of sound, Aiko's floor cone, the webcam; no resolution or
      quality yet*
- [x] Save/load, or an explicit decision that runs are session-only
      *Decided: the career is saved after every shift (Aiko's ledger); Continue resumes at the
      next shift, and a shift left halfway isn't saved. New career wipes it.*
- [x] Game-over and shift-summary screens
      *the Performance Review and the shift report after every shift; the burnout and
      notice endings, and Enter from them to the main menu*

### First build
- [x] **Produce a build.** Nothing has been built yet — expect to find problems here
      *2026-10-02: `KehaiBuild.MacOS` → `Builds/macOS/Kehai.app`, 158 MB, universal (Apple
      silicon and Intel), 10 minutes headless, no errors or warnings. A bot shift played
      through in the built game (headless) with no errors, and recorded itself.*
- [~] Fix whatever only breaks outside the editor (shader stripping, missing refs,
      `Resources` paths, script execution order)
      *found and fixed: two URP shaders the game makes materials from by name were stripped
      (the build script now always includes them); the webcam helper beside the app, and the
      camera line macOS needs in Info.plist; Japanese text in banners. Still to see with a
      person at the screen: the menus in a window, webcam blinking, frame rate*
- [x] Windows build target added alongside macOS
      *2026-10-04: `KehaiBuild.Windows` (64-bit, Mono), built from the Mac. Not yet run on a
      Windows PC; no webcam helper there yet (keyboard blink only)*
- [~] Set `applicationIdentifier` (currently empty) and a real version scheme
      *identifier `com.tokenlimit.kehai`; the version is still 0.1.0, and every build writes
      the commit it came from to `build.txt`*

---

## Milestone 2 — Steam Page Live

**Exit test: can someone wishlist this game?**

Run this *in parallel* with the vertical slice. The page should go up the moment you have
one good screenshot set and a short capsule — wishlists compound, and every week the page
is not live is wishlists you never get.

### Steamworks setup
- [ ] Register as a Steamworks partner — pay the $100 app deposit
- [ ] Complete tax and bank identity forms *(this gates release; it takes real time)*
- [ ] Reserve the app ID and the store page URL
- [ ] Decide the final title — check "Kehai" for trademark and store-search collisions

### Store page assets
- [ ] Capsule art — main, small, header, library (a real artist; this is the single
      highest-leverage art buy)
- [ ] 5+ screenshots that read at thumbnail size
- [ ] Short description (one sentence that makes the hook obvious)
- [ ] Long description with feature bullets and GIFs
- [ ] Announcement trailer — 60–90 s, hook in the first 5 seconds
- [ ] Tags, genre, categories
- [ ] System requirements *(from the first real builds — do not guess)*
- [ ] Age rating questionnaire
- [ ] Steam Deck compatibility target — decide yes/no

### Page live
- [ ] Submit for review (allow 3–5 business days)
- [ ] **Page live as "Coming Soon"**
- [ ] Start counting wishlists weekly from day one

---

## Milestone 3 — Playtesting

**Exit test: do players understand what to do without being told?**

Start the moment the vertical slice is playable. Playtesting is not QA — you are
watching for confusion, not crashes.

### Internal
- [ ] Play your own build every week, start to finish
- [ ] Keep a running list of "things I explained out loud" — every one is a design bug
- [~] Instrument the build: shift completion times, task failure rates, deaths
      *every shift is recorded (positions, events, an HTML report, the 3D replay). Playtest
      builds record the whole session and pack it to send (PLAYTEST.md, step 1); the upload
      service and the reports on the developer's side are steps 2–3*

### External
- [ ] 5 first-time players, watched over the shoulder, no help given
- [ ] Record sessions (screen + audio) with permission
- [ ] Post-session survey — what confused you, what did you enjoy, would you play more
- [ ] Round 2 after fixes, with fresh players who have never seen it
- [ ] Friends-and-family pass on the Steam build via a playtest branch

### What to watch for specifically
- [ ] Does the task list explain itself, or do players wander?
- [ ] Is the antagonist scary or annoying? (The line is thin and it is found by watching)
- [ ] Is the burnout meter legible as a resource, or just a screen effect?
- [ ] Can players find the breaker box in a blackout without a hint?
- [ ] Is the store maze navigable, or do players get lost in a bad way?

---

## Milestone 4 — Demo

**Exit test: does the demo end with the player annoyed that it ended?**

- [ ] Cut the demo from the vertical slice — one or two shifts, hard stop
- [ ] Demo-specific ending screen with a wishlist call to action
- [ ] Separate Steam depot and app for the demo
- [ ] Demo-only telemetry — completion rate, where players quit
- [ ] Ship the demo alongside a festival, not on a quiet week

### Festival targets
- [ ] Steam Next Fest — **pick the edition carefully**; you can only do this once, and
      only before release
- [ ] Apply 4–6 weeks ahead of the chosen edition
- [ ] Plan a content push for the week the fest runs
- [ ] Consider a smaller festival first as a rehearsal

---

## Milestone 5 — Marketing

**Exit test: is there a place people can follow this that is not the Steam page?**

Runs continuously from Milestone 2 onward. The goal is a wishlist curve, not virality.

### Foundation
- [ ] Pick the hook and write it in one sentence *(overworked clerk vs. a supervisor who
      learns your habits — lead with what is distinctive)*
- [ ] Press kit — screenshots, logo, fact sheet, contact, trailer
- [ ] Devlog home — one place that is yours, not a platform's

### Ongoing
- [ ] Post work-in-progress weekly somewhere with an audience for the genre
- [ ] Short-form video — the blackout, the chase, the mess. These travel well
- [ ] Build a mailing list; it is the only channel no algorithm can take away
- [ ] Reach out to streamers and YouTubers who cover horror/sim (start 2 months before
      launch, not the week of)
- [ ] Send press keys ahead of launch with an embargo
- [ ] Line up a launch-day discount and a Steam event

### Milestones worth a beat
- [ ] Steam page announcement
- [ ] Demo launch
- [ ] Next Fest participation
- [ ] Release date reveal
- [ ] Launch

---

## Milestone 6 — Content Complete / Beta

**Exit test: is every feature in, even if none of them are polished?**

- [ ] All shifts / levels present and playable
- [ ] Difficulty and escalation curve tuned across a full run
- [ ] All art at final quality
- [ ] All audio final
- [ ] Full UI pass
- [ ] Localization decision — which languages, if any *(store page alone is cheap and
      worth it; full text localization is not)*
- [ ] Accessibility pass — subtitles, remappable controls, colourblind-safe markers,
      motion/headbob toggle, adjustable text size
- [ ] **Feature freeze.** After this, bugs only

---

## Milestone 7 — Debugging and Hardening

**Exit test: can it run for two hours without a crash or a memory leak?**

### Stability
- [ ] Crash-free two-hour session on min-spec hardware
- [ ] Memory profile over a long session — the customer spawner and one-shot audio pool
      are the obvious leak candidates
- [ ] Frame-time profile — 3,403 items and 240 lights in one scene is the risk;
      check light culling, shadow distance, and physics contact counts
- [ ] Null-reference audit on scene-load and shift-reset paths
- [ ] Verify all statics reset properly on play-mode restart (several already use
      `RuntimeInitializeOnLoadMethod`; confirm the rest)

### Compatibility
- [ ] Windows and macOS builds both verified
- [ ] Test on low-spec hardware, not just the dev machine
- [ ] Multiple resolutions and aspect ratios, including ultrawide
- [ ] Alt-tab, minimise, and window-resize behaviour
- [ ] Controller support, or an explicit "keyboard and mouse only" statement

### Process
- [ ] Bug tracker with severity levels
- [ ] Automated build on push
      *`KehaiBuild.MacOS` is the headless entry a CI job would call*
- [ ] Crash reporting in the shipped build
- [ ] Triage pass — fix the crashes and blockers, ship the cosmetic ones

---

## Milestone 8 — Release

**Exit test: is it live and can people buy it?**

### Two weeks out
- [ ] Release date announced *(Steam requires the page live 2+ weeks before launch)*
- [ ] Release build uploaded to a beta branch and tested end to end
- [ ] Store page final — pricing, regional prices, launch discount
- [ ] Press and streamer keys out with embargo
- [ ] Launch-day post drafted in advance

### Launch day
- [ ] Set the build live
- [ ] Verify the store page, purchase flow, and download on a clean machine
- [ ] Be present for the first hours — forums, Discord, reviews
- [ ] Watch crash reports; a day-one hotfix is normal, not a failure

### After
- [ ] Hotfix pass in the first week
- [ ] Respond to reviews, especially negative ones with real bugs in them
- [ ] Post-mortem — wishlists at launch, conversion rate, what marketing actually worked
- [ ] Decide on post-launch content, or on stopping cleanly

---

## Project risks worth fixing before they cost you

Ordered by how much damage each can still do.

1. **The scene is not in the repo and is saved as binary.** On 2026-09-16 a branch
   switch auto-stashed 549 untracked files and reverted `SampleScene.unity` to a
   two-week-old commit. It was fully recoverable that time, by luck. Either switch the
   project to Force Text serialization and commit the scene, or set up a real backup.
   This is the single highest-value hour on this list. *(2026-10-02: the editor is set to
   Force Text, but `SampleScene.unity` on disk is still binary, last saved 2026-09-21; and
   `Assets/_Recovery/0.unity`, a crash-recovery copy from 2026-09-26, is newer and much
   bigger. Open it before deleting anything.)*
2. **No build has ever been made.** Every engine has a set of problems that only appear
   outside the editor. Finding them at Milestone 7 is expensive; finding them now is not.
   *(Addressed 2026-10-02: the first macOS build, with a bot shift played in it.)*
3. **The antagonist is the game's hook and it is not in the level.** Everything built so
   far is the chore loop. The chore loop is not the pitch. *(Addressed: Aiko is in.)*
4. **`AIKO.md` is much larger than the rest of the project.** It is a genuinely good
   design document, and implementing it fully is a bigger job than everything already
   built. Decide deliberately how much of it ships. *(Built in full; the open question is
   now tuning, which needs human play-testers.)*
5. **No menus and no save system** — both are invisible in a prototype and mandatory in
   a product. *(Addressed 2026-10-02: a main menu, the Esc menu, a career saved after every
   shift.)*
6. **The README is stale.** It documents `F` for the task list; it has been `C` since the
   control remap. Small, but it is the first thing anyone reads. *(Addressed: rewritten.)*

---

## Suggested ordering

The dependency that matters most: **the vertical slice gates the trailer, the trailer
gates the Steam page, and the Steam page is what accumulates wishlists.** Everything
marketing-shaped is downstream of one good ten-minute slice.

```
Prototype ──► Vertical Slice ──┬──► Steam Page ──► Demo ──► Next Fest
   [done]                      │
                               └──► Playtesting ──► Content Complete ──► Hardening ──► Release
```

Start the Steamworks paperwork now regardless — the tax and identity forms sit on a
clock you do not control, and they gate the release date rather than the page.
