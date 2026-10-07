using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Brings the 80 catalogue products in from the Blender pipeline (~/Desktop/KehaiRelated/Products,
// or KEHAI_PRODUCTS) and turns each one into a prefab the game can stock.
//
//   Products/Models/<id>.fbx         one low-poly mesh per product, materials remapped below
//   Products/Labels/<id>.png         its printed label
//   Products/Textures/               can tops, tin lids, the fruit nets
//   Products/Materials/              URP Lit: L_<id> per label, M_* shared (metal, PET, film...)
//   Products/Resources/Products/<id> a variant of Item_def wearing the model; ProductLook loads it
//
// Rerun it after regenerating the models; everything is overwritten in place, so references
// to the prefabs and materials survive.
public static class ProductImport
{
    public const string Root = "Assets/!_Project/_Game/Items/Products";
    const string ModelsFolder = Root + "/Models";
    const string LabelsFolder = Root + "/Labels";
    const string TexturesFolder = Root + "/Textures";
    const string MaterialsFolder = Root + "/Materials";
    public const string PrefabsFolder = Root + "/Resources/Products";
    const string ItemPrefabPath = "Assets/!_Project/_Game/Items/Prefabs/Item_def.prefab";
    const string ShelfModelPath = "Assets/!_Project/_Game/Level/Prefabs/Shelves/Models/ShelfOneside_3_grey.prefab";

    static readonly string[] SharedTextures = { "can_top", "tin_lid", "net_red", "net_orange" };

    static string SourceFolder
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("KEHAI_PRODUCTS");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                                "Desktop", "KehaiRelated", "Products");
        }
    }

    [MenuItem("Kehai/Products/1. Import Product Models")]
    public static void ImportMenu() => Debug.Log(ImportAll());

    [MenuItem("Kehai/Products/2. Lay Out the Showcase on Models_Island")]
    public static void ShowcaseMenu() => Debug.Log(BuildShowcase());

    public static string ImportAll()
    {
        var log = new StringBuilder("Product import:\n");
        string source = SourceFolder;
        if (!Directory.Exists(source)) return $"No product folder at {source} (set KEHAI_PRODUCTS).";

        foreach (string folder in new[] { ModelsFolder, LabelsFolder, TexturesFolder, MaterialsFolder, PrefabsFolder })
            EnsureFolder(folder);

        // 1. Copy the files in.
        int copied = 0;
        var missing = new List<string>();
        foreach (ProductDef p in ProductCatalog.All)
        {
            copied += Copy(Path.Combine(source, "fbx", p.Id + ".fbx"), $"{ModelsFolder}/{p.Id}.fbx", missing);
            copied += Copy(Path.Combine(source, "labels", p.Id + ".png"), $"{LabelsFolder}/{p.Id}.png", missing);
        }
        foreach (string t in SharedTextures)
            copied += Copy(Path.Combine(source, "textures", t + ".png"), $"{TexturesFolder}/{t}.png", missing);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        log.AppendLine($"  copied {copied} files" + (missing.Count > 0 ? $", missing: {string.Join(", ", missing)}" : ""));

        // 2. Texture settings.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (ProductDef p in ProductCatalog.All)
                ConfigureTexture($"{LabelsFolder}/{p.Id}.png", 1024, TextureWrapMode.Clamp, alpha: false);
            ConfigureTexture($"{TexturesFolder}/can_top.png", 512, TextureWrapMode.Clamp, alpha: false);
            ConfigureTexture($"{TexturesFolder}/tin_lid.png", 512, TextureWrapMode.Clamp, alpha: false);
            ConfigureTexture($"{TexturesFolder}/net_red.png", 256, TextureWrapMode.Repeat, alpha: true);
            ConfigureTexture($"{TexturesFolder}/net_orange.png", 256, TextureWrapMode.Repeat, alpha: true);
        }
        finally { AssetDatabase.StopAssetEditing(); }

        // 3. Materials.
        var materials = BuildMaterials();
        log.AppendLine($"  {materials.Count} materials");

        // 4. Models: no cameras or animation, readable (the shelf back-stock combines them),
        //    every Blender material remapped onto ours.
        int remapped = 0;
        var unmapped = new HashSet<string>();
        foreach (ProductDef p in ProductCatalog.All)
        {
            string path = $"{ModelsFolder}/{p.Id}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.addCollider = false;

            foreach (string name in SourceMaterialNames(path))
            {
                if (materials.TryGetValue(name, out Material m))
                {
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), m);
                    remapped++;
                }
                else unmapped.Add(name);
            }
            mi.SaveAndReimport();
        }
        log.AppendLine($"  {remapped} material slots remapped" +
                       (unmapped.Count > 0 ? $"; no material for: {string.Join(", ", unmapped)}" : ""));

        // 5. Prefabs.
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath);
        int made = 0;
        var sizeLog = new StringBuilder();
        foreach (ProductDef p in ProductCatalog.All)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsFolder}/{p.Id}.fbx");
            if (model == null || basePrefab == null) continue;
            var mf = model.GetComponentInChildren<MeshFilter>();
            var mr = model.GetComponentInChildren<MeshRenderer>();
            if (mf == null || mr == null) continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                instance.name = p.Id;
                instance.transform.localScale = Vector3.one;
                instance.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                instance.GetComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;

                Bounds b = mf.sharedMesh.bounds;
                var box = instance.GetComponent<BoxCollider>();
                if (box != null) { box.center = b.center; box.size = b.size; }

                var item = instance.GetComponent<Item>();
                item.type = p.Category;
                item.productId = p.Id;

                var rb = instance.GetComponent<Rigidbody>();
                if (rb != null) rb.mass = Mathf.Max(0.02f, p.Mass);

                PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabsFolder}/{p.Id}.prefab");
                made++;

                Vector3 d = b.size - p.Size;
                if (Mathf.Abs(d.x) > 0.015f || Mathf.Abs(d.y) > 0.015f || Mathf.Abs(d.z) > 0.015f)
                    sizeLog.AppendLine($"    {p.Id}: mesh {b.size:F3} vs catalogue {p.Size:F3}");
            }
            finally { Object.DestroyImmediate(instance); }
        }
        AssetDatabase.SaveAssets();
        log.AppendLine($"  {made} prefabs in {PrefabsFolder}");
        if (sizeLog.Length > 0) log.Append("  sizes off by more than 15 mm:\n" + sizeLog);
        return log.ToString();
    }

    // ------------------------------------------------------------------ materials

    enum Surface { Opaque, Transparent, Cutout }

    struct Shared
    {
        public string Name, Texture;
        public Color Linear;
        public float Smooth, Metal, Alpha;
        public Surface Surface;
        public bool TwoSided;
    }

    // The same values as build_products.py, in Blender's linear colour.
    static Shared S(string name, float r, float g, float b, float smooth, float metal = 0f,
                    Surface surface = Surface.Opaque, float alpha = 1f, string texture = null, bool twoSided = false)
    {
        return new Shared { Name = name, Linear = new Color(r, g, b), Smooth = smooth, Metal = metal,
                            Surface = surface, Alpha = alpha, Texture = texture, TwoSided = twoSided };
    }

    static readonly Shared[] SharedMaterials =
    {
        S("M_Aluminium", 0.80f, 0.81f, 0.83f, 0.72f, 1f),
        S("M_Steel", 0.62f, 0.63f, 0.64f, 0.60f, 0.9f),
        S("M_CanTop", 1f, 1f, 1f, 0.68f, 0.9f, texture: "can_top"),
        S("M_TinLid", 1f, 1f, 1f, 0.60f, 0.9f, texture: "tin_lid"),
        S("M_Foil", 0.82f, 0.82f, 0.84f, 0.65f, 1f),
        S("M_FilmClear", 0.95f, 0.97f, 1f, 0.88f, 0f, Surface.Transparent, 0.20f),
        S("M_TrayClear", 0.90f, 0.93f, 0.95f, 0.82f, 0f, Surface.Transparent, 0.55f),
        S("M_TrayBlack", 0.03f, 0.03f, 0.03f, 0.65f),
        S("M_PET_Cola", 0.10f, 0.04f, 0.02f, 0.94f, 0f, Surface.Transparent, 0.93f),
        S("M_PET_Orange", 1f, 0.36f, 0.02f, 0.94f, 0f, Surface.Transparent, 0.88f),
        S("M_PET_Water", 0.70f, 0.86f, 1f, 0.96f, 0f, Surface.Transparent, 0.35f),
        S("M_PET_Tea", 0.55f, 0.52f, 0.12f, 0.94f, 0f, Surface.Transparent, 0.82f),
        S("M_PET_Gel", 0.80f, 0.95f, 1f, 0.95f, 0f, Surface.Transparent, 0.50f),
        S("M_Net_Red", 1f, 1f, 1f, 0.4f, 0f, Surface.Cutout, 1f, "net_red", twoSided: true),
        S("M_Net_Orange", 1f, 1f, 1f, 0.4f, 0f, Surface.Cutout, 1f, "net_orange", twoSided: true),
        S("M_Leaf", 0.16f, 0.38f, 0.06f, 0.35f, twoSided: true),
        S("M_Stem", 0.20f, 0.12f, 0.05f, 0.2f),
        S("M_BananaTip", 0.10f, 0.07f, 0.03f, 0.2f),
        S("M_Croissant", 0.62f, 0.30f, 0.07f, 0.4f),
        S("M_Mochi", 0.96f, 0.70f, 0.76f, 0.5f),
    };

    // Fruit and veg take their catalogue colour, which is already sRGB.
    static readonly (string material, string product, float smooth)[] ProduceMaterials =
    {
        ("M_Apple", "produce_apples", 0.55f), ("M_Mikan", "produce_mikan", 0.35f),
        ("M_Tomato", "produce_tomatoes", 0.7f), ("M_Banana", "produce_bananas", 0.45f),
        ("M_Daikon", "produce_daikon", 0.5f),
    };

    // Printed ink on a can is far less shiny than the bare metal round it; at full metallic a
    // label goes nearly black wherever there's no reflection to show.
    const float LabelMetallicCap = 0.35f;

    static Dictionary<string, Material> BuildMaterials()
    {
        var made = new Dictionary<string, Material>();
        foreach (ProductDef p in ProductCatalog.All)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{LabelsFolder}/{p.Id}.png");
            made["L_" + p.Id] = Lit("L_" + p.Id, Color.white, p.Smoothness, Mathf.Min(p.Metallic, LabelMetallicCap),
                                    tex, Surface.Opaque, false);
        }
        foreach (Shared s in SharedMaterials)
        {
            Texture2D tex = s.Texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{s.Texture}.png") : null;
            Color c = s.Linear.gamma;
            c.a = s.Alpha;
            made[s.Name] = Lit(s.Name, c, s.Smooth, s.Metal, tex, s.Surface, s.TwoSided);
        }
        foreach (var (name, product, smooth) in ProduceMaterials)
        {
            ProductDef p = ProductCatalog.Get(product);
            made[name] = Lit(name, p != null ? p.Primary : Color.grey, smooth, 0f, null, Surface.Opaque, false);
        }
        AssetDatabase.SaveAssets();
        return made;
    }

    static Material Lit(string name, Color color, float smooth, float metal, Texture tex, Surface surface, bool twoSided)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader) m.shader = shader;

        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", tex);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_WorkflowMode", 1f);
        m.SetFloat("_Cull", twoSided ? 0f : 2f);

        bool transparent = surface == Surface.Transparent;
        bool cutout = surface == Surface.Cutout;
        m.SetFloat("_Surface", transparent ? 1f : 0f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
        m.SetFloat("_Cutoff", 0.5f);
        m.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
        m.SetFloat("_DstBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
        m.SetFloat("_SrcBlendAlpha", 1f);
        m.SetFloat("_DstBlendAlpha", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
        m.SetFloat("_ZWrite", transparent ? 0f : 1f);
        SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
        SetKeyword(m, "_ALPHATEST_ON", cutout);
        SetKeyword(m, "_ALPHAPREMULTIPLY_ON", false);
        m.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
        m.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent
                      : cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest
                      : -1;
        m.SetShaderPassEnabled("ShadowCaster", !transparent);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
    }

    // ------------------------------------------------------------------ the showcase

    // Every product on display shelves along the north edge of Models_Island, one section per
    // shelf in the order a customer walks the store, each with its name and price on the edge.
    public const string ShowcaseName = "Products_Showcase";

    public static string BuildShowcase()
    {
        var island = GameObject.Find("Models_Island");
        if (island == null) return "No Models_Island in the open scene.";
        var shelfModel = AssetDatabase.LoadAssetAtPath<GameObject>(ShelfModelPath);
        if (shelfModel == null) return $"No shelf model at {ShelfModelPath}.";

        Transform old = island.transform.Find(ShowcaseName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject(ShowcaseName);
        root.transform.SetParent(island.transform, false);
        Undo.RegisterCreatedObjectUndo(root, "Product showcase");

        ItemType[] order = ProductCatalog.StockSections;
        const float bayWidth = 4f;
        float[] platformTops = { 1.42f, 0.83f };   // eye level, then waist level, of a 3-shelf bay
        int bays = Mathf.CeilToInt(order.Length / (float)platformTops.Length);

        // A row along the north edge of the island's floor, backs to the edge, centred on it.
        Bounds floor = new Bounds(island.transform.position, new Vector3(30f, 0f, 20f));
        Transform plane = island.transform.Find("Plane_test");
        if (plane != null && plane.GetComponent<Renderer>() != null) floor = plane.GetComponent<Renderer>().bounds;
        float wallZ = floor.max.z - 0.7f;
        float firstX = floor.center.x - (bays - 1) * bayWidth * 0.5f;
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        bool hasYen = font != null && font.HasCharacter('¥');

        int placed = 0;
        for (int bay = 0; bay < bays; bay++)
        {
            var shelf = (GameObject)PrefabUtility.InstantiatePrefab(shelfModel, root.transform);
            shelf.name = $"ShowcaseShelf_{bay + 1}";
            // The one-sided bay's platforms stand in front of its back wall, on local -Z, so
            // with no rotation it faces south, into the island.
            shelf.transform.position = new Vector3(firstX + bay * bayWidth, 0f, wallZ);
            shelf.transform.rotation = Quaternion.identity;

            for (int level = 0; level < platformTops.Length; level++)
            {
                int s = bay * platformTops.Length + level;
                if (s >= order.Length) break;
                ItemType section = order[s];
                IReadOnlyList<ProductDef> range = ProductCatalog.InSection(section);

                // The section's name on the back panel over its row: dark on the pale top
                // panel, light on the dark one between the shelves.
                bool paleWall = level == 0;
                Label(shelf.transform, $"Sign_{section}", SignFor(section).ToUpperInvariant(),
                      new Vector3(-bayWidth * 0.5f + 0.06f, platformTops[level] + 0.47f, -0.03f),
                      2.4f, 0.11f, TextAlignmentOptions.Left,
                      paleWall ? new Color(0.55f, 0.08f, 0.06f) : new Color(1f, 0.85f, 0.3f), font, bold: true);

                for (int i = 0; i < range.Count; i++)
                {
                    ProductDef p = range[i];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/{p.Id}.prefab");
                    if (prefab == null) continue;

                    float x = -bayWidth * 0.5f + (i + 0.5f) * bayWidth / range.Count;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, shelf.transform);

                    // The model's front faces +Z; turn it to face the aisle. Anything whose
                    // print is on its lid (bars, the pizza, trays) is propped up to show it.
                    bool lidLabel = LabelOnTop(p);
                    go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(lidLabel ? 70f : 0f, 0f, 0f);
                    go.transform.localPosition = new Vector3(x, platformTops[level] + 0.3f, -0.3f);

                    // Stand it on the board, pulled up to the front edge the way stores face up.
                    Bounds b = go.GetComponent<Renderer>().bounds;
                    Vector3 local = go.transform.localPosition;
                    Vector3 minLocal = shelf.transform.InverseTransformPoint(b.min);
                    Vector3 maxLocal = shelf.transform.InverseTransformPoint(b.max);
                    float bottom = Mathf.Min(minLocal.y, maxLocal.y);
                    float front = Mathf.Min(minLocal.z, maxLocal.z);
                    go.transform.localPosition = new Vector3(x, local.y + (platformTops[level] + 0.002f - bottom),
                                                             local.z + (-0.47f - front));

                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

                    string price = hasYen ? $"¥{p.Price:0}" : $"{p.Price:0} yen";
                    Label(shelf.transform, $"Tag_{p.Id}", $"{Capitalise(p.Name)}  <b>{price}</b>",
                          new Vector3(x, platformTops[level] - 0.035f, -0.515f), bayWidth / range.Count - 0.04f, 0.06f,
                          TextAlignmentOptions.Center, new Color(0.12f, 0.12f, 0.13f), font);
                    placed++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(island.scene);
        EditorSceneManager.SaveScene(island.scene);
        return $"Showcase: {placed} products on {bays} shelves under Models_Island/{ShowcaseName}; scene saved.";
    }

    static string SignFor(ItemType section)
    {
        foreach (StoreLayout.Zone zone in StoreLayout.Zones)
            if (zone.Section == section) return zone.Sign;
        return ProductCatalog.SectionName(section);
    }

    // Packages printed on the lid rather than the front: bars and buns in flow wrap, flat
    // boxes (pizza, the mint tin, soap) and film-topped trays.
    static bool LabelOnTop(ProductDef p)
    {
        if (p.Shape == PackShape.Wrapper || p.Shape == PackShape.Tray) return true;
        return p.Shape == PackShape.Box && p.Size.y <= 0.5f * Mathf.Min(p.Size.x, p.Size.z) + 1e-4f;
    }

    static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

    static void Label(Transform parent, string name, string text, Vector3 localPosition, float width, float height,
                      TextAlignmentOptions align, Color color, TMP_FontAsset font, bool bold = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;     // TMP reads from -Z, the shelf's front
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.05f;
        tmp.fontSizeMax = 0.6f;
        tmp.alignment = align;
        tmp.color = color;
        if (bold) tmp.fontStyle = FontStyles.Bold;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(width, height);
        if (align == TextAlignmentOptions.Left) tmp.rectTransform.pivot = new Vector2(0f, 0.5f);
    }

    // ------------------------------------------------------------------ helpers

    static int Copy(string from, string to, List<string> missing)
    {
        if (!File.Exists(from)) { missing.Add(Path.GetFileName(from)); return 0; }
        string full = Path.GetFullPath(to);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.Copy(from, full, true);
        return 1;
    }

    static void ConfigureTexture(string path, int maxSize, TextureWrapMode wrap, bool alpha)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = wrap;
        ti.filterMode = FilterMode.Bilinear;
        ti.anisoLevel = 4;
        ti.maxTextureSize = maxSize;
        ti.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = alpha;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.SaveAndReimport();
    }

    static IEnumerable<string> SourceMaterialNames(string fbxPath)
    {
        var names = new HashSet<string>();
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is Material m) names.Add(m.name);
        // Already remapped ones no longer show up as sub-assets.
        if (AssetImporter.GetAtPath(fbxPath) is ModelImporter mi)
            foreach (var pair in mi.GetExternalObjectMap())
                if (pair.Key.type == typeof(Material)) names.Add(pair.Key.name);
        return names;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
