using UnityEngine;

namespace Kehai.Aiko
{
    // Aiko.md §3: six channels, plus touch (being caught is contact, not sight) and the
    // blink channel from the webcam.
    public enum SenseChannel { Sight, Hearing, Trace, Testimony, Infrastructure, Absence, Touch, Blink }

    // One piece of evidence about where the employee is. Nothing in the game grants Aiko a
    // boolean "sees player": everything arrives as one of these, with a confidence and a
    // timestamp, and goes into the belief grid.
    public readonly struct Observation
    {
        public readonly SenseChannel Channel;
        public readonly Vector3 Position;
        public readonly float Confidence;     // 0..1
        public readonly float Timestamp;
        public readonly float Sigma;          // metres of positional uncertainty
        public readonly Vector3 Heading;      // footprints carry a direction; zero otherwise
        public readonly bool IsNegative;      // "I looked and there was nothing here"
        public readonly string Label;         // what it was, for the thought log

        public Observation(SenseChannel channel, Vector3 position, float confidence, float sigma,
                           string label, Vector3 heading = default, bool isNegative = false)
        {
            Channel = channel;
            Position = position;
            Confidence = Mathf.Clamp01(confidence);
            Timestamp = Time.time;
            Sigma = Mathf.Max(0.5f, sigma);
            Heading = heading;
            IsNegative = isNegative;
            Label = label;
        }

        // Evidence strong enough to count as "knowing where you were" — it resets staleness.
        public bool IsStrong => !IsNegative && Confidence >= 0.5f &&
                                (Channel == SenseChannel.Sight || Channel == SenseChannel.Touch ||
                                 Channel == SenseChannel.Testimony || Channel == SenseChannel.Infrastructure);
    }
}
