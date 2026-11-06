using UnityEngine;

// What's been done to you, for a few seconds (0.2.0: Karen's Throw): blurred, slowed or
// stunned. PlayerMotor reads it, so the bots, who drive the same motor, feel it too; the
// screen effects (PlayerScreenFx, the second builder's) follow Changed.
[DisallowMultipleComponent]
public sealed class PlayerEffects : MonoBehaviour
{
    public float BlurredUntil { get; private set; } = -999f;
    public float SlowedUntil { get; private set; } = -999f;
    public float SlowFraction { get; private set; }
    public float StunnedUntil { get; private set; } = -999f;

    public bool Blurred => Time.time < BlurredUntil;
    public bool Slowed => Time.time < SlowedUntil;
    public bool Stunned => Time.time < StunnedUntil;

    // What your speed is multiplied by right now: 0 stunned, less than 1 slowed.
    public float SpeedFactor => Stunned ? 0f : Slowed ? 1f - SlowFraction : 1f;

    // Any change to anyone's effects, with whose.
    public static event System.Action<PlayerEffects> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Changed = null;

    public void Blur(float seconds)
    {
        BlurredUntil = Mathf.Max(BlurredUntil, Time.time + seconds);
        Changed?.Invoke(this);
    }

    public void Slow(float fraction, float seconds)
    {
        SlowFraction = Mathf.Max(Slowed ? SlowFraction : 0f, Mathf.Clamp01(fraction));
        SlowedUntil = Mathf.Max(SlowedUntil, Time.time + seconds);
        Changed?.Invoke(this);
    }

    public void Stun(float seconds)
    {
        StunnedUntil = Mathf.Max(StunnedUntil, Time.time + seconds);
        Changed?.Invoke(this);
    }

    public void Clear()
    {
        BlurredUntil = SlowedUntil = StunnedUntil = -999f;
        SlowFraction = 0f;
        Changed?.Invoke(this);
    }
}
