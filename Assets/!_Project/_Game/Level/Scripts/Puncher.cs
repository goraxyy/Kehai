using Kehai.Karen;
using UnityEngine;

// The time clock by the staff door. Press E to start the shift, E again to end it. If Karen
// has stopped it (the overtime, 0.3.0), hold E for three seconds to start it again.
public class Puncher : HighlightInteractable, IHoldInteractable
{
    public const float RestartSeconds = 3f;

    public ShiftManager shiftManager;

    protected override void Awake()
    {
        base.Awake();
        if (shiftManager == null) shiftManager = FindAnyObjectByType<ShiftManager>();
    }

    public override void Interact(PlayerInteract player)
    {
        if (shiftManager != null && shiftManager.IsFrozen) return;   // stopped: it's a hold now
        if (shiftManager == null)
        {
            Debug.LogWarning("Puncher has no ShiftManager to talk to.", this);
            return;
        }

        NoiseBus.Emit(transform.position, 0.8f, NoiseKind.TimeClock, NoiseAuthor.Player);
        shiftManager.ToggleShift();
    }

    public override string GetPrompt()
    {
        if (shiftManager == null) return "Punch in";
        if (shiftManager.IsFrozen) return "Hold E to restart the clock";
        if (!shiftManager.IsShiftActive) return "Clock in";
        return shiftManager.CanClockOut ? "Clock out" : "Finish your tasks first";
    }

    // ---- restarting the clock she stopped ------------------------------------------------

    public bool CanHold(PlayerInteract player) => shiftManager != null && shiftManager.IsFrozen;
    public float HoldDuration => RestartSeconds;

    public void OnHoldProgress(float normalised)
    {
        if (normalised > 0.05f && Random.value < 0.02f)
            NoiseBus.Emit(transform.position, 0.3f, NoiseKind.TimeClock, NoiseAuthor.Player);
    }

    public void OnHoldCancelled() { }

    public void OnHoldComplete(PlayerInteract player)
    {
        if (shiftManager == null || !shiftManager.Unfreeze()) return;   // her Ledger hears of it (clock_restarted)
        OneShotAudio.PlayAt(ProceduralAudio.ClockStart(), transform.position, 0.9f);
        NoiseBus.Emit(transform.position, 0.6f, NoiseKind.TimeClock, NoiseAuthor.Player);
    }
}
