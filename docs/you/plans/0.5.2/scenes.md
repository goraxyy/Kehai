# Scenes and version control

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 3: "Build 3 scenes for testing, training
and dev (maybe add more for multiple dev, and add version control)." The questions (Q numbers)
are in the plan's [open questions](../0.5.2-plan.md#11-open-questions). Prices were checked on
17 November 2026: **check them again before paying for anything.**

1. [What exists today](#1-what-exists-today)
2. [What "testing, training and dev" could mean](#2-what-testing-training-and-dev-could-mean)
3. [Options for the scenes](#3-options-for-the-scenes)
4. [Version control for scenes and art](#4-version-control-for-scenes-and-art)
5. [Art and data to Windows](#5-art-and-data-to-windows)
6. [Several developers](#6-several-developers)
7. [Disk and cost](#7-disk-and-cost)

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
- **Six layouts are C# data** built into the same scene at load: no scene per layout.
- **Much of the scene is built by menus from code** ([HANDOVER §3](../../../agents/HANDOVER.md#3-how-the-scene-is-put-together)),
  so it can be rebuilt.
- **Art sits beside code in `Assets/`.** Only code goes public; the art is kept out by a local
  exclude list (126 lines). The private `kehai-assets` repo has one zip release (11 November)
  and no history.
- **Two separate projects are already decided:** the sound designer's stripped sound project in
  `kehai-assets/SoundProject` (the [sound plan](https://github.com/goraxyy/Kehai/pull/86), which
  assumes Git LFS, its Q4), and the Look Lab (0.5.0 item 8, option B).
- **Unity's scene merge tool is installed:** `UnityYAMLMerge`, inside the editor at
  `Unity.app/Contents/Helpers/`.

---

## 2. What "testing, training and dev" could mean

| Word | Could mean | My reading |
|---|---|---|
| Training | the Training edition: bots and the trainer, no UI, no art | the same |
| Testing | **(a)** the playtesters' edition, or **(b)** our automated tests and checks | ask (Q14) |
| Dev | a scene to work in that never ships | ask (Q14) |

The players' Gameplay edition isn't in your list. Below, it loads the same scene as Testing.

---

## 3. Options for the scenes

| Option | How | Pros | Cons | Disk (*estimate*) |
|---|---|---|---|---|
| A. One scene, editions in code (today) | as now | nothing to build; one source | editor runs carry the full scene; developers share one file | — |
| B. Three hand-kept copies | `Store_Training`, `Store_Testing`, `Store_Dev` | each is simple to open | they drift; every builder change runs three times | about 70 MB of text, more as art grows |
| C. One base scene plus layers, loaded together | `Store_Core` (walls, floor, NavMesh, the lights she senses, everything that plays), `Store_Dressing` (looks only), `Store_Debug`; each edition loads a set | Training loads only the core; two people can edit two layers | Unity can't link objects across scenes, so code must find them at run time; splitting a 22.6 MB scene is a big, risky job; lighting and the NavMesh bake across scenes | about the same as today |
| **D. A generated Training scene (my pick)** | a menu writes `Store_Training.unity` from the store scene using the stripping code the Training build already has; never edited by hand, never versioned, made again after scene changes (a check flags a stale one) | cheap: the code exists; editor tests, ablations and training could load the light scene too; can't drift | one more step after scene edits; doesn't help two people edit the store | a few MB to 22 MB, regenerated |
| **E. Dev scenes, one per developer (my pick, with D)** | `Dev_<name>.unity` loads **on top of** the store scene and holds only that person's experiments; never in a build | nobody edits the store scene to try something; easy to throw away | anything that should ship must move into the store scene or a C# builder | small |
| F. A scene or prefab per area | each room (sales floor, cold room, staff room, office, WC, backstreet) its own scene or prefab | an artist on the staff room and you on the sales floor never touch the same file | the same cross-scene limits as C; prefab overrides get complicated | about the same as today |
| G. A separate project per edition | a folder each | fully apart | disk (each project has its own cache, [storage](../0.5.0/storage.md)); drift | gigabytes each |

**My pick:** D plus E now. F only when an artist actually starts (Q16), and only for the rooms
they work on. C and G cost more than they give for one developer.

**What D would give, as an example:**

| Scene | Made how | Used by |
|---|---|---|
| the store scene (today's) | by hand and by the builder menus; the one source | Testing, Gameplay, the editor |
| `Store_Training.unity` | generated by a menu or a command | the Training player, and editor runs that want speed (if C8.3 measures any) |
| `Dev_<name>.unity` | each developer's own | experiments only |

**Automated tests need no scene file:** they make their scenes in code (`EditorSceneManager.NewScene`),
so if "testing" means our tests, D and E already cover it.

---

## 4. Version control for scenes and art

**The constraint that decides it:** one `Assets` folder holds both the code (public) and the
art (private). Whatever holds the art must never let it into the public repo, and must never
need a second copy of the code that can drift.

| Option | How | Pros | Cons |
|---|---|---|---|
| A. Zips by hand (today) | zip, upload a release, download, unzip | free; nothing new | no history, no merge; Windows lags (its copy predates 0.5.0's scene changes) |
| **B. Git LFS in `kehai-assets`, over the same folder (my pick)** | a second git folder (`.git-assets`) beside the code repo's `.git`, in the same project. It tracks exactly what the code repo leaves out: art, scenes, prefabs, `Packages/`, `ProjectSettings/`. Big files (models, textures, audio) go to LFS; scenes and prefabs stay text, merged with `UnityYAMLMerge` and **locked** while someone edits them | free at today's size; history for every art file (the `git stash -u` that once wiped 579 art files would have been recoverable); file locks; the same git and GitHub the agents and the Windows PC already use; the sound plan already expects it | two repos in one folder must be kept apart by a script and a check; scene merges are still hard (locks avoid most) |
| C. `kehai-assets` as the whole project, the public repo a code-only copy | the private repo holds everything; a script publishes the code | one repo for helpers | the copy step can leak private files if it breaks; two histories for the code |
| D. Unity Version Control | Unity's own system (formerly Plastic SCM), inside the editor, with locks and a simple mode for artists | built for Unity; no seat charge since March 2026 ([Unity](https://support.unity.com/hc/en-us/articles/34748492914964-Understanding-Unity-DevOps-charges)) | a second system beside Git for the code; access stops the moment a free limit is crossed; agents know it less well |
| E. Perforce (P4) | the big studios' system, on a server you run (the PC could) | strong locks; handles huge art | you run a server; a lot to learn; far more than one developer needs |
| F. Unity's scene merge tool | `UnityYAMLMerge` (installed) merges two edits of a scene or prefab | free; works with B, C and D | it's a merge tool, not version control |
| G. A shared cloud folder | iCloud, Dropbox or OneDrive syncs the project | no set-up | no history or merge; a project open on two computers gets corrupted |

**My pick:** B with F, and locks on scenes and prefabs. Look at D again if an artist joins who
wants an in-editor tool, or if the art passes LFS's free storage.

**How B works, as an example:**
- `~/Developer/Kehai/.git`: the public code repo, exactly as today.
- `~/Developer/Kehai/.git-assets`: the private `kehai-assets` repo, same folder. A wrapper
  (`tools/assets/kgit`) runs git with it, so no agent mixes the two.
- **Two checks keep them apart:** no file tracked by both, and the code repo refuses art file
  types before a commit (a guard for the public repo).
- **Scenes and prefabs** are text in normal git, with `UnityYAMLMerge` as their merge tool and
  marked lockable: `kgit lfs lock Assets/…/SampleScene.unity` before you edit, unlock after.
  (Check during the build that LFS locks work on files that aren't stored in LFS.)
- **The sound designer** works in `kehai-assets/SoundProject/`, his own folder, as his plan says.
- **On Windows:** both repos cloned into one folder; `kehai-pull` updates both.

---

## 5. Art and data to Windows

| What | Size now | How (my pick) | Other options |
|---|---|---|---|
| Code | — | git, as today | — |
| Project files: art, scenes, `Packages/`, `ProjectSettings/` | 88.6 MB zipped, 4,413 files (the 11 November release) | **B above:** `kehai-pull` on the PC | A script that makes the zip and the release (no hand-made zips, but still no history) |
| Game data: the Ledger, shift records, datasets, results | 508 MB zipped (11 November); sizes now in [storage](../0.5.0/storage.md) | **copied directly** over the private network of [the testing page §6](testing.md#6-running-on-the-windows-pc-from-this-mac) (`rsync` or `scp` over SSH) | in LFS (uses the free download allowance fast); a scripted release zip |
| Training results back to the Mac | — | the same direct copy, and the run register's `import` (exists) | — |

**Why data stays out of LFS (Q18):** every download counts against the free 10 GiB a month, and
datasets change often; a direct copy costs nothing and keeps only the latest.

---

## 6. Several developers

| Who | Works on | Needs |
|---|---|---|
| You | everything, the scene by hand | a lock on the scene while you edit it; your own dev scene |
| Agents (Claude, Codex) | code; scene changes only through builder menus, run on a clone | the code repo only; never art files; the two checks in §4 |
| The sound designer | the separate sound project; audio files; the C# sound tables | his folder in `kehai-assets` with LFS for audio; never the game scene: his placements come back as C# "sound spots" ([sound plan](https://github.com/goraxyy/Kehai/pull/86), its §5) |
| A future artist | models, materials, textures, prefabs, maybe dressing the rooms | LFS for big files; locks; per-area prefabs or scenes (option F) for the rooms they work on |
| The Windows PC | pulls only | read access and `kehai-pull` |

---

## 7. Disk and cost

Measured on this Mac on 17 November: `Assets` 163 MB, `Library` 4.2 GB, about 18 GiB free.
The budgets for later are on the [storage page](../0.5.0/storage.md).

| Option | Money (checked 17 November 2026) | Disk on this Mac | Limits |
|---|---|---|---|
| A. Zips | free | each zip while it exists (about 0.6 GB a release now) | 2 GiB per release file |
| **B. Git LFS** | free up to 10 GiB stored and 10 GiB downloaded a month on GitHub Free; then $0.07 per GiB a month stored and $0.0875 per GiB downloaded ([GitHub's docs](https://docs.github.com/en/billing/concepts/product-billing/git-lfs)) | a second copy of each LFS file inside `.git-assets` (about 0.2 GB now; at the storage page's later budgets it could be tens of GB, trimmed with `git lfs prune`) | uploads are free; each fresh Windows clone downloads all the art |
| D. Unity Version Control | free up to 25 GB stored and 100 GB downloaded a month per organisation; no seat charge since March 2026 ([Unity](https://support.unity.com/hc/en-us/articles/34748492914964-Understanding-Unity-DevOps-charges)) | about one copy of the art (a central server, no full history on the Mac) | blocked at once when a limit is crossed |
| E. Perforce (P4) | free for 5 users and 20 workspaces, on your own server ([Perforce](https://www.perforce.com/resources/vcs/helix-core-pricing)) | about one copy on the Mac; the server's on the PC | the PC must be on whenever anyone works |
| F. Scene merge tool | free, installed | none | — |
| Scene option D: the generated Training scene | free | a few MB to 22 MB | — |
| Scene option E: dev scenes | free | small | — |

**The disk point that matters:** with B, art takes about twice its size on disk (the working
file and LFS's copy). Today that's 0.2 GB. If the art grows to the storage page's budgets, it's
another reason for the external SSD that page recommends.
