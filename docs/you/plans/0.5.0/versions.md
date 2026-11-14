# Three versions of the game

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 7. **Discuss first:** the tasks wait on
Q25–Q27.

1. [The memory question](#1-the-memory-question)
2. [What goes in each version](#2-what-goes-in-each-version)
3. [Version control](#3-version-control)
4. [How a build is made](#4-how-a-build-is-made)

---

## 1. The memory question

"I don't have a lot of memory, is that a problem?" It depends which memory (Q25). Measured on
this Mac on 13 November:

| | Size | Note |
|---|---|---|
| **Disk** | 228 GB, **13 GB free** (94% used) | macOS starts to struggle below about 10% free |
| Unity 6000.5.10f1 | 11 GB | one copy serves every project |
| The project's `Library/` (Unity's cache) | 4.2 GB | **one per project copy** |
| The project's `Assets/` | 161 MB | |
| Old builds (`Builds/`: macOS 158 MB, Windows 143 MB and its zip 56 MB, the training player 221 MB) | 578 MB | |
| Test and training data (`kehai_eval`) | 3.3 GB | 0.4.0's recordings live here |
| **RAM** | 8 GB | the editor in Play with the maze reached about 6 GB |

**What each way of making three versions needs:**

| | Extra disk | RAM | Verdict |
|---|---|---|---|
| **One project, three editions** | each build, 150–250 MB; only the ones you keep | one Unity at a time, as now | **fine** |
| Three projects | about 4–5 GB each for the two extra copies (their own `Library/` and assets): about 9 GB, leaving about 4 GB free | the temptation to open two at once, which 8 GB can't take | **not fine** |

**To give the Mac room** (Y7.1, your yes first): the old builds and the old test data (about
3.9 GB together) can move to the Windows PC or a `kehai-assets` release, and come back when
needed.

## 2. What goes in each version

My reading of your three (Q26):

| | Training | Testing | Gameplay |
|---|---|---|---|
| **Who uses it** | the Windows PC: bots and the trainer, many copies at once | playtesters, through PR #81's build | players: the Steam demo, then the game |
| **Guidance** | none | the guided first shift (item 6) | the guided first shift |
| **Plot** | none | none | yes, once written (Q27) |
| **Art** | only what the bots and Karen need: collision, the NavMesh, the lights she sees by; no textures, no sound | the quick pass | the redesign (Milestone 7) |
| **Interactions** | the ones the bots use | all | all |
| **UI** | none (no text components: they're what breaks today's training player) | the game's, plus the bug key and consent | the game's |
| **Recording** | learning records and shift records; no replays | everything, with consent (PR #81) | crash reports, and play data only with consent |
| **Debug tools** (F1, F2, the trick check) | the trick check, for tests | F1 and F2, the bug report | none (or behind a setting) |
| **Karen** | the version being trained or tested | the version that shipped last | the version that shipped last |

**The Training version also fixes a real gap.** 0.4.0's training player never reaches the
trainer: with no graphics device, its text components throw errors without end. A version with
no text UI at all is the straightest fix, and it's what lets the Windows PC run many copies at
once (C7.3).

## 3. Version control

| Option | How | Pros | Cons |
|---|---|---|---|
| **A. One project; the editions set in C# (my pick)** | a small table in code (`Edition.cs`) says which scenes, features and recording each edition has; the build command picks one | no extra disk; one fix reaches all three; "authored data in C#", as the rules ask | debug tools must be checked out of Gameplay (a test, C7.1) |
| B. Unity's build profiles | Unity 6's own way to keep a scene list and settings per build [1] | built in | the profiles are assets beside the project settings, which never go into git, so every computer would need them made by hand |
| C. Three branches | one git branch per edition | each is clean | they drift; every fix is merged three times; art isn't in git anyway |
| D. Three projects | a folder each | fully separate | the disk (§1), and the same drift |

**Version numbers:** one line of versions for all three, as the [release plan](../../RELEASE_PLAN.md#versions)
says, with the edition as a label: `0.5.0-training`, `0.5.0-testing`, `0.5.0`. Each build
already records its commit.

## 4. How a build is made

```bash
# planned (C7.2); not working today
Unity -batchmode -projectPath . -executeMethod KehaiBuild.Build -edition training -platform windows
```

- One commit gives all three; the edition is in the build's `build.txt`, with the commit.
- The Testing build is PR #81's playtest build, plus the guide (C7.4).
- Builds are made one at a time, under the heavy-work lock, never while your editor is open.

**Sources.** [1] Unity Manual (6.0), Build Profiles. <https://docs.unity3d.com/6/Documentation/Manual/build-profiles-reference.html>
