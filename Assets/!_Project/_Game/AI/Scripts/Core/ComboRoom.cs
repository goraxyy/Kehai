using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Karen
{
    // One chain of two tricks: `then`, started while `first` was her last trick.
    [System.Serializable]
    public class ChainStat
    {
        public string first, then;   // tactic ids
        public float q;              // what `then` paid after `first`: mean panic delta
        public float n;              // times she's done it (fractional after decay)
        public int found = -1;       // the shift it was written into the room; -1 not yet
        public float alone;          // what `then` paid on its own when the chain was found

        public string Name => first + " → " + then;
    }

    // Her last trick, as the combo learner sees it: which, how long ago, and whether its
    // effect still runs (the lights still out, a door still blocked).
    public struct LastTrick
    {
        public string Id;
        public float Ago;
        public bool Running;

        // A chain's second part starts within the window of the first, or while it still runs.
        public bool Active => Id != null && (Running || Ago <= ComboRoom.Window);

        public override string ToString() =>
            Id == null ? "nothing yet" : $"{Id} {Ago:0}s ago" + (Running ? ", still running" : string.Empty);
    }

    // The combo room (0.2.0 build plan C4.4, design notes §7.5): chains of tricks she finds
    // herself. It starts empty. Each time a trick starts while her last one is still in play,
    // the pair is counted, and its reward — the same panic delta the trick earns on its own —
    // is averaged in. A chain she's done often that pays clearly better than its second part
    // alone is "found": written into the room with the shift, for the thought log and the
    // shift report. Like everything she learns, it fades unless it's reinforced (rule 7).
    //
    // It chooses nothing. The bandit reads it (KarenLedger.BanditScore), and only among the
    // tricks the gate passed: every part of a chain passes the gate when it starts, and pays
    // its own tension.
    public sealed class ComboRoom
    {
        public const float Window = 20f;       // seconds after a trick that the next one chains on to it
        public const float FoundAfter = 3f;    // uses before a chain can be found
        public const float FoundBy = 0.1f;     // and it pays at least this much more than its second part alone

        readonly Dictionary<string, ChainStat> chains = new Dictionary<string, ChainStat>();

        public IEnumerable<ChainStat> All => chains.Values;
        public IEnumerable<ChainStat> Found => chains.Values.Where(c => c.found >= 0);
        public int Count => chains.Count;

        static string Key(string first, string then) => first + ">" + then;

        public void Clear() => chains.Clear();

        public void Load(IEnumerable<ChainStat> saved)
        {
            chains.Clear();
            if (saved == null) return;
            foreach (ChainStat c in saved)
                if (c != null && TacticLibrary.Get(c.first) != null && TacticLibrary.Get(c.then) != null)
                    chains[Key(c.first, c.then)] = c;
        }

        public ChainStat Find(string first, string then) =>
            first != null && chains.TryGetValue(Key(first, then), out ChainStat c) ? c : null;

        // Times she's started anything while `first` was in play.
        public float After(string first)
        {
            float n = 0f;
            foreach (ChainStat c in chains.Values) if (c.first == first) n += c.n;
            return n;
        }

        // A chain's reward: the running mean of what `then` paid after `first`. `alone` is
        // what `then` pays on its own. Returns the chain if this made it found.
        public ChainStat Reward(string first, string then, float delta, float alone, int shift)
        {
            string key = Key(first, then);
            if (!chains.TryGetValue(key, out ChainStat c)) chains[key] = c = new ChainStat { first = first, then = then, q = alone };
            c.n += 1f;
            c.q += (delta - c.q) / Mathf.Max(1f, c.n);
            if (c.found >= 0 || c.n < FoundAfter || c.q <= 0f || c.q < alone + FoundBy) return null;
            c.found = shift;
            c.alone = alone;
            return c;
        }

        // At the end of a shift: counts fade and values drift back to the second part's own,
        // so a chain the player has learned to answer stops looking good. A chain faded
        // below a third of a use is forgotten, found or not.
        public void Fade(System.Func<string, float> alone)
        {
            foreach (ChainStat c in chains.Values)
            {
                c.n *= 0.8f;
                c.q = Mathf.Lerp(c.q, alone(c.then), 0.15f);
            }
            foreach (string key in chains.Where(p => p.Value.n < 0.3f).Select(p => p.Key).ToList()) chains.Remove(key);
        }
    }
}
