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
