# Whiteboard notes, 5 November 2026

Two whiteboards, written out as text. The first compares how two horror games hunt the player;
the second sets Kehai's three pillars. Words are copied as written, with spelling fixed. Arrows
are drawn as `→`. Anything I couldn't read is marked *(unclear)*.

The section at the end, **What this means for Kehai**, isn't on the boards: it adds where the code
stands today, with sources.

---

## Board 1: how other games hunt you

### Alien: Isolation — "driven by behavioral trees & systems"

**Side notes** (top right, each marked with a double bar):
- 30 top decision layers, followed by sub-behaviors.
- 100+ nodes.
- Trees and nodes unlock with game progression.
- A "tail", to stop players just walking behind it.

**The diagram: two AIs.**

```
   Director  ──── no cheating ──X──▶  Alien
  (Macro AI)                        (Micro AI)
```

The arrow from the Director to the Alien is crossed out, labelled "no cheating".

| | Director (macro AI) | Alien (micro AI) |
|---|---|---|
| What it knows | Knows where you are | Sense-driven reactions |
| What it tracks | The "Menace Gauge" | — |

**Note pointing at the Menace Gauge:** being in proximity increases the index: hiding, the
tracker, etc. (it poses a threat).

**The Alien's states:**

| Active | Passive |
|---|---|
| Search the player's area | Vents |
| Search a noise | Directed to another area |

**Fairness:** it teleported only 2 times, both for cutscenes.

**Two more systems** (circled, left side):

| Job system | Search system |
|---|---|
| Tasks | Sub-optimal exploration of all nav points |
| Location of … | Searching locations randomly |
| Priority of … | ↳ backtracking |

### Hello Neighbor — "4 modes & key spots"

**The four modes, left to right:**

```
Idle ──stimuli──▶ Hunt ──sight──▶ Attack            Offline
                   ▲
                   ┊ (dotted arrow up into the stimuli arrow)
```

| Mode | What it is | Notes on the board |
|---|---|---|
| **Idle** | Ordinary life | Sleeping, TV, cleaning, etc. |
| **Hunt** | Searching | Triggered by changes to the scene: moved items, sounds, etc. He naps less (imagine sleeping when someone is in your house). |
| **Attack** | Chasing | Predicts your path → (arrow to the traps). Throws items to weaken you: blur, slow, stun. |
| **Offline** | Traps | Blocks doors; a bucket over a door, etc.; only in a few places; a camera with a noise. |

**Key spots:**
1. **Objects:** how often each is used (a closet, under the bed, etc.).
   - Using one often → its probability goes up → he starts checking it often.
   - A door you use → a bucket on it.
   - A path you use → traps or a camera.
2. **Routes:** how often you went there.

**Hello Neighbor 2:** a neural network?? Probability < reasoning (implication). Global training.

*(A small doodle in the bottom-left corner, not text.)*

---

## Board 2: Kehai pillars

**1. Wave dramaturgy**
- → No constant chasing → fixes Hello Neighbor.
- → No dumb screamers → fixes FNAF.
- → Efficiency → unexpectedness → smart?
- ⋯→ Idea: scare with customers when Karen is far?

**2. Fairness**
- → The Director can't give your location.
- → Winnable:
  - → no endless shifts;
  - → no repeating or annoying exploits.
- → No camping → scare / interest (explore) > catch.

**3. Two-way adaptation**
- → The player adapts to Karen → make it obvious → loop.

---

## What this means for Kehai (added; not on the boards)

| Board says | Kehai today | Source |
|---|---|---|
| Director and Alien, no cheating | Same split: the Director paces, her body only knows what it senses. A test fails if her code reads your position. | `KarenDirector.cs` header; `KarenTests.BodyBeliefAndDecisions_NeverReadThePlayer` |
| "Director can't give location" (pillar 2) | **Not quite true today.** After 45 s with no clue, the Director nudges her belief toward your real region, at most 0.15 of it a minute and never past half. Alien: Isolation does the same: its Director sends the Alien toward your general area. | `KarenDirector.MaybeBias`; [Game Developer, "Revisiting the AI of Alien: Isolation"](https://www.gamedeveloper.com/design/revisiting-the-ai-of-alien-isolation) |
| Menace Gauge | The Panic Index, steered toward a target (a setpoint) rather than a maximum. Alien's gauge calls the Alien away when it peaks; Karen's Withdraw does the same job. | `KarenDirector.cs`, `PanicIndex`; [Game Developer, "The Perfect Organism"](https://www.gamedeveloper.com/design/the-perfect-organism-the-ai-of-alien-isolation) |
| Trees unlock with progression | Tricks unlock by shift (the career table, shifts 1–10). | `Tactic.IntroducedShift` |
| Search all nav points, randomly, with backtracking | Patrol always takes the best-scoring region. Proposed in [IDEAS.md 6.1](../project/IDEAS.md#61-search-every-spot-on-purpose-not-perfectly). | `TacticsMovement.cs`, `PatrolTactic` |
| Key spots and routes warm up | Routes and hiding regions are in the Ledger; objects and doors aren't. Proposed in [IDEAS.md 6.3](../project/IDEAS.md#63-key-spots-that-warm-up-from-hello-neighbor-tied-to-the-ledger). | `KarenLedger.cs` |
| Traps: blocked doors, cameras | Door lock, crate wall, camera bolt-on exist. | `TacticsBody.cs`, `TacticsFacility.cs` |
| Hello Neighbor learns your habits | The developers describe it as reading sounds and changes you make to the house, then laying traps. | [Engadget, 2016](https://www.engadget.com/2016/09/30/hello-neighbor-pc-game/); [Game Developer interview](https://gamedeveloper.com/design/designing-a-domestic-hunter-killer-thriller-the-i-hello-neighbor-i-way) |
| Two-way adaptation, "make it obvious" | Her thought log, the F1 map and the shift report show why she did things, but nothing in play tells you she has learnt a habit. | `ThoughtLog`, IDEAS.md §5 |
