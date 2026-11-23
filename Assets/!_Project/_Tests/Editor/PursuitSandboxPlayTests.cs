using System;
using System.Collections;
using Kehai.Karen;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 0.5.2, Phase 11: the pursuit sandbox with her real brain, in play mode. The same shape as the
// other play-mode checks: no edit-mode [SetUp], steps driven by hand, always exits play mode, a
// failure carried back through SessionState. Her footsteps are silenced.
public class PursuitSandboxPlayTests
{
    const string FailureKey = "PursuitSandboxPlayTests.failure";

    [UnityTest]
    public IEnumerator PursuitSandbox_HerRealBrainHuntsYou_KeepsOnAfterLosingYou_AndACatchFreezes()
    {
        SessionState.EraseString(FailureKey);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();

        var root = new GameObject("Pursuit sandbox (test)");
        float previousFrameStep = Time.captureDeltaTime;
        SandboxBot.FixFrames();
        Exception failure = null;
        IEnumerator steps = null;
        try { steps = Checks(root.AddComponent<PursuitSandbox>()); }
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
        Time.captureDeltaTime = previousFrameStep;
        if (failure != null) SessionState.SetString(FailureKey, failure.GetType().Name + ": " + failure.Message);
        yield return null;
        yield return new ExitPlayMode();

        string failed = SessionState.GetString(FailureKey, "");
        SessionState.EraseString(FailureKey);
        if (!string.IsNullOrEmpty(failed)) Assert.Fail(failed);
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    static IEnumerator Checks(PursuitSandbox sandbox)
    {
        float until = Time.time + 10f;
        while (!sandbox.Ready && Time.time < until) yield return null;
        Assert.IsTrue(sandbox.Ready, "her brain starts and her shift begins");
        KarenBrain brain = sandbox.Brain;
        brain.Body.Silent = true;
        Assert.IsTrue(brain.ShiftActive);
        Assert.IsTrue(brain.Body.Agent.isOnNavMesh, "the sandbox's NavMesh carries her");
        Assert.Greater(brain.Map.CellCount, 0, "her map is built from the sandbox's floor");
        Assert.IsTrue(brain.config.sandboxNoTricks && brain.config.sandboxCatchFreezes && brain.config.hunter);
        Assert.AreEqual(sandbox.LedgerPath, brain.Ledger.SavePath, "her Ledger is the sandbox's temporary file, never your save");
        Assert.IsFalse(sandbox.Running, "she waits, frozen, for Start");
        var bot = new SandboxBot(sandbox.Player.GetComponent<PlayerMotor>());
        var eye = brain.Body.Sight;

        // Hold her gaze for the initial sighting. Otherwise her first patrol decision can
        // turn her away while the eye is still accumulating awareness.
        Vector3 spot = brain.Body.transform.position;
        bot.Teleport(spot + brain.Body.transform.forward * 6f);
        until = Time.time + 6f;
        while (eye.Awareness < eye.confirmAt && Time.time < until)
        {
            eye.Tick(Time.deltaTime);
            yield return null;
        }
        Assert.GreaterOrEqual(eye.Awareness, eye.confirmAt, "the initial setup gives her a confirmed sighting");
        sandbox.StartRunning();
        until = Time.time + 6f;
        while (!brain.IsChasing && Time.time < until) yield return null;
        Assert.IsTrue(brain.IsChasing, "seen in the open, she hunts");

        // Jump straight to cover, far from the last sighting. A hidden entry she watched
        // would start a different behaviour. The recorded corner check covers the walk around shelving.
        Vector3 fridge = PursuitSandbox.FridgeFront + Vector3.right * (SandboxKit.FridgeInside.z * .5f);
        bot.Teleport(PursuitSandbox.World(fridge));
        yield return Wait(1f);
        Assert.IsFalse(eye.SeesNow, "the fridge hides the player");
        Assert.IsTrue(brain.IsChasing && brain.Pursuit.Active(Time.time), "after losing you she keeps on, inside the 60 s window");
        Assert.IsTrue(brain.CurrentPlan == null || brain.CurrentPlan.Tactic == null || brain.CurrentPlan.Tactic.Tier == 0, "only her movement tactics run");

        // You walk into her: the catch freezes the test instead of ending the shift.
        bot.Teleport(brain.Body.transform.position + Vector3.up * 0.02f);
        until = Time.time + 3f;
        while (!sandbox.Caught && Time.time < until) yield return null;
        Assert.IsTrue(sandbox.Caught, "touching her is a catch");
        Assert.IsFalse(sandbox.Running, "a catch freezes the test");
        Assert.IsTrue(brain.ShiftActive, "the shift doesn't end in a sandbox");

        // Restart: a new shift, back on her spot, frozen, no hunt.
        sandbox.Restart();
        yield return null;
        Assert.IsFalse(sandbox.Caught);
        Assert.IsFalse(brain.IsChasing);
        Assert.Less(Vector3.Distance(spot, brain.Body.transform.position), 0.5f, "Restart puts her back on her spot");

        // A hit stuns her senses too (C9.10): in plain view while stunned, she gains nothing;
        // when the stun ends, she sees you.
        sandbox.StartRunning();
        bot.Teleport(spot + brain.Body.transform.forward * 6f);
        Assert.IsTrue(brain.Body.Stun.TryImpact(Thrown(sandbox.transform), 9f, 9f), "a fast throw stuns her");
        yield return Wait(0.8f);
        Assert.IsTrue(brain.Body.IsStunned);
        Assert.AreEqual(0f, brain.Body.Sight.Awareness, "stunned, she sees nothing, though you stand in plain view");
        until = Time.time + 6f;
        while (brain.Body.Sight.Awareness <= brain.Body.Sight.glimpseAt && Time.time < until) yield return null;
        Assert.IsFalse(brain.Body.IsStunned);
        Assert.Greater(brain.Body.Sight.Awareness, brain.Body.Sight.glimpseAt, "awake again, she sees you");
    }

    static Item Thrown(Transform parent)
    {
        var go = new GameObject("thrown item (test)");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 20f;
        go.AddComponent<BoxCollider>();
        go.AddComponent<Rigidbody>().useGravity = false;
        var item = go.AddComponent<Item>();
        item.lastAuthor = NoiseAuthor.Player;
        return item;
    }
}
