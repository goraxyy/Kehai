using System;
using System.Collections.Generic;

namespace Kehai.Karen
{
    // Timers for landed chores, keyed by fixture (several rooms/customers can coexist).
    // A cancellation consumes the timer without becoming a player answer.
    public sealed class RepairTiming
    {
        readonly Dictionary<(string kind, object fixture), float> started = new Dictionary<(string, object), float>();
        public void Clear() => started.Clear();
        public void Begin(string kind, object fixture, float now)
        {
            if (string.IsNullOrEmpty(kind) || fixture == null || float.IsNaN(now) || float.IsInfinity(now)) return;
            var key = (kind, fixture);
            if (!started.ContainsKey(key)) started.Add(key, now);
        }
        public bool Finish(string kind, object fixture, float now, bool repaired, out float seconds)
        {
            seconds = -1f;
            if (fixture == null || !started.TryGetValue((kind, fixture), out float since)) return false;
            started.Remove((kind, fixture));
            if (!repaired || float.IsNaN(now) || float.IsInfinity(now) || now < since) return false;
            seconds = now - since;
            return true;
        }
    }
}
