# The maze layouts

Part of the [Kehai 0.5.0 plan](../0.5.0-plan.md), item 4. How Karen copes with a new layout is on
[Karen in a new maze](maze-and-karen.md).

**Latest decision (14 November):** keep 1 Racetrack, 2 Labyrinth, 3 Fishbone, 5 Rings, 6 Split,
and 10 Brick bond. Park 4 Free-flow, 7 Plaza, 8 Switchback, 9 Four rooms. Keep their drawings as
history, not build tasks. Shelves must be drawn and placed at their measured thickness, not as
solid 2.5 m squares. See [the shelf remodel](shelf-remodel.md).

**Earlier review instructions (superseded):** read the logic in §1 and say whether it's right (Q14), then tick or
cross each layout in §3 (Q15). A crossed one, with a line on why, is as useful as a tick.

1. [The logic (check this first)](#1-the-logic-check-this-first)
2. [How to read the drawings](#2-how-to-read-the-drawings)
3. [The ten](#3-the-ten)
4. [How they were checked](#4-how-they-were-checked)

---

## 1. The logic (check this first)

This is what I read from the store, the shelf kit and the endless maze. Where I'm guessing, it
says so.

| # | Rule | Where it comes from |
|---|---|---|
| M1 | **Only the sales floor's shelving changes.** The walls, the doors (the lobby's exit at x 42.5 and entrance at x 67.5; the staff doors on the west wall; the storage and cold room doors on the south wall), the tills, the lobby and the back rooms stay where they are. | the building ([LEVEL_DESIGN.md §1](../../../agents/store/LEVEL_DESIGN.md#1-the-building-as-it-stands)) |
| M2 | **Shelving stands on a 5 m lattice.** A closed edge between two cells is a run of shelving (two bays); a pillar stands at every corner a run touches. Runs go east–west or north–south, never on a diagonal. | the endless maze (`MazeGenerator.cs`: 5 m cells, "a closed wall between two cells is a run of shelving", pillars at the corners) |
| M3 | **The pillar at a joint is picked by what meets there:** an end cap (E) at a free end, a straight piece (I) in a line, an L at a corner, a T where three meet, the cross (+) where four meet, and the "one-two" T and L where a one-sided run meets a two-sided one. **Guess:** the b, S and R pillars are special ends (b sells from its back, S and R sideways). | the shelf kit's pieces in the scene (`Shelf_Pillar_*`) and `MerchandisingTests` |
| M4 | **Two-sided runs stand in the open and sell to both aisles; one-sided runs stand only with their back to a wall** (the drinks along the east wall). A pillar sells only on the sides its price strips are on. | [STORE.md](../../../agents/store/STORE.md), "Which way a shelf faces" |
| M5 | **Tall shelves (2 m, three boards) block sight; short ones (1.25 m, two boards) don't** for a standing person. Since 0.4.1 she judges cover eye to eye, so crouching behind a short shelf hides you. Short shelves go by the doors (fruit and veg, bread) and where the view should stay open (health & beauty). | `ShelfPrefabBuilder.cs`; 0.4.1's sight |
| M6 | **Fridges and chillers stand with their backs to a wall, never in the open,** so dairy and frozen sit at the cold end, by the cold room's door. Layouts don't move them (Q18). Chest freezers are the one cold piece that may stand free, inside the frozen section. **Not checked chiller by chiller:** that they stand on the south wall comes from the sections (dairy and frozen are the south-most, by the cold room's door); I haven't read each chiller's position in the scene, nor whether any stand on the east wall. | the scene's chillers; [STORE.md](../../../agents/store/STORE.md), "Milk at the far wall" |
| M7 | **The sections keep the customer's order** (Q17): in at the north-east doors, round, out through the tills at the north-west. Fruit and veg by the doors, bread at the front, drinks on the east wall, milk at the far wall, frozen beside dairy, pet in a far corner, sweets at the tills. Each layout cuts its own section rectangles to fit. | `StoreLayout.cs` |
| M8 | **Every product needs a shelf, with heavy goods at floor level,** so a layout needs roughly today's amount of shelving: 161 bays in [STORE.md](../../../agents/store/STORE.md)'s 2.5 m plan. The ten here have 103–172 shelf squares: a rough comparison, since a bay in that plan and a square here aren't quite the same thing. | the merchandising tests |
| M9 | **Aisles are at least one bay wide,** so two people can pass and the NavMesh's agent fits. In the drawings that's 2.5 m; in the store, with the shelves' depth (about 1 m) taken off, 1.5–4 m. | today's store; the NavMesh |
| M10 | **Mostly loops.** A chase with no way round is frustrating rather than tense, and her herding needs loops: the endless maze knocks through 75% of its dead ends. A few pockets and chokepoints on purpose, in the layouts that are about them. | [DECISIONS.md §4](../../../agents/DECISIONS.md#4-the-endless-maze) |
| M11 | **The aprons stay open:** in front of every door, the tills' lanes, and the back aisle in front of the chillers. | the doors and the queue |
| M12 | **A shift can always be won:** from anywhere there's a way to the staff room's clock (fairness rule 6). | [Karen.md §9](../../../agents/karen/Karen.md#9-the-fairness-contract) |

## 2. How to read the drawings

```
 D  a door in the building's wall      #  tall shelving: a run or a pillar
 .  floor                              +  short shelving (1.25 m): tables and low runs
 $  a till                             |  one-sided shelving against a wall
 F  chillers on the south wall
```

- **North is up.** The lobby's doors are at the top: the exit on the left (by the tills), the
  entrance on the right. The staff doors are on the left (west) edge; the storage and cold room
  doors at the bottom.
- **One character is 2.5 m.** The drawing is 60 m across and 55 m deep.
- **The top seven rows are the same in all ten:** the tills, the fruit and bread tables, the
  walk in from the doors. The maze is the middle; the back aisle and the chillers are the bottom.
- **A shelf fills a whole 2.5 m square in the drawing.** In the store a bay is about 1 m deep, so
  the aisles are wider than they look.
- Today's store is drawn at the same scale (not in these symbols) in [STORE.md](../../../agents/store/STORE.md#the-building)'s 2.5 m plan.

**The four numbers under each layout (historical schematic counts, not physical validation):**

| Number | What it counts | Why it matters |
|---|---|---|
| Shelving | squares of shelving (short ones in brackets) | M8: room for every product |
| Pockets | floor squares in dead ends (about five of them are corners by the tills, the same in every layout) | where you can be cornered |
| Chokepoints | floor squares in the maze whose loss cuts the floor in two | what she can hold |
| Average view | from each floor square, the longest straight line you can see along, averaged | how far she sees you, and you her |

---

## 3. The ten

### 1. Racetrack

- [x] **Retained layout 1** (owner, 14 November; topology approved, physical joins still to verify)

A loop about 5 m in from the walls runs round a block of short east–west aisles. Each aisle opens onto the loop at both ends and breaks once in the middle, the breaks staggered.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...#######...#######.#.|#
D.....................#.|#
D.#.#######.#########.#.|#
#.#.....................|#
#.#.#########...#####.#.|#
#.#...................#.|#
#.#.#####...#########.#.|#
#.....................#.|#
#.#.###########...###.#.|#
#.#.....................|#
#.#.#########.#######.#.|#
#.#...................#.|#
#.#.#######...#######.#.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 160 (20 short) | 5 | 0 | 43 m |

- **For a chase:** You run laps, and so does she. The loop's long legs are the most exposed places in any of the ten.
- **Sections:** The block holds the dry goods (cereal, noodles, tins, snacks); the loop's outer walls the rest.
- **Risk:** Long views make her strong: the loop may want low shelves on its inner side.

### 2. Labyrinth

- [x] **Retained layout 2** (owner, 14 November; topology approved, physical joins still to verify)

The endless maze's own rules, on the store: a spanning tree that joins every cell, then braided (75% of dead ends knocked through, 12% of the other walls), from seed 7, with doorways all round its edge.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...#####.#####.#####.#.|#
D.........#...#.......#.|#
D.#...#.#####.#####.###.|#
#.#...#...#...#.........|#
#.#.#####.#.###...#####.|#
#...#...#...#.........#.|#
#.#.#.#.#...#...#.###.#.|#
#.#.#.#.#...#...#.......|#
#.###.#.#.#.#.#####.###.|#
#.....#...#.......#.#.#.|#
#.#.###.#.#.#.###.#.#.#.|#
#.#.....#...#...#.......|#
#.###.#####.#####.#####.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 165 (20 short) | 21 | 16 | 19 m |

- **For a chase:** Meant to feel most like today's store (not measured: today's store couldn't be compared on the same drawing): the shortest views of the ten, many corners, a few pockets.
- **Sections:** Sections are cut as rectangles over it, as today.
- **Risk:** Getting lost in a bad way (the release plan's worry): it needs the aisle signs and a landmark or two.

### 3. Fishbone

- [x] **Retained layout 3** (owner, 14 November; topology approved, physical joins still to verify)

A spine two cells wide runs from the front to the back. Aisles run off both sides; the north three on each side also open onto the side aisle by the wall, the south three are closed at the wall: pockets, each broken once half way.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#.#########...#########.|#
D.......................|#
D.###.#####...#####.###.|#
#.......................|#
#.#########...#########.|#
#.......................|#
#.###.#####...#####.###.|#
#.#...................#.|#
#.#########...#########.|#
#.#...................#.|#
#.###.#####...#####.###.|#
#.#...................#.|#
#.#########...#########.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 163 (20 short) | 17 | 12 | 47 m |

- **For a chase:** The spine is a gauntlet with long views. The pockets trap: once she's at the mouth, the only way out is past her.
- **Sections:** Each bone is one section, so a section is easy to find.
- **Risk:** The pockets may feel unfair. Rule M12 still holds (the clock is always reachable), but a player in a pocket has one way out.

### 4. Free-flow

**Parked layout 4** (owner, 14 November; keep as reference only)

Low tables (1.25 m) instead of tall runs, staggered, with the tall shelving only on the walls.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...............+.......|#
D...............+.......|#
D.......+++++...+.+++++.|#
#.......+...............|#
#...+++++.....+++++.....|#
#.............+.........|#
#.+++.+...+++++.....+++.|#
#.....+.............+...|#
#.....+++++.+...+++++...|#
#...........+...........|#
#.+++++.....+++++.+.....|#
#...+.............+.....|#
#...+.............+.....|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 103 (86 short) | 5 | 0 | 26 m |

- **For a chase:** She sees over everything unless you crouch: since 0.4.1 she judges cover eye to eye, so crouching behind a table hides you.
- **Sections:** The least shelving of the ten (and short shelves have two boards, not three): a check that every product still fits.
- **Risk:** Too open for her, or too easy for you: the one layout where crouching is everything.

### 5. Rings

- [x] **Retained layout 5** (owner, 14 November; topology approved, physical joins still to verify)

Three rings, one inside the other, with two ways through each ring, never in line with the next ring's, so the way in winds. A promotion table stands in the middle.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...#########.#########.|#
D.....................#.|#
D.#.#################.#.|#
#.#.#.................#.|#
#.#.#...#######.#...#.#.|#
#.#.#...#.......#...#.#.|#
#.#.#...#.+++++.#...#.#.|#
#.#.#...#.......#...#.#.|#
#.#.#...#.#######...#.#.|#
#.#.................#.#.|#
#.#.#################.#.|#
#.#.....................|#
#.#########.#########...|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 166 (25 short) | 5 | 0 | 34 m |

- **For a chase:** The further in you go, the longer the way out. No pockets: every ring corridor is a loop.
- **Sections:** The rings suit sections that wrap round: fruit and veg on the outer ring by the doors.
- **Risk:** The ring corridors are long and straight on their sides.

### 6. Split

- [x] **Retained layout 6** (owner, 14 November; topology approved, physical joins still to verify)

One wall of shelving runs down the middle, with a single gap half way. With the front and the back aisle, that's three ways across. Each half is a small braided maze of its own.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#.###.#####.###.###.###.|#
D...........#.........#.|#
D.###.###...#...#####.#.|#
#.#.........#.......#.#.|#
#.#####.....#...#...###.|#
#.#.........#...#.......|#
#.#.#.#.....#.#.###.#.#.|#
#...#.#.......#.....#.#.|#
#.#.###...#.#.#.#...#.#.|#
#.#.#.....#.#...#.....#.|#
#.###.###.#.#...#######.|#
#.#.........#...#...#...|#
#.#####.###.###.#.#####.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 153 (20 short) | 23 | 17 | 22 m |

- **For a chase:** Chokepoints she can hold: block the gap, and the only ways across are the front and the back.
- **Sections:** One half for food, one for the rest, say.
- **Risk:** If she camps the gap it can feel hopeless; the front and back stay open (rule M12).

### 7. Plaza

**Parked layout 7** (owner, 14 November; keep as reference only)

An open square in the middle, about 10 × 10 m, with a promotion table. Four blocks stand round it, with aisles running out of it north, south, east and west.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...#######...#######.#.|#
D.....#...............#.|#
D.#...###.#...#...#...#.|#
#.#.......#...#...#...#.|#
#.###.#####...#####.###.|#
#.......................|#
#.........+++++.........|#
#.......................|#
#.#####.###...###.#####.|#
#.#...#...#...#.......#.|#
#.#...#...#...#.###...#.|#
#.#...............#...#.|#
#.#.#######...#######.#.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 136 (25 short) | 9 | 4 | 30 m |

- **For a chase:** Crossing the square is the risk; the blocks are cover. She can watch the square from any of its four aisles.
- **Sections:** The square is a natural place for the week's promotion.
- **Risk:** The square's views are short, its aisles' long.

### 8. Switchback

**Parked layout 8** (owner, 14 November; keep as reference only)

Five long aisles across the store, each turning at the opposite end from the last, closed at the sides, with two shortcuts through the runs.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#.....................#.|#
D.....................#.|#
D.###################.#.|#
#.#...................#.|#
#.#.#######.###########.|#
#.#...................#.|#
#.###################.#.|#
#.#...................#.|#
#.#.###########.#######.|#
#.#...................#.|#
#.###################.#.|#
#.#...................#.|#
#.#...................#.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 149 (20 short) | 5 | 0 | 45 m |

- **For a chase:** A long way round, and every aisle is a lane she can look straight down. The shortcuts are secrets worth knowing.
- **Sections:** Each aisle holds two or three sections in the customer's order.
- **Risk:** Shoppers' lists cross the store, so their walk gets long; the queue may suffer.

### 9. Four rooms

**Parked layout 9** (owner, 14 November; keep as reference only)

A cross of shelving splits the floor into four rooms, with two doorways through each arm of the cross, two from the front and two to the back aisle. Low shelves in the north rooms, tall ones in the south.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#.###.#############.###.|#
D...........#.........#.|#
D.#.+++++++.#.........#.|#
#.#...................#.|#
#.#.........#.+++++++.#.|#
#.#.........#...........|#
#.#####.#########.#####.|#
#.#.........#.........#.|#
#.#.........#.#######.#.|#
#.#...................#.|#
#.#.#######.#.........#.|#
#.#.........#...........|#
#.#####.#########.#####.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 147 (34 short) | 5 | 0 | 29 m |

- **For a chase:** Room to room, like a house: you hear her in the next room before you see her.
- **Sections:** A room per group of sections: fresh, dry, household, cold.
- **Risk:** Doorways are where she waits; the north rooms are open to view.

### 10. Brick bond

- [x] **Retained layout 10** (owner, 14 November; topology approved, physical joins still to verify)

Runs of two bays, offset row to row like bricks, with short cross pieces where they don't close a cell.

```
#######DD########DD#######
D........................#
D........................#
#........................#
#.........++++++....++++|#
#.$.$.$.................|#
#.$.$.$...++++++....++++|#
#.......................|#
#...#####.#####.#####.#.|#
D...........#.........#.|#
D.#####.#####.#####.###.|#
#.#.....#...............|#
#.#.#####.#####.#####.#.|#
#.#...........#.......#.|#
#.#####.#####.#####.###.|#
#...................#.#.|#
#.#.#####.#####.#####.#.|#
#.#.....................|#
#.#####.#####.#####.###.|#
#.#.........#.........#.|#
#.###.#####.#####.#####.|#
#........................#
#FFFFFF..FFFFFFFF..FFFFFF#
#######DD########DD#######
```

| Shelving | Pockets | Chokepoints | Average view |
|---|---|---|---|
| 172 (20 short) | 13 | 8 | 30 m |

- **For a chase:** No long view in either direction, and corners everywhere: good for her Ambush, good for your breaking line of sight.
- **Sections:** Sections are cut as rectangles, as today.
- **Risk:** So many corners that you may see her only at two metres.

---

## 4. How they were checked

A short script drew each layout on the lattice and checked it, outside the repo (it's a planning
aid, not game code). For every layout:

- every floor square is reachable from every other;
- the aprons of all eight doorways and the tills' lanes are open (M11);
- one-sided shelving touches a wall, and chillers stand only on the south wall (M4, M6);
- the four numbers above.

**The drawings are the source of truth.** The Labyrinth came from Python's random numbers with
seed 7, which C# won't repeat, so C4.1 copies each drawing's walls, not a seed.

**What it doesn't check:** the real shelf models' sizes and how the pillars join (C4.2 does that
in Unity), whether every section has the right shelves for its products (C4.3), and how the bots
and Karen play on it (C4.6, C4.8). Those are tasks in [Phase 4](../0.5.0-plan.md#phase-4-the-layouts-c-and-her-training-across-them-c).
