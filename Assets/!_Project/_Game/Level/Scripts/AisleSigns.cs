using System.Collections.Generic;
using Kehai;
using TMPro;
using UnityEngine;

// A hanging sign over every aisle, so the maze can be read from inside it and "where's aisle
// 5?" has an answer you can see. Each is a black box on two wires, the same on all four sides:
// the aisle's number in a white square, its name, and the Japanese under it, in a rounded face.
// It hangs over the middle of its aisle's bays.
//
// Built at load from StoreLayout.Zones, like the stock. Kehai/Store/Stock the Maze bakes a copy
// into the scene so the editor shows them too; the game replaces it with its own at load (with
// the Japanese, which needs the computer's fonts).
public static class AisleSigns
{
    public const string RootName = "AisleSigns";
    public const float BoardHeight = 4.05f;      // centre of the box: well clear of the 2 m shelving
    public const float CeilingHeight = 8f;       // the roof, where the wires go up to
    const float Width = 1.9f;
    const float Tall = 0.62f;

    public static readonly Color BoardColour = new Color(0.02f, 0.02f, 0.025f);
    public static readonly Color BadgeColour = new Color(0.96f, 0.96f, 0.94f);
    public static readonly Color WireColour = new Color(0.05f, 0.05f, 0.05f);
    static readonly Color Ink = new Color(0.97f, 0.97f, 0.95f);
    static readonly Color Subtitle = new Color(0.70f, 0.70f, 0.72f);

    // Where each aisle's sign hangs: over the middle of its bays.
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

    public static int Build() =>
        Build(Flat(BoardColour, 0.2f), Flat(BadgeColour, 0.2f), Flat(WireColour, 0.3f), GameFonts.Rounded, japanese: true);

    public static int Build(Material board, Material badge, Material wire, TMP_FontAsset font, bool japanese)
    {
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            old.name += " (replaced)";
            if (Application.isPlaying) Object.Destroy(old);
            else Object.DestroyImmediate(old);
        }

        Dictionary<int, Vector3> at = Positions();
        if (at.Count == 0) return 0;

        var root = new GameObject(RootName);
        foreach (var pair in at)
            Sign(root.transform, StoreLayout.Zones[pair.Key], pair.Value, board, badge, wire, font, japanese);
        return at.Count;
    }

    // The words on a sign, for the sign and for anything that wants to say it the same way.
    public static string Title(StoreLayout.Zone zone) => $"AISLE {zone.Aisle}";

    static void Sign(Transform root, StoreLayout.Zone zone, Vector3 centre, Material board, Material badge,
                     Material wire, TMP_FontAsset font, bool japanese)
    {
        var sign = new GameObject("Sign_Aisle" + zone.Aisle);
        sign.transform.SetParent(root, false);
        sign.transform.position = centre;

        Box(sign.transform, "Board", Vector3.zero, new Vector3(Width, Tall, Width), board);

        // Two wires up to the roof.
        float wireLength = Mathf.Max(0.1f, CeilingHeight - (BoardHeight + Tall * 0.5f));
        foreach (float x in new[] { -Width * 0.35f, Width * 0.35f })
            Box(sign.transform, "Wire", new Vector3(x, Tall * 0.5f + wireLength * 0.5f, 0f),
                new Vector3(0.012f, wireLength, 0.012f), wire);

        // The same face on all four sides.
        for (int side = 0; side < 4; side++)
        {
            var face = new GameObject("Face" + side);
            face.transform.SetParent(sign.transform, false);
            face.transform.localRotation = Quaternion.Euler(0f, side * 90f, 0f);
            // TextMeshPro reads from -Z, so the face sits just off the box's -Z side.
            face.transform.localPosition = face.transform.localRotation * new Vector3(0f, 0f, -Width * 0.5f - 0.004f);

            Box(face.transform, "Badge", new Vector3(-Width * 0.5f + 0.36f, 0f, 0.002f),
                new Vector3(0.5f, 0.48f, 0.004f), badge);
            Text(face.transform, "Number", zone.Aisle.ToString(), new Vector3(-Width * 0.5f + 0.36f, -0.01f, -0.002f),
                 new Vector2(0.44f, 0.42f), BoardColour, font, bold: true);
            Text(face.transform, "Name", zone.Name, new Vector3(0.3f, japanese ? 0.09f : 0f, -0.002f),
                 new Vector2(Width - 0.84f, japanese ? 0.26f : 0.34f), Ink, font, bold: false);
            if (japanese)
                Text(face.transform, "Japanese", zone.Japanese, new Vector3(0.3f, -0.17f, -0.002f),
                     new Vector2(Width - 0.84f, 0.17f), Subtitle, font, bold: false);
        }
    }

    static void Box(Transform parent, string name, Vector3 local, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        // Scenery, high up: nothing walks into it.
        if (Application.isPlaying) Object.Destroy(go.GetComponent<Collider>());
        else Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = size;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void Text(Transform parent, string name, string text, Vector3 local, Vector2 size, Color color,
                     TMP_FontAsset font, bool bold)
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
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        tmp.rectTransform.sizeDelta = size;
    }

    public static Material Flat(Color color, float smoothness)
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
