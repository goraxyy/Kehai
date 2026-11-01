# Handover: picking Kehai up

Written on 2026-10-31, when the work moved to a new account on the **same Mac**. Read this first,
then [`CLAUDE.md`](../CLAUDE.md) (the working rules) and [`notmerged.md`](notmerged.md), which
covers how Karen adapts, with diagrams, and everything left to build.

---

## 1. The project in one minute

- **What it is.** A first-person horror game in **Unity 6**: a night shift in a convenience store.
  - You stock shelves, mop spills, empty bins, serve at the till, and walk lost customers to products.
  - **Karen**, the store's AI, hunts you using only what she can see and hear. She remembers where you hide, and she moves when you blink (an optional webcam). Her touch kills.
  - The studio is **TokenLimit**; the game is **Kehai** (気配, "the sense that someone is there").
- **Old names.** The game was Karoshi until 2026-09-29, and Karen was briefly Aiko (09-30 to 10-10). Those names survive only in `KehaiMigration.cs` and in readers of old save files. Never bring them back.
- **The showpiece is Karen's AI.** It's described in [`Karen.md`](Karen.md), measured in [`KAREN_RESULTS.md`](KAREN_RESULTS.md), and explained simply in [`notmerged.md`](notmerged.md). The pieces:
  - a Bayes-filter belief grid;
  - utility goals and HTN plans over 34 tactics;
  - a PI-controlled Director that paces tension in waves;
  - a UCB bandit and a per-player Ledger;
  - a PPO policy at rung F (built, not trained);
  - an ablation harness with simulated players that react to her.
- **Where it's heading.** Vertical slice → Steam page ("Coming Soon") → playtests → demo → release ([`RELEASE_PLAN.md`](RELEASE_PLAN.md)).
  - Art, levels and UI are placeholder quality on purpose. The owner redesigns everything before launch (Milestone 7), so don't polish visuals.
  - Code quality, saves and tests do matter: they survive the redesign.
- **Who works on it.** One developer, plus AI coding agents. Codex has also worked on the level, in branches under `~/.codex/worktrees`.

## 2. Where everything is on this Mac

The GitHub repository holds **code and docs only**. Everything else lives only on this Mac.

| what | where | in git? |
|---|---|---|
| The Unity project | `~/Developer/Kehai` (Unity **6000.5.10f1**, at `/Applications/Unity/Hub/Editor/6000.5.10f1/`) | code and docs only |
| The store scene | `Assets/!_Project/_Core/Scenes/SampleScene.unity` (about 22 MB, text YAML). Its layout is written out in [`SCENE_MAP.md`](SCENE_MAP.md) | no |
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
  - [`SCENE_MAP.md`](SCENE_MAP.md): every area, with world positions. It's generated by `tools/scene/scene_map.py`; rerun it after changing the scene.
  - [`STORE_MAP.md`](STORE_MAP.md): Karen's own map of the store (regions, links, chokepoints), exported by `Kehai → Map → Export STORE_MAP.md`.
  - [`STORE.md`](STORE.md): the building, the products, stocking, and Karen's map.
  - [`LEVEL_DESIGN.md`](LEVEL_DESIGN.md) and [`INTERIOR_MODELS.md`](INTERIOR_MODELS.md): room plans and the model libraries.
- **Coordinates:** world metres. The street is north (high Z), the store runs south along −Z to the stockroom, and east is +X.
- **If the editor opens an empty Untitled scene,** `OpenStoreOnLaunch.cs` opens the store. Otherwise open `SampleScene.unity` by hand.

## 4. State of the work (2026-10-31)

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
| #62, #63 | simulated players that react to Karen, and the full ladder rerun ([`KAREN_RESULTS.md`](KAREN_RESULTS.md)) |
| #64, #66 | doors, cold room, lockers and exterior fit; the scene hierarchy organised by area |
| #65 | [`notmerged.md`](notmerged.md) |

**Open:**
- **Draft PR #68, the learned motion model** (`feat/learned-motion`). About 70% done, paused before training.
  - Built: the code, the tests, the Python training tools, and the dataset (288 shifts, parity-checked).
  - Left: training (about 5 h), the held-out test (144 shifts) and closed loop (48), and the results.
  - Exact steps: `tools/belief/HANDOVER.md` on that branch. Design: [`plans/learned-motion-model.md`](plans/learned-motion-model.md).
- **The "capture" plan,** approved and not started: [`plans/karen-capture.md`](plans/karen-capture.md).
  - A two-headed contextual bandit that learns both Δpanic and the chance of a kill.
  - Three fair kill moves: cut you off, wait on your path, a deadly step-out.
  - 7 test arms × 400 shifts. It starts after PR #68 is merged.

## 5. What's next, in order

1. **Finish PR #68:** train, test, write up, merge.
2. **Build the capture plan** as one PR.
3. **Train rung F** (PPO; about 10 Mac nights, or the university's Windows PCs). It's judged against rung E on the same seeds.
4. **Real players.** The playtest pipeline is built and waits on:
   - a Cloudflare account (then the `wrangler` deploy in `tools/playtest/README.md`);
   - a Google Form, a password-protected itch.io page, and the Telegram and Claude keys.
5. **The release plan:** setup accounts, the vertical slice, the Steam page, playtests, demo and Next Fest, marketing, beta, the redesign ([`RELEASE_PLAN.md`](RELEASE_PLAN.md)).

**Known gaps:**
- Karen can stand on a stuck player without catching them.
- The Director nudges her belief toward the player's true region after 45 s without a sighting (`KarenDirector.MaybeBias`). Published out-of-view belief numbers include this hint.
- The README still says "she gives you overtime", but a touch now kills.
- `DECISIONS.md` still says she can't outrun a sprint, but she's now 5% faster than the player.

## 6. How to work on it

The short rules are in [`CLAUDE.md`](../CLAUDE.md). The longer how-to:

- **Verification without the editor.** Batch mode:
  ```bash
  /Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode -testResults <xml>
  ```
  Ablations use `-executeMethod EvalBatch.Play -kehai-ablation …` ([`tools/eval/README.md`](../tools/eval/README.md)).
- **Verification while the editor is open.** Run on an **APFS clone**:
  1. `cp -Rc ~/Developer/Kehai <scratch>/clone`, then delete the clone's `Temp/` and `.claude/worktrees`.
  2. Lay the branch on with `git archive HEAD | tar -x -C <clone>`.
  3. Set the clone's `productName` to `Kehai-verify`, so its data stays out of the real save folder.
  4. Run batch mode on the clone, holding the heavy-work lock.
  5. Afterwards:
     - point the editor prefs back at the real project (`defaults write com.unity3d.UnityEditor5.x` `LastUsedProjectPath`, `kProjectBasePath`, `kWorkspacePath` → `~/Developer/Kehai`);
     - delete the clone, `~/Library/Application Support/TokenLimit/Kehai-verify` and the `unity.TokenLimit.Kehai-verify` prefs.

  Only ask before running Unity at all if the owner's editor is open: this 8 GB Mac can't run two.
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
