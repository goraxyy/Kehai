# Scenes and version control

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 3: "Build 3 scenes for testing, training
and dev (maybe add more for multiple dev, and add version control)." Rebuilt on 21 November
around your answers to Q14–Q16. The questions (Q numbers) are in the plan's
[open questions](../0.5.2-plan.md#11-open-questions). Prices were checked on 17 November 2026:
**check them again before paying for anything.** How versions are numbered and tagged is on its
own page: [versions](versions.md).

1. [What exists today](#1-what-exists-today)
2. [The scenes you asked for](#2-the-scenes-you-asked-for)
3. [Does a light Training scene make training faster?](#3-does-a-light-training-scene-make-training-faster)
4. [One scene for sound and models?](#4-one-scene-for-sound-and-models)
5. [Version control for three people](#5-version-control-for-three-people)
6. [Checking each other's work](#6-checking-each-others-work)
7. [Art and data to Windows](#7-art-and-data-to-windows)
8. [Disk and cost](#8-disk-and-cost)

---

## 1. What exists today

- **One store scene,** `SampleScene.unity`: 22.6 MB of text (Unity's "Force Text" setting is on,
  so scenes and prefabs are readable, mergeable text). The parked endless maze is a second scene.
  The main folder also holds a crash-recovery scene in `Assets/_Recovery`.
- **Three editions from one project** (0.5.0, [versions](../0.5.0/versions.md)). All three build
  from the same scene list, which lives in the project settings, not in git. The Training
  edition is stripped **only when a player is built** (`EditionBuildProcessor`: no UI, no audio,
  no materials, cameras off). In the editor, every test, ablation and training run loads the
  full scene with the Testing edition's rules.
- **Every training or test shift reloads the whole store scene** (`KehaiEnv` resets with
  `LoadScene`), and runs at a fixed 15 frames per simulated second in ablations and training.
- **Six layouts are C# data** built into the same scene at load: no scene per layout.
- **Much of the scene is built by menus from code** ([HANDOVER §3](../../../agents/HANDOVER.md#3-how-the-scene-is-put-together)),
  so it can be rebuilt.
- **Art sits beside code in `Assets/`.** Only code goes public; the art is kept out by a local
  exclude list (126 lines). The private `kehai-assets` repo has one zip release (11 November)
  and no history.
- **Two full copies of the private files exist** (the main folder and `0.5.0-build`), nearly the
  same: compared on the [testing page](testing.md#1-what-gets-in-the-way-today). The main folder
  is the source (Q20).
- **Two separate projects are already decided:** the sound designer's stripped sound project in
  `kehai-assets/SoundProject` (the [sound plan](https://github.com/goraxyy/Kehai/pull/86), which
  assumes Git LFS, its Q4), and the Look Lab (0.5.0 item 8, option B).
- **Unity's scene merge tool is installed:** `UnityYAMLMerge`, inside the editor at
  `Unity.app/Contents/Helpers/`.

---

## 2. The scenes you asked for

Your answers (21 November): four scenes, with sound and models sharing one **Workshop** scene, and
a dev layer each for you, the sound designer and the artist. You also work in the Training scene, mostly.

| Scene | What it's for | Who works in it | What's in it |
|---|---|---|---|
| **Testing** | the store as playtesters see it: they download the Testing build, play, and send their sessions back for the shared training; later the Gameplay build too | you, and the builder menus | everything: the quick-pass art, the sounds, the UI |
| **Training** | benchmarking the two Karens (Satori and Oni), trying tricks and interactions; the PC's training copies | **you, mostly**, and the bots | the store's shape only: walls, shelves, doors, the NavMesh, the lights she senses, and one plain grey look on everything so you can still play it; no sound, no UI text |
| **Workshop** (sound and models) | placing and mixing every sound; visual improvements | the sound designer (sound layer), the artist (art layer) | the store with its art and its sound sources |
| **Dev layers** | experiments: yours on Karen (over Training), the sound designer's and the artist's (over their scenes) | each person their own | only that person's experiments; never shipped |

**How they fit together (my pick):** one **base** scene holds everything that plays (walls,
shelves, doors, the NavMesh, the lights she senses, every object a script uses). An **art layer**
and a **sound layer** are scenes loaded on top of it ([§4](#4-one-scene-for-sound-and-models)).

| Scene you asked for | Made of |
|---|---|
| Testing | base + art layer + sound layer |
| Training | base, generated light (grey look, no sound, no UI) by a menu, never edited by hand |
| Workshop | base + art layer + sound layer, open together; each person edits only their own layer |
| Dev | a layer on top of any of them |

**Why Training is generated, not kept by hand:** what you change while working there is code
(tricks, interactions, Karen) or a dev-layer experiment. The store's shape must stay exactly the
Testing scene's, or she trains on a different store from the one players get.

**Example:** the artist moves a shelf lamp in the art layer. The Testing build has it at once;
the Training scene doesn't change, because the lamp's look isn't in the base, and the lamp's
light, if she sees by it, is.

| Design | Pros | Cons |
|---|---|---|
| S1. Training generated; separate copies of the store for sound and for models | quick to start | three copies of the store drift; the artist's work is copied into Testing by hand |
| **S2. A base and layers (my pick)** | each person edits one file; Training has few assets by design; the art reaches Testing with no copy step | splitting today's scene is a careful job (her map and the NavMesh must come out the same); Unity can't link objects across scenes, so no script may point into a layer |
| S3. Training generated; one shared content scene with locks | simple | one person at a time on the content scene |

**Built in steps:** first the generated Training scene (cheap: the stripping code exists), then
the layers when the sound designer or the artist actually starts, room by room, moving the
look-only objects out of the base.

---

## 3. Does a light Training scene make training faster?

**Honestly: only a little per frame. It helps more with how fast each shift restarts and how
many copies fit at once.** All the sizes below are *estimates* until C8.3 measures them, except
where marked measured.

- **What costs nothing in training already:** in batch mode with no graphics, Unity draws
  nothing, so textures, materials and the look barely cost anything per frame.
- **What a shift's frames are spent on:** physics, navigation (paths and door carving), the
  customers walking, the bots, and her: her belief update is 13–16% of a batch frame and her
  decisions 7–10% (measured, 12 November); writing learning records added about 12% (7.3 against
  6.4 ms a frame, measured). Objects that aren't drawn still cost a little (moving and culling them).
- **What fewer assets do cut:** **load time,** because every shift reloads the whole scene, and
  **memory,** so more copies run at once on the PC.

**Does each need building?** You asked on 21 November. Every one is now a task in the plan:

| Speed-up | What it does | Rough gain (*estimate*) | Building or a setting? | Task (phase) |
|---|---|---|---|---|
| The light Training scene | quicker reloads, smaller copies | reloads perhaps 20–50% quicker; frames 0–10% | **building:** a menu that makes the scene | C8.1–C8.3 (Phase 8) |
| Fixing the batch slowdown | — | up to 2.25× if the old gap is still there ([testing §5](testing.md#5-the-batch-slowdown)) | **building:** find it, then fix it ("hunt first", Q10) | C6.3, C6.4 (Phase 6) |
| A built Training player, not the editor | no editor costs | unknown; large if the slowdown lives in the editor | **mostly built** in 0.5.0; checking that it connects, and fixing it if not, is building | C6.6 (Phase 6) |
| More copies at once on the PC | uses more of its cores | up to about 1.5× in all, if memory allows | **a setting** (the number of copies, `-Envs`), then measured | C6.7 (Phase 6; waits for the PC and C6.6) |
| Learning records off when a run doesn't use them | skips writing every choice | about 12% of a frame | **a setting:** `-kehai-no-records` exists. Never on a test shift: those are all recorded (Q1) | C6.5 (Phase 6) |
| Fewer frames per simulated second (15 → 10) | a third fewer frames | up to 30% | **a setting** (`-ablation-fps`, the league's `fps`), plus a check that she and the bots still play the same | C6.8 (Phase 6) |
| Fewer customers in an easy first stage | less walking and pathfinding | 5–15% | **a small build:** a shopper-cap flag (today the cap is fixed in code), off unless a training config sets it | C6.9 (Phase 6); used only if you agree |
| Several stores in one copy | shares the engine's fixed cost | perhaps 1.5–2× | **a big build:** the code assumes one store | C6.10 (Phase 6): the design and its cost only, nothing built in 0.5.2 |

**In 0.5.2:** the first six are built or set; the shopper cap is built but stays off until you
say; several stores in one copy is only designed.

---

## 4. One scene for sound and models?

**Yes, two people can share it,** if each edits a different file. The options:

| Option | How | Pros | Cons |
|---|---|---|---|
| **M1. A base plus a layer each (my pick)** | `Store_Art` for the artist, `Store_Sound` for the sound designer, both opened over the base | nobody edits the same file; each sees the other's latest after a pull; no scene merges | no links across scenes: a sound can't point at the artist's lamp, so it finds it by name at load, or is a C# "sound spot" as the sound plan already says |
| M2. Prefabs per person | the artist's room prefabs and the sound designer's sound prefabs, placed in one shared scene | small files, a lock per prefab | the shared scene is still one file someone edits; prefab overrides get complicated |
| M3. One scene, taking turns | a lock on the scene while one person edits | simplest | one at a time; a forgotten lock blocks the other |
| M4. One scene, edited at once | Unity's merge tool combines the two edits | no waiting | scene merges often fail or lose changes in practice; risky on a 22.6 MB scene |
| M5. Keep them apart | the sound plan's separate sound project, plus a models scene | as already planned | the art reaches the sound designer late; two copies of the store |

**Where the shared layers live:** the sound plan has the sound designer in a separate, stripped
project, so he never needs Karen or the game's code. Two ways to keep that with M1:
- the stripped project becomes a **content project** for both him and the artist, and the layers
  are copied back into the game, as the sound plan's merge step already does for sounds; or
- the layers live in the **game's private repo**, and they reach the Testing build with no copy.
  This changes the sound plan's §4, so it's your call (Q17).

**My lean:** the content project until version control exists (Phase 7), then the game's repo.

**Your answer (Q15, 21 November): "go with the pick for one scene 2 people":** M1, one base
with a layer each. Moving the layers into the game's repo changes the sound plan's §4 when it happens.

---

## 5. Version control for three people

**The constraint that decides it:** one `Assets` folder holds both the code (public) and the art
(private). Whatever holds the art must never let it into the public repo, and must never need a
second copy of the code that can drift. **The people:** you, the sound designer and a future
artist, plus the agents (code only) and the PC (pulls only).

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. Git LFS in private repos, with locks (my pick)** | a second git folder (`.git-assets`) beside the code repo's `.git`, in the same project, for a new private `kehai-project`: art, scenes, layers, prefabs, `Packages/`, `ProjectSettings/`. `kehai-assets` stays the sound designer's, as the sound plan lays it out. Big files go to LFS; scenes stay text, merged with `UnityYAMLMerge` and locked while edited | free at today's size; history for every art file (the `git stash -u` that once wiped 579 art files would have been recoverable); locks; the git and GitHub the agents and the PC already use; GitHub Desktop gives the sound designer and the artist buttons instead of commands | two repos in one folder must be kept apart by a script and two checks |
| B. Unity Version Control for the content | scenes, art and sounds in Unity's own system, inside the editor; **Gluon**, its simple mode, shows files with locks and no branches; **shelves** share unfinished work for a look; code stays in Git | built for Unity and for non-programmers; no seat charge since March 2026 ([Unity](https://support.unity.com/hc/en-us/articles/34748492914964-Understanding-Unity-DevOps-charges)) | a second system beside Git; access stops the moment a free limit is crossed; agents know it less well |
| C. One private repo for everything | `kehai-assets` also holds the game's files; each computer checks out only its part | one private repo | the sound designer gets write access to the game's scenes and settings too; the sound plan's layout changes |
| D. `kehai-assets` as the whole project, the public repo a code-only copy | a script publishes the code | one repo for helpers | the copy step can leak private files if it breaks; two histories for the code |
| E. Perforce (P4) | the big studios' system, on a server you run (the PC could) | strong locks; huge art | a server to run; far more than three people need |
| F. Zips, or a shared cloud folder | as today, or iCloud/Dropbox | nothing new | no history or merge; a project open on two computers gets corrupted |

**Your answer (Q17, 21 November): "go with the pick":** A.

Unity's merge tool works with A–E. Either way `kehai-assets` uses LFS, so **Q17 also answers
the sound plan's Q4.**

**How A works, as an example:**
- `~/Developer/Kehai/.git`: the public code repo, exactly as today.
- `~/Developer/Kehai/.git-assets`: the private `kehai-project` repo, same folder. A wrapper
  (`tools/assets/kgit`) runs git with it, so no agent mixes the two.
- **Two checks keep them apart:** no file tracked by both, and the code repo refuses art file
  types before a commit (a guard for the public repo).
- **Scenes and layers** are text in normal git, with `UnityYAMLMerge` as their merge tool and
  marked lockable: lock before you edit, unlock after. (Check during the build that LFS locks
  work on files that aren't stored in LFS.)
- **On Windows:** the repos cloned into one folder; `kehai-pull` updates them.

| Who | Works on | Needs, with A |
|---|---|---|
| You | the Training scene mostly, Karen's code, your dev layer; the base by hand | a lock on the base while you edit it |
| The sound designer | the sound layer or C# sound spots, audio files | `kehai-assets` (his own repo) or the content project; GitHub Desktop; LFS for audio |
| A future artist | the art layer: models, materials, textures, room dressing | `kehai-project` write access; LFS; locks; GitHub Desktop |
| Agents (Claude, Codex) | code; scene changes only through builder menus, run on a clone | the code repo only; never art files; the two checks |
| The PC | pulls only | read access and `kehai-pull` |

---

## 6. Checking each other's work

**Your question:** how a team of three pushes and pulls scenes so each can check the others' work.

| Option | How | Pros | Cons |
|---|---|---|---|
| R1. A branch per person, merged by pull request | each pushes their branch; whoever reviews pulls it and opens Unity | the usual way; a record of who changed what, and why | to see a change you pull it and open the project (minutes and disk) |
| R2. Layers on one branch, no review step | each pushes their own layer straight in | no merges at all | nobody looks before it's in |
| R3. Unity Version Control's shelves | unfinished work is shelved; a reviewer opens the shelf to look | built for it, inside the editor | needs option B |
| R4. An automatic review snapshot (adds to any of them) | every push triggers a batch run that opens the scene and posts pictures from fixed camera spots (the picture-taking check, `-kehai-shots`, exists) to the pull request; for sound, a list of the sound spots that changed | nobody pulls to look; works from a phone | Unity time per push (on the PC once it's ready, else the Mac with your editor closed); sound can't be judged from pictures, and a clip with sound from batch mode is unchecked |

**My pick:** R1 with R4, on the layers of [§4](#4-one-scene-for-sound-and-models) so branches
rarely clash.

**Example:** the artist pushes `art/staff-room-lights`. Ten minutes later the pull request shows
six pictures of the staff room from the same spots, before and after. You comment "darker by the
lockers"; they fix it and push; you merge. The sound designer's branch for the same room merges
the same day, with no conflict, because it's a different layer.

---

## 7. Art and data to Windows

| What | Size now | How (my pick) | Other options |
|---|---|---|---|
| Code | — | git, as today | — |
| Project files: art, scenes, `Packages/`, `ProjectSettings/` | 88.6 MB zipped, 4,413 files (the 11 November release) | **option A of §5:** `kehai-pull` on the PC | a script that makes the zip and the release (no hand-made zips, but no history) |
| Game data: the Ledger, shift records, datasets, results | 508 MB zipped (11 November); sizes now in [storage](../0.5.0/storage.md) | **copied directly** over the private network of [testing §6](testing.md#6-running-on-the-windows-pc-from-this-mac) (`rsync` or `scp` over SSH); a scripted zip until the link exists | in LFS (uses the free download allowance fast) |
| Training results back to the Mac | — | the same direct copy, and the run register's `import` (exists) | — |

**Your answer (Q18, 21 November): my suggestion:** a direct copy once the link exists, and a zip
made by a script until then (C7.7). The link to the PC is still Q8, and the PC is on hold (Q19).

---

## 8. Disk and cost

The project's measured sizes and the budgets for later are on the [storage page](../0.5.0/storage.md);
the free disk on 17 November is on the [testing page](testing.md#11-how-this-was-measured).

| Option | Money (checked 17 November 2026) | Disk on this Mac | Limits |
|---|---|---|---|
| **A. Git LFS** (one account's allowance for both private repos) | free up to 10 GiB stored and 10 GiB downloaded a month on GitHub Free; then $0.07 per GiB a month stored and $0.0875 per GiB downloaded ([GitHub's docs](https://docs.github.com/en/billing/concepts/product-billing/git-lfs)) | a second copy of each LFS file inside `.git-assets` (about 0.2 GB now; trimmed with `git lfs prune`) | uploads are free; each fresh clone on the PC downloads all the art |
| B. Unity Version Control | free up to 25 GB stored and 100 GB downloaded a month per organisation; no seat charge since March 2026 ([Unity](https://support.unity.com/hc/en-us/articles/34748492914964-Understanding-Unity-DevOps-charges)) | about one copy of the art (a central server, no full history on the Mac) | blocked at once when a limit is crossed |
| E. Perforce (P4) | free for 5 users and 20 workspaces, on your own server ([Perforce](https://www.perforce.com/resources/vcs/helix-core-pricing)) | about one copy; the server's on the PC | the PC must be on whenever anyone works |
| F. Zips | free | each zip while it exists (about 0.6 GB a release now) | 2 GiB per release file |
| GitHub Desktop, Unity's merge tool | free | small | — |
| The Training scene and the layers | free | a few MB to tens of MB of text | — |

**The disk point that matters:** with A, art takes about twice its size on disk (the working
file and LFS's copy). Today that's 0.2 GB. If the art grows to the storage page's budgets, it's
another reason for the external SSD that page recommends.
