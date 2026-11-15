using System;
using System.Collections.Generic;
using System.Linq;

namespace Kehai.Karen
{
    [Serializable]
    public class EscapeStat
    {
        public string layout, from, next;
        public float count;
    }

    // Shared pure arithmetic for the Ledger and offline tests. Inputs are completed-shift
    // evidence only. The prior is an explicit probability, not a made-up trained model.
    public static class EscapeHabits
    {
        public const float PriorWeight = 2f;
        public const float Fade = 0.7f;

        public static void Commit(List<EscapeStat> saved, IEnumerable<EscapeRecord> completed)
        {
            foreach (EscapeStat row in saved) row.count *= Fade;
            saved.RemoveAll(row => !Finite(row.count) || row.count < 0.01f);
            foreach (EscapeRecord record in completed)
            {
                if (record.path == null || record.path.Length == 0 || string.IsNullOrEmpty(record.from) ||
                    string.IsNullOrEmpty(record.path[0]) || string.IsNullOrEmpty(record.layout)) continue;
                var row = saved.FirstOrDefault(x => x.layout == record.layout && x.from == record.from && x.next == record.path[0]);
                if (row == null) saved.Add(row = new EscapeStat { layout = record.layout, from = record.from, next = record.path[0] });
                row.count += 1f;
            }
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static float Chance(IEnumerable<EscapeStat> saved, string layout, string from, string next, float everyone)
        {
            if (!Finite(everyone)) throw new ArgumentException("escape prior must be finite");
            float n = 0, yes = 0;
            foreach (EscapeStat row in saved)
                if (row.layout == layout && row.from == from && Finite(row.count) && row.count > 0f)
                { n += row.count; if (row.next == next) yes += row.count; }
            return (Math.Max(0f, Math.Min(1f, everyone)) * PriorWeight + yes) / (PriorWeight + n);
        }

        public static float Multiplier(IEnumerable<EscapeStat> saved, string layout, string from, string next, int exits)
        {
            float prior = 1f / Math.Max(1, exits);
            return Math.Max(0.25f, Math.Min(4f, Chance(saved, layout, from, next, prior) / prior));
        }
    }
}
