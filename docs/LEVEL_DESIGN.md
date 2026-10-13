# Level design: outside, front of house, back of house

Plans and model lists for the parts of the store that are still grey boxes: the parking lot, the
building's outside, the lobby, the restrooms and the staff rooms. It feeds Milestone 7 of
[RELEASE_PLAN.md](RELEASE_PLAN.md) (level design, models of the entire place). Greybox these
first and playtest Karen's routes through them before dressing them.

Coordinates are the scene's: x runs east, z runs north, metres. The street is north of the store
(+z); the store runs south from it.

1. [The building as it stands](#1-the-building-as-it-stands)
2. [Parking lot](#2-parking-lot)
3. [The outside of the store](#3-the-outside-of-the-store)
4. [Lobby](#4-lobby)
5. [Restrooms](#5-restrooms)
6. [Staff rooms](#6-staff-rooms)
7. [Palette and materials](#7-palette-and-materials)
8. [Model list](#8-model-list)

---

## 1. The building as it stands

| part | x | z | size | what's there now |
|---|---|---|---|---|
| Whole store (walls) | 10 – 85 | −190 – −110 | 75 × 80 m, 8 m walls, roof at 8.0 – 8.6 m | |
| Lobby | 25 – 85 | −120 – −110 | 60 × 10 m | two pairs of automatic doors, x 42.5 (exit) and 67.5 (entrance), outer at z −110, inner at −120 |
| Sales floor | 25 – 85 | −175 – −120 | 60 × 55 m | 13 aisles; tills at x 30 – 40, z −131.5 |
| Staff room (front) | 10 – 25 | −130 – −110 | 15 × 20 m | empty; door from the sales floor at (25, −122.5) |
| Staff room (back) | 10 – 25 | −150 – −130 | 15 × 20 m | time clock, coffee machine, radio, mop rack, stock crate; door at (25, −142.5) |
| Backstreet | 10 – 25 | −175 – −150 | 15 × 25 m, roofed, counts as outside | breaker box (24.5, −152), skip (22.6, −164.5) |
| Storage room | 10 – 54.5 | −190 – −175 | 44.5 × 15 m | racks of product boxes; doors from the sales floor at x 42.5 and from the backstreet at x 17.5 |
| Cold room | 54.5 – 85 | −190 – −175 | 30.5 × 15 m | fridges and chest freezers; door from the sales floor at x 67.5 |
| Ground | −55 – 95 | −200 – −50 | 150 × 150 m | shoppers appear at (67.5, −102.5) |

There are no restrooms yet, and the front staff room is an empty 300 m². The plan below takes
both from it.

---

## 2. Parking lot

North of the store, the full width of the building and a little more: **85 × 55 m = 4,675 m²,
78% of the store's floor** (the ask was at least half). Aisles run **towards** the doors, not
across them, so shoppers walk straight down an aisle to the entrance and never cross lines of
parked cars.

```
 z  −47 ════════════════════ road, two lanes (8 m) ═════════════════════════════
        trees    ▲ pylon        ║ IN / OUT (8 m) ║                    trees
 z  −55 ─────────────────────────╨────────────────╨──────────────────────────────
        ◄──────────────── north drive aisle, two-way (6.5 m) ────────────────►
 z −64.5 ┌───┬────┬────┬─┬────┬────┬─┬────┬────┬─┬────┬────┬───┐
         │ W │ ▮▮ │ ↕  │ │ ▮▮ │ ↕  │ │ ▮▮ │ ↕  │ │ ▮▮ │ ↕  │ E │
         │ d │ ▮▮ │    │ │ ▮▮ │    │ │ ▮▮ │    │ │ ▮▮ │    │ d │   13 stalls a row
         │ r │ ▮▮ │ A  │▲│ ▮▮ │ B  │▲│ ▮▮ │ C  │▲│ ▮▮ │ D  │ r │   two rows a module
         │ i │ EV │    │ │    │    │ │ ♿ │    │ │ ♿ │    │ i │
         │ v │ EV │    │ │    │    │ │ ♿ │    │ │ ♿ │    │ v │   ▲ planted island
         │ e │    │    │ │    │    │ │    │    │ │    │    │ e │      with a lamp post
 z −97.5 └───┴────┴────┴─┴────┴────┴─┴────┴────┴─┴────┴────┴───┘
        ◄────── fire and pickup lane, one-way west (6.5 m) ── ⊞ crossings ──
 z  −104 ▓ apron under the canopy (6 m): vending, carts, bikes, benches ▓▓▓▓▓▓
 z  −110 ███ staff ███│██ glass, EXIT x 42.5 ██│ ads │██ glass, ENTRANCE x 67.5 ██│
```

| band | z | depth | notes |
|---|---|---|---|
| Road | −55 – −47 | 8 m | two lanes; the ground has to be extended north to here |
| Verge | −58 – −55 | 3 m | grass, trees, the pylon sign; one driveway, x 43 – 51 |
| North drive aisle | −64.5 – −58 | 6.5 m | two-way, joins the four parking aisles |
| Parking field | −97.5 – −64.5 | 33 m | four modules, see below |
| Fire and pickup lane | −104 – −97.5 | 6.5 m | one-way west, no parking; two pickup bays by the west end; zebra crossings at x 42.5 and 67.5; one speed bump |
| Apron | −110 – −104 | 6 m | pavement under the canopy |

**The modules.** Each is a 6.5 m aisle with a row of 2.5 × 5 m stalls on each side (16.5 m
wide), 13 stalls a row. A 1.5 m planted island between modules carries the lamp posts. West and
east perimeter drives (6.5 m) close the loop.

| module | x | aisle centre | special stalls |
|---|---|---|---|
| A | 12 – 28.5 | 20.25 | 4 EV stalls with two chargers (south end, west row); motorbike bay at its north end |
| B | 30 – 46.5 | 38.25 | 2 family stalls by the south end; a cart corral mid-row |
| C | 48 – 64.5 | 56.25 | 3 accessible stalls (3.6 m + a hatched 1.5 m aisle) at the south end; a cart corral |
| D | 66 – 82.5 | 74.25 | 3 accessible stalls at the south end; 2 family stalls; a cart corral |

**About 100 stalls** in all: 104 minus what the accessible stalls' extra width and the corrals
take. The island between C and D (x 64.5 – 66) lines up with the entrance, so make it a raised
1.5 m walkway that leads straight to the door.

**Markings and kit:** white stall lines (0.1 m), arrows in the aisles, STOP lines and zebra
crossings at the doors, blue accessible bays with the symbol painted and a post sign, green EV
bays, yellow kerbs along the fire lane, wheel stops in the first row, kerbs and planters on the
islands, 10 lamp posts (8 m, one or two heads), the pylon sign, 4 cart corrals, bollards along
the apron, a few parked cars (6 – 10, so it's never empty, fewer at night), litter and a
shopping trolley left in a bay.

**For the game.** An open lot is the one place Karen can see a long way, so keep the lamps
patchy: one dead, one flickering. The corrals and the parked cars are the cover between the
door and the road.

---

## 3. The outside of the store

### The front (north wall, z −110)

```
 10 m ┌──────────────────────────────────────────────────────────────────┐ parapet, black cap
      │▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  K E H A I  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│ red brand band (1.2 m)
  8 m ├──────────────────────────────────────────────────────────────────┤
      │ staff block │░░ glass ░░│ ads ▣ ▣ ▣ │░░ glass ░░│ deals board  ▌24h│ white ribbed panels
4.5 m │  (solid)    │░ EXIT ░░░│  lightboxes │░ IN ░░░░░│              ▌   │ ═══ canopy, 32 – 78
      │             │░ doors ░░│             │░ doors ░░│              ▌   │
0.8 m ├─────────────┴──────────┴─────────────┴──────────┴──────────────────┤ charcoal plinth
      x 10          37.5   47.5            62.5   72.5                   85
```

- **Walls:** a charcoal concrete plinth (0 – 0.8 m); white insulated metal panels with a fine
  vertical rib above it; a red brand band at the top (1.2 m); a parapet to 10 m with a black cap,
  so the roof edge doesn't show from the lot.
- **Two glass portals**, 10 m wide and 4.5 m tall (x 37.5 – 47.5 and 62.5 – 72.5), around the
  existing door pairs: dark-tinted glass, black frames, a mullion every 1.25 m.
- **Canopy:** steel, 6 m deep, 4.5 m high, x 32 – 78. Black fascia with the red band, white
  underside, downlights every 3 m.
- **Between the portals:** three backlit poster boxes (1.2 × 1.8 m) with the game's own brands.
  Use the product labels: a Pipisi can, a Krunchos bag, a Ramyum cup.
- **The staff block** (x 10 – 25) is solid wall: a fire exit door with a lit sign, a gas meter
  box, a CCTV dome.
- **East end:** a vertical blade sign, "OPEN 24H", and a "this week's deals" price board.

**Branding:** the store's name in 1.8 m letters on the band above the doors, lit at night. One
letter dead. The same black, red and white as the game's menus. A red ring or a simple
"presence" mark (気配) for the logo, also on the pylon sign, the carts and the staff uniforms.

### The sides and the back

- **West wall:** the backstreet's gate (a roller shutter, 4 m), the breaker box on the inside
  of it, a dumpster bay with the skip.
- **East wall:** two emergency exits with lit EXIT signs and push bars, a fire hose cabinet, a
  dry riser inlet, wall-pack lights.
- **Back (south):** a loading dock behind the storage room: two dock doors with rubber bumpers,
  dock lights and yellow bollards, a cardboard compactor, stacked pallets, a staff smoking spot
  with a sand bin.
- **Everywhere:** downpipes every 10 m, a CCTV dome on each corner, "CCTV in operation" signs.
- **Roof:** flat, behind the parapet. Four HVAC units, ducting, a roof hatch. No skylights: they
  would let daylight in, and the store has to be able to go black.

### Things that stand outside

Under the canopy, east to west:

| item | where | notes |
|---|---|---|
| Drink vending machines × 3 | east of the entrance | Japanese style, lit; one sells Pipisi and Genki |
| Ice cream freezer | by the vending machines | |
| Capsule toy machines × 6 | west of the entrance | a row of gachapon |
| ATM | on the wall between the portals | |
| Recycling station | east end | four bins: PET, cans, glass, paper |
| Bins with ashtray tops × 2 | by each door | |
| Bagged-ice chest | by the exit | |
| Trolley shelter | in front of the entrance | roofed, 20 trolleys |
| Bike rack | west end | 10 bikes |
| Benches × 2, planters × 4 | along the apron | |
| Bollards | in front of the glass | steel, yellow tops, every 1.5 m |
| Flyer stand, umbrella stand | by the entrance | |

---

## 4. Lobby

The 60 × 10 m space between the outer and inner doors. It's the airlock between outside and
the shop, and in the game the place a shift starts and ends, so it should read as calm and safe.

```
 z −110  outer wall ──────█ EXIT █──────────────────────────────█ IN █──────────────
        │WC│ service │ lockers │ mats │  bench · notice board ·  │ mats │ baskets │trolley│
        │→ │ counter │ umbrella│      │  deals table · plants    │      │sanitiser│  bay  │
        │  │         │ map     │ EAS  │                          │ EAS  │         │       │
 z −120  inner wall ──────█ EXIT █──────────────────────────────█ IN █──────────────
        x 25      33        40   42.5   45                  65   67.5  70       77    85
```

| item | where | size, notes |
|---|---|---|
| Door to the restrooms | west wall, (25, −115) | single door, "Toilets" sign with pictograms |
| Customer service counter | x 26 – 33, along the inner wall | 6 m counter, two stools, a till, returns and parcel shelf behind |
| Coin lockers | x 33 – 37 | 2 × 6 lockers for bags |
| Umbrella stand, wet-umbrella bag dispenser | by the lockers | |
| Store map and aisle directory | x 37 – 40, on the outer wall | aisles 1 – 13, "you are here" |
| Entrance mats | 3 m deep at each door pair | dark grey |
| Security gates (EAS) | at each inner door | two pedestals a door |
| Bench, notice board, plants | x 45 – 65 | community notices, lost and found, a "staff wanted" poster |
| Deals table | middle | the week's promotion, a small end-cap of one product |
| Basket stacks × 3, hand sanitiser | x 68 – 72 | red baskets (they exist) |
| Trolley bay | x 77 – 85 | 4 rows of 5, a mobility scooter space |
| Signs | hanging | "Entrance", "Exit", "Welcome / いらっしゃいませ", "Thank you" |

**Finish:** light grey polished floor tiles, white walls like the shop, a lower ceiling (3.5 m)
with downlights, so the shop opening out beyond the inner doors feels big.

---

## 5. Restrooms

Taken from the north end of the front staff room: **x 10 – 25, z −120 – −110 (15 × 10 m)**, entered
from the lobby, so customers never go through staff space.

```
 z −110 ┌────────────┬───────────────────────────────┐
        │ Accessible │ Women's  ▯ ▯ ▯   ⌓ ⌓  dryer   │
        │ and family │ baby change          │ jan. ▤ │
        │  WC, rails ├──────────── door ────┴────────┤
        │  baby chg  ◄─────── corridor 1.8 m ────────◄ door from the lobby (25, −115)
        │  pull cord ├──────────── door ─────────────┤
        │            │ Men's   ▯ ▯   ┃┃┃   ⌓ ⌓  dryer│
 z −120 └────────────┴───────────────────────────────┘
        x 10         14                             25
```

| room | size | fixtures |
|---|---|---|
| Corridor | 1.8 m wide, z −115 | handrail, hand sanitiser, "Wet floor" sign |
| Women's | 11 × 4 m | 3 stalls, 2 basins, hand dryer, mirror, fold-down baby change, bin |
| Men's | 11 × 4 m | 2 stalls, 3 urinals with dividers, 2 basins, hand dryer, mirror, bin |
| Accessible and family | 4 × 10 m end room | WC with grab rails, basin, emergency pull cord, baby change table, a child seat |
| Cleaner's cupboard | 1.5 × 2 m | mop sink, shelves of supplies, the mop bucket |

**Finish:** white 20 cm wall tiles to 1.5 m with a grey band; painted wall above; dark grey
anti-slip floor tiles with a drain; stall partitions in the brand red; stainless basins and
fittings; signs with pictograms and Japanese (男 / 女 / 多目的トイレ).

**For the game:** a long mirror over the basins (a place to show something behind the player),
one stall door locked with "Out of order" on it, a light that hums.

---

## 6. Staff rooms

### The back staff room: the crew room (x 10 – 25, z −150 – −130)

Where the shift starts: the time clock, the radio and the coffee machine are already here.
Lockers, a kitchenette and a break table make it a crew room.

```
 z −130 ┌───────────────────────────────────────────┐
        │ lockers ▯▯▯▯▯▯▯▯▯▯▯▯   bench   coat hooks  │
        │                                     mop ▥ │
        │ kitchen   ┌────────────┐                  │
        │ counter   │ break table│  6 chairs        ◄ door from the sales floor (25, −142.5)
        │ sink ⌓    └────────────┘                  │
        │ fridge ▣ coffee ☕ radio ♪ clock ⏲ cards ▦ │  (existing: radio 18.5, coffee 20,
        │ sofa ▬▬▬  TV (CCTV)   notice board ▦       │   time clock 21.5, crate 16.85)
 z −150 └──────────────── door to the backstreet ───┘
        x 10              17.5                      25
```

- **North wall:** a row of 12 steel lockers (0.4 m each) with a bench in front; coat hooks.
- **West wall:** a 4 m kitchenette: counter, sink, microwave, kettle, mini fridge, the coffee
  machine (at (20, −144.5) now), a water cooler, a snack vending machine.
- **Middle:** a break table (2.4 × 1 m) with six plastic chairs.
- **South wall:** the time clock and card rack (they exist), the radio (exists), a notice board
  (shift rota, safety posters, "employee of the month"), a first aid box, a fire extinguisher, a
  worn sofa and a small TV that shows the CCTV feed (it could show Karen's cameras).
- **East wall:** the mop rack (exists), a cleaning cart, the stock crate (exists).
- **New:** a door in the south wall at x 17.5 to the backstreet, a short way to the breakers.

**Materials and colours:** institutional and a little worn.
- **Floor:** beige vinyl tiles (#CFC6B5), with a scuffed path to the clock.
- **Walls:** painted block, sage green below a dado rail (#8FA38C), off-white above (#ECEBE4).
- **Ceiling:** drop tiles with 60 × 60 cm light panels, one of them flickering.
- **Doors:** grey steel (#9AA0A6) with kick plates and "STAFF ONLY".
- **Furniture:** blue-grey lockers (#5B6B7A); a white laminate table with chrome legs; orange
  plastic chairs (#E07A2E), the room's one warm colour; a brown vinyl sofa, cracked.

### The front staff room: Karen's office and security (x 10 – 25, z −130 – −120)

What's left of the front staff room once the restrooms take its north end: 15 × 10 m, reached
from the sales floor by the existing door at (25, −122.5). Karen is the store manager, so this is
her room: tidy, cold, and watching.

```
 z −120 ┌────────────────────────────────────────────┐
        │ CCTV wall ▣▣▣▣   │ security desk, 2 chairs │
        │ (4 screens)      │                         │
        ├──── glass ───────┘                         ◄ door from the sales floor (25, −122.5)
        │ Karen's desk ▭  chair   filing ▯▯▯  safe ▣ │
        │ whiteboard (targets)   certificates  plant │  internal window onto the shop
 z −130 └────────────────────────────────────────────┘
```

- **Office:** a dark desk, a black chair, grey filing cabinets, a floor safe, a whiteboard of
  sales targets with the player's name circled in red, framed "Manager of the year" certificates,
  a fake plant, a computer showing the staff rota.
- **Security corner:** a desk under a wall of four CCTV screens showing the store's cameras
  (live, if possible), two chairs, a radio charger.
- **An internal window** with venetian blinds onto the sales floor: a sightline both ways.
- **Materials and colours:** cool grey walls (#B9BEC4), charcoal carpet tiles (#3E4247), black
  furniture, the brand red as the only accent.

---

## 7. Palette and materials

One palette everywhere, so the place reads as one store (and matches the menus):

| use | colour |
|---|---|
| Brand red | #C8102E |
| Black (signs, frames, caps) | #111111 |
| White (store walls, panels) | #E8E8E4 |
| Shelf grey | #808085 (now `M_ShelfGrey`) |
| Charcoal (plinths, carpet) | #3A3A3C |
| Staff green / off-white | #8FA38C / #ECEBE4 |
| Asphalt / line white / accessible blue / EV green | #2B2C2E / #F2F2EE / #1F5FA8 / #2E8B57 |

Low poly, flat colours, one texture atlas per area where a texture is needed (posters, signs,
labels); the same texel density as the product labels.

---

## 8. Model list

Grouped by area, in rough build order. Sizes are a starting point.

**Parking lot:** asphalt slab with painted lines (decal or texture); kerb (straight, corner);
planted island; tree (2 kinds); lamp post (1 and 2 heads); pylon sign (8 m); cart corral; wheel
stop; bollard; speed bump; EV charger; accessible post sign; parked car (3 shapes × 3 colours);
motorbike; a stray trolley; litter.

**Outside of the store:** wall panel set (plinth, ribbed panel, band, parapet cap); glass portal
with door frames; canopy with downlights; brand letters; blade sign; poster lightbox; deals
board; vending machine (drinks, ice cream); capsule machine; ATM; recycling bins; bin with
ashtray; ice chest; trolley shelter; bike rack; bench; planter; fire exit door with sign; fire
hose cabinet; downpipe; wall-pack light; CCTV dome; loading dock door with bumpers and dock
light; compactor; pallet stack; roller shutter; HVAC unit; roof hatch.

**Lobby:** service counter with stool and till; coin lockers; umbrella stand and bag dispenser;
directory board; entrance mat; EAS gate; bench; notice board; deals table; basket stack;
sanitiser stand; trolley bay; hanging signs.

**Restrooms:** stall partition and door (one "out of order"); WC; urinal with divider; basin
counter; mirror; hand dryer; baby change (fold-down, table); grab rails; pull cord; mop sink;
supply shelf; pictogram signs.

**Crew room:** locker bank; bench; coat hooks; kitchenette (counter, sink, microwave, kettle, mini
fridge); water cooler; snack vending machine; break table; plastic chair; sofa; TV; notice board;
first aid box; fire extinguisher; cleaning cart; light panel (with a flicker variant).

**Office and security:** desk; office chair; filing cabinet; safe; whiteboard; framed
certificates; plant; computer; CCTV screen wall; security desk; venetian blind window.
