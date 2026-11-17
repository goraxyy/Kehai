using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

// 0.5.1 item 9: the burnout index, traced end to end. Energy (1 fresh, 0 spent) -> the bar ->
// the sprint gate -> coffee -> chase and running drain -> an inactive shift -> restart and the
// next shift. Everything is stepped through the public Step(dt) seams with a known time;
// SendMessage("Update") on a plain MonoBehaviour in edit mode trips Unity's
// ShouldRunBehaviour() assertion, which is what failed the first version of these tests.
public class Kehai051BurnoutTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const float Tol = 0.0001f;

    GameObject root;

    [SetUp] public void SetUp() => root = new GameObject("Kehai051BurnoutTests");
    [TearDown] public void TearDown() => Object.DestroyImmediate(root);

    sealed class HeldKeys : IMotorInput
    {
        public bool Sprint { get; set; }
        public Vector3 MoveWorld => Vector3.zero;
        public bool Crouch => false;
        public float YawDelta => 0f;
        public float PitchDelta => 0f;
        public bool Jump => false;
    }

    // No Awake: it would copy the scene's Volume profile. The drain needs no shift for these.
    BurnoutSystem Energy(bool onlyDuringShift = false)
    {
        var energy = root.AddComponent<BurnoutSystem>();
        energy.onlyDrainDuringShift = onlyDuringShift;
        return energy;
    }

    BurnoutUI Bar(BurnoutSystem energy)
    {
        var fill = new GameObject("fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fill.transform.SetParent(root.transform);
        var hud = root.AddComponent<BurnoutUI>();
        hud.burnout = energy;
        hud.fill = fill.GetComponent<RectTransform>();
        hud.fillImage = fill.GetComponent<Image>();
        hud.smoothing = 0f;
        return hud;
    }

    PlayerMotor Motor(BurnoutSystem energy, bool shiftHeld, bool moving)
    {
        var body = new GameObject("player", typeof(CharacterController));
        body.transform.SetParent(root.transform);
        var motor = body.AddComponent<PlayerMotor>();
        motor.burnout = energy;
        motor.externalInput = new HeldKeys { Sprint = shiftHeld };
        typeof(PlayerMotor).GetField("planarVelocity", Private).SetValue(motor, moving ? new Vector3(6.5f, 0f, 0f) : Vector3.zero);
        typeof(BurnoutSystem).GetField("playerMotor", Private).SetValue(energy, motor);
        return motor;
    }

    static bool InChase(BurnoutSystem energy) => (bool)typeof(BurnoutSystem).GetField("isInChase", Private).GetValue(energy);

    // The bar is energy left, not a burnout score: full when fresh, empty when spent, and the
    // sprint gate agrees with it at both ends. Coffee fills it and stops at full.
    [Test]
    public void CoffeeAndShiftResetReachTheVisibleBarAndSprintGate()
    {
        var energy = Energy();
        var hud = Bar(energy);

        energy.energy = 1f;
        hud.Step(0.02f);
        Assert.AreEqual(1f, hud.fill.anchorMax.x, Tol, "fresh = a full bar");
        Assert.IsTrue(energy.CanSprint);

        energy.energy = 0f;
        hud.Step(0.02f);
        Assert.AreEqual(0f, hud.fill.anchorMax.x, Tol, "spent = an empty bar");
        Assert.IsFalse(energy.CanSprint, "an empty bar can't sprint");

        energy.DrinkCoffee();
        Assert.AreEqual(energy.coffeeRefill, energy.Energy01, Tol);
        Assert.IsTrue(energy.CanSprint);
        hud.Step(0.02f);
        Assert.AreEqual(energy.Energy01, hud.fill.anchorMax.x, Tol, "the bar shows what coffee put back");

        energy.energy = 0.95f;
        energy.DrinkCoffee();
        Assert.AreEqual(1f, energy.Energy01, "coffee stops at a full bar");
        Assert.AreEqual(1f, energy.energy, "and the stored value is clamped too");

        energy.SetChaseState(true);
        energy.ResetForNewShift(0);
        Assert.AreEqual(1f, energy.Energy01);
        Assert.IsFalse(InChase(energy), "a new shift isn't still in the last one's chase");
        energy.ResetForNewShift(3);
        Assert.AreEqual(0.7f, energy.Energy01, Tol);
        energy.ResetForNewShift(100);
        Assert.AreEqual(0f, energy.Energy01);
        Assert.IsFalse(energy.CanSprint);
    }

    // The bar slides to a new value rather than jumping, and never past it.
    [Test]
    public void TheBarSlidesToTheValueWithoutOvershooting()
    {
        var energy = Energy();
        var hud = Bar(energy);
        hud.smoothing = 8f;
        energy.energy = 1f;
        hud.Step(1f);
        energy.energy = 0.2f;
        hud.Step(0.05f);
        Assert.AreEqual(0.6f, hud.fill.anchorMax.x, Tol, "8 per second for 0.05 s");
        hud.Step(1f);
        Assert.AreEqual(0.2f, hud.fill.anchorMax.x, Tol);
    }

    // Before clock-in, between shifts, or with no shift manager, nothing drains: not the
    // base rate, not sprinting, not a chase.
    [Test]
    public void AnInactiveShiftDoesNotDrainEnergy()
    {
        var energy = Energy(onlyDuringShift: true);
        Motor(energy, shiftHeld: true, moving: true);
        energy.SetChaseState(true);
        energy.energy = 0.6f;
        energy.Step(60f);
        Assert.AreEqual(0.6f, energy.Energy01, "no shift manager: no drain");

        var shift = root.AddComponent<ShiftManager>();
        typeof(BurnoutSystem).GetField("shiftManager", Private).SetValue(energy, shift);
        Assert.IsFalse(shift.IsShiftActive);
        energy.Step(60f);
        Assert.AreEqual(0.6f, energy.Energy01, "before clock-in: no drain");
    }

    // The Esc menu sets timeScale to 0 (GamePause), so a paused frame is a zero-length step:
    // nothing drains and the bar holds still, mid-chase and mid-sprint included.
    [Test]
    public void APausedFrameChangesNeitherTheValueNorTheBar()
    {
        var energy = Energy();
        var hud = Bar(energy);
        hud.smoothing = 8f;
        Motor(energy, shiftHeld: true, moving: true);
        energy.SetChaseState(true);
        energy.energy = 0.5f;
        hud.Step(1f);
        Assert.AreEqual(0.5f, hud.fill.anchorMax.x, Tol);

        energy.energy = 0.3f;                        // a value the bar hasn't reached yet
        for (int i = 0; i < 100; i++) { energy.Step(0f); hud.Step(0f); }
        Assert.AreEqual(0.3f, energy.Energy01, Tol, "no drain while paused");
        Assert.AreEqual(0.5f, hud.fill.anchorMax.x, Tol, "the bar doesn't move while paused");
    }

    // Drain rates: walking 0.2 a minute, running twice that, a chase 0.03 a second on top.
    [Test]
    public void RunningAndChasingDrainAtTheirRates()
    {
        var energy = Energy();
        Motor(energy, shiftHeld: false, moving: true);
        energy.energy = 1f;
        energy.Step(60f);
        Assert.AreEqual(0.8f, energy.Energy01, Tol, "a minute of walking");

        Motor(energy, shiftHeld: true, moving: true);
        energy.energy = 1f;
        energy.Step(60f);
        Assert.AreEqual(0.6f, energy.Energy01, Tol, "a minute of running");

        Motor(energy, shiftHeld: false, moving: true);
        energy.energy = 1f;
        energy.SetChaseState(true);
        energy.Step(10f);
        Assert.AreEqual(1f - 10f * 0.2f / 60f - 0.3f, energy.Energy01, Tol, "ten seconds chased, applied once");
        energy.SetChaseState(false);
        energy.energy = 1f;
        energy.Step(60f);
        Assert.AreEqual(0.8f, energy.Energy01, Tol, "the chase drain stops with the chase");
    }

    // The reproduced fault: Shift held while standing still (at a till, hiding, stunned) was
    // charged as running, at twice the walking drain, though the player doesn't move, makes no
    // sprint footsteps, and Karen's own model of the bar doesn't charge it.
    [Test]
    public void HoldingShiftWhileStandingStillCostsWhatWalkingCosts()
    {
        var energy = Energy();
        var motor = Motor(energy, shiftHeld: true, moving: false);
        Assert.IsFalse(motor.IsMoving);
        energy.energy = 1f;
        energy.Step(60f);
        Assert.AreEqual(0.8f, energy.Energy01, Tol, "a minute standing with Shift held");
    }

    // The shift's own lifecycle drives the reset: clock-in starts at the shift's energy, a
    // failed shift (caught) restarts at the same energy with the chase cleared, the fourth
    // shift starts at 0.7, and a coffee drunk between shifts doesn't carry over.
    [Test]
    public void ClockInRestartAndNextShiftResetTheBar()
    {
        var energy = Energy(onlyDuringShift: true);
        var shift = root.AddComponent<ShiftManager>();
        shift.burnoutSystem = energy;
        typeof(BurnoutSystem).GetField("shiftManager", Private).SetValue(energy, shift);

        energy.energy = 0.4f;
        shift.StartShift();
        Assert.AreEqual(1, shift.ShiftNumber);
        Assert.AreEqual(1f, energy.Energy01, "shift 1 starts fresh");
        energy.Step(60f);
        Assert.AreEqual(0.8f, energy.Energy01, Tol, "drains once clocked in");

        energy.SetChaseState(true);
        energy.Step(10f);
        Assert.AreEqual(0.8f - 10f * 0.2f / 60f - 0.3f, energy.Energy01, Tol, "chased in an active shift");
        shift.EndShift(completed: false);            // caught: the same shift again, no save
        float atEnd = energy.Energy01;
        energy.Step(60f);
        Assert.AreEqual(atEnd, energy.Energy01, Tol, "no drain once the shift is over, chase or not");

        shift.StartShift();
        Assert.AreEqual(1, shift.ShiftNumber, "a failed shift is played again");
        Assert.AreEqual(1f, energy.Energy01);
        Assert.IsFalse(InChase(energy));
        shift.EndShift(completed: false);

        shift.SetShiftNumber(3);
        energy.energy = 0.2f;                        // left over from the last shift
        energy.DrinkCoffee();
        Assert.AreEqual(0.55f, energy.Energy01, Tol, "coffee between shifts does fill the bar");
        shift.StartShift();
        Assert.AreEqual(4, shift.ShiftNumber);
        Assert.AreEqual(0.7f, energy.Energy01, Tol, "today: shift 4 starts at 0.7 whatever was drunk between shifts");
        Assert.IsTrue(energy.CanSprint);
        shift.EndShift(completed: false);
    }

    // The coffee machine refills the same BurnoutSystem the bar reads.
    [Test]
    public void TheCoffeeMachineRefillsTheBarItsHudReads()
    {
        var energy = Energy();
        var hud = Bar(energy);
        var machine = new GameObject("coffee").AddComponent<CoffeeMachine>();
        machine.transform.SetParent(root.transform);
        typeof(CoffeeMachine).GetField("burnout", Private).SetValue(machine, energy);

        energy.energy = 0.1f;
        machine.Interact(null);
        Assert.AreEqual(0.45f, energy.Energy01, Tol);
        hud.Step(0.02f);
        Assert.AreEqual(0.45f, hud.fill.anchorMax.x, Tol);
        machine.Interact(null);
        machine.Interact(null);
        Assert.AreEqual(1f, energy.Energy01, "no cooldown by default, and full stops at full");
    }
}
