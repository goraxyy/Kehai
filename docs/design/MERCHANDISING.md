# Merchandising: how the shelves are stocked

Where every product sits, and why. Kehai's store is stocked the way a real supermarket is:
by a planogram, a fixed plan for which product goes on which shelf. This file describes the
strategy. The code it describes is in `Assets/!_Project/_Game/Level/Scripts/`:

| file | what it does |
|---|---|
| `Planogram.cs` | the rules and each aisle's layouts |
| `ShelfGrid.cs` | how a shelf is cut into slots |
| `StoreLayout.cs` | which aisle a bay belongs to, and stocking the shop at load |
| `ShelfSlot.cs`, `ShelfStock.cs` | the slots, as data, and the lookups over them |
| `ShelfDrawer.cs`, `ShelfAim.cs` | drawing the stock, and aiming at a slot |

[`STORE_CATALOG.md`](STORE_CATALOG.md) lists the 80 products themselves.

---

## 1. The route decides the aisles

A shopper comes in through the doors at the north-east, goes round the floor, and leaves
through the tills at the north-west. The aisles are numbered in that order, **1 at the door
and 13 at the tills**, and every section is placed where real stores put it:

| aisle | sells | why there |
|---|---|---|
| 1 | Fruit & Veg | Fresh food in the "decompression zone" by the door sets the tone, and nobody wants to carry it under a basket of tins. It is the biggest section. |
| 2 | Bakery | Also at the front, where the smell reaches the door. |
| 3 | Soft Drinks | Heavy and fast-selling, so it gets a long run on the east wall. |
| 4 | Snacks & Crisps | Next to the drinks: crisps sell beside cola. |
| 5 | Tins & Jars | Long shelf life goes where footfall is lowest, in the middle. |
| 6 | Cereal & Breakfast | Coffee and tea sit with the cereal, the way a breakfast aisle is blocked. |
| 7 | Noodles, Pasta & Rice | The rest of the dry store cupboard. |
| 8 | Health & Beauty | Non-food is blocked together, away from the food. It gets low gondolas so the sightline across the shop stays open. |
| 9 | Household & Cleaning | Beside health & beauty. |
| 10 | Dairy & Chilled | **Milk at the far wall**: the most-bought item is as far from the door as the building allows, so fetching it crosses everything else. |
| 11 | Frozen | Beside dairy, at the cold end. |
| 12 | Pet | Bulky and infrequent, so it takes the far corner. |
| 13 | Sweets | At the tills, for the impulse buy in the queue. |

A black sign hangs over every aisle: its number, its name, and the Japanese underneath.

## 2. Which shelf: the vertical rules

A 2 m gondola has three boards. Where a product sits on them decides how well it sells, so
every chain has the same rules:

| board | height | what goes there |
|---|---|---|
| **eye level** | 1.4 m | "Eye level is buy level": the brand leaders and the lines with the best margin |
| **waist level** | 0.8 m | A child's eye level: Choco Loops, Honey Nutz, Gummy Gang and Pokki are never higher |
| **stoop level** | 0.2 m | Heavy and bulky packs: rice, kibble, the litre of water, the laundry box. They are safe to lift from here, and people who want them will stoop |

A short two-board bay uses the waist and stoop rules. **Nothing over 0.8 kg goes on the top
board**, and the tests fail if a layout puts it there.

## 3. Which side, which neighbours

- **Vertical brand blocks.** A brand's lines share one face of a bay, one above the other:
  Krunchos salted over Krunchos sour cream, Pipisi over Pipisi Zero, the Moo-Moo milks, the
  Ramyum cup and 5-pack, all three Kamado breads, both Freezy bags, Nyan Nyan, Wan Wan. A
  shopper walking the aisle passes each brand once. The tests hold this too.
- **Best sellers take more facings.** Bananas, whole milk and Ramyum cups appear in more of
  their aisle's bays than anything else in it. That share is also how often shoppers come in
  for them.
- **Two layouts per aisle**, alternated across its bays by position, so neighbouring bays
  don't look alike.
- **End caps.** The bays at the ends of runs (the `Shelfpillar_E` pieces) carry the aisle's
  promotion, or a **cross-merchandised** partner: Pipisi on the crisps' end caps, Krunchos on
  the drinks'. A cross-merchandised slot keeps its product's own section, so the crisps' end
  cap only takes cola back.
- **The till counter** has mints, the impulse buy every till in the world has.
- **Everything is on sale somewhere.** The layouts are written for a bay with three boards a
  side, and not every aisle has those. Sweets are on one-sided bays, so their back faces never
  show. Health & beauty is mostly short bays with no eye-level board. A product whose layouts
  only use boards its aisle doesn't have takes one facing from the product with the most in its
  aisle. The facing it takes is on a board it's allowed on (nothing heavy and nothing for
  children at eye level), and on the board its layouts give it if the aisle has that board.
  Without this, Mochi Bites and the hand sanitiser were never on a shelf.

## 4. How a shelf is filled: the grid

Every board is cut into **0.5 m squares**:

| board | squares |
|---|---|
| two-sided bay, 4 × 1 m | 16 per board: 8 facing each aisle, so 48 on a three-board bay |
| one-sided bay, 4 × 0.5 m | 8 |
| pillar, 1 × 1 m | 4 |
| `b` pillar, 1 × 0.5 m | 2 |
| tail, 0.5 × 0.5 m | 1 |

Every square on one board facing one aisle sells the same product: that's a **facing**, and the
planogram picks its product. Each square is then filled with slots that fit that product:

| product | slots in a square | for example |
|---|---|---|
| small: fits a quarter of the square | 2 × 2 | tins, the Pokki box, the toothpaste |
| long: fits half the square, running into it | 2 across, 1 deep | a sourdough loaf |
| wide: fits half the square, running along it | 1 across, 2 deep | a cereal box, a bag of crisps, a bag of rice |
| a drink | as many as its footprint allows, up to 3 × 3 | cans and bottles |
| anything bigger | 1 | the pizza box |

One slot holds one item. The item is centred in its slot, standing on the board, facing the
aisle (the back row faces the other aisle), so a full shelf looks full and an empty slot is a
gap you can see.

The slots and what's on them are data: 16,500 records (`ShelfSlot`), with nothing about them
saved in the scene. `ShelfDrawer` shows what stands on each as a bare render object (a mesh
and its materials, no collider or script, never saved), which the GPU Resident Drawer culls
one by one. The player aims at a slot by ray against its box (`ShelfAim`). An item becomes a
real, interactive GameObject only when it leaves its shelf, and goes back to being data when
it's put back ([`IDEAS.md`](IDEAS.md#scaling-a-lot-of-stock-and-a-maze-with-no-end), step 2).

**The products are shown 1.5 times real size** so their labels read from the aisle. Tall ones are
shown smaller so they fit under the board above: nothing is over 0.5 m, so the daikon is
1.25 times real size. The catalogue keeps real sizes; the importer scales the models.

## 5. The layouts

Generated from `Planogram.cs`.

**Aisle 1 · Fruit & Veg**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a bag of salad mix | bananas | a bag of Fuji apples |
| A | back | a tray of tomatoes | a net of mikan | a daikon radish |
| B | front | a tray of tomatoes | bananas | a daikon radish |
| B | back | a bag of salad mix | a net of mikan | a bag of Fuji apples |

End cap: a net of mikan, bananas.

**Aisle 2 · Bakery**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a melon pan | an anpan | a Shokupan white loaf |
| A | back | a pack of croissants | a pack of sesame bagels | a Rye Rider sourdough |
| B | front | a melon pan | a Shokupan white loaf | an anpan |
| B | back | a pack of croissants | a pack of sesame bagels | a Rye Rider sourdough |

End cap: a pack of croissants, a melon pan.

**Aisle 3 · Soft Drinks**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Pipisi | Pipisi Zero | Aqua Pura water |
| A | back | Koka-Kora | Fanto orange | Chakra green tea |
| B | front | a Genki energy drink | Chakra green tea | Aqua Pura water |
| B | back | Koka-Kora | Pipisi Zero | Pipisi |

End cap: a Genki energy drink, Krunchos salted.

**Aisle 4 · Snacks & Crisps**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Krunchos salted | Krunchos sour cream & onion | Popcorn Panic butter |
| A | back | Nutzy mixed nuts | Wasabi Wave rice crackers | Pretzel Pals |
| B | front | Nutzy mixed nuts | Krunchos salted | Krunchos sour cream & onion |
| B | back | Wasabi Wave rice crackers | Popcorn Panic butter | Pretzel Pals |

End cap: Pipisi, Krunchos salted.

**Aisle 5 · Tins & Jars**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a jar of nori paste | a tin of Tunatastic | Bean Machine baked beans |
| A | back | a tin of Sardino | Corn Star sweetcorn | Miso Master miso |
| B | front | a tin of Tunatastic | a tin of Sardino | Miso Master miso |
| B | back | a jar of nori paste | Corn Star sweetcorn | Bean Machine baked beans |

End cap: a tin of Tunatastic.

**Aisle 6 · Cereal & Breakfast**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Bran Flakies | Choco Loops | Oatsy instant porridge |
| A | back | Kafé instant coffee | Honey Nutz clusters | Morning Mochi granola |
| B | front | Morning Mochi granola | Honey Nutz clusters | Bran Flakies |
| B | back | Kafé instant coffee | Sencha teabags | Choco Loops |

End cap: Kafé instant coffee, Choco Loops.

**Aisle 7 · Noodles, Pasta & Rice**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a Ramyum cup noodle | a Ramyum spicy 5-pack | a bag of Kome King rice |
| A | back | Udon Uno fresh udon | Soba Sensei dried soba | Pasta Basta spaghetti |
| B | front | Udon Uno fresh udon | Pasta Basta spaghetti | a bag of Kome King rice |
| B | back | a Ramyum cup noodle | Soba Sensei dried soba | a Ramyum spicy 5-pack |

End cap: a Ramyum cup noodle.

**Aisle 8 · Health & Beauty**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Silkstrand shampoo | Freshbreath toothpaste | pocket tissues |
| A | back | Handy sanitiser gel | a box of plasters | a bar of Soapy Sudz |
| B | front | Handy sanitiser gel | Freshbreath toothpaste | a bar of Soapy Sudz |
| B | back | Silkstrand shampoo | a box of plasters | pocket tissues |

End cap: Handy sanitiser gel.

**Aisle 9 · Household & Cleaning**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Sparkle Spray cleaner | Bubbles dish soap | Whitewash laundry powder |
| A | back | a pack of sponges | a roll of bin bags | kitchen roll |
| B | front | Bubbles dish soap | a pack of sponges | kitchen roll |
| B | back | Sparkle Spray cleaner | a roll of bin bags | Whitewash laundry powder |

End cap: kitchen roll.

**Aisle 10 · Dairy & Chilled**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Yogo strawberry yoghurt | Moo-Moo skimmed milk | Moo-Moo whole milk |
| A | back | Butterfly salted butter | a box of eggs | Kumo cream cheese |
| B | front | Kumo cream cheese | a box of eggs | Moo-Moo whole milk |
| B | back | Yogo strawberry yoghurt | Butterfly salted butter | Moo-Moo whole milk |

End cap: Yogo strawberry yoghurt.

**Aisle 11 · Frozen**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | Ice Dream vanilla | a bag of Freezy peas | a bag of frozen fries |
| A | back | Gyoza Gang gyoza | Kaiten prawns | a Pizza Piccolo margherita |
| B | front | Gyoza Gang gyoza | a Pizza Piccolo margherita | Kaiten prawns |
| B | back | Ice Dream vanilla | a bag of Freezy peas | a bag of frozen fries |

End cap: Ice Dream vanilla.

**Aisle 12 · Pet**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a Nyan Nyan cat food pouch | a Nyan Nyan tuna tin | Hamu hamster bedding |
| A | back | Birdy seed mix | a Wan Wan dog chunks tin | a bag of Wan Wan kibble |
| B | front | Birdy seed mix | a Wan Wan dog chunks tin | a bag of Wan Wan kibble |
| B | back | a Nyan Nyan cat food pouch | a Nyan Nyan tuna tin | Hamu hamster bedding |

End cap: a Nyan Nyan cat food pouch.

**Aisle 13 · Sweets**

| layout | face | eye (1.4 m) | waist (0.8 m) | stoop (0.2 m) |
|---|---|---|---|---|
| A | front | a tin of Mintz | a Kitto Katsu bar | Gummy Gang bears |
| A | back | a Chocobo milk bar | Pokki sticks | Mochi Bites |
| B | front | a Chocobo milk bar | Gummy Gang bears | Pokki sticks |
| B | back | Mochi Bites | a Kitto Katsu bar | Gummy Gang bears |

End cap: a Kitto Katsu bar.


## 6. How customers use it

Shoppers come in with a list of 2–4 products, weighted by how often each aisle is shopped and by
each product's share of facings. They walk it in aisle order and take each item from the nearest
facing that has it. When a facing is empty, or sometimes for the next thing on their list,
they ask for help: by name, by aisle (*"Which way is aisle 7? I need the noodles."*), or because
the shelf is bare. [`STORE_CATALOG.md`](STORE_CATALOG.md) §9 has the details.

## 7. Changing it

- **Re-merchandise an aisle:** edit its two layouts or its end cap in `Planogram.cs`. The tests
  hold the rules: nothing heavy at eye level, children's lines below it, a brand on one face.
- **Change how many fit:** the square size and slot classes are at the top of `ShelfGrid.cs`;
  the display scale is in `ProductLook.cs`.
- **See it in the editor:** the stocked shelves are drawn there from the planogram, as in the
  game. **Kehai/Store/Hang Signs and Lamps** bakes the signs and lamps in to show them too.
- **Count it:** **Kehai/Store/Report Layout** prints bays, facings and slots per aisle.
