# Versions and releases

Part of the [Kehai 0.5.2 plan](../0.5.2-plan.md), item 3, added on 21 November ("add the
version-control design, yes"). It says how the game, its scenes and art, and each Karen are
numbered, so that any version can be rebuilt and every record says what made it. The tasks are
the plan's [Phase 10](../0.5.2-plan.md#phase-10-versions-and-releases-c--sonnet-55-opus-55-for-c103--this-mac).
The Karens' names are Q21 ([the options](../0.5.2-plan.md#q21-names-for-the-two-karens)).

1. [What exists today](#1-what-exists-today)
2. [The game's version](#2-the-games-version)
3. [Rebuilding any version](#3-rebuilding-any-version)
4. [Each Karen's version](#4-each-karens-version)
5. [Dev, testing and prod](#5-dev-testing-and-prod)
6. [The stamp on every build and record](#6-the-stamp-on-every-build-and-record)

---

## 1. What exists today

- **The rule** is in [RELEASE_PLAN.md](../../RELEASE_PLAN.md#versions): `MAJOR.MINOR.PATCH`, `1.0.0`
  at launch, a minor step for a milestone, a patch for fixes, and ranges for alpha and beta.
- **The number itself** sits in the project settings: 0.1.0 in the main folder, 0.5.0 in the
  0.5.0 copy ([testing §1](testing.md#1-what-gets-in-the-way-today)). The main menu shows it.
- **The editions add a label:** `0.5.0-training`, `0.5.0-testing`, and plain `0.5.0` for Gameplay
  (`EditionRules.VersionLabel`).
- **A build's `build.txt`** records the code commit, whether there were uncommitted changes, and
  the edition. It records nothing about the art.
- **Shift records carry no version or commit.** Learning records carry the brain's version.
- **A Karen's model pack** has a whole-number version (1, 2, 3, one per publish) and an inputs
  version (2 today). The game refuses a pack made for other inputs or naming a trick it doesn't
  know (`tools/learn/pack.py`, `BrainSeam.Accepts`), and the publish gate checks evaluation,
  fairness, compatibility and the signature.
- **Nothing is tagged,** so an old version's scenes and art can't be found again.

---

## 2. The game's version

| Part | Changes when | Example |
|---|---|---|
| `0.MINOR` | new features | 0.6.0 adds the interactions |
| `PATCH` | fixes only | 0.6.1 fixes a door |
| a pre-release label | a build for playtesters before the version is final | 0.6.0-beta.1, then 0.6.0-beta.2 |
| `1.0.0` | the Steam release | — |

- **One change to the rule today:** a minor step now means "new features", not "a milestone",
  and playtest builds get a `-beta.N` label. C10.1 updates RELEASE_PLAN's section, so the rule
  stays in one place.
- **This plan keeps the name 0.5.2.** Under the new rule a version that adds features would be
  0.6.0; the rule starts with the next version, unless you'd rather rename this one.

---

## 3. Rebuilding any version

- **A release tags every repo with the same version:** `v0.6.0` on the public code repo, on the
  game's private repo (`kehai-project`, if Q17 picks option A) and on `kehai-assets`. The Unity
  version is in the project settings, so it's tagged too.
- **One command rebuilds a version** into a scratch folder: it checks out the tag in every repo
  and builds. Your working project isn't touched.

**Example:** in March a tester reports a bug in 0.6.0-beta.2. The command checks out
`v0.6.0-beta.2` in all three repos, builds it, and you can play exactly what the tester played.

---

## 4. Each Karen's version

Each trained Karen (Basic and Vital) gets her own number, apart from the game's:

| Part | Changes when | Example |
|---|---|---|
| `MAJOR` | her inputs or her tricks change, so she needs new game code | basic 2.0.0 after a new sense joins her inputs |
| `MINOR` | trained again, on the same inputs | basic 1.3.0 after a refresh round |
| `PATCH` | exported again, nothing new learned | basic 1.2.1 |

- **It maps onto what exists:** MAJOR is the pack's inputs version plus its list of tricks;
  MINOR is today's publish number.
- **A game version accepts any Karen with the same MAJOR.** A newer MINOR reaches players
  without a new build of the game, and **the publish gate still runs** on every one (with no
  per-model yes from you, your call of 12 November).
- **Example:** game 0.6.0 accepts basic 1.x and vital 1.x. Basic 1.3.0, trained in January,
  passes the gate and reaches testers without a new build. Basic 2.0.0, with a new input, needs
  game 0.7.0.
- **Names** come on top: "Karen Nocturne 1.3" would be `basic 1.3.0` (Q21). The code keeps `basic`
  and `vital`.

---

## 5. Dev, testing and prod

| | Dev | Testing | Prod |
|---|---|---|---|
| **Scenes** | Training and the dev layers; the sound and models scenes | the Testing scene | the Testing scene, built as Gameplay |
| **Edition** | Training, and the editor | Testing | Gameplay |
| **Version** | any commit: `0.6.0-dev+a1b2c3d` | `0.6.0-beta.N` | `0.6.0`, later `1.0.0` |
| **Which Karen** | the one being trained or benchmarked | the newest that passed the gate | the newest that passed the gate for release |
| **Who gets it** | you, the sound designer, the artist, the agents, the PC | playtesters | players |

**Later, on Steam:** Steam doesn't read version numbers; it has branches. The default branch
would carry prod, and a password-protected beta branch would carry testing.

---

## 6. The stamp on every build and record

Every build carries four things: **the game's version** (with its label), **the code commit**,
**the art commit**, and **each Karen's name and version**.

| Where | Today | After Phase 10 |
|---|---|---|
| `build.txt` | code commit, edition | all four |
| The Settings screen | the version on the main menu | one line with all four |
| Every shift record and replay | nothing | all four |
| A playtest upload | the version | all four |

**Example line:** `Kehai 0.6.0-beta.2 · code a1b2c3d · art 9f8e7d6 · Basic 1.3.0 · Vital 1.1.0`.
A bug report, a tester's session or a training record then says exactly which game and which
Karen made it, which is what the recorded test pass after the merge (Q1) needs.
