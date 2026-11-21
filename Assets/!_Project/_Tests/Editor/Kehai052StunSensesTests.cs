using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

// 0.5.2, C9.10 (the owner's item 4): a hit stuns her body and her senses. While stunned she sees
// nothing (her awareness only drains, no sighting or entry is remembered) and hears nothing (the
// noises made meanwhile are dropped). The player's protection stays: leaving the hiding place she
// saw you enter still takes her catch away.
public class Kehai052StunSensesTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    SightSensor eye;
    PlayerPresence player;

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        eye = new GameObject("eye").AddComponent<SightSensor>();
        KarenBody.ConfigureEye(eye, new KarenConfig());
        eye.transform.position = new Vector3(0f, 1.85f, 0f);
        var person = new GameObject("employee");
        var controller = person.AddComponent<CharacterController>();
        controller.radius = 0.5f;
        controller.height = 2f;
        controller.center = Vector3.up;
        player = person.AddComponent<PlayerPresence>();
        typeof(PlayerPresence).GetMethod("Awake", Private).Invoke(player, null);
    }

    [TearDown]
    public void Cleanup()
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            Object.DestroyImmediate(go);
    }

    void At(Vector3 feet)
    {
        player.transform.position = feet;
        Physics.SyncTransforms();
    }

    [Test]
    public void Blind_SeesNothing_AndHerAwarenessOnlyDrains()
    {
        At(new Vector3(0f, 0f, 5f));
        for (int i = 0; i < 10; i++) eye.Tick(0.1f);
        Assert.Greater(eye.Awareness, 0.1f, "awake, she sees you in the open");
        float before = eye.Awareness;
        for (int i = 0; i < 20; i++) eye.Blind(0.1f);
        Assert.AreEqual(0f, eye.DetectNow, "stunned, nothing is seen this frame");
        Assert.AreEqual(0f, eye.Exposure);
        Assert.Less(eye.Awareness, before, "stunned, her awareness drains");
        for (int i = 0; i < 10; i++) eye.Tick(0.1f);
        Assert.Greater(eye.DetectNow, 0f, "awake again, she sees you");
    }

    [Test]
    public void Blind_KeepsTheProtection_LeavingCancelsHerCatch()
    {
        // Interior z 5 to 7 (as in Kehai051HidingTests): seen beside it, then inside it.
        HidingPlace.Author(new GameObject("cabinet interior"), new Bounds(new Vector3(0f, 1.5f, 6f), new Vector3(4f, 3f, 2f)));
        At(new Vector3(0f, 0f, 4.5f));
        for (int i = 0; i < 5; i++) eye.Tick(0.05f);
        At(new Vector3(0f, 0f, 6f));
        eye.Tick(0.05f);
        Assert.IsTrue(eye.HiddenEntry.Active, "she saw you go in");
        Assert.IsTrue(eye.HiddenEntry.CanCatch(new Vector3(0f, 0f, 5f)), "close by, she could catch you");
        // Stunned, you step out; then back in, still while she's stunned.
        At(new Vector3(0f, 0f, 2f));
        eye.Blind(0.05f);
        At(new Vector3(0f, 0f, 6f));
        eye.Blind(0.05f);
        Assert.IsFalse(eye.HiddenEntry.CanCatch(new Vector3(0f, 0f, 5f)), "leaving cancels her catch, even unseen");
    }

    [Test]
    public void Stunned_TheNoisesMadeMeanwhileAreDropped()
    {
        var senses = new Sensorium(null, null) { UseTraces = false, UseTestimony = false, UseNegative = false };
        try
        {
            NoiseBus.Emit(new Vector3(0f, 0f, 3f), 0.9f, NoiseKind.Sprint, NoiseAuthor.Player);
            senses.CollectNothing();
            Assert.AreEqual(0, senses.Positive.Count);
            // Awake again: the noise from during the stun isn't heard late.
            senses.Collect(Vector3.zero, eye, null);
            Assert.AreEqual(0, senses.Positive.Count, "a noise made while she was stunned is never heard");
        }
        finally { senses.Dispose(); }
    }
}
