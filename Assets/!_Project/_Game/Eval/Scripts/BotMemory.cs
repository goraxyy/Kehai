using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Eval
{
    // A simulated player's memory across a career (0.4.0, build plan §7.4, C2.3): its habits, its
    // mood, its grudges. Kept for the four shifts of a career, fresh for the next.
    //
    //   favourite spots and routes   counts of where it hid, and which job it went to after which
    //   job order                    the order it did its jobs in, last shift
    //   where it was caught          region → the shift it happened; avoided for `grudge` shifts
    //   confidence                   drops after a catch, comes back over quiet shifts
    //
    // Plain C#, and it draws no randomness: the scripted players write to it without changing a
    // single choice they make.
    public sealed class BotMemory
    {
        readonly Dictionary<string, float> hides = new Dictionary<string, float>();
        readonly Dictionary<string, float> routes = new Dictionary<string, float>();   // "from>to" job kinds
        readonly Dictionary<string, int> caughtAt = new Dictionary<string, int>();     // region → shift
        readonly Dictionary<string, float> sheWas = new Dictionary<string, float>();   // region → Time.time she was last sensed there
        readonly List<string> order = new List<string>();
        public List<string> LastOrder { get; private set; } = new List<string>();

        readonly Dictionary<string, float> sightings = new Dictionary<string, float>();
        readonly Dictionary<string, float> catches = new Dictionary<string, float>();
        readonly Dictionary<string, float> escapes = new Dictionary<string, float>();
        readonly Dictionary<string, float> tricks = new Dictionary<string, float>();
        readonly HashSet<string> activeTricks = new HashSet<string>();
        float unseenSince = -1f;
        bool threatened, ended;

        public int Shift { get; private set; }
        public float Confidence { get; private set; } = 1f;
        public int Catches { get; private set; }
        public string LastJob { get; private set; }

        public bool HasHabits => hides.Count > 0 || routes.Count > 0;

        public void BeginShift(int shift)
        {
            Shift = shift;
            ended = false;
            threatened = false;
            unseenSince = -1f;
            activeTricks.Clear();
            if (order.Count > 0) LastOrder = new List<string>(order);
            order.Clear();
            sheWas.Clear();
            LastJob = null;
        }

        // A shift ended: caught or not. A quiet shift brings confidence back by a third.
        public void EndShift(bool caught, string where)
        {
            if (ended) return;
            ended = true;
            if (caught)
            {
                Catches++;
                Confidence = Mathf.Max(0.1f, Confidence * 0.5f);
                if (!string.IsNullOrEmpty(where)) { caughtAt[where] = Shift; Add(catches, where); }
            }
            else Confidence = Mathf.Min(1f, Confidence + (1f - Confidence) / 3f);
            // Habits fade a little each shift, so a changed habit wins in the end.
            foreach (string k in hides.Keys.ToList()) hides[k] *= 0.8f;
            foreach (string k in routes.Keys.ToList()) routes[k] *= 0.8f;
            foreach (var table in new[] { sightings, catches, escapes, tricks })
                foreach (string k in table.Keys.ToList())
                {
                    table[k] *= 0.8f;
                    if (table[k] < 0.01f) table.Remove(k);
                }
        }

        public void Hid(string region)
        {
            if (string.IsNullOrEmpty(region)) return;
            hides.TryGetValue(region, out float n);
            hides[region] = n + 1f;
        }

        public void DidJob(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (LastJob != null)
            {
                string key = LastJob + ">" + kind;
                routes.TryGetValue(key, out float n);
                routes[key] = n + 1f;
            }
            LastJob = kind;
            order.Add(kind);
        }

        public void SensedHer(string region, float at)
        {
            if (ended || string.IsNullOrEmpty(region) || float.IsNaN(at) || float.IsInfinity(at)) return;
            // Count distinct visits, not frames. Continuous visibility is one encounter.
            if (!sheWas.TryGetValue(region, out float before) || at - before > 5f) Add(sightings, region);
            sheWas[region] = at;
        }

        static void Add(Dictionary<string, float> table, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            table.TryGetValue(key, out float value);
            table[key] = value + 1f;
        }

        static float Read(Dictionary<string, float> table, string key) =>
            key != null && table.TryGetValue(key, out float value) ? value : 0f;

        public float SeenAt(string region) => Read(sightings, region);
        public float CaughtAt(string region) => Read(catches, region);
        public float EscapedAt(string region) => Read(escapes, region);
        public float TrickCount(string kind) => Read(tricks, kind);

        // Called only with EnvWorld's public, player-visible observation. An "escape" here
        // is the bot's impression (ten seconds without seeing/hearing her after red eyes),
        // never the Director's privileged escape truth or her actual chase state.
        public void ObserveFair(Dictionary<string, object> obs, float now)
        {
            if (ended || obs == null) return;
            var her = obs.TryGetValue("karen", out object h) ? h as Dictionary<string, object> : null;
            var you = obs.TryGetValue("you", out object y) ? y as Dictionary<string, object> : null;
            bool visible = her != null && her.TryGetValue("visible", out object v) && v is bool b && b;
            bool heard = her != null && her.TryGetValue("heard", out object a) && a is bool audible && audible;
            if (visible)
            {
                SensedHer(her.TryGetValue("region", out object r) ? r as string : null, now);
                if (her.TryGetValue("eye_colour", out object eye) && eye as string == "hunt") threatened = true;
            }
            if (visible || heard) unseenSince = -1f;
            else if (threatened)
            {
                if (unseenSince < 0f) unseenSince = now;
                if (now - unseenSince >= 10f)
                {
                    Add(escapes, you != null && you.TryGetValue("region", out object r) ? r as string : null);
                    threatened = false;
                }
            }
            var current = new HashSet<string>();
            foreach (var chore in EmployeeOptions.List(obs, "karen_chores"))
                if (chore.TryGetValue("kind", out object kind) && kind is string name)
                {
                    current.Add(name);
                    if (!activeTricks.Contains(name)) Add(tricks, name);
                }
            activeTricks.Clear();
            activeTricks.UnionWith(current);
        }

        // Its usual hiding spot, or null.
        public string UsualHide() => hides.Count == 0 ? null : hides.OrderByDescending(p => p.Value).ThenBy(p => p.Key).First().Key;

        // How strongly it hides there: the usual spot's share of its hides.
        public float HideHabit()
        {
            float total = hides.Values.Sum();
            return total <= 0f ? 0f : hides.Values.Max() / total;
        }

        // The job it usually does next after `kind`, or null.
        public string UsualNext(string kind)
        {
            if (kind == null) return null;
            string best = null;
            float n = 0f;
            foreach (KeyValuePair<string, float> r in routes)
                if (r.Key.StartsWith(kind + ">") && r.Value > n) { n = r.Value; best = r.Key.Substring(kind.Length + 1); }
            return best;
        }

        // A grudge: it was caught in `region` within the last `shifts` shifts.
        public bool Holds(string region, int shifts) =>
            region != null && caughtAt.TryGetValue(region, out int when) && Shift - when <= shifts;

        public IEnumerable<string> GrudgeRegions(int shifts) => caughtAt.Where(p => Shift - p.Value <= shifts).Select(p => p.Key);

        // Where it last sensed her, and how long ago (seconds; ∞ if never this shift).
        public float SinceSensed(string region, float now) => region != null && sheWas.TryGetValue(region, out float t) ? now - t : float.PositiveInfinity;

        public string LastSensedRegion(out float at)
        {
            at = -999f;
            string best = null;
            foreach (KeyValuePair<string, float> p in sheWas)
                if (p.Value > at) { at = p.Value; best = p.Key; }
            return best;
        }
    }
}
