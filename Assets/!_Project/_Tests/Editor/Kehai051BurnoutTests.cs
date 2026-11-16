using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

// Diagnose the reported index independently of lighting and the chase director.
public class Kehai051BurnoutTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void CoffeeAndShiftResetReachTheVisibleBarAndSprintGate()
    {
        var root = new GameObject("burnout test");
        var fill = new GameObject("fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        try
        {
            var energy = root.AddComponent<BurnoutSystem>();
            var hud = root.AddComponent<BurnoutUI>();
            hud.burnout = energy;
            hud.fill = fill.GetComponent<RectTransform>();
            hud.fillImage = fill.GetComponent<Image>();
            hud.smoothing = 0f;
            energy.energy = 0f;
            Assert.IsFalse(energy.CanSprint);
            hud.SendMessage("Update");
            Assert.AreEqual(0f, hud.fill.anchorMax.x);
            energy.DrinkCoffee();
            Assert.AreEqual(energy.coffeeRefill, energy.Energy01, 0.0001f);
            Assert.IsTrue(energy.CanSprint);
            hud.SendMessage("Update");
            Assert.AreEqual(energy.Energy01, hud.fill.anchorMax.x, 0.0001f);
            energy.energy = 0.95f;
            energy.DrinkCoffee();
            Assert.AreEqual(1f, energy.Energy01);
            energy.SetChaseState(true);
            energy.ResetForNewShift(0);
            Assert.AreEqual(1f, energy.Energy01);
            Assert.IsFalse((bool)typeof(BurnoutSystem).GetField("isInChase", Private).GetValue(energy));
            energy.ResetForNewShift(3);
            Assert.AreEqual(0.7f, energy.Energy01, 0.0001f);
            energy.ResetForNewShift(100);
            Assert.AreEqual(0f, energy.Energy01);
            Assert.IsFalse(energy.CanSprint);
        }
        finally { Object.DestroyImmediate(fill); Object.DestroyImmediate(root); }
    }

    [Test]
    public void AnInactiveShiftDoesNotDrainEnergy()
    {
        var root = new GameObject("idle burnout test");
        try
        {
            var energy = root.AddComponent<BurnoutSystem>();
            typeof(BurnoutSystem).GetField("shiftManager", Private).SetValue(energy, null);
            energy.onlyDrainDuringShift = true;
            Assert.IsFalse((bool)typeof(BurnoutSystem).GetMethod("ShouldDrain", Private).Invoke(energy, null));
            energy.energy = 0.6f;
            energy.SendMessage("Update");
            Assert.AreEqual(0.6f, energy.Energy01);
        }
        finally { Object.DestroyImmediate(root); }
    }
}
