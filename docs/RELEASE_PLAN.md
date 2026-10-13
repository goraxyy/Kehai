# Release plan

> **Kehai** · studio **TokenLimit** · Unity 6 (URP) · version `0.1.0` · macOS and Windows

Everything between today and a Steam release, in order. This is the file we follow.

- Each milestone has an **exit test**: one question that must be a clear "yes" before the next
  milestone counts as reached. Milestones can overlap in time; their exit tests can't.
- `[x]` done · `[~]` partly done (the note says what's left) · `[ ]` not started.
- No dates yet. They come once the Steam page is live.
- **Two passes.** Now: a quick pass, with placeholder art and level, good enough for the Steam
  page and the beta. Before launch: everything redesigned (Milestone 7). Versions follow the
  milestones (see [Versions](#versions)).

---

## Where we are

**Built:** the full shift loop; Karen with all six versions (rungs A–F); the main menu, the Esc
menu and a saved career; macOS and Windows builds; 80 products on 16,500 shelf slots; a fast
renderer (18.9 → 4.5 ms); 3D replays and shift reports; the playtest system; the marketing
automation; an endless maze in its own scene (parked).

**Biggest gaps:** nobody but bots has played it yet; no animation or feel pass; no Steam page;
the accounts and keys the playtest and marketing systems need.

---

## Setup: accounts and keys (do early, any order)

Nothing here needs code. Steps: [MARKETING.md → Setup](MARKETING.md#setup) and
[PLAYTEST.md → What you set up](PLAYTEST.md#what-you-set-up).

- [ ] **Steamworks:** partner account, the $100 app fee, tax and bank forms. These take weeks
      and gate the release date, so start now.
- [ ] **Handle** (e.g. *kehaigame*), claimed on YouTube, TikTok, Instagram, Reddit, X, Bluesky,
      itch.io and Discord
- [ ] **Website** with an email sign-up (an itch.io page works until then)
- [ ] **Discord server** with a *Playtester* role
- [ ] **Claude API** key, with a $15 monthly limit
- [ ] **Azure Speech** (free tier) for voices and transcripts
- [ ] **Telegram bot** for approvals
- [ ] **Buffer** with YouTube, Instagram, and X or Bluesky connected
- [ ] **Cloudflare:** R2 for Buffer's videos, and the playtest upload service
- [ ] **Google Drive** archive through rclone
- [ ] **Google Form** for playtest answers, and a password-protected **itch.io** page
- [ ] Turn off dry runs (`KEHAI_DRY_RUN=0`) once the keys are in

---

## Milestone 0: Prototype ✅

**Exit test: can one person play a whole shift without the editor's help?** Yes.

Moving, crouching and sprinting; hands and a 4-slot inventory; the jobs (restock, mop, bins,
till, walking customers to a shelf); customers who shop, spill and queue; burnout and coffee;
power cuts and the breaker box; the store radio; the flashlight; a 150 × 150 m maze of a store.

---

## Milestone 1: Vertical slice

**Exit test: do 10 minutes of the game convince a stranger the full game is worth playing?**

One shift with Karen in it, at quick-pass quality: placeholder art is fine, since everything is
redesigned before launch (Milestone 7). It's what the first trailer and Steam page are cut from.

### Karen
- [x] Karen in the store: perception, belief map, planner, 35 tactics, learning, fairness rules
- [x] Losing: a written warning, a lecture, overtime; the burnout and notice endings
- [ ] **Tune her with real people.** So far only bots have played against her
- [ ] Fix: she can stand on a stuck player without catching them (a touch only counts at the
      start of a chase)

### Feel
- [ ] Player animation: hands, held-item poses, footsteps
- [ ] Customer animation: walk, idle, browse
- [ ] Feedback: prompts, confirmations, a sound when a task is done
- [ ] Audio pass 2: footsteps, ambience, room tone, UI sounds
- [x] Volume by kind of sound, with one slider for all of them
- [ ] Lighting and mood pass
- [ ] Camera: head bob, FOV on sprint, smoothing

### The game around the shift
- [x] Main menu, Esc menu, saved career, shift summary and endings
- [x] Real products on full shelves, aisle signs, lamps
- [x] Options: sound, resolution, full screen, quality (Low / Medium / High), sensitivity,
      Karen's floor cone and the webcam, on one simple page in black, red and white

### Builds
- [x] macOS build (universal), with the webcam helper beside it
- [x] Windows build (64-bit)
- [ ] Run the Windows build on a real Windows PC
- [ ] Decide: a webcam helper for Windows, or keyboard blink only there
- [~] Check with a person at the screen: menus in a window, webcam blinking, frame rate
- [~] Version numbers per milestone ([Versions](#versions)); the identifier is
      `com.tokenlimit.kehai`, and each build records its commit

---

## Milestone 2: Steam page live

**Exit test: can someone wishlist the game?**

Do this alongside the slice: a page that's live earlier collects more wishlists.

- [ ] Reserve the app ID; pick the final title (check "Kehai" for trademark and search clashes)
- [ ] Capsule art: main, small, header, library (worth paying an artist for)
- [ ] 5+ screenshots that read as thumbnails
- [ ] Short description (the pitch from [MARKETING.md](MARKETING.md#pitch)) and a long one
      with GIFs
- [ ] Trailer, 60–90 s, hook in the first 5 seconds
- [ ] Tags, genre, system requirements (from real builds), age rating, Steam Deck yes/no
- [ ] Submit for review (3–5 business days), then **live as "Coming Soon"**
- [ ] Count wishlists every week from day one
- [ ] After the redesign (Milestone 7): new capsule, screenshots and trailer; a before/after
      post is a marketing beat of its own

---

## Milestone 3: Playtesting

**Exit test: do players understand what to do without being told?**

How a round runs: [PLAYTEST.md](PLAYTEST.md). We're watching for confusion, not crashes.

- [x] Playtest builds: tester code, consent, the whole session recorded, questions, upload
- [~] Upload service and reports: built; waits on the Cloudflare account (Setup)
- [ ] Play your own build every week, start to finish; list everything you had to explain
- [ ] Round 1: 5 first-time players, no help given
- [ ] Fix, then round 2 with new players
- [ ] Friends-and-family pass through Steam Playtest

**Watch for:** Does the task list explain itself? Is Karen scary or annoying? Is burnout read as
a resource? Can they find the breaker box in a blackout? Do they get lost in a bad way? Do they
notice she's learning?

---

## Milestone 4: Demo and Next Fest

**Exit test: does the demo end with the player annoyed that it ended?**

- [ ] Cut a demo from the slice: one or two shifts, then a wishlist screen
- [ ] A separate demo app on Steam; track completion and where people quit
- [ ] Pick a Steam Next Fest (February, June or October; a game gets **one**, before release),
      ideally after the redesign, since it's the biggest first impression
- [ ] Apply 4–6 weeks ahead; plan videos for the festival week
- [ ] Maybe a smaller festival first, as a rehearsal

---

## Milestone 5: Marketing (from now until launch)

**Exit test: is there a place to follow the game that isn't the Steam page?**

The plan: [MARKETING.md](MARKETING.md).

- [x] The pitch
- [x] Automation for fun shorts: picks moments, edits, voices, sends for approval, posts
- [ ] Rework the automated monthly long video to alternate **educational** and **update** videos
- [ ] Reddit posts with each update (by hand)
- [ ] Website, email list and Discord live (Setup)
- [ ] First 6 shorts banked before posting the first
- [ ] 2 shorts a week, 1 long video a month
- [ ] Press kit: logo, screenshots, GIFs, trailer, fact sheet, the blink privacy line
- [ ] Contact horror and sim YouTubers about 2 months before the demo and the launch
- [ ] Beats worth a video: the Steam page, the demo, Next Fest, the release date, the launch

---

## Milestone 6: Content complete (beta)

**Exit test: is every feature in, even if not polished?**

- [ ] All shifts present and playable; the difficulty curve tuned across a career
- [ ] Decide the endless maze: its own game mode, or how it fits the store
      ([IDEAS.md](IDEAS.md#2-the-endless-maze))
- [ ] Final art, audio and UI: Milestone 7
- [ ] Languages: the store page in English and Russian at least
- [ ] Accessibility: subtitles, remappable keys, colour-blind-safe markers, a head-bob toggle,
      text size
- [ ] Licences: every third-party model, sound and font checked for commercial use (the Teto
      customer model and the radio's music especially; see `Assets/!_Project/_Game/Audio/CREDITS.md`)
- [ ] **Feature freeze.** Bugs only after this

---

## Milestone 7: Redesign before launch

**Exit test: does every place and object look made for this game, not borrowed or placeholder?**

Everything from the quick pass is redone. Level first, then models, then the look on top.

### Level design
- [ ] The store's floor plan, redesigned: aisles, loops and dead ends Karen can use, sightlines,
      hiding spots, and landmarks so players don't get lost in a bad way
- [ ] Back of house: stockroom, staff room, office, toilets, loading bay, breaker box
- [ ] Outside: the parking lot, the street front, the bins and the skip, night lighting
- Plans and model lists for the lot, the outside, the lobby, the restrooms and the staff rooms:
  [LEVEL_DESIGN.md](LEVEL_DESIGN.md)
- [ ] Greybox first, playtest the layout (Karen's routes, the chase loops), then dress it
- [ ] A lighting and mood pass per area

### Models of the entire place
- [ ] A style guide first: palette, texel density, polygon budget, so it reads as one place
- [ ] The building: walls, floor, ceiling, roof, windows, storefront, doors, signage
- [ ] Fixtures: shelves and end caps, fridges and freezers, the tills and counter, baskets and
      trolleys, the coffee machine, the time clock, the breaker box, CCTV, speakers, lamps
- [ ] The 80 products, redone or refined to the style guide
- [ ] The little things: trash and litter, spills, bin bags, boxes and crates, the mop and bucket,
      wet-floor signs, posters, price tags, cables, vents, a fire extinguisher, a cleaning cart
- [ ] Outside: parked cars, parking lines, lamp posts, bollards, a trolley bay, kerbs, plants
- [ ] Characters: the player's hands, the customers, Karen

### The rest
- [ ] UI redesigned to match: menus, HUD, task list, maps
- [ ] Audio to match the new spaces
- [ ] Every third-party model, sound and font replaced or licensed (see Milestone 6)

---

## Milestone 8: Hardening

**Exit test: can it run for two hours without a crash or a memory leak?**

- [ ] Two crash-free hours on low-end hardware
- [ ] Memory over a long session (the customer spawner and the audio pool are the suspects)
- [ ] Frame time on low-end hardware (it's 4.5 ms on the dev Mac in the editor)
- [ ] Known hitches: the GPU drawer's first growth (130–210 ms, once); the endless maze's
      NavMesh rebuild (about 20 ms per border crossed)
- [ ] Statics reset on restart; no null references on load or shift reset
- [ ] Both platforms verified; several resolutions incl. ultrawide; alt-tab and resizing
- [ ] Controller support, or a plain "keyboard and mouse only"
- [ ] A bug tracker; automatic builds on push (`KehaiBuild.MacOS` is ready for it); crash reporting

---

## Milestone 9: Release

**Exit test: is it live, and can people buy it?**

- [ ] **Two weeks out:** release date announced (Steam wants the page live 2+ weeks before);
      the release build on a beta branch, tested end to end; price, regional prices and a launch
      discount set; keys to press and streamers with an embargo; the launch post written
- [ ] **Launch day:** build live; buy and download it on a clean machine; be around for the
      first hours; watch crash reports (a day-one fix is normal)
- [ ] **After:** fixes in the first week; answer reviews; a post-mortem (wishlists, conversion,
      what marketing worked); decide on more content, or stop cleanly

---

## Risks

1. **The scene lives only on the Mac**, as a binary file. A branch switch once nearly lost it.
   Back it up, or switch it to text and keep a copy somewhere safe.
2. **No human has played against Karen.** Her tuning is untested where it counts.
3. **8 GB of memory.** The editor in Play with the maze reached about 6 GB. Big tests need
   everything else closed.
4. **Licences** of third-party art and music (Milestone 6).
5. **The title.** "Kehai" still needs a trademark and store-search check.

---

## Order

The slice gates the trailer, the trailer gates the Steam page, and the page collects the
wishlists.

```
Prototype ──► Vertical slice ──┬──► Steam page ──────────────────────► Demo ──► Next Fest
  [done]      (quick pass)     │                                       ▲
                               └──► Playtesting ──► Content complete ──► Redesign ──► Hardening ──► Release
                                                    (beta)
Setup and marketing run alongside all of it.
```

---

## Versions

`MAJOR.MINOR.PATCH`. Below 1.0 the game is in development; `1.0.0` is the launch. A minor step
marks a milestone; a patch is fixes only. A label says how finished it is.

| version | what it is |
|---|---|
| `0.1.x` | prototype (now) |
| `0.2.x` | vertical slice, quick pass: the first Steam page |
| `0.3.x`–`0.5.x` | **alpha**: playtest rounds; features still coming |
| `0.6.x`–`0.9.x` | **beta**: content complete, then the redesign and hardening |
| `1.0.0-rc.1` | release candidate: the build that ships unless something's wrong |
| `1.0.0` | launch; then `1.0.1` for fixes, `1.1.0` for updates |

Steam doesn't read these: a build goes to a branch (default, beta, playtest), and the number
is for the game's own screen, the patch notes and bug reports.
