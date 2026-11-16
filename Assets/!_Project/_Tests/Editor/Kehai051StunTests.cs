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
        item.SetCarried(false, null);
        item.lastAuthor = NoiseAuthor.Karen;
        Assert.IsFalse(stun.TryImpact(item, 8f, 8f));
        item.lastAuthor = NoiseAuthor.Player;
        Assert.IsFalse(stun.TryImpact(item, 8f, 0f), "walking into resting clutter cannot stun Karen");
        Assert.IsFalse(stun.TryImpact(item, 8f, float.NaN));
        Assert.IsTrue(stun.TryImpact(item, 8f, 8f));
        Assert.IsTrue(stun.IsStunned);
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

    [UnityTest]
    public IEnumerator FastSmallItemsAreBlockedByWallsAndHerSolidBody_AndNavigationRecovers()
    {
        yield return new EnterPlayMode();
        var root = new GameObject("native impact regression");
        var settings = NavMesh.GetSettingsByIndex(0);
        var floor = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.Translate(Vector3.down * .1f),
            size = new Vector3(30f, .2f, 30f), area = 0
        };
        var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(30f, 5f, 30f)), Vector3.zero, Quaternion.identity);
        Assert.IsNotNull(data);
        var mesh = NavMesh.AddNavMeshData(data);
        try
        {
            var body = KarenBody.Build(root.transform, new KarenConfig(), Vector3.zero);
            body.Silent = true;
            yield return null;
            Assert.IsTrue(body.Agent.isOnNavMesh);
            Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, body.GetComponent<Rigidbody>().collisionDetectionMode);
            Assert.IsTrue(System.Array.Exists(body.GetComponents<CapsuleCollider>(), c => c.isTrigger));
            Assert.IsTrue(System.Array.Exists(body.GetComponents<CapsuleCollider>(), c => !c.isTrigger));

            var wall = new GameObject("occluding wall"); wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(0f, 1f, -1f);
            wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2f, .1f);
            Item blocked = Loose(); blocked.transform.SetParent(root.transform);
            blocked.transform.position = new Vector3(0f, 1f, -2.2f);
            blocked.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 80f;
            Physics.SyncTransforms();
            for (int i = 0; i < 5; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.IsFalse(body.IsStunned, "a wall-occluded throw cannot reach her");
            Assert.Less(blocked.transform.position.z, -.8f, "CCD must stop the small fast item at the wall");
            Object.Destroy(wall); Object.Destroy(blocked.gameObject);
            yield return null;

            Item direct = Loose(); direct.transform.SetParent(root.transform);
            direct.transform.position = new Vector3(0f, 1f, -2.2f);
            direct.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * 80f;
            Physics.SyncTransforms();
            for (int i = 0; i < 5 && !body.IsStunned; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.IsTrue(body.IsStunned, "fast small items without audio must collide and stun");
            Assert.Less(direct.transform.position.z, .45f, "the projectile must not pass through her physical body");
            Vector3 stopped = body.Position;
            body.MoveTo(Vector3.right * 6f, KarenBody.Pace.Run);
            body.BurstToward(Vector3.right * 6f, 20f, 2f, 6f);
            body.Carry(direct);
            for (int i = 0; i < 3; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.Less(Vector3.Distance(stopped, body.Position), .03f);
            Assert.IsNull(body.Carried);
            Assert.IsTrue(body.Agent.isStopped);

            body.Stun.Step(KarenStun.MaximumDuration);
            yield return null;
            body.BurstToward(Vector3.right * 6f, 20f, 2f, 6f);
            yield return null;
            Assert.Greater(body.Agent.acceleration, 100f);
            body.Stun.Step(KarenStun.RecoveryGrace);
            Item second = Loose(); second.transform.SetParent(root.transform);
            Assert.IsTrue(body.Stun.TryImpact(second, 8f, 8f));
            Assert.AreEqual(14f, body.Agent.acceleration, "an impact cancels an active dash acceleration");
            Assert.IsTrue(body.Agent.isStopped);
            Assert.Less(body.Agent.velocity.sqrMagnitude, .001f);
            body.Stun.Step(KarenStun.MaximumDuration);
            yield return null;
            body.MoveTo(Vector3.right * 6f, KarenBody.Pace.Run);
            Assert.IsFalse(body.Agent.isStopped);
            for (int i = 0; i < 8; i++) { yield return new WaitForFixedUpdate(); yield return null; }
            Assert.Greater(Vector3.Distance(stopped, body.Position), .05f, "a fresh plan must move again after recovery");
        }
        finally
        {
            Object.Destroy(root);
            mesh.Remove();
            Object.DestroyImmediate(data);
        }
        yield return null;
        yield return new ExitPlayMode();
    }
}
