using System.Linq;
using Kehai.Karen;
using Kehai.Sandbox;
using Kehai.Store;
using NUnit.Framework;
using UnityEngine;

// 0.5.2, Phase 11: the pursuit sandbox's switches and layout, in edit mode. The whole room with
// her real brain is checked in play mode (PursuitSandboxPlayTests).
public class PursuitSandboxTests
{
    [Test]
    public void NoTricks_RefusesEveryTrick_AndOnlyTricks()
    {
        var config = new KarenConfig { sandboxNoTricks = true };
        var c = new KarenContext { Config = config };
        int refused = 0;
        foreach (Tactic t in TacticLibrary.All.Where(t => !t.Draft && t.Tier > 0))
        {
            Assert.IsFalse(HtnPlanner.Gated(t, c, out string why), t.Id);
            Assert.AreEqual("sandbox: tricks off", why, t.Id);
            refused++;
        }
        Assert.Greater(refused, 10, "every live trick is refused");
        // Her movement tactics are never refused by the switch (other gates may still apply).
        foreach (Tactic t in TacticLibrary.All.Where(t => !t.Draft && t.Tier == 0))
        {
            try { HtnPlanner.Gated(t, c, out string why); Assert.AreNotEqual("sandbox: tricks off", why, t.Id); }
            catch (System.Exception e) when (!(e is AssertionException)) { /* a later gate needs a whole context: not this switch */ }
        }
    }

    [Test]
    public void TheSwitches_AreOffInTheGame()
    {
        var config = new KarenConfig();
        Assert.IsFalse(config.sandboxNoTricks);
        Assert.IsFalse(config.sandboxCatchFreezes);
    }

    [Test]
    public void TheRoom_SitsOnHerFloor_AndKeepsEverythingInside()
    {
        // Her map counts the sales floor as hers; the street (where the sight sandbox sits) is not.
        foreach (Vector3 local in new[] { PursuitSandbox.KarenSpot, PursuitSandbox.PlayerStart, PursuitSandbox.FridgeFront, PursuitSandbox.RackCentre })
            Assert.AreEqual("Sales floor", StoreMap.AreaAt(PursuitSandbox.World(local)), $"{local} must be on the sales floor");
        Assert.LessOrEqual(PursuitSandbox.PanelHeight, 1.6f, "the panels must be at eye level or lower");
        float hx = PursuitSandbox.RoomSize.x * 0.5f - SandboxKit.WallThickness * 0.5f;
        float back = PursuitSandbox.FridgeFront.x + SandboxKit.FridgeInside.z + SandboxKit.FridgeWall;
        Assert.Less(back, hx, "the fridge's back must stand inside the east wall");
        Assert.AreEqual(13, PursuitSandbox.Shelves().Count(), "two rows of seven, one gap in the north row");
    }
}
