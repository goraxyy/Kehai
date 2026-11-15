# Kehai — Store Map

The building as Karen and the eval harness see it. **Generated** from the scene by
`Kehai/Map/Export STORE_MAP.md` (`StoreMap.cs` + `StoreMapReport.cs`) — do not edit by
hand; move a shelf and re-export instead.

**4778 cells, 563 regions in 8 rooms, 1438 links, 18 chokepoints, 69 landmarks, 207 bays (191 ms)**

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
   -52
   -54 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -55 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -57 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -58 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -60 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -61 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -63 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -64 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -66 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -67 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -69 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -70 ~~~~~~~~~~~~~~~~~~~~~~~~~~   ~~~~~~~  ~~~~~   ~~~~~~~~~
   -72 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -73 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -75 ~~~~~~~~~~~~~~~~uu~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~u
   -76 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -78 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~u~
   -79 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -81 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -82 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -84 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -85 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -87 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -88 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -90 ~~~~~~~~~~~~~~~~  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -91 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -93 ~~~~~~~~~~~~~~~~~~~~~~~~~~   ~~~~~~~~~~~~~~~~~~~~~~~~~~
   -94 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -96 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -97 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
   -99 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  -100 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  -102 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  -103 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  -105 ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
  -106 ~~~:::::::::::::::::::::::::::::::::::::@::::::::::::~~
  -108 ~~~::::::::::::::::::::::::::::::::::::::::::::::::::~~
  -109 ~~~:::::::::::::::uu:::+++::::::::::::::++::uu:::::::~~
  -111 ___::::::::  lllllluull+++llllllllllllll++llllllll   __
  -112 ___::::::::: lllllllllllllllllllllllllllllllllllll   __
  -114 ___::::::::::llllllllllllllllllllllllllllllllllllllll__
  -115 ___::::::::::llllllllllllllllllllllllllllllllllllllll__
  -117 ___::u   u::+lllllllllllllllllllllllllllllllllllll   __
  -118 ___::::::::::llllllllllllllllullllllllllllllllllll   __
  -120 ___:::::::::olllll  lll++ll   ulllllllll++u ulllll   __
  -121 ___::::::::: WWWuWWWWWW+++BBBBBuuBBBPPPP++PPPPPuuPPPP__
  -123 ___::::::::: WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__
  -124 ___::::::::::WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__
  -126 ___::::::::::WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__
  -127 ___::::::::u WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPPP__
  -129 ___:::   :::uWWWWWWWWWWBBBBB         PPPPPPP         __
  -130 ___:::   :::+WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -132 ___::::::::::WW$WW$ WW $BBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -133 ___::::::::::WWWWWW WW BBBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -135 ___::::::::::WWWWWW WW BBBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -136 ___: : ::::++WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -138 ___        ++WWWWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPPPPPPP __
  -139 ___rrrrrrrrrtWWWWWW WW BBBBBBBBBBBBBPPPPPPPPPPPP PPP __
  -141 ___rrrrrrrrrrHHHWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPDD DDD __
  -142 ___rrr   rrr+HHHWWWWWWWBBBBBBBBBBBBBPPPPPPPPPPDD DDD __
  -144 ___rrrrrrrrr+HHHWW                            DD DDD __
  -145 ___rrrrrrrrrrHHHHH HHHHHHHCCCCCCCCCCSSSSSSSSSSDD DDD __
  -147 ___mrr rrrrrrHHHHH HHHHHHHCCCCCCCCCCSSSSSSSSSSDD DDD __
  -148 ___crrrrrrrrrHHHHH HHHHHHHCCCCCCCCCCSS SSSSSSSDD DDD __
  -150 ___ox u++rrrrHHHHH HHHHHHCCCCCCCCCCCSS SSSSSSSDD DDD __
  -151 ___bbbb++bbbeHHHHH HHHHHHCCCCCCCCCCCSS SSSSSSSDD DDD __
  -153 ___bbbbbbbbb HHHHH HHHHHHCCCCCCCCCCCSS SSSSSSSDD DDD __
  -154 ___bbbbbbbbbbHHHHH HHHHHHCCCCNNNNNNNSS SSSSSSSDD DDD __
  -156 ___bbbbbbbb  KKKKK KKKKKKNNNNNNNNNNNTT TTTTTTTDD DDD __
  -157 ___bbbbbbbk  KKKKK KKKKKKNNNNNNNNNNNTT TTTTTTTDD DDD __
  -159 ___bbbbbbbb  KKKKK KKKKKKNNN           TTTTTTTDD DDD __
  -160 ___bbbbbbbbbbKKKKKKKKKKKKKNNNNNNNNNNTTTTTTTTTMDD DDD __
  -162 ___bbbbbbbb  KKKKKKKKKKKKKNNNNNNNNNNTTTTTTTTTMDD DDD __
  -163 ___bbbbbbbk  KKKKKKKKKKKKKNNNNNNNNNNTTTTTTTTTMDD DDD __
  -165 ___bbbbbbbb  KKKKKKAAAAAAAFFFFFFFFFFMMMMMMMMMMDD DDD __
  -166 ___bbbbbbbbbbAAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -168 ___bbbbbbbb  AAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -169 ___bbbbbbbk  AAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -171 ___bbbbbbbb  AAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -172 ___bbbbbbbbbbAAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -174 ___bbbbbbbbbbAAAAAAAAAAAAAFFFFFFFFFFMMMMMMMMMMMMMMMM __
  -175 ___   s++             s++s         c   c++ c    c    __
  -177 ___   sss             ssss       ccccccccccccccccccc __
  -178 ___ssssssssssssssssssssssssssssssccccccccccccccccccc __
  -180 ___ssssssssssssssssssssssssssssssccccccccccccccccccc __
  -181 ___sss          sss          sss+ccccccccccccccccccc __
  -183 ___sss          sss          sss+cc     cccccc    cc __
  -184 ___sss          sss          ssssccccccccccccccccccc __
  -186 ___ssssssssssssssss sssssssssssssccccccccccccccccccc __
  -187  __sssssssssssssss  sssssssssssssccccccccccccccccccc __
  -189 ___                              ccccccccccccccccccc __
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
| `P` | Aisle 1 · Fruit & Veg | `B` | Aisle 2 · Bakery |
| `W` | Aisle 13 · Sweets | `D` | Aisle 3 · Soft Drinks |
| `S` | Aisle 4 · Snacks & Crisps | `T` | Aisle 5 · Tins & Jars |
| `C` | Aisle 6 · Cereal & Breakfast | `N` | Aisle 7 · Noodles, Pasta & Rice |
| `H` | Aisle 8 · Health & Beauty | `K` | Aisle 9 · Household & Cleaning |
| `M` | Aisle 10 · Dairy & Chilled | `F` | Aisle 11 · Frozen |
| `A` | Aisle 12 · Pet | | |
| `$` | checkout till | `c` | coffee machine |
| `t` | time clock | `e` | breaker box |
| `m` | store radio | `o` | mop rack |
| `x` | stock pallet | `u` | bin |
| `k` | skip | `@` | customer spawn |

## Rooms

| room | regions | cells | area (m²) | doors |
|---|---|---|---|---|
| Outside | 270 | 2633 | 5924.3 | Door: Backstreet - Staff room; Door: Backstreet - Stockroom; Doors: Lobby - Pavement; Doors: Lobby - Pavement (2) |
| Cold storage | 20 | 166 | 373.5 | Door: Cold storage - Sales floor; Door: Cold storage - Stockroom |
| Stockroom | 21 | 155 | 348.8 | Door: Backstreet - Stockroom; Door: Cold storage - Stockroom; Door: Sales floor - Stockroom |
| Sales floor | 175 | 1305 | 2936.3 | Door: Cold storage - Sales floor; Door: Office - Sales floor; Door: Sales floor - Staff room; Door: Sales floor - Stockroom; Doors: Lobby - Sales floor; Doors: Lobby - Sales floor (2) |
| Staff room | 11 | 71 | 159.8 | Door: Backstreet - Staff room; Door: Office - Staff room; Door: Sales floor - Staff room |
| Office | 14 | 77 | 173.3 | Door: Office - Sales floor; Door: Office - Staff room |
| WC | 14 | 81 | 182.3 | Door: Lobby - WC |
| Lobby | 25 | 250 | 562.5 | Door: Lobby - WC; Doors: Lobby - Pavement; Doors: Lobby - Pavement (2); Doors: Lobby - Sales floor; Doors: Lobby - Sales floor (2) |

## Doorways

| doorway | at (x, z) | width (links) | chokepoint |
|---|---|---|---|
| Door: Backstreet - Staff room | 18.1, -150.3 | 17 | no |
| Door: Backstreet - Stockroom | 18.1, -175.4 | 9 | no |
| Door: Office - Staff room | 23.8, -136.9 | 7 | no |
| Door: Sales floor - Staff room | 24.6, -143 | 11 | no |
| Door: Office - Sales floor | 24.6, -129.4 | 9 | no |
| Door: Lobby - WC | 24.8, -116.7 | 8 | **yes** |
| Doors: Lobby - Sales floor | 42.6, -120.6 | 15 | no |
| Doors: Lobby - Pavement | 42.8, -109.9 | 22 | no |
| Door: Sales floor - Stockroom | 42.1, -175.4 | 11 | no |
| Door: Cold storage - Stockroom | 54.6, -181.9 | 11 | no |
| Doors: Lobby - Sales floor (2) | 67.6, -120.4 | 16 | no |
| Doors: Lobby - Pavement (2) | 67.6, -109.9 | 16 | no |
| Door: Cold storage - Sales floor | 67.5, -175 | 8 | no |

## Landmarks

| landmark | kind | at (x, z) | region |
|---|---|---|---|
| Bin 1 | Bin | 51.5, -119.7 | Lobby/R |
| Bin 10 | Bin | 19.5, -116.7 | WC/D |
| Bin 11 | Bin | 15.2, -116.1 | WC/G |
| Bin 12 | Bin | 50.5, -119.7 | Lobby/R |
| Bin 13 | Bin | 31.5, -120.4 | Aisle 13/C |
| Bin 14 | Bin | 35, -110.4 | Lobby/H |
| Bin 15 | Bin | 36, -110.4 | Lobby/H |
| Bin 16 | Bin | 14.8, -149.7 | Staff room/H |
| Bin 17 | Bin | 51, -119.7 | Lobby/R |
| Bin 18 | Bin | 14.3, -149.7 | Staff room/J |
| Bin 19 | Bin | 87, -75.6 | Street/I4 |
| Bin 2 | Bin | 54.5, -120.4 | Aisle 2/D |
| Bin 20 | Bin | 19.5, -116.1 | WC/D |
| Bin 21 | Bin | 53.5, -120.4 | Aisle 2/D |
| Bin 22 | Bin | 15.2, -117.2 | WC/G |
| Bin 23 | Bin | 78.5, -120.4 | Aisle 1/D |
| Bin 24 | Bin | 35.5, -110.4 | Lobby/H |
| Bin 25 | Bin | 73.2, -109.5 | Pavement/E |
| Bin 26 | Bin | 15.3, -149.7 | Staff room/H |
| Bin 27 | Bin | 54, -120.4 | Aisle 2/D |
| Bin 28 | Bin | 74.2, -109.5 | Pavement/E |
| Bin 29 | Bin | 34.5, -109.5 | Pavement/K |
| Bin 3 | Bin | 15.2, -116.7 | WC/G |
| Bin 30 | Bin | 86.5, -75.6 | Street/R3 |
| Bin 31 | Bin | 35, -109.5 | Pavement/J |
| Bin 32 | Bin | 31.5, -75.6 | Street/Y3 |
| Bin 33 | Bin | 86, -75.6 | Street/R3 |
| Bin 34 | Bin | 31, -75.6 | Street/Y3 |
| Bin 35 | Bin | 31, -120.4 | Aisle 13/C |
| Bin 36 | Bin | 78, -120.4 | Aisle 1/D |
| Bin 37 | Bin | 77.5, -120.4 | Aisle 1/D |
| Bin 38 | Bin | 73.7, -109.5 | Pavement/E |
| Bin 39 | Bin | 24.7, -127.8 | Door: Office - Sales floor |
| Bin 4 | Bin | 35.5, -109.5 | Pavement/J |
| Bin 40 | Bin | 19.5, -117.2 | WC/D |
| Bin 41 | Bin | 30.5, -120.4 | Aisle 13/C |
| Bin 42 | Bin | 32, -75.6 | Street/Y3 |
| Bin 5 | Bin | 71.5, -119.7 | Lobby/N |
| Bin 6 | Bin | 24.7, -127.3 | Office/B |
| Bin 7 | Bin | 71, -119.7 | Lobby/O |
| Bin 8 | Bin | 24.7, -126.8 | Office/B |
| Bin 9 | Bin | 72, -119.7 | Lobby/N |
| Breaker box | BreakerBox | 24.5, -152.1 | Backstreet/A |
| Checkout 1 | Checkout | 29.9, -131.5 | Aisle 13/J |
| Checkout 2 | Checkout | 34.9, -131.5 | Aisle 13/I |
| Checkout 3 | Checkout | 39.9, -131.5 | Aisle 2/L |
| Coffee machine | CoffeeMachine | 10.6, -148 | Staff room/J |
| Customer spawn | CustomerSpawn | 67.5, -102.5 | Pavement/A |
| Mop rack | MopHome | 11.3, -149.7 | Staff room/J |
| Mop rack | MopHome | 24.6, -120.5 | WC/D |
| Store radio | Radio | 10.4, -146.7 | Staff room/J |
| Stock pallet | StockCrateHome | 12.6, -149.4 | Staff room/J |
| Time clock | TimeClock | 24.9, -139.3 | Staff room/A |
| Skip | TrashSkip | 23.9, -163 | Backstreet/G |
| Skip | TrashSkip | 23.9, -157 | Backstreet/B |
| Skip | TrashSkip | 23.9, -169 | Backstreet/J |

## Planogram sections

Which regions each section of the sales floor covers — where to send a customer asking
for something. `docs/agents/store/STORE.md` says what each section sells.

| section | bays | regions |
|---|---|---|
| Aisle 1 · Fruit & Veg | 30 | Aisle 1/C2, Aisle 1/A2, Aisle 1/Y, Aisle 1/B2, Aisle 1/Z, Aisle 1/X, Aisle 1/W, Aisle 1/V, Aisle 1/U, Aisle 1/T, Aisle 1/S, Aisle 1/R, Aisle 1/O, Aisle 1/N, Aisle 1/M, Aisle 1/Q, Aisle 1/P, Aisle 1/L, Aisle 1/K, Aisle 1/I, Aisle 1/G, Aisle 1/F, Aisle 1/C, Aisle 1/B, Aisle 1/J, Aisle 1/H, Aisle 1/E, Aisle 1/D, Aisle 1/A |
| Aisle 2 · Bakery | 13 | Aisle 2/Y, Aisle 2/W, Aisle 2/T, Aisle 2/S, Aisle 2/X, Aisle 2/V, Aisle 2/U, Aisle 2/R, Aisle 2/Q, Aisle 2/P, Aisle 2/N, Aisle 2/L, Aisle 2/K, Aisle 2/O, Aisle 2/M, Aisle 2/J, Aisle 2/I, Aisle 2/G, Aisle 2/F, Aisle 2/C, Aisle 2/B, Aisle 2/H, Aisle 2/E, Aisle 2/D, Aisle 2/A |
| Aisle 13 · Sweets | 10 | Aisle 13/N, Aisle 13/K, Aisle 13/O, Aisle 13/M, Aisle 13/L, Aisle 13/G, Aisle 13/J, Aisle 13/I, Aisle 13/H, Aisle 13/D, Aisle 13/B, Aisle 13/F, Aisle 13/E, Aisle 13/C, Aisle 13/A |
| Aisle 3 · Soft Drinks | 21 | Aisle 3/J, Aisle 3/I, Aisle 3/G, Aisle 3/E, Aisle 3/H, Aisle 3/F, Aisle 3/D, Aisle 3/C, Aisle 3/B, Aisle 3/A |
| Aisle 4 · Snacks & Crisps | 10 | Aisle 4/G, Aisle 4/F, Aisle 4/E, Aisle 4/D, Aisle 4/C, Aisle 4/B, Aisle 4/A |
| Aisle 5 · Tins & Jars | 8 | Aisle 5/G, Aisle 5/F, Aisle 5/E, Aisle 5/C, Aisle 5/B, Aisle 5/A, Aisle 5/D |
| Aisle 6 · Cereal & Breakfast | 12 | Aisle 6/K, Aisle 6/J, Aisle 6/F, Aisle 6/D, Aisle 6/I, Aisle 6/H, Aisle 6/G, Aisle 6/E, Aisle 6/C, Aisle 6/B, Aisle 6/A |
| Aisle 7 · Noodles, Pasta & Rice | 10 | Aisle 7/I, Aisle 7/H, Aisle 7/G, Aisle 7/F, Aisle 7/E, Aisle 7/C, Aisle 7/A, Aisle 7/D, Aisle 7/B |
| Aisle 8 · Health & Beauty | 14 | Aisle 8/K, Aisle 8/J, Aisle 8/I, Aisle 8/H, Aisle 8/G, Aisle 8/F, Aisle 8/E, Aisle 8/D, Aisle 8/C, Aisle 8/B, Aisle 8/A |
| Aisle 9 · Household & Cleaning | 10 | Aisle 9/J, Aisle 9/I, Aisle 9/H, Aisle 9/G, Aisle 9/F, Aisle 9/E, Aisle 9/D, Aisle 9/C, Aisle 9/B, Aisle 9/A |
| Aisle 10 · Dairy & Chilled | 31 | Aisle 10/O, Aisle 10/N, Aisle 10/L, Aisle 10/I, Aisle 10/G, Aisle 10/P, Aisle 10/M, Aisle 10/K, Aisle 10/J, Aisle 10/H, Aisle 10/E, Aisle 10/D, Aisle 10/B, Aisle 10/F, Aisle 10/C, Aisle 10/A |
| Aisle 11 · Frozen | 15 | Aisle 11/J, Aisle 11/I, Aisle 11/F, Aisle 11/K, Aisle 11/H, Aisle 11/G, Aisle 11/D, Aisle 11/A, Aisle 11/E, Aisle 11/C, Aisle 11/B |
| Aisle 12 · Pet | 23 | Aisle 12/M, Aisle 12/K, Aisle 12/I, Aisle 12/G, Aisle 12/N, Aisle 12/L, Aisle 12/J, Aisle 12/H, Aisle 12/F, Aisle 12/E, Aisle 12/C, Aisle 12/A, Aisle 12/D, Aisle 12/B |

## Chokepoints

Regions whose loss cuts part of the store off. Karen blocks these first; a player who
knows them knows where not to be cornered.

- **Door: Lobby - WC** (24.8, -116.7) — cuts off 81 cells
- **Stockroom/M** (42.8, -186.4) — cuts off 6 cells
- **Stockroom/L** (47.3, -186.4) — cuts off 12 cells
- **Stockroom/J** (52.5, -186.4) — cuts off 18 cells
- **Stockroom/K** (52.1, -182.8) — cuts off 26 cells
- **Staff room/J** (12.8, -147.1) — cuts off 1 cells
- **Aisle 13/K** (37.6, -141.5) — cuts off 2 cells
- **Office/H** (12.8, -132.4) — cuts off 2 cells
- **Aisle 1/M** (72.8, -132.3) — cuts off 18 cells
- **Aisle 1/L** (77.3, -131.5) — cuts off 9 cells
- **Office/F** (12.8, -127.2) — cuts off 2 cells
- **WC/I** (20.1, -121.9) — cuts off 0 cells
- **WC/G** (12.8, -117.3) — cuts off 6 cells
- **WC/K** (17.3, -118.7) — cuts off 4 cells
- **WC/D** (22.3, -117.4) — cuts off 66 cells
- **Lobby/Y** (27.8, -117.3) — cuts off 82 cells
- **WC/B** (17.4, -112.3) — cuts off 2 cells
- **Street/M4** (82.6, -77.7) — cuts off 4 cells

## Region index

<details><summary>All regions with their neighbours (click to expand)</summary>

| region | room | section | centre (x, z) | cells | neighbours (width) |
|---|---|---|---|---|---|
| Cold storage/A | Cold storage | — | 81.7, -178.3 | 9 | Cold storage/B (7), Cold storage/I (7), Cold storage/C (1) |
| Cold storage/B | Cold storage | — | 77.5, -178 | 10 | Cold storage/D (7), Cold storage/C (7), Cold storage/N (1), Cold storage/A (7), Cold storage/I (1) |
| Cold storage/C | Cold storage | — | 77.3, -180.5 | 3 | Cold storage/N (1), Cold storage/B (7), Cold storage/D (1), Cold storage/I (1), Cold storage/A (1) |
| Cold storage/D | Cold storage | — | 72.7, -178 | 10 | Cold storage/E (7), Cold storage/N (7), Cold storage/O (1), Cold storage/B (7), Cold storage/C (1) |
| Cold storage/E | Cold storage | — | 67.4, -178 | 13 | Cold storage/F (7), Cold storage/O (10), Cold storage/G (1), Cold storage/D (7), Cold storage/N (1), Door: Cold storage - Sales floor (4) |
| Cold storage/F | Cold storage | — | 62.3, -178.3 | 9 | Cold storage/H (7), Cold storage/G (7), Cold storage/T (1), Cold storage/E (7), Cold storage/O (1) |
| Cold storage/G | Cold storage | — | 62.3, -180.5 | 3 | Cold storage/T (1), Cold storage/F (7), Cold storage/H (1), Cold storage/O (1), Cold storage/E (1) |
| Cold storage/H | Cold storage | — | 58.2, -178 | 10 | Cold storage/T (7), Cold storage/F (7), Cold storage/G (1) |
| Cold storage/I | Cold storage | — | 81.9, -182.6 | 8 | Cold storage/K (1), Cold storage/J (7), Cold storage/L (1), Cold storage/C (1), Cold storage/A (7), Cold storage/B (1) |
| Cold storage/J | Cold storage | — | 81.7, -186.9 | 9 | Cold storage/L (7), Cold storage/I (7), Cold storage/K (1) |
| Cold storage/K | Cold storage | — | 77.3, -184.5 | 3 | Cold storage/N (1), Cold storage/L (7), Cold storage/M (1), Cold storage/I (1), Cold storage/J (1) |
| Cold storage/L | Cold storage | — | 77.3, -187 | 9 | Cold storage/M (7), Cold storage/J (7), Cold storage/K (7), Cold storage/N (1), Cold storage/I (1) |
| Cold storage/M | Cold storage | — | 72.9, -187 | 9 | Cold storage/L (7), Cold storage/P (5), Cold storage/N (7), Cold storage/O (1), Cold storage/K (1) |
| Cold storage/N | Cold storage | — | 72.8, -182.6 | 9 | Cold storage/O (7), Cold storage/M (7), Cold storage/P (1), Cold storage/K (1), Cold storage/L (1), Cold storage/D (7), Cold storage/E (1), Cold storage/C (1), Cold storage/B (1) |
| Cold storage/O | Cold storage | — | 67.8, -182.6 | 11 | Cold storage/Q (1), Cold storage/P (10), Cold storage/R (1), Cold storage/N (7), Cold storage/M (1), Cold storage/G (1), Cold storage/E (10), Cold storage/F (1), Cold storage/D (1) |
| Cold storage/P | Cold storage | — | 67.6, -187 | 12 | Cold storage/R (7), Cold storage/M (5), Cold storage/O (10), Cold storage/Q (1), Cold storage/N (1) |
| Cold storage/Q | Cold storage | — | 62.3, -184.5 | 3 | Cold storage/T (1), Cold storage/R (7), Cold storage/S (1), Cold storage/O (1), Cold storage/P (1) |
| Cold storage/R | Cold storage | — | 62.3, -186.9 | 9 | Cold storage/P (7), Cold storage/S (5), Cold storage/Q (7), Cold storage/T (1), Cold storage/O (1) |
| Cold storage/S | Cold storage | — | 58.1, -187 | 9 | Cold storage/R (5), Cold storage/T (7), Cold storage/Q (1) |
| Cold storage/T | Cold storage | — | 57.7, -182.7 | 8 | Cold storage/S (7), Cold storage/Q (1), Cold storage/R (1), Door: Cold storage - Stockroom (2), Cold storage/H (7), Cold storage/G (1), Cold storage/F (1) |
| Door: Backstreet - Staff room | Doorway | — | 18.1, -150.3 | 4 | Backstreet/D (7), Backstreet/A (2), Staff room/H (6), Staff room/G (2) |
| Door: Backstreet - Stockroom | Doorway | — | 18.1, -175.4 | 2 | Stockroom/H (6), Backstreet/L (3) |
| Door: Cold storage - Sales floor | Doorway | — | 67.5, -175 | 2 | Aisle 10/N (4), Cold storage/E (4) |
| Door: Cold storage - Stockroom | Doorway | — | 54.6, -181.9 | 2 | Cold storage/T (3), Stockroom/K (6), Stockroom/A (2) |
| Door: Lobby - WC | Doorway | — | 24.8, -116.7 | 1 | Lobby/Y (3), WC/D (5) |
| Door: Office - Sales floor | Doorway | — | 24.6, -129.4 | 2 | Aisle 13/J (2), Office/I (4), Aisle 13/D (1), Office/B (2) |
| Door: Office - Staff room | Doorway | — | 23.8, -136.9 | 4 | Staff room/A (4), Office/J (1), Office/I (2) |
| Door: Sales floor - Staff room | Doorway | — | 24.6, -143 | 2 | Staff room/B (6), Staff room/G (2), Aisle 8/A (3) |
| Door: Sales floor - Stockroom | Doorway | — | 42.1, -175.4 | 2 | Stockroom/C (8), Aisle 12/G (3) |
| Doors: Lobby - Pavement | Doorway | — | 42.8, -109.9 | 6 | Lobby/H (2), Lobby/U (7), Lobby/G (2), Pavement/J (2), Pavement/B (7), Pavement/I (2) |
| Doors: Lobby - Pavement (2) | Doorway | — | 67.6, -109.9 | 4 | Lobby/C (8), Pavement/A (8) |
| Doors: Lobby - Sales floor | Doorway | — | 42.6, -120.6 | 5 | Aisle 13/A (2), Aisle 2/H (5), Aisle 2/E (1), Lobby/V (2), Lobby/T (5) |
| Doors: Lobby - Sales floor (2) | Doorway | — | 67.6, -120.4 | 4 | Aisle 1/H (9), Lobby/O (7) |
| Lobby/A | Lobby | — | 77.3, -112.2 | 9 | Lobby/B (7), Lobby/M (7), Lobby/N (1), Lobby/K (7), Lobby/L (1) |
| Lobby/B | Lobby | — | 72.8, -112.3 | 9 | Lobby/C (7), Lobby/N (7), Lobby/O (1), Lobby/A (7), Lobby/M (1) |
| Lobby/C | Lobby | — | 67.6, -112.5 | 10 | Lobby/D (7), Lobby/O (10), Lobby/P (1), Lobby/B (7), Lobby/N (1), Doors: Lobby - Pavement (2) (8) |
| Lobby/D | Lobby | — | 62.3, -112.2 | 9 | Lobby/E (7), Lobby/P (7), Lobby/Q (1), Lobby/C (7), Lobby/O (1) |
| Lobby/E | Lobby | — | 57.8, -112.2 | 9 | Lobby/F (7), Lobby/Q (7), Lobby/R (1), Lobby/D (7), Lobby/P (1) |
| Lobby/F | Lobby | — | 52.6, -112.2 | 12 | Lobby/G (7), Lobby/R (10), Lobby/S (1), Lobby/E (7), Lobby/Q (1) |
| Lobby/G | Lobby | — | 47.3, -112.2 | 9 | Lobby/U (5), Lobby/S (7), Lobby/T (1), Lobby/F (7), Lobby/R (1), Doors: Lobby - Pavement (2) |
| Lobby/H | Lobby | — | 37.6, -112.2 | 12 | Lobby/I (7), Lobby/V (10), Lobby/W (1), Lobby/U (5), Lobby/T (1), Doors: Lobby - Pavement (2) |
| Lobby/I | Lobby | — | 32.4, -112.4 | 9 | Lobby/X (1), Lobby/W (7), Lobby/Y (1), Lobby/H (7), Lobby/V (1), Lobby/J (4) |
| Lobby/J | Lobby | — | 27.6, -112 | 8 | Lobby/Y (4), Lobby/I (4) |
| Lobby/K | Lobby | — | 81.7, -113.2 | 6 | Lobby/A (7), Lobby/L (10), Lobby/M (1) |
| Lobby/L | Lobby | — | 81.5, -116.4 | 7 | Lobby/M (10), Lobby/K (10), Lobby/A (1) |
| Lobby/M | Lobby | — | 77.4, -117.3 | 12 | Lobby/N (10), Lobby/L (10), Lobby/A (7), Lobby/B (1), Lobby/K (1) |
| Lobby/N | Lobby | — | 72.7, -117.2 | 12 | Lobby/M (10), Lobby/O (9), Lobby/B (7), Lobby/C (1), Lobby/A (1) |
| Lobby/O | Lobby | — | 67.4, -117 | 13 | Doors: Lobby - Sales floor (2) (7), Lobby/P (10), Lobby/N (9), Lobby/C (10), Lobby/D (1), Lobby/B (1) |
| Lobby/P | Lobby | — | 62.3, -117.3 | 12 | Lobby/Q (10), Lobby/O (10), Lobby/D (7), Lobby/E (1), Lobby/C (1) |
| Lobby/Q | Lobby | — | 57.8, -117.3 | 12 | Lobby/R (10), Lobby/P (10), Lobby/E (7), Lobby/F (1), Lobby/D (1) |
| Lobby/R | Lobby | — | 52.8, -117.2 | 15 | Lobby/Q (10), Lobby/S (7), Lobby/F (10), Lobby/G (1), Lobby/E (1) |
| Lobby/S | Lobby | — | 47.2, -116.9 | 10 | Lobby/T (10), Lobby/R (7), Lobby/G (7), Lobby/U (1), Lobby/F (1) |
| Lobby/T | Lobby | — | 43.1, -116.9 | 10 | Lobby/S (10), Doors: Lobby - Sales floor (5), Lobby/V (7), Lobby/U (7), Lobby/H (1), Lobby/G (1) |
| Lobby/U | Lobby | — | 42.8, -112.9 | 6 | Lobby/H (5), Lobby/T (7), Lobby/V (1), Lobby/G (5), Lobby/S (1), Doors: Lobby - Pavement (7) |
| Lobby/V | Lobby | — | 37.8, -117.2 | 15 | Doors: Lobby - Sales floor (2), Lobby/W (7), Lobby/T (7), Lobby/H (10), Lobby/I (1), Lobby/U (1) |
| Lobby/W | Lobby | — | 32.2, -117.1 | 11 | Lobby/Y (10), Lobby/V (7), Lobby/I (7), Lobby/X (1), Lobby/H (1) |
| Lobby/X | Lobby | — | 29.8, -114.3 | 1 | Lobby/I (1), Lobby/Y (1), Lobby/W (1) |
| Lobby/Y | Lobby | — | 27.8, -117.3 | 12 | Lobby/W (10), Door: Lobby - WC (1), Lobby/J (5), Lobby/X (2), Lobby/I (1) |
| Office/A | Office | — | 22.5, -124.4 | 4 | Office/D (1), Office/B (10), Office/C (1) |
| Office/B | Office | — | 22.1, -127 | 10 | Office/I (7), Door: Office - Sales floor (2), Office/C (4), Office/A (10), Office/D (1) |
| Office/C | Office | — | 17.3, -126.4 | 6 | Office/F (4), Office/B (4), Office/D (3), Office/E (1), Office/A (1) |
| Office/D | Office | — | 17.3, -124.6 | 3 | Office/E (1), Office/C (4), Office/F (1), Office/A (1), Office/B (1) |
| Office/E | Office | — | 14.3, -124.2 | 1 | Office/D (1), Office/F (1), Office/C (1) |
| Office/F | Office | — | 12.8, -127.2 | 9 | Office/H (7), Office/C (4), Office/G (4), Office/E (2), Office/D (1) |
| Office/G | Office | — | 12.1, -124.2 | 2 | Office/F (4) |
| Office/H | Office | — | 12.8, -132.4 | 12 | Office/N (1), Office/L (6), Office/M (1), Office/F (7) |
| Office/I | Office | — | 22.3, -132.5 | 15 | Office/L (7), Office/J (5), Office/K (1), Door: Office - Staff room (2), Door: Office - Sales floor (3), Office/B (7) |
| Office/J | Office | — | 21, -136 | 2 | Office/K (1), Office/I (5), Office/L (1), Door: Office - Staff room (1) |
| Office/K | Office | — | 18.4, -135.8 | 2 | Office/L (4), Office/J (1), Office/I (1) |
| Office/L | Office | — | 17.3, -133 | 9 | Office/H (6), Office/K (4), Office/I (5), Office/J (1) |
| Office/M | Office | — | 14.3, -136 | 1 | Office/H (1) |
| Office/N | Office | — | 11.5, -135.5 | 1 | Office/H (1) |
| Backstreet/A | Outside | — | 22.3, -152.7 | 11 | Backstreet/D (5), Backstreet/B (4), Backstreet/C (1), Door: Backstreet - Staff room (2) |
| Backstreet/B | Outside | — | 21.1, -157.2 | 6 | Backstreet/C (7), Backstreet/G (4), Backstreet/H (1), Backstreet/A (4), Backstreet/D (1) |
| Backstreet/C | Outside | — | 17.3, -157.2 | 9 | Backstreet/E (7), Backstreet/H (7), Backstreet/I (1), Backstreet/B (7), Backstreet/G (1), Backstreet/D (7), Backstreet/F (1), Backstreet/A (1) |
| Backstreet/D | Outside | — | 17.1, -153.1 | 7 | Backstreet/F (7), Backstreet/C (7), Backstreet/E (1), Backstreet/A (5), Backstreet/B (1), Door: Backstreet - Staff room (6) |
| Backstreet/E | Outside | — | 12.9, -157.1 | 9 | West alley/J (2), Backstreet/I (7), West alley/K (1), Backstreet/C (7), Backstreet/H (1), Backstreet/F (7), West alley/I (1), Backstreet/D (1) |
| Backstreet/F | Outside | — | 12.8, -152.7 | 9 | West alley/I (7), Backstreet/E (7), West alley/J (1), Backstreet/D (7), Backstreet/C (1) |
| Backstreet/G | Outside | — | 21.6, -162 | 10 | Backstreet/H (10), Backstreet/J (4), Backstreet/M (1), Backstreet/B (4), Backstreet/C (1) |
| Backstreet/H | Outside | — | 17.3, -162.4 | 12 | Backstreet/I (10), Backstreet/M (7), Backstreet/O (1), Backstreet/G (10), Backstreet/J (1), Backstreet/C (7), Backstreet/E (1), Backstreet/B (1) |
| Backstreet/I | Outside | — | 12.8, -162.4 | 12 | West alley/K (10), Backstreet/O (7), West alley/L (1), Backstreet/H (10), Backstreet/M (1), Backstreet/E (7), West alley/J (1), Backstreet/C (1) |
| Backstreet/J | Outside | — | 21.8, -167.3 | 8 | Backstreet/M (7), Backstreet/K (4), Backstreet/L (1), Backstreet/G (4), Backstreet/H (1) |
| Backstreet/K | Outside | — | 22.2, -172.5 | 10 | Backstreet/L (7), Backstreet/J (4), Backstreet/M (1) |
| Backstreet/L | Outside | — | 17.3, -172.2 | 9 | Backstreet/N (7), Door: Backstreet - Stockroom (2), Backstreet/K (7), Backstreet/M (7), Backstreet/O (1), Backstreet/J (1) |
| Backstreet/M | Outside | — | 17.3, -167.7 | 9 | Backstreet/O (7), Backstreet/L (7), Backstreet/N (1), Backstreet/J (7), Backstreet/K (1), Backstreet/H (7), Backstreet/I (1), Backstreet/G (1) |
| Backstreet/N | Outside | — | 12.9, -172.2 | 9 | West alley/M (2), Backstreet/L (7), Backstreet/O (7), Backstreet/M (1) |
| Backstreet/O | Outside | — | 12.9, -167.7 | 9 | West alley/L (2), Backstreet/N (7), Backstreet/M (7), Backstreet/L (1), Backstreet/I (7), Backstreet/H (1) |
| East alley/A | Outside | — | 87.1, -112.2 | 6 | East alley/B (4), Street/H8 (4) |
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
| Pavement/A | Outside | — | 67.6, -107.4 | 10 | Doors: Lobby - Pavement (2) (8), Pavement/F (7), Pavement/E (7), Street/U7 (8), Street/V7 (1), Street/T7 (1) |
| Pavement/B | Outside | — | 42.8, -107 | 6 | Pavement/J (5), Doors: Lobby - Pavement (7), Pavement/I (5), Street/Z7 (5), Street/A8 (1) |
| Pavement/C | Outside | — | 82.6, -107.7 | 12 | Pavement/D (7), Street/H8 (7), Street/R7 (10), Street/S7 (1), Street/Q7 (1) |
| Pavement/D | Outside | — | 77.3, -107.7 | 9 | Pavement/E (7), Pavement/C (7), Street/S7 (7), Street/T7 (1), Street/R7 (1) |
| Pavement/E | Outside | — | 72.8, -107.6 | 9 | Pavement/A (7), Pavement/D (7), Street/T7 (7), Street/U7 (1), Street/S7 (1) |
| Pavement/F | Outside | — | 62.3, -107.7 | 9 | Pavement/G (7), Pavement/A (7), Street/V7 (7), Street/W7 (1), Street/U7 (1) |
| Pavement/G | Outside | — | 57.8, -107.7 | 9 | Pavement/H (7), Pavement/F (7), Street/W7 (7), Street/X7 (1), Street/V7 (1) |
| Pavement/H | Outside | — | 52.6, -107.7 | 12 | Pavement/I (7), Pavement/G (7), Street/X7 (10), Street/Y7 (1), Street/W7 (1) |
| Pavement/I | Outside | — | 47.3, -107.7 | 9 | Doors: Lobby - Pavement (2), Pavement/B (5), Pavement/H (7), Street/Y7 (6), Street/Z7 (1), Street/X7 (1) |
| Pavement/J | Outside | — | 37.6, -107.6 | 12 | Pavement/K (7), Doors: Lobby - Pavement (2), Pavement/B (5), Street/A8 (10), Street/B8 (1), Street/Z7 (1) |
| Pavement/K | Outside | — | 32.3, -107.6 | 9 | Pavement/L (7), Pavement/J (7), Street/B8 (7), Street/C8 (1), Street/A8 (1) |
| Pavement/L | Outside | — | 27.8, -107.7 | 9 | Pavement/M (7), Pavement/K (7), Street/C8 (7), Street/D8 (1), Street/B8 (1) |
| Pavement/M | Outside | — | 22.6, -107.7 | 12 | Pavement/N (7), Pavement/L (7), Street/D8 (10), Street/E8 (1), Street/C8 (1) |
| Pavement/N | Outside | — | 17.3, -107.7 | 9 | Pavement/O (7), Pavement/M (5), Street/E8 (7), Street/F8 (1), Street/D8 (1) |
| Pavement/O | Outside | — | 12.8, -107.7 | 9 | Street/I8 (7), Pavement/N (7), Street/F8 (7), Street/G8 (1), Street/E8 (1) |
| South yard/A | Outside | — | 82.6, -191.9 | 12 | South yard/B (7), East alley/Q (7) |
| South yard/B | Outside | — | 77.3, -191.9 | 9 | South yard/C (7), South yard/A (7) |
| South yard/C | Outside | — | 72.8, -191.9 | 9 | South yard/D (7), South yard/B (7) |
| South yard/D | Outside | — | 67.6, -191.9 | 12 | South yard/E (7), South yard/C (7) |
| South yard/E | Outside | — | 62.3, -191.9 | 9 | South yard/F (7), South yard/D (7) |
| South yard/F | Outside | — | 57.8, -191.9 | 9 | South yard/G (7), South yard/E (7) |
| South yard/G | Outside | — | 52.6, -191.9 | 12 | South yard/H (7), South yard/F (7) |
| South yard/H | Outside | — | 47.3, -191.9 | 9 | South yard/I (7), South yard/G (7) |
| South yard/I | Outside | — | 42.8, -191.9 | 9 | South yard/J (7), South yard/H (7) |
| South yard/J | Outside | — | 37.6, -191.9 | 12 | South yard/K (7), South yard/I (7) |
| South yard/K | Outside | — | 32.3, -191.9 | 9 | South yard/L (7), South yard/J (7) |
| South yard/L | Outside | — | 27.8, -191.9 | 9 | South yard/M (7), South yard/K (7) |
| South yard/M | Outside | — | 22.6, -191.9 | 12 | South yard/N (7), South yard/L (7) |
| South yard/N | Outside | — | 17.3, -191.9 | 9 | South yard/O (7), South yard/M (7) |
| South yard/O | Outside | — | 12.8, -191.9 | 9 | West alley/Q (7), South yard/N (7) |
| Street/A | Outside | — | 87.1, -53.7 | 2 | Street/C (1), Street/B (4), Street/D (1) |
| Street/A2 | Outside | — | 22.6, -57.4 | 16 | Street/D2 (8), Street/J3 (10), Street/K3 (1), Street/Z (10), Street/G3 (1), Street/B2 (10), Street/Y (1) |
| Street/A3 | Outside | — | 42.8, -62.7 | 9 | Street/C3 (7), Street/B3 (7), Street/D3 (1), Street/Y2 (7), Street/Z2 (1), Street/S (7), Street/U (1), Street/Q (1) |
| Street/A4 | Outside | — | 22.6, -72.4 | 16 | Street/B4 (10), Street/J5 (10), Street/H4 (1), Street/Z3 (10), Street/H5 (1), Street/I3 (10), Street/L3 (1), Street/H3 (1) |
| Street/A5 | Outside | — | 47.3, -77.7 | 9 | Street/B5 (7), Street/Z4 (7), Street/C5 (1), Street/X4 (7), Street/Y4 (1), Street/G4 (7), Street/W3 (1), Street/V3 (1) |
| Street/A6 | Outside | — | 37.6, -87.4 | 16 | Street/B6 (10), Street/D7 (10), Street/F7 (1), Street/Z5 (10), Street/B7 (1), Street/E5 (10), Street/F5 (1), Street/C5 (1) |
| Street/A7 | Outside | — | 42.9, -97.1 | 9 | Street/C7 (4), Street/Z7 (7), Street/A8 (1), Street/Y6 (7), Street/Y7 (1), Street/B7 (7), Street/D7 (1), Street/Z6 (1) |
| Street/A8 | Outside | — | 37.6, -102.4 | 16 | Street/B8 (10), Pavement/J (10), Pavement/K (1), Street/Z7 (10), Pavement/B (1), Street/C7 (10), Street/E7 (1), Street/A7 (1) |
| Street/B | Outside | — | 87.1, -57.4 | 8 | Street/D (10), Street/I2 (4), Street/K2 (1), Street/A (4), Street/C (1) |
| Street/B2 | Outside | — | 22.6, -53.7 | 4 | Street/C2 (1), Street/A2 (10), Street/D2 (1), Street/Y (1), Street/Z (1) |
| Street/B3 | Outside | — | 42.8, -67.2 | 9 | Street/D3 (7), Street/W3 (7), Street/X3 (1), Street/Z2 (7), Street/A3 (7), Street/C3 (1), Street/Y2 (1) |
| Street/B4 | Outside | — | 17.3, -72.4 | 12 | Street/C4 (10), Street/H4 (7), Street/M5 (1), Street/A4 (10), Street/J5 (1), Street/L3 (7), Street/M3 (1), Street/I3 (1) |
| Street/B5 | Outside | — | 42.8, -77.7 | 9 | Street/D5 (7), Street/C5 (7), Street/E5 (1), Street/A5 (7), Street/Z4 (1), Street/W3 (7), Street/X3 (1), Street/G4 (1) |
| Street/B6 | Outside | — | 32.5, -86.9 | 10 | Street/A6 (10), Street/F7 (1), Street/D7 (1), Street/C6 (7), Street/F5 (7), Street/I5 (1), Street/E5 (1) |
| Street/B7 | Outside | — | 42.8, -92.7 | 9 | Street/D7 (7), Street/A7 (7), Street/C7 (1), Street/Z6 (1), Street/Y6 (1), Street/Z5 (7), Street/A6 (1), Street/H6 (1), Street/Y5 (1) |
| Street/B8 | Outside | — | 32.3, -102.4 | 12 | Street/C8 (10), Pavement/K (5), Pavement/L (1), Street/A8 (10), Pavement/J (1), Street/E7 (7), Street/H7 (1), Street/C7 (1) |
| Street/C | Outside | — | 82.6, -53.7 | 4 | Street/E (1), Street/D (10), Street/F (1), Street/A (1), Street/B (1) |
| Street/C2 | Outside | — | 17.3, -53.7 | 3 | Street/E2 (1), Street/D2 (7), Street/F2 (1), Street/B2 (1), Street/A2 (1) |
| Street/C3 | Outside | — | 37.6, -62.7 | 12 | Street/F3 (7), Street/D3 (10), Street/E3 (1), Street/A3 (7), Street/B3 (1), Street/U (10), Street/W (1), Street/S (1) |
| Street/C4 | Outside | — | 12.8, -72.4 | 12 | Street/D4 (10), Street/M5 (7), Street/P5 (1), Street/B4 (10), Street/H4 (1), Street/M3 (7), Street/P3 (1), Street/L3 (1) |
| Street/C5 | Outside | — | 42.8, -82.2 | 9 | Street/E5 (7), Street/Z5 (7), Street/A6 (1), Street/Z4 (7), Street/Y5 (1), Street/B5 (7), Street/D5 (1), Street/A5 (1) |
| Street/C6 | Outside | — | 27.8, -87.4 | 12 | Street/D6 (10), Street/G7 (7), Street/I7 (1), Street/B6 (7), Street/I5 (7), Street/K5 (1), Street/F5 (1) |
| Street/C7 | Outside | — | 37.6, -97.2 | 12 | Street/E7 (7), Street/A8 (10), Street/B8 (1), Street/A7 (4), Street/Z7 (1), Street/D7 (10), Street/F7 (1), Street/B7 (1) |
| Street/C8 | Outside | — | 27.8, -102.4 | 12 | Street/D8 (10), Pavement/L (7), Pavement/M (1), Street/B8 (10), Pavement/K (1), Street/H7 (7), Street/J7 (1), Street/E7 (1) |
| Street/D | Outside | — | 82.6, -57.4 | 16 | Street/F (10), Street/K2 (10), Street/N2 (1), Street/B (10), Street/I2 (1), Street/C (10), Street/E (1), Street/A (1) |
| Street/D2 | Outside | — | 17.3, -57.4 | 12 | Street/F2 (10), Street/K3 (7), Street/N3 (1), Street/A2 (8), Street/J3 (1), Street/C2 (7), Street/E2 (1) |
| Street/D3 | Outside | — | 37.6, -67.2 | 12 | Street/E3 (7), Street/X3 (10), Street/Y3 (1), Street/B3 (7), Street/W3 (1), Street/C3 (10), Street/F3 (1), Street/A3 (1) |
| Street/D4 | Outside | — | 8.3, -72.4 | 12 | Street/P5 (7), Street/C4 (10), Street/M5 (1), Street/P3 (7), Street/M3 (1) |
| Street/D5 | Outside | — | 37.6, -77.7 | 12 | Street/G5 (7), Street/E5 (10), Street/F5 (1), Street/B5 (7), Street/C5 (1), Street/X3 (9), Street/Y3 (1), Street/W3 (1) |
| Street/D6 | Outside | — | 22.6, -87.4 | 16 | Street/E6 (10), Street/I7 (10), Street/K7 (1), Street/C6 (10), Street/G7 (1), Street/K5 (10), Street/L5 (1), Street/I5 (1) |
| Street/D7 | Outside | — | 37.6, -92.6 | 12 | Street/F7 (7), Street/C7 (8), Street/E7 (1), Street/B7 (7), Street/A7 (1), Street/A6 (10), Street/B6 (1), Street/Z5 (1) |
| Street/D8 | Outside | — | 22.6, -102.4 | 16 | Street/E8 (10), Pavement/M (10), Pavement/N (1), Street/C8 (10), Pavement/L (1), Street/J7 (10), Street/L7 (1), Street/H7 (1) |
| Street/E | Outside | — | 77.3, -53.7 | 3 | Street/G (1), Street/F (7), Street/H (1), Street/C (1), Street/D (1) |
| Street/E2 | Outside | — | 12.8, -53.7 | 3 | Street/G2 (1), Street/F2 (7), Street/H2 (1), Street/C2 (1), Street/D2 (1) |
| Street/E3 | Outside | — | 32.3, -67.2 | 9 | Street/H3 (7), Street/Y3 (7), Street/Z3 (1), Street/D3 (7), Street/X3 (1), Street/F3 (7), Street/G3 (1), Street/C3 (1) |
| Street/E4 | Outside | — | 72.8, -73.2 | 9 | Street/T3 (7), Street/Q4 (7), Street/R4 (1), Street/S3 (7), Street/N4 (1) |
| Street/E5 | Outside | — | 37.6, -82.2 | 12 | Street/F5 (7), Street/A6 (10), Street/B6 (1), Street/C5 (7), Street/Z5 (1), Street/D5 (10), Street/G5 (1), Street/B5 (1) |
| Street/E6 | Outside | — | 17.3, -87.4 | 12 | Street/F6 (10), Street/K7 (7), Street/M7 (1), Street/D6 (10), Street/I7 (1), Street/L5 (7), Street/N5 (1), Street/K5 (1) |
| Street/E7 | Outside | — | 32.3, -97.2 | 9 | Street/H7 (7), Street/B8 (7), Street/C8 (1), Street/C7 (7), Street/A8 (1), Street/F7 (7), Street/G7 (1), Street/D7 (1) |
| Street/E8 | Outside | — | 17.3, -102.4 | 12 | Street/F8 (10), Pavement/N (7), Pavement/O (1), Street/D8 (10), Pavement/M (1), Street/L7 (7), Street/N7 (1), Street/J7 (1) |
| Street/F | Outside | — | 77.3, -57.4 | 12 | Street/H (10), Street/N2 (7), Street/P2 (1), Street/D (10), Street/K2 (1), Street/E (5), Street/G (1), Street/C (1) |
| Street/F2 | Outside | — | 12.8, -57.4 | 12 | Street/H2 (10), Street/N3 (7), Street/O3 (1), Street/D2 (10), Street/K3 (1), Street/E2 (7), Street/G2 (1), Street/C2 (1) |
| Street/F3 | Outside | — | 32.3, -62.7 | 9 | Street/G3 (7), Street/E3 (7), Street/H3 (1), Street/C3 (7), Street/D3 (1), Street/W (7), Street/Z (1), Street/U (1) |
| Street/F4 | Outside | — | 62.5, -72.9 | 10 | Street/U3 (7), Street/T4 (7), Street/W4 (1), Street/T3 (10), Street/R4 (1), Street/S2 (1), Street/Q2 (1) |
| Street/F5 | Outside | — | 32.3, -82.2 | 9 | Street/I5 (7), Street/B6 (7), Street/C6 (1), Street/E5 (7), Street/A6 (1), Street/G5 (1), Street/D5 (1) |
| Street/F6 | Outside | — | 12.9, -87.4 | 12 | Street/G6 (10), Street/M7 (7), Street/P7 (1), Street/E6 (10), Street/K7 (1), Street/N5 (7), Street/O5 (1), Street/L5 (1) |
| Street/F7 | Outside | — | 32.3, -92.7 | 9 | Street/G7 (7), Street/E7 (7), Street/H7 (1), Street/D7 (7), Street/C7 (1), Street/B6 (1), Street/A6 (1) |
| Street/F8 | Outside | — | 12.8, -102.4 | 12 | Street/G8 (10), Pavement/O (7), Street/I8 (1), Street/E8 (10), Pavement/N (1), Street/N7 (7), Street/O7 (1), Street/L7 (1) |
| Street/G | Outside | — | 72.8, -53.7 | 3 | Street/J (1), Street/H (7), Street/I (1), Street/E (1), Street/F (1) |
| Street/G2 | Outside | — | 8.3, -53.7 | 3 | Street/H2 (7), Street/E2 (1), Street/F2 (1) |
| Street/G3 | Outside | — | 27.8, -62.7 | 9 | Street/J3 (7), Street/H3 (7), Street/I3 (1), Street/F3 (7), Street/E3 (1), Street/Z (7), Street/A2 (1), Street/W (1) |
| Street/G4 | Outside | — | 47.3, -73.2 | 9 | Street/W3 (7), Street/A5 (7), Street/B5 (1), Street/V3 (7), Street/X4 (1) |
| Street/G5 | Outside | — | 32.3, -77.7 | 9 | Street/H5 (3), Street/D5 (7), Street/F5 (2), Street/E5 (1), Street/Y3 (5), Street/Z3 (1), Street/X3 (1) |
| Street/G6 | Outside | — | 8.3, -87.4 | 12 | Street/P7 (7), Street/F6 (10), Street/M7 (1), Street/O5 (7), Street/N5 (1) |
| Street/G7 | Outside | — | 27.8, -92.7 | 9 | Street/I7 (7), Street/H7 (7), Street/J7 (1), Street/F7 (7), Street/E7 (1), Street/C6 (7), Street/D6 (1) |
| Street/G8 | Outside | — | 8.3, -102.4 | 12 | Street/I8 (7), Street/F8 (10), Pavement/O (1), Street/O7 (7), Street/N7 (1) |
| Street/H | Outside | — | 72.8, -57.4 | 12 | Street/I (10), Street/P2 (7), Street/R2 (1), Street/F (10), Street/N2 (1), Street/G (7), Street/J (1), Street/E (1) |
| Street/H2 | Outside | — | 8.3, -57.4 | 12 | Street/O3 (7), Street/F2 (10), Street/N3 (1), Street/G2 (7), Street/E2 (1) |
| Street/H3 | Outside | — | 27.8, -67.2 | 9 | Street/I3 (7), Street/Z3 (7), Street/A4 (1), Street/E3 (7), Street/Y3 (1), Street/G3 (7), Street/J3 (1), Street/F3 (1) |
| Street/H4 | Outside | — | 17.3, -77.4 | 9 | Street/M5 (7), Street/J5 (7), Street/B4 (7), Street/C4 (1), Street/A4 (1) |
| Street/H5 | Outside | — | 27.8, -77.7 | 9 | Street/J5 (7), Street/I5 (7), Street/K5 (1), Street/G5 (4), Street/Z3 (7), Street/A4 (1), Street/Y3 (1) |
| Street/H6 | Outside | — | 47.3, -91.2 | 3 | Street/B7 (1), Street/Y5 (7), Street/Z5 (1), Street/X6 (1), Street/X5 (1) |
| Street/H7 | Outside | — | 27.8, -97.2 | 9 | Street/J7 (7), Street/C8 (7), Street/D8 (1), Street/E7 (7), Street/B8 (1), Street/G7 (7), Street/I7 (1), Street/F7 (1) |
| Street/H8 | Outside | — | 87.1, -107.7 | 6 | Pavement/C (7), East alley/A (4), Street/Q7 (4), Street/R7 (1) |
| Street/I | Outside | — | 67.6, -57.4 | 16 | Street/L (10), Street/R2 (10), Street/T2 (1), Street/H (10), Street/P2 (1), Street/J (10), Street/K (1), Street/G (1) |
| Street/I2 | Outside | — | 87.1, -62.7 | 6 | Street/K2 (7), Street/J2 (4), Street/L2 (1), Street/B (4), Street/D (1) |
| Street/I3 | Outside | — | 22.6, -67.2 | 12 | Street/L3 (7), Street/A4 (10), Street/B4 (1), Street/H3 (7), Street/Z3 (1), Street/J3 (10), Street/K3 (1), Street/G3 (1) |
| Street/I4 | Outside | — | 87.1, -77.7 | 2 | Street/M4 (1) |
| Street/I5 | Outside | — | 27.8, -82.2 | 9 | Street/K5 (7), Street/C6 (7), Street/D6 (1), Street/F5 (7), Street/B6 (1), Street/H5 (7), Street/J5 (1) |
| Street/I6 | Outside | — | 87.1, -92.7 | 6 | Street/K6 (7), Street/J6 (4), Street/L6 (1), Street/Q5 (4), Street/R5 (1) |
| Street/I7 | Outside | — | 22.6, -92.7 | 12 | Street/K7 (7), Street/J7 (10), Street/L7 (1), Street/G7 (7), Street/H7 (1), Street/D6 (10), Street/E6 (1), Street/C6 (1) |
| Street/I8 | Outside | — | 8.3, -107.7 | 9 | West alley/A (4), Pavement/O (7), Street/G8 (7), Street/F8 (1) |
| Street/J | Outside | — | 67.6, -53.7 | 4 | Street/K (1), Street/I (10), Street/L (1), Street/G (1), Street/H (1) |
| Street/J2 | Outside | — | 87.1, -67.2 | 6 | Street/L2 (7), Street/Q3 (4), Street/R3 (1), Street/I2 (4), Street/K2 (1) |
| Street/J3 | Outside | — | 22.6, -62.7 | 12 | Street/K3 (7), Street/I3 (10), Street/L3 (1), Street/G3 (7), Street/H3 (1), Street/A2 (10), Street/D2 (1), Street/Z (1) |
| Street/J4 | Outside | — | 87.1, -79.2 | 2 | Street/M4 (1) |
| Street/J5 | Outside | — | 22.6, -77.7 | 12 | Street/H4 (7), Street/K5 (10), Street/H5 (7), Street/I5 (1), Street/A4 (10), Street/B4 (1), Street/Z3 (1) |
| Street/J6 | Outside | — | 87.1, -97.2 | 6 | Street/L6 (7), Street/Q7 (4), Street/R7 (1), Street/I6 (4), Street/K6 (1) |
| Street/J7 | Outside | — | 22.6, -97.2 | 12 | Street/L7 (7), Street/D8 (10), Street/E8 (1), Street/H7 (7), Street/C8 (1), Street/I7 (10), Street/K7 (1), Street/G7 (1) |
| Street/K | Outside | — | 62.3, -53.7 | 3 | Street/M (1), Street/L (7), Street/N (1), Street/J (1), Street/I (1) |
| Street/K2 | Outside | — | 82.6, -62.7 | 12 | Street/N2 (7), Street/L2 (10), Street/M2 (1), Street/I2 (7), Street/J2 (1), Street/D (10), Street/F (1), Street/B (1) |
| Street/K3 | Outside | — | 17.3, -62.7 | 9 | Street/N3 (7), Street/L3 (7), Street/M3 (1), Street/J3 (7), Street/I3 (1), Street/D2 (7), Street/F2 (1), Street/A2 (1) |
| Street/K4 | Outside | — | 87.1, -82.2 | 6 | Street/L4 (7), Street/Q5 (4), Street/R5 (1) |
| Street/K5 | Outside | — | 22.6, -82.2 | 12 | Street/L5 (7), Street/D6 (10), Street/E6 (1), Street/I5 (7), Street/C6 (1), Street/J5 (10), Street/H5 (1) |
| Street/K6 | Outside | — | 82.6, -92.7 | 12 | Street/M6 (7), Street/L6 (10), Street/N6 (1), Street/I6 (7), Street/J6 (1), Street/R5 (10), Street/S5 (1), Street/Q5 (1) |
| Street/K7 | Outside | — | 17.3, -92.7 | 9 | Street/M7 (7), Street/L7 (7), Street/N7 (1), Street/I7 (7), Street/J7 (1), Street/E6 (7), Street/F6 (1), Street/D6 (1) |
| Street/L | Outside | — | 62.3, -57.4 | 12 | Street/N (10), Street/T2 (7), Street/U2 (1), Street/I (10), Street/R2 (1), Street/K (7), Street/M (1), Street/J (1) |
| Street/L2 | Outside | — | 82.6, -67.2 | 12 | Street/M2 (7), Street/R3 (10), Street/S3 (1), Street/J2 (7), Street/Q3 (1), Street/K2 (10), Street/N2 (1), Street/I2 (1) |
| Street/L3 | Outside | — | 17.3, -67.2 | 9 | Street/M3 (7), Street/B4 (7), Street/C4 (1), Street/I3 (7), Street/A4 (1), Street/K3 (7), Street/N3 (1), Street/J3 (1) |
| Street/L4 | Outside | — | 82.6, -82.2 | 12 | Street/O4 (7), Street/R5 (10), Street/S5 (1), Street/K4 (7), Street/Q5 (1), Street/M4 (8), Street/N4 (1) |
| Street/L5 | Outside | — | 17.3, -82.4 | 9 | Street/N5 (7), Street/E6 (7), Street/F6 (1), Street/K5 (7), Street/D6 (1) |
| Street/L6 | Outside | — | 82.6, -97.2 | 12 | Street/N6 (7), Street/R7 (10), Street/S7 (1), Street/J6 (7), Street/Q7 (1), Street/K6 (10), Street/M6 (1), Street/I6 (1) |
| Street/L7 | Outside | — | 17.3, -97.2 | 9 | Street/N7 (7), Street/E8 (7), Street/F8 (1), Street/J7 (7), Street/D8 (1), Street/K7 (7), Street/M7 (1), Street/I7 (1) |
| Street/M | Outside | — | 57.8, -53.7 | 3 | Street/O (1), Street/N (7), Street/P (1), Street/K (1), Street/L (1) |
| Street/M2 | Outside | — | 77.3, -67.2 | 9 | Street/O2 (7), Street/S3 (7), Street/L2 (7), Street/R3 (1), Street/N2 (7), Street/P2 (1), Street/K2 (1) |
| Street/M3 | Outside | — | 12.8, -67.2 | 9 | Street/P3 (7), Street/C4 (7), Street/D4 (1), Street/L3 (7), Street/B4 (1), Street/N3 (7), Street/O3 (1), Street/K3 (1) |
| Street/M4 | Outside | — | 82.6, -77.7 | 12 | Street/N4 (7), Street/L4 (8), Street/O4 (1), Street/J4 (1), Street/I4 (1), Street/R3 (10), Street/S3 (1) |
| Street/M5 | Outside | — | 12.8, -77.6 | 9 | Street/P5 (7), Street/N5 (7), Street/O5 (1), Street/H4 (7), Street/C4 (7), Street/D4 (1), Street/B4 (1) |
| Street/M6 | Outside | — | 77.3, -92.7 | 9 | Street/O6 (7), Street/N6 (7), Street/P6 (1), Street/K6 (7), Street/L6 (1), Street/S5 (7), Street/T5 (1), Street/R5 (1) |
| Street/M7 | Outside | — | 12.8, -92.7 | 9 | Street/P7 (7), Street/N7 (7), Street/O7 (1), Street/K7 (7), Street/L7 (1), Street/F6 (7), Street/G6 (1), Street/E6 (1) |
| Street/N | Outside | — | 57.8, -57.4 | 12 | Street/P (10), Street/U2 (7), Street/X2 (1), Street/L (10), Street/T2 (1), Street/M (7), Street/O (1), Street/K (1) |
| Street/N2 | Outside | — | 77.3, -62.7 | 9 | Street/P2 (7), Street/M2 (7), Street/O2 (1), Street/K2 (7), Street/L2 (1), Street/F (7), Street/H (1), Street/D (1) |
| Street/N3 | Outside | — | 12.8, -62.7 | 9 | Street/O3 (7), Street/M3 (7), Street/P3 (1), Street/K3 (7), Street/L3 (1), Street/F2 (7), Street/H2 (1), Street/D2 (1) |
| Street/N4 | Outside | — | 77.3, -77.7 | 9 | Street/Q4 (7), Street/O4 (7), Street/P4 (1), Street/M4 (7), Street/L4 (1), Street/S3 (7), Street/E4 (1), Street/R3 (1) |
| Street/N5 | Outside | — | 12.8, -82.2 | 9 | Street/O5 (7), Street/F6 (7), Street/G6 (1), Street/L5 (7), Street/E6 (1), Street/M5 (7), Street/P5 (1) |
| Street/N6 | Outside | — | 77.3, -97.2 | 9 | Street/P6 (7), Street/S7 (7), Street/T7 (1), Street/L6 (7), Street/R7 (1), Street/M6 (7), Street/O6 (1), Street/K6 (1) |
| Street/N7 | Outside | — | 12.8, -97.2 | 9 | Street/O7 (7), Street/F8 (7), Street/G8 (1), Street/L7 (7), Street/E8 (1), Street/M7 (7), Street/P7 (1), Street/K7 (1) |
| Street/O | Outside | — | 52.6, -53.7 | 4 | Street/R (1), Street/P (10), Street/Q (1), Street/M (1), Street/N (1) |
| Street/O2 | Outside | — | 72.8, -67.2 | 9 | Street/Q2 (7), Street/M2 (7), Street/P2 (7), Street/R2 (1), Street/N2 (1) |
| Street/O3 | Outside | — | 8.3, -62.7 | 9 | Street/P3 (7), Street/N3 (7), Street/M3 (1), Street/H2 (7), Street/F2 (1) |
| Street/O4 | Outside | — | 77.3, -82.2 | 9 | Street/P4 (7), Street/S5 (7), Street/T5 (1), Street/L4 (7), Street/R5 (1), Street/N4 (7), Street/Q4 (1), Street/M4 (1) |
| Street/O5 | Outside | — | 8.3, -82.2 | 9 | Street/G6 (7), Street/N5 (7), Street/F6 (1), Street/P5 (7), Street/M5 (1) |
| Street/O6 | Outside | — | 72.8, -92.7 | 9 | Street/Q6 (7), Street/P6 (7), Street/R6 (1), Street/M6 (7), Street/N6 (1), Street/T5 (7), Street/U5 (1), Street/S5 (1) |
| Street/O7 | Outside | — | 8.3, -97.2 | 9 | Street/G8 (7), Street/N7 (7), Street/F8 (1), Street/P7 (7), Street/M7 (1) |
| Street/P | Outside | — | 52.6, -57.4 | 16 | Street/Q (10), Street/X2 (10), Street/Y2 (1), Street/N (10), Street/U2 (1), Street/O (8), Street/R (1), Street/M (1) |
| Street/P2 | Outside | — | 72.8, -62.7 | 9 | Street/R2 (7), Street/O2 (7), Street/Q2 (1), Street/N2 (7), Street/M2 (1), Street/H (7), Street/I (1), Street/F (1) |
| Street/P3 | Outside | — | 8.3, -67.2 | 9 | Street/D4 (7), Street/M3 (7), Street/C4 (1), Street/O3 (7), Street/N3 (1) |
| Street/P4 | Outside | — | 72.8, -82.2 | 9 | Street/S4 (7), Street/T5 (7), Street/U5 (1), Street/O4 (7), Street/S5 (1), Street/Q4 (7), Street/R4 (1), Street/N4 (1) |
| Street/P5 | Outside | — | 8.3, -77.7 | 9 | Street/O5 (7), Street/M5 (7), Street/N5 (1), Street/D4 (7), Street/C4 (1) |
| Street/P6 | Outside | — | 72.8, -97.2 | 9 | Street/R6 (7), Street/T7 (7), Street/U7 (1), Street/N6 (7), Street/S7 (1), Street/O6 (7), Street/Q6 (1), Street/M6 (1) |
| Street/P7 | Outside | — | 8.3, -92.7 | 9 | Street/O7 (7), Street/M7 (7), Street/N7 (1), Street/G6 (7), Street/F6 (1) |
| Street/Q | Outside | — | 47.3, -57.4 | 12 | Street/S (10), Street/Y2 (7), Street/A3 (1), Street/P (10), Street/X2 (1), Street/R (7), Street/T (1), Street/O (1) |
| Street/Q2 | Outside | — | 67.6, -67.2 | 12 | Street/S2 (7), Street/T3 (10), Street/F4 (1), Street/O2 (7), Street/R2 (10), Street/T2 (1), Street/P2 (1) |
| Street/Q3 | Outside | — | 87.1, -70.9 | 4 | Street/R3 (4), Street/J2 (4), Street/L2 (1) |
| Street/Q4 | Outside | — | 72.8, -77.7 | 9 | Street/R4 (7), Street/P4 (7), Street/S4 (1), Street/N4 (7), Street/O4 (1), Street/E4 (7), Street/T3 (1), Street/S3 (1) |
| Street/Q5 | Outside | — | 87.1, -87.4 | 8 | Street/R5 (10), Street/I6 (4), Street/K6 (1), Street/K4 (4), Street/L4 (1) |
| Street/Q6 | Outside | — | 67.6, -92.7 | 12 | Street/S6 (7), Street/R6 (10), Street/T6 (1), Street/O6 (7), Street/P6 (1), Street/U5 (10), Street/V5 (1), Street/T5 (1) |
| Street/Q7 | Outside | — | 87.1, -102.4 | 8 | Street/R7 (10), Street/H8 (4), Pavement/C (1), Street/J6 (4), Street/L6 (1) |
| Street/R | Outside | — | 47.3, -53.7 | 3 | Street/T (1), Street/Q (7), Street/S (1), Street/O (1), Street/P (1) |
| Street/R2 | Outside | — | 67.6, -62.7 | 12 | Street/T2 (7), Street/Q2 (10), Street/S2 (1), Street/P2 (7), Street/O2 (1), Street/I (10), Street/L (1), Street/H (1) |
| Street/R3 | Outside | — | 82.4, -72.4 | 15 | Street/S3 (10), Street/M4 (10), Street/N4 (1), Street/Q3 (4), Street/L2 (10), Street/M2 (1), Street/J2 (1) |
| Street/R4 | Outside | — | 67.6, -77.7 | 12 | Street/T4 (7), Street/S4 (10), Street/U4 (1), Street/Q4 (7), Street/P4 (1), Street/T3 (10), Street/F4 (1), Street/E4 (1) |
| Street/R5 | Outside | — | 82.6, -87.4 | 16 | Street/S5 (10), Street/K6 (10), Street/M6 (1), Street/Q5 (10), Street/I6 (1), Street/L4 (10), Street/O4 (1), Street/K4 (1) |
| Street/R6 | Outside | — | 67.6, -97.1 | 12 | Street/T6 (7), Street/U7 (10), Street/V7 (1), Street/P6 (7), Street/T7 (1), Street/Q6 (10), Street/S6 (1), Street/O6 (1) |
| Street/R7 | Outside | — | 82.6, -102.4 | 16 | Street/S7 (10), Pavement/C (10), Pavement/D (1), Street/Q7 (10), Street/H8 (1), Street/L6 (10), Street/N6 (1), Street/J6 (1) |
| Street/S | Outside | — | 42.8, -57.4 | 12 | Street/U (10), Street/A3 (7), Street/C3 (1), Street/Q (10), Street/Y2 (1), Street/T (7), Street/V (1), Street/R (1) |
| Street/S2 | Outside | — | 62.3, -67.2 | 9 | Street/V2 (7), Street/Q2 (7), Street/F4 (1), Street/T3 (1), Street/T2 (7), Street/U2 (1), Street/R2 (1) |
| Street/S3 | Outside | — | 77.3, -72.4 | 12 | Street/E4 (7), Street/N4 (7), Street/Q4 (1), Street/R3 (10), Street/M4 (1), Street/M2 (7), Street/L2 (1) |
| Street/S4 | Outside | — | 67.6, -82.2 | 12 | Street/U4 (7), Street/U5 (10), Street/V5 (1), Street/P4 (7), Street/T5 (1), Street/R4 (10), Street/T4 (1), Street/Q4 (1) |
| Street/S5 | Outside | — | 77.3, -87.4 | 12 | Street/T5 (10), Street/M6 (7), Street/O6 (1), Street/R5 (10), Street/K6 (1), Street/O4 (7), Street/P4 (1), Street/L4 (1) |
| Street/S6 | Outside | — | 62.3, -92.7 | 9 | Street/V6 (7), Street/T6 (7), Street/U6 (1), Street/Q6 (7), Street/R6 (1), Street/V5 (7), Street/W5 (1), Street/U5 (1) |
| Street/S7 | Outside | — | 77.3, -102.4 | 12 | Street/T7 (10), Pavement/D (5), Pavement/E (1), Street/R7 (10), Pavement/C (1), Street/N6 (7), Street/P6 (1), Street/L6 (1) |
| Street/T | Outside | — | 42.8, -53.7 | 3 | Street/V (1), Street/S (7), Street/U (1), Street/R (1), Street/Q (1) |
| Street/T2 | Outside | — | 62.3, -62.7 | 9 | Street/U2 (7), Street/S2 (7), Street/V2 (1), Street/R2 (7), Street/Q2 (1), Street/L (7), Street/N (1), Street/I (1) |
| Street/T3 | Outside | — | 67.6, -72.4 | 16 | Street/F4 (10), Street/R4 (10), Street/T4 (1), Street/E4 (7), Street/Q4 (1), Street/Q2 (10), Street/S2 (1) |
| Street/T4 | Outside | — | 62.3, -77.7 | 9 | Street/W4 (7), Street/U4 (7), Street/V4 (1), Street/R4 (7), Street/S4 (1), Street/F4 (7), Street/U3 (1), Street/T3 (1) |
| Street/T5 | Outside | — | 72.8, -87.4 | 12 | Street/U5 (10), Street/O6 (7), Street/Q6 (1), Street/S5 (10), Street/M6 (1), Street/P4 (7), Street/S4 (1), Street/O4 (1) |
| Street/T6 | Outside | — | 62.3, -97.2 | 9 | Street/U6 (7), Street/V7 (7), Street/W7 (1), Street/R6 (7), Street/U7 (1), Street/S6 (7), Street/V6 (1), Street/Q6 (1) |
| Street/T7 | Outside | — | 72.9, -102.4 | 12 | Pavement/E (7), Street/S7 (10), Pavement/D (1), Street/U7 (8), Street/P6 (7), Street/R6 (1), Street/N6 (1) |
| Street/U | Outside | — | 37.6, -57.4 | 16 | Street/W (8), Street/C3 (10), Street/F3 (1), Street/S (10), Street/A3 (1), Street/V (10), Street/T (1) |
| Street/U2 | Outside | — | 57.8, -62.7 | 9 | Street/X2 (7), Street/V2 (7), Street/W2 (1), Street/T2 (7), Street/S2 (1), Street/N (7), Street/P (1), Street/L (1) |
| Street/U3 | Outside | — | 57.8, -72.4 | 12 | Street/V3 (10), Street/W4 (7), Street/X4 (1), Street/F4 (7), Street/T4 (1), Street/V2 (7), Street/W2 (1) |
| Street/U4 | Outside | — | 62.3, -82.2 | 9 | Street/V4 (7), Street/V5 (7), Street/W5 (1), Street/S4 (7), Street/U5 (1), Street/T4 (7), Street/W4 (1), Street/R4 (1) |
| Street/U5 | Outside | — | 67.6, -87.4 | 16 | Street/V5 (10), Street/Q6 (10), Street/S6 (1), Street/T5 (10), Street/O6 (1), Street/S4 (10), Street/U4 (1), Street/P4 (1) |
| Street/U6 | Outside | — | 57.8, -97.2 | 9 | Street/W6 (7), Street/W7 (7), Street/X7 (1), Street/T6 (7), Street/V7 (1), Street/V6 (7), Street/X6 (1), Street/S6 (1) |
| Street/U7 | Outside | — | 67.6, -102.4 | 16 | Street/V7 (10), Pavement/A (8), Pavement/F (1), Street/T7 (8), Street/R6 (10), Street/T6 (1), Street/P6 (1) |
| Street/V | Outside | — | 37.6, -53.7 | 4 | Street/X (1), Street/U (10), Street/W (1), Street/T (1), Street/S (1) |
| Street/V2 | Outside | — | 57.8, -67.2 | 9 | Street/W2 (7), Street/U3 (7), Street/V3 (1), Street/S2 (7), Street/U2 (7), Street/X2 (1), Street/T2 (1) |
| Street/V3 | Outside | — | 52.6, -72.4 | 16 | Street/G4 (7), Street/X4 (10), Street/A5 (1), Street/U3 (10), Street/W4 (1), Street/W2 (10), Street/V2 (1) |
| Street/V4 | Outside | — | 57.8, -82.2 | 9 | Street/Y4 (7), Street/W5 (7), Street/X5 (1), Street/U4 (7), Street/V5 (1), Street/W4 (7), Street/X4 (1), Street/T4 (1) |
| Street/V5 | Outside | — | 62.3, -87.4 | 12 | Street/W5 (10), Street/S6 (7), Street/V6 (1), Street/U5 (10), Street/Q6 (1), Street/U4 (7), Street/V4 (1), Street/S4 (1) |
| Street/V6 | Outside | — | 57.8, -92.7 | 9 | Street/X6 (7), Street/U6 (7), Street/W6 (1), Street/S6 (7), Street/T6 (1), Street/W5 (7), Street/X5 (1), Street/V5 (1) |
| Street/V7 | Outside | — | 62.3, -102.4 | 12 | Street/W7 (10), Pavement/F (7), Pavement/G (1), Street/U7 (10), Pavement/A (1), Street/T6 (7), Street/U6 (1), Street/R6 (1) |
| Street/W | Outside | — | 32.3, -57.4 | 12 | Street/Z (10), Street/F3 (7), Street/G3 (1), Street/U (8), Street/C3 (1), Street/X (7), Street/Y (1) |
| Street/W2 | Outside | — | 52.6, -67.2 | 12 | Street/Z2 (7), Street/V3 (10), Street/V2 (7), Street/U3 (1), Street/X2 (10), Street/Y2 (1), Street/U2 (1) |
| Street/W3 | Outside | — | 42.8, -72.4 | 12 | Street/X3 (10), Street/B5 (7), Street/D5 (1), Street/G4 (7), Street/A5 (1), Street/B3 (7), Street/D3 (1) |
| Street/W4 | Outside | — | 57.8, -77.7 | 9 | Street/X4 (7), Street/V4 (7), Street/Y4 (1), Street/T4 (7), Street/U4 (1), Street/U3 (7), Street/V3 (1), Street/F4 (1) |
| Street/W5 | Outside | — | 57.8, -87.4 | 12 | Street/X5 (10), Street/V6 (7), Street/X6 (1), Street/V5 (10), Street/S6 (1), Street/V4 (7), Street/Y4 (1), Street/U4 (1) |
| Street/W6 | Outside | — | 52.6, -97.2 | 12 | Street/Y6 (7), Street/X7 (10), Street/Y7 (1), Street/U6 (7), Street/W7 (1), Street/X6 (10), Street/Z6 (1), Street/V6 (1) |
| Street/W7 | Outside | — | 57.8, -102.4 | 12 | Pavement/G (7), Street/V7 (10), Pavement/F (1), Street/X7 (8), Street/U6 (7), Street/W6 (1), Street/T6 (1) |
| Street/X | Outside | — | 32.3, -53.7 | 3 | Street/Y (1), Street/W (7), Street/Z (1), Street/V (1), Street/U (1) |
| Street/X2 | Outside | — | 52.6, -62.7 | 12 | Street/Y2 (7), Street/W2 (10), Street/Z2 (1), Street/U2 (7), Street/V2 (1), Street/P (10), Street/Q (1), Street/N (1) |
| Street/X3 | Outside | — | 37.6, -72.4 | 16 | Street/Y3 (10), Street/D5 (9), Street/G5 (1), Street/W3 (10), Street/B5 (1), Street/D3 (10), Street/E3 (1), Street/B3 (1) |
| Street/X4 | Outside | — | 52.6, -77.7 | 12 | Street/A5 (7), Street/Y4 (10), Street/Z4 (1), Street/W4 (7), Street/V4 (1), Street/V3 (10), Street/G4 (1), Street/U3 (1) |
| Street/X5 | Outside | — | 52.6, -87.4 | 16 | Street/Y5 (10), Street/X6 (10), Street/H6 (1), Street/W5 (10), Street/V6 (1), Street/Y4 (10), Street/Z4 (1), Street/V4 (1) |
| Street/X6 | Outside | — | 52.6, -92.7 | 12 | Street/Z6 (1), Street/W6 (10), Street/Y6 (1), Street/V6 (7), Street/U6 (1), Street/H6 (1), Street/X5 (10), Street/Y5 (1), Street/W5 (1) |
| Street/X7 | Outside | — | 52.6, -102.4 | 16 | Street/Y7 (10), Pavement/H (10), Pavement/I (1), Street/W7 (8), Street/W6 (10), Street/Y6 (1), Street/U6 (1) |
| Street/Y | Outside | — | 27.8, -53.7 | 3 | Street/B2 (1), Street/Z (7), Street/A2 (1), Street/X (1), Street/W (1) |
| Street/Y2 | Outside | — | 47.3, -62.7 | 9 | Street/A3 (7), Street/Z2 (7), Street/B3 (1), Street/X2 (7), Street/W2 (1), Street/Q (7), Street/S (1), Street/P (1) |
| Street/Y3 | Outside | — | 32.3, -72.4 | 12 | Street/Z3 (10), Street/G5 (5), Street/H5 (1), Street/X3 (10), Street/D5 (1), Street/E3 (7), Street/H3 (1), Street/D3 (1) |
| Street/Y4 | Outside | — | 52.6, -82.2 | 12 | Street/Z4 (7), Street/X5 (10), Street/Y5 (1), Street/V4 (7), Street/W5 (1), Street/X4 (10), Street/A5 (1), Street/W4 (1) |
| Street/Y5 | Outside | — | 47.3, -87.4 | 12 | Street/Z5 (10), Street/H6 (7), Street/B7 (1), Street/X5 (10), Street/X6 (1), Street/Z4 (7), Street/C5 (1), Street/Y4 (1) |
| Street/Y6 | Outside | — | 47.3, -97.2 | 9 | Street/A7 (7), Street/Y7 (7), Street/Z7 (1), Street/W6 (7), Street/X7 (1), Street/Z6 (7), Street/B7 (1), Street/X6 (1) |
| Street/Y7 | Outside | — | 47.3, -102.4 | 12 | Street/Z7 (10), Pavement/I (6), Pavement/B (1), Street/X7 (10), Pavement/H (1), Street/Y6 (7), Street/A7 (1), Street/W6 (1) |
| Street/Z | Outside | — | 27.8, -57.4 | 12 | Street/A2 (10), Street/G3 (7), Street/J3 (1), Street/W (10), Street/F3 (1), Street/Y (7), Street/B2 (1), Street/X (1) |
| Street/Z2 | Outside | — | 47.3, -67.2 | 9 | Street/B3 (7), Street/W2 (7), Street/Y2 (7), Street/A3 (1), Street/X2 (1) |
| Street/Z3 | Outside | — | 27.8, -72.4 | 12 | Street/A4 (10), Street/H5 (7), Street/J5 (1), Street/Y3 (10), Street/G5 (1), Street/H3 (7), Street/I3 (1), Street/E3 (1) |
| Street/Z4 | Outside | — | 47.3, -82.2 | 9 | Street/C5 (7), Street/Y5 (7), Street/Z5 (1), Street/Y4 (7), Street/X5 (1), Street/A5 (7), Street/B5 (1), Street/X4 (1) |
| Street/Z5 | Outside | — | 42.8, -87.4 | 12 | Street/A6 (10), Street/B7 (7), Street/D7 (1), Street/Y5 (10), Street/H6 (1), Street/C5 (7), Street/E5 (1), Street/Z4 (1) |
| Street/Z6 | Outside | — | 47.3, -94.2 | 3 | Street/B7 (1), Street/Y6 (7), Street/A7 (1), Street/X6 (1), Street/W6 (1) |
| Street/Z7 | Outside | — | 42.8, -102.4 | 12 | Street/A8 (10), Pavement/B (5), Pavement/J (1), Street/Y7 (10), Street/A7 (7), Street/C7 (1), Street/Y6 (1) |
| West alley/A | Outside | — | 8.1, -112.2 | 9 | West alley/B (7), Street/I8 (4) |
| West alley/B | Outside | — | 8.1, -117.4 | 12 | West alley/C (7), West alley/A (7) |
| West alley/C | Outside | — | 8.1, -122.7 | 9 | West alley/D (7), West alley/B (7) |
| West alley/D | Outside | — | 8.1, -127.2 | 9 | West alley/E (7), West alley/C (7) |
| West alley/E | Outside | — | 8.1, -132.4 | 12 | West alley/F (7), West alley/D (7) |
| West alley/F | Outside | — | 8.1, -137.7 | 9 | West alley/G (7), West alley/E (7) |
| West alley/G | Outside | — | 8.1, -142.2 | 9 | West alley/H (7), West alley/F (7) |
| West alley/H | Outside | — | 8.2, -147.4 | 12 | West alley/I (7), West alley/G (7) |
| West alley/I | Outside | — | 8.3, -152.7 | 9 | West alley/J (7), Backstreet/F (7), Backstreet/E (1), West alley/H (7) |
| West alley/J | Outside | — | 8.3, -157.1 | 9 | West alley/K (7), Backstreet/E (2), Backstreet/I (1), West alley/I (7), Backstreet/F (1) |
| West alley/K | Outside | — | 8.3, -162.4 | 12 | West alley/L (7), Backstreet/I (10), Backstreet/O (1), West alley/J (7), Backstreet/E (1) |
| West alley/L | Outside | — | 8.3, -167.7 | 9 | West alley/M (7), Backstreet/O (2), West alley/K (7) |
| West alley/M | Outside | — | 8.3, -172.2 | 9 | West alley/N (7), Backstreet/N (2), West alley/L (7) |
| West alley/N | Outside | — | 8.1, -177.4 | 12 | West alley/P (7), West alley/M (7) |
| West alley/O | Outside | — | 8.3, -187.2 | 8 | West alley/Q (7), West alley/P (7) |
| West alley/P | Outside | — | 8.1, -182.7 | 9 | West alley/O (7), West alley/N (7) |
| West alley/Q | Outside | — | 8.3, -191.7 | 9 | South yard/O (7), West alley/O (7) |
| Aisle 1/A | Sales floor | Aisle 1 · Fruit & Veg | 82.5, -122.6 | 12 | Aisle 1/D (7), Aisle 1/B (10), Aisle 1/C (1) |
| Aisle 1/A2 | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -141.4 | 8 | Aisle 1/C2 (4), Aisle 1/Y (4), Aisle 1/Z (10), Aisle 1/B2 (1), Aisle 1/X (1) |
| Aisle 1/B | Sales floor | Aisle 1 · Fruit & Veg | 82.5, -126.4 | 8 | Aisle 1/C (4), Aisle 1/A (10), Aisle 1/D (1) |
| Aisle 1/B2 | Sales floor | Aisle 1 · Fruit & Veg | 62.3, -139.8 | 3 | Aisle 2/R (1), Aisle 1/C2 (7), Aisle 2/S (1), Aisle 1/Z (1), Aisle 1/A2 (1) |
| Aisle 1/C | Sales floor | Aisle 1 · Fruit & Veg | 77.3, -126.4 | 6 | Aisle 1/F (4), Aisle 1/B (4), Aisle 1/D (7), Aisle 1/E (1), Aisle 1/A (1) |
| Aisle 1/C2 | Sales floor | Aisle 1 · Fruit & Veg | 62.3, -141.4 | 6 | Aisle 2/S (4), Aisle 1/A2 (4), Aisle 1/B2 (7), Aisle 2/R (1), Aisle 1/Z (1) |
| Aisle 1/D | Sales floor | Aisle 1 · Fruit & Veg | 77.3, -122.7 | 9 | Aisle 1/E (7), Aisle 1/C (7), Aisle 1/F (1), Aisle 1/A (7), Aisle 1/B (1) |
| Aisle 1/E | Sales floor | Aisle 1 · Fruit & Veg | 72.8, -122.7 | 9 | Aisle 1/H (7), Aisle 1/F (7), Aisle 1/G (1), Aisle 1/D (7), Aisle 1/C (1) |
| Aisle 1/F | Sales floor | Aisle 1 · Fruit & Veg | 72.6, -126.7 | 7 | Aisle 1/G (7), Aisle 1/M (1), Aisle 1/N (1), Aisle 1/C (4), Aisle 1/E (7), Aisle 1/H (1), Aisle 1/D (1) |
| Aisle 1/G | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -127.2 | 12 | Aisle 1/I (7), Aisle 1/N (10), Aisle 1/O (1), Aisle 1/F (7), Aisle 1/M (1), Aisle 1/H (10), Aisle 1/J (1), Aisle 1/E (1) |
| Aisle 1/H | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -123 | 10 | Aisle 1/J (7), Aisle 1/G (10), Aisle 1/I (1), Aisle 1/E (7), Aisle 1/F (1), Doors: Lobby - Sales floor (2) (7) |
| Aisle 1/I | Sales floor | Aisle 1 · Fruit & Veg | 62.6, -127 | 8 | Aisle 1/O (4), Aisle 1/G (7), Aisle 1/N (1), Aisle 2/B (4), Aisle 1/J (7), Aisle 2/A (1), Aisle 1/H (1) |
| Aisle 1/J | Sales floor | Aisle 1 · Fruit & Veg | 62.3, -122.7 | 9 | Aisle 2/A (7), Aisle 1/I (7), Aisle 2/B (1), Aisle 1/H (7), Aisle 1/G (1) |
| Aisle 1/K | Sales floor | Aisle 1 · Fruit & Veg | 81.6, -131.5 | 9 | Aisle 1/L (7) |
| Aisle 1/L | Sales floor | Aisle 1 · Fruit & Veg | 77.3, -131.5 | 9 | Aisle 1/M (7), Aisle 1/K (7) |
| Aisle 1/M | Sales floor | Aisle 1 · Fruit & Veg | 72.8, -132.3 | 12 | Aisle 1/N (10), Aisle 1/S (7), Aisle 1/T (1), Aisle 1/Q (1), Aisle 1/R (1), Aisle 1/L (7), Aisle 1/F (1), Aisle 1/G (1) |
| Aisle 1/N | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -132.4 | 16 | Aisle 1/O (10), Aisle 1/T (10), Aisle 1/U (1), Aisle 1/M (10), Aisle 1/S (1), Aisle 1/G (10), Aisle 1/I (1), Aisle 1/F (1) |
| Aisle 1/O | Sales floor | Aisle 1 · Fruit & Veg | 62.3, -132.3 | 12 | Aisle 2/M (1), Aisle 1/U (7), Aisle 2/R (1), Aisle 1/N (10), Aisle 1/T (1), Aisle 2/I (7), Aisle 1/I (4), Aisle 1/G (1) |
| Aisle 1/P | Sales floor | Aisle 1 · Fruit & Veg | 81.6, -134.8 | 3 | Aisle 1/Q (1), Aisle 1/V (7), Aisle 1/R (1) |
| Aisle 1/Q | Sales floor | Aisle 1 · Fruit & Veg | 77.3, -134.8 | 3 | Aisle 1/M (1), Aisle 1/R (7), Aisle 1/S (1), Aisle 1/P (1), Aisle 1/V (1) |
| Aisle 1/R | Sales floor | Aisle 1 · Fruit & Veg | 77.3, -136.9 | 6 | Aisle 1/S (4), Aisle 1/V (4), Aisle 1/Q (7), Aisle 1/M (1), Aisle 1/P (1) |
| Aisle 1/S | Sales floor | Aisle 1 · Fruit & Veg | 72.8, -136.9 | 6 | Aisle 1/T (4), Aisle 1/R (4), Aisle 1/M (7), Aisle 1/N (1), Aisle 1/Q (1) |
| Aisle 1/T | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -136.9 | 8 | Aisle 1/U (4), Aisle 1/S (4), Aisle 1/N (10), Aisle 1/O (1), Aisle 1/M (1) |
| Aisle 1/U | Sales floor | Aisle 1 · Fruit & Veg | 62.3, -136.9 | 6 | Aisle 2/R (4), Aisle 1/T (4), Aisle 1/O (7), Aisle 2/M (1), Aisle 1/N (1) |
| Aisle 1/V | Sales floor | Aisle 1 · Fruit & Veg | 81.6, -137.7 | 9 | Aisle 3/A (7), Aisle 1/R (4), Aisle 1/P (7), Aisle 1/Q (1) |
| Aisle 1/W | Sales floor | Aisle 1 · Fruit & Veg | 76.6, -139.8 | 2 | Aisle 1/X (1), Aisle 3/B (4), Aisle 1/Y (1) |
| Aisle 1/X | Sales floor | Aisle 1 · Fruit & Veg | 72.8, -139.8 | 3 | Aisle 1/Z (1), Aisle 1/Y (7), Aisle 1/A2 (1), Aisle 1/W (1), Aisle 3/B (1) |
| Aisle 1/Y | Sales floor | Aisle 1 · Fruit & Veg | 72.8, -141.4 | 6 | Aisle 1/A2 (4), Aisle 3/B (4), Aisle 1/X (7), Aisle 1/Z (1), Aisle 1/W (1) |
| Aisle 1/Z | Sales floor | Aisle 1 · Fruit & Veg | 67.6, -139.8 | 4 | Aisle 1/B2 (1), Aisle 1/A2 (10), Aisle 1/C2 (1), Aisle 1/X (1), Aisle 1/Y (1) |
| Aisle 10/A | Sales floor | Aisle 10 · Dairy & Chilled | 73.8, -163.2 | 6 | Aisle 10/C (1), Aisle 10/B (7), Aisle 10/D (1), Aisle 3/J (10), Aisle 10/J (1), Aisle 5/A (1), Aisle 3/G (1) |
| Aisle 10/B | Sales floor | Aisle 10 · Dairy & Chilled | 72.8, -166.9 | 6 | Aisle 10/D (4), Aisle 10/J (4), Aisle 10/A (7), Aisle 10/C (1), Aisle 3/J (1) |
| Aisle 10/C | Sales floor | Aisle 10 · Dairy & Chilled | 67.6, -164.8 | 4 | Aisle 10/F (1), Aisle 10/D (10), Aisle 10/E (1), Aisle 10/A (1), Aisle 10/B (1) |
| Aisle 10/D | Sales floor | Aisle 10 · Dairy & Chilled | 67.6, -166.9 | 8 | Aisle 10/E (4), Aisle 10/B (4), Aisle 10/C (10), Aisle 10/F (1), Aisle 10/A (1) |
| Aisle 10/E | Sales floor | Aisle 10 · Dairy & Chilled | 62.3, -166.9 | 6 | Aisle 11/A (4), Aisle 10/D (4), Aisle 10/F (7), Aisle 11/B (1), Aisle 10/C (1) |
| Aisle 10/F | Sales floor | Aisle 10 · Dairy & Chilled | 62.3, -164.8 | 3 | Aisle 11/B (1), Aisle 10/E (7), Aisle 11/A (1), Aisle 10/C (1), Aisle 10/D (1) |
| Aisle 10/G | Sales floor | Aisle 10 · Dairy & Chilled | 81.8, -172 | 9 | Aisle 10/I (7), Aisle 10/H (7), Aisle 10/J (1) |
| Aisle 10/H | Sales floor | Aisle 10 · Dairy & Chilled | 81.6, -167.7 | 9 | Aisle 10/J (7), Aisle 10/G (7), Aisle 10/I (1), Aisle 3/I (7) |
| Aisle 10/I | Sales floor | Aisle 10 · Dairy & Chilled | 77.3, -172 | 9 | Aisle 10/L (7), Aisle 10/G (7), Aisle 10/J (7), Aisle 10/K (1), Aisle 10/H (1) |
| Aisle 10/J | Sales floor | Aisle 10 · Dairy & Chilled | 77.3, -167.7 | 9 | Aisle 10/K (1), Aisle 10/I (7), Aisle 10/L (1), Aisle 10/H (7), Aisle 10/G (1), Aisle 10/B (4), Aisle 3/J (4), Aisle 10/A (1) |
| Aisle 10/K | Sales floor | Aisle 10 · Dairy & Chilled | 72.8, -169.8 | 3 | Aisle 10/M (1), Aisle 10/L (7), Aisle 10/N (1), Aisle 10/J (1), Aisle 10/I (1) |
| Aisle 10/L | Sales floor | Aisle 10 · Dairy & Chilled | 72.8, -172 | 9 | Aisle 10/N (7), Aisle 10/I (7), Aisle 10/K (7), Aisle 10/M (1), Aisle 10/J (1) |
| Aisle 10/M | Sales floor | Aisle 10 · Dairy & Chilled | 67.6, -169.8 | 4 | Aisle 10/P (1), Aisle 10/N (10), Aisle 10/O (1), Aisle 10/K (1), Aisle 10/L (1) |
| Aisle 10/N | Sales floor | Aisle 10 · Dairy & Chilled | 67.6, -172.2 | 12 | Aisle 10/O (7), Door: Cold storage - Sales floor (4), Aisle 10/L (7), Aisle 10/M (10), Aisle 10/P (1), Aisle 10/K (1) |
| Aisle 10/O | Sales floor | Aisle 10 · Dairy & Chilled | 62.3, -172 | 9 | Aisle 11/F (7), Aisle 10/N (7), Aisle 10/P (7), Aisle 11/G (1), Aisle 10/M (1) |
| Aisle 10/P | Sales floor | Aisle 10 · Dairy & Chilled | 62.3, -169.8 | 3 | Aisle 11/G (1), Aisle 10/O (7), Aisle 11/F (1), Aisle 10/M (1), Aisle 10/N (1) |
| Aisle 11/A | Sales floor | Aisle 11 · Frozen | 57.8, -166.9 | 6 | Aisle 11/H (4), Aisle 10/E (4), Aisle 11/B (7), Aisle 11/C (1), Aisle 10/F (1) |
| Aisle 11/B | Sales floor | Aisle 11 · Frozen | 57.8, -164.8 | 3 | Aisle 11/C (1), Aisle 11/A (7), Aisle 11/H (1), Aisle 10/F (1), Aisle 10/E (1) |
| Aisle 11/C | Sales floor | Aisle 11 · Frozen | 52.6, -164.8 | 4 | Aisle 11/E (1), Aisle 11/H (10), Aisle 11/D (1), Aisle 11/B (1), Aisle 11/A (1) |
| Aisle 11/D | Sales floor | Aisle 11 · Frozen | 47.3, -166.9 | 6 | Aisle 12/A (4), Aisle 11/H (4), Aisle 11/E (7), Aisle 12/B (1), Aisle 11/C (1) |
| Aisle 11/E | Sales floor | Aisle 11 · Frozen | 47.3, -164.8 | 3 | Aisle 12/B (1), Aisle 11/D (7), Aisle 12/A (1), Aisle 11/C (1), Aisle 11/H (1) |
| Aisle 11/F | Sales floor | Aisle 11 · Frozen | 57.8, -172 | 9 | Aisle 11/I (7), Aisle 10/O (7), Aisle 11/G (7), Aisle 11/H (1), Aisle 10/P (1) |
| Aisle 11/G | Sales floor | Aisle 11 · Frozen | 57.8, -169.8 | 3 | Aisle 11/H (1), Aisle 11/F (7), Aisle 11/I (1), Aisle 10/P (1), Aisle 10/O (1) |
| Aisle 11/H | Sales floor | Aisle 11 · Frozen | 52.6, -167.8 | 12 | Aisle 11/K (1), Aisle 11/I (10), Aisle 11/J (1), Aisle 11/G (1), Aisle 11/F (1), Aisle 11/D (4), Aisle 11/A (4), Aisle 11/C (10), Aisle 11/E (1), Aisle 11/B (1) |
| Aisle 11/I | Sales floor | Aisle 11 · Frozen | 52.6, -172 | 12 | Aisle 11/J (7), Aisle 11/F (7), Aisle 11/H (10), Aisle 11/K (1), Aisle 11/G (1) |
| Aisle 11/J | Sales floor | Aisle 11 · Frozen | 47.3, -172 | 9 | Aisle 12/G (7), Aisle 11/I (7), Aisle 11/K (7), Aisle 12/H (1), Aisle 11/H (1) |
| Aisle 11/K | Sales floor | Aisle 11 · Frozen | 47.3, -169.8 | 3 | Aisle 12/H (1), Aisle 11/J (7), Aisle 12/G (1), Aisle 11/H (1), Aisle 11/I (1) |
| Aisle 12/A | Sales floor | Aisle 12 · Pet | 42.8, -166.9 | 6 | Aisle 12/C (4), Aisle 11/D (4), Aisle 12/B (7), Aisle 12/D (1), Aisle 11/E (1) |
| Aisle 12/B | Sales floor | Aisle 12 · Pet | 42.8, -164.8 | 3 | Aisle 12/D (1), Aisle 12/A (7), Aisle 12/C (1), Aisle 11/E (1), Aisle 11/D (1) |
| Aisle 12/C | Sales floor | Aisle 12 · Pet | 37.6, -166.9 | 8 | Aisle 12/E (4), Aisle 12/A (4), Aisle 12/D (10), Aisle 9/H (1), Aisle 12/B (1) |
| Aisle 12/D | Sales floor | Aisle 12 · Pet | 37.6, -164.8 | 4 | Aisle 9/H (1), Aisle 12/C (10), Aisle 12/E (1), Aisle 12/B (1), Aisle 12/A (1) |
| Aisle 12/E | Sales floor | Aisle 12 · Pet | 32.3, -166.9 | 6 | Aisle 12/F (4), Aisle 12/C (4), Aisle 9/H (7), Aisle 9/I (1), Aisle 12/D (1) |
| Aisle 12/F | Sales floor | Aisle 12 · Pet | 29.8, -166.9 | 2 | Aisle 12/E (4), Aisle 9/I (1), Aisle 9/H (1) |
| Aisle 12/G | Sales floor | Aisle 12 · Pet | 42.8, -172.2 | 9 | Aisle 12/I (7), Door: Sales floor - Stockroom (2), Aisle 11/J (7), Aisle 12/H (7), Aisle 12/J (1), Aisle 11/K (1) |
| Aisle 12/H | Sales floor | Aisle 12 · Pet | 42.8, -169.8 | 3 | Aisle 12/J (1), Aisle 12/G (7), Aisle 12/I (1), Aisle 11/K (1), Aisle 11/J (1) |
| Aisle 12/I | Sales floor | Aisle 12 · Pet | 37.6, -172 | 12 | Aisle 12/K (7), Aisle 12/G (7), Aisle 12/J (10), Aisle 12/L (1), Aisle 12/H (1) |
| Aisle 12/J | Sales floor | Aisle 12 · Pet | 37.6, -169.8 | 4 | Aisle 12/L (1), Aisle 12/I (10), Aisle 12/K (1), Aisle 12/H (1), Aisle 12/G (1) |
| Aisle 12/K | Sales floor | Aisle 12 · Pet | 32.3, -172 | 9 | Aisle 12/M (7), Aisle 12/I (7), Aisle 12/L (7), Aisle 12/N (1), Aisle 12/J (1) |
| Aisle 12/L | Sales floor | Aisle 12 · Pet | 32.3, -169.8 | 3 | Aisle 12/N (1), Aisle 12/K (7), Aisle 12/M (1), Aisle 12/J (1), Aisle 12/I (1) |
| Aisle 12/M | Sales floor | Aisle 12 · Pet | 28, -172 | 9 | Aisle 12/K (7), Aisle 12/N (7), Aisle 12/L (1) |
| Aisle 12/N | Sales floor | Aisle 12 · Pet | 27.4, -167.9 | 7 | Aisle 12/M (7), Aisle 12/L (1), Aisle 12/K (1), Aisle 9/J (4) |
| Aisle 13/A | Sales floor | Aisle 13 · Sweets | 37.6, -122.7 | 12 | Aisle 13/C (7), Aisle 13/F (10), Aisle 13/B (1), Aisle 2/H (5), Aisle 2/G (1), Doors: Lobby - Sales floor (1) |
| Aisle 13/B | Sales floor | Aisle 13 · Sweets | 32.5, -127.4 | 10 | Aisle 13/F (10), Aisle 13/I (6), Aisle 13/D (8), Aisle 13/C (7), Aisle 13/E (1), Aisle 13/A (1) |
| Aisle 13/C | Sales floor | Aisle 13 · Sweets | 32.3, -122.7 | 9 | Aisle 13/E (7), Aisle 13/B (7), Aisle 13/D (1), Aisle 13/A (7), Aisle 13/F (1) |
| Aisle 13/D | Sales floor | Aisle 13 · Sweets | 28, -127.4 | 10 | Aisle 13/I (2), Aisle 13/J (6), Aisle 13/B (8), Aisle 13/E (7), Aisle 13/C (1) |
| Aisle 13/E | Sales floor | Aisle 13 · Sweets | 27.8, -122.7 | 9 | Aisle 13/D (7), Aisle 13/C (7), Aisle 13/B (1) |
| Aisle 13/F | Sales floor | Aisle 13 · Sweets | 37.6, -127.6 | 15 | Aisle 13/H (5), Aisle 13/B (10), Aisle 2/L (2), Aisle 2/G (8), Aisle 13/A (10), Aisle 13/C (1), Aisle 2/H (1) |
| Aisle 13/G | Sales floor | Aisle 13 · Sweets | 37.6, -136.9 | 8 | Aisle 13/M (4), Aisle 2/Q (4), Aisle 13/H (4) |
| Aisle 13/H | Sales floor | Aisle 13 · Sweets | 37.5, -132.7 | 7 | Aisle 13/G (4), Aisle 13/F (6) |
| Aisle 13/I | Sales floor | Aisle 13 · Sweets | 32.3, -132.6 | 11 | Aisle 13/M (7), Aisle 13/D (2), Aisle 13/B (6) |
| Aisle 13/J | Sales floor | Aisle 13 · Sweets | 27.6, -132.6 | 11 | Aisle 13/O (7), Door: Office - Sales floor (1), Aisle 13/D (6) |
| Aisle 13/K | Sales floor | Aisle 13 · Sweets | 37.6, -141.5 | 8 | Aisle 13/N (4), Aisle 2/Y (4), Aisle 13/L (4) |
| Aisle 13/L | Sales floor | Aisle 13 · Sweets | 37.6, -139.8 | 2 | Aisle 13/K (4) |
| Aisle 13/M | Sales floor | Aisle 13 · Sweets | 32.3, -137.7 | 9 | Aisle 13/O (7), Aisle 13/N (7), Aisle 13/G (4), Aisle 13/I (7) |
| Aisle 13/N | Sales floor | Aisle 13 · Sweets | 32.2, -142 | 8 | Aisle 8/A (7), Aisle 8/D (4), Aisle 8/E (1), Aisle 13/K (4), Aisle 13/M (7) |
| Aisle 13/O | Sales floor | Aisle 13 · Sweets | 27.8, -137.7 | 9 | Aisle 8/A (7), Aisle 13/M (7), Aisle 13/J (7), Aisle 13/I (1) |
| Aisle 2/A | Sales floor | Aisle 2 · Bakery | 57.8, -122.7 | 9 | Aisle 2/D (7), Aisle 2/B (7), Aisle 2/C (1), Aisle 1/J (7), Aisle 1/I (1) |
| Aisle 2/B | Sales floor | Aisle 2 · Bakery | 57.8, -126.4 | 6 | Aisle 2/C (4), Aisle 1/I (4), Aisle 2/A (7), Aisle 2/D (1), Aisle 1/J (1) |
| Aisle 2/C | Sales floor | Aisle 2 · Bakery | 52.6, -126.4 | 8 | Aisle 2/F (4), Aisle 2/B (4), Aisle 2/D (10), Aisle 2/E (1), Aisle 2/A (1) |
| Aisle 2/D | Sales floor | Aisle 2 · Bakery | 52.6, -122.7 | 12 | Aisle 2/E (7), Aisle 2/C (10), Aisle 2/F (1), Aisle 2/A (7), Aisle 2/B (1) |
| Aisle 2/E | Sales floor | Aisle 2 · Bakery | 47.3, -122.7 | 9 | Aisle 2/H (5), Aisle 2/F (7), Aisle 2/G (1), Aisle 2/D (7), Aisle 2/C (1), Doors: Lobby - Sales floor (2) |
| Aisle 2/F | Sales floor | Aisle 2 · Bakery | 47.1, -127 | 8 | Aisle 2/G (7), Aisle 2/K (4), Aisle 2/L (1), Aisle 2/C (4), Aisle 2/E (7), Aisle 2/H (1), Aisle 2/D (1) |
| Aisle 2/G | Sales floor | Aisle 2 · Bakery | 42.8, -127.2 | 9 | Aisle 13/F (8), Aisle 2/L (7), Aisle 2/F (7), Aisle 2/K (1), Aisle 2/H (7), Aisle 13/A (1), Aisle 2/E (1) |
| Aisle 2/H | Sales floor | Aisle 2 · Bakery | 42.8, -123.5 | 6 | Aisle 13/A (5), Aisle 2/G (7), Aisle 13/F (1), Aisle 2/E (4), Aisle 2/F (1), Doors: Lobby - Sales floor (5) |
| Aisle 2/I | Sales floor | Aisle 2 · Bakery | 57.8, -131.5 | 9 | Aisle 2/J (7), Aisle 1/O (7) |
| Aisle 2/J | Sales floor | Aisle 2 · Bakery | 52.6, -131.5 | 12 | Aisle 2/K (7), Aisle 2/I (7) |
| Aisle 2/K | Sales floor | Aisle 2 · Bakery | 47.3, -132.4 | 12 | Aisle 2/L (10), Aisle 2/P (7), Aisle 2/Q (1), Aisle 2/O (1), Aisle 2/N (1), Aisle 2/J (7), Aisle 2/F (4), Aisle 2/G (1) |
| Aisle 2/L | Sales floor | Aisle 2 · Bakery | 42.8, -132.4 | 12 | Aisle 2/Q (7), Aisle 2/K (10), Aisle 2/P (1), Aisle 13/F (2), Aisle 2/G (7), Aisle 2/F (1) |
| Aisle 2/M | Sales floor | Aisle 2 · Bakery | 57.8, -134.8 | 3 | Aisle 2/O (1), Aisle 2/R (7), Aisle 2/N (1), Aisle 1/O (1), Aisle 1/U (1) |
| Aisle 2/N | Sales floor | Aisle 2 · Bakery | 52.6, -136.9 | 8 | Aisle 2/P (4), Aisle 2/R (4), Aisle 2/O (10), Aisle 2/K (1), Aisle 2/M (1) |
| Aisle 2/O | Sales floor | Aisle 2 · Bakery | 52.6, -134.8 | 4 | Aisle 2/K (1), Aisle 2/N (10), Aisle 2/P (1), Aisle 2/M (1), Aisle 2/R (1) |
| Aisle 2/P | Sales floor | Aisle 2 · Bakery | 47.3, -136.9 | 6 | Aisle 2/Q (4), Aisle 2/N (4), Aisle 2/K (7), Aisle 2/L (1), Aisle 2/O (1) |
| Aisle 2/Q | Sales floor | Aisle 2 · Bakery | 42.8, -136.9 | 6 | Aisle 13/G (4), Aisle 2/P (4), Aisle 2/L (7), Aisle 2/K (1) |
| Aisle 2/R | Sales floor | Aisle 2 · Bakery | 57.8, -137.8 | 9 | Aisle 2/U (2), Aisle 2/S (7), Aisle 2/N (5), Aisle 2/T (1), Aisle 1/B2 (1), Aisle 1/C2 (1), Aisle 1/U (4), Aisle 2/M (7), Aisle 2/O (1), Aisle 1/O (1) |
| Aisle 2/S | Sales floor | Aisle 2 · Bakery | 57.8, -141.4 | 6 | Aisle 2/T (4), Aisle 1/C2 (4), Aisle 2/R (7), Aisle 2/U (1), Aisle 1/B2 (1) |
| Aisle 2/T | Sales floor | Aisle 2 · Bakery | 52.6, -141.4 | 8 | Aisle 2/W (4), Aisle 2/S (4), Aisle 2/U (10), Aisle 2/V (1), Aisle 2/R (1) |
| Aisle 2/U | Sales floor | Aisle 2 · Bakery | 52.6, -139.8 | 4 | Aisle 2/V (1), Aisle 2/T (10), Aisle 2/W (1), Aisle 2/R (1), Aisle 2/S (1) |
| Aisle 2/V | Sales floor | Aisle 2 · Bakery | 47.3, -139.8 | 3 | Aisle 2/X (1), Aisle 2/W (7), Aisle 2/Y (1), Aisle 2/U (1), Aisle 2/T (1) |
| Aisle 2/W | Sales floor | Aisle 2 · Bakery | 47.3, -141.4 | 6 | Aisle 2/Y (4), Aisle 2/T (4), Aisle 2/V (7), Aisle 2/X (1), Aisle 2/U (1) |
| Aisle 2/X | Sales floor | Aisle 2 · Bakery | 42.8, -139.8 | 3 | Aisle 2/Y (7), Aisle 2/V (1), Aisle 2/W (1) |
| Aisle 2/Y | Sales floor | Aisle 2 · Bakery | 42.8, -141.4 | 6 | Aisle 13/K (4), Aisle 2/W (4), Aisle 2/X (7), Aisle 2/V (1) |
| Aisle 3/A | Sales floor | Aisle 3 · Soft Drinks | 81.6, -142.2 | 9 | Aisle 3/C (7), Aisle 1/V (7) |
| Aisle 3/B | Sales floor | Aisle 3 · Soft Drinks | 76.6, -142.2 | 6 | Aisle 3/D (4), Aisle 1/Y (4), Aisle 1/W (4), Aisle 1/X (1) |
| Aisle 3/C | Sales floor | Aisle 3 · Soft Drinks | 81.6, -147.4 | 12 | Aisle 3/F (7), Aisle 3/A (7) |
| Aisle 3/D | Sales floor | Aisle 3 · Soft Drinks | 76.6, -147.4 | 8 | Aisle 4/A (10), Aisle 3/H (4), Aisle 4/D (1), Aisle 3/B (4) |
| Aisle 3/E | Sales floor | Aisle 3 · Soft Drinks | 81.6, -157.2 | 9 | Aisle 3/I (7), Aisle 3/F (7) |
| Aisle 3/F | Sales floor | Aisle 3 · Soft Drinks | 81.6, -152.7 | 9 | Aisle 3/E (7), Aisle 3/C (7) |
| Aisle 3/G | Sales floor | Aisle 3 · Soft Drinks | 76.6, -157.2 | 6 | Aisle 5/A (7), Aisle 3/J (4), Aisle 10/A (1), Aisle 3/H (4), Aisle 4/D (1) |
| Aisle 3/H | Sales floor | Aisle 3 · Soft Drinks | 76.6, -152.7 | 6 | Aisle 4/D (7), Aisle 3/G (4), Aisle 5/A (1), Aisle 3/D (4), Aisle 4/A (1) |
| Aisle 3/I | Sales floor | Aisle 3 · Soft Drinks | 81.6, -162.4 | 12 | Aisle 10/H (7), Aisle 3/E (7) |
| Aisle 3/J | Sales floor | Aisle 3 · Soft Drinks | 76.6, -162.4 | 8 | Aisle 10/A (10), Aisle 10/J (4), Aisle 10/B (1), Aisle 3/G (4), Aisle 5/A (1) |
| Aisle 4/A | Sales floor | Aisle 4 · Snacks & Crisps | 72.9, -147.4 | 12 | Aisle 4/B (10), Aisle 4/E (4), Aisle 4/F (1), Aisle 3/D (10), Aisle 4/D (1), Aisle 3/H (1) |
| Aisle 4/B | Sales floor | Aisle 4 · Snacks & Crisps | 67.6, -147.4 | 16 | Aisle 4/F (10), Aisle 4/A (10), Aisle 4/E (1), Aisle 4/C (4) |
| Aisle 4/C | Sales floor | Aisle 4 · Snacks & Crisps | 62, -147.1 | 10 | Aisle 6/E (1), Aisle 4/G (4), Aisle 6/D (1), Aisle 6/A (7), Aisle 4/B (4) |
| Aisle 4/D | Sales floor | Aisle 4 · Snacks & Crisps | 74.8, -152.7 | 3 | Aisle 3/H (7), Aisle 5/A (1), Aisle 3/G (1), Aisle 4/A (1), Aisle 3/D (1) |
| Aisle 4/E | Sales floor | Aisle 4 · Snacks & Crisps | 72, -152.7 | 6 | Aisle 4/F (7), Aisle 5/B (4), Aisle 5/C (1), Aisle 4/A (4), Aisle 4/B (1) |
| Aisle 4/F | Sales floor | Aisle 4 · Snacks & Crisps | 67.6, -152.7 | 12 | Aisle 5/C (10), Aisle 4/E (7), Aisle 5/B (1), Aisle 4/B (10), Aisle 4/A (1) |
| Aisle 4/G | Sales floor | Aisle 4 · Snacks & Crisps | 61.6, -152.7 | 6 | Aisle 7/B (1), Aisle 5/D (4), Aisle 7/A (1), Aisle 6/D (4), Aisle 4/C (4), Aisle 6/E (1) |
| Aisle 5/A | Sales floor | Aisle 5 · Tins & Jars | 74.8, -157.2 | 3 | Aisle 3/G (7), Aisle 10/A (1), Aisle 3/J (1), Aisle 4/D (1), Aisle 3/H (1) |
| Aisle 5/B | Sales floor | Aisle 5 · Tins & Jars | 72, -157.2 | 6 | Aisle 5/C (7), Aisle 5/E (4), Aisle 5/F (1), Aisle 4/E (4), Aisle 4/F (1) |
| Aisle 5/C | Sales floor | Aisle 5 · Tins & Jars | 67.6, -157.2 | 12 | Aisle 5/F (10), Aisle 5/B (7), Aisle 5/E (1), Aisle 4/F (10), Aisle 4/E (1) |
| Aisle 5/D | Sales floor | Aisle 5 · Tins & Jars | 61.6, -156.4 | 4 | Aisle 7/A (4), Aisle 4/G (4), Aisle 7/B (1) |
| Aisle 5/E | Sales floor | Aisle 5 · Tins & Jars | 72, -161.5 | 6 | Aisle 5/F (7), Aisle 5/B (4), Aisle 5/C (1) |
| Aisle 5/F | Sales floor | Aisle 5 · Tins & Jars | 67.6, -161.5 | 12 | Aisle 5/G (7), Aisle 5/E (7), Aisle 5/C (10), Aisle 5/B (1) |
| Aisle 5/G | Sales floor | Aisle 5 · Tins & Jars | 62.3, -161.5 | 9 | Aisle 7/G (7), Aisle 5/F (7) |
| Aisle 6/A | Sales floor | Aisle 6 · Cereal & Breakfast | 57.8, -146.5 | 9 | Aisle 6/B (7), Aisle 4/C (7) |
| Aisle 6/B | Sales floor | Aisle 6 · Cereal & Breakfast | 52.6, -146.5 | 12 | Aisle 6/C (7), Aisle 6/A (7) |
| Aisle 6/C | Sales floor | Aisle 6 · Cereal & Breakfast | 47.3, -146.5 | 9 | Aisle 8/B (7), Aisle 6/B (7) |
| Aisle 6/D | Sales floor | Aisle 6 · Cereal & Breakfast | 57.8, -151.9 | 6 | Aisle 6/F (4), Aisle 4/G (4), Aisle 6/E (7), Aisle 6/G (1), Aisle 4/C (1) |
| Aisle 6/E | Sales floor | Aisle 6 · Cereal & Breakfast | 57.8, -149.8 | 3 | Aisle 6/G (1), Aisle 6/D (7), Aisle 6/F (1), Aisle 4/C (1), Aisle 4/G (1) |
| Aisle 6/F | Sales floor | Aisle 6 · Cereal & Breakfast | 52.6, -151.9 | 8 | Aisle 6/J (4), Aisle 6/D (4), Aisle 6/G (10), Aisle 6/H (1), Aisle 6/E (1) |
| Aisle 6/G | Sales floor | Aisle 6 · Cereal & Breakfast | 52.6, -149.8 | 4 | Aisle 6/H (1), Aisle 6/F (10), Aisle 6/J (1), Aisle 6/E (1), Aisle 6/D (1) |
| Aisle 6/H | Sales floor | Aisle 6 · Cereal & Breakfast | 47.3, -149.8 | 3 | Aisle 6/I (1), Aisle 6/J (7), Aisle 6/K (1), Aisle 6/G (1), Aisle 6/F (1) |
| Aisle 6/I | Sales floor | Aisle 6 · Cereal & Breakfast | 44.8, -149.8 | 1 | Aisle 6/H (1), Aisle 6/K (1), Aisle 6/J (1) |
| Aisle 6/J | Sales floor | Aisle 6 · Cereal & Breakfast | 47.3, -152.7 | 9 | Aisle 6/K (7), Aisle 7/E (7), Aisle 7/F (1), Aisle 7/D (1), Aisle 7/C (1), Aisle 6/F (4), Aisle 6/H (7), Aisle 6/I (1), Aisle 6/G (1) |
| Aisle 6/K | Sales floor | Aisle 6 · Cereal & Breakfast | 44.8, -152.7 | 3 | Aisle 6/J (7), Aisle 7/F (1), Aisle 7/E (1), Aisle 6/I (1), Aisle 6/H (1) |
| Aisle 7/A | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 57.8, -156.4 | 6 | Aisle 7/C (4), Aisle 5/D (4), Aisle 7/B (7), Aisle 7/D (1), Aisle 4/G (1) |
| Aisle 7/B | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 57.8, -154.8 | 3 | Aisle 7/D (1), Aisle 7/A (7), Aisle 7/C (1), Aisle 4/G (1), Aisle 5/D (1) |
| Aisle 7/C | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 52.6, -156.4 | 8 | Aisle 7/E (4), Aisle 7/A (4), Aisle 7/D (10), Aisle 6/J (1), Aisle 7/B (1) |
| Aisle 7/D | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 52.6, -154.8 | 4 | Aisle 6/J (1), Aisle 7/C (10), Aisle 7/E (1), Aisle 7/B (1), Aisle 7/A (1) |
| Aisle 7/E | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 47.1, -157 | 8 | Aisle 7/F (7), Aisle 7/I (4), Aisle 9/F (1), Aisle 7/C (4), Aisle 6/J (7), Aisle 6/K (1), Aisle 7/D (1) |
| Aisle 7/F | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 44.8, -157.2 | 3 | Aisle 7/E (7), Aisle 9/F (1), Aisle 7/I (1), Aisle 6/K (1), Aisle 6/J (1) |
| Aisle 7/G | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 57.8, -161.5 | 9 | Aisle 7/H (7), Aisle 5/G (7) |
| Aisle 7/H | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 52.6, -161.5 | 12 | Aisle 7/I (7), Aisle 7/G (7) |
| Aisle 7/I | Sales floor | Aisle 7 · Noodles, Pasta & Rice | 47.3, -161.5 | 9 | Aisle 9/F (7), Aisle 7/H (7), Aisle 7/E (4), Aisle 7/F (1) |
| Aisle 8/A | Sales floor | Aisle 8 · Health & Beauty | 27.8, -142.1 | 9 | Aisle 8/F (4), Aisle 13/N (7), Aisle 8/E (1), Aisle 8/D (1), Door: Sales floor - Staff room (2), Aisle 13/O (7) |
| Aisle 8/B | Sales floor | Aisle 8 · Health & Beauty | 42.7, -147.2 | 11 | Aisle 8/C (10), Aisle 8/G (4), Aisle 8/H (1), Aisle 6/C (7) |
| Aisle 8/C | Sales floor | Aisle 8 · Health & Beauty | 37.6, -147.4 | 16 | Aisle 8/H (10), Aisle 8/B (10), Aisle 8/G (1) |
| Aisle 8/D | Sales floor | Aisle 8 · Health & Beauty | 31.6, -147.4 | 8 | Aisle 8/E (10), Aisle 8/I (4), Aisle 8/J (1), Aisle 13/N (4), Aisle 8/A (1) |
| Aisle 8/E | Sales floor | Aisle 8 · Health & Beauty | 29.8, -147.4 | 4 | Aisle 8/D (10), Aisle 8/J (1), Aisle 8/I (1), Aisle 8/A (1), Aisle 13/N (1) |
| Aisle 8/F | Sales floor | Aisle 8 · Health & Beauty | 27, -147.4 | 8 | Aisle 8/K (4), Aisle 8/A (4) |
| Aisle 8/G | Sales floor | Aisle 8 · Health & Beauty | 42, -152.7 | 6 | Aisle 8/H (7), Aisle 9/A (4), Aisle 9/B (1), Aisle 8/B (4), Aisle 8/C (1) |
| Aisle 8/H | Sales floor | Aisle 8 · Health & Beauty | 37.6, -152.7 | 12 | Aisle 9/B (10), Aisle 8/G (7), Aisle 9/A (1), Aisle 8/C (10), Aisle 8/B (1) |
| Aisle 8/I | Sales floor | Aisle 8 · Health & Beauty | 31.6, -152.7 | 6 | Aisle 8/J (7), Aisle 9/C (4), Aisle 9/D (1), Aisle 8/D (4), Aisle 8/E (1) |
| Aisle 8/J | Sales floor | Aisle 8 · Health & Beauty | 29.8, -152.7 | 3 | Aisle 8/I (7), Aisle 9/D (1), Aisle 9/C (1), Aisle 8/E (1), Aisle 8/D (1) |
| Aisle 8/K | Sales floor | Aisle 8 · Health & Beauty | 27, -152.7 | 6 | Aisle 9/E (4), Aisle 8/F (4) |
| Aisle 9/A | Sales floor | Aisle 9 · Household & Cleaning | 42, -157.2 | 6 | Aisle 9/B (7), Aisle 9/F (4), Aisle 9/G (1), Aisle 8/G (4), Aisle 8/H (1) |
| Aisle 9/B | Sales floor | Aisle 9 · Household & Cleaning | 37.6, -157.2 | 12 | Aisle 9/G (10), Aisle 9/A (7), Aisle 9/F (1), Aisle 8/H (10), Aisle 8/G (1) |
| Aisle 9/C | Sales floor | Aisle 9 · Household & Cleaning | 31.6, -157.2 | 6 | Aisle 9/D (7), Aisle 9/H (4), Aisle 9/I (1), Aisle 8/I (4), Aisle 8/J (1) |
| Aisle 9/D | Sales floor | Aisle 9 · Household & Cleaning | 29.8, -157.2 | 3 | Aisle 9/C (7), Aisle 9/I (1), Aisle 9/H (1), Aisle 8/J (1), Aisle 8/I (1) |
| Aisle 9/E | Sales floor | Aisle 9 · Household & Cleaning | 27, -157.2 | 6 | Aisle 9/J (4), Aisle 8/K (4) |
| Aisle 9/F | Sales floor | Aisle 9 · Household & Cleaning | 42.9, -161.5 | 9 | Aisle 9/G (7), Aisle 7/I (7), Aisle 9/A (4), Aisle 9/B (1), Aisle 7/F (1), Aisle 7/E (1) |
| Aisle 9/G | Sales floor | Aisle 9 · Household & Cleaning | 37.6, -161.5 | 12 | Aisle 9/H (7), Aisle 9/F (7), Aisle 9/B (10), Aisle 9/A (1) |
| Aisle 9/H | Sales floor | Aisle 9 · Household & Cleaning | 32.3, -162.4 | 12 | Aisle 9/I (10), Aisle 12/E (7), Aisle 12/F (1), Aisle 12/D (1), Aisle 12/C (1), Aisle 9/G (7), Aisle 9/C (4), Aisle 9/D (1) |
| Aisle 9/I | Sales floor | Aisle 9 · Household & Cleaning | 29.8, -162.4 | 4 | Aisle 9/H (10), Aisle 12/F (1), Aisle 12/E (1), Aisle 9/D (1), Aisle 9/C (1) |
| Aisle 9/J | Sales floor | Aisle 9 · Household & Cleaning | 27, -162.4 | 8 | Aisle 12/N (4), Aisle 9/E (4) |
| Staff room/A | Staff room | — | 22.5, -139.2 | 4 | Staff room/D (1), Staff room/B (10), Staff room/C (1), Door: Office - Staff room (4) |
| Staff room/B | Staff room | — | 22.1, -142 | 10 | Staff room/I (1), Staff room/G (8), Staff room/H (1), Door: Sales floor - Staff room (6), Staff room/C (1), Staff room/A (10), Staff room/D (1) |
| Staff room/C | Staff room | — | 19.5, -140.7 | 1 | Staff room/B (1), Staff room/D (3), Staff room/A (1) |
| Staff room/D | Staff room | — | 17, -139.5 | 5 | Staff room/E (2), Staff room/F (2), Staff room/C (3), Staff room/A (1), Staff room/B (1) |
| Staff room/E | Staff room | — | 13.1, -141.8 | 8 | Staff room/J (4), Staff room/I (1), Staff room/F (7), Staff room/D (2) |
| Staff room/F | Staff room | — | 12.8, -139.2 | 3 | Staff room/E (7), Staff room/D (2) |
| Staff room/G | Staff room | — | 22.5, -147.3 | 16 | Door: Backstreet - Staff room (2), Staff room/H (6), Staff room/B (8), Staff room/I (1), Door: Sales floor - Staff room (2) |
| Staff room/H | Staff room | — | 17.4, -146.9 | 9 | Door: Backstreet - Staff room (6), Staff room/J (2), Staff room/G (6) |
| Staff room/I | Staff room | — | 17.3, -143.3 | 3 | Staff room/E (1), Staff room/B (1) |
| Staff room/J | Staff room | — | 12.8, -147.1 | 11 | Staff room/H (2), Staff room/K (2), Staff room/E (5) |
| Staff room/K | Staff room | — | 11.5, -144 | 1 | Staff room/J (1) |
| Stockroom/A | Stockroom | — | 52.5, -178.9 | 8 | Stockroom/B (4), Stockroom/K (8), Door: Cold storage - Stockroom (2) |
| Stockroom/B | Stockroom | — | 47.3, -178.9 | 6 | Stockroom/C (4), Stockroom/A (4) |
| Stockroom/C | Stockroom | — | 42.6, -177.6 | 12 | Stockroom/D (6), Stockroom/B (4), Door: Sales floor - Stockroom (8) |
| Stockroom/D | Stockroom | — | 37.6, -178.9 | 8 | Stockroom/E (4), Stockroom/C (6) |
| Stockroom/E | Stockroom | — | 32.3, -178.9 | 6 | Stockroom/F (4), Stockroom/O (7), Stockroom/D (4) |
| Stockroom/F | Stockroom | — | 27.8, -178.9 | 6 | Stockroom/G (4), Stockroom/E (4) |
| Stockroom/G | Stockroom | — | 22.6, -178.9 | 8 | Stockroom/H (4), Stockroom/F (4) |
| Stockroom/H | Stockroom | — | 17.2, -177.9 | 10 | Stockroom/I (4), Stockroom/G (4), Door: Backstreet - Stockroom (6) |
| Stockroom/I | Stockroom | — | 12.8, -178.9 | 6 | Stockroom/U (7), Stockroom/H (4) |
| Stockroom/J | Stockroom | — | 52.5, -186.4 | 8 | Stockroom/L (4), Stockroom/K (10) |
| Stockroom/K | Stockroom | — | 52.1, -182.8 | 10 | Stockroom/J (10), Door: Cold storage - Stockroom (6), Stockroom/A (8) |
| Stockroom/L | Stockroom | — | 47.3, -186.4 | 6 | Stockroom/M (4), Stockroom/J (4) |
| Stockroom/M | Stockroom | — | 42.8, -186.4 | 6 | Stockroom/N (4), Stockroom/L (4) |
| Stockroom/N | Stockroom | — | 38.4, -186.4 | 6 | Stockroom/M (4) |
| Stockroom/O | Stockroom | — | 32.3, -182.6 | 9 | Stockroom/P (7), Stockroom/E (7) |
| Stockroom/P | Stockroom | — | 31.9, -186.3 | 5 | Stockroom/Q (4), Stockroom/O (7) |
| Stockroom/Q | Stockroom | — | 27.8, -186.4 | 6 | Stockroom/R (4), Stockroom/P (4) |
| Stockroom/R | Stockroom | — | 22.6, -186.4 | 8 | Stockroom/S (4), Stockroom/Q (4) |
| Stockroom/S | Stockroom | — | 17.3, -186.4 | 6 | Stockroom/T (4), Stockroom/R (4) |
| Stockroom/T | Stockroom | — | 12.8, -186.4 | 6 | Stockroom/S (4), Stockroom/U (7) |
| Stockroom/U | Stockroom | — | 12.8, -182.7 | 9 | Stockroom/T (7), Stockroom/I (7) |
| WC/A | WC | — | 20.1, -111.6 | 2 | WC/B (4) |
| WC/B | WC | — | 17.4, -112.3 | 9 | WC/C (1), WC/F (7), WC/G (1), WC/E (2), WC/A (5), WC/D (1) |
| WC/C | WC | — | 12.9, -112.4 | 9 | WC/G (7), WC/B (1), WC/F (1) |
| WC/D | WC | — | 22.3, -117.4 | 15 | WC/K (4), WC/I (1), WC/J (1), WC/H (2), Door: Lobby - WC (5), WC/F (1), WC/E (10), WC/B (1) |
| WC/E | WC | — | 22.3, -113 | 7 | WC/B (1), WC/D (10), WC/F (1) |
| WC/F | WC | — | 17.3, -115.2 | 3 | WC/G (1), WC/B (7), WC/C (1), WC/D (1), WC/E (1) |
| WC/G | WC | — | 12.8, -117.3 | 12 | WC/N (1), WC/K (4), WC/M (2), WC/C (7), WC/F (1), WC/B (1) |
| WC/H | WC | — | 22.3, -121.8 | 4 | WC/D (2) |
| WC/I | WC | — | 20.1, -121.9 | 2 | WC/J (4), WC/D (1), WC/K (1) |
| WC/J | WC | — | 18.8, -121.7 | 2 | WC/I (4) |
| WC/K | WC | — | 17.3, -118.7 | 6 | WC/G (4), WC/L (2), WC/D (4) |
| WC/L | WC | — | 16.7, -121.8 | 4 | WC/K (2) |
| WC/M | WC | — | 14.1, -121.6 | 3 | WC/G (1) |
| WC/N | WC | — | 11.6, -122 | 3 | WC/G (1) |

</details>
