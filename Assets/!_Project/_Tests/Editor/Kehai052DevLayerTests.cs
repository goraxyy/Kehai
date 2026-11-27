using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 0.5.2, C8.5: a dev layer opens over a saved scene, takes the new objects, keeps its experiments,
// and never ships.
public class Kehai052DevLayerTests
{
    const string Folder = "Assets/KehaiDevLayerTests";
    const string BasePath = Folder + "/Base.unity";

    [SetUp]
    public void SetUp()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "KehaiDevLayerTests");
        Scene under = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Shelf_01");
        Assert.IsTrue(EditorSceneManager.SaveScene(under, BasePath));
    }

    [TearDown]
    public void TearDown()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AssetDatabase.DeleteAsset(Folder);
    }

    [Test]
    public void ALayer_OpensOverTheScene_AndNewObjectsLandInIt()
    {
        Scene layer = DevLayers.Open("Owner", Folder);
        Assert.AreEqual(2, SceneManager.sceneCount);
        Assert.AreEqual(layer, SceneManager.GetActiveScene());
        Assert.AreEqual(layer, new GameObject("Experiment").scene);
        Assert.IsTrue(System.IO.File.Exists(Folder + "/Dev_Owner.unity"));
        Assert.IsTrue(SceneManager.GetSceneByPath(BasePath).isLoaded, "the scene underneath stays open");

        DevLayers.CloseAll(folder: Folder);
        Assert.AreEqual(1, SceneManager.sceneCount);
    }

    [Test]
    public void ALayer_KeepsItsExperiments()
    {
        Scene layer = DevLayers.Open("Sound", Folder);
        new GameObject("Hum test");
        EditorSceneManager.SaveScene(layer);
        DevLayers.CloseAll(folder: Folder);

        layer = DevLayers.Open("Sound", Folder);
        Assert.AreEqual("Hum test", layer.GetRootGameObjects()[0].name);
    }

    [Test]
    public void ALayer_NeedsASavedSceneUnderneath()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Assert.Throws<System.InvalidOperationException>(() => DevLayers.Open("Art", Folder));
        Assert.Throws<System.ArgumentException>(() => DevLayers.PathFor("../Base"));
    }

    [Test]
    public void TheBuild_RefusesADevLayer()
    {
        Assert.Throws<BuildFailedException>(() => DevLayers.RefuseInBuild(new[]
            { "Assets/!_Project/_Core/Scenes/SampleScene.unity", DevLayers.PathFor("Owner") }));
        Assert.DoesNotThrow(() => DevLayers.RefuseInBuild(new[] { "Assets/!_Project/_Core/Scenes/SampleScene.unity" }));
        Assert.IsTrue(DevLayers.IsDevLayer("assets\\kehaidevlayers\\Dev_Art.unity"), "any case, either slash");
    }
}
