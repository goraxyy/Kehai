# KEHAI marketing pipeline: build plan

> Studio **TokenLimit** · Game **Kehai** (気配, "the sense that someone is there") · Antagonist **Karen** (カレン, "she")
> Plan started 2026-09-29. This file is the source of truth for the build: a later session
> reads it first, continues from **Status**, and ticks boxes as it goes.

## Status

| | |
|---|---|
| **Current phase** | Phase 8 (reference videos → patterns) built and tested without keys — branch `feat/references`, worktree `~/Developer/kehai-references` ✋. Phases 1–7 and the launchd schedule (#21) are merged; the schedule runs from `~/Developer/Kehai`, in dry runs |
| **Next action** | Owner: merge the Phase 8 PR; fill `tools/marketing/.env` (the checklist, step by step; the file is in `~/Developer/Kehai`). Then Claude: the Telegram chat id, the voice samples, a first real reference study, one trial short; then `KEHAI_DRY_RUN=0` in `~/TokenLimit/n8n/env` |
| **Blocked on owner** | The keys and accounts (Anthropic, Azure, Telegram bot, Buffer, a public host for Q8, rclone/Drive); answers to Q8–Q10, Q12, Q14 and Q16 |
| **Last updated** | 2026-10-04 |

**How to resume (for a later session).**
1. Read this file, then the memory notes for this project.
2. `git fetch origin && git worktree list`. Phase work happens in a worktree next to the project
   (`~/Developer/kehai-<phase>`) from `origin/main`, never in the main folder `~/Developer/Kehai`
   (it holds untracked art that `stash`/`checkout` can destroy).
3. Continue at the first unticked box of the current phase. Stop at every ✋ and wait.
4. A phase may be stacked on the previous one's branch while that PR waits for review (Phase 4 is);
   once the earlier PR merges, pull `main` and rebase or merge it into the later branch.

---

## Ground rules (from the owner's brief)

- **Commit code and docs only:** C#, TypeScript, Python, n8n workflow JSON without secrets, `.md`,
  JSON schemas. Never media, voices, music, keys. Secrets live in `tools/marketing/.env`
  (gitignored) and in n8n credentials.
- **One branch + PR per phase**, in a `git worktree` from `origin/main`. Scripts move with their
  `.meta`. The owner merges.
- **Names and branding** come from one place: `GameNames` (C#, player-facing text) and
  `tools/marketing/brand.json` (names, handles, links, colours, fonts, voices). The old names
  appear only in `KehaiMigration.cs`, its test and one `CHANGELOG.md` line.
- **Hardware:** M2, 8 GB RAM, ~17 GB free. Heavy jobs (Unity render, Remotion render, TTS) run
  **one at a time behind a lock**; Remotion concurrency 1–2. Local working budget 10 GB; archive
  to Google Drive and delete local copies by the retention rule below.
- **Nothing goes public without the owner's ✅ in Telegram.** Every step has a dry-run mode.
- Claude can't create accounts or enter passwords: the owner does the setup checklist.
- **Verification** (memory: headless Unity): compile offline with `csc` and the editor's `.rsp`;
  run tests and bot shifts in batch mode on an APFS clone (`cp -Rc`) of `~/Developer/Kehai` with the
  branch laid over it and productName set to a throwaway (`Kehai-verify`), then delete the clone,
  that data folder and its prefs. Unity renders (Phase 4) only run when the editor is closed.
- **Sleep:** a sleeping Mac freezes Unity mid-job, and EvalBatch's timeout (wall clock) then fires
  on wake — seen 2026-09-30. Run long jobs under `caffeinate -i`; closing the lid on battery still
  sleeps, so overnight jobs need the charger (or the lid open).

---

## Phase 0 — Prerequisites ✅

Done 2026-09-29. What later phases still need from it:

- **Buffer's API** (developers.buffer.com, GraphQL, API key from `publish.buffer.com/settings/api`)
  supports Instagram, Threads, LinkedIn, X, Facebook, Google Business, Mastodon, YouTube,
  Pinterest and Bluesky, and (2026-10-02) TikTok too; TikTok stays by hand (`posting.tiktok`). **Video must be a public
  URL** (`createPost` → `assets.video.url`), so approved files need a public host → Q8.
  Free plan: 1 API key, 100 requests/15 min, 250/day, 3,000/30 days, 3 channels. Per-channel
  video support and YouTube title metadata get tested in Phase 7.
- **n8n** 2.41.6 needs **Node 24+** (its docs still say 20.19–24.x; found 2026-10-02): Homebrew's
  `node@24` (keg-only, doesn't replace Node 25) runs it. n8n 2.x disables the Execute Command and
  Local File Trigger nodes by default; we re-enable Execute Command (`NODES_EXCLUDE=[]`) and poll
  folders on a schedule.
- **macOS privacy (TCC):** background jobs can't read `~/Desktop`; the project now lives at
  `~/Developer/Kehai` for that reason.
- **Belief grid size:** the store map uses 1.5 m cells over ~150×150 m (≤10,000 cells). At 2 Hz
  that's ~12 MB per 10 min raw at one byte per cell, so Phase 3 downsamples to 3 m and
  compresses (≈1 MB per 10 min).
- **Unity Hub** keeps its project list in `~/Library/Application Support/UnityHub/hub.db`
  (SQLite, table `projects`); `projects-v1.json` is a leftover it no longer reads.

| Tool | Found (2026-09-29) | Needed for |
|---|---|---|
| macOS 26.5.1, Apple M2, 8 GB | — | — |
| Disk | ~17 GB free of 228 GB | everything; keep ≥ 3 GB free at all times |
| Unity | 6000.5.10f1 (+6000.3.10f1), project at `~/Developer/Kehai` | Phases 2–4 |
| git 2.54, gh 2.97 (logged in as goraxyy) | ✓ | PRs |
| Node 25.9 / npm 11.12 (Homebrew) | ✓ for Remotion; ✗ for n8n | Phase 5 (Remotion), Phase 7 (n8n needs `node@22`) |
| Python 3.9.6 (Apple CLT), uv 0.12.7 | ✓ uv will provide Python 3.12 in a project venv | Phase 6 |
| dotnet 7.0.317, jq 1.7.1, Homebrew 6.0.22 | ✓ | tools |
| **ffmpeg / ffprobe** | ✗, but Remotion's own (7.1, in `editor/node_modules`) does the job | Phase 4 on |
| **rclone** | ✗ (`brew install rclone`, 1.75.1) | Drive, Phase 5 on |
| **n8n** | 2.41.6 in `~/TokenLimit/n8n` on `node@24` 24.21.0 (installed 2026-10-02) | Phase 7 |
| Telegram.app | ✓ installed | approvals |

## Phase 1 — Rename to Kehai / Karen ✅

Merged 2026-09-30 as PR #13 (`d823e35`); see `CHANGELOG.md` and the PR for what changed. Player-facing
text reads `GameNames`; save data and settings were migrated to `TokenLimit/Kehai` (the old folder
stays as a backup); productName `Kehai`, bundle id `com.tokenlimit.kehai`; the repo is
`goraxyy/Kehai`; the project moved to `~/Developer/Kehai`, the art and reference folder to
`~/Desktop/KehaiRelated/`, Unity Hub and Claude's memory followed. EditMode: 37/37 at the new path.

## Phase 2 — Clip markers (Unity) ✋

Branch `feat/clip-markers`, worktree `~/Developer/kehai-markers`. Markers **only observe**: the
only changes to game code are two events for listeners (`KarenBrain.ShelfSabotaged`,
`ShiftRecorder.Recorded`/`Finishing`) and the recorder writing one more file.

- [x] `ClipMarkers.cs`: the one table — id, weight, pre/post-roll, subjects, tags, always-kept — plus
      every threshold (merge gap 8 s, chase ×1.5, min score 5, 3 m near miss, 2 m / 50 % found blind,
      0.5 m in 6 s stuck, 60 s undone work, 3 s loud mistake, 10 s tell → trick, cooldowns).
      Weights as briefed; `manual_good` 10 and `manual_bug` 0, both always kept.
- [x] `ClipWatch.cs` (plain C#, testable) + `ClipMarkerRecorder.cs` (listens, feeds it):
  - blink_move ← `StoryKind.Blink` · learned ← `StoryKind.Learned`
  - catch ← the thought log's `CAUGHT` · escape ← a chase in the live frame ending with no catch
  - near_miss, found_blind, karen_stuck, possessed, lost guide ← the recorder's 10 Hz live frame
  - blackout ← mains off or a breaker tripped, until the lights are back (seconds in `value`)
  - undone_work ← `ShelfSabotaged` on a shelf restocked ≤ 60 s before, or the spill tactic's
    effect ≤ 15 m / ≤ 60 s from a spill you mopped
  - pa_call ← `PaSystem.SpeechStarted` · prop_trick ← a `PLAN` for crate_wall, fog, door_lock,
    camera_bolt_on, shelf_relocation or mimicry, lasting until its effect
  - clock_refused ← `PunchAttempted(false)` or the overtime tactic's refusal
  - loud_mistake ← your sprint or drop, `StoryKind.Heard` ≤ 3 s later, and her closing ≥ 1 m on it within 4 s
  - tell_then_trick ← a `StoryKind.Warning` then a (non-PA) effect ≤ 10 s later
  - shift_review ← clock-out · customer_chaos ← gave up at the till, or lost you while guided
  - manual_good **F7**, manual_bug **Left Shift + F7** (1 s tick on screen, works in builds);
    blink calibration keeps F9
- [x] `ClipMoments.cs`: merge markers ≤ 8 s apart (a lasting marker counts from its end); moment =
      union of pre/post-rolls clamped to the shift; score = Σ weights × 1.5 with a chase or catch;
      subjects, tags, `captionSeed` = narrator lines inside (≤ 12, repeats collapsed); drop < 5
      unless always kept; best first. Added after the first bot shift (one 5-minute "moment"):
      a moment is at most **45 s**, a lasting marker stretches it by at most **20 s**, and
      cooldowns for learned (20 s), PA (15 s) and loud mistakes (10 s).
- [x] `<stem>.markers.json` next to the shift's `.json`/`.html`, written with the record at clock-out;
      schema in `tools/marketing/schemas/markers.schema.json`. The report data carries
      `markers` and `moments` too.
- [x] Ticks on the F2 timeline (gold moments, weighted ticks, bug in red, the moment under the
      cursor named) and in the HTML report (a clickable strip and a "Clip moments" list).
- [x] F7 / Left Shift + F7 in `Controls.cs`, `CONTROLS.md`, README.
- [x] EditMode tests (`ClipMarkerTests`, 24): the table, merging, spans, scoring, chase boost,
      clamping, dropping/keeping, subjects/tags, caption seed, the file's JSON, and each rule.
- [x] Verified 2026-09-30 on the final branch: offline compile (0 errors, 0 warnings); EditMode
      61/61 on a clone; a batch-mode bot shift (6 min, rung F, clocked out) wrote `.markers.json`
      with 31 markers → 16 moments (longest 26 s; best: the catch, 20 s, score 54), valid against
      the schema; the HTML report's strip and list work (no console errors); F2 and the F7 tick
      checked in a windowed run (screenshots).
- [x] PR #14, merged 2026-09-30 (`bdf7587`). ✋

## Phase 3 — 3D replay recorder (Unity) ✋

Branch `feat/replay-recorder`, worktree `~/Developer/kehai-replay`. Code in
`Assets/!_Project/_Game/Replay/Scripts/` (namespace `Kehai.Replay`). Only listens; the game
code gained listen-only hooks: an `Item` registry, `AutoDoubleDoor` panel accessors,
`KarenBrain.Told`, `MazeMutation.LastMoves`, and `ShiftRecorder.Stem` (the shift's files are now
named when it starts, so the replay can stream to `<stem>.krec.part` and be renamed at the end).

- [x] `KrecFormat.cs` / `KrecWriter.cs`: one gzip stream of tagged records. Header: the shift,
      scene, Karen's seed and rung, the maze's moves, the belief grid (3 m bins of her 1.5 m cells),
      every shelf slot (position, filled, product) and ceiling light (position, on). Then 30 Hz
      ticks: spawns, poses as **millimetre deltas** (zigzag varints), rotations as three 16-bit
      numbers (smallest three), state and visibility — **only what changed**; the view at 60 Hz
      (position, rotation, FOV, eyelids, what's in hand); events; belief frames XOR'd with the last.
- [x] `ReplayRecorder.cs`: the player (motion, carrying, holding a tool), the view, Karen (mood,
      sees you, chasing), customers (their mark), understudies, hinge doors (locked), auto-door
      panels, every shelf unit (so relocations and the maze show), crate walls, fog, CCTV cameras
      (bolted on, dead), coffee cups, footprints, spills, bins (how full), bags (disposed), and every
      item off its shelf — tools included (the mop, the torch on/off) — loose, in hand, held (Karen
      with the mop), or back on a shelf. Events: noises, tells (kind, place, lead), PA chime and speech, mains and
      breakers, the thought log, the narrator, shelf slots filling and emptying, ceiling lights.
      Belief map at 2 Hz with her guess and how sure she is.
- [x] `KrecReader.cs`: reads a file back into timelines; `TryPose` holds a still entity until the
      tick before it moves (samples are written only on change), `TryCamera`, `BeliefAt`; a file
      cut short (the game quit) still loads.
- [x] `PlayerBodySlot.cs`: loads `Resources/ReplayBody/PlayerBody` (see below) and plays its clips
      through the Playables API; a 1.8 m capsule (lower when crouching) until then.
- [x] Tests: `KrecTests` (varints, rotations, a written file reads back as written, a still entity
      doesn't drift, a cut file loads, ten minutes stay under 20 MB, the capsule, clips by name) and
      `ReplayRoundTripTests` (10 s of a bot shift in the real store; every pose of the player, Karen
      and the customers comes back within 1 cm).
- [x] Verified 2026-09-30: offline compile (0 errors, 0 warnings); **EditMode 70/70** on a clone
      (the round trip: 389 poses of the player, Karen and a customer, worst **0.72 mm**, rotations
      exact, 151 views, 21 belief frames); a full 6-minute bot shift wrote a 380 KB `.krec`
      (≈0.6 MB per 10 min) that reads back complete in 0.6 s: 252 entities (player, Karen, 3
      customers, 14 items incl. the mop, torch, crate and what customers carried, 6 doors, 8 door
      panels, 189 shelf units, 20 footprints, 5 spills, 3 bins, a bag), 1,013 noises, 35 tells,
      12 PA events, 3,069 thoughts, 301 narrator lines, 16 slot and 81 light changes, 5,501 views,
      734 belief frames.
- [x] PR #15, opened 2026-09-30 (not merged yet). ✋

**Your player model (Q13), when it's ready:** export an FBX to
`Assets/!_Project/_Game/Replay/Resources/ReplayBody/PlayerBody.fbx` (local art, never
committed). In its import settings: Rig → **Humanoid**; Animation → one clip each, named so the
name contains **idle**, **walk**, **crouch** (walking crouched), **run** (or sprint) and **carry**
(walking with something in hand), each with **Loop Time** on; about 1.8 m tall, facing +Z, feet at
the origin. No Animator Controller needed. A missing clip falls back to walk or idle.

## Phase 4 — Replay player, cameras, shot render (Unity) ✋

Branch `feat/replay-player`, worktree `~/Developer/kehai-player`, **stacked on
`feat/replay-recorder`** (PR #15 wasn't merged when the owner said to continue). Code in
`Replay/Scripts/` and `Replay/Editor/`, the overlay shader in `Replay/Resources/`. Changes to game
code: `KarenBody.BuildLook`/`MoodColour` (her look without her body), `FogCloud.Build` (a cloud that
doesn't time itself out), `ShiftRestart.Reload`, the replay branch in `KarenBootstrap`, **R** on the
review screen, `ProceduralAudio`'s synth helpers made internal, and the recorder writes a crate
wall's width, the fog's radius and "the last coffee" as their state.

- [x] **The player** (`ReplayMode`, `ReplayPlayer`, `ReplayStage`, `ReplayPuppets`): opened with
      `-replay <file.krec>`, **R** on the review screen after a shift, or Kehai → Replay (Open Latest
      Shift / Open Shift… / Show Recorded Shifts). The store loads as usual; then every game script
      is switched off (except the one that paints the shelves), and so are the NavMesh agents, the
      player's controller, the HUD and physics (script mode). The player's camera becomes the
      replay's. Driven from the recording:
      - the store's own objects: shelf units (maze moves mapped back), doors, door panels, bins,
        and the spills, bags and loose items that were there when the shift began (loose ones
        it didn't start with are hidden);
      - puppets: Karen (her own look, eye colour by mood), customers and the understudy (the
        customer prefab, stripped), items off their shelves (copies of the same product), crate
        walls, fog (its particles on the replay's clock), CCTV (LED off when dead), coffee,
        footprints, spills, bags, and your body (`PlayerBodySlot`);
      - shelf slots and ceiling lights, matched by position.
      Poses are interpolated. Sounds play in 3D (the game's own procedural sounds; stand-ins for
      clips from the art folder). Backspace leaves, with the career where it was.
- [x] **Timeline:** play/pause, drag to scrub, 0.1–4×, frame step, ±5 s, previous/next clip moment,
      the moments (gold) and markers (ticks: a bug in red, F7 in blue) from `<stem>.markers.json`,
      **H** hides the HUD, **F1** lists the keys, the narrator and the PA as subtitles, and a note
      when the mains are off or a breaker has tripped. The keys are in `Controls.cs` and
      `CONTROLS.md`.
- [x] **Cameras** (`ReplayCameras`):
      - POV: recorded, with the eyelids;
      - CCTV corner: a high corner that can see the subject, cutting to another when it loses sight;
      - chase; orbit;
      - top-down: the roof cut away by the near plane;
      - free fly: right mouse, WASD, Q/E, the scroll wheel for speed, Z/X for field of view.

      The presets are worked out from the recording at t alone, so scrubbing and rendering see
      the same picture. **Tab** follows Karen or you; **F** turns on depth of field (URP Bokeh on the
      subject). **K** adds a keyframe to `<stem>.path.json`; **Shift+K** removes the last. **7** plays
      the path (`ShotPath`: Hermite positions, squad rotations, still at the ends; schema
      `shot-path.schema.json`).
- [x] **Karen's mind** (`MindLayers`), on its own layer:
      - belief heat map: her 3 m bins, red to yellow;
      - her guess: a ring, tighter the surer she is, plus a pin;
      - view cone: her sight range and field of view, cut by shelves, red while she sees you;
      - sound rings: as far as each noise carries; you blue, her red, others grey;
      - thought log: the last three thoughts, two lines each, over everything;
      - position dots, for the picture-in-picture.

      **M/B/G/V/N/T** toggle them. `-alpha` renders them alone on a transparent background.
- [x] **Render** (`ReplayRender`, `ReplayRenderBatch`, `tools/marketing/render_shot.sh`):

      `-krec -moment|-from/-to -shot -subject -layers -alpha -dof -size -fps -out -dry-run`

      - fixed timestep (1/fps, `Time.captureFramerate`), 8 warm-up frames;
      - frames go to ffmpeg (PNG through a pipe since Phase 5): H.264 `.mp4`, VP9 `.webm` (with alpha), or ProRes `.mov`
        (4444 with alpha). A folder `-out` gets PNGs instead;
      - the WAV is mixed from the event track as heard at the camera, then muxed in;
      - a `<out>.json` sidecar (schema `shot.schema.json`).

      The script:
      - refuses while an editor holds `Temp/UnityLockfile` (exit 3), or when a video is asked for
        without ffmpeg (exit 3);
      - takes the heavy-job lock `~/TokenLimit/marketing/state/heavy.lock` (busy: exit 75;
        `KEHAI_LOCK_WAIT` to wait);
      - runs Unity in batch mode with graphics under `caffeinate -i`, with a 60-minute timeout;
      - logs to `~/TokenLimit/marketing/logs/`; `--dry-run` checks everything and renders nothing.
- [x] Tests: `ReplayPlayerTests` (5: the path through its keys, smooth, saved and loaded; the shot
      settings; the sound heard where it happened; the WAV) and `ReplayEndToEndTests`. The end-to-end
      test records 12 s of a bot shift and opens it as a replay. It checks:
      - the game is switched off;
      - everyone is within 1 cm of the recording, scrubbing both ways;
      - every camera is somewhere sensible, and POV is exact;
      - a path goes through its keys;
      - with graphics, the store draws and her mind draws alone with alpha;
      - the shift is audible;
      - Backspace gives the game back.
- [x] Verified 2026-09-30:
      - offline compile: 0 errors, 0 warnings;
      - **EditMode 76/76** on a clone, with graphics. In the end-to-end test, 209 of the store's
        own objects were driven and 6 puppets built, and all 3,399 shelf slots and all 240 lights
        were matched;
      - a full bot shift (6 min, 33 markers → 16 moments), rendered through `render_shot.sh` on the
        clone: chase with her mind (1080×1920), POV, CCTV, top-down with her mind, orbit following
        you with depth of field, her mind alone (PNG with alpha: about half the pixels
        transparent), and a hand-made 3-key path. I looked at the frames;
      - each render took about 1–1.5 minutes, including Unity's start. PNG frames at 1080×1920
        render at about 5/s. The pipe ran at 82 frames/s at 640×360;
      - the ffmpeg pipe, checked with a stand-in that counted bytes: moment 1 at 60 fps, 1,201 ×
        640×360×4 = 1,106,841,600 bytes exactly, the mux arguments right and the temp file
        removed; the same for the transparent WebM;
      - the refusals: the editor open (a process holding the lockfile) exits 3; the lock busy
        exits 75; `.mp4` without ffmpeg exits 3. The dry run works.
- **Not yet verified:**
  - Real encoding: ffmpeg wasn't installed. Phase 5 found that Remotion's own ffmpeg is enough
    and switched the pipe to PNG frames; the sample shots are the first real encodes.
  - The POV eyelids: bot shifts never close them. The bot's blinks feed Karen's blink sense, not
    the eyelids on screen, so this needs a shift played with the webcam or **B**.
  - The interactive player's mouse and keys (scrubbing, free fly): tested only through the same
    code paths the render uses. This needs a look in the editor.
- [ ] PR. ✋

## Phase 5 — Editor (Remotion, `tools/marketing/editor`) ✋

Branch `feat/editor`, worktree `~/Developer/kehai-editor`, stacked on `feat/replay-player`. Remotion
4.0.530, React 19, TypeScript; Python 3.12 through uv (`tools/marketing/pyproject.toml`). Only code,
schemas and docs are committed (`tools/marketing/.gitignore` keeps media, `node_modules` and `.venv`
out). `editor/README.md` explains an edit.

- [x] **`brand.json`** (Q11): crimson `#DC143C`, soft black `#151518`, white; the game's own palette
      for her and you; soft rounded fonts: **Nunito** (English and Russian) and **M PLUS Rounded
      1c** (only the glyphs 気配 and カレン), both from Google Fonts at render time. Handles and links
      are `null` until the accounts exist. Voices hold the recommended Azure voices, marked
      `proposed` (Q10). A test checks the names against `GameNames.cs`.
- [x] **`edit.json`** + `schemas/edit.schema.json`, shared by the renderer (ajv) and Python
      (`km/schemas.py`, jsonschema); both accept the samples and reject the same mistakes.
      9:16 (1080×1920) and 16:9 (1920×1080), 24–60 fps, one video per language. Scene times are in
      seconds; transitions overlap scenes; voice and music run on the video's clock. Paths are
      relative to the working folder; absolute paths and `..` are refused.
- [x] **One composition** (`editor/src/EditVideo.tsx`):
      - shots: trim, constant speed or smooth ramps (constant pieces), zoom and pan keys, split
        screen (row or column), picture in picture (with alpha: Karen's mind from
        `render_shot.sh -alpha`), images, flat colours;
      - transitions: fade, slide, wipe, flip, clock;
      - sound: voice per language, music that ducks under the voice (0.25 s ramps, fades in and
        out), the game's own sound (70% by default, down to 35% under the voice), sound effects;
        every video is mastered to **-14 LUFS, peaks under -1.5 dBTP** (two-pass `loudnorm`,
        what the platforms play at);
      - word-timed captions: a few words at a time with the spoken one in crimson, or whole lines;
        Karen's lines in her colour;
      - text: hook, labels, lower thirds, and an end card (気配, KEHAI, the tagline, a call to
        action, the handles). `*Starred words*` come out in crimson;
      - pictures: images, GIFs, Lottie;
      - marks that draw themselves: arrows and circles;
      - memes as components: pov, top-bottom, nobody, caption-bar, expectation-reality;
      - vertical safe areas that keep text clear of the platforms' buttons.
- [x] **`editor/render.mjs`**:
      - validates the edit; checks every file exists, and that every shot is long enough for its
        trim and speed;
      - takes the heavy-job lock (`<root>/state/heavy.lock`, shared with `render_shot.sh`; busy:
        exit 75);
      - bundles with only the edit's files (hard links);
      - renders H.264/AAC, concurrency 1 (at most 2);
      - writes `drafts/<id>.<lang>.mp4` and a `.json` beside each;
      - options: `--dry-run`, `--scale`, `--frames`.
- [x] **Asset library** (Q7: `drive.file` scope; assets come in through `add_asset.py`):
      - where it lives: `<root>/assets/<kind>/` and `assets/manifest.json`
        (`schemas/asset-manifest.schema.json`), recording file, kind, sha256, licence, source,
        author, attribution, clearances, mood and tags;
      - what it refuses: a file without a licence or a source, music not cleared for YouTube,
        TikTok and Instagram, and the wrong file type for its kind; the same file twice gets one
        entry;
      - Drive: rclone uploads each asset and the manifest to `TokenLimit Marketing/assets/`.
        Until rclone is set up, entries stay `pending` and `--sync` uploads them later;
      - `--dry-run` and `--list`.

      `validate.py` checks any pipeline JSON against its schema (`--files` also checks an
      edit's files exist).
- [x] **ffmpeg:** Remotion's own build (in `editor/node_modules`) is enough, so
      `brew install ffmpeg` isn't needed. It can't read raw frames from a pipe, so `ReplayRender`
      now pipes PNG frames (encoded on worker threads). `render_shot.sh` finds that ffmpeg itself
      and runs it from its own folder, where it finds its libraries.
- [x] Tests: `npm test` (9: timing, transitions, speed ramps, zoom easing, ducking, captions, text
      fallback, the samples, and what the schema refuses); `uv run pytest` (20: the schemas, the
      samples, `brand.json` against `GameNames.cs`, and the library's refusals, copies, duplicates,
      dry run and Drive wait); `tsc` clean; C# compiles with 0 warnings.
- [x] **Verified with test clips** (frame numbers burned in, rendered EN + RU at 1080×1920, 14 s,
      about 50 s per language):
      - trims exact (source frames 63 and 87);
      - a 1× → 0.25× → 1× ramp exact (frame 25);
      - picture in picture with alpha exact (189 over 39);
      - a split, with the 9-frame wipe, exact (27 and 327);
      - hook, labels, lower third, arrow, circle, memes, Lottie, word captions, the end card with
        気配, and Russian text all rendered.

      Found and fixed along the way: captions ran together when the spoken word was scaled up;
      the Japanese font loaded 119 files (now one); on 16:9 the lower third covered the captions;
      a transparent picture in picture was hard to read over a busy shot (it now has a dark glass
      panel); the first sample mix clipped (game sound at full volume under everything: now
      staged, ducked and mastered).
- [x] **The samples**, rendered 2026-10-01 into `~/TokenLimit/marketing/drafts/`:
      - **Footage:** a bot shift recorded on the clone (6 min, 16 moments). `render_shot.sh` made
        15 shots (13 videos, 2 still sets; 202 MB) through Remotion's ffmpeg, 44–86 s each with
        Unity's start.
      - **`sample-short-catch.en/ru.mp4`:** 9:16, 21.4 s, about 29 MB, about 2 minutes each.
        Moment 1, the catch: hook and word captions, a CCTV cut with a label and an arrow, top-down
        with her mind as a glass picture in picture, a slow-motion ramp and a circle, your POV
        with a zoom punch on the catch and her line in her colour, an expectation/reality split,
        the end card.
      - **`sample-long-shift.en.mp4`:** 16:9, 1:58, 171 MB, about 8 minutes. A Lottie title card,
        lower thirds, a CCTV ramp with a zoom and an arrow, the catch from above with her belief
        map, the "Nobody:" meme, a split of two near misses, a still with a slow zoom, the
        caption-bar meme and a GIF, a top-bottom meme, the end card.
      - **Sound:** before mastering, peaks are -3.1 dBTP (short) and -1.4 dBTP (long). Finished:
        -14.2 to -14.5 LUFS, true peaks -0.9 to -1.3 dBTP after AAC.
      - **Stand-ins:** the voice is macOS `say` (Samantha, Shelley; Milena for Russian) with
        estimated word timings, until Phase 6's Azure voices. The music, sound effects, Lottie,
        GIF and still are our own, entered through `add_asset.py`. They wait for Drive (rclone).
      - **Re-check:** EditMode 76/76 on the clone afterwards.
- [x] PR #17, opened 2026-10-02 (not merged yet). ✋
- Licence note: Remotion is free for individuals and companies of up to 3 people.

## Phase 6 — Voice and writing (Python, `tools/marketing`) ✋

Branch `feat/voice-writing`, worktree `~/Developer/kehai-voice`, stacked on `feat/editor` (PR #17).
Built without keys (the owner had none yet): every step runs for real against replayed answers
and the macOS stand-in voice, and switches to Claude and Azure when `.env` has the keys.
`tools/marketing/README.md` explains each script.

- [x] **The Claude steps**, one script each, all on **Claude Opus 5.5** with the effort set per
      step (`km/llm/steps.py`: low for translate, package and the report; medium for picking and
      revising; high for writing):
      - `pick_moments.py`: the week's `.markers.json` → 3 shorts, 2 to 4 shots each, and a decision
        for **every kept moment** (short, long, bug, skip; checked, and sent back once if one is
        missing); moments used in earlier weeks are marked; writes `plans/<week>/picks.json`
        with the `render_shot.sh` runs, which `render_picks.py` renders one at a time;
      - `write_short.py`: a draft against the rendered shots (their events and where Karen and
        you are in the picture) and the asset library → `edits/<id>/` (edit, draft, context);
      - `translate.py`: every text to Russian, spoken lines checked to fit their time, names as
        brand.json says (Latin on screen, «Кэхай» and «Карен» in speech); remembered, so a
        revision only translates what changed;
      - `revise.py --note` / `--undo`: the owner's note applied to the whole draft; every earlier
        version kept in `versions/`;
      - `package.py`: YouTube, Instagram, TikTok, X, Bluesky, a pinned comment, per language;
        lengths, hashtags, no invented handles or links, and never her name's meaning, checked;
      - `long_video.py outline | script | voice | edit`: the monthly video in stages for ✋ gates,
        from the month's moments, `CHANGELOG.md`, the commits and the owner's notes; the edit
        is timed to the recorded voice-over;
      - `weekly_report.py`: the week's numbers counted in code, Claude's short read on top.
- [x] **How they call Claude** (`km/llm/client.py`): structured outputs (`output_config.format`)
      against `schemas/llm/*.schema.json`, kept inside the API's limits (every field required, no
      ranges, no unions; a test checks), then the rules the schema can't hold (`km/drafts.py`:
      shot lengths against trim and speed, overlays inside scenes, the hook at 0 not repeating the
      voice, lines sayable in their time, ids that exist) with **one repair round**; the style
      guide and the game reference (`prompts/style.md`, `reference.md`) cached at the front of
      every request; `fallbacks: "default"` for refusals, and a refusal stops the step and tells
      the owner; streaming; no prefill; Opus's thinking left adaptive.
- [x] **Cost:** every call to `logs/llm_costs.csv` (tokens, cache reads and writes, dollars,
      priced per model, a fallback priced by the model that answered). Before a call: this
      month's spend + a high estimate against **$15** (`KEHAI_LLM_MONTHLY_USD`); past it nothing
      is sent and the owner is told (`state/alerts.jsonl`, to Telegram in Phase 7); a warning at
      80%. **Dry run** on every step (the request saved with its estimate); **replay**
      (`KEHAI_LLM_REPLAY`) answers from files.
- [x] **The voice** (`km/tts/`, `voice.py`): Azure neural voices (SSML with rate, pitch and style;
      word timings from `WordBoundary`; 429 retried; F0's 0.5M characters a month tracked and
      capped), ElevenLabs (word timings from its character alignment), and macOS `say` as the
      stand-in (word timings estimated from the silences). Karen has her own voice. Voices per
      language and role in `brand.json`. Lines cached by what's said and who says it; lines
      that would overlap move later; behind the heavy-job lock. The edit gained `script` (the
      voice-over as written; `voice` is what the renderer plays).
- [x] **Where Karen is in a shot** (`ShotTrack.cs`): every render now records where she and you
      are in the picture, ten times a second, in the shot's `.json` (`track`). The writer sees
      it once a second; an arrow or circle with `target: karen` is placed from it exactly
      (trim, speed ramps and zoom included) and **follows her** through its time on screen (the
      editor's new `follow` keys).
- [x] **Silence:** every Unity batch run (tests, bot shifts, renders) plays nothing out loud:
      `SoundSettings.Audible` keeps the listener at 0 in batch mode (the owner heard the earlier
      runs). A shot's sound is mixed offline, so it loses nothing.
- [x] **Fixes found on the way:** the end card's call to action ran off the screen in Russian
      (now wrapped inside the safe area, and capped at 32 characters in English); monthly totals
      counted rows from other months.
- [x] **Verified 2026-10-02:**
      - `uv run pytest`: **103** (the client, prices, the cap and its alerts, replays, the repair
        round, refusals, cut-off answers, the schemas' limits, prompts free of hard-coded names;
        drafts and every check; translations carried over; targets and following marks; SSML,
        alignments, word estimates, fitting, quotas, the lock; every step script end to end with
        replayed answers, the long video's four stages included). `npm test` 11, `tsc` clean;
      - C# compiles offline with 0 warnings; **EditMode 78/78** on a clone (2 new ShotTrack tests;
        the end-to-end test checks her place in the picture and the silence);
      - for real, with replayed answers: a 5-minute bot shift → `pick_moments` → `render_picks`
        rendered 2 tracked shots through Unity (16.4 s each, about 22 s per render) →
        `write_short` → `translate` → `voice` (say) → `render.mjs` EN + RU. The track matched
        the frames; circles and an arrow followed her; the shots kept their sound (peak level 0.55);
      - the API path with a deliberately wrong key: the SDK takes the request; the 401 becomes
        "check the key" (exit 3) with nothing logged.
- **Not verified yet:** a real Claude answer (no key), Azure and ElevenLabs voices (no keys), so
  real costs and prompt quality are untested. The first sample render once stopped at 30% with
  no error (49 s in); it didn't happen again in seven renders since.
- [x] PR #18, opened 2026-10-02 (not merged yet). ✋

**Budget estimate**, Opus 5.5 at the efforts above ($4 / $20 per M tokens, cache reads $0.20,
cache writes 1.25×), from the dry runs' high guesses: a pick about $0.15, a short about $0.27, a
translation or a package about $0.08, a revision about $0.20, the report about $0.06; a week with
three revisions ≈ **$2**, and a long video ≈ $2–3 a month: **≈ $10–11 a month**, inside $15 but
with less room than Sonnet would leave. The ledger will show the real figure within two weeks;
the levers are lower effort, or `KEHAI_LLM_MODEL=claude-sonnet-5-5` (the owner's call).

## Phase 7 — n8n (self-hosted, npm, localhost) ✋

Branch `feat/n8n`, worktree `~/Developer/kehai-n8n`, stacked on `feat/voice-writing` (PR #18).
Built without keys (owner: "go to phase 7 without them"): with no Telegram bot the messages go to
an outbox file and the owner's taps are played with `run_job.py fake`; with no Buffer, no public
host and no Drive, posting comes to Telegram to do by hand and nothing is archived (the owner is
told once). `tools/marketing/README.md` and `n8n/README.md` explain it.

- [x] **n8n 2.41.6** in `~/TokenLimit/n8n` on Homebrew's `node@24` (it needs Node 24+, not the
      22 the docs suggested), listening on 127.0.0.1 only, `NODES_EXCLUDE=[]` for the Execute
      Command node. `n8n/setup.sh` installs it (pinned), writes its settings, imports and
      publishes the workflows (`--launch-agent` to start at login: not installed yet); `n8n/run.sh`
      starts it. **7 workflows** in `n8n/workflows/` (no secrets), each a schedule and a "Run now"
      trigger calling `n8n/job.sh <job>` → `run_job.py`: Telegram every minute, work every 5 min,
      produce at 1/3/5 a.m., publish at 9, housekeeping at 4:30, the report Sundays at 8 p.m., the
      long video daily at 10 (it starts on `long.day`). `KEHAI_DRY_RUN=1` in n8n's settings
      until the keys are in.
- [x] **`run_job.py`** (one entry point; one JSON line for n8n; caffeinate and a `pipeline` lock for
      the long jobs), **`pipeline.json`** (shorts per week, pick day, posting days and channels,
      YouTube/Instagram post settings, retention, storage) and **`state/pipeline.db`** (SQLite:
      every video's status, the job queue, posts, Telegram's bookkeeping).
- [x] 1. **New shift → drafts:** produce announces new shifts; on the pick day (Saturday's 1 a.m.
      run) picks the week's shorts; takes each along shots (Unity, only while the editor is
      closed) → write → translate → voice → render → package → preview. Each step looks at
      the files, so a stopped run carries on; failures retry (3×) then wait for the owner;
      a missing key or the cap blocks and the owner hears once a day; production waits when the
      working folder passes 10 GB or the disk 3 GB free.
- [x] 2. **Approval in Telegram** (polled, only the owner's chat): the English video with ✅ ❌ ✏️
      🇷🇺, and ↩️ once there's an earlier version; buttons carry their version, so old ones do
      nothing. ✏️ asks what to change; the reply is revised, re-voiced, re-rendered and comes back
      as the next version (unchanged lines keep their audio). ↩️ brings back exactly the version
      shown before. Files over 50 MB go as a smaller preview. /status /costs /report /retry /help.
      Decisions go to `state/approvals.jsonl`; alerts (the cap, quotas, failures) to Telegram.
- [x] 3. **Publish** on posting days (Mon/Wed/Fri): the oldest approved short to each Buffer
      channel's queue (YouTube with its title and category, Instagram as a reel, X), from a public
      copy (Q8: Buffer refuses Drive links and fetches when it posts); housekeeping asks Buffer
      how each post did. TikTok, and anything Buffer can't take, comes to Telegram with the text
      ready and a "Posted ✅" button per platform. The Buffer calls follow its GraphQL schema
      (checked 2026-10-02) and are tested against a mock; never against Buffer itself (no key).
- [x] 4. **The long video, monthly:** ✋ gates in Telegram for the outline, the script and the rough
      cut (✏️ does the stage again with the note); then the package, the Russian audio as its own
      track, English and Russian subtitles (from the word timings), all to Drive for YouTube Studio
      with a checklist, and "Uploaded ✅".
- [x] 5. **Housekeeping:** Buffer statuses; archive to Drive (videos, then the recordings whose
      moments were used); public copies down after posting; retention as proposed in Q12, only
      **reported** until `retention.apply` is turned on; the storage budget. Weekly report to
      Telegram.
- [x] **Found and fixed on the way:** n8n's CLI needs a manual trigger to run a workflow; a published
      workflow can't be re-imported until it's unpublished; httpx wasn't installed (the Anthropic
      SDK 1.x no longer brings it: ElevenLabs needed it too); `KEHAI_TTS_BACKEND` in `.env` was
      ignored; undo would have restored a translation snapshot instead of the version shown; a
      restored version kept its old file time, so it wouldn't have been rendered again; one
      malformed Telegram update stopped the whole poll and lost the rest.
- [x] **Verified 2026-10-02:** `uv run pytest` **147** (Phase 7: the store, every button and reply,
      stale versions, strangers ignored, a bad update, the chain from what's on disk, exit codes,
      retries and blocks, posting by hand and through a mocked Buffer, statuses, retention reported
      and applied, nothing outside the working folder touched, subtitles, the long video's gates,
      the workflow files). **End to end, through n8n** (`n8n execute`), with replayed answers and
      the `say` voice: a 5-minute bot shift on a clone → produce picked the blink moment, rendered
      3 tracked shots with Unity (145 s), wrote, translated, voiced, rendered EN + RU and packaged
      → **a draft short in Telegram** (the outbox) → ✏️ and a note → work revised it → v2 → ✅ →
      publish (Friday) sent it to post by hand → "Posted ✅" on four platforms → 🎉; housekeeping
      and the weekly report ran. Sample: `drafts/sample-blink.{en,ru}.mp4`.
- [x] **The schedule from launchd** (the owner chose it over n8n staying up, 2026-10-02: the Mac
      is an 8 GB MacBook that sleeps after a minute on battery, and n8n takes ~400 MB and misses
      whatever it sleeps through). `n8n/launchd.py` turns each workflow's Schedule node into a
      LaunchAgent (`com.tokenlimit.kehai.job.<job>` → `job.sh <job>`), so the times still live in
      one place; a calendar time slept through runs once on wake; the quick jobs run at background
      priority; `job.sh` logs to `logs/scheduled/<date>.log` only when a run says something, and
      reads `KEHAI_DRY_RUN` on every run. `setup.sh --launchd` installs them and leaves n8n's
      workflows unpublished (n8n for looking and "Run now"); the choice is `KEHAI_SCHEDULER`, kept
      on later runs. The pick also happens on the first run after `pick_day` that week, for a
      Saturday slept through. Cloudflare R2 needs no rclone setup: rclone reads the
      `RCLONE_CONFIG_R2_*` lines in `.env`. Tests: **151**. Installed 2026-10-02 from the
      worktree; the first launchd Telegram run exited 0.
- **Not verified yet:** a real Telegram bot, Buffer, R2 and Drive (no accounts yet), a night of
  launchd runs across sleep and wake, and a real Claude or Azure answer.
- [x] PR #19, opened 2026-10-02 (not merged yet). ✋

## Phase 8 — Reference videos → patterns (Python, `tools/marketing`) ✋

Branch `feat/references`, worktree `~/Developer/kehai-references`, from `main`. The owner asked
(2026-10-04): "i will going over some videos in socials medias and send reference videos, i need
the workflow to read the video and make something inspired by them". Their answers: send them as
**videos in Telegram**; each becomes **a saved pattern and a new short**; follow the **idea, the
pacing and the format** (rebuilt as closely as the editor allows); a reference **takes one of the
week's slots** (3 shorts a week stays).

- [x] **Reading a video** (`km/watch.py`), with Remotion's ffmpeg only (it has no scene or loudness
      filters, so both are worked out in Python): frames twice a second through the hook, then about
      once a second, plus each shot's first moment (at most 48, 384 px wide); cuts from tiny grey
      frames ten a second; the sound's level every half second with its jumps and drops; 16 kHz WAV.
      A 16 s video reads in about 4 s.
- [x] **What it says** (`km/stt.py`): Azure speech-to-text with the voices' key (F0: 5 audio hours a
      month, `KEHAI_AZURE_STT_MONTHLY_SECONDS`), English or Russian detected, phrases with times.
      Optional: without the key, over the quota or on an error, the study goes on without it.
- [x] **Studying** (`study_reference.py`, step `study_reference`, Opus 5.5 at medium effort,
      `schemas/llm/reference_study.schema.json`, `prompts/study_reference.md`): the frames go to
      Claude as pictures (the client gained images; dry runs log their paths), with the cuts, the
      loudness and the transcript. The answer: hook, beats second by second, pacing, format, the
      **recipe** (each part of the format mapped to an editor feature, with timing and position),
      the replay shots it needs, what the editor can't do yet, the kind of Kehai moment it needs,
      and what must not be copied. Checked against the video (beats from 0 to its end, in order;
      its length). Saved to `references/<id>/` and the library, `patterns/<name>.json`. About
      **$0.19** for a 21 s video (28 frames, ~17.5k input tokens). Works on any local file too:
      `uv run study_reference.py <video> --note "…"`.
- [x] **Telegram** (`km/references.py`): a video (≤ 20 MB, the Bot API's limit; its caption is the
      note) is saved and queued; the work job studies it and sends the pattern back with ✏️ (study
      it again with a correction; the name stays) and ❌ (out of the library). `/patterns` lists the
      library; `/status` counts references. Too big a file gets told how to send it.
- [x] **The week's shorts:** on the pick day the oldest ready reference (`references.per_week`, 1)
      goes to `pick_moments.py --pattern`: one short must follow it, filmed with the cameras it
      needs, or the answer says no moment fits and what to play (the owner is told; it waits a
      week). Any short may follow a library pattern when it suits. `write_short.py` gives the
      writer the whole recipe and the owner's note ("Pattern: …" shows in the preview);
      `revise.py` keeps a video's pattern, and a ✏️ note that names another brings it in.
- [x] **Housekeeping:** a reference's video, frames and audio go `retention.references_days` (30)
      after it arrived; its study and pattern stay; never archived to Drive (someone else's video).
- [x] **Verified 2026-10-04:** `uv run pytest` **181** (a real 4-second test video made with
      ffmpeg: the cut found at 2 s, the frames, the sound; pictures before text in the request; the
      study saved and studied again under its name; the whole Telegram path in outbox mode with the
      study as a subprocess; too big a video; ✏️ and ❌; the week's picks taking a reference or
      saying what to play; the picks' new checks; a short written in a pattern and revised into
      another; references' media retention). By hand: `run_job.py fake video` → `telegram` →
      `work` → the pattern in the outbox with its buttons; a dry run on the 21 s sample short.
- **Not verified yet:** a real Claude study (no key) and an Azure transcript (no key).
- [ ] PR. ✋

## Architecture

```
Unity (C#)                           tools/marketing (Python 3.12 via uv)             Remotion (TS)
 ShiftRecorder ─► <stem>.json/.html   run_job.py ◄── launchd (or n8n) schedules            editor/
 ClipMarkers   ─► <stem>.markers.json  produce · telegram · work · publish ·              edit.json ─► mp4
 ReplayRecorder─► <stem>.krec          housekeeping · report · long                        brand.json
 ReplayRender  ◄─ render_shot.sh ◄──── render_picks.py
               ─► shot.mp4 + .json     pick_moments write_short translate revise
                  (with its track)     package long_video weekly_report   (km/llm: Claude)
                                       voice.py (km/tts: Azure, ElevenLabs, say)
                                       study_reference (km/watch, km/stt) → patterns/
                                       km/telegram · km/buffer · km/media (Drive, public host)
                                       km/housekeeping · km/store (state/pipeline.db)
```

Shift records (and the `.markers.json` files) are written to
`~/Library/Application Support/TokenLimit/Kehai/shift_records/`.

**Local working folder:** `~/TokenLimit/marketing/` (outside the repo and outside `~/Desktop`):
`inbox/ shots/ audio/ drafts/ approved/ archive-staging/ logs/ state/`. Budget 10 GB and a
floor of 3 GB free disk; a job that would break either waits for housekeeping.

**Retention (proposal, Q12):** rejected drafts 7 days · approved drafts: deleted locally once
archived to Drive and posted + 3 days · shot renders: after their draft is approved or rejected
+ 3 days · `.krec`: archived to Drive after its moments are used, local copy kept 30 days ·
TTS audio: kept with its draft · logs: 90 days.

---

## Owner setup checklist

Secrets go **only** in `tools/marketing/.env`: it's already made, in `~/Developer/Kehai` (from
`.env.example`, readable only by you, ignored by git), and the worktree links to it. Open it with
`open -e ~/Developer/Kehai/tools/marketing/.env`, paste each value after its `=` (no quotes, no
spaces), save. Never paste a key into a chat. An empty value counts as not set.

- [ ] **1. Claude API** → `ANTHROPIC_API_KEY`
      1. https://platform.claude.com: sign in, or create an account.
      2. Settings → Billing (https://platform.claude.com/settings/billing): add a card and buy
         credits (about $10–15 covers a month; leave auto-reload off).
      3. Settings → Workspaces (https://platform.claude.com/settings/workspaces) → create
         `tokenlimit-marketing` → open it → **Limits** → Change Limit → **$15** a month.
      4. Settings → API keys (https://platform.claude.com/settings/keys) → Create key: name
         `kehai-pipeline`, workspace `tokenlimit-marketing`, linked to you, the longest expiration
         offered. Copy it (`sk-ant-…`, shown once).
- [ ] **2. Azure Speech, free tier** → `AZURE_SPEECH_KEY`, `AZURE_SPEECH_REGION`
      1. An Azure account: https://azure.microsoft.com/pricing/purchase-options/azure-account
         ("Try Azure for free"; it asks for a phone and a card to check who you are).
      2. https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices: your
         subscription; resource group: Create new → `kehai`; region **East US**; a name such as
         `kehai-speech-1` (it must be unique); pricing tier **Free F0**; Review + create → Create.
      3. Go to resource → **Keys and Endpoint**: KEY 1 → `AZURE_SPEECH_KEY`; Location/Region
         (`eastus`) → `AZURE_SPEECH_REGION`.
      4. When Azure asks (after 30 days) to move to pay-as-you-go to keep the subscription, accept:
         F0 stays free (0.5M characters a month; it stops at the quota rather than charging).
- [ ] **3. Telegram bot** → `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`
      1. https://t.me/BotFather → `/newbot` → a name (e.g. *Kehai Studio*) → a username ending in
         `bot`. Copy the token it gives.
      2. Open your bot (BotFather's link) → **Start**.
      3. Claude runs `uv run run_job.py telegram-setup` (or you, in `tools/marketing`): it prints
         `TELEGRAM_CHAT_ID=…` for your chat. Don't set a webhook.
- [ ] **4. Buffer** → `BUFFER_API_KEY`
      1. https://buffer.com: the Free plan.
      2. Connect **YouTube**, **Instagram** (a Business or Creator account: Instagram app →
         Settings → Account type and tools) and **X or Bluesky** (Free plan = 3 channels, Q9).
      3. Each channel's posting schedule: Mon/Wed/Fri at your times, EDT (Q14).
      4. https://publish.buffer.com/settings/api → create a key.
- [ ] **5. The public host for Buffer** (Q8, Cloudflare R2) → `KEHAI_PUBLIC_URL`, `RCLONE_CONFIG_R2_*`
      1. https://dash.cloudflare.com/sign-up, then **R2 Object Storage** (it may ask for a payment
         method; free up to 10 GB stored, downloads free).
      2. Create bucket **`kehai-public`** (location automatic).
      3. The bucket → Settings → **Public Development URL** → Enable → type `allow` → copy the
         `https://pub-….r2.dev` address → `KEHAI_PUBLIC_URL`.
      4. R2 Object Storage → API Tokens → **Manage** → Create Account API token: permission
         **Object Read & Write**, only the `kehai-public` bucket → Create. Access Key ID →
         `RCLONE_CONFIG_R2_ACCESS_KEY_ID`; Secret Access Key → `RCLONE_CONFIG_R2_SECRET_ACCESS_KEY`
         (shown once); the S3 endpoint `https://<account id>.r2.cloudflarestorage.com` →
         `RCLONE_CONFIG_R2_ENDPOINT`. Nothing to set up in rclone: it reads these lines.
- [ ] **6. Google Drive archive** (Q7; a sign-in, no key)
      1. `brew install rclone` (5 needs it too).
      2. `rclone config create gdrive drive scope=drive.file` → the browser opens → sign in with the
         Google account for the archive → Allow.
      3. Don't make the `TokenLimit Marketing` folder yourself: with `drive.file` rclone only sees
         what it made, so it makes the folder on the first archive.
- [x] **ffmpeg:** not needed. `npm install` in `tools/marketing/editor` brings Remotion's own, and
      `render_shot.sh` uses it (Phase 5). `brew install ffmpeg` still works if you want it anyway.
- [x] **Node for n8n:** `node@24` installed 2026-10-02 (keg-only; Node 25 stays the default), and
      n8n with it (`tools/marketing/n8n/setup.sh`).
- [x] **n8n's owner account** (local only), made 2026-10-02.
- [ ] *(Later, for weekly_report)* a Google Cloud project with **YouTube Data API v3** enabled and
      an API key (read-only public stats) → `YOUTUBE_API_KEY`, plus your channel id.
- [ ] *(Optional)* ElevenLabs API key → `ELEVENLABS_API_KEY`, if you want that backend.

Then Claude checks each key with a dry run, makes the voice samples (Q10) and one trial short,
and sets `KEHAI_DRY_RUN=0` in `~/TokenLimit/n8n/env`.

---

## Questions

**Answered 2026-09-29:** Q1 clip markers on **F7** (blink calibration stays F9) · Q2 the burnout
ending keeps 過労死 with **BURNED OUT** under it · Q3 settings migrated on macOS · Q4 the pitch
rewrites, and Karen's name meaning is **never explained** · Q5 folders renamed and the project moved
out of `~/Desktop` · Q6 repo renamed `goraxyy/Kehai` · Q15 the migration and its test are the only
code that names the old game. **2026-09-30:** Q13 the player body will have idle, walk,
crouch-walk, run and carry animations; a capsule until then (path as proposed, Humanoid rig).

**2026-09-30 (later):** Q7 the recommended `drive.file` scope, with assets entering through
`add_asset.py` · Q11 crimson red, soft black and white, soft fonts (Nunito and M PLUS Rounded 1c;
handles and links still to come).

**Open** (each has a recommendation; answer "ok" to take it):

7. ~~**Drive scope for rclone.**~~ *Answered: as recommended.* *Recommend:* `drive.file` (rclone only sees files it created) and
   assets enter the library through `add_asset.py` from a local inbox (it also records the licence).
   Alternative: full `drive` scope limited to one folder, so you can drop assets in via the web.
8. **Public URL for Buffer.** Buffer fetches videos from a public URL **when the post goes out**
   (hours or days after queuing), and its docs say Drive or Dropbox share links don't work
   (checked 2026-10-02, so the earlier "Drive link, revoked after queuing" idea is out).
   *Recommend:* a **Cloudflare R2** bucket with public access (free tier: 10 GB stored, free
   egress; needs a Cloudflare account): rclone uploads each approved short, and housekeeping
   deletes it a few days after it's posted. Alternative: Cloudinary's free tier. Until then,
   posting days send everything to Telegram to post by hand.
9. **X or Bluesky** for the third Buffer channel (Free plan = 3)? Or a paid Buffer plan for both.
10. **Voices.** English narrator, Russian narrator, and Karen. *Recommend:* 3–4 Azure samples of
    each for you to pick (e.g. EN: Andrew / Ava; RU: Dmitry / Svetlana; Karen: a calm,
    formal female voice, possibly a Japanese voice speaking English). Should Karen ever speak
    Japanese with subtitles? *Ready:* once the Azure key is in `.env`, `uv run voice_samples.py`
    makes 13 samples (`audio/samples/`).
11. ~~**brand.json**~~ *Answered: crimson, soft black, white, soft fonts; handles and links when the accounts exist.* It needs: handle(s), website, Discord/Steam links (if any yet), colours, fonts.
    *Recommend as defaults:* colours from the shift report (`#0f1116` bg, `#ff5454` Karen,
    `#4dd2ff` you, `#ffd640` her guess); fonts **Inter** (Latin + Cyrillic) and **Noto Sans JP**
    for 気配/カレン. Handle: is `@kehaigame` free where you want it? (You check; Claude can't sign up.)
12. **Retention and budget** as proposed above (10 GB working, 3 GB free floor)? The tools
    themselves (Remotion + its Chrome, ffmpeg, Python venv, rclone) take roughly 1 GB, and n8n
    with node@24 another 2.6 GB; the disk had ~43 GB free on 2026-10-02.
14. **Posting times** for Mon/Wed/Fri: set in Buffer's queue (EDT). Any preference?
16. **The Russian versions.** Every short is made in English and Russian, but Buffer's channels
    get one language. *Recommend:* English to Buffer for now (`posting.languages`), the Russian
    file in Telegram with its text (🇷🇺 under each preview) to post where you like; later,
    Russian accounts as their own Buffer channels (a paid plan beyond 3 channels). For the long
    video, the Russian audio is a second audio track on the same YouTube video (done).

---

## Decisions log

| Date | Decision | By |
|---|---|---|
| 2026-09-29 | Names: Kehai (気配), Aiko (愛子), studio TokenLimit; Aiko's name meaning never explained | owner |
| 2026-10-10 | The antagonist is Karen (カレン, «Карен» in Russian) again, after all; the game stays Kehai | owner |
| 2026-09-29 | n8n runs on node@22 (n8n supports Node 20.19–24.x) | Phase 0 |
| 2026-09-29 | Clip markers on F7 / Left Shift + F7; blink calibration stays on F9 | owner (Q1) |
| 2026-09-29 | Burnout ending: 過労死 / BURNED OUT | owner (Q2) |
| 2026-09-30 | Project at `~/Developer/Kehai`, repo `goraxyy/Kehai` | owner (Q5, Q6) |
| 2026-09-30 | This plan committed with the Phase 2 PR; before that it was untracked in the main folder | Phase 2 |
| 2026-09-30 | Clip moments scoring under 5 are dropped unless they hold a manual marker; `manual_good` weighs 10, `manual_bug` 0 | Phase 2 (for review) |
| 2026-09-30 | A clip moment is at most 45 s; a long marker stretches it at most 20 s; cooldowns for learned, PA, loud mistakes | Phase 2 (for review) |
| 2026-09-30 | Phase 4 stacked on the open Phase 3 PR: the owner said to continue before #15 was merged | owner ("continue") |
| 2026-09-30 | The replay drives the store's own objects where the recording matches them, and builds puppets for the rest; the game's scripts are switched off, not removed | Phase 4 (for review) |
| 2026-09-30 | Shots: H.264 `.mp4`; alpha as VP9 `.webm` (ProRes 4444 `.mov` if asked); a folder gives PNGs; sound as a WAV beside the video, also muxed in | Phase 4 (for review) |
| 2026-09-30 | Drive through rclone with the `drive.file` scope; assets only through `add_asset.py` | owner (Q7) |
| 2026-09-30 | Brand: crimson #DC143C, soft black #151518, white; Nunito + M PLUS Rounded 1c | owner (Q11) + Phase 5 |
| 2026-09-30 | Remotion's bundled ffmpeg replaces Homebrew's; ReplayRender pipes PNG frames | Phase 5 (for review) |
| 2026-10-02 | Phase 6 built without keys (owner: "proceed building"): replayed answers and the macOS voice stand in until the keys exist | owner |
| 2026-10-02 | Every Claude step on Opus 5.5, effort per step; Sonnet 5.5 only if the owner chooses it (`KEHAI_LLM_MODEL`) | Phase 6 (for review) |
| 2026-10-02 | Claude writes drafts in a small schema of its own; the code turns them into edits and checks what the schema can't | Phase 6 (for review) |
| 2026-10-02 | Unity batch runs are silent (the owner heard them); shots record where Karen and you are on screen, and marks follow her | owner + Phase 6 |
| 2026-10-02 | Phase 7 built without keys (owner: "go to phase 7 without them"); node@24 and n8n installed with the owner's OK | owner |
| 2026-10-02 | n8n only schedules: every decision is in run_job.py; posting falls back to Telegram by hand while Buffer isn't ready | Phase 7 (for review) |
| 2026-10-02 | Picks on Saturday's 1 a.m. run, so the weekend is for approving; retention only reports until Q12 is answered | Phase 7 (for review) |
| 2026-10-02 | The schedule runs from launchd, not a running n8n; n8n stays installed for looking and "Run now" | owner ("build option B") |
| 2026-10-04 | Reference videos come as Telegram videos; each becomes a pattern and a short that follows its idea, pacing and format; one of the week's 3 slots | owner (Phase 8 questions) |
| 2026-10-04 | No downloading from links (the platforms' terms); never their footage, words or sound in ours; reference videos aren't archived and go after 30 days | Phase 8 (for review) |
| 2026-09-30 | Heavy jobs share one lock, `~/TokenLimit/marketing/state/heavy.lock` (a directory holding the owner's pid); a job that finds it held exits 75 | Phase 4 (for review) |

## Changelog

- 2026-09-29 — Phase 0: prerequisites checked, plan written.
- 2026-09-29 — Phase 1: rename done and verified; PR #13 opened.
- 2026-09-30 — PR #13 merged; main folder synced; Project Settings set through the Unity MCP; one
  play-mode run migrated the save data and settings. The repo was renamed, the project moved to
  `~/Developer/Kehai`, the Desktop folders renamed, Unity Hub (its `hub.db`) and Claude's memory
  re-pointed. EditMode at the new path: 37/37 (a first run hit a one-off FMOD audio error in one
  fixture's setup and caught two capitalised name spellings in this plan; both fixed).
- 2026-09-30 — Phase 2: clip markers built in `feat/clip-markers`; PR #14 merged.
- 2026-09-30 — Phase 3 started (`feat/replay-recorder`); PR #15 opened.
- 2026-09-30 — Phase 4 built on top of it (`feat/replay-player`): the 3D replay, its cameras and
  Karen's mind, and unattended shot renders; PR #16.
- 2026-09-30 — Phase 5 started (`feat/editor`): brand.json, the edit schema, the Remotion editor,
  the asset library.
- 2026-10-02 — Phase 5 PR #17 opened. Phase 6 built (`feat/voice-writing`): the Claude steps,
  the cost ledger and cap, the voice, shot tracks and following marks, silent batch runs.
- 2026-10-02 — Phase 6 PR #18 opened. Phase 7 built (`feat/n8n`): run_job.py, pipeline.json and its
  state, Telegram approvals, posting, housekeeping, the long video's gates, n8n with 7 workflows;
  verified end to end through n8n from a bot shift to a posted short.
- 2026-10-02 — PR #19 opened. The owner made n8n's account, then chose launchd for the schedule
  (`n8n/launchd.py`, `setup.sh --launchd`, installed); `.env` made in `~/Developer/Kehai` for the
  keys, with step-by-step links in the checklist.
- 2026-10-04 — Phase 8 built (`feat/references`): reference videos sent in Telegram are read (frames,
  cuts, loudness, transcript), studied into patterns, and one of each week's shorts follows one.
