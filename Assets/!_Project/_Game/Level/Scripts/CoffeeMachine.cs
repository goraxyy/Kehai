using Kehai.Karen;
using UnityEngine;

// Refills the burnout bar. Pour as many cups as you like: cooldownTime is 0 by default,
// and only gates the machine if you deliberately dial one in.
public class CoffeeMachine : HighlightInteractable
{
    [Tooltip("Seconds before another cup can be poured. 0 means no waiting at all.")]
    public float cooldownTime = 0f;
    public AudioClip pourSound;

    float lastUseTime = -999f;
    BurnoutSystem burnout;

    protected override void Awake()
    {
        base.Awake();
        burnout = FindAnyObjectByType<BurnoutSystem>();
    }

    bool OnCooldown => cooldownTime > 0f && Time.time - lastUseTime < cooldownTime;

    public override void Interact(PlayerInteract player)
    {
        if (OnCooldown) return;

        if (burnout == null) burnout = FindAnyObjectByType<BurnoutSystem>();
        if (burnout == null)
        {
            Debug.LogWarning("No BurnoutSystem in the scene for the coffee machine to refill.", this);
            return;
        }

        burnout.DrinkCoffee();
        lastUseTime = Time.time;
        NoiseBus.Emit(transform.position, 0.6f, NoiseKind.Coffee, NoiseAuthor.Player);
        GameEvents.RaiseCoffeeDrunk(transform.position);
        OneShotAudio.PlayAt(pourSound, transform.position);
    }

    public override string GetPrompt()
    {
        if (OnCooldown)
            return $"Brewing... {(int)(cooldownTime - (Time.time - lastUseTime)) + 1}s";

        return "Drink coffee";
    }
}
