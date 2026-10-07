using System.Collections.Generic;
using Kehai;
using TMPro;
using UnityEngine;

// Hanging signs over every part of the shop — a numbered board over each aisle, a named one
// over each department — so the maze can be read from inside it and "where's aisle 5?" has an
// answer you can see. Each is a four-sided box on two wires, readable from any direction, over
// the middle of its zone's bays.
//
// Built at load from StoreLayout.Zones, like the stock: nothing about them lives in the scene.
public static class AisleSigns
{
    public const string RootName = "AisleSigns";
    public const float BoardHeight = 3.05f;      // centre of the box: clear of the 2 m shelving
    public const float CeilingHeight = 7.6f;     // where the wires go up to (the ceiling lights)
    const float Width = 1.9f;
    const float Tall = 0.62f;

    static readonly Color Board = new Color(0.06f, 0.16f, 0.30f);
    static readonly Color Badge = new Color(1f, 0.80f, 0.18f);
    static readonly Color Ink = new Color(0.97f, 0.96f, 0.93f);
    static readonly Color Subtitle = new Color(0.72f, 0.80f, 0.92f);

    // Where each zone's sign hangs: over the middle of its bays.
    public static Dictionary<int, Vector3> Positions()
    {
        var sums = new Dictionary<int, Vector3>();
        var counts = new Dictionary<int, int>();
        foreach (ShelfUnit unit in Object.FindObjectsByType<ShelfUnit>(FindObjectsInactive.Exclude))
        {
            int z = IndexOf(StoreLayout.ZoneAt(unit.transform.position));
            sums[z] = (sums.TryGetValue(z, out Vector3 s) ? s : Vector3.zero) + unit.transform.position;
            counts[z] = (counts.TryGetValue(z, out int n) ? n : 0) + 1;
        }

        var at = new Dictionary<int, Vector3>();
        foreach (var pair in sums)
        {
            Vector3 middle = pair.Value / counts[pair.Key];
            at[pair.Key] = new Vector3(middle.x, BoardHeight, middle.z);
        }
        return at;
    }

    public static int Build()
    {
        var old = GameObject.Find(RootName);
        if (old != null) Object.Destroy(old);

        Dictionary<int, Vector3> at = Positions();
        if (at.Count == 0) return 0;

        var root = new GameObject(RootName);
        Material board = Flat(Board, 0.35f);
        Material badge = Flat(Badge, 0.4f);
        Material wire = Flat(new Color(0.12f, 0.12f, 0.13f), 0.5f);
        TMP_FontAsset font = GameFonts.Heading;

        foreach (var pair in at)
            Sign(root.transform, StoreLayout.Zones[pair.Key], pair.Value, board, badge, wire, font);
        return at.Count;
    }

    // The words on a sign, for the sign and for anything that wants to say it the same way.
    public static string Title(StoreLayout.Zone zone) => zone.IsAisle ? $"AISLE {zone.Aisle}" : zone.Name.ToUpperInvariant();
    public static string Caption(StoreLayout.Zone zone) => zone.IsAisle ? zone.Name : "";

    static void Sign(Transform root, StoreLayout.Zone zone, Vector3 centre, Material board, Material badge,
                     Material wire, TMP_FontAsset font)
    {
        var sign = new GameObject("Sign_" + (zone.IsAisle ? "Aisle" + zone.Aisle : zone.Section.ToString()));
        sign.transform.SetParent(root, false);
        sign.transform.position = centre;

        Box(sign.transform, "Board", Vector3.zero, new Vector3(Width, Tall, Width), board);

        // Two wires up to the ceiling.
        float wireLength = Mathf.Max(0.1f, CeilingHeight - (BoardHeight + Tall * 0.5f));
        foreach (float x in new[] { -Width * 0.35f, Width * 0.35f })
            Box(sign.transform, "Wire", new Vector3(x, Tall * 0.5f + wireLength * 0.5f, 0f),
                new Vector3(0.015f, wireLength, 0.015f), wire);

        // The same face on all four sides.
        for (int side = 0; side < 4; side++)
        {
            var face = new GameObject("Face" + side);
            face.transform.SetParent(sign.transform, false);
            face.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            // TextMeshPro reads from -Z, so the face sits just off the box's -Z side.
            face.transform.localPosition = face.transform.localRotation * new Vector3(0f, 0f, -Width * 0.5f - 0.004f);

            if (zone.IsAisle)
            {
                Box(face.transform, "Badge", new Vector3(-Width * 0.5f + 0.36f, 0f, 0.002f),
                    new Vector3(0.52f, 0.5f, 0.004f), badge);
                Text(face.transform, "Number", zone.Aisle.ToString(), new Vector3(-Width * 0.5f + 0.36f, 0f, -0.002f),
                     new Vector2(0.5f, 0.46f), Board, font, TextAlignmentOptions.Center, bold: true);
                Text(face.transform, "Name", zone.Name, new Vector3(0.3f, 0.09f, -0.002f),
                     new Vector2(Width - 0.82f, 0.26f), Ink, font, TextAlignmentOptions.Center, bold: true);
                Text(face.transform, "Japanese", zone.Japanese, new Vector3(0.3f, -0.17f, -0.002f),
                     new Vector2(Width - 0.82f, 0.17f), Subtitle, font, TextAlignmentOptions.Center);
            }
            else
            {
                Text(face.transform, "Name", zone.Name.ToUpperInvariant(), new Vector3(0f, 0.09f, -0.002f),
                     new Vector2(Width - 0.2f, 0.28f), Ink, font, TextAlignmentOptions.Center, bold: true);
                Text(face.transform, "Japanese", zone.Japanese, new Vector3(0f, -0.17f, -0.002f),
                     new Vector2(Width - 0.2f, 0.17f), Badge, font, TextAlignmentOptions.Center);
            }
        }
    }

    static void Box(Transform parent, string name, Vector3 local, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());     // scenery, high up: nothing walks into it
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Text(Transform parent, string name, string text, Vector3 local, Vector2 size, Color color,
                     TMP_FontAsset font, TextAlignmentOptions align, bool bold = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = GameFonts.ForText(text, font);
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.1f;
        tmp.fontSizeMax = 4f;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = size;
    }

    static Material Flat(Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { color = color };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    static int IndexOf(StoreLayout.Zone zone)
    {
        for (int i = 0; i < StoreLayout.Zones.Length; i++)
            if (StoreLayout.Zones[i].Section == zone.Section) return i;
        return 0;
    }
}
