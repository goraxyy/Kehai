using Kehai.Karen;
using UnityEngine;

// The fix boxes in the backstreet (0.4.1): the breaker box (the lights), the fridge control box
// (the fog) and the camera box (the store's cameras). Fixing what she did takes time now: each
// is held for twenty seconds, out in the open with your back to the building. Before, the fog
// took three seconds, the breakers were a tap puzzle, and there was no camera box.
//
// A hold is all or nothing: let go and it starts again. It makes a little noise while you
// work and a clear one when it's done, so she can hear where you are, as with any chore.
public static class BackstreetFix
{
    public const float HoldSeconds = 20f;

    public static string Prompt(string toDoWhat) => $"Hold E to {toDoWhat} ({HoldSeconds:0} s)";

    // The scrape and click of working at a box, now and then while it's held.
    public static void WorkNoise(Vector3 at, float normalised)
    {
        if (normalised > 0.05f && Random.value < 0.02f)
            NoiseBus.Emit(at, 0.3f, NoiseKind.Environmental, NoiseAuthor.Player);
    }

    // The clunk when a fix lands.
    public static void DoneNoise(Vector3 at)
    {
        OneShotAudio.PlayAt(ProceduralAudio.BreakerThrow(), at);
        NoiseBus.Emit(at, 0.5f, NoiseKind.Environmental, NoiseAuthor.Player);
    }
}
