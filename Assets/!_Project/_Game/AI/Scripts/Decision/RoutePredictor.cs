using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    public enum GuessKind { Goal, TurnedBack, Hiding }

    // One route you might be taking, from where she last saw you (0.2.0 build plan, D1).
    public sealed class RouteGuess
    {
        public string Name;          // "to the time clock", "turned back", "hiding close by"
        public GuessKind Kind;
        public int[] Path;           // her cells, from where she last saw you to the goal
        public float[] Along;        // metres along the path to each of them
        public float Prior;          // before any evidence
        public float Weight;         // now: all the guesses' weights add up to 1
        public float Length => Along.Length > 0 ? Along[Along.Length - 1] : 0f;
    }

    // Where you're going, once a hunt has lost you: a handful of guesses about your route,
    // each with a weight and, for every spot on it, a window of when you could be there.
    //
    //   The guesses — your next task, the ways out, the time clock, the breaker box when the
    //   lights are out, and always "turned back" and "hiding close by" — are routes on her part
    //   of the map from where she last saw you. Each starts with a prior: how well it fits the
    //   way you were heading, and how far it goes.
    //
    //   Depth is a time window. A spot `s` metres along a route is reached at the earliest at
    //   your sprint (s / 6.5 m/s), most likely at your walk (s / 4), at the latest crouching
    //   (s / 2.5).
    //
    //   Implication is Bayes' rule with her belief grid as the evidence: a guess keeps its
    //   weight only where her grid still believes you could be, on that route, inside that
    //   window. The grid already learns from where she looked and found nothing, so "she was
    //   there in time and you didn't come" lowers that guess, and the others gain.
    //
    // Built only from what she saw, believes and knows of her own store. Never from where you
    // are. Phase 3 of the build plan; Cut, Ambush and Hunt's run read it.
    public sealed class RoutePredictor
    {
        public const float Sprint = 6.5f, Walk = 4f, Crouch = 2.5f;
        const int MaxGoalGuesses = 4;
        const float Ahead = 8f;      // the furthest it looks: past that, guesses spread too wide to race to

        readonly StoreMap map;
        readonly bool[] notHers;
        readonly List<RouteGuess> guesses = new List<RouteGuess>();
        float[] fromSeen;

        public IReadOnlyList<RouteGuess> Guesses => guesses;
        public bool Active => guesses.Count > 0;
        public Vector3 Seen { get; private set; }
        public float SeenAt { get; private set; } = -999f;
        public RouteGuess Best
        {
            get
            {
                RouteGuess best = null;
                foreach (RouteGuess g in guesses) if (best == null || g.Weight > best.Weight) best = g;
                return best;
            }
        }

        public RoutePredictor(StoreMap map)
        {
            this.map = map;
            notHers = new bool[map.CellCount];
            for (int c = 0; c < notHers.Length; c++) notHers[c] = !map.IsKarens(c);
        }

        public void Clear()
        {
            guesses.Clear();
            SeenAt = -999f;
        }

        // ---- the guesses ---------------------------------------------------------------------

        // Builds the guesses from a sighting: where and when she last saw you, which way you
        // were going, and the places you might be going to.
        // `modelPrior` (0.4.0, C4.5): each guess's chance from the prediction model instead of the
        // hand-set heading fit; given (the goal's name, where it is, its kind), null keeps today's.
        public void Build(Vector3 seen, Vector3 velocity, float seenAt, IEnumerable<(string name, Vector3 at)> goals,
                          System.Func<string, Vector3, GuessKind, float> modelPrior = null)
        {
            guesses.Clear();
            Seen = seen;
            SeenAt = seenAt;
            int start = map.CellAt(seen);
            if (start < 0 || !map.IsKarens(start)) return;

            fromSeen = map.Distances(start, float.MaxValue, fromSeen, notHers);
            velocity.y = 0f;
            Vector3 heading = velocity.sqrMagnitude > 0.09f ? velocity.normalized : Vector3.zero;

            var candidates = new List<RouteGuess>();
            var taken = new HashSet<int>();
            foreach (var (name, at) in goals)
            {
                int goal = map.CellAt(at);
                if (goal < 0 || !map.IsKarens(goal) || float.IsInfinity(fromSeen[goal]) || !taken.Add(map.CellRegion[goal])) continue;
                RouteGuess g = Route(start, goal, GuessKind.Goal, name);
                if (g == null) continue;
                g.Prior = modelPrior != null ? modelPrior(name, at, GuessKind.Goal) : HeadingFit(g, heading) / (1f + g.Length / 40f);
                candidates.Add(g);
            }
            candidates.Sort((a, b) => b.Prior.CompareTo(a.Prior));
            for (int i = 0; i < candidates.Count && i < MaxGoalGuesses; i++) guesses.Add(candidates[i]);

            // Turned back: the way you came, as far as you'd get in a few seconds.
            if (heading != Vector3.zero)
            {
                Vector3 back = TacticHelpers.WalkAlong(map, seen, -heading, Walk * 4f);
                int backCell = map.CellAt(back);
                RouteGuess turned = backCell >= 0 && backCell != start ? Route(start, backCell, GuessKind.TurnedBack, "turned back") : null;
                if (turned != null)
                {
                    turned.Prior = modelPrior != null ? modelPrior(turned.Name, back, GuessKind.TurnedBack) : 0.15f;
                    guesses.Add(turned);
                }
            }

            // Hiding close by: you didn't go anywhere.
            guesses.Add(new RouteGuess { Name = "hiding close by", Kind = GuessKind.Hiding, Path = new[] { start }, Along = new[] { 0f },
                                         Prior = modelPrior != null ? modelPrior("hiding close by", seen, GuessKind.Hiding) : 0.1f });

            float sum = 0f;
            foreach (RouteGuess g in guesses) sum += g.Prior;
            if (sum <= 0f) { foreach (RouteGuess g in guesses) g.Prior = 1f; sum = guesses.Count; }
            foreach (RouteGuess g in guesses) g.Weight = g.Prior / sum;
        }

        // The shortest walk from `start` to `goal` over her cells, read back off the distance
        // field from the sighting.
        RouteGuess Route(int start, int goal, GuessKind kind, string name)
        {
            var path = new List<int> { goal };
            int cell = goal;
            int guard = map.CellCount;
            while (cell != start && guard-- > 0)
            {
                int next = -1;
                float best = fromSeen[cell];
                for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
                {
                    int n = map.EdgeTo[e];
                    if (fromSeen[n] < best) { best = fromSeen[n]; next = n; }
                }
                if (next < 0) return null;
                path.Add(next);
                cell = next;
            }
            path.Reverse();
            var along = new float[path.Count];
            for (int i = 0; i < path.Count; i++) along[i] = fromSeen[path[i]];
            return new RouteGuess { Name = name, Kind = kind, Path = path.ToArray(), Along = along };
        }

        // How well a route fits the way you were heading, over its first few metres.
        float HeadingFit(RouteGuess g, Vector3 heading)
        {
            if (heading == Vector3.zero || g.Path.Length < 2) return 0.5f;
            int i = 1;
            while (i < g.Path.Length - 1 && g.Along[i] < 6f) i++;
            Vector3 d = map.CellPosition[g.Path[i]] - map.CellPosition[g.Path[0]];
            d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return 0.5f;
            float dot = Vector3.Dot(d.normalized, heading);
            float fit = (1f + dot) * 0.5f;
            return Mathf.Max(0.02f, fit * fit);
        }

        // ---- implication ---------------------------------------------------------------------

        // Each guess's weight: its prior, times how much of her belief lies where the guess
        // says you could be now — on its route, between your crouch and your sprint since she
        // lost you. Called four times a second during a hunt.
        public void Reweigh(BeliefGrid belief, float now)
        {
            if (!Active) return;
            float t = Mathf.Max(0f, now - SeenAt);
            float sum = 0f;
            foreach (RouteGuess g in guesses)
            {
                float near = Mathf.Min(Crouch * t, g.Length), far = Mathf.Min(Sprint * t, g.Length);
                float mass = 0f;
                for (int i = 0; i < g.Path.Length; i++)
                    if (g.Along[i] >= near - 1.5f && g.Along[i] <= far + 1.5f) mass += Around(belief, g.Path[i]);
                g.Weight = g.Prior * (mass + 1e-6f);
                sum += g.Weight;
            }
            if (sum <= 0f) return;
            foreach (RouteGuess g in guesses) g.Weight /= sum;
        }

        float Around(BeliefGrid belief, int cell)
        {
            float sum = belief[cell];
            for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++) sum += belief[map.EdgeTo[e]];
            return sum;
        }

        // ---- windows and positions -------------------------------------------------------

        // When you could be at the spot `i` along a guess, in seconds after she lost you:
        // earliest at a sprint, likely at a walk, latest crouching.
        public static (float earliest, float likely, float latest) Window(RouteGuess g, int i)
        {
            float s = g.Along[i];
            return (s / Sprint, s / Walk, s / Crouch);
        }

        // Where a guess puts you `seconds` after she lost you, at `speed`, looking at most Ahead
        // seconds further than now.
        public Vector3 PositionOn(RouteGuess g, float seconds, float speed)
        {
            float s = Mathf.Min(speed * seconds, g.Length);
            int i = 0;
            while (i < g.Path.Length - 1 && g.Along[i + 1] <= s) i++;
            return map.CellPosition[g.Path[i]];
        }

        // Where the best guess puts you `ahead` seconds from now, at the speed she last saw
        // you going (a walk at least); with no guesses, where she last saw you.
        public Vector3 Expected(float now, float ahead, float lastSpeed)
        {
            RouteGuess best = Best;
            if (best == null) return Seen;
            float speed = Mathf.Clamp(lastSpeed, Walk, Sprint);
            return PositionOn(best, Mathf.Min(now - SeenAt + ahead, now - SeenAt + Ahead), speed);
        }
    }
}
