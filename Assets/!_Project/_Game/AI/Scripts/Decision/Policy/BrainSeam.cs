using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kehai.Karen
{
    // The seam between the planner and a trained brain (0.4.0, build plan §5.2, D1, C5.1).
    //
    // Today the planner answers "what next?" in two steps: which action (ActionSet, by hand-set
    // utility), then which trick (HtnPlanner.Rank). A brain answers both as one choice: an
    // (action, trick) pair such as "flush:blackout". The steps of the plan stay the planner's.
    //
    //   when she's asked   only when the planner would re-decide anyway: a plan ended, an interrupt
    //                      fired, or the minimum commit time passed. "Finish what she starts" holds.
    //   what she may pick  the mask: only pairs that can really run now. For Basic the Director has
    //                      already removed what pacing forbids; for Vital (and the hunter) only the
    //                      fairness rules and physics remove anything (KarenConfig.PacingOff).
    //   how she picks      the network's chance for each legal pair, and a draw: she doesn't always
    //                      take the top one, and the chance of her pick is logged.
    //   if it can't be planned, the planner's own order is the fallback.
    //
    // Outputs are tied to names, not positions: a removed trick's output is ignored; a new trick has
    // no output yet, so the planner ranks it until the next training.
    public static class BrainSeam
    {
        static readonly ActionId[] actions = (ActionId[])System.Enum.GetValues(typeof(ActionId));

        // Every (action, trick) pair the library can run, by name, in a fixed order (the network's
        // outputs when it was exported; BrainSpec writes them for the trainer).
        public static List<string> AllPairs()
        {
            var pairs = new List<string>();
            foreach (Tactic t in TacticLibrary.All.OrderBy(t => t.Id, System.StringComparer.Ordinal))
                foreach (ActionId a in t.Actions.OrderBy(a => (int)a))
                    pairs.Add(KarenBrain.PickKey(a, t));
            return pairs;
        }

        public static bool KnownOutput(string name)
        {
            int colon = name?.IndexOf(':') ?? -1;
            if (colon <= 0) return false;
            Tactic t = TacticLibrary.Get(name.Substring(colon + 1));
            if (t == null) return false;
            string action = name.Substring(0, colon);
            return t.Actions.Any(a => a.ToString().ToLowerInvariant() == action);
        }

        public struct Pair
        {
            public ActionId Action;
            public Tactic Tactic;
            public string Key => KarenBrain.PickKey(Action, Tactic);
        }

        // The legal pairs now: the action is allowed (the Director's permission, or with pacing off
        // only the fairness rules: never a hunt outside the store), and the trick passes the
        // planner's own gates and its own preconditions.
        public static List<Pair> Legal(KarenContext c)
        {
            var legal = new List<Pair>();
            foreach (ActionId a in actions)
            {
                if (Permission(a, c) <= 0f) continue;
                foreach (Tactic t in HtnPlanner.Candidates(a, c, null)) legal.Add(new Pair { Action = a, Tactic = t });
            }
            return legal;
        }

        // The Director's permission, or with pacing off (Vital, the hunter) only what fairness asks.
        public static float Permission(ActionId a, KarenContext c)
        {
            if (c.Config == null || !c.Config.PacingOff) return c.Director.ActionPermission(a, c);
            return c.Director.FairPermission(a, c);
        }

        // Is the brain asked now? Only at the planner's own decision moments.
        public static bool Asks(bool forced, bool hasPlan, float sinceCommit, float minCommit) =>
            forced || !hasPlan || sinceCommit >= minCommit;

        // A brain's chances over the legal pairs, by name. Null when it has no output for any of them.
        public static float[] Chances(BrainNet net, float[] inputs, IReadOnlyList<Pair> legal)
        {
            if (net == null || legal.Count == 0 || net.Card.OutputNames == null || net.Card.OutputNames.Length == 0) return null;
            float[] logits = net.Logits(inputs)[0];
            string[] names = net.Card.OutputNames[0];
            var index = new Dictionary<string, int>();
            for (int i = 0; i < names.Length; i++) index[names[i]] = i;
            var l = new float[legal.Count];
            var open = new bool[legal.Count];
            bool any = false;
            for (int i = 0; i < legal.Count; i++)
            {
                if (index.TryGetValue(legal[i].Key, out int k)) { l[i] = logits[k]; open[i] = true; any = true; }
            }
            return any ? BrainNet.Softmax(l, open) : null;
        }

        // A draw from the chances, with the brain's own dice (not the planner's: it never moves the
        // planner's stream). Returns the index and its chance.
        public static int Draw(float[] chances, System.Random dice, out float chance)
        {
            chance = 0f;
            if (chances == null) return -1;
            double u = dice.NextDouble(), acc = 0.0;
            int last = -1;
            for (int i = 0; i < chances.Length; i++)
            {
                if (chances[i] <= 0f) continue;
                last = i;
                acc += chances[i];
                if (u < acc) { chance = chances[i]; return i; }
            }
            if (last >= 0) chance = chances[last];
            return last;
        }

        // Whether a brain made for this build can run: its inputs' version, and every output a pair
        // this build knows (a removed trick's output only warns). The message says what's wrong.
        public static bool Accepts(BrainNet net, out string why)
        {
            why = null;
            if (net == null) { why = "no brain"; return false; }
            if (net.Card.InputsVersion != BrainObservation.Version)
            {
                why = $"{net.Card.Name} was trained on inputs version {net.Card.InputsVersion}; this build writes version {BrainObservation.Version}. Retrain or re-export it.";
                return false;
            }
            if (net.InputSize != BrainObservation.Size)
            {
                why = $"{net.Card.Name} takes {net.InputSize} inputs; this build's version {BrainObservation.Version} has {BrainObservation.Size}.";
                return false;
            }
            if (net.Card.OutputNames == null || net.Card.OutputNames.Length == 0) { why = net.Card.Name + " names none of its outputs"; return false; }
            var unknown = net.Card.OutputNames[0].Where(n => !KnownOutput(n)).ToList();
            if (unknown.Count == net.Card.OutputNames[0].Length) { why = $"{net.Card.Name}: none of its outputs is a pair this build has"; return false; }
            if (unknown.Count > 0) why = $"{net.Card.Name}: {unknown.Count} outputs for tricks this build doesn't have are ignored ({string.Join(", ", unknown.Take(3))})";
            return true;
        }

        // The brain spec for the trainer side (export_brain.py --outputs): the inputs' version and
        // size, and the output names in order.
        public static string SpecJson() =>
            "{\"inputs_version\":" + BrainObservation.Version + ",\"input_size\":" + BrainObservation.Size +
            ",\"outputs\":[[" + string.Join(",", AllPairs().Select(MiniJson.Escape)) + "]]}";
    }
}
