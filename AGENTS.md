# Working on Kehai

Kehai is a first-person horror game in Unity 6. You work a night shift in a convenience store while
Karen, the store's AI, hunts you using only what she senses. Karen's AI is the technical
showpiece.

**Start here:**
- [`docs/project/HANDOVER.md`](docs/project/HANDOVER.md): where everything is on this Mac, the state of the work, and what's next.
- [`docs/plans/0.3.0-build-plan.md`](docs/plans/0.3.0-build-plan.md): Karen 0.3.0, the tricks rebuilt end to end (one builder: Claude).
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
