using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;

namespace Kehai.Karen
{
    // Cut (0.2.0, a trick of Hunt's): with you lost and her guesses about your route in hand
    // (RoutePredictor), she doesn't run after you. She runs, by her own shorter way, to a spot on
    // your likeliest routes that she can reach at least a second before you can, and waits there
    // through your window. A doorway several routes share beats a spot on one route.
    public sealed class CutTactic : Tactic
    {
        public override string Id => "cut";
        public override string Title => "Cut";
        public override ActionId[] Actions => new[] { ActionId.Hunt };
        public override int Tier => 1;
        public override int IntroducedShift => 1;
        public override float TensionCost => 0f;      // a hunt's cost is the Hunt action's
        public override float Cooldown => 0f;
        public override float PanicPrior => 0.3f;
        public override TellKind Tell => TellKind.Footsteps;
        public override string Chore => "don't go where she expects: turn back, or take the long way";
        public override string Attacks => "you";

        CutSpot spot;

        public override bool Available(KarenContext c, out string why)
        {
            why = null;
            RoutePredictor p = c.Brain != null ? c.Brain.Predictor : null;
            if (p == null || !p.Active) { why = "no guesses about your route"; return false; }
            if (c.Body.Sight.SeesNow) { why = "she can see you: no need to cut you off"; return false; }
            if (!ChooseSpot(c.Map, p, c.Body.Position, c.Config.runSpeed, Time.time, out spot))
            {
                why = "nowhere on your routes she can reach first";
                return false;
            }
            return true;
        }

        public override void Plan(KarenContext c, PlanBuilder plan)
        {
            plan.Target = $"{c.RegionName(c.Map.CellRegion[spot.Cell])} ({spot.Name}, {spot.Weight:P0})";
            plan.Add(new CutRun());
        }

        // ---- where to cut you off ----------------------------------------------------------

        public struct CutSpot
        {
            public int Cell;
            public float Weight;          // the guesses' weight that passes here
            public float YouEarliest;     // seconds from now: the soonest you could be here
            public float YouLatest;       // seconds after she lost you: the latest, crouching
            public float Her;             // seconds from now: when she'd be here
            public string Name;           // the likeliest guess through it
        }

        static float[] fromHer;

        // A guess needs this much weight before she'll cut you off on it.
        public const float MinBelief = 0.3f;

        // The spot on her best guess's route that she can reach at least a second before your
        // earliest, with the most of all the guesses' weight through it (a doorway several routes
        // share beats a spot on one), and on a tie the soonest. Only on the best guess: a spot she
        // could reach first on a route she doubts sent her the wrong way (the first test run). Pure
        // apart from a distance cache, so the choice can be tested without a body.
        public static bool ChooseSpot(StoreMap map, RoutePredictor p, Vector3 her, float herSpeed, float now, out CutSpot spot)
        {
            spot = default;
            if (p == null || !p.Active || herSpeed <= 0f) return false;
            int from = map.CellAt(her);
            if (from < 0) return false;
            RouteGuess best = null;
            foreach (RouteGuess g in p.Guesses)
                if (g.Kind != GuessKind.Hiding && (best == null || g.Weight > best.Weight)) best = g;
            if (best == null || best.Weight < MinBelief) return false;
            var onBest = new HashSet<int>(best.Path);

            fromHer = map.Distances(from, herSpeed * 9f, fromHer, TacticHelpers.NotHers(map));
            float since = now - p.SeenAt;

            var spots = new Dictionary<int, CutSpot>();
            foreach (RouteGuess g in p.Guesses)
            {
                if (g.Kind == GuessKind.Hiding) continue;
                for (int i = 1; i < g.Path.Length; i++)
                {
                    var (earliest, _, latest) = RoutePredictor.Window(g, i);
                    float you = earliest - since;
                    if (you <= 0.5f || you > 8f) continue;              // past it already, or too far ahead to race to
                    int cell = g.Path[i];
                    if (!onBest.Contains(cell)) continue;
                    float d = fromHer[cell];
                    if (float.IsInfinity(d)) continue;
                    float herT = d / herSpeed;
                    if (herT + 1f > you) continue;                       // she wouldn't be there first

                    spots.TryGetValue(cell, out CutSpot s);
                    if (s.Name == null) s = new CutSpot { Cell = cell, YouEarliest = you, YouLatest = latest, Her = herT, Name = g.Name };
                    s.Weight += g.Weight;
                    s.YouEarliest = Mathf.Min(s.YouEarliest, you);
                    s.YouLatest = Mathf.Max(s.YouLatest, latest);
                    spots[cell] = s;
                }
            }

            bool found = false;
            foreach (CutSpot s in spots.Values)
            {
                if (!found || s.Weight > spot.Weight + 1e-4f || Mathf.Abs(s.Weight - spot.Weight) <= 1e-4f && s.YouEarliest < spot.YouEarliest)
                {
                    spot = s;
                    found = true;
                }
            }
            return found;
        }
    }

    // Cut's run: to the spot, by her shortest way, then still there through your window. Seeing
    // you again hands the hunt back to its run at you; so does the window closing with no sign
    // of you, a recovery, or you outside.
    public sealed class CutRun : Primitive
    {
        float retarget, t;
        CutTactic.CutSpot spot;
        bool have;

        public CutRun() => Label = "cut";

        protected override Status Tick(KarenContext c, float dt)
        {
            t += dt;
            if (c.Body.Sight.SeesNow) return Status.Success;       // she has you again: Hunt runs at you

            RoutePredictor p = c.Brain.Predictor;
            if (p == null || !p.Active) return Status.Failure;
            if (c.Director.InRecovery || c.Belief.OutsideMass > 0.5f) return Status.Failure;

            retarget -= dt;
            if (retarget <= 0f)
            {
                retarget = 0.25f;
                if (CutTactic.ChooseSpot(c.Map, p, c.Body.Position, c.Config.runSpeed, Time.time, out CutTactic.CutSpot next))
                {
                    spot = next;
                    have = true;
                }
                if (have) c.Body.MoveTo(c.Map.CellPosition[spot.Cell], KarenBody.Pace.Run);
            }
            if (!have) return Status.Failure;

            Vector3 d = c.Body.Position - c.Map.CellPosition[spot.Cell];
            d.y = 0f;
            if (d.magnitude < 1.5f) c.Body.Stop();                   // there first: wait for you

            float since = Time.time - p.SeenAt;
            if (since > spot.YouLatest + 1f)
            {
                c.Think("HUNT", "HUNT     your window at the cut closed and you didn't come");
                return Status.Failure;
            }
            return t > 20f ? Status.Failure : Status.Running;
        }
    }
}
