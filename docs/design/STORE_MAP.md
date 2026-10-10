# Kehai — Store Map

The building as Karen and the eval harness see it. **Generated** from the scene by
`Kehai/Map/Export STORE_MAP.md` (`StoreMap.cs` + `StoreMapReport.cs`) — do not edit by
hand; move a shelf and re-export instead.

**2799 cells, 310 regions in 7 rooms, 504 links, 29 chokepoints, 24 landmarks, 189 bays (143 ms)**

World coordinates are metres. The street is north (high Z); the store runs south
along −Z to the stockroom. East is +X.

## How the map is built

- **Cells** — a 1.5 m grid over the walkable NavMesh. Two cells are joined when a
  person could walk straight between them: two physics sweeps (knee and chest height)
  that stop at walls and shelving but pass doors, stock and people. Cells nobody can walk
  to are dropped.
- **Rooms** — what the walls enclose once every doorway is cut out; named from the area table
  in `StoreMap.AreaAt`.
- **Regions** — each room divided into 5 m tiles, each tile split into its connected
  pieces. Every doorway is a region of its own. Sales-floor regions are named for their
  planogram section (`StoreLayout`), lettered north→south, east→west: `Aisle 2/B`.
- **Links** — region neighbours, weighted by how wide the boundary is. Min-cut uses the width.
- **Chokepoints** — articulation points: regions whose loss splits the store in two.

## Floor plan

One character per 1.5 m cell, north up. The ruler marks every 10 m of X; row labels are Z.

```
         |      |     |      |      |     |      |      |     |
  -106                                                         
  -108 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~ 
  -109 ~~~~~~~~~~~~~~~~~~~~~~u+++~~~~~~~~~~~~~~++~~~~~~~~~~~~~ 
  -111 ___rrrrrrrrrrllllllllll+++llllllllllllll++lllllllllll__ 
  -112 ___rrrrrrrrrrllllllllllllllllllllllllllllllllllllllll__ 
  -114 ___rrrrrrrrrrllllllllllllllllllllllllllllllllllllllll__ 
  -115 ___rrrrrrrrrrllllllllllllllllllllllllllllllllllllllll__ 
  -117 ___rrrrrrrrrrllllllllllllllllllllllllllllllllllllllll__ 
  -118 ___rrrrrrrrrrllllllllllllllllllllllllllllllllllllllll__ 
  -120 ___rrrrrrrrrrllllllllll+++llllllllllllll++lllllllllll__ 
  -121 ___rrrrrrrrr+WWWWWWWWWW+++BBBBBBBBBBPPPP++PPPPPPPPPPP__ 
  -123 ___rrrrrrrrr+WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__ 
  -124 ___rrrrrrrrrrWWWWWWWWuWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__ 
  -126 ___rrrrrrrrrrWWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__ 
  -127 ___rrrrrrrrrrWWWWWWWWuWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__ 
  -129 ___rrrrrrrrrrWWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__ 
  -130 ___rrrrrrrrrrWWWWWWWWWWBBB       BBB       PPP       __ 
  -132 ___rrrrrrrrrrWW $WW$WW $BBBBBBBBBBBBPPPPPP PPPPPPPPP __ 
  -133 ___rrrrrrrrrrWW WWWWWW BBBBBBBBBBBBBPPPPPP PPPPPPPPP __ 
  -135 ___rrrrrrrrrrWW WWWWWW BBBBBB       PPPPPP        PP __ 
  -136 ___rrrrrrrrrrWWWWWWWWWWBBBBBB BBBBBBPPP PPPPPPPPP PP __ 
  -138 ___rrrrrrrrrrWWWWWWWWWWBBBBBB BBBBBBPPP PPPPPPPPP PP __ 
  -139 ___rrrrrrrrrrWWWWWWWWWWBBBBBB BBBBBBPPP PPPPPPPPP PP __ 
  -141 ___rrrrrrrrroHHHHHH HH HHHCCCCCCCCCCSSSSSSSSSSDDDDDD __ 
  -142 ___rrrrrrrrr+HHHHHH HH HHHCCCCCCCCCCSSSSSSSSSSDDDDDD __ 
  -144 ___rrrrrrctr+HHHHHH HH HHHCCCCCCCCCCSSSSSSSSSSDDDDDD __ 
  -145 ___rrrrrmrrr HHHHHH HH HHH       CCCSSS              __ 
  -147 ___rrrrrrrrrrHHHHHH HH HHHCCCCCC CCCSSSSSSSSSSDDDDDD __ 
  -148 ___rrrrrrrrrrHHHHHH HH HHHCCCCCC CCCSSSSSSSSSSDDDDDD __ 
  -150 ___rrrrrrrrrrHHHHHH HH HHH   CCC                 DDD __ 
  -151 ___bbbbbbbbb HHHHHHHHHHHHHCCCCCC CCCSSSSSSSSSSDDDDDD __ 
  -153 ___bbbbbbbbe HHHHHHHHHHHHHCCCCCC CCCSSSSSSSSSSDDDDDD __ 
  -154 ___bbbbbbbbb HHHHHHHHHHHHHCCCCCC CCCSSSSSSSSSSDDDDDD __ 
  -156 ___bbbbbbbbbbKKKKKK KK KKKNNN NNNNNNTTTTTTTTTTDDDDDD __ 
  -157 ___bbbbbbbbbbKKKKKK KK KKKNNN NNNNNNTTTTTTTTTTDDDDDD __ 
  -159 ___bbbbbbbbbbKKKKKK KK KKKNNN NNNNNNTTTTTTTTTTDDDDDD __ 
  -160 ___bbbbbbbbbbKKK    KK KKK    NNN                DDD __ 
  -162 ___bbbbbbbbbbKKKAAAKKK KKKNNNNNNNNNNTTTTTTTTTTDDDDDD __ 
  -163 ___bbbbbbbbkbKKKAAAKKK KKKNNNNNNNNNNTTTTTTTTTTDDDDDD __ 
  -165 ___bbbbbbb   KKKAAAK          NNN                DDD __ 
  -166 ___bbbbbbbb  AAAAAAAAAAAAAFFF FFFFFFMMMMMMMMMMMMMMMM __ 
  -168 ___bbbbbbbbbbAAAAAAAAAAAAAFFF FFFFFFMMMMMMMMMMMMMMMM __ 
  -169 ___bbbbbbbbbbAAAAAAAAAAAAAFFF FFFFFFMMMMMMMMMMMMMMMM __ 
  -171 ___bbbbbbbbbbAAAAAA AAAAAAFFFFFF FFFMMMMMMMMMMMMMMMM __ 
  -172 ___bbbbbbbbbbA AA A AAAAAAFFFFFF FFFMMMMMMMMMMMMMMMM __ 
  -174 ___bbbbbbbbbbAAAAAA AAAAAAFFFFFF FFFMMMMMMMMMMMMMMMM __ 
  -175 ___bbbs++bbbbAAAAAA AAA++s             s++s        M __ 
  -177 ___ss sss             ssss       sssssssssssssssssss __ 
  -178 ___ssssssssssssssssssssssssssssxssssssssssssssssssss __ 
  -180 ___ssssssssssssssssssssssssssssssss    ssss       ss __ 
  -181 ___sss          sss          sss+sssssssssssssssssss __ 
  -183 ___sss          sss          sss+sssssssssssssssssss __ 
  -184 ___sss          sss          sssssssssssssssssssssss __ 
  -186 ___ssssssssssssssss sssssssssssssss    ssss       ss __ 
  -187 ___sssssssssssssss  ssssssssssssssssssssssssssssssss __ 
  -189 ___                              sssssssssssssssssss __ 
  -190 _______________________________________________________ 
  -192 _______________________________________________________ 
  -193 _______________________________________________________ 
```

| glyph | meaning | glyph | meaning |
|---|---|---|---|
| `~` | street | `_` | alleys and yard (outside) |
| `l` | lobby | `r` | staff rooms |
| `b` | backstreet | `s` | stockroom |
| `+` | doorway | ` ` | wall, shelving, or unreachable |
| `P` | Produce | `B` | Bakery |
| `W` | Checkout · Sweets | `D` | Aisle 1 · Soft Drinks |
| `S` | Aisle 2 · Snacks | `T` | Aisle 3 · Tins & Jars |
| `C` | Aisle 4 · Cereal | `N` | Aisle 5 · Noodles |
| `H` | Aisle 6 · Health & Beauty | `K` | Aisle 7 · Household |
| `M` | Dairy wall | `F` | Frozen wall |
| `A` | Pet wall | | |
| `$` | checkout till | `c` | coffee machine |
| `t` | time clock | `e` | breaker box |
| `m` | store radio | `o` | mop rack |
| `x` | stock pallet | `u` | bin |
| `k` | skip | `@` | customer spawn |

## Rooms

| room | regions | cells | area (m²) | doors |
|---|---|---|---|---|
| Outside | 81 | 694 | 1561.5 | Door: Backstreet - Stockroom; Doors: Lobby - Street; Doors: Lobby - Street (2) |
| Stockroom | 18 | 151 | 339.8 | Door: Sales floor - Stockroom (2); Door: Stockroom - Stockroom 2 |
| Stockroom 2 | 21 | 156 | 351 | Door: Backstreet - Stockroom; Door: Sales floor - Stockroom; Door: Stockroom - Stockroom 2 |
| Sales floor | 132 | 1231 | 2769.8 | Door: Sales floor - Staff room; Door: Sales floor - Staff room (2); Door: Sales floor - Stockroom; Door: Sales floor - Stockroom (2); Doors: Lobby - Sales floor; Doors: Lobby - Sales floor (2) |
| Staff room | 12 | 126 | 283.5 | Door: Sales floor - Staff room (2) |
| Staff room 2 | 12 | 138 | 310.5 | Door: Sales floor - Staff room |
| Lobby | 24 | 270 | 607.5 | Doors: Lobby - Sales floor; Doors: Lobby - Sales floor (2); Doors: Lobby - Street; Doors: Lobby - Street (2) |

## Doorways

| doorway | at (x, z) | width (links) | chokepoint |
|---|---|---|---|
| Doors: Lobby - Street | 67.6, -109.9 | 18 | no |
| Doors: Lobby - Sales floor | 67.6, -120.4 | 18 | no |
| Doors: Lobby - Sales floor (2) | 42.8, -120.4 | 22 | no |
| Doors: Lobby - Street (2) | 42.8, -109.9 | 22 | no |
| Door: Sales floor - Stockroom | 42.1, -175.4 | 9 | no |
| Door: Stockroom - Stockroom 2 | 54.6, -181.9 | 11 | no |
| Door: Sales floor - Staff room | 24.6, -121.9 | 11 | **yes** |
| Door: Sales floor - Stockroom (2) | 67.6, -175.1 | 14 | no |
| Door: Sales floor - Staff room (2) | 24.5, -142.3 | 12 | **yes** |
| Door: Backstreet - Stockroom | 18.1, -175.4 | 9 | no |

## Landmarks

| landmark | kind | at (x, z) | region |
|---|---|---|---|
| Bin 1 | Bin | 38.9, -127 | Checkout/A |
| Bin 2 | Bin | 39.3, -109.1 | Street/J |
| Bin 3 | Bin | 38.7, -124.3 | Checkout/B |
| Breaker box | BreakerBox | 24.5, -152.1 | Backstreet/B |
| Checkout 1 | Checkout | 29.9, -131.5 | Checkout/I |
| Checkout 2 | Checkout | 34.9, -131.5 | Checkout/H |
| Checkout 3 | Checkout | 39.9, -131.5 | Bakery/K |
| Coffee machine | CoffeeMachine | 20, -144.5 | Staff room/Q |
| Customer spawn | CustomerSpawn | 67.5, -102.5 | off the map |
| Mop rack | MopHome | 24.5, -140.5 | Door: Sales floor - Staff room (2) |
| Store radio | Radio | 18.5, -144.5 | Staff room/V |
| Stock pallet | StockCrateHome | 53, -176.5 | Stockroom/G |
| Time clock | TimeClock | 21.5, -144.5 | Staff room/Q |
| Skip | TrashSkip | 22.6, -164.5 | Backstreet/G |

## Planogram sections

Which regions each section of the sales floor covers — where to send a customer asking
for something. `STORE_CATALOG.md` lists what each section sells.

| section | bays | regions |
|---|---|---|
| Produce · Fruit & Veg | 29 | Produce/T, Produce/S, Produce/R, Produce/Q, Produce/P, Produce/O, Produce/N, Produce/M, Produce/L, Produce/K, Produce/J, Produce/H, Produce/F, Produce/C, Produce/B, Produce/I, Produce/G, Produce/E, Produce/D, Produce/A |
| Bakery | 19 | Bakery/P, Bakery/O, Bakery/N, Bakery/M, Bakery/K, Bakery/L, Bakery/J, Bakery/I, Bakery/G, Bakery/E, Bakery/C, Bakery/A, Bakery/H, Bakery/F, Bakery/D, Bakery/B |
| Checkout · Sweets & Impulse | 6 | Checkout/L, Checkout/K, Checkout/J, Checkout/G, Checkout/I, Checkout/H, Checkout/E, Checkout/C, Checkout/A, Checkout/F, Checkout/D, Checkout/B |
| Aisle 1 · Soft Drinks | 20 | Aisle 1/I, Aisle 1/J, Aisle 1/G, Aisle 1/E, Aisle 1/H, Aisle 1/F, Aisle 1/D, Aisle 1/C, Aisle 1/B, Aisle 1/A |
| Aisle 2 · Snacks & Crisps | 15 | Aisle 2/I, Aisle 2/H, Aisle 2/G, Aisle 2/F, Aisle 2/E, Aisle 2/D, Aisle 2/C, Aisle 2/B, Aisle 2/A |
| Aisle 3 · Tins & Jars | 12 | Aisle 3/F, Aisle 3/E, Aisle 3/D, Aisle 3/C, Aisle 3/B, Aisle 3/A |
| Aisle 4 · Cereal & Breakfast | 16 | Aisle 4/I, Aisle 4/H, Aisle 4/G, Aisle 4/F, Aisle 4/E, Aisle 4/D, Aisle 4/C, Aisle 4/B, Aisle 4/A |
| Aisle 5 · Noodles, Pasta & Rice | 10 | Aisle 5/E, Aisle 5/F, Aisle 5/D, Aisle 5/C, Aisle 5/B, Aisle 5/A |
| Aisle 6 · Health & Beauty | 13 | Aisle 6/L, Aisle 6/K, Aisle 6/J, Aisle 6/I, Aisle 6/H, Aisle 6/G, Aisle 6/F, Aisle 6/E, Aisle 6/D, Aisle 6/C, Aisle 6/B, Aisle 6/A |
| Aisle 7 · Household & Cleaning | 17 | Aisle 7/G, Aisle 7/F, Aisle 7/E, Aisle 7/D, Aisle 7/C, Aisle 7/B, Aisle 7/A |
| Back Wall · Dairy & Chilled | 12 | Dairy wall/A, Dairy wall/I, Dairy wall/H, Dairy wall/E, Dairy wall/C, Dairy wall/J, Dairy wall/G, Dairy wall/F, Dairy wall/D, Dairy wall/B |
| Back Wall · Frozen | 10 | Frozen wall/E, Frozen wall/D, Frozen wall/A, Frozen wall/F, Frozen wall/C, Frozen wall/B |
| Back Wall · Pet | 10 | Pet wall/I, Pet wall/H, Pet wall/G, Pet wall/B, Pet wall/F, Pet wall/E, Pet wall/D, Pet wall/C, Pet wall/A |

## Chokepoints

Regions whose loss cuts part of the store off. Karen blocks these first; a player who
knows them knows where not to be cornered.

- **Door: Sales floor - Staff room** (24.6, -121.9) — cuts off 138 cells
- **Door: Sales floor - Staff room (2)** (24.5, -142.3) — cuts off 126 cells
- **Stockroom/B2** (42.8, -186.4) — cuts off 6 cells
- **Stockroom/A2** (47.3, -186.4) — cuts off 12 cells
- **Stockroom/Y** (52.5, -186.4) — cuts off 18 cells
- **Stockroom/Z** (52.1, -182.8) — cuts off 26 cells
- **Pet wall/I** (27.8, -172.8) — cuts off 11 cells
- **Pet wall/F** (27.8, -167.7) — cuts off 22 cells
- **Aisle 7/G** (27.7, -162.4) — cuts off 18 cells
- **Aisle 5/E** (52.9, -162.4) — cuts off 33 cells
- **Aisle 7/E** (42.8, -161.7) — cuts off 18 cells
- **Aisle 5/F** (47.3, -162.4) — cuts off 27 cells
- **Aisle 7/D** (27.8, -157.3) — cuts off 9 cells
- **Aisle 7/A** (42.8, -157.3) — cuts off 9 cells
- **Aisle 4/H** (51.8, -152.6) — cuts off 9 cells
- **Aisle 2/I** (62.3, -152.5) — cuts off 9 cells
- **Aisle 2/H** (67.6, -152.5) — cuts off 18 cells
- **Aisle 2/G** (72.8, -152.5) — cuts off 30 cells
- **Aisle 1/H** (77.3, -152.5) — cuts off 39 cells
- **Aisle 1/F** (81.8, -152.7) — cuts off 48 cells
- **Aisle 6/D** (27.7, -142.2) — cuts off 129 cells
- **Aisle 2/B** (67.6, -142.2) — cuts off 72 cells
- **Bakery/M** (57.8, -137.5) — cuts off 9 cells
- **Produce/T** (62.3, -137.5) — cuts off 18 cells
- **Produce/S** (68.3, -137.6) — cuts off 63 cells
- **Produce/R** (72.8, -137.6) — cuts off 9 cells
- **Produce/O** (62.4, -133.2) — cuts off 27 cells
- **Produce/N** (66.8, -133.1) — cuts off 36 cells
- **Checkout/F** (27.8, -122.7) — cuts off 140 cells

## Region index

<details><summary>All regions with their neighbours (click to expand)</summary>

| region | room | section | centre (x, z) | cells | neighbours (width) |
|---|---|---|---|---|---|
| Door: Backstreet - Stockroom | Doorway | — | 18.1, -175.4 | 2 | Stockroom/N (6), Backstreet/K (3) |
| Door: Sales floor - Staff room | Doorway | — | 24.6, -121.9 | 2 | Checkout/F (3), Staff room/G (6), Staff room/D (2) |
| Door: Sales floor - Staff room (2) | Doorway | — | 24.5, -142.3 | 3 | Staff room/Q (7), Aisle 6/D (3), Staff room/P (2) |
| Door: Sales floor - Stockroom | Doorway | — | 42.1, -175.4 | 2 | Stockroom/I (6), Pet wall/B (3) |
| Door: Sales floor - Stockroom (2) | Doorway | — | 67.6, -175.1 | 2 | Stockroom/E (8), Dairy wall/H (6) |
| Door: Stockroom - Stockroom 2 | Doorway | — | 54.6, -181.9 | 2 | Stockroom/W (3), Stockroom/Z (6), Stockroom/G (2) |
| Doors: Lobby - Sales floor | Doorway | — | 67.6, -120.4 | 4 | Produce/G (10), Lobby/O (8) |
| Doors: Lobby - Sales floor (2) | Doorway | — | 42.8, -120.4 | 6 | Checkout/B (2), Bakery/H (7), Bakery/F (2), Lobby/V (2), Lobby/T (7), Lobby/S (2) |
| Doors: Lobby - Street | Doorway | — | 67.6, -109.9 | 4 | Lobby/D (8), Street/E (10) |
| Doors: Lobby - Street (2) | Doorway | — | 42.8, -109.9 | 6 | Lobby/I (2), Lobby/U (7), Lobby/H (2), Street/J (8), Street/K (1), Street/I (2) |
| Lobby/A | Lobby | — | 82.5, -112.2 | 12 | Lobby/B (7), Lobby/L (10), Lobby/M (1) |
| Lobby/B | Lobby | — | 77.3, -112.2 | 9 | Lobby/C (7), Lobby/M (7), Lobby/N (1), Lobby/A (7), Lobby/L (1) |
| Lobby/C | Lobby | — | 72.8, -112.2 | 9 | Lobby/D (7), Lobby/N (7), Lobby/O (1), Lobby/B (7), Lobby/M (1) |
| Lobby/D | Lobby | — | 67.6, -112.5 | 10 | Lobby/E (7), Lobby/O (10), Lobby/P (1), Lobby/C (7), Lobby/N (1), Doors: Lobby - Street (8) |
| Lobby/E | Lobby | — | 62.3, -112.2 | 9 | Lobby/F (7), Lobby/P (7), Lobby/Q (1), Lobby/D (7), Lobby/O (1) |
| Lobby/F | Lobby | — | 57.8, -112.2 | 9 | Lobby/G (7), Lobby/Q (7), Lobby/R (1), Lobby/E (7), Lobby/P (1) |
| Lobby/G | Lobby | — | 52.6, -112.2 | 12 | Lobby/H (7), Lobby/R (10), Lobby/S (1), Lobby/F (7), Lobby/Q (1) |
| Lobby/H | Lobby | — | 47.3, -112.2 | 9 | Lobby/U (5), Lobby/S (7), Lobby/T (1), Lobby/G (7), Lobby/R (1), Doors: Lobby - Street (2) (2) |
| Lobby/I | Lobby | — | 37.6, -112.2 | 12 | Lobby/J (7), Lobby/V (10), Lobby/W (1), Lobby/U (5), Lobby/T (1), Doors: Lobby - Street (2) (2) |
| Lobby/J | Lobby | — | 32.3, -112.2 | 9 | Lobby/K (7), Lobby/W (7), Lobby/X (1), Lobby/I (7), Lobby/V (1) |
| Lobby/K | Lobby | — | 27.8, -112.2 | 9 | Lobby/X (7), Lobby/J (7), Lobby/W (1) |
| Lobby/L | Lobby | — | 82.5, -117.4 | 16 | Lobby/M (10), Lobby/A (10), Lobby/B (1) |
| Lobby/M | Lobby | — | 77.3, -117.4 | 12 | Lobby/N (10), Lobby/L (10), Lobby/B (7), Lobby/C (1), Lobby/A (1) |
| Lobby/N | Lobby | — | 72.8, -117.4 | 12 | Lobby/O (10), Lobby/M (10), Lobby/C (7), Lobby/D (1), Lobby/B (1) |
| Lobby/O | Lobby | — | 67.6, -117.1 | 14 | Doors: Lobby - Sales floor (8), Lobby/P (10), Lobby/N (10), Lobby/D (10), Lobby/E (1), Lobby/C (1) |
| Lobby/P | Lobby | — | 62.3, -117.4 | 12 | Lobby/Q (10), Lobby/O (10), Lobby/E (7), Lobby/F (1), Lobby/D (1) |
| Lobby/Q | Lobby | — | 57.8, -117.4 | 12 | Lobby/R (10), Lobby/P (10), Lobby/F (7), Lobby/G (1), Lobby/E (1) |
| Lobby/R | Lobby | — | 52.6, -117.4 | 16 | Lobby/S (10), Lobby/Q (10), Lobby/G (10), Lobby/H (1), Lobby/F (1) |
| Lobby/S | Lobby | — | 47.3, -117.4 | 12 | Doors: Lobby - Sales floor (2) (2), Lobby/T (8), Lobby/R (10), Lobby/H (7), Lobby/U (1), Lobby/G (1) |
| Lobby/T | Lobby | — | 42.8, -116.7 | 9 | Lobby/V (8), Doors: Lobby - Sales floor (2) (7), Lobby/S (8), Lobby/U (7), Lobby/I (1), Lobby/H (1) |
| Lobby/U | Lobby | — | 42.8, -112.9 | 6 | Lobby/I (5), Lobby/T (7), Lobby/V (1), Lobby/H (5), Lobby/S (1), Doors: Lobby - Street (2) (7) |
| Lobby/V | Lobby | — | 37.6, -117.4 | 16 | Lobby/W (10), Doors: Lobby - Sales floor (2) (2), Lobby/T (8), Lobby/I (10), Lobby/J (1), Lobby/U (1) |
| Lobby/W | Lobby | — | 32.3, -117.4 | 12 | Lobby/X (10), Lobby/V (10), Lobby/J (7), Lobby/K (1), Lobby/I (1) |
| Lobby/X | Lobby | — | 27.8, -117.4 | 12 | Lobby/W (10), Lobby/K (7), Lobby/J (1) |
| Backstreet/A | Outside | — | 22.5, -157.2 | 12 | Backstreet/C (7), Backstreet/G (10), Backstreet/I (1), Backstreet/B (7), Backstreet/D (1) |
| Backstreet/B | Outside | — | 21.8, -152.7 | 9 | Backstreet/D (7), Backstreet/A (7), Backstreet/C (1) |
| Backstreet/C | Outside | — | 17.3, -157.2 | 9 | Backstreet/E (7), Backstreet/I (7), Backstreet/H (1), Backstreet/A (7), Backstreet/G (1), Backstreet/D (7), Backstreet/F (1), Backstreet/B (1) |
| Backstreet/D | Outside | — | 17.3, -152.7 | 9 | Backstreet/F (7), Backstreet/C (7), Backstreet/E (1), Backstreet/B (7), Backstreet/A (1) |
| Backstreet/E | Outside | — | 12.8, -157.2 | 9 | West alley/I (7), Backstreet/H (7), West alley/K (1), Backstreet/C (7), Backstreet/I (1), Backstreet/F (7), West alley/J (1), Backstreet/D (1) |
| Backstreet/F | Outside | — | 12.8, -152.7 | 9 | West alley/J (7), Backstreet/E (7), West alley/I (1), Backstreet/D (7), Backstreet/C (1) |
| Backstreet/G | Outside | — | 22.5, -161.5 | 12 | Backstreet/I (9), Backstreet/A (10), Backstreet/C (1) |
| Backstreet/H | Outside | — | 12.8, -162.4 | 12 | West alley/K (10), Backstreet/M (7), West alley/L (1), Backstreet/I (10), Backstreet/L (1), Backstreet/E (7), West alley/I (1), Backstreet/C (1) |
| Backstreet/I | Outside | — | 17.5, -162.6 | 13 | Backstreet/H (10), Backstreet/L (8), Backstreet/M (1), Backstreet/G (9), Backstreet/J (2), Backstreet/C (7), Backstreet/E (1), Backstreet/A (1) |
| Backstreet/J | Outside | — | 22.1, -168 | 10 | Backstreet/L (7), Backstreet/N (10), Backstreet/K (1), Backstreet/I (2) |
| Backstreet/K | Outside | — | 17.3, -172.2 | 9 | Backstreet/O (7), Door: Backstreet - Stockroom (2), Backstreet/N (7), Backstreet/L (7), Backstreet/M (1), Backstreet/J (1) |
| Backstreet/L | Outside | — | 17.3, -167.7 | 9 | Backstreet/M (7), Backstreet/K (7), Backstreet/O (1), Backstreet/J (7), Backstreet/N (1), Backstreet/I (8), Backstreet/H (1) |
| Backstreet/M | Outside | — | 12.8, -167.7 | 9 | West alley/L (7), Backstreet/O (7), West alley/M (1), Backstreet/L (7), Backstreet/K (1), Backstreet/H (7), West alley/K (1), Backstreet/I (1) |
| Backstreet/N | Outside | — | 22.5, -172.7 | 16 | Backstreet/K (7), Backstreet/J (10), Backstreet/L (1) |
| Backstreet/O | Outside | — | 12.8, -172.7 | 12 | West alley/N (2), West alley/M (8), Backstreet/K (7), Backstreet/M (7), West alley/L (1), Backstreet/L (1) |
| East alley/A | Outside | — | 87.1, -112.2 | 6 | East alley/B (4), Street/A (4) |
| East alley/B | Outside | — | 87.1, -117.4 | 8 | East alley/C (4), East alley/A (4) |
| East alley/C | Outside | — | 87.1, -122.7 | 6 | East alley/D (4), East alley/B (4) |
| East alley/D | Outside | — | 87.1, -127.2 | 6 | East alley/E (4), East alley/C (4) |
| East alley/E | Outside | — | 87.1, -132.4 | 8 | East alley/F (4), East alley/D (4) |
| East alley/F | Outside | — | 87.1, -137.7 | 6 | East alley/G (4), East alley/E (4) |
| East alley/G | Outside | — | 87.1, -142.2 | 6 | East alley/H (4), East alley/F (4) |
| East alley/H | Outside | — | 87.1, -147.4 | 8 | East alley/I (4), East alley/G (4) |
| East alley/I | Outside | — | 87.1, -152.7 | 6 | East alley/J (4), East alley/H (4) |
| East alley/J | Outside | — | 87.1, -157.2 | 6 | East alley/K (4), East alley/I (4) |
| East alley/K | Outside | — | 87.1, -162.4 | 8 | East alley/L (4), East alley/J (4) |
| East alley/L | Outside | — | 87.1, -167.7 | 6 | East alley/M (4), East alley/K (4) |
| East alley/M | Outside | — | 87.1, -172.2 | 6 | East alley/N (4), East alley/L (4) |
| East alley/N | Outside | — | 87.1, -177.4 | 8 | East alley/O (4), East alley/M (4) |
| East alley/O | Outside | — | 87.1, -182.7 | 6 | East alley/P (4), East alley/N (4) |
| East alley/P | Outside | — | 87.1, -187.2 | 6 | East alley/Q (4), East alley/O (4) |
| East alley/Q | Outside | — | 87.1, -191.7 | 6 | South yard/A (7), East alley/P (4) |
| South yard/A | Outside | — | 82.6, -191.8 | 12 | South yard/B (7), East alley/Q (7) |
| South yard/B | Outside | — | 77.3, -191.8 | 9 | South yard/C (7), South yard/A (7) |
| South yard/C | Outside | — | 72.8, -191.8 | 9 | South yard/D (7), South yard/B (7) |
| South yard/D | Outside | — | 67.6, -191.8 | 12 | South yard/E (7), South yard/C (7) |
| South yard/E | Outside | — | 62.3, -191.8 | 9 | South yard/F (7), South yard/D (7) |
| South yard/F | Outside | — | 57.8, -191.8 | 9 | South yard/G (7), South yard/E (7) |
| South yard/G | Outside | — | 52.6, -191.8 | 12 | South yard/H (7), South yard/F (7) |
| South yard/H | Outside | — | 47.3, -191.8 | 9 | South yard/I (7), South yard/G (7) |
| South yard/I | Outside | — | 42.8, -191.8 | 9 | South yard/J (7), South yard/H (7) |
| South yard/J | Outside | — | 37.6, -191.8 | 12 | South yard/K (7), South yard/I (7) |
| South yard/K | Outside | — | 32.3, -191.8 | 9 | South yard/L (7), South yard/J (7) |
| South yard/L | Outside | — | 27.8, -191.8 | 9 | South yard/M (7), South yard/K (7) |
| South yard/M | Outside | — | 22.6, -191.8 | 12 | South yard/N (7), South yard/L (7) |
| South yard/N | Outside | — | 17.3, -191.8 | 9 | South yard/O (7), South yard/M (7) |
| South yard/O | Outside | — | 12.8, -191.8 | 9 | West alley/Q (7), South yard/N (7) |
| Street/A | Outside | — | 87.1, -108.4 | 4 | Street/B (4), East alley/A (4) |
| Street/B | Outside | — | 82.6, -108.4 | 8 | Street/C (4), Street/A (4) |
| Street/C | Outside | — | 77.3, -108.4 | 6 | Street/D (4), Street/B (4) |
| Street/D | Outside | — | 72.8, -108.4 | 6 | Street/E (4), Street/C (4) |
| Street/E | Outside | — | 67.6, -108.2 | 6 | Doors: Lobby - Street (8), Street/F (4), Street/D (4) |
| Street/F | Outside | — | 62.3, -108.4 | 6 | Street/G (4), Street/E (4) |
| Street/G | Outside | — | 57.8, -108.4 | 6 | Street/H (4), Street/F (4) |
| Street/H | Outside | — | 52.6, -108.4 | 8 | Street/I (4), Street/G (4) |
| Street/I | Outside | — | 47.3, -108.4 | 6 | Doors: Lobby - Street (2) (2), Street/J (2), Street/H (4) |
| Street/J | Outside | — | 42.2, -108 | 4 | Doors: Lobby - Street (2) (8), Street/K (2), Street/I (2) |
| Street/K | Outside | — | 37.3, -108.3 | 7 | Street/L (4), Street/J (3), Doors: Lobby - Street (2) (1) |
| Street/L | Outside | — | 32.3, -108.4 | 6 | Street/M (4), Street/K (4) |
| Street/M | Outside | — | 27.8, -108.4 | 6 | Street/N (4), Street/L (4) |
| Street/N | Outside | — | 22.6, -108.4 | 8 | Street/O (4), Street/M (4) |
| Street/O | Outside | — | 17.3, -108.4 | 6 | Street/P (4), Street/N (4) |
| Street/P | Outside | — | 12.8, -108.4 | 6 | Street/Q (4), Street/O (4) |
| Street/Q | Outside | — | 8.3, -108.4 | 6 | West alley/A (7), Street/P (4) |
| West alley/A | Outside | — | 8.2, -112.2 | 9 | West alley/B (7), Street/Q (7) |
| West alley/B | Outside | — | 8.2, -117.4 | 12 | West alley/C (7), West alley/A (7) |
| West alley/C | Outside | — | 8.2, -122.7 | 9 | West alley/D (7), West alley/B (7) |
| West alley/D | Outside | — | 8.2, -127.2 | 9 | West alley/E (7), West alley/C (7) |
| West alley/E | Outside | — | 8.2, -132.4 | 12 | West alley/F (7), West alley/D (7) |
| West alley/F | Outside | — | 8.2, -137.7 | 9 | West alley/G (7), West alley/E (7) |
| West alley/G | Outside | — | 8.2, -142.2 | 9 | West alley/H (7), West alley/F (7) |
| West alley/H | Outside | — | 8.2, -147.4 | 12 | West alley/J (7), West alley/G (7) |
| West alley/I | Outside | — | 8.3, -157.2 | 9 | West alley/K (7), Backstreet/E (7), Backstreet/H (1), West alley/J (7), Backstreet/F (1) |
| West alley/J | Outside | — | 8.3, -152.7 | 9 | West alley/I (7), Backstreet/F (7), Backstreet/E (1), West alley/H (7) |
| West alley/K | Outside | — | 8.3, -162.4 | 12 | West alley/L (7), Backstreet/H (10), Backstreet/M (1), West alley/I (7), Backstreet/E (1) |
| West alley/L | Outside | — | 8.3, -167.7 | 9 | West alley/M (7), Backstreet/M (7), Backstreet/O (1), West alley/K (7), Backstreet/H (1) |
| West alley/M | Outside | — | 8.3, -172.2 | 9 | West alley/N (7), Backstreet/O (8), West alley/L (7), Backstreet/M (1) |
| West alley/N | Outside | — | 8.2, -177.4 | 12 | West alley/O (7), West alley/M (7), Backstreet/O (2) |
| West alley/O | Outside | — | 8.2, -182.7 | 9 | West alley/P (7), West alley/N (7) |
| West alley/P | Outside | — | 8.2, -187.2 | 9 | West alley/Q (7), West alley/O (7) |
| West alley/Q | Outside | — | 8.3, -191.7 | 9 | South yard/O (7), West alley/P (7) |
| Aisle 1/A | Sales floor | Aisle 1 · Soft Drinks | 81.8, -142.2 | 9 | Aisle 1/B (7), Produce/P (4) |
| Aisle 1/B | Sales floor | Aisle 1 · Soft Drinks | 77.3, -142.3 | 9 | Aisle 2/A (7), Aisle 1/A (7) |
| Aisle 1/C | Sales floor | Aisle 1 · Soft Drinks | 77.3, -147.4 | 6 | Aisle 2/D (4), Aisle 1/D (4) |
| Aisle 1/D | Sales floor | Aisle 1 · Soft Drinks | 81.9, -148.2 | 9 | Aisle 1/F (7), Aisle 1/C (4) |
| Aisle 1/E | Sales floor | Aisle 1 · Soft Drinks | 81.9, -157.2 | 9 | Aisle 1/G (7), Aisle 1/I (7), Aisle 1/F (7) |
| Aisle 1/F | Sales floor | Aisle 1 · Soft Drinks | 81.8, -152.7 | 9 | Aisle 1/H (7), Aisle 1/E (7), Aisle 1/D (7) |
| Aisle 1/G | Sales floor | Aisle 1 · Soft Drinks | 77.3, -157.3 | 9 | Aisle 3/A (7), Aisle 1/E (7) |
| Aisle 1/H | Sales floor | Aisle 1 · Soft Drinks | 77.3, -152.5 | 9 | Aisle 2/G (7), Aisle 1/F (7) |
| Aisle 1/I | Sales floor | Aisle 1 · Soft Drinks | 81.9, -162.4 | 12 | Dairy wall/B (7), Aisle 1/J (4), Aisle 1/E (7) |
| Aisle 1/J | Sales floor | Aisle 1 · Soft Drinks | 77.3, -162.4 | 6 | Aisle 3/D (4), Aisle 1/I (4) |
| Aisle 2/A | Sales floor | Aisle 2 · Snacks & Crisps | 72.8, -142.3 | 9 | Aisle 2/B (7), Aisle 1/B (7) |
| Aisle 2/B | Sales floor | Aisle 2 · Snacks & Crisps | 67.6, -142.2 | 12 | Aisle 2/C (7), Aisle 2/A (7), Produce/S (7) |
| Aisle 2/C | Sales floor | Aisle 2 · Snacks & Crisps | 62.4, -142.3 | 9 | Aisle 2/F (7), Aisle 2/B (7) |
| Aisle 2/D | Sales floor | Aisle 2 · Snacks & Crisps | 72.8, -147.4 | 6 | Aisle 2/E (4), Aisle 1/C (4) |
| Aisle 2/E | Sales floor | Aisle 2 · Snacks & Crisps | 67.6, -147.4 | 8 | Aisle 2/F (4), Aisle 2/D (4) |
| Aisle 2/F | Sales floor | Aisle 2 · Snacks & Crisps | 62.4, -146.7 | 9 | Aisle 4/D (7), Aisle 2/E (4), Aisle 2/C (7) |
| Aisle 2/G | Sales floor | Aisle 2 · Snacks & Crisps | 72.8, -152.5 | 9 | Aisle 2/H (7), Aisle 1/H (7) |
| Aisle 2/H | Sales floor | Aisle 2 · Snacks & Crisps | 67.6, -152.5 | 12 | Aisle 2/I (7), Aisle 2/G (7) |
| Aisle 2/I | Sales floor | Aisle 2 · Snacks & Crisps | 62.3, -152.5 | 9 | Aisle 4/G (7), Aisle 2/H (7) |
| Aisle 3/A | Sales floor | Aisle 3 · Tins & Jars | 72.8, -157.3 | 9 | Aisle 3/B (7), Aisle 1/G (7) |
| Aisle 3/B | Sales floor | Aisle 3 · Tins & Jars | 67.6, -157.3 | 12 | Aisle 3/C (7), Aisle 3/A (7) |
| Aisle 3/C | Sales floor | Aisle 3 · Tins & Jars | 62.3, -157.3 | 9 | Aisle 5/A (7), Aisle 3/B (7) |
| Aisle 3/D | Sales floor | Aisle 3 · Tins & Jars | 72.8, -162.4 | 6 | Aisle 3/E (4), Aisle 1/J (4) |
| Aisle 3/E | Sales floor | Aisle 3 · Tins & Jars | 67.6, -162.4 | 8 | Aisle 3/F (4), Aisle 3/D (4) |
| Aisle 3/F | Sales floor | Aisle 3 · Tins & Jars | 62.3, -162.4 | 6 | Aisle 5/D (4), Aisle 3/E (4) |
| Aisle 4/A | Sales floor | Aisle 4 · Cereal & Breakfast | 57.7, -142.3 | 9 | Aisle 4/B (7), Aisle 4/D (7) |
| Aisle 4/B | Sales floor | Aisle 4 · Cereal & Breakfast | 52.6, -142.3 | 12 | Aisle 4/C (7), Aisle 4/A (7) |
| Aisle 4/C | Sales floor | Aisle 4 · Cereal & Breakfast | 47.4, -142.2 | 9 | Aisle 4/B (7), Bakery/O (7) |
| Aisle 4/D | Sales floor | Aisle 4 · Cereal & Breakfast | 57.8, -146.7 | 9 | Aisle 2/F (7), Aisle 4/A (7) |
| Aisle 4/E | Sales floor | Aisle 4 · Cereal & Breakfast | 47.3, -147.4 | 6 | Aisle 6/E (4), Aisle 4/F (4) |
| Aisle 4/F | Sales floor | Aisle 4 · Cereal & Breakfast | 51.9, -148.2 | 9 | Aisle 4/H (7), Aisle 4/E (4) |
| Aisle 4/G | Sales floor | Aisle 4 · Cereal & Breakfast | 57.8, -152.5 | 9 | Aisle 2/I (7) |
| Aisle 4/H | Sales floor | Aisle 4 · Cereal & Breakfast | 51.8, -152.6 | 9 | Aisle 4/I (7), Aisle 5/B (4), Aisle 4/F (7) |
| Aisle 4/I | Sales floor | Aisle 4 · Cereal & Breakfast | 47.4, -152.5 | 9 | Aisle 4/H (7) |
| Aisle 5/A | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 57.8, -157.3 | 9 | Aisle 5/B (7), Aisle 3/C (7) |
| Aisle 5/B | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 53.3, -157.2 | 9 | Aisle 5/E (7), Aisle 5/A (7), Aisle 4/H (4) |
| Aisle 5/C | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 47.3, -157.3 | 9 | Aisle 7/A (7) |
| Aisle 5/D | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 57.8, -162.4 | 6 | Aisle 5/E (4), Aisle 3/F (4) |
| Aisle 5/E | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 52.9, -162.4 | 14 | Frozen wall/C (7), Aisle 5/F (4), Aisle 5/D (4), Aisle 5/B (7) |
| Aisle 5/F | Sales floor | Aisle 5 · Noodles, Pasta & Rice | 47.3, -162.4 | 6 | Aisle 7/E (4), Aisle 5/E (4) |
| Aisle 6/A | Sales floor | Aisle 6 · Health & Beauty | 42.7, -142.2 | 9 | Aisle 6/E (7), Bakery/P (7) |
| Aisle 6/B | Sales floor | Aisle 6 · Health & Beauty | 37.6, -142.2 | 6 | Aisle 6/F (4), Checkout/J (4) |
| Aisle 6/C | Sales floor | Aisle 6 · Health & Beauty | 32.4, -142.2 | 9 | Aisle 6/G (7), Checkout/K (7) |
| Aisle 6/D | Sales floor | Aisle 6 · Health & Beauty | 27.7, -142.2 | 9 | Aisle 6/H (7), Door: Sales floor - Staff room (2) (2), Checkout/L (7) |
| Aisle 6/E | Sales floor | Aisle 6 · Health & Beauty | 42.8, -147.4 | 12 | Aisle 6/I (7), Aisle 4/E (4), Aisle 6/A (7) |
| Aisle 6/F | Sales floor | Aisle 6 · Health & Beauty | 37.6, -147.4 | 8 | Aisle 6/J (4), Aisle 6/B (4) |
| Aisle 6/G | Sales floor | Aisle 6 · Health & Beauty | 32.4, -147.4 | 12 | Aisle 6/K (7), Aisle 6/C (7) |
| Aisle 6/H | Sales floor | Aisle 6 · Health & Beauty | 27.7, -147.4 | 12 | Aisle 6/L (7), Aisle 6/D (7) |
| Aisle 6/I | Sales floor | Aisle 6 · Health & Beauty | 42.7, -152.5 | 9 | Aisle 6/J (7), Aisle 6/E (7) |
| Aisle 6/J | Sales floor | Aisle 6 · Health & Beauty | 37.6, -152.6 | 12 | Aisle 6/K (7), Aisle 7/B (4), Aisle 6/I (7), Aisle 6/F (4) |
| Aisle 6/K | Sales floor | Aisle 6 · Health & Beauty | 32.3, -152.5 | 9 | Aisle 6/L (7), Aisle 6/J (7), Aisle 6/G (7), Aisle 6/H (1) |
| Aisle 6/L | Sales floor | Aisle 6 · Health & Beauty | 27.8, -152.5 | 9 | Aisle 6/K (7), Aisle 6/H (7), Aisle 6/G (1) |
| Aisle 7/A | Sales floor | Aisle 7 · Household & Cleaning | 42.8, -157.3 | 9 | Aisle 7/E (7), Aisle 5/C (7) |
| Aisle 7/B | Sales floor | Aisle 7 · Household & Cleaning | 37.6, -157.2 | 6 | Aisle 7/F (4), Aisle 6/J (4) |
| Aisle 7/C | Sales floor | Aisle 7 · Household & Cleaning | 32.3, -157.3 | 9 | Aisle 7/D (7) |
| Aisle 7/D | Sales floor | Aisle 7 · Household & Cleaning | 27.8, -157.3 | 9 | Aisle 7/G (7), Aisle 7/C (7) |
| Aisle 7/E | Sales floor | Aisle 7 · Household & Cleaning | 42.8, -161.7 | 9 | Aisle 5/F (4), Aisle 7/A (7) |
| Aisle 7/F | Sales floor | Aisle 7 · Household & Cleaning | 36.8, -162.1 | 9 | Pet wall/A (7), Aisle 7/B (4) |
| Aisle 7/G | Sales floor | Aisle 7 · Household & Cleaning | 27.7, -162.4 | 12 | Pet wall/F (7), Aisle 7/D (7) |
| Bakery/A | Sales floor | Bakery | 57.8, -127.2 | 9 | Bakery/C (7), Bakery/I (7), Produce/J (7), Bakery/B (7), Bakery/D (1), Produce/I (1) |
| Bakery/B | Sales floor | Bakery | 57.8, -122.7 | 9 | Bakery/D (7), Bakery/A (7), Bakery/C (1), Produce/I (7), Produce/J (1) |
| Bakery/C | Sales floor | Bakery | 52.6, -127.2 | 12 | Bakery/E (7), Bakery/A (7), Bakery/D (10), Bakery/F (1), Bakery/B (1) |
| Bakery/D | Sales floor | Bakery | 52.6, -122.7 | 12 | Bakery/F (7), Bakery/C (10), Bakery/E (1), Bakery/B (7), Bakery/A (1) |
| Bakery/E | Sales floor | Bakery | 47.3, -127.2 | 9 | Bakery/G (7), Bakery/C (7), Bakery/F (7), Bakery/H (1), Bakery/D (1) |
| Bakery/F | Sales floor | Bakery | 47.3, -122.7 | 9 | Bakery/H (5), Bakery/E (7), Bakery/G (1), Bakery/D (7), Bakery/C (1), Doors: Lobby - Sales floor (2) (2) |
| Bakery/G | Sales floor | Bakery | 42.8, -127.2 | 9 | Checkout/A (8), Bakery/K (7), Bakery/E (7), Bakery/H (7), Checkout/B (1), Bakery/F (1) |
| Bakery/H | Sales floor | Bakery | 42.8, -123.4 | 6 | Checkout/B (5), Bakery/G (7), Checkout/A (1), Bakery/F (5), Bakery/E (1), Doors: Lobby - Sales floor (2) (7) |
| Bakery/I | Sales floor | Bakery | 57.7, -131.7 | 9 | Bakery/J (4), Bakery/A (7) |
| Bakery/J | Sales floor | Bakery | 52.6, -132.4 | 8 | Bakery/L (4), Bakery/I (4) |
| Bakery/K | Sales floor | Bakery | 42.7, -132.4 | 12 | Bakery/P (7), Checkout/A (2), Bakery/G (7) |
| Bakery/L | Sales floor | Bakery | 47.4, -133.2 | 9 | Bakery/O (7), Bakery/J (4) |
| Bakery/M | Sales floor | Bakery | 57.8, -137.5 | 9 | Bakery/N (7), Produce/T (7) |
| Bakery/N | Sales floor | Bakery | 53.3, -137.5 | 9 | Bakery/M (7) |
| Bakery/O | Sales floor | Bakery | 47.4, -137.7 | 9 | Aisle 4/C (7), Bakery/L (7) |
| Bakery/P | Sales floor | Bakery | 42.7, -137.7 | 9 | Checkout/J (7), Aisle 6/A (7), Bakery/K (7) |
| Checkout/A | Sales floor | Checkout · Sweets & Impulse | 37.8, -127.5 | 14 | Checkout/H (6), Bakery/K (2), Bakery/G (8), Checkout/C (8), Checkout/B (9), Checkout/D (1), Bakery/H (1) |
| Checkout/B | Sales floor | Checkout · Sweets & Impulse | 37.6, -122.6 | 12 | Checkout/D (7), Checkout/A (9), Checkout/C (1), Bakery/H (5), Bakery/G (1), Doors: Lobby - Sales floor (2) (2) |
| Checkout/C | Sales floor | Checkout · Sweets & Impulse | 32.5, -127.4 | 10 | Checkout/H (2), Checkout/I (6), Checkout/A (8), Checkout/E (8), Checkout/D (7), Checkout/F (1), Checkout/B (1) |
| Checkout/D | Sales floor | Checkout · Sweets & Impulse | 32.3, -122.7 | 9 | Checkout/F (7), Checkout/C (7), Checkout/E (1), Checkout/B (7), Checkout/A (1) |
| Checkout/E | Sales floor | Checkout · Sweets & Impulse | 28, -127.4 | 10 | Checkout/I (2), Checkout/G (6), Checkout/C (8), Checkout/F (7), Checkout/D (1) |
| Checkout/F | Sales floor | Checkout · Sweets & Impulse | 27.8, -122.7 | 9 | Checkout/E (7), Checkout/D (7), Checkout/C (1), Door: Sales floor - Staff room (2) |
| Checkout/G | Sales floor | Checkout · Sweets & Impulse | 27, -132.4 | 8 | Checkout/L (4), Checkout/E (6) |
| Checkout/H | Sales floor | Checkout · Sweets & Impulse | 36.8, -132.6 | 11 | Checkout/J (7), Checkout/A (7), Checkout/C (2) |
| Checkout/I | Sales floor | Checkout · Sweets & Impulse | 32.1, -132.6 | 11 | Checkout/K (7), Checkout/E (2), Checkout/C (6) |
| Checkout/J | Sales floor | Checkout · Sweets & Impulse | 37.6, -137.7 | 12 | Checkout/K (7), Aisle 6/B (4), Bakery/P (7), Checkout/H (7), Checkout/I (1) |
| Checkout/K | Sales floor | Checkout · Sweets & Impulse | 32.3, -137.7 | 9 | Checkout/L (7), Aisle 6/C (7), Checkout/J (7), Checkout/I (7), Checkout/H (1) |
| Checkout/L | Sales floor | Checkout · Sweets & Impulse | 27.8, -137.7 | 9 | Aisle 6/D (7), Checkout/K (7), Checkout/G (4) |
| Dairy wall/A | Sales floor | Back Wall · Dairy & Chilled | 82, -172.3 | 10 | Dairy wall/C (7), Dairy wall/B (7), Dairy wall/D (1) |
| Dairy wall/B | Sales floor | Back Wall · Dairy & Chilled | 81.8, -167.7 | 9 | Dairy wall/D (7), Dairy wall/A (7), Dairy wall/C (1), Aisle 1/I (7) |
| Dairy wall/C | Sales floor | Back Wall · Dairy & Chilled | 77.3, -172 | 9 | Dairy wall/E (7), Dairy wall/A (7), Dairy wall/D (7), Dairy wall/F (1), Dairy wall/B (1) |
| Dairy wall/D | Sales floor | Back Wall · Dairy & Chilled | 77.3, -167.7 | 9 | Dairy wall/F (7), Dairy wall/C (7), Dairy wall/E (1), Dairy wall/B (7), Dairy wall/A (1) |
| Dairy wall/E | Sales floor | Back Wall · Dairy & Chilled | 72.8, -172 | 9 | Dairy wall/H (7), Dairy wall/C (7), Dairy wall/F (7), Dairy wall/G (1), Dairy wall/D (1) |
| Dairy wall/F | Sales floor | Back Wall · Dairy & Chilled | 72.8, -167.7 | 9 | Dairy wall/G (7), Dairy wall/E (7), Dairy wall/H (1), Dairy wall/D (7), Dairy wall/C (1) |
| Dairy wall/G | Sales floor | Back Wall · Dairy & Chilled | 67.6, -167.7 | 12 | Dairy wall/J (7), Dairy wall/H (10), Dairy wall/I (1), Dairy wall/F (7), Dairy wall/E (1) |
| Dairy wall/H | Sales floor | Back Wall · Dairy & Chilled | 67.6, -172.2 | 12 | Dairy wall/I (7), Door: Sales floor - Stockroom (2) (4), Dairy wall/E (7), Dairy wall/G (10), Dairy wall/J (1), Dairy wall/F (1) |
| Dairy wall/I | Sales floor | Back Wall · Dairy & Chilled | 62.3, -172 | 9 | Frozen wall/A (7), Dairy wall/H (7), Dairy wall/J (7), Frozen wall/B (1), Dairy wall/G (1) |
| Dairy wall/J | Sales floor | Back Wall · Dairy & Chilled | 62.3, -167.7 | 9 | Frozen wall/B (7), Dairy wall/I (7), Frozen wall/A (1), Dairy wall/G (7), Dairy wall/H (1) |
| Frozen wall/A | Sales floor | Back Wall · Frozen | 57.8, -172 | 9 | Dairy wall/I (7), Frozen wall/B (7), Dairy wall/J (1) |
| Frozen wall/B | Sales floor | Back Wall · Frozen | 57.8, -167.7 | 9 | Frozen wall/C (7), Frozen wall/A (7), Dairy wall/J (7), Dairy wall/I (1) |
| Frozen wall/C | Sales floor | Back Wall · Frozen | 53.3, -167.7 | 9 | Frozen wall/D (4), Frozen wall/B (7), Aisle 5/E (7) |
| Frozen wall/D | Sales floor | Back Wall · Frozen | 51.8, -172 | 9 | Frozen wall/E (7), Frozen wall/C (4) |
| Frozen wall/E | Sales floor | Back Wall · Frozen | 47.3, -172 | 9 | Pet wall/B (7), Frozen wall/D (7), Frozen wall/F (7), Pet wall/C (1) |
| Frozen wall/F | Sales floor | Back Wall · Frozen | 47.3, -167.7 | 9 | Pet wall/C (7), Frozen wall/E (7), Pet wall/B (1) |
| Pet wall/A | Sales floor | Back Wall · Pet | 32.4, -163.2 | 9 | Pet wall/E (7), Aisle 7/F (7), Pet wall/D (1) |
| Pet wall/B | Sales floor | Back Wall · Pet | 42.8, -172.2 | 9 | Pet wall/G (7), Door: Sales floor - Stockroom (2), Frozen wall/E (7), Pet wall/C (7), Pet wall/D (1), Frozen wall/F (1) |
| Pet wall/C | Sales floor | Back Wall · Pet | 42.8, -167.7 | 9 | Pet wall/D (7), Pet wall/B (7), Pet wall/G (1), Frozen wall/F (7), Frozen wall/E (1) |
| Pet wall/D | Sales floor | Back Wall · Pet | 37.6, -167.6 | 12 | Pet wall/E (7), Pet wall/G (7), Pet wall/C (7), Pet wall/B (1) |
| Pet wall/E | Sales floor | Back Wall · Pet | 32.3, -167.5 | 9 | Pet wall/F (7), Pet wall/D (7), Pet wall/A (7), Aisle 7/G (1), Aisle 7/F (1) |
| Pet wall/F | Sales floor | Back Wall · Pet | 27.8, -167.7 | 9 | Pet wall/I (7), Pet wall/E (7), Aisle 7/G (7), Pet wall/A (1) |
| Pet wall/G | Sales floor | Back Wall · Pet | 38.3, -172.7 | 12 | Pet wall/B (7), Pet wall/D (7), Pet wall/C (1) |
| Pet wall/H | Sales floor | Back Wall · Pet | 32.3, -172.9 | 11 | Pet wall/I (10) |
| Pet wall/I | Sales floor | Back Wall · Pet | 27.8, -172.8 | 11 | Pet wall/H (10), Pet wall/F (7) |
| Produce/A | Sales floor | Produce · Fruit & Veg | 82.5, -122.7 | 12 | Produce/D (7), Produce/B (10), Produce/C (1) |
| Produce/B | Sales floor | Produce · Fruit & Veg | 82.5, -127.2 | 12 | Produce/C (7), Produce/A (10), Produce/D (1) |
| Produce/C | Sales floor | Produce · Fruit & Veg | 77.3, -127.2 | 9 | Produce/F (7), Produce/B (7), Produce/D (7), Produce/E (1), Produce/A (1) |
| Produce/D | Sales floor | Produce · Fruit & Veg | 77.3, -122.7 | 9 | Produce/E (7), Produce/C (7), Produce/F (1), Produce/A (7), Produce/B (1) |
| Produce/E | Sales floor | Produce · Fruit & Veg | 72.8, -122.7 | 9 | Produce/G (7), Produce/F (7), Produce/H (1), Produce/D (7), Produce/C (1) |
| Produce/F | Sales floor | Produce · Fruit & Veg | 72.8, -127.2 | 9 | Produce/H (7), Produce/L (7), Produce/C (7), Produce/E (7), Produce/G (1), Produce/D (1) |
| Produce/G | Sales floor | Produce · Fruit & Veg | 67.6, -123 | 10 | Produce/I (7), Produce/H (10), Produce/J (1), Produce/E (7), Produce/F (1), Doors: Lobby - Sales floor (8) |
| Produce/H | Sales floor | Produce · Fruit & Veg | 67.6, -127.2 | 12 | Produce/J (7), Produce/F (7), Produce/G (10), Produce/I (1), Produce/E (1) |
| Produce/I | Sales floor | Produce · Fruit & Veg | 62.3, -122.7 | 9 | Bakery/B (7), Produce/J (7), Bakery/A (1), Produce/G (7), Produce/H (1) |
| Produce/J | Sales floor | Produce · Fruit & Veg | 62.3, -127.2 | 9 | Bakery/A (7), Produce/H (7), Produce/I (7), Bakery/B (1), Produce/G (1) |
| Produce/K | Sales floor | Produce · Fruit & Veg | 77.3, -132.4 | 6 | Produce/L (4), Produce/M (4) |
| Produce/L | Sales floor | Produce · Fruit & Veg | 72.8, -131.7 | 9 | Produce/K (4), Produce/F (7) |
| Produce/M | Sales floor | Produce · Fruit & Veg | 82, -133 | 8 | Produce/P (4), Produce/K (4) |
| Produce/N | Sales floor | Produce · Fruit & Veg | 66.8, -133.1 | 9 | Produce/O (7), Produce/S (4) |
| Produce/O | Sales floor | Produce · Fruit & Veg | 62.4, -133.2 | 9 | Produce/T (7), Produce/N (7) |
| Produce/P | Sales floor | Produce · Fruit & Veg | 82.6, -137.7 | 6 | Aisle 1/A (4), Produce/M (4) |
| Produce/Q | Sales floor | Produce · Fruit & Veg | 77.3, -137.6 | 9 | Produce/R (7) |
| Produce/R | Sales floor | Produce · Fruit & Veg | 72.8, -137.6 | 9 | Produce/S (7), Produce/Q (7) |
| Produce/S | Sales floor | Produce · Fruit & Veg | 68.3, -137.6 | 9 | Aisle 2/B (7), Produce/R (7), Produce/N (4) |
| Produce/T | Sales floor | Produce · Fruit & Veg | 62.3, -137.5 | 9 | Bakery/M (7), Produce/O (7) |
| Staff room/M | Staff room | — | 22.5, -133.2 | 12 | Staff room/N (7), Staff room/P (10), Staff room/R (1) |
| Staff room/N | Staff room | — | 17.3, -133.2 | 9 | Staff room/O (7), Staff room/R (7), Staff room/T (1), Staff room/M (7), Staff room/P (1) |
| Staff room/O | Staff room | — | 12.8, -133.2 | 9 | Staff room/T (7), Staff room/N (7), Staff room/R (1) |
| Staff room/P | Staff room | — | 22.5, -137.7 | 12 | Staff room/R (7), Staff room/Q (8), Staff room/S (1), Door: Sales floor - Staff room (2) (2), Staff room/M (10), Staff room/N (1) |
| Staff room/Q | Staff room | — | 21.8, -142.1 | 9 | Staff room/S (7), Door: Sales floor - Staff room (2) (7), Staff room/X (2), Staff room/P (8), Staff room/R (1) |
| Staff room/R | Staff room | — | 17.3, -137.7 | 9 | Staff room/T (7), Staff room/S (7), Staff room/U (1), Staff room/P (7), Staff room/Q (1), Staff room/N (7), Staff room/O (1), Staff room/M (1) |
| Staff room/S | Staff room | — | 17.3, -142.2 | 9 | Staff room/U (7), Staff room/V (4), Staff room/W (1), Staff room/Q (7), Staff room/R (7), Staff room/T (1), Staff room/P (1) |
| Staff room/T | Staff room | — | 12.8, -137.7 | 9 | Staff room/U (7), Staff room/R (7), Staff room/S (1), Staff room/O (7), Staff room/N (1) |
| Staff room/U | Staff room | — | 12.8, -142.2 | 9 | Staff room/W (7), Staff room/S (7), Staff room/V (1), Staff room/T (7), Staff room/R (1) |
| Staff room/V | Staff room | — | 17.3, -147.4 | 12 | Staff room/W (10), Staff room/X (10), Staff room/S (4), Staff room/U (1) |
| Staff room/W | Staff room | — | 12.8, -147.4 | 12 | Staff room/V (10), Staff room/U (7), Staff room/S (1) |
| Staff room/X | Staff room | — | 22.3, -147.7 | 15 | Staff room/V (10), Staff room/Q (2) |
| Staff room/A | Staff room 2 | — | 22.5, -112.2 | 12 | Staff room/B (7), Staff room/D (10), Staff room/E (1) |
| Staff room/B | Staff room 2 | — | 17.3, -112.2 | 9 | Staff room/C (7), Staff room/E (7), Staff room/F (1), Staff room/A (7), Staff room/D (1) |
| Staff room/C | Staff room 2 | — | 12.8, -112.2 | 9 | Staff room/F (7), Staff room/B (7), Staff room/E (1) |
| Staff room/D | Staff room 2 | — | 22.5, -117.4 | 16 | Staff room/E (10), Staff room/G (8), Staff room/H (1), Door: Sales floor - Staff room (2), Staff room/A (10), Staff room/B (1) |
| Staff room/E | Staff room 2 | — | 17.3, -117.4 | 12 | Staff room/F (10), Staff room/H (7), Staff room/I (1), Staff room/D (10), Staff room/G (1), Staff room/B (7), Staff room/C (1), Staff room/A (1) |
| Staff room/F | Staff room 2 | — | 12.8, -117.4 | 12 | Staff room/I (7), Staff room/E (10), Staff room/H (1), Staff room/C (7), Staff room/B (1) |
| Staff room/G | Staff room 2 | — | 22.1, -122.8 | 10 | Staff room/H (7), Staff room/J (10), Staff room/K (1), Door: Sales floor - Staff room (6), Staff room/D (8), Staff room/E (1) |
| Staff room/H | Staff room 2 | — | 17.3, -122.7 | 9 | Staff room/I (7), Staff room/K (7), Staff room/L (1), Staff room/G (7), Staff room/J (1), Staff room/E (7), Staff room/F (1), Staff room/D (1) |
| Staff room/I | Staff room 2 | — | 12.8, -122.7 | 9 | Staff room/L (7), Staff room/H (7), Staff room/K (1), Staff room/F (7), Staff room/E (1) |
| Staff room/J | Staff room 2 | — | 22.5, -127.8 | 16 | Staff room/K (10), Staff room/G (10), Staff room/H (1) |
| Staff room/K | Staff room 2 | — | 17.3, -127.8 | 12 | Staff room/L (10), Staff room/J (10), Staff room/H (7), Staff room/I (1), Staff room/G (1) |
| Staff room/L | Staff room 2 | — | 12.8, -127.8 | 12 | Staff room/K (10), Staff room/I (7), Staff room/H (1) |
| Stockroom/A | Stockroom | — | 77.3, -177.4 | 6 | Stockroom/B (4), Stockroom/D (4) |
| Stockroom/B | Stockroom | — | 72.8, -177.4 | 6 | Stockroom/E (4), Stockroom/A (4) |
| Stockroom/C | Stockroom | — | 62.3, -177.5 | 6 | Stockroom/F (4), Stockroom/E (4) |
| Stockroom/D | Stockroom | — | 82.1, -178 | 8 | Stockroom/Q (4), Stockroom/A (4) |
| Stockroom/E | Stockroom | — | 67.6, -177.8 | 14 | Stockroom/T (10), Stockroom/C (4), Stockroom/B (4), Door: Sales floor - Stockroom (2) (8) |
| Stockroom/F | Stockroom | — | 57.6, -178 | 8 | Stockroom/W (4), Stockroom/C (4) |
| Stockroom/K2 | Stockroom | — | 77.3, -187.8 | 6 | Stockroom/L2 (4), Stockroom/P (4) |
| Stockroom/L2 | Stockroom | — | 72.8, -187.8 | 6 | Stockroom/U (4), Stockroom/K2 (4) |
| Stockroom/M2 | Stockroom | — | 62.3, -187.8 | 6 | Stockroom/X (4), Stockroom/U (4) |
| Stockroom/P | Stockroom | — | 82.1, -187.3 | 8 | Stockroom/K2 (4), Stockroom/Q (4) |
| Stockroom/Q | Stockroom | — | 81.9, -182.7 | 9 | Stockroom/R (7), Stockroom/P (4), Stockroom/D (4) |
| Stockroom/R | Stockroom | — | 77.3, -182.5 | 9 | Stockroom/S (7), Stockroom/Q (7) |
| Stockroom/S | Stockroom | — | 72.8, -182.5 | 9 | Stockroom/T (7), Stockroom/R (7) |
| Stockroom/T | Stockroom | — | 67.6, -182.7 | 12 | Stockroom/V (7), Stockroom/U (10), Stockroom/S (7), Stockroom/E (10) |
| Stockroom/U | Stockroom | — | 67.6, -187.1 | 12 | Stockroom/M2 (4), Stockroom/L2 (4), Stockroom/T (10) |
| Stockroom/V | Stockroom | — | 62.3, -182.6 | 9 | Stockroom/W (7), Stockroom/T (7) |
| Stockroom/W | Stockroom | — | 57.8, -182.6 | 9 | Stockroom/X (4), Stockroom/V (7), Door: Stockroom - Stockroom 2 (2), Stockroom/F (4) |
| Stockroom/X | Stockroom | — | 57.6, -187.3 | 8 | Stockroom/M2 (4), Stockroom/W (4) |
| Stockroom/A2 | Stockroom 2 | — | 47.3, -186.4 | 6 | Stockroom/B2 (4), Stockroom/Y (4) |
| Stockroom/B2 | Stockroom 2 | — | 42.8, -186.4 | 6 | Stockroom/C2 (4), Stockroom/A2 (4) |
| Stockroom/C2 | Stockroom 2 | — | 38.4, -186.4 | 6 | Stockroom/B2 (4) |
| Stockroom/D2 | Stockroom 2 | — | 32.3, -182.6 | 9 | Stockroom/E2 (7), Stockroom/K (7) |
| Stockroom/E2 | Stockroom 2 | — | 31.9, -186.3 | 5 | Stockroom/F2 (4), Stockroom/D2 (7) |
| Stockroom/F2 | Stockroom 2 | — | 27.8, -186.4 | 6 | Stockroom/G2 (4), Stockroom/E2 (4) |
| Stockroom/G | Stockroom 2 | — | 52.5, -178.9 | 8 | Stockroom/H (4), Stockroom/Z (8), Door: Stockroom - Stockroom 2 (2) |
| Stockroom/G2 | Stockroom 2 | — | 22.6, -186.4 | 8 | Stockroom/H2 (4), Stockroom/F2 (4) |
| Stockroom/H | Stockroom 2 | — | 47.3, -178.9 | 6 | Stockroom/I (4), Stockroom/G (4) |
| Stockroom/H2 | Stockroom 2 | — | 17.3, -186.4 | 6 | Stockroom/I2 (4), Stockroom/G2 (4) |
| Stockroom/I | Stockroom 2 | — | 42.8, -177.8 | 11 | Stockroom/J (6), Stockroom/H (4), Door: Sales floor - Stockroom (6) |
| Stockroom/I2 | Stockroom 2 | — | 12.8, -186.4 | 6 | Stockroom/H2 (4), Stockroom/J2 (7) |
| Stockroom/J | Stockroom 2 | — | 37.6, -178.9 | 8 | Stockroom/K (4), Stockroom/I (6) |
| Stockroom/J2 | Stockroom 2 | — | 12.8, -182.7 | 9 | Stockroom/I2 (7), Stockroom/O (7) |
| Stockroom/K | Stockroom 2 | — | 32.3, -178.9 | 6 | Stockroom/L (4), Stockroom/D2 (7), Stockroom/J (4) |
| Stockroom/L | Stockroom 2 | — | 27.8, -178.9 | 6 | Stockroom/M (4), Stockroom/K (4) |
| Stockroom/M | Stockroom 2 | — | 22.6, -178.9 | 8 | Stockroom/N (4), Stockroom/L (4) |
| Stockroom/N | Stockroom 2 | — | 17.2, -177.9 | 10 | Stockroom/O (4), Stockroom/M (4), Door: Backstreet - Stockroom (6) |
| Stockroom/O | Stockroom 2 | — | 12.6, -178.5 | 8 | Stockroom/J2 (7), Stockroom/N (4) |
| Stockroom/Y | Stockroom 2 | — | 52.5, -186.4 | 8 | Stockroom/A2 (4), Stockroom/Z (10) |
| Stockroom/Z | Stockroom 2 | — | 52.1, -182.8 | 10 | Stockroom/Y (10), Door: Stockroom - Stockroom 2 (6), Stockroom/G (8) |

</details>
