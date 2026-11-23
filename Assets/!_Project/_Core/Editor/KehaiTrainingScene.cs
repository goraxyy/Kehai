using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A local Training copy of the saved Testing store. The source and build list stay as authored.
public static class KehaiTrainingScene
{
    const string Folder = "Assets/KehaiGeneratedTraining";
    public const string ScenePath = Folder + "/Training.unity";
    const string MaterialPath = Folder + "/TrainingGrey.mat";
    const string Receipt = Folder + "/training-fingerprint.txt";
    const string Generator = "Assets/!_Project/_Core/Editor/KehaiTrainingScene.cs";
    const string Stripper = "Assets/!_Project/_Core/Editor/EditionBuildProcessor.cs";

    [MenuItem("Kehai/Training Scene/Generate")]
    public static void Generate()
    {
        string source = TestingScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finish Play mode before generating the Training scene.");
        if (SceneManager.GetSceneByPath(ScenePath).isLoaded)
            throw new InvalidOperationException("Close the generated Training scene before regenerating it.");
        if (IsDirty(source))
            throw new InvalidOperationException("Save the Testing scene before generating the Training scene.");

        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "KehaiGeneratedTraining");
        Material grey = GreyMaterial();
        string scratch = AssetDatabase.GenerateUniqueAssetPath(Folder + "/TrainingWork.unity");
        if (!AssetDatabase.CopyAsset(source, scratch))
            throw new IOException($"Could not make a local copy of {source}.");

        Scene previous = SceneManager.GetActiveScene();
        Scene copy = default;
        try
        {
            copy = EditorSceneManager.OpenScene(scratch, OpenSceneMode.Additive);
            EditionBuildProcessor.StripScene(copy, KehaiEdition.Training, grey);
            if (File.Exists(Receipt)) File.Delete(Receipt);
            if (!EditorSceneManager.SaveScene(copy, ScenePath, saveAsCopy: true))
                throw new IOException("Could not save the generated Training scene.");
        }
        finally
        {
            if (copy.IsValid() && copy.isLoaded) EditorSceneManager.CloseScene(copy, removeScene: true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            AssetDatabase.DeleteAsset(scratch);
        }
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        File.WriteAllText(Receipt, Fingerprint(source));
        Debug.Log("Generated Training scene at " + ScenePath);
    }

    public static void GenerateBatch() => RunBatch(() =>
    {
        Generate();
        Check();
    });

    [MenuItem("Kehai/Training Scene/Check")]
    public static void Check()
    {
        RequireCurrent();
        Debug.Log("Training scene is current at " + ScenePath);
    }

    public static void CheckBatch() => RunBatch(Check);

    public static void RequireCurrent()
    {
        string source = TestingScene();
        if (IsDirty(source) || !File.Exists(ScenePath) || !File.Exists(Receipt) ||
            File.ReadAllText(Receipt) != Fingerprint(source))
            throw new InvalidOperationException("Training scene is missing or stale. Save the Testing scene and use Kehai → Training Scene → Generate.");
    }

    static string TestingScene() => KehaiBuild.Scenes.FirstOrDefault(path => path != ScenePath)
        ?? throw new InvalidOperationException("Add the Testing store as the first enabled scene in File → Build Profiles.");

    static bool IsDirty(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        return scene.IsValid() && scene.isDirty;
    }

    static void RunBatch(Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    static string Fingerprint(string source) => string.Join("\n", new[]
    {
        source,
        AssetDatabase.GetAssetDependencyHash(source).ToString(),
        AssetDatabase.GetAssetDependencyHash(ScenePath).ToString(),
        AssetDatabase.GetAssetDependencyHash(Generator).ToString(),
        AssetDatabase.GetAssetDependencyHash(Stripper).ToString(),
    });

    static Material GreyMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null) return existing;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("Cannot find the URP Lit shader for the Training scene.");
        var grey = new Material(shader) { color = Color.gray };
        AssetDatabase.CreateAsset(grey, MaterialPath);
        return grey;
    }
}
