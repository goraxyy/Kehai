using System.Collections.Generic;
using UnityEngine;

// The price strips along the shelves' front edges (the shelf models' "Pricetag" pieces) show
// the price of what's on the board above them: a label a metre, with the product's name, its
// price and a bar of its colour. Kehai/Products/4 makes each product's label and a material
// for each length of strip the shelves have (4 m bays, 1 m pillars, 0.5 m sides) into
// Resources/PriceTags; StoreLayout puts them on the strips as it stocks the shop.
public static class PriceTags
{
    public const string Folder = "PriceTags";

    // The strip lengths the shelves have, and the suffix of each one's material.
    public static readonly (float length, string suffix)[] Lengths = { (4f, "x4"), (2f, "x2"), (1.5f, "x15"), (1f, "x1"), (0.5f, "x05") };

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => cache.Clear();

    // Looks the materials up again: they have just been made, or made again.
    public static void Forget() => cache.Clear();

    public static string NameFor(string productId, float length) => productId + "_" + SuffixFor(length);

    static string SuffixFor(float length)
    {
        string best = Lengths[0].suffix;
        float gap = float.MaxValue;
        foreach ((float l, string suffix) in Lengths)
            if (Mathf.Abs(l - length) < gap) { gap = Mathf.Abs(l - length); best = suffix; }
        return best;
    }

    // The material for a strip of `length` under `productId`, or null before the labels are made.
    public static Material MaterialFor(string productId, float length)
    {
        if (string.IsNullOrEmpty(productId)) return null;
        string name = NameFor(productId, length);
        if (!cache.TryGetValue(name, out Material m)) cache[name] = m = Resources.Load<Material>(Folder + "/" + name);
        return m;
    }
}
