# Release plan

> **Kehai** · studio **TokenLimit** · Unity 6 (URP) · version `0.1.0` · macOS and Windows

Everything between today and a Steam release, in order. This is the file we follow.

- Each milestone has an **exit test**: one question that must be a clear "yes" before the next
  milestone counts as reached. Milestones can overlap in time; their exit tests can't.
- `[x]` done · `[~]` partly done (the note says what's left) · `[ ]` not started.
- No dates yet. They come once the Steam page is live.

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

One shift, at final quality, with Karen in it. It's what the trailer, the demo and the Steam
page are cut from.

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
- [~] Options: sensitivity, volumes, Karen's floor cone and the webcam are in;
      resolution and quality aren't

### Builds
- [x] macOS build (universal), with the webcam helper beside it
- [x] Windows build (64-bit)
- [ ] Run the Windows build on a real Windows PC
- [ ] Decide: a webcam helper for Windows, or keyboard blink only there
- [~] Check with a person at the screen: menus in a window, webcam blinking, frame rate
- [~] A real version scheme (the identifier is `com.tokenlimit.kehai`; the version is still
      0.1.0, and each build records its commit)

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
- [ ] Pick a Steam Next Fest (February, June or October; a game gets **one**, before release)
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
- [ ] Final art, audio and UI
- [ ] Languages: the store page in English and Russian at least
- [ ] Accessibility: subtitles, remappable keys, colour-blind-safe markers, a head-bob toggle,
      text size
- [ ] Licences: every third-party model, sound and font checked for commercial use (the Teto
      customer model and the radio's music especially; see `Assets/!_Project/_Game/Audio/CREDITS.md`)
- [ ] **Feature freeze.** Bugs only after this

---

## Milestone 7: Hardening

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

## Milestone 8: Release

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
Prototype ──► Vertical slice ──┬──► Steam page ──► Demo ──► Next Fest
  [done]                       │
                               └──► Playtesting ──► Content complete ──► Hardening ──► Release
Setup and marketing run alongside all of it.
```
