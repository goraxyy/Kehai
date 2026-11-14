# Karen in a new maze

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 4. The layouts themselves are on
[the maze layouts page](maze-layouts.md).

1. [What each of her parts does on a new layout](#1-what-each-of-her-parts-does-on-a-new-layout)
2. [Places she can remember across layouts](#2-places-she-can-remember-across-layouts)
3. [Training across layouts](#3-training-across-layouts)
4. [A new layout every shift](#4-a-new-layout-every-shift)

---

## 1. What each of her parts does on a new layout

Most of her is built from the map at load, so it follows a new layout without any change.

| Her part | On a new layout | Work needed |
|---|---|---|
| **The map** (1.5 m cells, rooms, 5 m regions, doorways, chokepoints; `StoreMap.cs`) | rebuilt from the NavMesh at every load | the shelves must be placed, and the NavMesh built, before the map is (C4.2) |
| **The belief map** (a chance for every cell) | built on the map | none |
| **Her senses** (sight, hearing, traces; S1's fitted numbers in 0.4.0) | numbers about distance and walls, not places | none; checked on held-out layouts (C4.10) |
| **The predict step** (P1 in 0.4.0: the hand-written step with fitted gains) | spreads along the map's links | none; its gains are tested on held-out layouts |
| **Route guesses, Cut, Ambush** | read the map's shortest ways | none |
| **Trick placement** (where to Spill, where to Block a door) | candidate places come from the map | none; the trick check runs per layout (C4.6) |
| **Section names** ("Aisle 7/B") | come from the section rectangles | a section table per layout (C4.3) |
| **The Ledger's places** (where you dwell, hide, the routes you take, placement memory, the new escape habits) | keyed by region names, which mean other places on another layout | places keyed by layout, plus a "kind of place" that carries over (§2; C4.4) |
| **The trained brains' inputs** (0.4.0's `BrainObservation`: about 200 numbers; her belief as mass per room, not per aisle, and her three best guesses; D7) | layout-free: the rooms don't change, and the whole sales floor is one room | a test that no input names an aisle or region; training across layouts (§3; C4.9) |
| **The starting Ledger** ("everyone's" habits: where people hide) | keyed by place | per kind of place, so it carries to a new layout |

**The one bug this exposes:** the map numbers doorways in whatever order Unity finds the doors,
so rebuilding the same store can renumber them, and a remembered doorway habit can point at the
wrong door (found by 0.4.0 on 13 November). Sorting the doors by position fixes it (C4.4).

## 2. Places she can remember across layouts

**Today** a place in the Ledger is a region name: `Aisle 7/B`, `door_3`.

**Proposed:** every place gets two keys.

| Key | Example | Used for |
|---|---|---|
| exact: layout + region | `fishbone:Aisle 7/B` | "you hide here" on the layout you're on |
| kind of place | `dead end`, `chokepoint`, `beside a chore box`, `near the clock`, `long view`, `low shelves` | carried to a layout she hasn't seen you on |

*Example.* On the Fishbone you always hide in a south pocket. On the Brick bond there are no
pockets like it, but there are dead ends: she starts by checking the dead ends nearest to where
she lost you. After two shifts on the Brick bond, the exact places take over.

The kinds are worked out from the map at load (a dead end is a region with one way in; a
chokepoint is one already), so they cost nothing to author.

## 3. Training across layouts

> **Domain randomisation.** *What:* training on many versions of a world so the learner learns
> the skill, not one map. *In Kehai:* each training career draws one of the four training
> layouts. *Elsewhere:* robot hands trained in simulations with random lighting, colours and
> weights, so they cope with a real kitchen.

- **Four layouts train, two are held out.** The held-out two are named in `EvalSplits` and no
  training or tuning run may draw them, the same rule as the cautious player (C4.9).
- **What's trained across them:** the bots' trained mind (they must play any store), the two
  Karens' brains, and the fits (S1's senses, P1's gains, the mirror map's numbers). The fits are
  cheap; the brains are the long runs.
- **The test** (C4.10) is 0.4.0's ladders, run on the two held-out layouts. If she's much worse
  there than on the training layouts, she memorised the four.
- **When:** once, after 0.5.0 (your answer to Q1), so it's done on the
  layouts from the start, with careers of 4 shifts like the game.
- **Today's store stays a layout of its own** (the seventh), so today's results still compare.

## 4. A new layout every shift

| | Kept across a change | Lost at a change |
|---|---|---|
| Her | her senses, her tricks' general strength on you (per trick and per kind of trick), your habits by kind of place, your sprint and crouch share, how fast you answer each chore | exact places: where you hide, your routes, where each trick worked, the escape table's exact entries |
| You | how she behaves, which tricks she likes, the building's rooms and doors | the aisles, the shortcuts, where the dead ends are |

So a change every shift resets most of what she learns about you before it pays, and you spend
each shift's first minutes lost.

**Your answer (Q16): the game has 4 shifts, and the layout changes at the 3rd and the 4th.**

| Shift | The store | Karen | What she knows of you |
|---|---|---|---|
| 1 | today's store | not there (Q23; in the Testing version she is, Q39) | your habits from shift 1: the store watches (Q40) |
| 2 | today's store | there | your exact places, from shift 1 (Q40) and shift 2 |
| 3 | a new layout | there | your habits by kind of place; exact places start again |
| 4 | another new layout | there | the same |

The kinds of place (§2) carry what she learned in shifts 1–2 into shifts 3 and 4, which is why
they matter so much in a 4-shift game. Shifts 1–2 are today's store; 3 and 4 are a fixed pair you
choose (Q36). Training careers do the same, with the pair drawn at random (C4.11).

For a hard mode or the endless-maze idea ([IDEAS.md §2](../../IDEAS.md#2-the-endless-maze)),
"every shift" is a fair setting to offer later.
