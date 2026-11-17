using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

public class Kehai051StunTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    readonly List<GameObject> objects = new List<GameObject>();
    KarenStun stun;

    GameObject New(string name)
    {
        var go = new GameObject(name);
        objects.Add(go);
        return go;
    }

    Item Loose(float mass = 1f)
    {
        var go = New("loose item without an audio clip");
        go.AddComponent<BoxCollider>().size = Vector3.one * .06f;
        var rigid = go.AddComponent<Rigidbody>();
        rigid.useGravity = false;
        rigid.mass = mass;
        var item = go.AddComponent<Item>();
        if (!Application.isPlaying) Call(item, "Awake");
        return item;
    }

    static void Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);

    [SetUp]
    public void Setup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        stun = New("impact state").AddComponent<KarenStun>();
    }

    [TearDown]
    public void Cleanup()
    {
        GamePause.Set(false);
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear();
    }

    [Test]
    public void NormalClosingSpeedAndMass_ControlDuration_AndInvalidInputsCannotStun()
    {
        Assert.Zero(KarenStun.DurationFor(1.49f, 5f));
        Assert.Zero(KarenStun.DurationFor(1.9f, 1f));
        Assert.Zero(KarenStun.DurationFor(.5f, 5f), "a fast tangential graze has little normal closing speed");
        Assert.Greater(KarenStun.DurationFor(8f, 1f), KarenStun.DurationFor(3f, 1f));
        Assert.Greater(KarenStun.DurationFor(8f, 2f), KarenStun.DurationFor(8f, .2f));
        Assert.AreEqual(KarenStun.MaximumDuration, KarenStun.DurationFor(1000f, 1000f));
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f })
        {
            Assert.Zero(KarenStun.DurationFor(invalid, 1f));
            Assert.Zero(KarenStun.DurationFor(8f, invalid));
        }
    }

    [Test]
    public void OnlyMovingLooseItemsCount_RegardlessOfTheirAudio()
    {
        Item item = Loose();
        Assert.IsNull(item.impactSound);
        item.isCarried = true;
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f));
        item.isCarried = false; item.isOnShelf = true;
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f));
        item.isOnShelf = false;
        item.GetComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Discrete;
        item.GetComponent<Rigidbody>().isKinematic = true;
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f));
        Rethrow(item, NoiseAuthor.Karen);
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f), "her own items never stun her");
        Rethrow(item, NoiseAuthor.Player);
        Assert.IsFalse(stun.TryImpact(item, 8f, 0f), "walking into resting clutter cannot stun Karen");
        Rethrow(item, NoiseAuthor.Player);
        Assert.IsFalse(stun.TryImpact(item, 8f, float.NaN));
        Rethrow(item, NoiseAuthor.Player);
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
        Assert.IsTrue(stun.IsStunned);
    }

    // The player picks it up and lets go of it again: a new release.
    void Rethrow(Item item, NoiseAuthor by)
    {
        item.SetCarried(true, New("hand").transform);
        item.SetCarried(false, null);
        item.lastAuthor = by;
    }

    // 0.5.1: any touch uses up the release, so clutter she shoves or a graze can't bounce back
    // off a wall and stun her, and a weak contact never stuns at all.
    [Test]
    public void WeakContactsAndShovedClutter_NeverStun_AndUseUpTheirRelease()
    {
        Item item = Loose();
        Assert.IsFalse(stun.TryImpact(item, 1.2f, 1.2f), "a weak contact is below the speed floor");
        Assert.IsFalse(stun.TryImpact(item, 9f, 9f), "the same release came back off a wall");
        Assert.IsFalse(stun.IsStunned);

        Item graze = Loose(.2f);
        Assert.IsFalse(stun.TryImpact(graze, 1.6f, 6f), "fast but glancing: little closing speed, a light item");
        Assert.IsFalse(stun.IsStunned);

        Item kicked = Loose();
        kicked.lastAuthor = NoiseAuthor.Karen;   // Item.OnCollisionEnter: she walked into it
        Assert.IsFalse(stun.TryImpact(kicked, 9f, 9f));
        Rethrow(kicked, NoiseAuthor.Player);
        Assert.IsTrue(stun.TryImpact(kicked, 9f, 9f), "thrown again by you, it counts");
    }

    // A stronger valid impact gives a longer stun, and none goes past the cap.
    [Test]
    public void StrongerImpacts_StunLonger_UpToTheCap()
    {
        float light = KarenStun.DurationFor(4f, .3f), medium = KarenStun.DurationFor(8f, 1f), heavy = KarenStun.DurationFor(13f, 3f);
        Assert.Greater(light, 0f);
        Assert.Greater(medium, light);
        Assert.Greater(heavy, medium);
        Assert.LessOrEqual(heavy, KarenStun.MaximumDuration);
        Assert.GreaterOrEqual(light, .25f, "the shortest stun is a quarter second");
    }

    [Test]
    public void BouncesAndMultipleColliders_CannotExtendOneRelease_ButPickupAndRethrowCan()
    {
        Item item = Loose();
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
        stun.Step(.25f);
        float remaining = stun.Remaining;
        Assert.IsFalse(stun.TryImpact(item, 20f, 20f));
        Assert.AreEqual(remaining, stun.Remaining);
        stun.Step(10f);
        stun.Step(KarenStun.RecoveryGrace);
        Assert.IsFalse(stun.TryImpact(item, 20f, 20f), "the same release remains consumed after recovery");
        item.SetCarried(true, New("hand").transform);
        item.SetCarried(false, null);
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
    }

    [Test]
    public void FreshItemsCannotLockHerDown_PauseFreezesTime_AndGraceConsumesContacts()
    {
        Assert.IsTrue(stun.TryImpact(Loose(5f), 20f, 20f));
        GamePause.Set(true);
        stun.Step(100f);
        Assert.AreEqual(KarenStun.MaximumDuration, stun.Remaining);
        Assert.IsFalse(stun.TryImpact(Loose(), 8f, 8f));
        GamePause.Set(false);
        for (int i = 0; i < 5; i++)
        {
            stun.Step(.5f);
            Assert.IsTrue(stun.TryImpact(Loose(5f), 20f, 20f));
            Assert.LessOrEqual(stun.Remaining, KarenStun.MaximumDuration - (i + 1) * .5f);
        }
        stun.Step(.5f);
        Assert.IsFalse(stun.IsStunned, "the entire burst ends within the original three-second window");
        Item graceContact = Loose();
        Assert.IsFalse(stun.TryImpact(graceContact, 8f, 8f));
        stun.Step(KarenStun.RecoveryGrace);
        Assert.IsFalse(stun.TryImpact(graceContact, 8f, 8f));
        Assert.IsTrue(stun.TryImpact(Loose(), 8f, 8f));
    }

    [Test]
    public void ResetAndDisable_ClearTimerAndDedup_ForANewShift()
    {
        Item item = Loose();
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
        stun.ResetState();
        Assert.IsFalse(stun.IsStunned);
        Assert.IsFalse(stun.HitThisFrame);
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
        stun.enabled = false;
        Call(stun, "OnDisable");
        Assert.IsFalse(stun.IsStunned);
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f));
        stun.enabled = true;
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
    }

    [Test]
    public void CatchWaitsForAllPhysicsCallbacks_AndStayCanCatchAfterRecovery()
    {
        var go = New("Karen touch arbitration");
        var body = go.AddComponent<KarenBody>();
        body.GetComponent<NavMeshAgent>().enabled = false;
        var state = go.AddComponent<KarenStun>();
        typeof(KarenBody).GetProperty("Stun").SetValue(body, state);
        Call(state, "Awake");
        var person = New("employee").AddComponent<CharacterController>();
        int catches = 0;
        body.Touched += _ => catches++;
        Call(body, "OnTriggerEnter", person);
        Assert.Zero(catches, "trigger callbacks must not catch immediately");
        Assert.IsTrue(state.TryImpact(Loose(), 8f, 8f));
        Call(body, "LateUpdate");
        Assert.Zero(catches);
        Call(body, "OnTriggerStay", person);
        Call(body, "LateUpdate");
        Assert.Zero(catches, "remaining inside her trigger is safe while stunned");
        state.ResetState();
        Call(body, "OnTriggerStay", person);
        Call(body, "LateUpdate");
        Assert.AreEqual(1, catches, "recovery does not require leaving and re-entering the trigger");
        Call(body, "OnTriggerEnter", person);
        body.ClearPendingTouches();
        Call(body, "LateUpdate");
        Assert.AreEqual(1, catches, "a shift reset must discard queued contact");
    }
}
