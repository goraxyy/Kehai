using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // What each place paid (0.3.0 build plan C5.1). When a trick lands somewhere, it leaves a
    // watch here: a question to ask about that place until it has an answer — has the bay been
    // restocked, has the camera been unplugged, did anyone walk through the spill — and what to
    // say if the shift ends first. The answer, 0 to 1, goes to the placement memory.
    //
    // Everything a watch asks is her own knowledge: her props, her belief, her ears. Nothing
    // reads where you really are.
    public sealed class PlaceRewards
    {
        sealed class Watching
        {
            public Tactic Trick;
            public string Key;
            public float Since;
            public System.Func<float, float?> Ask;    // seconds since → the reward, or null for "not yet"
            public System.Func<float, float> AtEnd;   // seconds since → the reward, at the shift's end
        }

        readonly List<Watching> watching = new List<Watching>();
        public int Count => watching.Count;

        // What was settled, for tests and the shift report: trick, place, reward.
        public readonly List<(string trick, string key, float reward)> Settled = new List<(string, string, float)>();

        public void Clear()
        {
            watching.Clear();
            Settled.Clear();
        }

        public void Watch(Tactic trick, string key, float now, System.Func<float, float?> ask, System.Func<float, float> atEnd)
        {
            if (trick == null || string.IsNullOrEmpty(key) || ask == null) return;
            watching.Add(new Watching { Trick = trick, Key = key, Since = now, Ask = ask, AtEnd = atEnd ?? (_ => 0.5f) });
            Placed?.Invoke(trick, key, now);
        }

        // Each trick put down is a bet on your way (0.4.0, C4.6): the brain logs it where she stands.
        public event System.Action<Tactic, string, float> Placed;

        public void Tick(KarenContext c, float now)
        {
            for (int i = watching.Count - 1; i >= 0; i--)
            {
                Watching w = watching[i];
                float? reward;
                try { reward = w.Ask(now - w.Since); }
                catch (System.Exception e) { Debug.LogWarning($"{GameNames.Antagonist}: a place watch failed ({w.Trick.Id}): {e.Message}"); reward = 0.5f; }
                if (!reward.HasValue) continue;
                watching.RemoveAt(i);
                Settle(c, w, reward.Value);
            }
        }

        // The shift's over: every watch still open answers now.
        public void EndShift(KarenContext c, float now)
        {
            foreach (Watching w in watching)
            {
                float reward;
                try { reward = w.AtEnd(now - w.Since); }
                catch (System.Exception) { reward = 0.5f; }
                Settle(c, w, reward);
            }
            watching.Clear();
        }

        void Settle(KarenContext c, Watching w, float reward)
        {
            reward = Mathf.Clamp01(reward);
            Settled.Add((w.Trick.Id, w.Key, reward));
            if (c.Ledger != null) c.Ledger.SettlePlace(c, w.Trick, w.Key, reward);
            c.Think("LEARN", $"LEARN    place {w.Trick.Id} at {w.Key} paid {reward:0.00}");
        }

        // A reward from how long something took you: 0 if at once, 1 by `full` seconds.
        public static float ByTime(float seconds, float full) => Mathf.Clamp01(seconds / Mathf.Max(1f, full));
    }
}
