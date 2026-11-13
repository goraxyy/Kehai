using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Brings Karen's props in from the Blender pipeline (~/Desktop/KehaiRelated/Props, or
// KEHAI_PROPS): her camera, the fridge control box and the PA box (0.3.0 build plan B6.1, C6.1),
// and 0.4.1's PA microphone, camera box, store camera and office screen (PropModels.All).
//
//   Level/Props/Resources/KarenProps/<name>.fbx   the models; PropModels loads them by name
//   Level/Props/Materials/M_*.mat                 URP Lit, from the same colours as the Blender
//                                                 script, the lamps emissive
//
// The whole Level/Props folder is in .gitignore: models stay on the Mac, never in git. Until
// this runs, every prop builds its own boxes. Rerun it after rebuilding the models.
public static class PropImport
{
    public const string Root = "Assets/!_Project/_Game/Level/Props";
    const string ModelsFolder = Root + "/Resources/" + PropModels.Folder;
    const string MaterialsFolder = Root + "/Materials";
    static readonly string[] Models = PropModels.All;

    // name → colour (sRGB), smoothness, metallic, emission (black for none). As build_props.py.
    static readonly (string name, Color colour, float smooth, float metal, Color emit)[] Palette =
    {
        ("M_CamBody", new Color(0.86f, 0.86f, 0.83f), 0.55f, 0f, Color.black),
        ("M_CamLens", new Color(0.02f, 0.02f, 0.025f), 0.95f, 0f, Color.black),
        ("M_CamBezel", new Color(0.12f, 0.12f, 0.13f), 0.65f, 0.6f, Color.black),
        ("M_LedRed", new Color(0.8f, 0.03f, 0.02f), 0.8f, 0f, new Color(1f, 0.04f, 0.02f) * 2f),
        ("M_DarkMetal", new Color(0.18f, 0.18f, 0.19f), 0.6f, 0.8f, Color.black),
        ("M_Sticker", new Color(0.96f, 0.96f, 0.94f), 0.4f, 0f, Color.black),
        ("M_Ink", new Color(0.03f, 0.03f, 0.03f), 0.3f, 0f, Color.black),
        ("M_BoxSteel", new Color(0.33f, 0.42f, 0.47f), 0.45f, 0.35f, Color.black),
        ("M_BoxSeam", new Color(0.22f, 0.28f, 0.31f), 0.4f, 0.3f, Color.black),
        ("M_Hazard", new Color(0.95f, 0.74f, 0.05f), 0.5f, 0f, Color.black),
        ("M_LampAmber", new Color(0.9f, 0.45f, 0.03f), 0.8f, 0f, new Color(1f, 0.5f, 0.05f) * 2f),
        ("M_KnobRed", new Color(0.72f, 0.06f, 0.05f), 0.65f, 0f, Color.black),
        ("M_Conduit", new Color(0.55f, 0.56f, 0.57f), 0.6f, 0.7f, Color.black),
        ("M_Enamel", new Color(0.9f, 0.86f, 0.74f), 0.6f, 0f, Color.black),
        ("M_Chrome", new Color(0.8f, 0.8f, 0.82f), 0.85f, 1f, Color.black),
        ("M_Grille", new Color(0.08f, 0.08f, 0.09f), 0.4f, 0f, Color.black),
        ("M_LampGreen", new Color(0.05f, 0.45f, 0.1f), 0.8f, 0f, new Color(0.1f, 0.9f, 0.2f) * 1.5f),
        ("M_LampRedDim", new Color(0.45f, 0.04f, 0.04f), 0.75f, 0f, Color.black),
        ("M_Lever", new Color(0.06f, 0.06f, 0.06f), 0.7f, 0f, Color.black),
        // Kehai 0.4.1
        ("M_CamBoxSteel", new Color(0.24f, 0.27f, 0.33f), 0.45f, 0.35f, Color.black),
        ("M_CamBoxSeam", new Color(0.19f, 0.22f, 0.27f), 0.4f, 0.3f, Color.black),
        ("M_TalkRed", new Color(0.78f, 0.2f, 0.2f), 0.65f, 0f, Color.black),
        ("M_MicFoam", new Color(0.13f, 0.13f, 0.14f), 0.05f, 0f, Color.black),
        ("M_ScreenCase", new Color(0.07f, 0.07f, 0.08f), 0.55f, 0f, Color.black),
        ("M_ScreenGlass", new Color(0.02f, 0.025f, 0.03f), 0.92f, 0f, Color.black),
    };

    static string SourceFolder
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("KEHAI_PROPS");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Desktop", "KehaiRelated", "Props");
        }
    }

    [MenuItem("Kehai/Props/Import Karen's Props")]
    public static void ImportMenu() => Debug.Log(ImportAll());

    public static string ImportAll()
    {
        var log = new StringBuilder("Karen's props:\n");
        string source = SourceFolder;
        if (!Directory.Exists(source)) return $"No props folder at {source} (set KEHAI_PROPS).";
        EnsureFolder(ModelsFolder);
        EnsureFolder(MaterialsFolder);

        var missing = new List<string>();
        int copied = 0;
        foreach (string m in Models)
        {
            string from = Path.Combine(source, "fbx", m + ".fbx");
            if (!File.Exists(from)) { missing.Add(m); continue; }
            string to = Path.GetFullPath($"{ModelsFolder}/{m}.fbx");
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
            copied++;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        log.AppendLine($"  copied {copied} models" + (missing.Count > 0 ? $"; missing: {string.Join(", ", missing)}" : ""));

        var materials = BuildMaterials();
        log.AppendLine($"  {materials.Count} materials");

        int remapped = 0;
        foreach (string m in Models)
        {
            string path = $"{ModelsFolder}/{m}.fbx";
            if (!(AssetImporter.GetAtPath(path) is ModelImporter mi)) continue;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.useFileScale = true;
            mi.globalScale = 1f;
            mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Material mat && materials.TryGetValue(mat.name, out Material ours))
                {
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), ours);
                    remapped++;
                }
            mi.SaveAndReimport();
        }
        log.AppendLine($"  {remapped} material slots remapped");
        log.AppendLine("  The game uses them from the next Play; without them it builds its own boxes.");
        return log.ToString();
    }

    static Dictionary<string, Material> BuildMaterials()
    {
        var made = new Dictionary<string, Material>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        foreach (var p in Palette)
        {
            string path = $"{MaterialsFolder}/{p.name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit) { name = p.name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = lit;
            m.color = p.colour;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", p.colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", p.smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", p.metal);
            bool glows = p.emit.maxColorComponent > 0f;
            if (glows) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glows ? p.emit : Color.black);
            m.globalIlluminationFlags = glows ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            EditorUtility.SetDirty(m);
            made[p.name] = m;
        }
        AssetDatabase.SaveAssets();
        return made;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
