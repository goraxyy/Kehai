# Working on Kehai

Kehai is a first-person horror game in Unity 6. You work a night shift in a convenience store while
Karen, the store's AI, hunts you using only what she senses. Karen's AI is the technical
showpiece.

**Start here:**
- [`docs/you/NOW.md`](../you/NOW.md): what's done, what's being built, what's next, and what waits on the owner.
- [`docs/you/plans/`](../you/plans/): the active plans. Karen 0.4.0 is [`0.4.0-build-plan.md`](../you/plans/0.4.0-build-plan.md).
- [`docs/agents/HANDOVER.md`](HANDOVER.md): where everything is on this Mac, and how to work on it.
- [`docs/agents/karen/Karen.md`](karen/Karen.md) and [`docs/agents/results/KAREN_RESULTS.md`](results/KAREN_RESULTS.md): Karen's full design, and what testing found.
- [`docs/agents/store/SCENE_MAP.md`](store/SCENE_MAP.md) and [`docs/agents/store/STORE_MAP.md`](store/STORE_MAP.md): the scene layout and Karen's map. Neither the scene nor the art is in git.
- [`docs/agents/archive/`](archive/): finished plans. The [0.3.0](archive/0.3.0-build-plan.md) and [0.2.0](archive/0.2.0-build-plan.md) plans say how the tricks work today.
- The [`README.md`](../../README.md) lists every doc.

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

**The docs** (split by reader on 2026-11-11)
- **`docs/you/` is the owner's:** short pages they read and edit (`NOW.md`, `HOW_KAREN_WORKS.md`, `DECISIONS.md`, `IDEAS.md`, `RELEASE_PLAN.md`, the active plans). Agents tick boxes and update status there; anything more only when asked.
- **`docs/agents/` is for agents:** long and exact. Keep it in line with `docs/you/`.
- **One source per fact.** A `docs/you/` page says it short and links to the detail. Never copy a table or a number into two places.
- **When the owner decides something,** add one dated line to `docs/you/DECISIONS.md`, then bring `docs/agents/` in line.
- **A finished or replaced plan moves to `docs/agents/archive/`** (`git mv`, unchanged, in its own commit).
- **`SCENE_MAP.md` and `STORE_MAP.md` are generated.** Never edit them by hand.
- **These rules exist once, in this file.** `CLAUDE.md` and `AGENTS.md` at the top of the repository only point here.

**Unity and the Mac** (an M2 with 8 GB)
- **Never run a second Unity** (batch tests, ablations, renders, builds) while the owner's editor is open (`pgrep -f "Unity.app/Contents/MacOS/Unity"`). Do the non-Unity work first, then ask.
- **One heavy job at a time,** holding the lock at `~/TokenLimit/marketing/state/heavy.lock`: a directory with the holder's pid.
- **Verify on an APFS clone** (`cp -Rc`) when you can't use the editor: productName `Kehai-verify`, then restore the editor prefs and delete the clone afterwards. The recipe is in [`HANDOVER.md`](HANDOVER.md) §6.
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
python3 tools/scene/scene_map.py "Assets/!_Project/_Core/Scenes/SampleScene.unity" > docs/agents/store/SCENE_MAP.md
```
