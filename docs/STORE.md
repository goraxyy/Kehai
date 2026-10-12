# The store

The building, what it sells, how the shelves are stocked, and the map Karen uses. It's all code:
the scene stores none of it, and the shop stocks itself when it loads.

| file (`Assets/!_Project/_Game/`) | what it holds |
|---|---|
| `Items/Scripts/ProductCatalog.cs` | the 80 products: name, brand, shape, size, weight, price, colours |
| `Level/Scripts/StoreLayout.cs` | which aisle each shelf bay belongs to |
| `Level/Scripts/Planogram.cs` | which product goes on which shelf, and the rules |
| `Level/Scripts/ShelfGrid.cs` | how a shelf is cut into slots |
| `Level/Scripts/ShelfSlot.cs`, `ShelfStock.cs`, `ShelfDrawer.cs`, `ShelfAim.cs` | the slots as data; drawing them; aiming at one |
| `Map/Scripts/StoreMap.cs` | Karen's map: rooms, regions, doors, chokepoints |

---

## The building

World coordinates in metres; north is the street (high Z), the store runs south.

- **The route:** customers come in at the north-east doors, go round the floor, and leave
  through the tills at the north-west. The aisles are numbered in that order.
- **Sales floor:** a 60 × 45 m maze of 189 shelf bays (x 25…85, z −130…−175).
- **Lobby:** two sets of automatic doors; the bins stand just inside.
- **Tills:** three checkouts at the north-west, with mints on the counter.
- **Staff rooms** (west): the time clock, the coffee machine, the radio with the stock crate
  beside it (one crate restocks a whole shelf), the mop. Their two doors are chokepoints: lose
  one and a room is cut off.
- **Stockroom** (south), two rooms of product boxes (see [The back rooms](#the-back-rooms)):
  the storage room's racks, and the cold room of fridges and freezers.
- **Backstreet:** the breaker box and the skip.

```
Floor plan: one character per 2.5 m bay, north up (doors above, chillers below).

            25  35  45  55  65  75  85
 -126             B  B  P     P P
 -129             B B   P P   P P
 -131            BB B BPP PPP    P
 -134     W W W     B B     P P
 -136     W W W  B BB   PP  P PPPP
 -139               B B P   P P
 -141      H H H CC    SS S S D DD
 -144             C C     S S D D
 -146      H H H   CCCC S S S  DDD
 -149             C   C S S S D
 -151    H  H H KCC  CN T T T  DDD
 -154     K K   K N   N T T T D
 -156       KK KK NN  N T T T  DDD
 -159       K     N   N T T T D
 -161      KKK KA   N F M M M  DDD
 -164         A A F   F M M M M
 -166       A     FFF           MM
 -169       A
 -171     A AA      FF          MM

P 1 Fruit & Veg   B 2 Bakery   D 3 Soft Drinks   S 4 Snacks   T 5 Tins & Jars
C 6 Cereal        N 7 Noodles  H 8 Health & Beauty   K 9 Household   M 10 Dairy
F 11 Frozen       A 12 Pet     W 13 Sweets
```

The gaps are the maze: the shelving doubles back, dead-ends and loops.

---

## The aisles

| aisle | sells | bays | why there (real supermarket rules) |
|---|---|---|---|
| 1 | Fruit & Veg | 29 | Fresh food by the door sets the tone; the biggest section |
| 2 | Bakery | 19 | At the front, where the smell reaches the door |
| 3 | Soft Drinks | 20 | Heavy and fast-selling: a long run on the east wall |
| 4 | Snacks & Crisps | 15 | Next to the drinks: crisps sell beside cola |
| 5 | Tins & Jars | 12 | Long shelf life goes in the quiet middle |
| 6 | Cereal & Breakfast | 16 | Coffee and tea sit with the cereal |
| 7 | Noodles, Pasta & Rice | 10 | The rest of the dry goods |
| 8 | Health & Beauty | 13 | Non-food, away from food; low shelves keep the view open |
| 9 | Household & Cleaning | 17 | Beside health & beauty |
| 10 | Dairy & Chilled | 12 | **Milk at the far wall**, so fetching it crosses the whole shop |
| 11 | Frozen | 10 | Beside dairy, at the cold end |
| 12 | Pet | 10 | Bulky and rarely bought: the far corner |
| 13 | Sweets | 6 | At the tills, for the impulse buy in the queue |

A black sign hangs over every aisle with its number, name and Japanese, and a lamp sits over
every ceiling light (dark when its light is off).

---

## How the shelves are stocked

**Which shelf (height):**

| shelf | height | what goes there |
|---|---|---|
| eye level | 1.4 m | brand leaders and the best margins ("eye level is buy level") |
| waist level | 0.8 m | children's products (a child's eye level) |
| floor level | 0.2 m | heavy and bulky packs: rice, kibble, water, laundry boxes |

Nothing over 0.8 kg goes on the top shelf; the tests fail if a layout tries.

**Which side and neighbours:**
- **Planned by aisle, not by bay:** each side of an aisle is one run (the two-sided bays, the
  pillars between them, whichever way each was placed), and the section's **brand columns** run
  along it in blocks of about 6 m: one brand stacked eye over waist over stoop, then the next.
  The two sides of an aisle start on different columns. The columns are the faces of the
  section's two layouts in `Planogram.cs`.
- **Best sellers get more space:** bananas, whole milk and Ramyum cups.
- **End caps** carry the aisle's promotion or a partner product (cola on the crisps' ends).
- **Every product is on sale somewhere**, even in aisles missing the shelf its layout wants.

**Filling a shelf.** Every shelf is cut into 0.5 m squares. All the squares on one shelf, on one
side, sell one product. Each square holds as many slots as fit: 2 × 2 for small things (tins),
two for long or wide packs (loaves, cereal boxes), up to 3 × 3 drinks, one for anything bigger.
One slot holds one item: **16,500 slots** in all. Products are shown **1.5× real size** (at most
0.5 m tall) so their labels read from the aisle.

**Which way a shelf faces** comes from the shelf models' price strips (the "Pricetag" pieces on
the boards' front edges): they're on the sides that sell. Two-sided bays and "I" pillars face
both aisles; a pillar where a perpendicular bay joins the run faces only its open sides (a "T"
its front, an "L" its front and one side); "b" and "E" pieces face their back; some pillars
face sideways. A square inside a board faces the way the stock in front of it does.

**Prices on the shelves.** Every price strip shows what its board sells on that side: the
name, the section, the price and a bar of the product's colour, a label a metre
(`PriceTags.cs`). **Kehai → Products → 4. Make Price Tags** makes the labels (run it again
after prices change); the stock puts them on as it's built, in the editor too.

**Shelf colour.** The shelves are one plain grey (`M_ShelfGrey`), not the grid prototype
materials they came in: **Kehai → Shelves → 4. Paint Shelves Grey** repaints the shelf prefabs.

---

## The back rooms

A box of every product, never used up: carry it to the shelf, and E on any
slot of its product refills that product's empty slots on the bay. A box glows when looked at,
and its spot shows a see-through yellow ghost of it while it's away. Drop it (Q) and it lands
where it falls, like the stock crate; press E on its spot to send it back. Built when the store
loads (`StorageBoxes.cs`), so nothing is baked into the scene. In the editor the same boxes stand
in place to look at, never saved and not clickable (`Kehai/Storage` turns them off), and
`Kehai/Products/3` lays a box of every product on Models_Island, model and materials only.

- **Storage room:** shelf-stable products in brown boxes (a band of the product's colour, its
  name and section on a label, its packaging beside it). **One section to a rack**, named on
  the rack's lowest board. Every board but the floor row is full: each product has a box in
  reach, a second where there's room, and the top board repeats the box below it.
- **Cold room:** products kept cold, in white insulated boxes labelled on both sides: frozen
  food on the chest freezers, chilled food on the upright fridges. Kept cold
  (`ProductDef.Storage`): all of dairy and frozen, the salad mix, fresh udon, Chakra green tea
  and Genki.

---

## The products

80 products in 13 sections, six or seven each. Each is one row in `ProductCatalog.cs`: id, name,
brand, section, shape, material, size, weight, price, two colours, shine. A shelf accepts
anything from its own section, so any soft drink goes back on the drinks shelf.

The models (with printed labels) are made outside the repo, in a Blender pipeline;
**Kehai → Products → 1. Import Product Models** brings them in. A fresh checkout shows
placeholder boxes until then. Never renumber the sections in `Item.cs`; add new ones at the end.

---

## Customers

Each comes in with a list of 2–4 products, picked by how often each aisle is shopped, and walks
it in aisle order, taking each from the nearest shelf that has it. About one in three grabs
sweets at the till. They ask for help when a shelf is empty, and sometimes for the next thing on
their list:

| how they ask | example |
|---|---|
| by name | *"Excuse me - I'm looking for Pipisi Zero."* |
| which aisle | *"Which aisle would I find a tin of Tunatastic in?"* |
| by aisle | *"Which way is aisle 7? I need the noodles."* |

You walk them to a shelf that really has it.

---

## Karen's map

The game builds a map from the NavMesh and the walls every time the store loads:
- **cells** on a 1.5 m grid, joined where a person can walk between them;
- **rooms** (7: outside, lobby, sales floor, two staff rooms, two stockrooms) and **regions**
  (5 m pieces of each room, named after their aisle);
- **chokepoints:** regions whose loss splits the store in two. Karen blocks these first; a
  player who knows them knows where not to get cornered.

Karen, the shift reports and the AI agents all use it. **Kehai → Map → Export STORE_MAP.md**
writes the full map (floor plan, regions, chokepoints) to `Library/Kehai/STORE_MAP.md`.

---

## Changing it

- **Add a product:** a row in `ProductCatalog.cs` and a shelf for it in `Planogram.cs` (a test
  fails until it has one).
- **Re-stock an aisle:** edit its section's two layouts (their faces are its brand columns) or
  its end cap in `Planogram.cs`.
- **Move an aisle:** edit its rectangle in `StoreLayout.cs`.
- **Count it:** **Kehai → Store → Report Layout** prints bays, shelves and slots per aisle.
