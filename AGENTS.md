# Working on Kehai

Kehai is a first-person horror game in Unity 6. You work a night shift in a convenience store while
Karen, the store's AI, hunts you using only what she senses. Karen's AI is the technical
showpiece.

**Start here:**
- [`docs/project/HANDOVER.md`](docs/project/HANDOVER.md): where everything is on this Mac, the state of the work, and what's next.
- [`docs/plans/0.3.0-build-plan.md`](docs/plans/0.3.0-build-plan.md): Karen 0.3.0, the tricks rebuilt end to end. **The plan you're building.**
- [`docs/plans/0.2.0-build-plan.md`](docs/plans/0.2.0-build-plan.md): Karen 0.2.0, merged; how the tricks work today.
- Docs are sorted by topic under `docs/` (`project/`, `plans/`, `karen/`, `store/`, `game/`, `marketing/`, `research/`); finished or stale ones go to `docs/archive/`. The README lists them all.
- [`docs/store/SCENE_MAP.md`](docs/store/SCENE_MAP.md) and [`docs/store/STORE_MAP.md`](docs/store/STORE_MAP.md): the scene layout and Karen's map. Neither the scene nor the art is in git.
- Then [`README.md`](README.md), [`docs/karen/Karen.md`](docs/karen/Karen.md), [`docs/karen/KAREN_RESULTS.md`](docs/karen/KAREN_RESULTS.md) and [`docs/project/RELEASE_PLAN.md`](docs/project/RELEASE_PLAN.md).

## Rules

**The repository**
- **Code and docs only.** Push `.cs` (and shader code) with their `.meta` files, plus Markdown and the tools' code. Never push:
  - models, materials, audio, prefabs or scenes;
  - `Packages/` or `ProjectSettings/`;
  - `.env` files or keys.
- **Authored data goes in C#,** not ScriptableObjects, prefabs or the scene. For example, `StoreLayout.cs` stocks the shop at load.
- **Branch work happens in a git worktree** from `origin/main`.
  - **Never stash, checkout or clean in the main folder** (`~/Developer/Kehai`). Its art files are untracked and only excluded locally, and a `git stash -u` there once wiped 579 asset files.
  - Pull main with `git pull --ff-only`.
- **Old names stay dead.** The game is Kehai and the antagonist is Karen. The old names (Karoshi, Aiko) appear only in `KehaiMigration.cs` and in readers of old saves.

**Unity and the Mac** (an M2 with 8 GB)
- **Never run a second Unity** (batch tests, ablations, renders, builds) while the owner's editor is open (`pgrep -f "Unity.app/Contents/MacOS/Unity"`). Do the non-Unity work first, then ask.
- **One heavy job at a time,** holding the lock at `~/TokenLimit/marketing/state/heavy.lock`: a directory with the holder's pid.
- **Verify on an APFS clone** (`cp -Rc`) when you can't use the editor: productName `Kehai-verify`, then restore the editor prefs and delete the clone afterwards. The recipe is in `docs/project/HANDOVER.md` §6.
- **Nothing you run may make sound.** Batch runs are silent by design. Mute the GUI editor (`EditorUtility.audioMasterMute`) before entering Play there. Never touch the Mac's volume.
- **Batch-mode loops** over game actions must yield at least one frame per iteration.

**How the owner wants things done**
- **Karen's ML work is planned first, then built as one big PR.** Write the plan, get it approved, then build. No piecemeal PRs.
- **Evaluate honestly:**
  - seeded, paired ablations with standard errors;
  - the held-out "cautious" player is never used for tuning or training;
  - primary metrics are written down before test runs;
  - negative results are reported as they come.
- **Quick pass now, redesign later.** Art, levels and UI only need to be good enough to show. The owner redesigns them before launch. Code, saves and tests must still be solid.
- **New props:** ask whether materials are wanted (older props are geometry-only). Use real-world metre scale. Catalogue items have their origin at the box centre.
- **Explain plainly:** short bullets, tables, a concrete example.
- **Explain new terms in three parts:** what it is, how Kehai uses it, and a real-world example from apps or products people know.

## Handy commands

```bash
# EditMode tests, editor closed
/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults results.xml

# A quick ablation, editor closed (see tools/eval/README.md)
/Applications/Unity/Hub/Editor/6000.5.10f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -executeMethod EvalBatch.Play -kehai-ablation -ablation-careers 1 -ablation-shifts 2 -ablation-rungs CE

# Analyse an ablation
python3 tools/eval/analyze_ablation.py <kehai_eval>/ablation_<time>.jsonl

# Regenerate the scene map after changing the scene
python3 tools/scene/scene_map.py "Assets/!_Project/_Core/Scenes/SampleScene.unity" > docs/store/SCENE_MAP.md
```

## If you're the second builder, [B]

Two AI builders work on Karen 0.3.0 at the same time. You are **[B]** in
[`docs/plans/0.3.0-build-plan.md`](docs/plans/0.3.0-build-plan.md); Claude is [C].

- **Your tasks:** only the ones tagged [B], phase by phase: B2.1–B2.4, B3.1–B3.2, B4.1–B4.2, B6.1, B7.1–B7.2. Tick each box (`- [ ]` → `- [x]`), its step boxes as they land, and the progress table at the top, in the same commit as the work.
- **When to start:** after **Sync 1**, when [C]'s Phase 1 APIs (task C1.6) are on `0.3.0`. Merge `0.3.0` into `0.3.0-b` first.
- **Your place:** the worktree `.claude/worktrees/tricks-b`, on branch `0.3.0-b`. Never commit to `0.3.0` yourself: at each sync point [C] merges `0.3.0-b` into `0.3.0`, and you then merge `0.3.0` back into `0.3.0-b`.
- **Your files:** only the ones your task lists. If you need a file that isn't on the list, add a row to the plan's §9 "Requests between builders" and move on to something else.
- **The APIs between you:** use [C]'s APIs from C1.6 (`ShiftManager.Freeze`, `FogState`, `PaSystem.SetPowered`, `ProceduralAudio.CameraBeep`, the new task-list jobs, `KarenBrain.Force`). Don't change them; ask in §9.
- **Tests:** leave any test you write inside the repo (an `Editor/Tests` folder), never in `/tmp`.
- **Labels:** if you rename text the game writes to a file (a shift-record field, a clip marker), grep `tools/` too: the Python readers match labels by text.
- **Unity:** never start one while the owner's editor is open (see the rules above). Ask the owner first.
- **Commits** read as the owner's own: no AI credit and no `Co-Authored-By` lines.
- **New props:** the owner wants materials for the new ones (the camera, the fridge control box, the PA box). [C] makes the models (C6.1); you import them (B6.1) and keep a geometry fallback.
