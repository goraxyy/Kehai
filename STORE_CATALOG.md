# Kehai — Store Catalogue

Everything the shop sells, what it is made of, and where it stands.

This is the human-readable half of two files that must agree:

| | |
|---|---|
| `Assets/!_Project/_Game/Items/Scripts/ProductCatalog.cs` | the range — 80 products, their packaging and their materials |
| `Assets/!_Project/_Game/Level/Scripts/StoreLayout.cs` | the plan — which section is sold on which piece of floor |
| `Assets/!_Project/_Game/Level/Scripts/Planogram.cs` | the merchandising — which product goes on which board of a bay |

Both are plain C#, so they live in version control alongside the rest of the game. The
scene does not store any of it; the shop is stocked at load. Every number in this document
was printed out of the running project rather than typed by hand.

---

## 1. The two levels

Real shops separate *where a thing belongs* from *what the thing is*, and so does this.

**Section** (`ItemType`) — the part of the shop. There are 13 of them plus four tool types
that never reach a shelf. A shelf slot accepts anything from its own section, which is what
makes restocking forgiving: any soft drink goes on the soft-drinks shelf.

**Product** (`ProductDef`) — the SKU. *Pipisi Zero*, `drink_pipisi_zero`, a 0.07 × 0.23 ×
0.07 m PET bottle in `#212121` with a red band. This is what a customer asks for by name,
what the interaction prompt says, and what a modeller builds.

A facing holds one product. A bay holds several. A section holds a range of six or seven.

```
ItemType.SoftDrinks                 ← section: what the shelf accepts
  └── drink_pipisi_zero             ← product: what is actually stacked there
      ├── "Pipisi Zero"             ← what the customer says
      ├── Bottle / PET              ← what it is
      └── 0.07 × 0.23 × 0.07 m      ← how big
```

The enum's numbers are written out explicitly in `Item.cs` and **must never be reshuffled**:
every shelf slot in the scene stores its section as an integer, so renumbering would restock
the whole building with the wrong goods overnight. New sections go on the end.

---

## 2. The building

World coordinates, in metres. The shop runs north–south along −Z: the doors are at the
**north** end (high Z, around −110) and the chillers at the **south** (low Z, around −175).
East is +X.

| | position (x, z) | notes |
|---|---|---|
| Customer spawn | 67.5, −102.5 | outside, under the sign |
| Outer automatic doors | 43, −110 · 68, −110 | `AutoDoubleDoor`, chime on open |
| Inner automatic doors | 43, −120 · 68, −120 | lobby between the two |
| Player start | 64.9, −105.3 | |
| Exit point | 42.5, −110 | customers leave west of where they came in |
| Checkouts ×3 | 30, −132 · 35, −132 · 40, −132 | `CashierPoint` at 41.5, −132.5 |
| **Sales floor (the maze)** | **x 25…85, z −129.5…−175** | 60 × 45.5 m, 186 bays, 3 344 facings |
| Chiller wall | z ≈ −174, x 23…85 | 100 fridge props, scenery only |
| Stockroom | x 6…55, z −175…−190 | 29 storage racks, cardboard at 35, −185 |
| Stock crate home | 53, −176.5 | one crate restocks the whole shop |
| Staff corridor | x 18…25, z −140…−145 | coffee machine 20, radio 18.5, time clock 21.5 |
| Mop home | 24.5, −140.5 | |
| Bins | 38.9, −127.0 · 38.7, −124.3 | by the doors |
| Back doors | 19, −175 · 44, −175 · 69, −175 | onto the stockroom |
| Side doors | 25, −124 · 25, −144 | onto the staff corridor |

A customer's route is therefore: **in at the north-east, around the floor, out through the
checkouts at the north-west.** The plan below is built around that walk.

---

## 3. The floor plan

One character per 2.5 m cell, one cell per shelf bay. North is up, so the doors are off the
top of the map and the chillers are along the bottom.

```
            25  35  45  55  65  75  85
            |   |   |   |   |   |   |
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
```

```
P  Produce · Fruit & Veg          C  Aisle 4 · Cereal & Breakfast
B  Bakery                         N  Aisle 5 · Noodles, Pasta & Rice
W  Checkout · Sweets & Impulse    H  Aisle 6 · Health & Beauty
D  Aisle 1 · Soft Drinks          K  Aisle 7 · Household & Cleaning
S  Aisle 2 · Snacks & Crisps      M  Back Wall · Dairy & Chilled
T  Aisle 3 · Tins & Jars          F  Back Wall · Frozen
                                  A  Back Wall · Pet
```

The gaps are the maze. Shelving is not laid out in parallel runs — it doubles back, dead-ends
and loops — which is why sections are cut out of the *floor plan* rather than assigned per
run. A bay belongs to whichever rectangle it stands in, so a bay is never split down the
middle and the plan survives a shelf being nudged.

---

## 4. The sections

Bounds are half-open: `xMin ≤ x < xMax`, `zMin ≤ z < zMax`. They tile the whole plane, so
nothing can fall through. First match wins, which is why the front strip is listed first.

| Sign | `ItemType` | x | z | bays | facings | range |
|---|---|---|---|---|---|---|
| Produce · Fruit & Veg | `Produce` | ≥ 58 | ≥ −142 | 29 | 577 | 6 |
| Bakery | `Bakery` | 42…58 | ≥ −142 | 19 | 381 | 6 |
| Checkout · Sweets & Impulse | `Confectionery` | < 42 | ≥ −142 | 6 | 57 | 6 |
| Aisle 1 · Soft Drinks | `SoftDrinks` | ≥ 76 | −163…−142 | 20 | 299 | 7 |
| Aisle 2 · Snacks & Crisps | `Snacks` | 60…76 | −153…−142 | 15 | 258 | 6 |
| Aisle 3 · Tins & Jars | `Canned` | 60…76 | −163…−153 | 12 | 252 | 6 |
| Aisle 4 · Cereal & Breakfast | `Cereal` | 44…60 | −153…−142 | 16 | 332 | 7 |
| Aisle 5 · Noodles, Pasta & Rice | `Noodles` | 44…60 | −163…−153 | 10 | 206 | 6 |
| Aisle 6 · Health & Beauty | `PersonalCare` | < 44 | −153…−142 | 13 | 162 | 6 |
| Aisle 7 · Household & Cleaning | `Household` | < 44 | −163…−153 | 17 | 338 | 6 |
| Back Wall · Dairy & Chilled | `Dairy` | ≥ 58 | < −163 | 12 | 207 | 6 |
| Back Wall · Frozen | `Frozen` | 44…58 | < −163 | 10 | 165 | 6 |
| Back Wall · Pet | `PetFood` | < 44 | < −163 | 10 | 165 | 6 |
| | | | **total** | **189** | **3 399** | **80** |

Aisle numbers climb with distance from the doors, so a customer who has walked to Aisle 7 has
crossed the whole shop.

### Why it sits this way

Each placement is a real supermarket convention, not decoration:

- **Produce at the entrance.** Fresh goods in the decompression zone are close to universal —
  they set the tone of the shop and nobody wants to carry them under a trolley-load of tins.
  It is the largest section here for the same reason it is in a real store.
- **Bakery next to it**, also at the front, where the smell reaches the door.
- **Milk at the far wall.** The single most-bought item is put as far from the entrance as the
  building allows, so that fetching it crosses everything else. The chiller props are already
  along z ≈ −174, so the dairy bays stand right in front of them.
- **Frozen beside dairy**, sharing the cold end of the building.
- **Soft drinks on the east wall run.** Heavy, high-turnover, and a natural wall block.
- **Snacks next to drinks.** Complementary adjacency — crisps sell next to cola.
- **Tins and dry goods in the middle.** Long shelf life goes where footfall is lowest.
- **Non-food at the far end.** Health & beauty and household are traditionally blocked
  together away from food. Health & beauty gets the short bays at x 30–42, z −143…−152: low
  gondolas, so the sightline across the shop is preserved — exactly why real stores use low
  shelving there.
- **Pet food in the far corner.** Bulky, infrequent, tolerates the worst spot in the building.
- **Sweets at the checkouts.** Impulse buys in the queue, which is what those six small bays
  at x 28–39 are for, plus the three cashier counters themselves.

---

## 5. The range

80 products. Sizes are metres in the model's local axes — **x across the shelf, y up, z into
the shelf**. Mass is kilograms, for the `Rigidbody`. Colours and the smoothness/metallic pair
feed straight into a URP Lit material.

### Produce · Fruit & Veg

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `produce_apples` | a bag of Fuji apples | Orchard Row | Net | Mesh | 0.18×0.22×0.14 | 1.10 | 380 | `#C0392B` | `#7CB342` | 0.30 | 0.00 |
| `produce_bananas` | bananas | Orchard Row | Loose | Mesh | 0.22×0.10×0.12 | 0.85 | 220 | `#F4D03F` | `#6B8E23` | 0.25 | 0.00 |
| `produce_tomatoes` | a tray of tomatoes | Orchard Row | Tray | PlasticFilm | 0.22×0.08×0.15 | 0.60 | 340 | `#E74C3C` | `#FFFFFF` | 0.55 | 0.00 |
| `produce_daikon` | a daikon radish | Orchard Row | Loose | PlasticFilm | 0.09×0.40×0.09 | 0.90 | 180 | `#F7F3E8` | `#7CB342` | 0.40 | 0.00 |
| `produce_saladmix` | a bag of salad mix | Leafy Lane | Bag | PlasticFilm | 0.22×0.26×0.08 | 0.20 | 290 | `#58D68D` | `#1E8449` | 0.60 | 0.00 |
| `produce_mikan` | a net of mikan | Orchard Row | Net | Mesh | 0.18×0.20×0.14 | 1.00 | 420 | `#E67E22` | `#2E7D32` | 0.30 | 0.00 |

### Bakery

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `bakery_shokupan` | a Shokupan white loaf | Kamado | Bag | PlasticFilm | 0.13×0.14×0.24 | 0.45 | 260 | `#FDF2D0` | `#D35400` | 0.62 | 0.00 |
| `bakery_sourdough` | a Rye Rider sourdough | Rye Rider | Bag | Paperboard | 0.12×0.13×0.25 | 0.55 | 480 | `#8D6E63` | `#3E2723` | 0.25 | 0.00 |
| `bakery_melonpan` | a melon pan | Kamado | Wrapper | PlasticFilm | 0.13×0.07×0.13 | 0.12 | 150 | `#D4E157` | `#795548` | 0.65 | 0.00 |
| `bakery_anpan` | an anpan | Kamado | Wrapper | PlasticFilm | 0.11×0.06×0.11 | 0.10 | 140 | `#C68642` | `#4E342E` | 0.65 | 0.00 |
| `bakery_croissants` | a pack of croissants | Beurre Bros | Tray | PlasticFilm | 0.24×0.09×0.16 | 0.28 | 390 | `#E0A64B` | `#8B1A1A` | 0.58 | 0.00 |
| `bakery_bagels` | a pack of sesame bagels | Beurre Bros | Bag | PlasticFilm | 0.17×0.20×0.11 | 0.48 | 420 | `#C89F63` | `#283593` | 0.60 | 0.00 |

### Back Wall · Dairy & Chilled

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `dairy_milk_whole` | Moo-Moo whole milk | Moo-Moo | Carton | WaxedCarton | 0.07×0.21×0.07 | 1.04 | 240 | `#F5F5F5` | `#1565C0` | 0.35 | 0.00 |
| `dairy_milk_skim` | Moo-Moo skimmed milk | Moo-Moo | Carton | WaxedCarton | 0.07×0.21×0.07 | 1.04 | 240 | `#F5F5F5` | `#4FC3F7` | 0.35 | 0.00 |
| `dairy_yoghurt` | Yogo strawberry yoghurt | Yogo | Tub | Hdpe | 0.11×0.09×0.11 | 0.45 | 210 | `#F48FB1` | `#AD1457` | 0.48 | 0.00 |
| `dairy_creamcheese` | Kumo cream cheese | Kumo | Box | Paperboard | 0.10×0.05×0.07 | 0.20 | 320 | `#FFFDE7` | `#0277BD` | 0.30 | 0.00 |
| `dairy_butter` | Butterfly salted butter | Butterfly | Box | FoilLaminate | 0.11×0.05×0.06 | 0.23 | 430 | `#FFE082` | `#37474F` | 0.52 | 0.00 |
| `dairy_eggs` | a box of eggs | Hinata Farm | Tray | Paperboard | 0.24×0.07×0.11 | 0.65 | 280 | `#E0E0E0` | `#F9A825` | 0.20 | 0.00 |

### Back Wall · Frozen

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `frozen_peas` | a bag of Freezy peas | Freezy | Bag | PlasticFilm | 0.20×0.26×0.07 | 0.50 | 230 | `#43A047` | `#FFFFFF` | 0.58 | 0.00 |
| `frozen_gyoza` | Gyoza Gang gyoza | Gyoza Gang | Bag | FoilLaminate | 0.21×0.24×0.07 | 0.55 | 390 | `#C62828` | `#FFD54F` | 0.50 | 0.00 |
| `frozen_icecream` | Ice Dream vanilla | Ice Dream | Tub | Paperboard | 0.13×0.11×0.13 | 0.60 | 520 | `#FFF8E1` | `#6D4C41` | 0.30 | 0.00 |
| `frozen_fries` | a bag of frozen fries | Freezy | Bag | PlasticFilm | 0.22×0.28×0.08 | 0.80 | 310 | `#FBC02D` | `#E53935` | 0.58 | 0.00 |
| `frozen_prawns` | Kaiten prawns | Kaiten | Bag | FoilLaminate | 0.19×0.23×0.06 | 0.35 | 680 | `#EF6C00` | `#0D47A1` | 0.50 | 0.00 |
| `frozen_pizza` | a Pizza Piccolo margherita | Pizza Piccolo | Box | Paperboard | 0.25×0.04×0.25 | 0.42 | 450 | `#D32F2F` | `#1B5E20` | 0.28 | 0.00 |

### Aisle 4 · Cereal & Breakfast

Coffee and tea live here, next to the cereal, the way a breakfast aisle is blocked in a real
store.

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `cereal_chocoloops` | Choco Loops | Choco Loops | Box | Paperboard | 0.20×0.32×0.07 | 0.38 | 460 | `#5D4037` | `#FFC107` | 0.26 | 0.00 |
| `cereal_branflakies` | Bran Flakies | Flakies | Box | Paperboard | 0.20×0.32×0.07 | 0.40 | 420 | `#C0392B` | `#F5DEB3` | 0.26 | 0.00 |
| `cereal_honeynutz` | Honey Nutz clusters | Honey Nutz | Box | Paperboard | 0.19×0.30×0.07 | 0.37 | 480 | `#F9A825` | `#4E342E` | 0.26 | 0.00 |
| `cereal_oatsy` | Oatsy instant porridge | Oatsy | Box | Paperboard | 0.16×0.24×0.07 | 0.42 | 350 | `#EFEBE9` | `#00695C` | 0.26 | 0.00 |
| `cereal_granola` | Morning Mochi granola | Morning Mochi | Pouch | FoilLaminate | 0.17×0.27×0.08 | 0.50 | 540 | `#8D6E63` | `#FF7043` | 0.45 | 0.00 |
| `cereal_kafe` | Kafé instant coffee | Kafé | Jar | Glass | 0.09×0.16×0.09 | 0.32 | 620 | `#3E2723` | `#C62828` | 0.85 | 0.00 |
| `cereal_sencha` | Sencha teabags | Chakra | Box | Paperboard | 0.13×0.17×0.08 | 0.14 | 380 | `#2E7D32` | `#F1F8E9` | 0.26 | 0.00 |

### Aisle 2 · Snacks & Crisps

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `snack_krunchos` | Krunchos salted | Krunchos | Bag | FoilLaminate | 0.20×0.30×0.09 | 0.09 | 180 | `#1565C0` | `#FDD835` | 0.55 | 0.00 |
| `snack_krunchos_sco` | Krunchos sour cream & onion | Krunchos | Bag | FoilLaminate | 0.20×0.30×0.09 | 0.09 | 180 | `#2E7D32` | `#FDD835` | 0.55 | 0.00 |
| `snack_pretzelpals` | Pretzel Pals | Pretzel Pals | Bag | PlasticFilm | 0.18×0.26×0.08 | 0.14 | 160 | `#6D4C41` | `#FFF176` | 0.58 | 0.00 |
| `snack_wasabiwave` | Wasabi Wave rice crackers | Wasabi Wave | Box | Paperboard | 0.16×0.22×0.07 | 0.16 | 240 | `#7CB342` | `#FFFFFF` | 0.28 | 0.00 |
| `snack_nutzy` | Nutzy mixed nuts | Nutzy | Pouch | FoilLaminate | 0.15×0.20×0.07 | 0.20 | 520 | `#8D6E63` | `#FFB300` | 0.48 | 0.00 |
| `snack_popcorn` | Popcorn Panic butter | Popcorn Panic | Bag | Paperboard | 0.14×0.24×0.07 | 0.11 | 190 | `#FFF9C4` | `#E64A19` | 0.30 | 0.00 |

### Checkout · Sweets & Impulse

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `sweet_kittokatsu` | a Kitto Katsu bar | Kitto Katsu | Wrapper | FoilLaminate | 0.11×0.02×0.09 | 0.05 | 130 | `#D32F2F` | `#FFFFFF` | 0.55 | 0.00 |
| `sweet_chocobo` | a Chocobo milk bar | Chocobo | Wrapper | FoilLaminate | 0.15×0.02×0.06 | 0.10 | 160 | `#4E342E` | `#FFD54F` | 0.55 | 0.00 |
| `sweet_gummygang` | Gummy Gang bears | Gummy Gang | Bag | PlasticFilm | 0.12×0.17×0.05 | 0.10 | 140 | `#8E24AA` | `#FFEB3B` | 0.60 | 0.00 |
| `sweet_pokki` | Pokki sticks | Pokki | Box | Paperboard | 0.06×0.16×0.04 | 0.07 | 150 | `#E91E63` | `#FFF8E1` | 0.28 | 0.00 |
| `sweet_mochibites` | Mochi Bites | Mochi Bites | Tray | PlasticFilm | 0.13×0.05×0.10 | 0.14 | 280 | `#F8BBD0` | `#4A148C` | 0.60 | 0.00 |
| `sweet_mintz` | a tin of Mintz | Mintz | Box | Steel | 0.06×0.02×0.04 | 0.05 | 120 | `#26A69A` | `#FFFFFF` | 0.70 | 0.85 |

### Aisle 1 · Soft Drinks

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `drink_pipisi` | Pipisi | Pipisi | Bottle | Pet | 0.07×0.23×0.07 | 0.53 | 160 | `#1A237E` | `#D32F2F` | 0.92 | 0.00 |
| `drink_pipisi_zero` | Pipisi Zero | Pipisi | Bottle | Pet | 0.07×0.23×0.07 | 0.53 | 160 | `#212121` | `#D32F2F` | 0.92 | 0.00 |
| `drink_kokakora` | Koka-Kora | Koka-Kora | Can | Aluminium | 0.07×0.12×0.07 | 0.34 | 130 | `#B71C1C` | `#FFFFFF` | 0.78 | 0.95 |
| `drink_fanto` | Fanto orange | Fanto | Bottle | Pet | 0.07×0.23×0.07 | 0.53 | 160 | `#EF6C00` | `#FFFFFF` | 0.92 | 0.00 |
| `drink_aquapura` | Aqua Pura water | Aqua Pura | Bottle | Pet | 0.08×0.28×0.08 | 1.02 | 110 | `#B3E5FC` | `#01579B` | 0.94 | 0.00 |
| `drink_genki` | a Genki energy drink | Genki | Can | Aluminium | 0.05×0.15×0.05 | 0.26 | 210 | `#FDD835` | `#212121` | 0.78 | 0.95 |
| `drink_chakra_tea` | Chakra green tea | Chakra | Bottle | Pet | 0.07×0.25×0.07 | 0.62 | 150 | `#2E7D32` | `#FFFFFF` | 0.92 | 0.00 |

### Aisle 3 · Tins & Jars

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `canned_tuna` | a tin of Tunatastic | Tunatastic | Can | Steel | 0.08×0.04×0.08 | 0.18 | 240 | `#0277BD` | `#FFF176` | 0.62 | 0.90 |
| `canned_beans` | Bean Machine baked beans | Bean Machine | Can | Steel | 0.08×0.11×0.08 | 0.42 | 190 | `#2E7D32` | `#FF6F00` | 0.62 | 0.90 |
| `canned_sweetcorn` | Corn Star sweetcorn | Corn Star | Can | Steel | 0.07×0.09×0.07 | 0.34 | 170 | `#FBC02D` | `#1B5E20` | 0.62 | 0.90 |
| `canned_sardines` | a tin of Sardino | Sardino | Can | Steel | 0.11×0.03×0.07 | 0.13 | 260 | `#00838F` | `#FFCC80` | 0.62 | 0.90 |
| `canned_nori` | a jar of nori paste | Umi | Jar | Glass | 0.06×0.11×0.06 | 0.24 | 310 | `#1B2A22` | `#C62828` | 0.88 | 0.00 |
| `canned_miso` | Miso Master miso | Miso Master | Tub | Hdpe | 0.12×0.10×0.12 | 0.75 | 420 | `#8D6E63` | `#D84315` | 0.46 | 0.00 |

### Aisle 5 · Noodles, Pasta & Rice

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `noodle_ramyum_cup` | a Ramyum cup noodle | Ramyum | Tub | Paperboard | 0.11×0.12×0.11 | 0.10 | 190 | `#D50000` | `#FFE082` | 0.32 | 0.00 |
| `noodle_ramyum_5pk` | a Ramyum spicy 5-pack | Ramyum | Bag | PlasticFilm | 0.22×0.18×0.12 | 0.55 | 520 | `#BF360C` | `#FFECB3` | 0.58 | 0.00 |
| `noodle_soba` | Soba Sensei dried soba | Soba Sensei | Bag | PlasticFilm | 0.11×0.26×0.05 | 0.32 | 340 | `#4E342E` | `#EFEBE9` | 0.55 | 0.00 |
| `noodle_pasta` | Pasta Basta spaghetti | Pasta Basta | Bag | PlasticFilm | 0.09×0.28×0.05 | 0.52 | 280 | `#1565C0` | `#FFC107` | 0.55 | 0.00 |
| `noodle_rice` | a bag of Kome King rice | Kome King | Bag | PlasticFilm | 0.20×0.30×0.11 | 2.10 | 980 | `#F5F5F5` | `#C62828` | 0.50 | 0.00 |
| `noodle_udon` | Udon Uno fresh udon | Udon Uno | Pouch | PlasticFilm | 0.16×0.13×0.06 | 0.24 | 160 | `#FFF8E1` | `#00695C` | 0.60 | 0.00 |

### Aisle 7 · Household & Cleaning

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `house_sparklespray` | Sparkle Spray cleaner | Sparkle | Bottle | Hdpe | 0.10×0.27×0.07 | 0.78 | 380 | `#00ACC1` | `#FFEB3B` | 0.66 | 0.00 |
| `house_dishsoap` | Bubbles dish soap | Bubbles | Bottle | Hdpe | 0.07×0.22×0.05 | 0.52 | 240 | `#43A047` | `#FFFFFF` | 0.66 | 0.00 |
| `house_laundry` | Whitewash laundry powder | Whitewash | Box | Paperboard | 0.19×0.28×0.10 | 1.30 | 760 | `#1E88E5` | `#FFFFFF` | 0.26 | 0.00 |
| `house_kitchenroll` | kitchen roll | Softly | Bag | PlasticFilm | 0.24×0.25×0.12 | 0.36 | 320 | `#FFFFFF` | `#29B6F6` | 0.58 | 0.00 |
| `house_binbags` | a roll of bin bags | Sakku | Box | Paperboard | 0.14×0.09×0.08 | 0.30 | 260 | `#37474F` | `#8BC34A` | 0.28 | 0.00 |
| `house_sponges` | a pack of sponges | Sponge Squad | Bag | PlasticFilm | 0.16×0.12×0.09 | 0.08 | 180 | `#FDD835` | `#4CAF50` | 0.58 | 0.00 |

### Aisle 6 · Health & Beauty

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `care_toothpaste` | Freshbreath toothpaste | Freshbreath | Box | Paperboard | 0.05×0.19×0.04 | 0.14 | 290 | `#039BE5` | `#FFFFFF` | 0.28 | 0.00 |
| `care_shampoo` | Silkstrand shampoo | Silkstrand | Bottle | Hdpe | 0.09×0.23×0.06 | 0.55 | 640 | `#7E57C2` | `#F3E5F5` | 0.68 | 0.00 |
| `care_soap` | a bar of Soapy Sudz | Soapy Sudz | Wrapper | Paperboard | 0.09×0.03×0.06 | 0.11 | 130 | `#FFF3E0` | `#EC407A` | 0.32 | 0.00 |
| `care_tissues` | pocket tissues | Tissue Tower | Bag | PlasticFilm | 0.11×0.06×0.05 | 0.06 | 110 | `#FFFFFF` | `#42A5F5` | 0.60 | 0.00 |
| `care_sanitiser` | Handy sanitiser gel | Handy | Bottle | Pet | 0.05×0.14×0.04 | 0.14 | 320 | `#E0F7FA` | `#00838F` | 0.90 | 0.00 |
| `care_plasters` | a box of plasters | Plaster Patrol | Box | Paperboard | 0.09×0.06×0.03 | 0.05 | 250 | `#FFCC80` | `#C62828` | 0.28 | 0.00 |

### Back Wall · Pet

| id | name | brand | shape | material | w×h×d (m) | kg | ¥ | primary | accent | smooth | metal |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `pet_cat_pouch` | a Nyan Nyan cat food pouch | Nyan Nyan | Pouch | FoilLaminate | 0.12×0.14×0.03 | 0.09 | 120 | `#EC407A` | `#FFF176` | 0.50 | 0.00 |
| `pet_cat_tin` | a Nyan Nyan tuna tin | Nyan Nyan | Can | Steel | 0.08×0.04×0.08 | 0.17 | 150 | `#AD1457` | `#FFFFFF` | 0.62 | 0.90 |
| `pet_dog_can` | a Wan Wan dog chunks tin | Wan Wan | Can | Steel | 0.09×0.11×0.09 | 0.44 | 230 | `#1565C0` | `#FF8F00` | 0.62 | 0.90 |
| `pet_dog_kibble` | a bag of Wan Wan kibble | Wan Wan | Bag | PlasticFilm | 0.21×0.30×0.11 | 1.80 | 890 | `#0D47A1` | `#FDD835` | 0.52 | 0.00 |
| `pet_bird_seed` | Birdy seed mix | Birdy | Box | Paperboard | 0.12×0.20×0.07 | 0.55 | 340 | `#8BC34A` | `#FFECB3` | 0.28 | 0.00 |
| `pet_hamster_bed` | Hamu hamster bedding | Hamu | Bag | PlasticFilm | 0.20×0.24×0.12 | 0.40 | 420 | `#FFB74D` | `#5D4037` | 0.52 | 0.00 |

---

## 6. Packaging parameters

### The envelope

Every product has to fit the shelf slot it stands in. The slot's trigger is
**0.26 × 0.44 × 0.26 m**, centred 0.20 m above the shelf surface, so the working limit is

> **width ≤ 0.25 m · height ≤ 0.42 m · depth ≤ 0.25 m**

Anything larger pokes through the facing next door. The placeholder, `Item_def`, is a
0.20 × 0.40 × 0.20 box; the tallest real product is the daikon at 0.40. The item's origin sits
at the *centre* of the model. The snap point is 0.20 above the board (the placeholder's middle),
so a real product is lifted or lowered by the difference (`Item.restHeight`) and stands on the
board whatever its height.

### Shapes

`PackShape` is the silhouette, and is all a modeller needs to block one out.

| shape | what it is |
|---|---|
| `Box` | rectangular carton, printed on every face — cereal, tea, crackers |
| `Carton` | gable-top or brick carton — milk, juice |
| `Bottle` | necked, round or oval footprint |
| `Can` | cylinder with seamed ends |
| `Jar` | short, wide, screw lid |
| `Bag` | pillow bag, sealed top and bottom, puffed in the middle |
| `Pouch` | flat stand-up pouch with a gusseted base |
| `Tub` | round or oval with a lid, wider than tall |
| `Tray` | shallow, film-sealed |
| `Wrapper` | flow-wrapped bar or roll |
| `Net` | mesh bag of loose produce |
| `Loose` | no package at all |

### Materials

`PackMaterial` labels the package *and* drives the URP Lit material. Suggested starting
values — the per-product `smooth` and `metal` columns above override them:

| material | metallic | smoothness | notes |
|---|---|---|---|
| `Cardboard` | 0.00 | 0.15 | corrugated, visible flute, matte |
| `Paperboard` | 0.00 | 0.26 | folding carton, slight coat |
| `Pet` | 0.00 | 0.92 | clear drinks plastic; needs transparency |
| `Hdpe` | 0.00 | 0.66 | opaque bottle plastic, smooth but not glossy |
| `Glass` | 0.00 | 0.88 | transparent, often tinted |
| `Aluminium` | 0.95 | 0.78 | drinks can |
| `Steel` | 0.90 | 0.62 | food tin, duller than aluminium |
| `PlasticFilm` | 0.00 | 0.58 | crinkled bag; sharp small highlights |
| `FoilLaminate` | 0.00–0.30 | 0.50 | metallised film, between film and metal |
| `WaxedCarton` | 0.00 | 0.35 | milk carton, matte with a waxy sheen |
| `Mesh` | 0.00 | 0.30 | produce net; wants an alpha-clipped weave |

Two things a URP Lit material still needs that are deliberately *not* in the catalogue,
because they are per-asset rather than per-product: the **albedo / label texture** and the
**normal map** for crinkle and flute. `ProductDef` carries the colours so a placeholder
material can be generated without either.

> **If you have packaging parameters from elsewhere** — a spec sheet, another conversation,
> a supplier's dimensions — the place to put them is the `P(...)` row for that product in
> `ProductCatalog.cs`. The row is positional: `id, name, brand, section, shape, material,
> sizeX, sizeY, sizeZ, mass, price, primaryHex, accentHex, smoothness[, metallic]`. Nothing
> else has to change; this document is regenerated from those rows.

---

## 7. How a bay gets stocked

A **facing** is one board on one side of a bay — the whole of the top board, front side. That
is how a real planogram is blocked out, and it is how this works too:

- A `ShelfTwoside` bay has 3 boards × 2 sides × 6 slots = **36 slots in 6 facings**,
  so it shows six different products.
- A one-sided bay or a tall pillar has **3 facings**.
- A short pillar has **1**.

`StoreLayout` decides which section a bay belongs to; `Planogram` decides what each of its boards
carries, from the board's height and the side it faces. The rules are the ones every
supermarket chain works to:

| rule | what it means here |
|---|---|
| **Eye level is buy level** | The top board (1.4 m) is where an adult's eye lands: brand leaders and the lines with the best margin. |
| **Kids' eye level** | The middle board (0.8 m) is a child's eye level: Choco Loops, Honey Nutz, Gummy Gang and Pokki are never higher. |
| **Heavy goes low** | Nothing over 0.8 kg is on the top board. Rice, kibble, the litre of water and the laundry box sit on the bottom board. |
| **Vertical brand blocks** | A brand's lines share one face of a bay, one above the other (Krunchos, Pipisi, Moo-Moo, Ramyum, Kamado, Freezy, Nyan Nyan, Wan Wan), so a shopper walking the aisle passes every brand once. |
| **Best sellers take more facings** | Bananas, whole milk and Ramyum cups appear in more of their section's bays than anything else in it. |
| **End caps** | The ends of runs (the `Shelfpillar_E` pieces) carry the section's promotion, or a cross-merchandised partner: Pipisi on the crisps' end caps, Krunchos on the drinks'. A cross-merchandised facing keeps its own section, so the crisps' end cap still only takes cola. |
| **Impulse at the till** | The facing on the till counter is Mintz, and shoppers sometimes grab sweets while queueing. |

Each section has two layouts, alternated across its bays by position so neighbouring bays don't
match. Short two-board bays use the waist and stoop rows.

**Produce · Fruit & Veg**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a bag of salad mix | bananas | a bag of Fuji apples |
| A | back | a tray of tomatoes | a net of mikan | a daikon radish |
| B | front | a tray of tomatoes | bananas | a daikon radish |
| B | back | a bag of salad mix | a net of mikan | a bag of Fuji apples |

End cap: a net of mikan, bananas.

**Bakery**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a melon pan | an anpan | a Shokupan white loaf |
| A | back | a pack of croissants | a pack of sesame bagels | a Rye Rider sourdough |
| B | front | a melon pan | a Shokupan white loaf | an anpan |
| B | back | a pack of croissants | a pack of sesame bagels | a Rye Rider sourdough |

End cap: a pack of croissants, a melon pan.

**Back Wall · Dairy & Chilled**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Yogo strawberry yoghurt | Moo-Moo skimmed milk | Moo-Moo whole milk |
| A | back | Butterfly salted butter | a box of eggs | Kumo cream cheese |
| B | front | Kumo cream cheese | a box of eggs | Moo-Moo whole milk |
| B | back | Yogo strawberry yoghurt | Butterfly salted butter | Moo-Moo whole milk |

End cap: Yogo strawberry yoghurt.

**Back Wall · Frozen**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Ice Dream vanilla | a bag of Freezy peas | a bag of frozen fries |
| A | back | Gyoza Gang gyoza | Kaiten prawns | a Pizza Piccolo margherita |
| B | front | Gyoza Gang gyoza | a Pizza Piccolo margherita | Kaiten prawns |
| B | back | Ice Dream vanilla | a bag of Freezy peas | a bag of frozen fries |

End cap: Ice Dream vanilla.

**Aisle 4 · Cereal & Breakfast**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Bran Flakies | Choco Loops | Oatsy instant porridge |
| A | back | Kafé instant coffee | Honey Nutz clusters | Morning Mochi granola |
| B | front | Morning Mochi granola | Honey Nutz clusters | Bran Flakies |
| B | back | Kafé instant coffee | Sencha teabags | Choco Loops |

End cap: Kafé instant coffee, Choco Loops.

**Aisle 2 · Snacks & Crisps**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Krunchos salted | Krunchos sour cream & onion | Popcorn Panic butter |
| A | back | Nutzy mixed nuts | Wasabi Wave rice crackers | Pretzel Pals |
| B | front | Nutzy mixed nuts | Krunchos salted | Krunchos sour cream & onion |
| B | back | Wasabi Wave rice crackers | Popcorn Panic butter | Pretzel Pals |

End cap: Pipisi, Krunchos salted.

**Checkout · Sweets & Impulse**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a tin of Mintz | a Kitto Katsu bar | Gummy Gang bears |
| A | back | a Chocobo milk bar | Pokki sticks | Mochi Bites |
| B | front | a Chocobo milk bar | Gummy Gang bears | Pokki sticks |
| B | back | Mochi Bites | a Kitto Katsu bar | Gummy Gang bears |

End cap: a Kitto Katsu bar.

**Aisle 1 · Soft Drinks**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Pipisi | Pipisi Zero | Aqua Pura water |
| A | back | Koka-Kora | Fanto orange | Chakra green tea |
| B | front | a Genki energy drink | Chakra green tea | Aqua Pura water |
| B | back | Koka-Kora | Pipisi Zero | Pipisi |

End cap: a Genki energy drink, Krunchos salted.

**Aisle 3 · Tins & Jars**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a jar of nori paste | a tin of Tunatastic | Bean Machine baked beans |
| A | back | a tin of Sardino | Corn Star sweetcorn | Miso Master miso |
| B | front | a tin of Tunatastic | a tin of Sardino | Miso Master miso |
| B | back | a jar of nori paste | Corn Star sweetcorn | Bean Machine baked beans |

End cap: a tin of Tunatastic.

**Aisle 5 · Noodles, Pasta & Rice**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a Ramyum cup noodle | a Ramyum spicy 5-pack | a bag of Kome King rice |
| A | back | Udon Uno fresh udon | Soba Sensei dried soba | Pasta Basta spaghetti |
| B | front | Udon Uno fresh udon | Pasta Basta spaghetti | a bag of Kome King rice |
| B | back | a Ramyum cup noodle | Soba Sensei dried soba | a Ramyum spicy 5-pack |

End cap: a Ramyum cup noodle.

**Aisle 6 · Health & Beauty**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Silkstrand shampoo | Freshbreath toothpaste | pocket tissues |
| A | back | Handy sanitiser gel | a box of plasters | a bar of Soapy Sudz |
| B | front | Handy sanitiser gel | Freshbreath toothpaste | a bar of Soapy Sudz |
| B | back | Silkstrand shampoo | a box of plasters | pocket tissues |

End cap: Handy sanitiser gel.

**Aisle 7 · Household & Cleaning**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Sparkle Spray cleaner | Bubbles dish soap | Whitewash laundry powder |
| A | back | a pack of sponges | a roll of bin bags | kitchen roll |
| B | front | Bubbles dish soap | a pack of sponges | kitchen roll |
| B | back | Sparkle Spray cleaner | a roll of bin bags | Whitewash laundry powder |

End cap: kitchen roll.

**Back Wall · Pet**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a Nyan Nyan cat food pouch | a Nyan Nyan tuna tin | Hamu hamster bedding |
| A | back | Birdy seed mix | a Wan Wan dog chunks tin | a bag of Wan Wan kibble |
| B | front | Birdy seed mix | a Wan Wan dog chunks tin | a bag of Wan Wan kibble |
| B | back | a Nyan Nyan cat food pouch | a Nyan Nyan tuna tin | Hamu hamster bedding |

End cap: a Nyan Nyan cat food pouch.


### The block on each slot

A slot holds one item, the thing the player picks up and puts back, but a stocked shelf shows a
block of the product: as many across as fit the slot, up to three deep, and flat tins and bars
stacked. `Backstock` draws that block round the slot's item as one shared mesh per product and
size, with no colliders and no shadows, and hides it while the slot is empty. A gap on the shelf
still means "restock me", and the item itself stands in one of the block's cells.

Restocking uses a single placeholder prefab for the whole building, so `ShelfSlot.FillWithNewItem`
stamps the facing's section and product onto whatever it spawns, and `ProductLook` swaps the
placeholder box for the product's model.

### The aisles

The middle of the shop is **Aisles 1–7**; the edge is named departments (Fruit & Veg, Bakery,
Dairy & Chilled, Frozen, Pet, Sweets), as in any supermarket. `AisleSigns` hangs a four-sided
sign over the middle of each zone's bays at 3 m, clear of the 2 m shelving: a numbered badge
and the aisle's name for an aisle, the department's name for a department, with the Japanese
underneath. It's built at load, like the stock.

## 8. Where this lives in code

| file | what it does |
|---|---|
| `_Game/Items/Scripts/Item.cs` | `ItemType` (the 13 sections + 4 tools); `Item.productId`, `Item.DisplayName` |
| `_Game/Items/Scripts/ProductCatalog.cs` | `ProductDef`, `PackShape`, `PackMaterial`, the 80-row table, lookups |
| `_Game/Level/Scripts/StoreLayout.cs` | the zone table (aisle numbers, names, what customers call them), `SectionAt`, `ApplyToScene`, `Describe` |
| `_Game/Level/Scripts/Planogram.cs` | the merchandising rules and layouts; which facings stock each product |
| `_Game/Level/Scripts/Backstock.cs` | the block of product drawn round each slot's item |
| `_Game/Level/Scripts/AisleSigns.cs` | the hanging aisle and department signs |
| `_Game/Level/Scripts/ShelfSlot.cs` | `requiredType` + `productId` per facing; `Label`; stamps restocked items |
| `_Game/Items/Scripts/ProductLook.cs` | dresses a placeholder item as its product's model |
| `_Game/Items/Editor/ProductImport.cs` | `Kehai/Products/…`: imports the models and lays out the showcase on `Models_Island` |
| `_Game/Characters/Scripts/ShoppingList.cs` | what a shopper came in for, in walking order |
| `_Game/Characters/Scripts/CustomerQuestion.cs` | how they ask, and how they say thanks |
| `_Game/Level/Scripts/ShelfUnit.cs` | `section` sign and `category` per bay |
| `_Game/Characters/Scripts/CustomerRequest.cs` | asks for a product by name; the bubble's position |
| `_Game/Level/Editor/StoreLayoutBuilder.cs` | `Kehai/Store/Report Layout` and `Kehai/Store/Apply Layout` |

**The shop stocks itself at load** — `StoreLayout` has a `[RuntimeInitializeOnLoadMethod]`
that runs the plan over every bay before the first frame. Nothing needs to be baked for the
game to work.

The two menu items are for authoring:

- **`Kehai/Store/Report Layout`** — prints the table in section 4 for the scene as it
  currently stands, and names any product that has ended up with no facing. Writes nothing.
- **`Kehai/Store/Apply Layout`** — bakes the plan into the scene so the Inspector shows
  each bay's section and each facing's product instead of the placeholder cereal. Optional,
  undoable, and it costs something: every facing becomes a prefab override in a scene file
  that is already a megabyte. Reach for the report first.

---

## 9. What the customer says

Shoppers come in with a **list**: two to four products, drawn by how often each section is
shopped (milk, fruit, bread and drinks far more than plasters or hamster bedding) and by each
product's share of its section's facings. The list is walked in store order: fresh food by the
door, then the aisles, then the back wall. At each product they walk to the nearest facing that
stocks it and take one. Waiting at the till, about one in three grabs something from the sweets.

They ask for help in two situations:

- **The shelf is empty.** They ask whether there's any more, and are walked to another facing
  that has it.
- **Sometimes, the next thing on the list.** They ask in one of four ways:

| how they ask | example | done when |
|---|---|---|
| by name | *"Excuse me - I'm looking for Pipisi Zero."* | they reach the facing |
| which aisle | *"Which aisle would I find a tin of Tunatastic in?"* | they reach the facing |
| by aisle | *"Which way is aisle 5? I need the noodles."* | they're anywhere in the aisle |
| by department | *"Where's the bakery?"* | they're anywhere in the department |

A department has no number, so something sold round the edge of the shop is never asked for by
aisle. Whatever is asked, the shelf the beacon lands on really stocks it. Product names are
written as **object phrases** (`Pipisi Zero`, `a tin of Tunatastic`), so they drop into any
line without a `the` in front.

With no planogram (a test scene, an old layout), shoppers fall back to the old way: a few
random shelves, and a request for whatever a random facing holds.

The speech bubble hangs **0.4 m in front of the customer's legs and 0.25 m above the floor**,
and grows upward from there.

## 10. Changing it

**Add a product** — one `P(...)` row in `ProductCatalog.cs`, and a board for it in one of its
section's layouts in `Planogram.cs` (a test fails until it has one). Keep within the
0.25 × 0.42 × 0.25 envelope. Until its model is imported it shows as the placeholder box.

**Re-merchandise a section** — edit its two layouts, or its end cap, in `Planogram.cs`. The tests
hold the rules: nothing heavy at eye level, children's lines below it, a brand on one face.

**Move a section** — edit its rectangle in `StoreLayout.Zones`. The zones must keep tiling the
plane; `Report Layout` will show the new bay and facing counts straight away.

**Add a section** — append to `ItemType` (on the end — never renumber), add a name to
`ProductCatalog.SectionName`, add it to `StockSections`, give it products, and add a zone.

**Re-balance a section that is too big or too small** — the bay counts in section 4 are the
measurement. Produce is deliberately the largest at 29 bays; anything under about 6 bays will
feel like a corner rather than a section.

---

## 11. The models

The 80 products are low-poly models with their own printed labels. They're made outside the
repository, in a Blender pipeline in `~/Desktop/KehaiRelated/Products` that reads the catalogue,
draws the labels and builds the meshes. **Kehai/Products/1. Import Product Models** brings them
in:

| where | what |
|---|---|
| `Items/Products/Models/<id>.fbx` | the mesh: real size, origin at the centre of its box, front facing +Z |
| `Items/Products/Labels/<id>.png` | its label: front panel, side panel and a strip of flat colours for caps and lids |
| `Items/Products/Materials/` | URP Lit. One `L_<id>` per label (smoothness and metallic from the catalogue row, metallic capped at 0.35 because printed ink isn't bare metal), plus the shared ones: can tops, tin lids, bare metal, PET with the drink inside, film, the fruit nets |
| `Items/Products/Resources/Products/<id>.prefab` | a variant of `Item_def` wearing the model, its collider sized to it and its mass from the catalogue |

None of it is in version control (the repository holds scripts only), so a fresh checkout shows
the placeholder boxes until the import is run. **Kehai/Products/2. Lay Out the Showcase on
Models_Island** stands every product on display shelves along the north edge of the model
island, one section per shelf in walking order, each with its name and price on the shelf edge.
