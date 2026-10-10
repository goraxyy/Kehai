using System.IO;
using NUnit.Framework;
using Choice = MainMenu.Choice;

// What the main menu offers depends on the career Karen has on file, and the build has to
// contain the store.
public class MainMenuTests
{
    [Test]
    public void ANewEmployee_StartsTheirFirstShift()
    {
        var choices = MainMenu.ChoicesFor(0, careerOver: false);
        Assert.AreEqual(Choice.FirstShift, choices[0]);
        CollectionAssert.DoesNotContain(choices, Choice.Continue);
        CollectionAssert.DoesNotContain(choices, Choice.NewCareer);
    }

    [Test]
    public void ACareerOnFile_ComesFirst_AndCanBeStartedOver()
    {
        var choices = MainMenu.ChoicesFor(6, careerOver: false);
        Assert.AreEqual(Choice.Continue, choices[0]);
        Assert.AreEqual(Choice.NewCareer, choices[1]);
        CollectionAssert.DoesNotContain(choices, Choice.FirstShift);
    }

    [Test]
    public void AnEndedCareer_CanOnlyBeStartedOver()
    {
        var choices = MainMenu.ChoicesFor(14, careerOver: true);
        Assert.AreEqual(Choice.NewCareer, choices[0]);
        CollectionAssert.DoesNotContain(choices, Choice.Continue);
    }

    [Test]
    public void EveryMenu_EndsWithSettingsControlsAndQuit()
    {
        foreach ((int shifts, bool over) in new[] { (0, false), (3, false), (9, true) })
        {
            var choices = MainMenu.ChoicesFor(shifts, over);
            CollectionAssert.AreEqual(new[] { Choice.Settings, Choice.Controls, Choice.Quit },
                choices.GetRange(choices.Count - 3, 3), $"{shifts} shifts, over: {over}");
        }
    }

    [Test]
    public void TheBuild_OpensOnTheStore()
    {
        string[] scenes = KehaiBuild.Scenes;
        Assert.IsNotEmpty(scenes, "no scenes in the build list");
        foreach (string scene in scenes) Assert.IsTrue(File.Exists(scene), scene);
        StringAssert.EndsWith("SampleScene.unity", scenes[0], "the first scene is the one the game opens with");
    }

    // Parked until work on it picks up again: listed after everything else, so the store stays
    // the scene the game, the eval and the replays open with.
    [Test]
    public void TheEndlessMaze_IsLastInTheBuildList()
    {
        UnityEditor.EditorBuildSettingsScene[] scenes = UnityEditor.EditorBuildSettings.scenes;
        int at = System.Array.FindIndex(scenes, s => s.path == EndlessMazeBuilder.ScenePath);
        if (at < 0) Assert.Ignore("the endless maze hasn't been made on this computer");
        Assert.AreEqual(scenes.Length - 1, at, "the endless maze isn't last in the build list");
    }

    [Test]
    public void EachPlatform_BuildsIntoItsOwnFolder()
    {
        string name = UnityEditor.PlayerSettings.productName;
        StringAssert.EndsWith(Path.Combine("Builds", "macOS", name + ".app"), KehaiBuild.DefaultPath(UnityEditor.BuildTarget.StandaloneOSX));
        StringAssert.EndsWith(Path.Combine("Builds", "Windows", name + ".exe"), KehaiBuild.DefaultPath(UnityEditor.BuildTarget.StandaloneWindows64));
    }
}
