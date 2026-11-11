# Handover: picking Kehai up

Written on 2026-10-31, when the work moved to a new account on the **same Mac**. It says where
everything is and how to work on it. For what's being built and what's next, read
[`NOW.md`](../you/NOW.md) first; the working rules are in [`RULES.md`](RULES.md). The older
[`notmerged.md`](archive/notmerged.md) (archived on 2026-11-05) still explains how Karen adapts,
with diagrams, but its to-do list is out of date.

---

## 1. The project in one minute

- **What it is.** A first-person horror game in **Unity 6**: a night shift in a convenience store.
  - You stock shelves, mop spills, empty bins, serve at the till, and walk lost customers to products.
  - **Karen**, the store's AI, hunts you using only what she can see and hear. She remembers where you hide, and she moves when you blink (an optional webcam). Her touch kills.
  - The studio is **TokenLimit**; the game is **Kehai** (気配, "the sense that someone is there").
- **Old names.** The game was Karoshi until 2026-09-29, and Karen was briefly Aiko (09-30 to 10-10). Those names survive only in `KehaiMigration.cs` and in readers of old save files. Never bring them back.
- **The showpiece is Karen's AI.** It's described in [`Karen.md`](karen/Karen.md), measured in [`KAREN_RESULTS.md`](results/KAREN_RESULTS.md), and explained simply in [`notmerged.md`](archive/notmerged.md). The pieces:
  - a Bayes-filter belief grid;
  - utility actions and HTN plans over 36 tricks, 14 of them live in 0.2.0 (before it: utility goals and HTN plans over 34 tactics, all in play);
  - guesses at your route while she hunts, which her Cut and Ambush race to (0.2.0);
  - a PI-controlled Director that paces tension in waves;
  - a UCB bandit and a per-player Ledger, which since 0.2.0 also learns which pairs of tricks pay (the combo room);
  - a PPO policy at rung F (built, not trained);
  - an ablation harness with simulated players that react to her.
- **Where it's heading.** Vertical slice → Steam page ("Coming Soon") → playtests → demo → release ([`RELEASE_PLAN.md`](../you/RELEASE_PLAN.md)).
  - Art, levels and UI are placeholder quality on purpose. The owner redesigns everything before launch (Milestone 7), so don't polish visuals.
  - Code quality, saves and tests do matter: they survive the redesign.
- **Who works on it.** One developer, plus AI coding agents. Codex has also worked on the level, in branches under `~/.codex/worktrees`.

## 2. Where everything is on this Mac

The GitHub repository holds **code and docs only**. Everything else lives only on this Mac.

| what | where | in git? |
|---|---|---|
| The Unity project | `~/Developer/Kehai` (Unity **6000.5.10f1**, at `/Applications/Unity/Hub/Editor/6000.5.10f1/`) | code and docs only |
| The store scene | `Assets/!_Project/_Core/Scenes/SampleScene.unity` (about 22 MB, text YAML). Its layout is written out in [`SCENE_MAP.md`](store/SCENE_MAP.md) | no |
| The endless-maze scene (parked) | `Assets/!_Project/_Core/Scenes/EndlessMaze.unity`, last and unticked in the build list | no |
| Art: models, materials, prefabs, audio, textures | under `Assets/` (about 160 MB). Art files are listed in the local-only `.git/info/exclude`, so `git status` stays clean | no |
| The product model pipeline (80 textured products) | `~/Desktop/KehaiRelated/Products/`: `tools/products.py` → `products.json` (from `ProductCatalog.cs`), `tools/make_labels.swift` → labels and textures, `tools/build_products.py` (headless Blender) → blend, FBX and thumbnails | no |
| Older art, and the "_nomat" blends | `~/Desktop/KehaiRelated/archive/` | no |
| Blender | the Steam install: `~/Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender` (`/Applications/Blender.app` is only a launcher) | — |
| Game data | `~/Library/Application Support/TokenLimit/Kehai/`: Karen's ledger, shift records, and `kehai_eval/`, which holds the ablation runs and the motion model's 480 MB dataset in `belief_data/` | no |
| Backups from the renames (keep) | `~/Library/Application Support/TokenLimit/Karoshi/`, `~/Library/Preferences/unity.TokenLimit.Karoshi.plist`, and the `aiko_*` files in `TokenLimit/Kehai/` | no |
| Builds | `~/Developer/Kehai/Builds/` (gitignored); RL training builds in `Builds/KehaiTrain/` | no |
| Marketing automation | the code in `tools/marketing/`. Keys in `tools/marketing/.env` (**never commit it**). Output in `~/TokenLimit/marketing/`; n8n in `~/TokenLimit/n8n/`. The jobs run from launchd agents `com.tokenlimit.kehai.job.*` | code yes, keys and media no |
| The playtest pipeline | `tools/playtest/` (a Cloudflare Worker and R2). Settings in `tools/playtest/.env`; the report at `~/TokenLimit/playtests/report.html` | code yes |
| The RL and ML Python environment | `tools/rl/.venv` (Python 3.10.12, `mlagents` 1.1.0, PyTorch), shared with `tools/belief/` | no |
| The heavy-work lock | `~/TokenLimit/marketing/state/heavy.lock`: a directory holding the holder's pid. Batch Unity, training and renders take it, so only one heavy job runs at a time | — |
| Old scene and model history | local git tags `scene-rescue`, `archive/prop-models`, `archive/prop-models-tip` | local only |
| Stashes in the main folder | `main-folder-copy-of-pr64-66-2026-10-31` and `kehai-wip-identical-to-pr55-2026-10-16`. Both are identical copies of work already on main, so they're safe to drop | local only |

## 3. How the scene is put together

- **A lot of the level is built by editor menus from code**, so it can be rebuilt. The menus, all under `Kehai`:

  | menu | what it builds or does |
  |---|---|
  | **Store** | `Apply Layout` (the shelf layout from `StoreLayout.cs`), `Hang Signs and Lamps`, `Migrate to Data Shelves`, `Report Layout` |
  | **Store Finish** | `Build Sales Floor, Front and Backstreet`, `Build Parking Lot`, `Refine Fixtures, WC and Midnight Lighting`, `Apply Compact Cold Room and Environment Fit`, `Apply Door, Locker and Exterior Fit` |
  | **Staff Room** | 1. model library · 2. build the room and Models_Island · 3. organise the island · 4. bake navigation |
  | **Office** | 1. model library · 2. build the office |
  | **Public Interiors** | 1. WC and lobby model library · 2. build the WC · 3. build the lobby |
  | **Shelves** | 1. model prefabs · 2. ready prefabs · 3. swap the scene's shelves · 4. paint them grey |
  | **Products** | 1. import the product models · 2. lay out every product on Models_Island · 3. a box of each · 4. price tags |
  | **Storage** | rebuild the boxes |
  | **Scene** | `Organize by Area and Name Objects` (`SceneHierarchyOrganizer.cs`) and its undo |
  | **Map** | `Export STORE_MAP.md`, `Log Map Summary` |
  | **Endless** | open the endless maze |
  | **Eval** | env server (port 5555), quick and full ablations, an eval build, watch one bot shift |
  | **Replay** | open the latest shift, or any shift |
  | **RL** | set up ML-Agents, build training players |
  | **Build** | macOS, Windows, and playtest builds |
  | **Playtest** | forget the tester on this computer |

- **The stock is data, not scene objects.** The 16,502 shelf slots are plain records rebuilt from the planogram in code at load (`StoreLayout.cs`, `Planogram.cs`, `ShelfGrid.cs`). They're drawn as bare render objects through the GPU Resident Drawer.
- **The hierarchy is organised by area** under six roots: `Supermarket` (one folder per room), `Exterior`, `Game_Systems`, `Models_Island` (an off-map showcase of every model), `Authoring_References` and `Generated_Content`.
- **Four documents describe the layout:**
  - [`SCENE_MAP.md`](store/SCENE_MAP.md): every area, with world positions. It's generated by `tools/scene/scene_map.py`; rerun it after changing the scene.
  - [`STORE_MAP.md`](store/STORE_MAP.md): Karen's own map of the store (regions, links, chokepoints), exported by `Kehai → Map → Export STORE_MAP.md`.
  - [`STORE.md`](store/STORE.md): the building, the products, stocking, and Karen's map.
  - [`LEVEL_DESIGN.md`](store/LEVEL_DESIGN.md) and [`INTERIOR_MODELS.md`](store/INTERIOR_MODELS.md): room plans and the model libraries.
- **Coordinates:** world metres. The street is north (high Z), the store runs south along −Z to the stockroom, and east is +X.
- **If the editor opens an empty Untitled scene,** `OpenStoreOnLaunch.cs` opens the store. Otherwise open `SampleScene.unity` by hand.

## 4. State of the work (updated 2026-11-07)

**Merged to main** (highlights; the full history is in git and [`CHANGELOG.md`](CHANGELOG.md)):

| PRs | what |
|---|---|
| #13, #47 | the rename to Kehai, and the docs remake |
| #14–#21, #27 | the marketing pipeline: clip markers, `.krec` replays, 3D replay, Remotion editor, voice and writing, scheduling, reference videos |
| #25, #26 | the playtest pipeline (game side, and the worker plus the Mac side) |
| #34–#40 | rendering and scaling: GPU Resident Drawer, shelves as data, the endless maze |
| #56, #57 | blink accuracy made honest; the reopen-prediction fix |
| #58 | belief accuracy measured per rung |
| #59 | rung F: a PPO policy picks Karen's tactics (Unity ML-Agents) |
| #60 | the blink dash in waves, touch kills, Karen slightly faster, a black capsule body |
| #62, #63 | simulated players that react to Karen, and the full ladder rerun ([`KAREN_RESULTS.md`](results/KAREN_RESULTS.md)) |
| #64, #66 | doors, cold room, lockers and exterior fit; the scene hierarchy organised by area |
| #65 | [`notmerged.md`](archive/notmerged.md) |

**Open:**
- **Karen 0.3.0, the tricks come back** (branch `0.3.0`, worktree `.claude/worktrees/tricks`; draft [PR #78](https://github.com/goraxyy/Kehai/pull/78)). The owner's 21 items of 7 November: nine tricks removed, ten rebuilt end to end, a fog that blinds everyone, a PA off switch, placement memory.
  - Plan with tick boxes and a progress table: [`plans/0.3.0-build-plan.md`](archive/0.3.0-build-plan.md). What each trick does now: [Karen.md §17](karen/Karen.md#17-karen-030-the-tricks-come-back).
  - **One builder** (the owner's call, 7 November): Claude built every task, the old [B] ones too.
  - **Built (7 November):** phases 0–4, most of 5, the props' Blender models (`~/Desktop/KehaiRelated/Props`) and their importer, the claims, and the trick check (`-kehai-trick-check`). All 15 of the owner's live tricks are live; five drafts stay.
  - **The trick check** passes: every forced trick starts and lands, with 0 fairness violations. The test player fixes every chore but Spill's and Shelf sweep's, which the shift sometimes ends before it gets to.
  - **The ablation against `main`** ran on 7 November ([KAREN_RESULTS.md](results/KAREN_RESULTS.md#karen-030-the-ablation-7-november)):
    - fairness 0, and she uses 3.6 tricks a shift against 1.1;
    - **13 of 240 shifts got stuck**, against 2 on `main`: mostly because she keeps making chores after the doors close, and the clock-out waits for all of them;
    - also found: the bot falling through Aisle 4/B's floor, the bot wedging on the staff-room furniture, 0.3.0 running 3.6 times slower, and the fog's log flood (fixed);
    - the understudy, the overtime and Cut never ran.
  - **Rule 10 (8 November, the owner's call):** no new chore once the doors close. It's built and tested.
  - **Every trick from shift 1 (8 November, the owner's call),** for testing: the career table is off behind `KarenConfig.careerTable`, to come back later.
  - **Tool theft fixed:** she stopped 2.2 m from the WC rack, out of rule 9's reach, so the take failed; she now stands at the nearest spot on her floor.
  - **The second run (8 November)** ([KAREN_RESULTS.md](results/KAREN_RESULTS.md#the-second-run-8-november-rule-10-every-trick-from-shift-1)):
    - 0 chores after closing;
    - 9 stuck shifts (the first run had 13, `main` 2), now from the test player's own walking and fear.
  - **Found and fixed:**
    - the understudy's and the overtime's cooldown kept them from ever running;
    - Shelf sweep's bay lookup is cached (14% faster).
  - **Found:** her hands-on tricks (Spill, Bin tamper, Tool theft, Camera bolt-on, Block door, mostly Shelf sweep) rarely landed in play. She re-decided on the walk and dropped them. **Fixed on the owner's call (8 November):** she finishes what she starts, unless she starts a hunt. In a 60-shift check Spill then landed 39 of 42 times (was 1 of 55), and stuck shifts fell from 7 to 3.
  - **Left:** the full ablation again (on the owner's go); a spill spot in the office the test player can't reach; the slowdown and the fall; the How Karen Learns guide; the owner's model import (Kehai → Props → Import Karen's Props), playtest and merge.
- **PR #74, Karen 0.2.0** (`0.2.0` → `main`): **merged** (`d5d14f3`). What follows is how it was built, kept for reference.
  - Plan with tick boxes: [`plans/0.2.0-build-plan.md`](archive/0.2.0-build-plan.md). What changed in the code: [Karen.md §16](karen/Karen.md#16-karen-020). Why: [DECISIONS.md §10](DECISIONS.md#10-karen-020).
  - **Two builders.** Claude [C] does the logic on `0.2.0` (worktree `.claude/worktrees/0.2.0`). A second agent [B] does level design and small code on `0.2.0-b` (`.claude/worktrees/0.2.0-b`), following `AGENTS.md`. [C] merges `0.2.0-b` in at each sync point. Requests between them go in the plan's §7.
  - **Built [C]:** phases 0–5's code: her area and grid, actions and drafts, Hunt at 7.5 m/s on her guesses, no warnings, the route guesses, Cut, Throw and their effects, her eyes, the combo room, bots that turn back and dodge, and the ablation's measurements. At syncs 2–4 (6 November) the second builder's work was merged in, Block door went live, and replays record her route guesses (`.krec` version 2). All 290 EditMode tests pass.
  - **The ablation against `main`** ran on 6 November ([KAREN_RESULTS.md](results/KAREN_RESULTS.md#the-ablation-results-6-november)). She's far deadlier: a catch in 94–100% of shifts, against 28–54%, for every player, the held-out one included. `main` took two runs: the first stopped at its time limit after 91 shifts, and a second ran the reckless and cautious players on the same seeds.
  - **Left [C]:** the How Karen Learns guide.
  - **Built [B]:** the map legend, the developer switch (F3), replays without her steps, Block door rebuilt round a restock box, the thrown items seen, the effects felt, the combo list in the shift report, and the claims (B1.1–B5.1). B3.3, her guesses drawn on the F1 map and in replays, too; merged 6 November. **Nothing is left for [B].**
  - **Left for the owner:** playtest a shift (Y5.1; the second builder re-exported the maps, Y1.1, at your request), then review and merge (Y5.2). Before pulling the merge into the main folder, move its untracked `AGENTS.md` aside: the branch tracks its own.
- **Draft PR #68, the learned motion model** (`feat/learned-motion`). About 70% done, paused before training.
  - Built: the code, the tests, the Python training tools, and the dataset (288 shifts, parity-checked).
  - Left: training (about 5 h), the held-out test (144 shifts) and closed loop (48), and the results.
  - Exact steps: `tools/belief/HANDOVER.md` on that branch. Design: [`plans/learned-motion-model.md`](archive/learned-motion-model.md).
  - After 0.2.0 its dataset has to be re-recorded on the new grid (her cells and one outside cell). Train only on the owner's go.
- **The "capture" plan,** approved and not started: [`plans/karen-capture.md`](archive/karen-capture.md).
  - A two-headed contextual bandit that learns both Δpanic and the chance of a kill.
  - Three fair kill moves: cut you off, wait on your path, a deadly step-out.
  - 7 test arms × 400 shifts. It starts after PR #68 is merged.
  - Not in 0.2.0. Cut, and Ambush on her guessed routes, now cover part of its first two kill moves, so re-read it against 0.2.0 before building it.

## 5. What's next, in order

The list moved to [`NOW.md`](../you/NOW.md) on 2026-11-11, so it's kept in one place. §4 above
is the history up to 10 November: since then PR #78 (0.3.0) is merged and PR #68 is closed,
redesigned inside the [0.4.0 plan](../you/plans/0.4.0-build-plan.md).

**Known gaps:**
- Karen can stand on a stuck player without catching them.
- The Director nudges her belief toward the player's true region after 45 s without a sighting (`KarenDirector.MaybeBias`). Published out-of-view belief numbers include this hint.
- On 0.2.0, Cut rarely fires: never on a 25 m dash, sometimes on a 45–60 m run (Karen.md §16.4), and never in the ablation's 144 shifts.
- On 0.2.0, `main` runs 2 to 4.6 times slower per simulated second than the branch in batch mode, for a reason not yet found; its log repeats a particle-system warning tens of thousands of times. Allow for it when timing a run against `main`.

## 6. How to work on it

The short rules are in [`RULES.md`](RULES.md). The longer how-to:

- **Verification without the editor.** Batch mode:
  ```bash
  /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode -testResults <xml>
  ```
  Ablations use `-executeMethod EvalBatch.Play -kehai-ablation …` ([`tools/eval/README.md`](../../tools/eval/README.md)).
- **Verification while the editor is open.** Run on an **APFS clone**:
  1. `cp -Rc ~/Developer/Kehai <scratch>/clone`, then delete the clone's `Temp/` and `.claude/worktrees`.
  2. Lay the branch on with `git archive HEAD | tar -x -C <clone>`.
  3. Set the clone's `productName` to `Kehai-verify`, so its data stays out of the real save folder.
  4. Run batch mode on the clone, holding the heavy-work lock.
  5. Afterwards:
     - point the editor prefs back at the real project (`defaults write com.unity3d.UnityEditor5.x` `LastUsedProjectPath`, `kProjectBasePath`, `kWorkspacePath` → `~/Developer/Kehai`);
     - delete the clone, `~/Library/Application Support/TokenLimit/Kehai-verify` and the `unity.TokenLimit.Kehai-verify` prefs.

  Only ask before running Unity at all if the owner's editor is open: this 8 GB Mac can't run two.
- **Comparing a branch with `main`** (as 0.2.0's ablation does):
  1. Check the main folder matches `origin/main`, then clone it the same way, with `productName` `Kehai-verify-main`.
  2. Add only the measuring code. For 0.2.0 that's `HuntMetrics.cs`, with its parts between `// -- 0.2.0 only --` and `// -- end --` taken out, and its hooks in `EpisodeMetrics.cs`.
  3. Run both clones one after the other on the same seeds, rungs and players.
  4. Pair the runs with `tools/eval/compare_builds.py main.jsonl branch.jsonl` ([`tools/eval/README.md`](../../tools/eval/README.md)).
  5. Clean up both clones, their data folders and prefs, and the editor prefs, as above.
- **Offline compile check.** Copy the `Assembly-CSharp(.Editor).rsp` files from `Library/Bee/artifacts/200b0aE.dag/`, point `-out` at a scratch folder, add the new sources (each on its own line), and run Unity's bundled Roslyn `csc.dll` through its `dotnet`.
- **Audio.** Batch runs are silent (`SoundSettings.Audible()` returns 0 in batch mode). In the GUI editor, set `EditorUtility.audioMasterMute = true` before entering Play or running tests that do. Never change the Mac's volume.
- **The Unity bridges.**
  - The editor has an MCP bridge (`Unity_RunCommand`).
  - If it hangs after a domain reload, a second bridge (MCP for Unity, `com.coplaydev.unity-mcp`) answers on `127.0.0.1:6400`.
  - Menu items can also be run through the macOS app menu.
- **Evaluation discipline.**
  - Seeded, paired ablations with standard errors.
  - A held-out player type (cautious) that is never used for tuning or training.
  - Primary metrics written down before test runs.
  - Negative results reported as they come.
