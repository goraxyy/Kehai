using System;
using System.Collections;
using Kehai.Karen;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEditor;
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
        yield return new EnterPlayMode();

        var root = new GameObject("Pursuit sandbox (test)");
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

        // You stand in the open in front of her: she becomes sure and hunts.
        Vector3 spot = brain.Body.transform.position;
        Teleport(sandbox.Player, spot + brain.Body.transform.forward * 6f);
        sandbox.StartRunning();
        until = Time.time + 6f;
        while (!brain.IsChasing && Time.time < until) yield return null;
        Assert.IsTrue(brain.IsChasing, "seen in the open, she hunts");

        // You vanish behind the shelves at the far end: she keeps pursuing for the hunt's window.
        Teleport(sandbox.Player, PursuitSandbox.World(new Vector3(10.5f, 0f, -6.5f)));
        yield return Wait(3f);
        Assert.IsTrue(brain.IsChasing && brain.Pursuit.Active(Time.time), "after losing you she keeps on, inside the 60 s window");
        Assert.IsTrue(brain.CurrentPlan == null || brain.CurrentPlan.Tactic == null || brain.CurrentPlan.Tactic.Tier == 0, "only her movement tactics run");

        // You walk into her: the catch freezes the test instead of ending the shift.
        Teleport(sandbox.Player, brain.Body.transform.position + brain.Body.transform.forward * 0.3f);
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
    }
}
