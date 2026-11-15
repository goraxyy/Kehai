using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Karen
{
    [System.Serializable]
    public sealed class EscapeRecord
    {
        public int version = 1;
        public int shift, seed;
        public string layout, from, end, player;
        public float t;
        public float[] karen;
        public string[] path;
        public string[] topThree;
        public EscapeRecord Copy() => new EscapeRecord { shift = shift, seed = seed, layout = layout,
            from = from, end = end, player = player, t = t, karen = karen == null ? new float[0] : (float[])karen.Clone(),
            path = path == null ? new string[0] : (string[])path.Clone(), topThree = topThree == null ? new string[0] : (string[])topThree.Clone() };
    }

    // Director/recorder-only answer sheet. Never passed to the hunt or belief during a shift.
    // One falling edge of sight creates one sample. We keep three region transitions, stopping
    // at reacquisition, 30 seconds, or shift end; partial/censored paths are labelled explicitly.
    public sealed class EscapeRecorder
    {
        readonly List<EscapeRecord> completed = new List<EscapeRecord>();
        EscapeRecord pending;
        readonly List<string> path = new List<string>();
        bool seen;
        string last;

        public bool IsLosingSight(bool sees) => seen && !sees;

        public void Reset() { completed.Clear(); pending = null; path.Clear(); seen = false; last = null; }

        public void Observe(bool sees, string seenRegion, string trueRegion, float time, float[] karen,
                            string layout, int shift, int seed, IEnumerable<string> ranked = null, string player = "")
        {
            if (pending != null)
            {
                if (sees) Finish("reacquired");
                else if (time - pending.t > 30f) Finish("timeout");
            }
            if (seen && !sees && !string.IsNullOrEmpty(seenRegion))
            {
                pending = new EscapeRecord { from = seenRegion, t = time, karen = karen == null ? new float[0] : (float[])karen.Clone(),
                    layout = layout, shift = shift, seed = seed, player = player, path = new string[0],
                    topThree = (ranked ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(3).ToArray() };
                path.Clear();
                last = seenRegion;
            }
            seen = sees;
            if (pending == null || string.IsNullOrEmpty(trueRegion) || trueRegion == last) return;
            path.Add(trueRegion);
            last = trueRegion;
            if (path.Count == 3) Finish("three_places");
        }

        void Finish(string reason)
        {
            if (pending == null) return;
            pending.path = path.ToArray();
            pending.end = reason;
            completed.Add(pending);
            pending = null;
            path.Clear();
        }

        // Recorder snapshots copy pending state and do not commit it to Karen's readable memory.
        public List<EscapeRecord> Snapshot()
        {
            var result = completed.Select(x => x.Copy()).ToList();
            if (pending != null)
            {
                pending.path = path.ToArray();
                pending.end = "shift_end";
                result.Add(pending.Copy());
            }
            return result;
        }

        public void EndShift() => Finish("shift_end");
    }
}
