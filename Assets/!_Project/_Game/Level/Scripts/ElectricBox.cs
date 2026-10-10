using Kehai.Karen;
using UnityEngine;

// The breaker box out the back. When Karen has tripped the circuits, the switches mounted
// on its face are the puzzle (see BreakerPanel); the box itself only restores a mains cut
// that didn't touch the breakers — the debug key, or a power cut with the panel intact.
public class ElectricBox : HighlightInteractable
{
    public AudioClip switchSound;

    [Tooltip("What the prompt says when there is nothing to fix.")]
    public string idlePrompt = "Breakers are on";
    public string resetPrompt = "Flip the breakers";
    public string puzzlePrompt = "Reset the breakers — listen: low hum to high";

    public override void Interact(PlayerInteract player)
    {
        PowerSystem power = PowerSystem.Instance;
        if (power == null) return;

        BreakerPanel panel = BreakerPanel.Instance;
        if (panel != null && panel.TrippedCount > 0) return;   // the switches are the way in

        if (power.HasPower) return;
        power.RestorePower();
        OneShotAudio.PlayAt(switchSound, transform.position);
    }

    public override string GetPrompt()
    {
        PowerSystem power = PowerSystem.Instance;
        if (power == null) return string.Empty;

        BreakerPanel panel = BreakerPanel.Instance;
        if (panel != null && panel.TrippedCount > 0) return puzzlePrompt;

        return power.HasPower ? idlePrompt : resetPrompt;
    }
}
