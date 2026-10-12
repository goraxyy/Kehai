using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

// Kehai/Products/4: a price label for every product, for the shelves' price strips (PriceTags).
// Each is a metre of strip: the label on its left half (a bar of the product's colour, its
// name and section, and its price), plain strip on the right, so a strip shows a label every
// metre and a half-metre strip shows one whole. Rendered from text by a camera in the editor,
// saved as a texture, with a material per strip length in Resources/PriceTags. Run it again
// after prices change; the store picks the new labels up as it stocks the shelves.
public static class PriceTagMaker
{
    const string TexturesFolder = "Assets/!_Project/_Game/Items/Products/PriceTags";
    const string MaterialsFolder = "Assets/!_Project/_Game/Items/Products/Resources/" + PriceTags.Folder;
    const int Width = 1024, Height = 160;          // a metre of strip, 0.156 m tall
    const float Tall = Height / (float)Width;       // the strip's height, in metres of width
    const int Layer = 31;                           // nothing else is drawn by the label camera

    static readonly Color Strip = new Color(0.93f, 0.93f, 0.92f);
    static readonly Color Card = new Color(0.995f, 0.995f, 0.99f);
    static readonly Color Ink = new Color(0.1f, 0.1f, 0.11f);
    static readonly Color Faint = new Color(0.45f, 0.45f, 0.47f);
    static readonly Color PriceInk = new Color(0.72f, 0.06f, 0.05f);

    [MenuItem("Kehai/Products/4. Make Price Tags")]
    static void MakeMenu() => Debug.Log(Make());

    public static string Make()
    {
        Directory.CreateDirectory(TexturesFolder);
        Directory.CreateDirectory(MaterialsFolder);
        AssetDatabase.Refresh();

        var stage = new GameObject("price tag stage") { hideFlags = HideFlags.HideAndDontSave };
        stage.transform.position = new Vector3(0f, -3000f, 0f);
        Camera cam = new GameObject("price tag camera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
        cam.transform.SetParent(stage.transform, false);
        cam.transform.localPosition = new Vector3(0.5f, Tall * 0.5f, -1f);
        cam.orthographic = true;
        cam.orthographicSize = Tall * 0.5f;
        cam.aspect = Width / (float)Height;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Strip;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 3f;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = target;

        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Mesh quad = QuadMesh();
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        bool yen = font != null && font.HasCharacter('¥');
        int made = 0;
        try
        {
            foreach (ProductDef p in ProductCatalog.All)
            {
                var label = new GameObject(p.Id) { hideFlags = HideFlags.HideAndDontSave };
                label.transform.SetParent(stage.transform, false);
                Block(label.transform, quad, unlit, Card, 0.015f, 0.485f, 0.012f, Tall - 0.012f, 0.01f);
                Block(label.transform, quad, unlit, p.Primary, 0.015f, 0.05f, 0.012f, Tall - 0.012f, 0.02f);
                Block(label.transform, quad, unlit, Faint, 0f, 1f, Tall - 0.004f, Tall, 0.02f);
                Text(label.transform, font, ProductBox.ShortName(p), Ink, FontStyles.Bold, 0.065f, 0.3f, 0.07f, Tall - 0.012f, TextAlignmentOptions.TopLeft);
                Text(label.transform, font, ProductCatalog.SectionName(p.Category).ToUpperInvariant(), Faint, FontStyles.Normal,
                     0.065f, 0.3f, 0.016f, 0.062f, TextAlignmentOptions.BottomLeft);
                Text(label.transform, font, yen ? $"¥{p.Price:0}" : $"{p.Price:0} yen", PriceInk, FontStyles.Bold,
                     0.3f, 0.475f, 0.02f, Tall - 0.02f, TextAlignmentOptions.Right);

                cam.Render();
                RenderTexture.active = target;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string png = $"{TexturesFolder}/T_Price_{p.Id}.png";
                File.WriteAllBytes(png, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(label);
                made++;
            }
        }
        finally
        {
            cam.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(stage);
            Object.DestroyImmediate(quad);
        }

        AssetDatabase.Refresh();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        foreach (ProductDef p in ProductCatalog.All)
        {
            string png = $"{TexturesFolder}/T_Price_{p.Id}.png";
            if (AssetImporter.GetAtPath(png) is TextureImporter importer)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(png);
            foreach ((float length, string suffix) in PriceTags.Lengths)
                Keep($"{MaterialsFolder}/{p.Id}_{suffix}.mat", lit, tex, length);
        }
        AssetDatabase.SaveAssets();
        PriceTags.Forget();
        return $"Price tags: {made} labels in {TexturesFolder}, {made * PriceTags.Lengths.Length} materials in {MaterialsFolder}. " +
               "The shelves take them as the stock is next built (Play, or the editor's stock preview).";
    }

    // A material for a strip of `length`: the label once a metre along it.
    static void Keep(string path, Shader lit, Texture2D tex, float length)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool fresh = m == null;
        if (fresh) m = new Material(lit);
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        var tiling = new Vector2(length, 1f);
        m.mainTextureScale = tiling;
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", tiling);
        m.color = Color.white;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        if (fresh) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
    }

    // A flat rectangle of colour from (x0, y0) to (x1, y1) on the strip, `z` in front of it
    // (the larger, the nearer the camera).
    static void Block(Transform parent, Mesh quad, Shader unlit, Color colour, float x0, float x1, float y0, float y1, float z)
    {
        var go = new GameObject("block") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, -z);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var m = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave, color = colour };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    static void Text(Transform parent, TMP_FontAsset font, string words, Color colour, FontStyles style,
                     float x0, float x1, float y0, float y1, TextAlignmentOptions align)
    {
        var go = new GameObject("text") { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, -0.05f);
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.rectTransform.sizeDelta = new Vector2(x1 - x0, y1 - y0);
        tmp.text = words;
        tmp.color = colour;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 2f;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.ForceMeshUpdate();
    }

    // A one-by-one quad facing the camera (-Z), centred on its origin.
    static Mesh QuadMesh()
    {
        var mesh = new Mesh { name = "price tag quad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        return mesh;
    }
}
