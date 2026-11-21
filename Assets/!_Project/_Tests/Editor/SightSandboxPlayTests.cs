using System;
using System.Collections;
using Kehai.Karen;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 0.5.2, Phase 11: the whole sight sandbox, built as Play starts, with her real body. Like the
// stun's play-mode check (Kehai051StunPhysicsTests), it has no edit-mode [SetUp], drives its
// steps by hand so a failure can't skip the clean-up, always exits play mode, and reports a
// failure back in edit mode through SessionState. Her footsteps are silenced.
public class SightSandboxPlayTests
{
    const string FailureKey = "SightSandboxPlayTests.failure";

    [UnityTest]
    public IEnumerator SightSandbox_BuildsFromNothing_AndHerControlsWork()
    {
        SessionState.EraseString(FailureKey);
        yield return new EnterPlayMode();

        var root = new GameObject("Sight sandbox (test)");
        Exception failure = null;
        IEnumerator steps = null;
        try { steps = Checks(root.AddComponent<SightSandbox>()); }
        catch (Exception e) { failure = e; }
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

        Object.Destroy(root);
        if (failure != null) SessionState.SetString(FailureKey, failure.GetType().Name + ": " + failure.Message);
        yield return null;
        yield return new ExitPlayMode();

        string failed = SessionState.GetString(FailureKey, "");
        SessionState.EraseString(FailureKey);
        if (!string.IsNullOrEmpty(failed)) Assert.Fail(failed);
    }

    static void Teleport(PlayerPresence player, Vector3 feet)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.position = feet + Vector3.up * 0.02f;
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    static IEnumerator Checks(SightSandbox sandbox)
    {
        yield return null;
        Assert.IsTrue(sandbox.Built, "the sandbox builds itself as Play starts");
        SandboxKaren karen = sandbox.Karen;
        Assert.IsNotNull(karen);
        karen.Body.Silent = true;
        Assert.IsTrue(karen.Body.Agent.isOnNavMesh, "the sandbox's own NavMesh carries her");
        Assert.IsNotNull(sandbox.Player, "a player is built when there is none");
        Assert.AreEqual(8, sandbox.GetComponentsInChildren<SandboxButton>().Length, "two panels of four buttons");
        Assert.AreEqual(SandboxKaren.State.Frozen, karen.Current, "she starts frozen");

        // Her eye is the game's: the same numbers KarenBody.ConfigureEye gives the store's Karen.
        var config = new KarenConfig();
        Assert.AreEqual(config.sightRange, karen.Body.Sight.range);
        Assert.AreEqual(config.sightFov, karen.Body.Sight.fieldOfView);
        Assert.AreEqual(config.sightGain, karen.Body.Sight.gain);

        // Start (F5, as the panel's START): she walks her loop.
        Vector3 spot = karen.Body.transform.position;
        Assert.IsTrue(sandbox.Press(KeyCode.F5));
        yield return Wait(2f);
        Assert.Greater(Vector3.Distance(spot, karen.Body.transform.position), 0.5f, "Start sets her walking");

        // Freeze (F7): she stays put.
        Assert.IsTrue(sandbox.Press(KeyCode.F7));
        yield return Wait(0.3f);
        Vector3 held = karen.Body.transform.position;
        yield return Wait(0.7f);
        Assert.Less(Vector3.Distance(held, karen.Body.transform.position), 0.1f, "Freeze stops her");

        // F4 hides every indicator and shows them again (C11.4).
        Assert.IsTrue(sandbox.Indicators.AnyShown);
        sandbox.Press(KeyCode.F4);
        Assert.IsFalse(sandbox.Indicators.AnyShown, "F4 hides the indicators");
        sandbox.Press(KeyCode.F4);
        Assert.IsTrue(sandbox.Indicators.AnyShown, "F4 again shows them");

        // In the open, in front of her: her awareness fills. (Restart is F6.)
        Assert.IsTrue(sandbox.Press(KeyCode.F6));
        yield return null;
        Assert.Less(Vector3.Distance(spot, karen.Body.transform.position), 0.3f, "Restart puts her back on her spot");
        Assert.AreEqual(0f, karen.Body.Sight.Awareness, "Restart clears her awareness");
        Assert.AreEqual(SandboxKaren.State.Frozen, karen.Current, "Restart leaves her frozen");
        Teleport(sandbox.Player, spot + karen.Body.transform.forward * 5f);
        yield return Wait(1.5f);
        Assert.Greater(karen.Body.Sight.Awareness, karen.Body.Sight.glimpseAt, "standing in the open, 5 m in front of her, you're seen");

        // The panel's buttons do what the keys do (C11.3): RESTART clears her, START and FREEZE.
        SandboxButton Button(string label) => System.Array.Find(sandbox.GetComponentsInChildren<SandboxButton>(), b => b.label == label);
        Button("RESTART").Interact(null);
        Assert.AreEqual(0f, karen.Body.Sight.Awareness, "the panel's RESTART clears her awareness");
        Assert.AreEqual(SandboxKaren.State.Frozen, karen.Current);
        Button("START").Interact(null);
        Assert.AreEqual(SandboxKaren.State.Moving, karen.Current, "the panel's START sets her going");
        Button("FREEZE").Interact(null);
        Assert.AreEqual(SandboxKaren.State.Frozen, karen.Current, "the panel's FREEZE stops her");

        // Inside the fridge: hidden, her awareness drains and nothing more is seen.
        karen.Restart();
        Vector3 inside = SightSandbox.FridgeFront + Vector3.right * (SandboxKit.FridgeInside.z * 0.5f);
        Teleport(sandbox.Player, inside);
        yield return Wait(1f);
        Assert.IsFalse(karen.Body.Sight.SeesNow, "hidden in the fridge, she sees nothing");
        Assert.AreEqual(0f, karen.Body.Sight.Awareness, "hidden from the start, her awareness stays at zero");
    }
}
