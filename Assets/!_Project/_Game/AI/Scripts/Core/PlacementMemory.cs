using System.Collections.Generic;
using System.Linq;

namespace Kehai.Karen
{
    // One place she has used a trick: a camera's slot and angle, a spill's region, the door and
    // side she blocked. `q` is what doing it there paid, between 0 and 1.
    [System.Serializable]
    public class PlaceStat
    {
        public string trick;     // tactic id
        public string key;       // the place, in words the map keeps stable (a region key, a slot)
        public float q;          // what it paid there: mean reward, 0..1
        public float n;          // times she's used it there (fractional after fading)
        public int lastShift;
    }

    // A place a trick could happen, with how good it looks before she's tried it: her guess
    // from your habits and the map (how much of your hiding place a camera there would see, how
    // close a spot is to your route), between 0 and 1.
    public readonly struct PlaceCandidate
    {
        public readonly string Key;
        public readonly float Prior;

        public PlaceCandidate(string key, float prior)
        {
            Key = key;
            Prior = prior;
        }

        public override string ToString() => $"{Key} ({Prior:0.00})";
    }

    // Placement memory (0.3.0 build plan §5): for each trick, a score per place, from how well
    // doing it *there* paid before. She mostly picks the best place she knows, and sometimes
    // tries one she knows least. Places come from her Ledger and the map; this only sorts them.
    //
    //   Score    — her guess (the prior) until she's used a place, then what it paid there,
    //              blended by how often: one try moves it halfway, three most of the way.
    //   Explore  — one pick in `ExploreChance` goes to the candidate she's tried least, so a
    //              place that paid once doesn't shut out the rest.
    //   Fade     — at the shift's end the count of every place she didn't use shrinks, so its
    //              score drifts back toward her guess (fairness rule 7): change your habits
    //              and she loses the place.
    //
    // It's per player, saved in the Ledger file, and keeps the trick and place as text, so the
    // learning plan (PR #76) can pool it across players.
    public sealed class PlacementMemory
    {
        public const float ExploreChance = 0.15f;
        public const float PriorWeight = 1f;     // a guess counts as one use
        public const float FadeCount = 0.6f;     // the count a place keeps over a shift she didn't use it
        public const int MaxPerTrick = 64;       // places kept per trick; the least used go first

        readonly Dictionary<string, PlaceStat> places = new Dictionary<string, PlaceStat>();

        public IEnumerable<PlaceStat> All => places.Values;
        public int Count => places.Count;

        static string Key(string trick, string key) => trick + "@" + key;

        public void Clear() => places.Clear();

        public void Load(IEnumerable<PlaceStat> saved)
        {
            places.Clear();
            if (saved == null) return;
            foreach (PlaceStat p in saved)
                if (p != null && !string.IsNullOrEmpty(p.key) && TacticLibrary.Get(p.trick) != null)
                    places[Key(p.trick, p.key)] = p;
        }

        public PlaceStat Find(string trick, string key) =>
            places.TryGetValue(Key(trick, key), out PlaceStat p) ? p : null;

        // Her score for a place: the guess, until use teaches her better.
        public float Score(string trick, PlaceCandidate place)
        {
            PlaceStat p = Find(trick, place.Key);
            if (p == null || p.n <= 0f) return place.Prior;
            return (place.Prior * PriorWeight + p.q * p.n) / (PriorWeight + p.n);
        }

        // Which candidate to use: the best score, or, now and then, the least tried. -1 for none.
        // `learn` false (rungs below D) uses the guesses alone, and never explores.
        public int Pick(string trick, IReadOnlyList<PlaceCandidate> candidates, KarenRng rng, bool learn = true)
        {
            if (candidates == null || candidates.Count == 0) return -1;
            if (learn && candidates.Count > 1 && rng != null && rng.Chance(ExploreChance))
            {
                float fewest = float.MaxValue;
                var least = new List<int>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    float n = Find(trick, candidates[i].Key)?.n ?? 0f;
                    if (n < fewest - 1e-4f) { fewest = n; least.Clear(); }
                    if (n <= fewest + 1e-4f) least.Add(i);
                }
                return least[rng.Range(0, least.Count)];
            }

            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                float s = learn ? Score(trick, candidates[i]) : candidates[i].Prior;
                if (s > bestScore + 1e-5f) { bestScore = s; best = i; }
            }
            return best;
        }

        // What using a place paid, between 0 and 1.
        public void Settle(string trick, string key, float reward, int shift)
        {
            if (string.IsNullOrEmpty(trick) || string.IsNullOrEmpty(key)) return;
            reward = UnityEngine.Mathf.Clamp01(reward);
            string k = Key(trick, key);
            bool fresh = !places.TryGetValue(k, out PlaceStat p);
            if (fresh)
            {
                p = new PlaceStat { trick = trick, key = key, q = reward, n = 0f };
                places[k] = p;
            }
            p.n += 1f;
            p.q += (reward - p.q) / p.n;
            p.lastShift = shift;
            if (fresh) Trim(trick);
        }

        // Rule 7: a place she hasn't used this shift fades. Its count shrinks, so its score slides
        // back toward her guess; once it's nearly nothing, she forgets it.
        public void Fade(int shift)
        {
            var gone = new List<string>();
            foreach (KeyValuePair<string, PlaceStat> pair in places)
            {
                PlaceStat p = pair.Value;
                if (p.lastShift != shift) p.n *= FadeCount;
                if (p.n < 0.1f) gone.Add(pair.Key);
            }
            foreach (string k in gone) places.Remove(k);
        }

        // Her best places for a trick, for the shift report.
        public IEnumerable<PlaceStat> Best(string trick, int count) =>
            places.Values.Where(p => p.trick == trick && p.n >= 0.5f).OrderByDescending(p => p.q).Take(count);

        void Trim(string trick)
        {
            var mine = places.Values.Where(p => p.trick == trick).ToList();
            if (mine.Count <= MaxPerTrick) return;
            foreach (PlaceStat p in mine.OrderBy(m => m.n).ThenBy(m => m.lastShift).Take(mine.Count - MaxPerTrick))
                places.Remove(Key(p.trick, p.key));
        }
    }
}
