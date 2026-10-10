using Kehai.Karen;
using UnityEngine;

// The time clock by the staff door. Press E to start the shift, E again to end it.
public class Puncher : HighlightInteractable
{
    public ShiftManager shiftManager;

    protected override void Awake()
    {
        base.Awake();
        if (shiftManager == null) shiftManager = FindAnyObjectByType<ShiftManager>();
    }

    public override void Interact(PlayerInteract player)
    {
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
        if (!shiftManager.IsShiftActive) return "Clock in";
        return shiftManager.CanClockOut ? "Clock out" : "Finish your tasks first";
    }
}
