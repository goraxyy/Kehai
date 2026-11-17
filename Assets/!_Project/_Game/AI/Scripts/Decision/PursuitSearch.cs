using System.Collections.Generic;
using UnityEngine;

namespace Kehai.Karen
{
    // Where the hunt searches next once a guess turned out empty (0.5.1, item 13). In order:
    //   1. the newest noise she heard since she last saw you, not yet searched;
    //   2. the end of each of her route guesses (RoutePredictor), likeliest first, not yet searched;
    //   3. where she last saw you, if she hasn't searched there;
    //   4. her belief's likeliest place, even if she looked there already.
    // Every input is hers: her hearing, her guesses, her sighting, her belief. Pure, so the
    // order can be tested.
    public static class PursuitSearch
    {
        public struct Spot
        {
            public Vector3 At;
            public string Why;
            public bool Fresh;     // a noise from the last few seconds: worth running to
        }

        public const float FreshNoise = 3f;
        public const float MinGuessWeight = 0.05f;

        public static Spot Choose(PostSightPursuit p, IList<(string name, Vector3 end, float weight)> guesses,
                                  Vector3 lastSeen, Vector3 beliefPeak, float now)
        {
            IReadOnlyList<(float t, Vector3 at)> noises = p.Noises;
            for (int i = noises.Count - 1; i >= 0; i--)
            {
                float age = now - noises[i].t;
                if (age > PostSightPursuit.NoiseMemory || p.WasSearched(noises[i].at)) continue;
                return new Spot { At = noises[i].at, Why = $"a noise {age:0.0}s ago", Fresh = age <= FreshNoise };
            }

            int best = -1;
            if (guesses != null)
            {
                for (int i = 0; i < guesses.Count; i++)
                {
                    if (guesses[i].weight < MinGuessWeight || p.WasSearched(guesses[i].end)) continue;
                    if (best < 0 || guesses[i].weight > guesses[best].weight) best = i;
                }
            }
            if (best >= 0) return new Spot { At = guesses[best].end, Why = $"the end of her guess '{guesses[best].name}' ({guesses[best].weight:P0})" };

            if (!p.WasSearched(lastSeen)) return new Spot { At = lastSeen, Why = "where she last saw you" };
            return new Spot { At = beliefPeak, Why = "her belief's likeliest place" };
        }

        // The end of each route guess, from the predictor's cells.
        public static List<(string name, Vector3 end, float weight)> Ends(RoutePredictor predictor, Kehai.Store.StoreMap map)
        {
            var ends = new List<(string, Vector3, float)>();
            if (predictor == null || !predictor.Active || map == null) return ends;
            foreach (RouteGuess g in predictor.Guesses)
            {
                if (g.Path == null || g.Path.Length == 0) continue;
                int cell = g.Path[g.Path.Length - 1];
                if (cell < 0 || cell >= map.CellCount) continue;
                ends.Add((g.Name, map.CellPosition[cell], g.Weight));
            }
            return ends;
        }
    }
}
