using UnityEngine;

namespace Kehai.Karen
{
    // The search belongs to the whole chase, not to a particular plan. Only a fresh
    // sighting can renew it; sounds, new plans and arriving at an empty guess cannot.
    public sealed class PostSightPursuit
    {
        public float Deadline { get; private set; } = float.NegativeInfinity;
        float lastEvidence = float.NegativeInfinity;
        float retiredThrough = float.NegativeInfinity;

        public bool Active(float now) => now < Deadline;

        public void ObserveSight(float seenAt, float now, float seconds)
        {
            if (seenAt <= retiredThrough || seenAt < lastEvidence || now - seenAt > 0.25f || seenAt > now) return;
            lastEvidence = seenAt;
            Deadline = seenAt + Mathf.Max(0f, seconds);
        }

        public void Clear()
        {
            retiredThrough = Mathf.Max(retiredThrough, lastEvidence);
            Deadline = float.NegativeInfinity;
        }

        public void Reset()
        {
            Deadline = lastEvidence = retiredThrough = float.NegativeInfinity;
        }
    }
}
