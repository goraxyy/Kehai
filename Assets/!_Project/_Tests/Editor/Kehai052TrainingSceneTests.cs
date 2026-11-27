using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 0.5.2, C8.1: the generated Training scene carries no UI text, no audio and one material, and its
// check names what breaks that.
public class Kehai052TrainingSceneTests
{
    Scene scene;
    Material grey, other;

    [SetUp]
    public void SetUp()
    {
        scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        grey = new Material(shader) { name = "TrainingGrey" };
        other = new Material(shader) { name = "Shelf wood" };
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(grey);
        Object.DestroyImmediate(other);
    }

    GameObject Shelf(Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    [Test]
    public void AGreyScene_WithNoTextOrAudio_IsClean()
    {
        Shelf(grey);
        Shelf(grey);
        Assert.IsNull(KehaiTrainingScene.ContentsProblem(scene, grey));
    }

    [Test]
    public void AnotherMaterial_TextOrAudio_IsNamed()
    {
        Shelf(other).name = "Shelf_01";
        StringAssert.Contains("Shelf_01", KehaiTrainingScene.ContentsProblem(scene, grey));

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene = SceneManager.GetActiveScene();
        new GameObject("PA voice").AddComponent<AudioSource>();
        StringAssert.Contains("audio", KehaiTrainingScene.ContentsProblem(scene, grey));

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene = SceneManager.GetActiveScene();
        new GameObject("Label").AddComponent<TMPro.TextMeshPro>();
        StringAssert.Contains("text or UI", KehaiTrainingScene.ContentsProblem(scene, grey));
    }
}
