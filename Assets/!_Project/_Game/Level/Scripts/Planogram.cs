using System.Collections.Generic;
using UnityEngine;

// What goes on which board of a bay — the shop's merchandising, the way a real chain's
// planogram team would block it out. StoreLayout decides which section a bay belongs to;
// this decides what each of its boards carries. STORE_CATALOG.md §7 is this in prose.
//
// The rules, all standard supermarket practice:
//   - Eye level is buy level. The top board of a 2 m gondola (1.4 m) is where an adult's eye
//     lands, so it carries the brand leaders and the lines with the best margin.
//   - Waist level (0.8 m) is a child's eye level: the kids' cereal and the sweets go there.
//   - The bottom board carries what is heavy or bulky — rice, kibble, the litre of water, the
//     laundry box — safe to lift, and people who want it will stoop for it.
//   - Brands are blocked vertically: two lines of one brand share a side of a bay, one above
//     the other, so a shopper walking the aisle passes every brand once.
//   - Best sellers take more facings: they appear in more of a section's bays.
//   - Ends of runs are end caps: the section's promotion, or a cross-merchandised partner
//     (crisps by the cola, cola by the crisps).
//   - The till counter takes the classic impulse buy.
public static class Planogram
{
    public enum Board { Stoop = 0, Waist = 1, Eye = 2 }

    // A bay's layout: the product on each board of its front (-Z) and back (+Z) faces.
    public readonly struct Bay
    {
        public readonly string[] Front, Back;   // indexed by Board

        public Bay(string frontEye, string frontWaist, string frontStoop,
                   string backEye, string backWaist, string backStoop)
        {
            Front = new[] { frontStoop, frontWaist, frontEye };
            Back = new[] { backStoop, backWaist, backEye };
        }

        public string At(bool back, Board board) => (back ? Back : Front)[(int)board];
    }

    // Two layouts per section, alternated across its bays so neighbouring bays don't match.
    // Listed eye, waist, stoop for the front, then the same for the back.
    static readonly Dictionary<ItemType, Bay[]> bays = new Dictionary<ItemType, Bay[]>
    {
        [ItemType.Produce] = new[]
        {
            // Produce is blocked by colour and kind rather than brand: greens and reds high,
            // bananas — the single best seller — at hand height, bagged fruit and daikon low.
            new Bay("produce_saladmix", "produce_bananas", "produce_apples",
                    "produce_tomatoes", "produce_mikan", "produce_daikon"),
            new Bay("produce_tomatoes", "produce_bananas", "produce_daikon",
                    "produce_saladmix", "produce_mikan", "produce_apples"),
        },
        [ItemType.Bakery] = new[]
        {
            // Kamado's three lines stacked on one face, Beurre Bros on the other.
            new Bay("bakery_melonpan", "bakery_anpan", "bakery_shokupan",
                    "bakery_croissants", "bakery_bagels", "bakery_sourdough"),
            new Bay("bakery_melonpan", "bakery_shokupan", "bakery_anpan",
                    "bakery_croissants", "bakery_bagels", "bakery_sourdough"),
        },
        [ItemType.Dairy] = new[]
        {
            // Milk is the destination item: low, heavy, and in both layouts.
            new Bay("dairy_yoghurt", "dairy_milk_skim", "dairy_milk_whole",
                    "dairy_butter", "dairy_eggs", "dairy_creamcheese"),
            new Bay("dairy_creamcheese", "dairy_eggs", "dairy_milk_whole",
                    "dairy_yoghurt", "dairy_butter", "dairy_milk_whole"),
        },
        [ItemType.Frozen] = new[]
        {
            // Ice cream is the impulse line at eye level; Freezy's bags stacked together.
            new Bay("frozen_icecream", "frozen_peas", "frozen_fries",
                    "frozen_gyoza", "frozen_prawns", "frozen_pizza"),
            new Bay("frozen_gyoza", "frozen_pizza", "frozen_prawns",
                    "frozen_icecream", "frozen_peas", "frozen_fries"),
        },
        [ItemType.Cereal] = new[]
        {
            // Adult cereal and coffee high, the kids' boxes at their eye level, porridge low.
            new Bay("cereal_branflakies", "cereal_chocoloops", "cereal_oatsy",
                    "cereal_kafe", "cereal_honeynutz", "cereal_granola"),
            new Bay("cereal_granola", "cereal_honeynutz", "cereal_branflakies",
                    "cereal_kafe", "cereal_sencha", "cereal_chocoloops"),
        },
        [ItemType.Snacks] = new[]
        {
            // The brand leader buys eye level; both Krunchos flavours share a face.
            new Bay("snack_krunchos", "snack_krunchos_sco", "snack_popcorn",
                    "snack_nutzy", "snack_wasabiwave", "snack_pretzelpals"),
            new Bay("snack_nutzy", "snack_krunchos", "snack_krunchos_sco",
                    "snack_wasabiwave", "snack_popcorn", "snack_pretzelpals"),
        },
        [ItemType.Confectionery] = new[]
        {
            // Mints and the adult bars high; gummies and Pokki low, where children are.
            new Bay("sweet_mintz", "sweet_kittokatsu", "sweet_gummygang",
                    "sweet_chocobo", "sweet_pokki", "sweet_mochibites"),
            new Bay("sweet_chocobo", "sweet_gummygang", "sweet_pokki",
                    "sweet_mochibites", "sweet_kittokatsu", "sweet_gummygang"),
        },
        [ItemType.SoftDrinks] = new[]
        {
            // Pipisi and Pipisi Zero stacked; the litre of water always on the floor board.
            new Bay("drink_pipisi", "drink_pipisi_zero", "drink_aquapura",
                    "drink_kokakora", "drink_fanto", "drink_chakra_tea"),
            new Bay("drink_genki", "drink_chakra_tea", "drink_aquapura",
                    "drink_kokakora", "drink_pipisi_zero", "drink_pipisi"),
        },
        [ItemType.Canned] = new[]
        {
            // Glass jars high, where they won't be kicked; the heavy tins low.
            new Bay("canned_nori", "canned_tuna", "canned_beans",
                    "canned_sardines", "canned_sweetcorn", "canned_miso"),
            new Bay("canned_tuna", "canned_sardines", "canned_miso",
                    "canned_nori", "canned_sweetcorn", "canned_beans"),
        },
        [ItemType.Noodles] = new[]
        {
            // Cup noodles at eye level, the Ramyum 5-pack under them, 2 kg of rice on the floor.
            new Bay("noodle_ramyum_cup", "noodle_ramyum_5pk", "noodle_rice",
                    "noodle_udon", "noodle_soba", "noodle_pasta"),
            new Bay("noodle_udon", "noodle_pasta", "noodle_rice",
                    "noodle_ramyum_cup", "noodle_soba", "noodle_ramyum_5pk"),
        },
        [ItemType.PersonalCare] = new[]
        {
            new Bay("care_shampoo", "care_toothpaste", "care_tissues",
                    "care_sanitiser", "care_plasters", "care_soap"),
            new Bay("care_sanitiser", "care_toothpaste", "care_soap",
                    "care_shampoo", "care_plasters", "care_tissues"),
        },
        [ItemType.Household] = new[]
        {
            // Bulky paper and the laundry box low.
            new Bay("house_sparklespray", "house_dishsoap", "house_laundry",
                    "house_sponges", "house_binbags", "house_kitchenroll"),
            new Bay("house_dishsoap", "house_sponges", "house_kitchenroll",
                    "house_sparklespray", "house_binbags", "house_laundry"),
        },
        [ItemType.PetFood] = new[]
        {
            // Nyan Nyan on one face, Wan Wan on the other; the big bags low.
            new Bay("pet_cat_pouch", "pet_cat_tin", "pet_hamster_bed",
                    "pet_bird_seed", "pet_dog_can", "pet_dog_kibble"),
            new Bay("pet_bird_seed", "pet_dog_can", "pet_dog_kibble",
                    "pet_cat_pouch", "pet_cat_tin", "pet_hamster_bed"),
        },
    };

    // End caps: the section's promotion, and the cross-merchandised partners. A product from
    // another section keeps its own section — the facing takes the cola's, not the crisps'.
    static readonly Dictionary<ItemType, string[]> endCaps = new Dictionary<ItemType, string[]>
    {
        [ItemType.Produce] = new[] { "produce_mikan", "produce_bananas" },
        [ItemType.Bakery] = new[] { "bakery_croissants", "bakery_melonpan" },
        [ItemType.Dairy] = new[] { "dairy_yoghurt" },
        [ItemType.Frozen] = new[] { "frozen_icecream" },
        [ItemType.Cereal] = new[] { "cereal_kafe", "cereal_chocoloops" },
        [ItemType.Snacks] = new[] { "drink_pipisi", "snack_krunchos" },
        [ItemType.Confectionery] = new[] { "sweet_kittokatsu" },
        [ItemType.SoftDrinks] = new[] { "drink_genki", "snack_krunchos" },
        [ItemType.Canned] = new[] { "canned_tuna" },
        [ItemType.Noodles] = new[] { "noodle_ramyum_cup" },
        [ItemType.PersonalCare] = new[] { "care_sanitiser" },
        [ItemType.Household] = new[] { "house_kitchenroll" },
        [ItemType.PetFood] = new[] { "pet_cat_pouch" },
    };

    // The facing on the till counter itself.
    public const string CounterProduct = "sweet_mintz";

    // Children's lines — never at eye level, whatever a layout says.
    public static readonly string[] KidsProducts =
    {
        "cereal_chocoloops", "cereal_honeynutz", "sweet_gummygang", "sweet_pokki",
    };

    // Heavier than this and it doesn't go on the top board.
    public const float HeavyKg = 0.8f;

    public static IReadOnlyList<Bay> LayoutsFor(ItemType section) =>
        bays.TryGetValue(section, out Bay[] list) ? list : System.Array.Empty<Bay>();

    public static IReadOnlyList<string> EndCapsFor(ItemType section) =>
        endCaps.TryGetValue(section, out string[] list) ? list : System.Array.Empty<string>();

    // Which board a slot is on, from its height above the floor. Two-board "short" bays
    // (0.24 and 0.75) come out as stoop and waist.
    public static Board BoardAt(float heightAboveFloor)
    {
        if (heightAboveFloor >= 1.2f) return Board.Eye;
        if (heightAboveFloor >= 0.6f) return Board.Waist;
        return Board.Stoop;
    }

    // The product for one board of one bay. `seed` is the bay's own (from its position), so a
    // section's bays alternate layouts in a fixed pattern; `endCap` bays carry the promotions.
    public static string ProductFor(ItemType section, int seed, bool back, Board board, bool endCap)
    {
        if (endCap)
        {
            IReadOnlyList<string> caps = EndCapsFor(section);
            if (caps.Count > 0) return caps[Mod(seed + (int)board + (back ? 1 : 0), caps.Count)];
        }

        IReadOnlyList<Bay> layouts = LayoutsFor(section);
        if (layouts.Count > 0) return layouts[Mod(seed, layouts.Count)].At(back, board);

        ProductDef fallback = ProductCatalog.AtIndex(section, seed + (int)board);
        return fallback != null ? fallback.Id : string.Empty;
    }

    // How many boards a product holds across a section's layouts — the share of facings it
    // gets, which is also how often shoppers come in for it.
    public static int Share(string productId)
    {
        ProductDef p = ProductCatalog.Get(productId);
        if (p == null) return 0;
        int n = 0;
        foreach (Bay bay in LayoutsFor(p.Category))
            for (int b = 0; b < 3; b++)
            {
                if (bay.Front[b] == productId) n++;
                if (bay.Back[b] == productId) n++;
            }
        return n;
    }

    // The bays at the ends of runs. The maze's end pieces are the "E" pillars.
    public static bool IsEndCap(Transform bay) => bay != null && bay.name.Contains("pillar_E");

    static int Mod(int a, int n) => ((a % n) + n) % n;

    // ------------------------------------------------------------------ the live shop

    // Every facing that stocks each product, filled in as StoreLayout stocks the shop.
    static readonly Dictionary<string, List<ShelfSlot>> facings = new Dictionary<string, List<ShelfSlot>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetIndex() => facings.Clear();

    public static void Clear() => facings.Clear();

    public static void Register(ShelfSlot slot)
    {
        if (slot == null || string.IsNullOrEmpty(slot.productId)) return;
        if (!facings.TryGetValue(slot.productId, out List<ShelfSlot> list))
        {
            list = new List<ShelfSlot>();
            facings[slot.productId] = list;
        }
        if (!list.Contains(slot)) list.Add(slot);
    }

    public static bool HasStock => facings.Count > 0;

    public static IReadOnlyList<ShelfSlot> FacingsOf(string productId) =>
        productId != null && facings.TryGetValue(productId, out List<ShelfSlot> list)
            ? (IReadOnlyList<ShelfSlot>)list
            : System.Array.Empty<ShelfSlot>();

    // The closest slot planogrammed for a product: one with the product on it if any has,
    // otherwise the closest empty one. `except` skips the facing a shopper just found empty.
    public static ShelfSlot Nearest(string productId, Vector3 from, bool mustBeFilled, ShelfSlot except = null)
    {
        ShelfSlot best = null;
        float bestSqr = float.MaxValue;
        bool bestFilled = false;
        foreach (ShelfSlot slot in FacingsOf(productId))
        {
            if (slot == null || !slot.isActiveAndEnabled || slot == except) continue;
            if (except != null && except.owner != null && slot.owner == except.owner) continue;
            bool filled = slot.isFilled;
            if (mustBeFilled && !filled) continue;
            float sqr = (slot.transform.position - from).sqrMagnitude;
            // A filled facing beats an empty one however far it is.
            if (best == null || (filled && !bestFilled) || (filled == bestFilled && sqr < bestSqr))
            {
                best = slot;
                bestSqr = sqr;
                bestFilled = filled;
            }
        }
        return best;
    }
}
