using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 0.5.2, C8.5: a dev layer each for the owner, the sound designer and the artist. A layer is a
// scene of one person's experiments, opened on top of the Training or the Testing scene (or any
// saved scene), so trying something never touches the scene underneath. Never shipped: the folder
// is kept out of git, and a build that would carry one stops (DevLayerBuildGuard).
public static class DevLayers
{
    public const string Folder = "Assets/KehaiDevLayers";
    public static readonly string[] People = { "Owner", "Sound", "Art" };

    public static string PathFor(string who, string folder = Folder)
    {
        if (string.IsNullOrWhiteSpace(who) || who.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || who.Contains('/'))
            throw new ArgumentException($"'{who}' can't name a dev layer.");
        return $"{folder}/Dev_{who}.unity";
    }

    public static bool IsDevLayer(string scenePath, string folder = Folder) => !string.IsNullOrEmpty(scenePath) &&
        scenePath.Replace('\\', '/').StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);

    // The build's side: stops when any scene in the list is a dev layer.
    public static void RefuseInBuild(IEnumerable<string> scenes)
    {
        string[] layers = scenes.Where(path => IsDevLayer(path)).ToArray();
        if (layers.Length > 0)
            throw new BuildFailedException("A dev layer is never shipped. Take it out of File → Build Profiles: " + string.Join(", ", layers));
    }

    [MenuItem("Kehai/Dev Layer/Open Mine")] static void OpenOwner() => Open("Owner");
    [MenuItem("Kehai/Dev Layer/Open the Sound Designer's")] static void OpenSound() => Open("Sound");
    [MenuItem("Kehai/Dev Layer/Open the Artist's")] static void OpenArt() => Open("Art");
    [MenuItem("Kehai/Dev Layer/Close Dev Layers")] static void CloseMenu() => CloseAll(askToSave: true);

    // Opens `who`'s layer over the scenes already open, making it the first time, and makes it the
    // active scene, so new objects land in the layer and not in the scene underneath.
    public static Scene Open(string who, string folder = Folder)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Finish Play mode before opening a dev layer.");
        string path = PathFor(who, folder);
        var open = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
        if (!open.Any(s => s.isLoaded && !IsDevLayer(s.path, folder)))
            throw new InvalidOperationException("Open the Training or the Testing scene first: a dev layer goes on top of one.");
        if (open.Any(s => s.isLoaded && string.IsNullOrEmpty(s.path)))
            throw new InvalidOperationException("Save the open scene first: a dev layer can't go on top of an untitled one.");

        Scene layer = SceneManager.GetSceneByPath(path);
        if (!layer.IsValid() || !layer.isLoaded)
        {
            if (File.Exists(path)) layer = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            else
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
                layer = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                if (!EditorSceneManager.SaveScene(layer, path))
                    throw new IOException("Could not save the dev layer at " + path);
            }
        }
        SceneManager.SetActiveScene(layer);
        return layer;
    }

    // The scene a shift plays and reloads: the first open one that isn't a dev layer.
    public static Scene SceneToPlay(string folder = Folder) => Enumerable.Range(0, SceneManager.sceneCount)
        .Select(SceneManager.GetSceneAt).FirstOrDefault(s => s.isLoaded && !IsDevLayer(s.path, folder));

    // Closes every open dev layer; the scene underneath becomes active again.
    public static void CloseAll(bool askToSave = false, string folder = Folder)
    {
        var layers = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
            .Where(s => IsDevLayer(s.path, folder)).ToArray();
        if (askToSave && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(layers)) return;
        foreach (Scene layer in layers) EditorSceneManager.CloseScene(layer, removeScene: true);
    }

    // The proof on the real scenes (kehai-run unity, on a clone): the owner's layer opens over the
    // Testing scene, then over a freshly generated Training scene.
    public static void CheckBatch()
    {
        try
        {
            KehaiTrainingScene.Generate();
            foreach (string scene in new[] { KehaiBuild.Scenes.First(p => p != KehaiTrainingScene.ScenePath), KehaiTrainingScene.ScenePath })
            {
                EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
                Scene layer = Open("Owner");
                var marker = new GameObject("Dev layer check");
                bool inLayer = marker.scene == layer;
                Scene under = SceneManager.GetSceneByPath(scene);
                if (!inLayer || !under.isLoaded || SceneManager.sceneCount != 2)
                    throw new InvalidOperationException($"The dev layer didn't open over {scene}.");
                Debug.Log($"Dev layer {layer.path} opens over {scene} ({under.rootCount} roots underneath); new objects land in the layer");
                UnityEngine.Object.DestroyImmediate(marker);
                CloseAll();
            }
            RefuseInBuild(KehaiBuild.Scenes);
            Debug.Log("No dev layer is in the build list");
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }
}

// Play with a dev layer open. A shift reloads the active scene (KehaiEnv, ShiftRestart), so the
// scene underneath is made active as Play starts, and every open layer is loaded again on top
// after each reload. Back in edit mode, the layer that was active is active again.
[InitializeOnLoad]
static class DevLayerPlay
{
    const string OpenKey = "Kehai.DevLayers.Open", ActiveKey = "Kehai.DevLayers.Active";

    static DevLayerPlay()
    {
        EditorApplication.playModeStateChanged += Changed;
        SceneManager.sceneLoaded += Loaded;
    }

    static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var layers = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .Where(s => s.isLoaded && DevLayers.IsDevLayer(s.path)).Select(s => s.path).ToArray();
            SessionState.SetString(OpenKey, string.Join("\n", layers));   // survives Play's domain reload
            Scene active = SceneManager.GetActiveScene();
            Scene under = DevLayers.SceneToPlay();
            SessionState.SetString(ActiveKey, DevLayers.IsDevLayer(active.path) ? active.path : "");
            if (DevLayers.IsDevLayer(active.path) && under.IsValid()) SceneManager.SetActiveScene(under);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Scene layer = SceneManager.GetSceneByPath(SessionState.GetString(ActiveKey, ""));
            if (layer.IsValid() && layer.isLoaded) SceneManager.SetActiveScene(layer);
            SessionState.EraseString(OpenKey);
            SessionState.EraseString(ActiveKey);
        }
    }

    static void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (!Application.isPlaying || mode != LoadSceneMode.Single || DevLayers.IsDevLayer(scene.path)) return;
        foreach (string path in SessionState.GetString(OpenKey, "").Split('\n'))
            if (path.Length > 0 && !SceneManager.GetSceneByPath(path).isLoaded)
                EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
    }
}

// Every build, from the Kehai menus or from Unity's own Build Profiles window, processes each scene
// it ships; a dev layer among them stops the build. Play mode passes no report and isn't checked.
public sealed class DevLayerBuildGuard : IProcessSceneWithReport
{
    public int callbackOrder => -2000;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report != null) DevLayers.RefuseInBuild(new[] { scene.path });
    }
}
