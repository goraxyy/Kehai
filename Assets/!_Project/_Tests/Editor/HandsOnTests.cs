using System.Linq;
using Kehai.Karen;
using NUnit.Framework;
using UnityEngine;

// Karen 0.3.0, rule 9: a hands-on trick happens at her hands (build plan C1.3). The effect lands
// only while her body is within 2 m of the spot it touches, measured across the floor; the
// building's tricks stay remote.
public class HandsOnTests
{
    static KarenContext Context() =>
        new KarenContext { Config = new KarenConfig { seed = 7 }, Rng = new KarenRng(7) };

    [Test]
    public void TheHandsOnTricks_AreTheSevenThePlanNames()
    {
        var handsOn = TacticLibrary.All.Where(t => t.IsHandsOn).Select(t => t.Id).OrderBy(id => id).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "bin_tamper", "camera_bolt_on", "door_lock", "overtime", "shelf_sweep", "spill", "tool_theft",
        }, handsOn);
    }

    [Test]
    public void TheBuildingsTricks_StayRemote()
    {
        foreach (string id in new[] { "blackout", "fog", "phantom_chime" })
            Assert.IsFalse(TacticLibrary.Get(id).IsHandsOn, id);
    }

    [Test]
    public void AHandsOnStep_OutOfReach_Fails_AndDoesNothing()
    {
        int landed = 0;
        var step = new HandsOn("refill a bin", _ => new Vector3(3f, 0f, 0f), _ => landed++)
        {
            Hands = _ => Vector3.zero
        };
        Assert.AreEqual(Status.Failure, step.Run(Context(), 0.1f));
        Assert.AreEqual(0, landed);
    }

    [Test]
    public void AHandsOnStep_WithinReach_Lands()
    {
        int landed = 0;
        var step = new HandsOn("refill a bin", _ => new Vector3(1.9f, 0f, 0f), _ => landed++)
        {
            Hands = _ => Vector3.zero
        };
        Assert.AreEqual(Status.Success, step.Run(Context(), 0.1f));
        Assert.AreEqual(1, landed);
    }

    // A bin's lid or a top shelf is above her, not away from her: height doesn't count.
    [Test]
    public void Reach_IsMeasuredAcrossTheFloor()
    {
        int landed = 0;
        var step = new HandsOn("a top slot", _ => new Vector3(1.5f, 2.4f, 0f), _ => landed++)
        {
            Hands = _ => Vector3.zero
        };
        Assert.AreEqual(Status.Success, step.Run(Context(), 0.1f));
        Assert.AreEqual(1.5f, FairnessGuard.Flat(Vector3.zero, new Vector3(1.5f, 2.4f, 0f)), 1e-4f);
    }

    [Test]
    public void TheAudit_CountsAHandsOnEffectThatLandsFarAway()
    {
        var go = new GameObject("KarenBody (test)");
        try
        {
            KarenBody body = go.AddComponent<KarenBody>();
            go.transform.position = Vector3.zero;
            int before = FairnessGuard.Violations;
            Assert.IsTrue(FairnessGuard.CheckHandsOn("spill", body, new Vector3(0f, 0f, 1.8f)));
            Assert.AreEqual(before, FairnessGuard.Violations);
            Assert.IsFalse(FairnessGuard.CheckHandsOn("spill", body, new Vector3(0f, 0f, 6f)));
            Assert.AreEqual(before + 1, FairnessGuard.Violations);
            Assert.IsTrue(FairnessGuard.CheckHandsOn("spill", null, new Vector3(50f, 0f, 0f)), "no body, nothing to check");
        }
        finally { Object.DestroyImmediate(go); }
    }
}
