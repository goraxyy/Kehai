using System.Collections.Generic;
using UnityEngine;

// The store's product range, in code rather than in ScriptableObjects, for two reasons:
// the repository only carries scripts, so a catalogue kept in assets could never be shared
// or reviewed; and every entry here is data a modeller, a material author and the customer
// dialogue all have to agree on, which is easier to keep honest in one readable table.
//
// Two levels, exactly like a real store:
//   ItemType  - the section. A shelf slot accepts anything of its section.
//   ProductDef - the SKU. What is actually stacked on that facing, and what a customer
//                asks for by name.
//
// See STORE_CATALOG.md for the whole range laid out with its aisles.

// The silhouette of the package. A modeller needs nothing else to block one out.
public enum PackShape
{
    Box,      // rectangular carton, printed on all faces (cereal, tea, crackers)
    Carton,   // gable-top or brick carton (milk, juice)
    Bottle,   // necked, round or oval footprint
    Can,      // cylindrical, seamed ends
    Jar,      // short, wide, screw lid
    Bag,      // pillow bag, sealed top and bottom, puffed
    Pouch,    // flat stand-up pouch with a gusset
    Tub,      // round or oval with a lid, wider than tall
    Tray,     // shallow, film-sealed
    Wrapper,  // flow-wrapped bar or roll
    Net,      // mesh bag of loose produce
    Loose     // no package at all
}

// Drives the URP material as much as it labels the package: a PET bottle is smooth and
// translucent, a cardboard box is matte, a can is the only metallic one in the list.
public enum PackMaterial
{
    Cardboard,    // corrugated, matte, visible flute
    Paperboard,   // folding carton, matte with a slight coat
    Pet,          // clear drinks plastic, very smooth
    Hdpe,         // opaque bottle plastic, smooth but not glossy
    Glass,        // smooth, transparent, dark tint on some
    Aluminium,    // drinks can - metallic
    Steel,        // food tin - metallic, duller than aluminium
    PlasticFilm,  // crinkled bag or wrapper, glossy with sharp highlights
    FoilLaminate, // metallised film - between film and metal
    WaxedCarton,  // milk carton, matte with a waxy sheen
    Mesh          // produce net
}

// One SKU. Sizes are metres, in the local axes the item is modelled in:
// x = width across the shelf, y = height, z = depth into the shelf.
//
// The shelf slot's trigger is 0.26 x 0.44 x 0.26, so nothing here is wider or deeper than
// 0.25 or taller than 0.42 - anything bigger pokes through a neighbouring facing.
// Where a product's boxes are kept out back: on the storage room's racks, or in the cold room.
public enum StorageKind { Ambient, Cold }

public class ProductDef
{
    public readonly string Id;          // stable key, never shown to the player
    public readonly string Name;        // what a customer says out loud
    public readonly string Brand;
    public readonly ItemType Category;
    public readonly PackShape Shape;
    public readonly PackMaterial Material;
    public readonly Vector3 Size;       // metres
    public readonly float Mass;         // kilograms, for the Rigidbody
    public readonly float Price;        // shelf price
    public readonly Color Primary;      // dominant packaging colour
    public readonly Color Accent;       // logo / band colour
    public readonly float Smoothness;   // URP smoothness
    public readonly float Metallic;     // URP metallic
    public readonly StorageKind Storage;

    // Dairy and frozen food are kept cold, and so are the few others that would spoil on a
    // rack: fresh salad, fresh udon, and the drinks sold chilled.
    static readonly HashSet<string> AlsoCold = new HashSet<string>
    {
        "produce_saladmix", "noodle_udon", "drink_chakra_tea", "drink_genki",
    };

    public ProductDef(string id, string name, string brand, ItemType category,
                      PackShape shape, PackMaterial material, Vector3 size, float mass,
                      float price, string primaryHex, string accentHex,
                      float smoothness, float metallic)
    {
        Id = id;
        Name = name;
        Brand = brand;
        Category = category;
        Shape = shape;
        Material = material;
        Size = size;
        Mass = mass;
        Price = price;
        Primary = ParseHex(primaryHex, Color.grey);
        Accent = ParseHex(accentHex, Color.white);
        Smoothness = smoothness;
        Metallic = metallic;
        Storage = category == ItemType.Dairy || category == ItemType.Frozen || AlsoCold.Contains(id)
            ? StorageKind.Cold : StorageKind.Ambient;
    }

    static Color ParseHex(string hex, Color fallback)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color parsed) ? parsed : fallback;
    }
}

public static class ProductCatalog
{
    // Sections that hold stock. The rest of ItemType is tools, which never reach a shelf.
    public static readonly ItemType[] StockSections =
    {
        ItemType.Produce, ItemType.Bakery, ItemType.Dairy, ItemType.Frozen,
        ItemType.Cereal, ItemType.Snacks, ItemType.Confectionery, ItemType.SoftDrinks,
        ItemType.Canned, ItemType.Noodles, ItemType.Household, ItemType.PersonalCare,
        ItemType.PetFood
    };

    // The full range. Roughly six facings per section, which is what one two-sided bay
    // holds: three platforms, two faces, one SKU each.
    static readonly ProductDef[] all =
    {
        // ---- Produce -------------------------------------------------------
        P("produce_apples",    "a bag of Fuji apples",  "Orchard Row", ItemType.Produce, PackShape.Net,   PackMaterial.Mesh,        0.18f, 0.22f, 0.14f, 1.10f,  380f, "#C0392B", "#7CB342", 0.30f),
        P("produce_bananas",   "bananas",               "Orchard Row", ItemType.Produce, PackShape.Loose, PackMaterial.Mesh,        0.22f, 0.10f, 0.12f, 0.85f,  220f, "#F4D03F", "#6B8E23", 0.25f),
        P("produce_tomatoes",  "a tray of tomatoes",    "Orchard Row", ItemType.Produce, PackShape.Tray,  PackMaterial.PlasticFilm, 0.22f, 0.08f, 0.15f, 0.60f,  340f, "#E74C3C", "#FFFFFF", 0.55f),
        P("produce_daikon",    "a daikon radish",       "Orchard Row", ItemType.Produce, PackShape.Loose, PackMaterial.PlasticFilm, 0.09f, 0.40f, 0.09f, 0.90f,  180f, "#F7F3E8", "#7CB342", 0.40f),
        P("produce_saladmix",  "a bag of salad mix",    "Leafy Lane",  ItemType.Produce, PackShape.Bag,   PackMaterial.PlasticFilm, 0.22f, 0.26f, 0.08f, 0.20f,  290f, "#58D68D", "#1E8449", 0.60f),
        P("produce_mikan",     "a net of mikan",        "Orchard Row", ItemType.Produce, PackShape.Net,   PackMaterial.Mesh,        0.18f, 0.20f, 0.14f, 1.00f,  420f, "#E67E22", "#2E7D32", 0.30f),

        // ---- Bakery --------------------------------------------------------
        P("bakery_shokupan",   "a Shokupan white loaf", "Kamado",      ItemType.Bakery,  PackShape.Bag,   PackMaterial.PlasticFilm, 0.13f, 0.14f, 0.24f, 0.45f,  260f, "#FDF2D0", "#D35400", 0.62f),
        P("bakery_sourdough",  "a Rye Rider sourdough", "Rye Rider",   ItemType.Bakery,  PackShape.Bag,   PackMaterial.Paperboard,  0.12f, 0.13f, 0.25f, 0.55f,  480f, "#8D6E63", "#3E2723", 0.25f),
        P("bakery_melonpan",   "a melon pan",           "Kamado",      ItemType.Bakery,  PackShape.Wrapper, PackMaterial.PlasticFilm, 0.13f, 0.07f, 0.13f, 0.12f, 150f, "#D4E157", "#795548", 0.65f),
        P("bakery_anpan",      "an anpan",              "Kamado",      ItemType.Bakery,  PackShape.Wrapper, PackMaterial.PlasticFilm, 0.11f, 0.06f, 0.11f, 0.10f, 140f, "#C68642", "#4E342E", 0.65f),
        P("bakery_croissants", "a pack of croissants",  "Beurre Bros", ItemType.Bakery,  PackShape.Tray,  PackMaterial.PlasticFilm, 0.24f, 0.09f, 0.16f, 0.28f,  390f, "#E0A64B", "#8B1A1A", 0.58f),
        P("bakery_bagels",     "a pack of sesame bagels", "Beurre Bros", ItemType.Bakery, PackShape.Bag,  PackMaterial.PlasticFilm, 0.17f, 0.20f, 0.11f, 0.48f,  420f, "#C89F63", "#283593", 0.60f),

        // ---- Dairy ---------------------------------------------------------
        P("dairy_milk_whole",  "Moo-Moo whole milk",    "Moo-Moo",     ItemType.Dairy,   PackShape.Carton, PackMaterial.WaxedCarton, 0.07f, 0.21f, 0.07f, 1.04f, 240f, "#F5F5F5", "#1565C0", 0.35f),
        P("dairy_milk_skim",   "Moo-Moo skimmed milk",  "Moo-Moo",     ItemType.Dairy,   PackShape.Carton, PackMaterial.WaxedCarton, 0.07f, 0.21f, 0.07f, 1.04f, 240f, "#F5F5F5", "#4FC3F7", 0.35f),
        P("dairy_yoghurt",     "Yogo strawberry yoghurt", "Yogo",      ItemType.Dairy,   PackShape.Tub,   PackMaterial.Hdpe,        0.11f, 0.09f, 0.11f, 0.45f,  210f, "#F48FB1", "#AD1457", 0.48f),
        P("dairy_creamcheese", "Kumo cream cheese",     "Kumo",        ItemType.Dairy,   PackShape.Box,   PackMaterial.Paperboard,  0.10f, 0.05f, 0.07f, 0.20f,  320f, "#FFFDE7", "#0277BD", 0.30f),
        P("dairy_butter",      "Butterfly salted butter", "Butterfly", ItemType.Dairy,   PackShape.Box,   PackMaterial.FoilLaminate, 0.11f, 0.05f, 0.06f, 0.23f, 430f, "#FFE082", "#37474F", 0.52f),
        P("dairy_eggs",        "a box of eggs",         "Hinata Farm", ItemType.Dairy,   PackShape.Tray,  PackMaterial.Paperboard,  0.24f, 0.07f, 0.11f, 0.65f,  280f, "#E0E0E0", "#F9A825", 0.20f),

        // ---- Frozen --------------------------------------------------------
        P("frozen_peas",       "a bag of Freezy peas",  "Freezy",      ItemType.Frozen,  PackShape.Bag,   PackMaterial.PlasticFilm, 0.20f, 0.26f, 0.07f, 0.50f,  230f, "#43A047", "#FFFFFF", 0.58f),
        P("frozen_gyoza",      "Gyoza Gang gyoza",      "Gyoza Gang",  ItemType.Frozen,  PackShape.Bag,   PackMaterial.FoilLaminate, 0.21f, 0.24f, 0.07f, 0.55f,  390f, "#C62828", "#FFD54F", 0.50f),
        P("frozen_icecream",   "Ice Dream vanilla",     "Ice Dream",   ItemType.Frozen,  PackShape.Tub,   PackMaterial.Paperboard,  0.13f, 0.11f, 0.13f, 0.60f,  520f, "#FFF8E1", "#6D4C41", 0.30f),
        P("frozen_fries",      "a bag of frozen fries", "Freezy",      ItemType.Frozen,  PackShape.Bag,   PackMaterial.PlasticFilm, 0.22f, 0.28f, 0.08f, 0.80f,  310f, "#FBC02D", "#E53935", 0.58f),
        P("frozen_prawns",     "Kaiten prawns",         "Kaiten",      ItemType.Frozen,  PackShape.Bag,   PackMaterial.FoilLaminate, 0.19f, 0.23f, 0.06f, 0.35f,  680f, "#EF6C00", "#0D47A1", 0.50f),
        P("frozen_pizza",      "a Pizza Piccolo margherita", "Pizza Piccolo", ItemType.Frozen, PackShape.Box, PackMaterial.Paperboard, 0.25f, 0.04f, 0.25f, 0.42f, 450f, "#D32F2F", "#1B5E20", 0.28f),

        // ---- Cereal & Breakfast --------------------------------------------
        P("cereal_chocoloops", "Choco Loops",           "Choco Loops", ItemType.Cereal,  PackShape.Box,   PackMaterial.Paperboard,  0.20f, 0.32f, 0.07f, 0.38f,  460f, "#5D4037", "#FFC107", 0.26f),
        P("cereal_branflakies","Bran Flakies",          "Flakies",     ItemType.Cereal,  PackShape.Box,   PackMaterial.Paperboard,  0.20f, 0.32f, 0.07f, 0.40f,  420f, "#C0392B", "#F5DEB3", 0.26f),
        P("cereal_honeynutz",  "Honey Nutz clusters",   "Honey Nutz",  ItemType.Cereal,  PackShape.Box,   PackMaterial.Paperboard,  0.19f, 0.30f, 0.07f, 0.37f,  480f, "#F9A825", "#4E342E", 0.26f),
        P("cereal_oatsy",      "Oatsy instant porridge", "Oatsy",      ItemType.Cereal,  PackShape.Box,   PackMaterial.Paperboard,  0.16f, 0.24f, 0.07f, 0.42f,  350f, "#EFEBE9", "#00695C", 0.26f),
        P("cereal_granola",    "Morning Mochi granola", "Morning Mochi", ItemType.Cereal, PackShape.Pouch, PackMaterial.FoilLaminate, 0.17f, 0.27f, 0.08f, 0.50f, 540f, "#8D6E63", "#FF7043", 0.45f),
        P("cereal_kafe",       "Kafé instant coffee",   "Kafé",        ItemType.Cereal,  PackShape.Jar,   PackMaterial.Glass,       0.09f, 0.16f, 0.09f, 0.32f,  620f, "#3E2723", "#C62828", 0.85f),
        P("cereal_sencha",     "Sencha teabags",        "Chakra",      ItemType.Cereal,  PackShape.Box,   PackMaterial.Paperboard,  0.13f, 0.17f, 0.08f, 0.14f,  380f, "#2E7D32", "#F1F8E9", 0.26f),

        // ---- Snacks & Crisps -----------------------------------------------
        P("snack_krunchos",    "Krunchos salted",       "Krunchos",    ItemType.Snacks,  PackShape.Bag,   PackMaterial.FoilLaminate, 0.20f, 0.30f, 0.09f, 0.09f, 180f, "#1565C0", "#FDD835", 0.55f),
        P("snack_krunchos_sco","Krunchos sour cream & onion", "Krunchos", ItemType.Snacks, PackShape.Bag, PackMaterial.FoilLaminate, 0.20f, 0.30f, 0.09f, 0.09f, 180f, "#2E7D32", "#FDD835", 0.55f),
        P("snack_pretzelpals", "Pretzel Pals",          "Pretzel Pals", ItemType.Snacks, PackShape.Bag,   PackMaterial.PlasticFilm, 0.18f, 0.26f, 0.08f, 0.14f,  160f, "#6D4C41", "#FFF176", 0.58f),
        P("snack_wasabiwave",  "Wasabi Wave rice crackers", "Wasabi Wave", ItemType.Snacks, PackShape.Box, PackMaterial.Paperboard, 0.16f, 0.22f, 0.07f, 0.16f, 240f, "#7CB342", "#FFFFFF", 0.28f),
        P("snack_nutzy",       "Nutzy mixed nuts",      "Nutzy",       ItemType.Snacks,  PackShape.Pouch, PackMaterial.FoilLaminate, 0.15f, 0.20f, 0.07f, 0.20f,  520f, "#8D6E63", "#FFB300", 0.48f),
        P("snack_popcorn",     "Popcorn Panic butter",  "Popcorn Panic", ItemType.Snacks, PackShape.Bag,  PackMaterial.Paperboard,  0.14f, 0.24f, 0.07f, 0.11f,  190f, "#FFF9C4", "#E64A19", 0.30f),

        // ---- Confectionery -------------------------------------------------
        P("sweet_kittokatsu",  "a Kitto Katsu bar",     "Kitto Katsu", ItemType.Confectionery, PackShape.Wrapper, PackMaterial.FoilLaminate, 0.11f, 0.02f, 0.09f, 0.05f, 130f, "#D32F2F", "#FFFFFF", 0.55f),
        P("sweet_chocobo",     "a Chocobo milk bar",    "Chocobo",     ItemType.Confectionery, PackShape.Wrapper, PackMaterial.FoilLaminate, 0.15f, 0.02f, 0.06f, 0.10f, 160f, "#4E342E", "#FFD54F", 0.55f),
        P("sweet_gummygang",   "Gummy Gang bears",      "Gummy Gang",  ItemType.Confectionery, PackShape.Bag,  PackMaterial.PlasticFilm, 0.12f, 0.17f, 0.05f, 0.10f, 140f, "#8E24AA", "#FFEB3B", 0.60f),
        P("sweet_pokki",       "Pokki sticks",          "Pokki",       ItemType.Confectionery, PackShape.Box,  PackMaterial.Paperboard, 0.06f, 0.16f, 0.04f, 0.07f, 150f, "#E91E63", "#FFF8E1", 0.28f),
        P("sweet_mochibites",  "Mochi Bites",           "Mochi Bites", ItemType.Confectionery, PackShape.Tray, PackMaterial.PlasticFilm, 0.13f, 0.05f, 0.10f, 0.14f, 280f, "#F8BBD0", "#4A148C", 0.60f),
        P("sweet_mintz",       "a tin of Mintz",        "Mintz",       ItemType.Confectionery, PackShape.Box,  PackMaterial.Steel,      0.06f, 0.02f, 0.04f, 0.05f, 120f, "#26A69A", "#FFFFFF", 0.70f, 0.85f),

        // ---- Soft drinks ---------------------------------------------------
        P("drink_pipisi",      "Pipisi",                "Pipisi",      ItemType.SoftDrinks, PackShape.Bottle, PackMaterial.Pet,       0.07f, 0.23f, 0.07f, 0.53f, 160f, "#1A237E", "#D32F2F", 0.92f),
        P("drink_pipisi_zero", "Pipisi Zero",           "Pipisi",      ItemType.SoftDrinks, PackShape.Bottle, PackMaterial.Pet,       0.07f, 0.23f, 0.07f, 0.53f, 160f, "#212121", "#D32F2F", 0.92f),
        P("drink_kokakora",    "Koka-Kora",             "Koka-Kora",   ItemType.SoftDrinks, PackShape.Can,    PackMaterial.Aluminium, 0.07f, 0.12f, 0.07f, 0.34f, 130f, "#B71C1C", "#FFFFFF", 0.78f, 0.95f),
        P("drink_fanto",       "Fanto orange",          "Fanto",       ItemType.SoftDrinks, PackShape.Bottle, PackMaterial.Pet,       0.07f, 0.23f, 0.07f, 0.53f, 160f, "#EF6C00", "#FFFFFF", 0.92f),
        P("drink_aquapura",    "Aqua Pura water",       "Aqua Pura",   ItemType.SoftDrinks, PackShape.Bottle, PackMaterial.Pet,       0.08f, 0.28f, 0.08f, 1.02f, 110f, "#B3E5FC", "#01579B", 0.94f),
        P("drink_genki",       "a Genki energy drink",  "Genki",       ItemType.SoftDrinks, PackShape.Can,    PackMaterial.Aluminium, 0.05f, 0.15f, 0.05f, 0.26f, 210f, "#FDD835", "#212121", 0.78f, 0.95f),
        P("drink_chakra_tea",  "Chakra green tea",      "Chakra",      ItemType.SoftDrinks, PackShape.Bottle, PackMaterial.Pet,       0.07f, 0.25f, 0.07f, 0.62f, 150f, "#2E7D32", "#FFFFFF", 0.92f),

        // ---- Canned & jars -------------------------------------------------
        P("canned_tuna",       "a tin of Tunatastic",   "Tunatastic",  ItemType.Canned,  PackShape.Can,   PackMaterial.Steel,       0.08f, 0.04f, 0.08f, 0.18f,  240f, "#0277BD", "#FFF176", 0.62f, 0.90f),
        P("canned_beans",      "Bean Machine baked beans", "Bean Machine", ItemType.Canned, PackShape.Can, PackMaterial.Steel,      0.08f, 0.11f, 0.08f, 0.42f,  190f, "#2E7D32", "#FF6F00", 0.62f, 0.90f),
        P("canned_sweetcorn",  "Corn Star sweetcorn",   "Corn Star",   ItemType.Canned,  PackShape.Can,   PackMaterial.Steel,       0.07f, 0.09f, 0.07f, 0.34f,  170f, "#FBC02D", "#1B5E20", 0.62f, 0.90f),
        P("canned_sardines",   "a tin of Sardino",      "Sardino",     ItemType.Canned,  PackShape.Can,   PackMaterial.Steel,       0.11f, 0.03f, 0.07f, 0.13f,  260f, "#00838F", "#FFCC80", 0.62f, 0.90f),
        P("canned_nori",       "a jar of nori paste",   "Umi",         ItemType.Canned,  PackShape.Jar,   PackMaterial.Glass,       0.06f, 0.11f, 0.06f, 0.24f,  310f, "#1B2A22", "#C62828", 0.88f),
        P("canned_miso",       "Miso Master miso",      "Miso Master", ItemType.Canned,  PackShape.Tub,   PackMaterial.Hdpe,        0.12f, 0.10f, 0.12f, 0.75f,  420f, "#8D6E63", "#D84315", 0.46f),

        // ---- Noodles, pasta & rice -----------------------------------------
        P("noodle_ramyum_cup", "a Ramyum cup noodle",   "Ramyum",      ItemType.Noodles, PackShape.Tub,   PackMaterial.Paperboard,  0.11f, 0.12f, 0.11f, 0.10f,  190f, "#D50000", "#FFE082", 0.32f),
        P("noodle_ramyum_5pk", "a Ramyum spicy 5-pack", "Ramyum",      ItemType.Noodles, PackShape.Bag,   PackMaterial.PlasticFilm, 0.22f, 0.18f, 0.12f, 0.55f,  520f, "#BF360C", "#FFECB3", 0.58f),
        P("noodle_soba",       "Soba Sensei dried soba", "Soba Sensei", ItemType.Noodles, PackShape.Bag,  PackMaterial.PlasticFilm, 0.11f, 0.26f, 0.05f, 0.32f,  340f, "#4E342E", "#EFEBE9", 0.55f),
        P("noodle_pasta",      "Pasta Basta spaghetti", "Pasta Basta", ItemType.Noodles, PackShape.Bag,   PackMaterial.PlasticFilm, 0.09f, 0.28f, 0.05f, 0.52f,  280f, "#1565C0", "#FFC107", 0.55f),
        P("noodle_rice",       "a bag of Kome King rice", "Kome King", ItemType.Noodles, PackShape.Bag,   PackMaterial.PlasticFilm, 0.20f, 0.30f, 0.11f, 2.10f,  980f, "#F5F5F5", "#C62828", 0.50f),
        P("noodle_udon",       "Udon Uno fresh udon",   "Udon Uno",    ItemType.Noodles, PackShape.Pouch, PackMaterial.PlasticFilm, 0.16f, 0.13f, 0.06f, 0.24f,  160f, "#FFF8E1", "#00695C", 0.60f),

        // ---- Household & cleaning ------------------------------------------
        P("house_sparklespray","Sparkle Spray cleaner", "Sparkle",     ItemType.Household, PackShape.Bottle, PackMaterial.Hdpe,      0.10f, 0.27f, 0.07f, 0.78f,  380f, "#00ACC1", "#FFEB3B", 0.66f),
        P("house_dishsoap",    "Bubbles dish soap",     "Bubbles",     ItemType.Household, PackShape.Bottle, PackMaterial.Hdpe,      0.07f, 0.22f, 0.05f, 0.52f,  240f, "#43A047", "#FFFFFF", 0.66f),
        P("house_laundry",     "Whitewash laundry powder", "Whitewash", ItemType.Household, PackShape.Box,  PackMaterial.Paperboard, 0.19f, 0.28f, 0.10f, 1.30f,  760f, "#1E88E5", "#FFFFFF", 0.26f),
        P("house_kitchenroll", "kitchen roll",          "Softly",      ItemType.Household, PackShape.Bag,  PackMaterial.PlasticFilm, 0.24f, 0.25f, 0.12f, 0.36f,  320f, "#FFFFFF", "#29B6F6", 0.58f),
        P("house_binbags",     "a roll of bin bags",    "Sakku",       ItemType.Household, PackShape.Box,  PackMaterial.Paperboard,  0.14f, 0.09f, 0.08f, 0.30f,  260f, "#37474F", "#8BC34A", 0.28f),
        P("house_sponges",     "a pack of sponges",     "Sponge Squad", ItemType.Household, PackShape.Bag, PackMaterial.PlasticFilm, 0.16f, 0.12f, 0.09f, 0.08f,  180f, "#FDD835", "#4CAF50", 0.58f),

        // ---- Personal care ---------------------------------------------------
        P("care_toothpaste",   "Freshbreath toothpaste", "Freshbreath", ItemType.PersonalCare, PackShape.Box,   PackMaterial.Paperboard,  0.05f, 0.19f, 0.04f, 0.14f, 290f, "#039BE5", "#FFFFFF", 0.28f),
        P("care_shampoo",      "Silkstrand shampoo",     "Silkstrand",  ItemType.PersonalCare, PackShape.Bottle, PackMaterial.Hdpe,       0.09f, 0.23f, 0.06f, 0.55f, 640f, "#7E57C2", "#F3E5F5", 0.68f),
        P("care_soap",         "a bar of Soapy Sudz",    "Soapy Sudz",  ItemType.PersonalCare, PackShape.Wrapper, PackMaterial.Paperboard, 0.09f, 0.03f, 0.06f, 0.11f, 130f, "#FFF3E0", "#EC407A", 0.32f),
        P("care_tissues",      "pocket tissues",         "Tissue Tower", ItemType.PersonalCare, PackShape.Bag,  PackMaterial.PlasticFilm, 0.11f, 0.06f, 0.05f, 0.06f, 110f, "#FFFFFF", "#42A5F5", 0.60f),
        P("care_sanitiser",    "Handy sanitiser gel",    "Handy",       ItemType.PersonalCare, PackShape.Bottle, PackMaterial.Pet,        0.05f, 0.14f, 0.04f, 0.14f, 320f, "#E0F7FA", "#00838F", 0.90f),
        P("care_plasters",     "a box of plasters",      "Plaster Patrol", ItemType.PersonalCare, PackShape.Box, PackMaterial.Paperboard, 0.09f, 0.06f, 0.03f, 0.05f, 250f, "#FFCC80", "#C62828", 0.28f),

        // ---- Pet food ------------------------------------------------------
        P("pet_cat_pouch",     "a Nyan Nyan cat food pouch", "Nyan Nyan", ItemType.PetFood, PackShape.Pouch, PackMaterial.FoilLaminate, 0.12f, 0.14f, 0.03f, 0.09f, 120f, "#EC407A", "#FFF176", 0.50f),
        P("pet_cat_tin",       "a Nyan Nyan tuna tin",   "Nyan Nyan",   ItemType.PetFood, PackShape.Can,   PackMaterial.Steel,       0.08f, 0.04f, 0.08f, 0.17f,  150f, "#AD1457", "#FFFFFF", 0.62f, 0.90f),
        P("pet_dog_can",       "a Wan Wan dog chunks tin", "Wan Wan",   ItemType.PetFood, PackShape.Can,   PackMaterial.Steel,       0.09f, 0.11f, 0.09f, 0.44f,  230f, "#1565C0", "#FF8F00", 0.62f, 0.90f),
        P("pet_dog_kibble",    "a bag of Wan Wan kibble", "Wan Wan",    ItemType.PetFood, PackShape.Bag,   PackMaterial.PlasticFilm, 0.21f, 0.30f, 0.11f, 1.80f,  890f, "#0D47A1", "#FDD835", 0.52f),
        P("pet_bird_seed",     "Birdy seed mix",         "Birdy",       ItemType.PetFood, PackShape.Box,   PackMaterial.Paperboard,  0.12f, 0.20f, 0.07f, 0.55f,  340f, "#8BC34A", "#FFECB3", 0.28f),
        P("pet_hamster_bed",   "Hamu hamster bedding",   "Hamu",        ItemType.PetFood, PackShape.Bag,   PackMaterial.PlasticFilm, 0.20f, 0.24f, 0.12f, 0.40f,  420f, "#FFB74D", "#5D4037", 0.52f)
    };

    // Shorthand so the table above reads as a table. Metallic defaults to 0 - only cans
    // and the mint tin pass one.
    static ProductDef P(string id, string name, string brand, ItemType category,
                        PackShape shape, PackMaterial material,
                        float sizeX, float sizeY, float sizeZ, float mass, float price,
                        string primaryHex, string accentHex,
                        float smoothness, float metallic = 0f)
    {
        return new ProductDef(id, name, brand, category, shape, material,
                              new Vector3(sizeX, sizeY, sizeZ), mass, price,
                              primaryHex, accentHex, smoothness, metallic);
    }

    static Dictionary<string, ProductDef> byId;
    static Dictionary<ItemType, List<ProductDef>> bySection;

    public static IReadOnlyList<ProductDef> All => all;

    static void Build()
    {
        if (byId != null) return;

        byId = new Dictionary<string, ProductDef>(all.Length);
        bySection = new Dictionary<ItemType, List<ProductDef>>();

        foreach (ProductDef product in all)
        {
            byId[product.Id] = product;

            if (!bySection.TryGetValue(product.Category, out List<ProductDef> list))
            {
                list = new List<ProductDef>();
                bySection[product.Category] = list;
            }
            list.Add(product);
        }
    }

    public static ProductDef Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Build();
        return byId.TryGetValue(id, out ProductDef product) ? product : null;
    }

    // Every SKU shelved in one section, in catalogue order. Empty for the tool types.
    public static IReadOnlyList<ProductDef> InSection(ItemType section)
    {
        Build();
        return bySection.TryGetValue(section, out List<ProductDef> list)
            ? (IReadOnlyList<ProductDef>)list
            : System.Array.Empty<ProductDef>();
    }

    // The nth SKU of a section, wrapping round. Used by the layout pass to walk a
    // section's range across the platforms of a bay without ever falling off the end.
    public static ProductDef AtIndex(ItemType section, int index)
    {
        IReadOnlyList<ProductDef> list = InSection(section);
        if (list.Count == 0) return null;
        return list[((index % list.Count) + list.Count) % list.Count];
    }

    // What a customer calls the whole section when no particular SKU is meant.
    public static string SectionName(ItemType section)
    {
        switch (section)
        {
            case ItemType.Produce: return "fruit and veg";
            case ItemType.Bakery: return "bread";
            case ItemType.Dairy: return "milk";
            case ItemType.Frozen: return "frozen food";
            case ItemType.Cereal: return "cereal";
            case ItemType.Snacks: return "crisps";
            case ItemType.Confectionery: return "sweets";
            case ItemType.SoftDrinks: return "soft drinks";
            case ItemType.Canned: return "tinned food";
            case ItemType.Noodles: return "noodles";
            case ItemType.Household: return "cleaning stuff";
            case ItemType.PersonalCare: return "toiletries";
            case ItemType.PetFood: return "pet food";
            case ItemType.Mop: return "mop";
            case ItemType.Stock: return "stock crate";
            case ItemType.TrashBag: return "bin bag";
            case ItemType.Flashlight: return "torch";
            default: return "that thing";
        }
    }

    // The name to print on a shelf's sign or an interaction prompt: the SKU if the facing
    // is planogrammed for one, otherwise the section it belongs to.
    public static string Label(ItemType section, string productId)
    {
        ProductDef product = Get(productId);
        return product != null ? product.Name : SectionName(section);
    }

    public static bool IsStockSection(ItemType type)
    {
        switch (type)
        {
            case ItemType.Mop:
            case ItemType.Stock:
            case ItemType.TrashBag:
            case ItemType.Flashlight:
                return false;
            default:
                return true;
        }
    }
}
