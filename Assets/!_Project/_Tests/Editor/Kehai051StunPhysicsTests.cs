using System;
using System.Collections;
using System.Collections.Generic;
using Kehai.Karen;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// The one play-mode stun check (0.5.1, item 14): real PhysX contacts against her real body.
// Its own class, with no edit-mode [SetUp] (EditorSceneManager.NewScene throws in play mode, and
// the runner re-runs SetUp after the domain reload that entering play mode causes). Whatever
// happens inside, it removes its NavMesh and objects and always yields ExitPlayMode, so play mode
// never stays on for the tests after it; a failure is carried across that exit in SessionState
// and reported once back in edit mode.
public class Kehai051StunPhysicsTests
{
    const string FailureKey = "Kehai051StunPhysicsTests.failure";
    const string RootName = "Kehai051 native impact regression";

    // Faster than any throw the player can make (PlayerInteract.maxThrowSpeed is 13 m/s).
    const float ThrowSpeed = 20f;

    [UnityTest]
    public IEnumerator FastSmallItemsAreBlockedByWallsAndHerSolidBody_AndNavigationRecovers()
    {
        SessionState.EraseString(FailureKey);
        yield return new EnterPlayMode();

        var settings = NavMesh.GetSettingsByIndex(0);
        var floor = new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            transform = Matrix4x4.Translate(Vector3.down * .1f),
            size = new Vector3(30f, .2f, 30f), area = 0
        };
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(30f, 5f, 30f)), Vector3.zero, Quaternion.identity);
        NavMeshDataInstance mesh = data != null ? NavMesh.AddNavMeshData(data) : default;
        var root = new GameObject(RootName);

        // Drive the checks by hand, so an assertion can't skip the clean-up or the exit.
        Exception failure = data == null ? new AssertionException("no NavMesh data built") : null;
        IEnumerator steps = failure == null ? Checks(root) : null;
        while (steps != null)
        {
            object step;
            try
            {
                if (!steps.MoveNext()) break;
                step = steps.Current;
            }
            catch (Exception e)
            {
                failure = e;
                break;
            }
            yield return step;
        }

        GamePause.Set(false);
        Object.Destroy(root);
        if (mesh.valid) mesh.Remove();
        if (data != null) Object.Destroy(data);
        if (failure != null) SessionState.SetString(FailureKey, failure.GetType().Name + ": " + failure.Message);
        yield return null;
        yield return new ExitPlayMode();

        string failed = SessionState.GetString(FailureKey, "");
        SessionState.EraseString(FailureKey);
        if (!string.IsNullOrEmpty(failed)) Assert.Fail(failed);
    }

    static Item Loose(Transform parent, float mass = 1f)
    {
        var go = new GameObject("loose item without an audio clip");
        go.transform.SetParent(parent);
        go.AddComponent<BoxCollider>().size = Vector3.one * .06f;
        var rigid = go.AddComponent<Rigidbody>();
        rigid.useGravity = false;
        rigid.mass = mass;
        var item = go.AddComponent<Item>();
        item.lastAuthor = NoiseAuthor.Player;
        return item;
    }

    static IEnumerator Checks(GameObject root)
    {
        var config = new KarenConfig();
        var body = KarenBody.Build(root.transform, config, Vector3.zero);
        body.Silent = true;
        yield return null;
        Assert.IsTrue(body.Agent.isOnNavMesh);
        Assert.AreEqual(CollisionDetectionMode.ContinuousSpeculative, body.GetComponent<Rigidbody>().collisionDetectionMode);
        CapsuleCollider touch = Array.Find(body.GetComponents<CapsuleCollider>(), c => c.isTrigger);
        CapsuleCollider solid = Array.Find(body.GetComponents<CapsuleCollider>(), c => !c.isTrigger);
        Assert.IsNotNull(touch);
        Assert.IsNotNull(solid);
        Assert.LessOrEqual(solid.radius, touch.radius - KarenBody.TouchMargin + 1e-4f, "whoever reaches her solid body is inside her touch");

        // Her own solid body never blinds her eye.
        Vector3 ahead = body.Sight.Eye + body.transform.forward * 5f;
        Assert.IsTrue(body.Sight.CanSee(ahead), "her eye starts inside her own capsule and must see past it");

        var wall = new GameObject("occluding wall");
        wall.transform.SetParent(root.transform);
        wall.transform.position = new Vector3(0f, 1f, -1f);
        wall.AddComponent<BoxCollider>().size = new Vector3(3f, 2f, .1f);
        Item blocked = Loose(root.transform);
        blocked.transform.position = new Vector3(0f, 1f, -2.2f);
        blocked.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * ThrowSpeed;
        Physics.SyncTransforms();
        yield return For(0.4f);
        Assert.Greater(blocked.transform.position.z, -1.6f, "the throw really flew (physics stepped)");
        Assert.IsFalse(body.IsStunned, "a wall-occluded throw cannot reach her");
        Assert.Less(blocked.transform.position.z, -.9f, "CCD must stop the small fast item at the wall");
        Object.Destroy(wall);
        Object.Destroy(blocked.gameObject);
        yield return null;

        Item direct = Loose(root.transform);
        direct.transform.position = new Vector3(0f, 1f, -2.2f);
        direct.GetComponent<Rigidbody>().linearVelocity = Vector3.forward * ThrowSpeed;
        Physics.SyncTransforms();
        yield return For(0.4f, () => body.IsStunned);
        Assert.IsTrue(body.IsStunned, "fast small items without audio must collide and stun");
        Assert.Less(direct.transform.position.z, solid.radius, "the item must not pass through her physical body");

        // Stunned: no walk, no dash, no hands.
        Vector3 stopped = body.Position;
        body.MoveTo(Vector3.right * 6f, KarenBody.Pace.Run);
        body.BurstToward(Vector3.right * 6f, 20f, 2f, 6f);
        body.Carry(direct);
        yield return For(0.3f);
        Assert.Less(Vector3.Distance(stopped, body.Position), .03f);
        Assert.IsNull(body.Carried);
        Assert.IsTrue(body.Agent.isStopped);

        // Awake again: a dash works, and a fresh hit cancels it.
        body.Stun.Step(KarenStun.MaximumDuration);
        yield return null;
        body.BurstToward(Vector3.right * 6f, 20f, 2f, 6f);
        yield return null;
        Assert.Greater(body.Agent.acceleration, 100f);
        body.Stun.Step(KarenStun.RecoveryGrace);
        Item second = Loose(root.transform);
        Assert.IsTrue(body.Stun.TryImpact(second, 8f, 8f));
        Assert.AreEqual(14f, body.Agent.acceleration, "an impact cancels an active dash acceleration");
        Assert.IsTrue(body.Agent.isStopped);
        Assert.Less(body.Agent.velocity.sqrMagnitude, .001f);

        // And she moves again after it.
        body.Stun.Step(KarenStun.MaximumDuration);
        yield return null;
        body.MoveTo(Vector3.right * 6f, KarenBody.Pace.Run);
        Assert.IsFalse(body.Agent.isStopped);
        yield return For(0.5f);
        Assert.Greater(Vector3.Distance(stopped, body.Position), .05f, "a fresh plan must move again after recovery");
    }

    // Waits in game time, not frames: in batch mode a frame can take well under a millisecond, so
    // ten frames may not hold a single 20 ms physics step (the first version of this test waited
    // ten frames, and its wall half passed without the item ever moving).
    static System.Collections.IEnumerator For(float seconds, System.Func<bool> done = null)
    {
        float until = Time.time + seconds;
        while (Time.time < until && (done == null || !done())) yield return null;
    }
}
