using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Kehai.Sandbox;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The roadside art's way into the project (Codex's importer, 27 November; adopted 28 November).
//
// Kehai → Art → Import roadside exchange reads the Blender exporter's Exchange folder
// (geometry.bin, market.json, Textures) and writes native meshes, materials and prefabs into
// the private art folder, Assets/KehaiRoadsideArt, never into a Resources folder (one ships in
// every build). The materials use the game's own copy of the shader (Sandbox/Shaders, with fog).
//
// An older package, imported from Codex's .unitypackage, put its prefabs under Resources and
// carried its own copy of the shader. When the editor loads, Adopt moves the prefabs out of
// Resources (keeping their GUIDs), points the materials at the game's shader and deletes the
// package's copy, once.
[InitializeOnLoad]
public static class RoadsideArtImport
{
    const string Root = MiniStoreRoadside.ArtRoot;
    const string Prefabs = MiniStoreRoadside.Prefabs;
    const string OldResources = Root + "/Resources/KehaiRoadside";
    const string GameShader = "Assets/!_Project/_Game/Sandbox/Shaders/RoadsideArt.shader";
    const string PackageShader = Root + "/RoadsideArt.shader";

    static RoadsideArtImport() => EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Adopt(); };

    [MenuItem("Kehai/Art/Tidy the roadside art (out of Resources, the game's shader)")]
    public static void AdoptNow() => Debug.Log("RoadsideArtImport: " + Adopt());

    // What it did, or why nothing was needed.
    public static string Adopt()
    {
        if (!AssetDatabase.IsValidFolder(Root)) return "no roadside art in this project";
        var done = new List<string>();
        if (AssetDatabase.IsValidFolder(OldResources))
        {
            if (AssetDatabase.IsValidFolder(Prefabs)) return $"both {OldResources} and {Prefabs} exist: merge them by hand";
            string error = AssetDatabase.MoveAsset(OldResources, Prefabs);
            if (!string.IsNullOrEmpty(error)) return "could not move the prefabs out of Resources: " + error;
            done.Add("prefabs moved out of Resources");
            string resources = Root + "/Resources";
            if (AssetDatabase.IsValidFolder(resources) && AssetDatabase.FindAssets("", new[] { resources }).Length == 0)
            {
                AssetDatabase.DeleteAsset(resources);
                done.Add("the empty Resources folder deleted");
            }
        }
        Shader game = AssetDatabase.LoadAssetAtPath<Shader>(GameShader);
        Shader package = AssetDatabase.LoadAssetAtPath<Shader>(PackageShader);
        if (game != null && package != null)
        {
            int moved = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material.shader != package) continue;
                int queue = material.renderQueue;
                material.shader = game;
                material.renderQueue = queue;
                EditorUtility.SetDirty(material);
                moved++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(PackageShader);
            done.Add($"{moved} materials on the game's shader, the package's copy deleted");
        }
        return done.Count == 0 ? "nothing to tidy" : string.Join("; ", done);
    }

    [Serializable] class Description
    {
        public string name, role, sku;
        public float[] pivot, size, bay;
        public int tri;
    }
    [Serializable] class Finish { public string name, tex, finish, mode; public float em, alpha; }
    [Serializable] class Manifest { public int version; public Description[] meshes; public Finish[] materials; }

    [MenuItem("Kehai/Art/Import roadside exchange")]
    public static void Choose()
    {
        string folder = EditorUtility.OpenFolderPanel("Choose the Blender Exchange directory", "", "");
        if (!string.IsNullOrEmpty(folder)) Import(folder);
    }

    static Vector3 Vector(float[] a) => new Vector3(a[0], a[1], a[2]);
    static string ReadName(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
    static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name);

    public static void Import(string exchange)
    {
        Adopt();
        Manifest data = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(exchange, "market.json")));
        if (data.version != 1) throw new InvalidDataException("Unsupported exchange version");
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(GameShader);
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("The game's RoadsideArt.shader is missing or broken: " + GameShader);
        foreach (string folder in new[] { Root + "/Textures", Root + "/Meshes", Root + "/Materials", Prefabs + "/Products" })
            Directory.CreateDirectory(folder);
        var names = new HashSet<string>(data.materials.SelectMany(d => new[] { d.tex, d.finish }))
            { "Illumination.png", "ReflectionRoad.png", "ReflectionShop.png" };
        foreach (string file in names)
            File.Copy(Path.Combine(exchange, "Textures", file), Root + "/Textures/" + file, true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (string name in names) ConfigureTexture(name);
        var materials = data.materials.ToDictionary(d => d.name, d => StoreMaterial(shader, d));
        var descriptions = data.meshes.ToDictionary(d => d.name);
        var art = new GameObject("Roadside art");
        try
        {
            using var reader = new BinaryReader(File.OpenRead(Path.Combine(exchange, "geometry.bin")));
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "KHM1" || reader.ReadInt32() != descriptions.Count)
                throw new InvalidDataException("Mesh header does not match the manifest");
            for (int i = 0; i < data.meshes.Length; i++)
            {
                var d = descriptions[ReadName(reader)];
                var mesh = ReadMesh(reader, d, materials, out var finishes);
                if (d.role == "Product") FitProduct(mesh, d.sku);
                var saved = SaveMesh(mesh, Root + "/Meshes/" + d.name + ".asset");
                var go = new GameObject(d.name);
                go.AddComponent<MeshFilter>().sharedMesh = saved;
                var render = go.AddComponent<MeshRenderer>();
                render.sharedMaterials = finishes;
                render.shadowCastingMode = ShadowCastingMode.On;
                if (d.role == "Product")
                {
                    try { PrefabUtility.SaveAsPrefabAsset(go, Prefabs + "/Products/" + d.sku + ".prefab"); }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
                else
                {
                    go.transform.SetParent(art.transform, false);
                    go.transform.localPosition = Vector(d.pivot);
                }
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected trailing data");
            PrefabUtility.SaveAsPrefabAsset(art, Prefabs + "/Art.prefab");
            AssetDatabase.SaveAssets();
        }
        finally { UnityEngine.Object.DestroyImmediate(art); }
    }

    static void ConfigureTexture(string name)
    {
        string path = Root + "/Textures/" + name;
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        bool reflection = name.StartsWith("Reflection");
        bool data = reflection || name.StartsWith("Finish_") || name == "Illumination.png";
        imp.sRGBTexture = !data;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.alphaIsTransparency = false;
        imp.maxTextureSize = 2048;
        imp.mipmapEnabled = !reflection && !name.Contains("Finishes") && name != "Illumination.png";
        bool clamp = name.Contains("Print") || name.Contains("Finishes") || name == "Illumination.png";
        imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        imp.anisoLevel = name.Contains("Print") ? 2 : 1;
        imp.textureCompression = (data || name == "Finishes.png") ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
        if (name.StartsWith("Finish_Print")) imp.maxTextureSize = 512;
        if (AssetDatabase.WriteImportSettingsIfDirty(path)) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    static Material StoreMaterial(Shader shader, Finish d)
    {
        string path = Root + "/Materials/" + d.name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = !mat;
        if (create) mat = new Material(shader);
        mat.name = d.name;
        mat.shader = shader;
        mat.enableInstancing = true;
        mat.SetTexture("_BaseMap", Texture(d.tex));
        mat.SetTexture("_FinishMap", Texture(d.finish));
        mat.SetTexture("_ShopReflection", Texture("ReflectionShop.png"));
        mat.SetTexture("_RoadReflection", Texture("ReflectionRoad.png"));
        mat.SetTexture("_Irradiance", Texture("Illumination.png"));
        int mode = d.mode == "emissive" ? 1 : d.mode == "screen" ? 2 : d.mode == "glass" ? 3 : d.mode == "cutout" ? 4 : 0;
        bool glass = mode == 3;
        mat.SetFloat("_Mode", mode);
        mat.SetFloat("_Glow", Mathf.Max(.85f, d.em));
        mat.SetFloat("_Alpha", d.alpha);
        mat.SetFloat("_Cutoff", .38f);
        mat.SetFloat("_Wall", d.name == "TileWall" ? 1 : 0);
        mat.SetFloat("_Bump", d.name == "FloorFinish" ? .0035f : d.name.StartsWith("Tile") ? .004f : 0);
        mat.SetFloat("_Cull", glass || mode == 4 || d.name.Contains("Canvas") ? (int)CullMode.Off : (int)CullMode.Back);
        mat.SetFloat("_ZWrite", glass ? 0 : 1);
        mat.SetFloat("_Src", glass ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
        mat.SetFloat("_Dst", glass ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
        mat.SetOverrideTag("RenderType", glass ? "Transparent" : "Opaque");
        mat.renderQueue = glass ? 3000 : mode == 4 ? 2450 : 2000;
        mat.SetShaderPassEnabled("ShadowCaster", mode == 0 || mode == 4);
        mat.SetShaderPassEnabled("DepthOnly", !glass);
        if (create) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
        return mat;
    }

    static Mesh ReadMesh(BinaryReader r, Description desc, Dictionary<string, Material> all, out Material[] mats)
    {
        int parts = r.ReadInt32();
        mats = new Material[parts];
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int[]>();
        for (int p = 0; p < parts; p++)
        {
            mats[p] = all[ReadName(r)];
            int count = r.ReadInt32(), indices = r.ReadInt32(), offset = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                vertices.Add(ReadBlenderVector(r));
                normals.Add(ReadBlenderVector(r));
                uv.Add(new Vector2(r.ReadSingle(), r.ReadSingle()));
            }
            int[] faces = new int[indices];
            for (int i = 0; i < indices; i += 3)
            {
                faces[i] = offset + r.ReadInt32();
                faces[i + 2] = offset + r.ReadInt32();
                faces[i + 1] = offset + r.ReadInt32();
            }
            triangles.Add(faces);
        }
        var mesh = new Mesh { name = desc.name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = parts;
        for (int i = 0; i < parts; i++) mesh.SetTriangles(triangles[i], i, false);
        mesh.RecalculateBounds();
        if (triangles.Sum(t => t.Length / 3) != desc.tri || (mesh.bounds.size - Vector(desc.size)).magnitude > .004f)
            throw new InvalidDataException("Bounds or triangle count changed on " + desc.name);
        return mesh;
    }

    static Vector3 ReadBlenderVector(BinaryReader r)
    {
        float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle();
        return new Vector3(x, z, y);
    }

    static void FitProduct(Mesh mesh, string sku)
    {
        var source = ProductLook.Prefab(sku);
        var original = source ? source.GetComponentInChildren<MeshFilter>() : null;
        if (!original || !original.sharedMesh) throw new InvalidOperationException("Import the base game catalogue first. Missing " + sku);
        var target = original.sharedMesh.bounds;
        var bounds = mesh.bounds;
        var scale = new Vector3(target.size.x / bounds.size.x, target.size.y / bounds.size.y, target.size.z / bounds.size.z);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = target.center + Vector3.Scale(vertices[i] - bounds.center, scale);
            normals[i] = new Vector3(normals[i].x / scale.x, normals[i].y / scale.y, normals[i].z / scale.z).normalized;
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
    }

    static Mesh SaveMesh(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!existing) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
