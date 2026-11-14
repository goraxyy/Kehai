using Kehai.Karen;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Compile offline; execute only in the Unity EditMode suite when authorised.
public sealed class EditionRuntimeHookTests
{
    GameObject root;
    [SetUp] public void SetUp()
    {
        Edition.EditorOverride = KehaiEdition.Training;
        CareerRuntime.PendingCompletedShifts = 0;
        root = new GameObject("Edition runtime hook test");
    }
    [TearDown] public void TearDown()
    {
        Object.DestroyImmediate(root);
        CareerRuntime.PendingCompletedShifts = null;
        Edition.EditorOverride = null;
        CareerRuntime.GameplaySimulation = false;
    }
    [Test] public void PreparationRunsBeforeClockInAndTasks()
    {
        var tasks = root.AddComponent<TaskManager>();
        var shift = root.AddComponent<ShiftManager>(); shift.taskManager = tasks;
        shift.SetShiftNumber(2);
        bool prepared = false;
        System.Action<int, int, bool> listener = (number, seed, training) =>
        {
            Assert.IsFalse(shift.IsShiftActive); Assert.IsFalse(tasks.ShiftRunning);
            Assert.AreEqual(3, number); Assert.IsTrue(training); prepared = true;
        };
        CareerRuntime.CareerPreparing += listener;
        try { shift.StartShift(); Assert.IsTrue(prepared); Assert.IsTrue(shift.IsShiftActive); }
        finally { CareerRuntime.CareerPreparing -= listener; }
    }
    [Test] public void AbortedFourthShiftDoesNotRaiseCompletionOrAdvance()
    {
        var shift = root.AddComponent<ShiftManager>(); shift.SetShiftNumber(3);
        int completed = 0; shift.CareerCompleted += () => completed++;
        shift.StartShift(); shift.EndShift(completed: false);
        Assert.AreEqual(0, completed); Assert.IsFalse(shift.CareerComplete);
        Assert.AreEqual(3, shift.CompletedShifts); Assert.AreEqual(4, shift.UpcomingShiftNumber);
        shift.StartShift(); shift.EndShift();
        Assert.AreEqual(1, completed); Assert.IsTrue(shift.CareerComplete);
        shift.StartShift(); Assert.IsFalse(shift.IsShiftActive);
    }
    [Test] public void TrainingSpeechBubbleHasNoCanvasOrText()
    {
        var bubble = SpeechBubble.Create(root.transform, 2f);
        try
        {
            Assert.IsEmpty(bubble.GetComponentsInChildren<TMP_Text>(true));
            Assert.IsEmpty(bubble.GetComponentsInChildren<Canvas>(true));
            bubble.Show("No visual output"); bubble.ShowChoices("Question", new[] { "Answer" }, 0);
        }
        finally { Object.DestroyImmediate(bubble.gameObject); }
    }
    [Test] public void PresenceFacadeHonorsBenchmarkSimulation()
    {
        Assert.IsTrue(Edition.ShouldKarenAppear(1));
        CareerRuntime.GameplaySimulation = true;
        Assert.IsFalse(Edition.ShouldKarenAppear(1)); Assert.IsTrue(Edition.ShouldKarenAppear(2));
        Edition.EditorOverride = KehaiEdition.Testing;
        Assert.IsFalse(Edition.ShouldKarenAppear(1)); Assert.IsTrue(Edition.ShouldKarenAppear(4));
    }
    [Test] public void TrainingAudioIsSilentEvenOutsideBatchMode()
    {
        Assert.AreEqual(0f, SoundSettings.Audible(1f));
        Assert.IsFalse(Edition.ShowUI); Assert.IsFalse(Edition.AllowDebugTools); Assert.IsFalse(Edition.RecordReplays);
    }
    [Test] public void TrainingSceneStripPreservesCollisionAndLightButRemovesText()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var fixture = new GameObject("Strip fixture"); SceneManager.MoveGameObjectToScene(fixture, scene);
        fixture.SetActive(false); fixture.AddComponent<BoxCollider>(); fixture.AddComponent<Light>();
        var ui = new GameObject("Text", typeof(RectTransform)); ui.transform.SetParent(fixture.transform);
        ui.AddComponent<Canvas>(); ui.AddComponent<TextMeshProUGUI>();
        try
        {
            EditionBuildProcessor.StripScene(scene, KehaiEdition.Training);
            Assert.IsEmpty(fixture.GetComponentsInChildren<TMP_Text>(true));
            Assert.IsEmpty(fixture.GetComponentsInChildren<Canvas>(true));
            Assert.IsNotNull(fixture.GetComponent<BoxCollider>());
            Assert.IsTrue(fixture.GetComponent<Light>().enabled);
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
    [Test] public void GameplayStripRemovesDiagnosticsAndTestingKeepsThem()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var fixture = new GameObject("Debug fixture"); SceneManager.MoveGameObjectToScene(fixture, scene);
        fixture.SetActive(false); fixture.AddComponent<KarenDebugOverlay>();
        try
        {
            EditionBuildProcessor.StripScene(scene, KehaiEdition.Testing);
            Assert.IsNotNull(fixture.GetComponent<KarenDebugOverlay>());
            EditionBuildProcessor.StripScene(scene, KehaiEdition.Gameplay);
            Assert.IsNull(fixture.GetComponent<KarenDebugOverlay>());
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}
