# Three versions of the game

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 7. **Choices settled:** one gameplay project produces three editions. A separate external
visual project is additional, not a fourth game edition. See [storage budgets and opening from an external drive](storage.md).

1. [The memory question](#1-the-memory-question)
2. [What goes in each version](#2-what-goes-in-each-version)
3. [Version control](#3-version-control)
4. [How a build is made](#4-how-a-build-is-made)

---

## 1. The memory question

See [the storage assessment](storage.md) for measured current sizes, future budgets, the linked
128 GB drive's USB 2.0 limitation and direct-opening steps. Three editions share one gameplay
project; the selected external visual project is an additional source/cache copy. No migration
or project creation occurs until explicitly authorised.

## 2. What goes in each version

Your three, as you confirmed them (Q26):

| | Training | Testing | Gameplay |
|---|---|---|---|
| **Who uses it** | the Windows PC: bots and the trainer, about 8–12 copies at once | playtesters, through PR #81's build | players: the Steam demo, then the game |
| **Guidance** | none | the guided first shift (item 6) | the guided first shift |
| **Plot** | none | none | yes, once written (Q27) |
| **Art** | only what the bots and Karen need: collision, the NavMesh, the lights she sees by; no textures, no sound | the quick pass | the redesign (Milestone 7) |
| **Interactions** | the ones the bots use | all | all |
| **UI** | none (no text components: they're what breaks today's training player) | the game's, plus the bug key and consent | the game's |
| **Recording** | learning records and shift records; no replays | everything, with consent (PR #81) | crash reports, and play data only with consent |
| **Debug tools** (F1, F2, the trick check) | the trick check, for tests | F1 and F2, the bug report | none (or behind a setting) |
| **Karen** | the version being trained or tested | the version that shipped last; **there from shift 1** (Q39) | the version that shipped last; **not in the first shift** (Q23) |

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
