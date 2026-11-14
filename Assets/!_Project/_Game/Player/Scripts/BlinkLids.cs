using UnityEngine;

// The eyelids as two states (0.4.1): open or shut, and the short trip between them.
//
// Until now the lids mirrored the webcam's analog reading, so half-shut eyes drew half-shut
// lids, a squint darkened the screen, and fully shut eyes often left a slit. Now they follow
// one yes-or-no — are your eyes shut? — the same one Karen acts on (BlinkTracker.EyesClosed),
// and move at the speed real lids do.
//
// The times are a person's, measured at 600 frames a second (Kwon et al., "High-speed camera
// characterization of voluntary eye blinking kinematics", J. R. Soc. Interface 10, 2013; 25
// volunteers): the lid comes down in 76 ms, stays down 58 ms, and is 97% open again 299 ms
// after it started, which leaves about 165 ms for the way up. (The last 3% creeps open over
// another quarter of a second; too slow and small to see, so it's left out.)
//
// A plain class stepped by time, so a test can run it without a scene.
public sealed class BlinkLids
{
    public const float CloseSeconds = 0.076f;     // open → shut
    public const float MinShutSeconds = 0.058f;   // the shortest the lids stay shut
    public const float OpenSeconds = 0.165f;      // shut → open

    // The shortest whole blink the lids can show: down, held, up.
    public const float ShortestBlink = CloseSeconds + MinShutSeconds + OpenSeconds;

    // What's asked of them: shut (true) or open.
    public bool WantShut { get; private set; }

    // The two states. Neither is true during the trip between them.
    public bool IsShut => travel >= 1f;
    public bool IsOpen => travel <= 0f;

    // How far down the lids are, 0 open to 1 shut, as a straight line in time: whoever draws
    // them adds the ease (Eyelids.ease).
    public float Closed01 => travel;

    float travel;
    float shutFor;

    public void Set(bool shut) => WantShut = shut;

    // Straight to a state, with no trip: a scene starting with the eyes shut.
    public void Snap(bool shut)
    {
        WantShut = shut;
        travel = shut ? 1f : 0f;
        shutFor = 0f;
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        if (WantShut)
        {
            if (travel < 1f) { travel = Mathf.Min(1f, travel + dt / CloseSeconds); shutFor = 0f; }
            else shutFor += dt;
        }
        else if (travel >= 1f && shutFor < MinShutSeconds) shutFor += dt;   // real lids rest a moment at the bottom
        else travel = Mathf.Max(0f, travel - dt / OpenSeconds);
    }
}
