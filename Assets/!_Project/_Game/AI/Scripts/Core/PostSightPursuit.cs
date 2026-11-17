using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // The hunt's clock (0.5.1, item 13): one window of huntMemorySeconds (60 s) from her last
    // confirmed sighting, for the whole hunt, not for a particular plan. Only a fresh sighting
    // renews it; sounds, new plans, a stun and arriving at an empty guess cannot. Inside the window
    // she runs on after you, and once a guess turns out empty she searches (Pursue): the spots
    // she has looked at and the noises she heard since the sighting live here, so a new plan
    // carries on the same search. Nothing here knows where you are: only her own sightings, her
    // hearing and her guesses feed it.
    public sealed class PostSightPursuit
    {
        public float Deadline { get; private set; } = float.NegativeInfinity;
        public float Seconds { get; private set; }
        public float LastSighting => lastEvidence;
        public bool Searching { get; private set; }
        public int SpotsSearched { get; private set; }
        public string EndWhy { get; private set; }

        float lastEvidence = float.NegativeInfinity;
        float retiredThrough = float.NegativeInfinity;
        readonly List<Vector3> searched = new List<Vector3>();
        readonly List<(float t, Vector3 at)> noises = new List<(float, Vector3)>();
        public IReadOnlyList<(float t, Vector3 at)> Noises => noises;

        // A spot within this many metres of one she has searched counts as searched.
        public const float SearchedRadius = 3f;
        // Noises older than this no longer steer the search.
        public const float NoiseMemory = 15f;

        public bool Active(float now) => now < Deadline;
        public float Left(float now) => Mathf.Max(0f, Deadline - now);

        // The hunt starts on the sighting that started it, however many frames ago that was
        // (her awareness lags her eye). A sighting from a hunt already over can't start another.
        public void Start(float seenAt, float now, float seconds)
        {
            Searching = false;
            SpotsSearched = 0;
            EndWhy = null;
            searched.Clear();
            noises.Clear();
            Seconds = Mathf.Max(0f, seconds);
            if (seenAt <= retiredThrough || seenAt > now || float.IsNaN(seenAt)) { Deadline = float.NegativeInfinity; return; }
            lastEvidence = seenAt;
            Deadline = seenAt + Seconds;
        }

        // A confirmed sighting while she hunts: the window starts again from it, and the search
        // is over (she runs at you; anything she searches later is from this sighting on).
        public void ObserveSight(float seenAt, float now, float seconds)
        {
            if (seenAt <= retiredThrough || seenAt < lastEvidence || now - seenAt > 0.25f || seenAt > now) return;
            lastEvidence = seenAt;
            Seconds = Mathf.Max(0f, seconds);
            Deadline = seenAt + Seconds;
            Searching = false;
            searched.Clear();
            noises.Clear();
        }

        // A sound she heard while the window is open. It steers the search; it never moves the
        // deadline.
        public void Heard(Vector3 at, float now)
        {
            if (!Active(now) || now < lastEvidence) return;
            noises.Add((now, at));
            while (noises.Count > 0 && now - noises[0].t > NoiseMemory) noises.RemoveAt(0);
            if (noises.Count > 16) noises.RemoveAt(0);
        }

        public void BeginSearch() => Searching = true;
        public void StopSearching() => Searching = false;

        public void Searched(Vector3 at)
        {
            searched.Add(at);
            SpotsSearched++;
        }

        public bool WasSearched(Vector3 at)
        {
            for (int i = 0; i < searched.Count; i++)
            {
                Vector3 d = searched[i] - at;
                d.y = 0f;
                if (d.sqrMagnitude < SearchedRadius * SearchedRadius) return true;
            }
            return false;
        }

        // Why the hunt is ending, said by whoever knows (the run, the window, a recovery). The
        // brain logs it when the hunt actually ends.
        public void Ending(string why)
        {
            if (EndWhy == null) EndWhy = why;
        }

        // The hunt is over: its sighting can't start or renew another.
        public void Clear()
        {
            retiredThrough = Mathf.Max(retiredThrough, lastEvidence);
            Deadline = float.NegativeInfinity;
            Searching = false;
            EndWhy = null;
            searched.Clear();
            noises.Clear();
        }

        // A new shift or a restart.
        public void Reset()
        {
            Deadline = lastEvidence = retiredThrough = float.NegativeInfinity;
            Searching = false;
            SpotsSearched = 0;
            EndWhy = null;
            searched.Clear();
            noises.Clear();
        }
    }
}
