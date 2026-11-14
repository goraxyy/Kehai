using Kehai.Karen;
using UnityEngine;

// The breaker box out the back. When Karen has put the lights out — the whole store, or one
// wing's circuit — you bring them back here: hold E on the box for twenty seconds
// (BackstreetFix, 0.4.1) and every circuit and the mains come back together. The three
// switches on its face light up one by one as you hold, so you can see how far along you are.
//
// Until 0.4.1 the switches were a puzzle (throw them low hum to high); the owner asked for a
// fix that takes time instead. A live switch can still be thrown off with a tap.
public class ElectricBox : HighlightInteractable, IHoldInteractable
{
    public AudioClip switchSound;
    [Tooltip("Optional authored front of the enclosure for the breaker controls.")]
    public Transform controlFace;

    [Tooltip("What the prompt says when there is nothing to fix.")]
    public string idlePrompt = "Breakers are on";

    // Something to fix: the mains are off, or a circuit is tripped.
    public bool NeedsFix
    {
        get
        {
            PowerSystem power = PowerSystem.Instance;
            BreakerPanel panel = BreakerPanel.Instance;
            return power != null && !power.HasPower || panel != null && panel.TrippedCount > 0;
        }
    }

    public override void Interact(PlayerInteract player)
    {
        // A tap does nothing: it's a hold, so it can't be done in passing.
    }

    public override string GetPrompt()
    {
        if (PowerSystem.Instance == null && BreakerPanel.Instance == null) return string.Empty;
        return NeedsFix ? BackstreetFix.Prompt("reset the breakers") : idlePrompt;
    }

    // ---- the hold ---------------------------------------------------------------------------

    public bool CanHold(PlayerInteract player) => NeedsFix;
    public float HoldDuration => BackstreetFix.HoldSeconds;

    public void OnHoldProgress(float normalised)
    {
        BreakerPanel.Instance?.ShowProgress(normalised);
        BackstreetFix.WorkNoise(transform.position, normalised);
    }

    public void OnHoldCancelled() => BreakerPanel.Instance?.ShowProgress(0f);

    public void OnHoldComplete(PlayerInteract player)
    {
        BreakerPanel panel = BreakerPanel.Instance;
        if (panel != null) panel.ResetAll();
        else PowerSystem.Instance?.RestorePower();
        OneShotAudio.PlayAt(switchSound, transform.position);
        BackstreetFix.DoneNoise(transform.position);
    }
}
