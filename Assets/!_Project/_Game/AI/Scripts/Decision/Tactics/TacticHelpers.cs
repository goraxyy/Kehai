using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // Shared reasoning the tactics lean on. Everything here works from belief and the map,
    // never from where the player actually is.
    public static class TacticHelpers
    {
        public static Vector3 Believed(KarenContext c) => c.Belief.RegionCentroid(LookRegion(c.Map, c.Belief));

        // Where you'll be `ahead` seconds from now, by her reckoning, for a hunt that has lost
        // you: from her last sighting, along the floor the way you were going at the speed you
        // were going, and at each fork the way with more of her belief behind it. Built from
        // what she saw and believes, never from where you are. (Phase 3's route guesses take
        // over from this, C3.1.)
        public static Vector3 Forecast(KarenContext c, float ahead)
        {
            SightSensor s = c.Body.Sight;
            return Forecast(c.Map, c.Belief, s.LastSeenPosition, s.LastSeenVelocity, s.Time_SinceSeen() + ahead);
        }

        public static Vector3 Forecast(StoreMap map, BeliefGrid belief, Vector3 seen, Vector3 velocity, float seconds)
        {
            velocity.y = 0f;
            float speed = Mathf.Min(velocity.magnitude, 7f);
            int cell = map.CellAt(seen);
            if (cell < 0 || speed < 0.3f) return seen;          // standing still: where she saw you

            Vector3 heading = velocity / velocity.magnitude;
            float left = Mathf.Min(speed * seconds, 20f);
            var visited = new HashSet<int> { cell };
            while (left > 0f)
            {
                float most = 0f;
                for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
                    most = Mathf.Max(most, Around(map, belief, map.EdgeTo[e]));

                int next = -1;
                float bestScore = float.MinValue, step = 0f;
                Vector3 dir = heading;
                for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++)
                {
                    int n = map.EdgeTo[e];
                    if (visited.Contains(n) || !map.IsKarens(n)) continue;
                    Vector3 d = map.CellPosition[n] - map.CellPosition[cell];
                    d.y = 0f;
                    float length = d.magnitude;
                    if (length < 1e-3f) continue;
                    d /= length;
                    float along = Vector3.Dot(d, heading);
                    if (along < 0.3f) continue;                   // only onward
                    float score = along + (most > 0f ? 0.5f * Around(map, belief, n) / most : 0f);
                    if (score > bestScore) { bestScore = score; next = n; step = map.EdgeLength[e]; dir = d; }
                }
                if (next < 0) break;                              // a dead end: you're here somewhere
                visited.Add(next);
                left -= step;
                heading = (heading + dir * 0.35f).normalized;     // follow the floor round a bend
                cell = next;
            }
            return map.CellPosition[cell];
        }

        // Her belief in a cell and the cells beside it.
        static float Around(StoreMap map, BeliefGrid belief, int cell)
        {
            float sum = belief[cell];
            for (int e = map.EdgeStart[cell]; e < map.EdgeStart[cell + 1]; e++) sum += belief[map.EdgeTo[e]];
            return sum;
        }

        // Whether a region is in her part of the building (StoreMap.KarenWorksIn).
        public static bool Hers(StoreMap map, Region r) => r.Cells.Count > 0 && map.IsKarens(r.Cells[0]);

        // The region to look for you in: the likeliest one of hers. When most of her belief is
        // outside, it's the way out you most likely took — she can't follow you out there, so
        // she goes to the door you'll come back through.
        public static int LookRegion(StoreMap map, BeliefGrid belief)
        {
            Region peak = map.Regions[belief.PeakRegion];
            if (Hers(map, peak)) return peak.Id;
            int exit = belief.LikeliestExit();
            if (exit >= 0)
            {
                int region = map.RegionAt(belief.ExitPosition(exit));
                if (region >= 0) return region;
            }
            int best = -1;
            float mass = -1f;
            foreach (Region r in map.Regions)
                if (Hers(map, r) && belief.RegionMass[r.Id] > mass) { mass = belief.RegionMass[r.Id]; best = r.Id; }
            return best >= 0 ? best : peak.Id;
        }

        public static Vector3 OnFloor(Vector3 p, float radius = 3f) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, radius, NavMesh.AllAreas) ? hit.position : p;

        // Walking distance field from the believed position — "how far is this from where
        // she thinks you are". Cached per decision.
        static float[] fromBelief;
        static int fromBeliefCell = -1;
        static float fromBeliefTime = -1f;

        public static float[] DistanceFromBelief(KarenContext c)
        {
            int cell = c.Belief.PeakCell;
            if (fromBelief == null || fromBeliefCell != cell || Time.time - fromBeliefTime > 1f || fromBelief.Length != c.Map.CellCount)
            {
                fromBelief = c.Map.Distances(cell, float.MaxValue, fromBelief);
                fromBeliefCell = cell;
                fromBeliefTime = Time.time;
            }
            return fromBelief;
        }

        public static float WalkFromBelief(KarenContext c, Vector3 p)
        {
            int cell = c.Map.CellAt(p);
            if (cell < 0) return 0f;
            float d = DistanceFromBelief(c)[cell];
            return float.IsInfinity(d) ? 0f : d;
        }

        // A dark, quiet spot not far from the body — where she goes after doing something
        // she didn't want to be seen doing.
        public static Vector3 Retreat(KarenContext c, float within = 22f)
        {
            Vector3 body = c.Body.Position;
            Vector3 best = body;
            float bestScore = float.MinValue;
            foreach (Region r in c.Map.Regions)
            {
                if (r.IsDoor || r.Cells.Count < 3 || !Hers(c.Map, r)) continue;
                float d = Vector3.Distance(r.Centroid, body);
                if (d > within || d < 5f) continue;
                float score = WalkFromBelief(c, r.Centroid) * 0.1f
                            - LightProbe.LevelAt(r.Centroid) * 3f
                            - c.Belief.RegionMass[r.Id] * 20f
                            + c.Rng.Value * 0.5f;
                if (score > bestScore) { bestScore = score; best = r.Centroid; }
            }
            return OnFloor(best);
        }

        // ---- task progress and the "never deny a nearly finished task" rule ----------

        // True when sabotaging `target` would break rule 5 (Karen.md §9.5): the task is past
        // the configured completion *and* the target is the one she believes you're finishing.
        public static bool DenyVetoed(KarenContext c, TaskManager.TaskKind kind, Vector3 target, out string check)
        {
            float progress = c.Brain.TaskProgress(kind);
            float fromPlayer = WalkFromBelief(c, target);
            bool working = fromPlayer < 10f && c.A.Confidence > 0.15f;
            if (progress > c.Config.denyMaxCompletion && working && !c.Brain.FinalShiftException)
            {
                check = $"fairness rule 5 — {kind} at {progress:P0} > {c.Config.denyMaxCompletion:P0}, target is the one being finished → VETOED";
                return true;
            }
            check = null;
            return false;
        }

        // A full bay far (by walking) from where she believes you are — so fixing it costs
        // maximum walking — and not one she'd be seen stripping.
        public static ShelfUnit BayToSweep(KarenContext c, out string check)
        {
            check = null;
            ShelfUnit best = null;
            float bestScore = float.MinValue;
            foreach (Bay b in c.Map.Bays)
            {
                if (b.Unit == null || !b.Unit.IsFull) continue;
                // Measured from where you'd stand to restock it, not from the bay's transform,
                // which sits inside the shelving and can be nearer the far side of a wall.
                Vector3 stand = StandIn(b.Unit);
                if (StoreMap.AreaAt(stand) != "Sales floor") continue;

                float walk = WalkFromBelief(c, stand);
                if (walk < 12f) continue;
                float fromBody = Vector3.Distance(stand, c.Body.Position);
                float score = walk - fromBody * 0.35f + c.Rng.Value * 3f;
                if (score <= bestScore) continue;

                if (DenyVetoed(c, TaskManager.TaskKind.Stock, stand, out string why)) { check = why; continue; }
                bestScore = score;
                best = b.Unit;
            }
            return best;
        }

        // Where you stand to use a bay: its ShelfPoint, which sits on the aisle side. The bay's
        // own transform is inside the shelving and can be nearer the far side of a wall.
        public static Vector3 StandIn(ShelfUnit bay)
        {
            Transform point = SceneObjectNames.Child(bay.transform, "ShelfPoint");
            return OnFloor(point != null ? point.position : bay.transform.position + bay.transform.forward * -1.2f);
        }

        // A point to watch the believed position from: in line of sight, at the far end of
        // an aisle, 8–14 m off.
        public static bool Vantage(KarenContext c, out Vector3 vantage)
        {
            Vector3 target = Believed(c) + Vector3.up * 1.2f;
            vantage = c.Body.Position;
            float best = float.MaxValue;
            var cells = new List<int>();
            c.Map.CellsWithin(target, 14f, cells);
            for (int i = 0; i < cells.Count; i += 3)
            {
                if (!c.Map.IsKarens(cells[i])) continue;
                Vector3 p = c.Map.CellPosition[cells[i]];
                float d = Vector3.Distance(p, target);
                if (d < 8f) continue;
                Vector3 eye = p + Vector3.up * 1.8f;
                if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponent<CharacterController>() == null) continue;
                float score = Vector3.Distance(p, c.Body.Position) + Mathf.Abs(d - 11f) * 2f;
                if (score < best) { best = score; vantage = p; }
            }
            return best < float.MaxValue;
        }

        public static CustomerNPC CustomerNear(KarenContext c, Vector3 p, float maxDistance, bool excludeQueued = true)
        {
            CustomerNPC best = null;
            float bestSqr = maxDistance * maxDistance;
            foreach (CustomerNPC npc in CustomerNPC.All)
            {
                if (npc == null) continue;
                if (excludeQueued && npc.IsWaitingToBeServed) continue;
                if (npc.GetComponent<Possession>() != null) continue;
                var req = npc.GetComponent<CustomerRequest>();
                if (req != null && req.CurrentStage != CustomerRequest.Stage.None) continue;
                float d = (npc.transform.position - p).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = npc; }
            }
            return best;
        }

        public static string PlayerName(KarenContext c) => c.Ledger.PlayerName;
    }
}
