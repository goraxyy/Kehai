using System.Collections.Generic;
using System.Linq;
using Kehai.Eval;
using Kehai.Karen;
using Kehai.Sandbox;
using NUnit.Framework;
using UnityEngine;

public sealed class TricksSandboxTests
{
    [Test]
    public void StallsDispatchTheSharedCatalogueOnceEachThroughInteractableButtons()
    {
        var root = new GameObject("Trick stalls test");
        try
        {
            var calls = new List<string>();
            TricksSandbox.BuildStalls(root.transform, calls.Add);
            var buttons = root.GetComponentsInChildren<SandboxButton>();
            CollectionAssert.AreEqual(TrickCheck.Tricks, buttons.Select(b => b.name));
            Assert.That(buttons.Select(b => b.name).Distinct().Count(), Is.EqualTo(buttons.Length));
            foreach (var button in buttons)
            {
                Assert.That(button.label, Is.EqualTo(TacticLibrary.Get(button.name).Title));
                Assert.That(button.gameObject.layer, Is.EqualTo(LayerMask.NameToLayer("Interactable")));
                Assert.That(button.GetComponent<Collider>(), Is.Not.Null);
                button.Interact(null);
            }
            CollectionAssert.AreEqual(TrickCheck.Tricks, calls);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // 0.5.2 (C11.11): the stalls stand on the sandbox island's ground, clear of the sight area.
    [Test]
    public void TheStallsStandOnTheSandboxIsland_ClearOfTheSightArea()
    {
        Vector3 c = SandboxIsland.GroundCentre, half = SandboxIsland.GroundSize * 0.5f;
        float width = (TrickCheck.Tricks.Length + 2) / 3 * 1.25f + 2f;   // the stalls and their panel
        Vector3 at = TricksSandbox.StallsAt;
        Assert.That(at.x - width / 2f, Is.GreaterThan(c.x - half.x));
        Assert.That(at.x + width / 2f, Is.LessThan(c.x + half.x));
        Assert.That(at.z, Is.InRange(c.z - half.z + 1f, c.z + half.z - 1f));
        // The sight room's outer walls: 20 x 20 m round SightAt, shifted 2 m south (SightSandbox.BuildRoom).
        Vector3 room = SandboxIsland.SightAt + new Vector3(0f, 0f, -2f);
        Assert.That(Mathf.Abs(at.x - room.x), Is.GreaterThan(10f + width / 2f), "clear of the sight area");
    }

    [Test]
    public void UnarmedRoomCannotFireATrickInAnOrdinaryScene()
    {
        var root = new GameObject("Unarmed tricks sandbox");
        try
        {
            var room = root.AddComponent<TricksSandbox>();
            Assert.IsFalse(room.TryFire("absent"));
            StringAssert.Contains("Unknown sandbox trick", room.Feedback);
            Assert.IsFalse(room.TryFire(TrickCheck.Tricks[0]));
            Assert.IsNull(root.GetComponentInChildren<KarenBrain>());
        }
        finally { Object.DestroyImmediate(root); }
    }
}
