using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Aiko
{
    // The fairness contract (AIKO.md §9), enforced in code.
    //
    // Some of the checks need the truth — whether the employee could still finish the
    // shift, whether they can see her path — and this is the one place outside the Director
    // that may consult it. It never feeds anything back into belief or planning; it only
    // says yes or no, and every no is written to the thought log as a CHECK.
    public static class FairnessGuard
    {
        // Rule 3 bookkeeping: when did the current plan last announce itself?
        public static float LastTell { get; private set; } = -999f;
        public static string LastTellKind { get; private set; }
        public static int Violations { get; private set; }
        public static readonly List<string> ViolationLog = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LastTell = -999f;
            LastTellKind = null;
            Violations = 0;
            ViolationLog.Clear();
        }

        public static void NoteTell(TellKind kind, float lead)
        {
            LastTell = Time.time;
            LastTellKind = kind.ToString();
        }

        // Called as a tactic's effect lands. The plan's own most recent tell must have come at
        // least `minLead` seconds before (another tell — a PA chime for something else —
        // neither counts for it nor against it).
        public static bool CheckEffect(string tactic, float planStarted, float planTell, float minLead)
        {
            if (planTell < planStarted)
                return Fail($"{tactic}: effect with no tell in this plan");
            float lead = Time.time - planTell;
            if (lead + 0.02f < minLead)
                return Fail($"{tactic}: tell only {lead:0.00}s before the effect (< {minLead:0.0}s)");
            return true;
        }

        public static bool CheckToldEffect(string what, float lead, float minLead)
        {
            if (lead + 0.02f < minLead)
                return Fail($"{what}: tell only {lead:0.00}s before the effect (< {minLead:0.0}s)");
            return true;
        }

        static bool Fail(string what)
        {
            Violations++;
            if (ViolationLog.Count < 500) ViolationLog.Add($"[{Time.time:0.0}] {what}");
            // Loud, but not a flood: the first few, then one in fifty.
            if (Violations <= 10 || Violations % 50 == 0)
                Debug.LogWarning($"{GameNames.Antagonist} fairness violation #{Violations}: {what}");
            return false;
        }

        // Rule 2: the hint is withheld while the employee can see the path she's walking.
        public static bool PlayerSeesPath(AikoBody body, PlayerPresence player)
        {
            if (body == null || player == null) return false;
            Vector3 eye = player.Head;
            if (Visible(eye, body.Position + Vector3.up * 1.5f)) return true;
            NavMeshPath path = body.Agent.hasPath ? body.Agent.path : null;
            if (path == null) return false;
            foreach (Vector3 corner in path.corners)
                if (Vector3.Distance(corner, eye) < 25f && Visible(eye, corner + Vector3.up * 1.2f)) return true;
            return false;
        }

        static bool Visible(Vector3 from, Vector3 to)
        {
            return !Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                || hit.collider is CharacterController;
        }

        // Rule 6: every unfinished job, and the time clock, must stay reachable from wherever
        // the employee actually is, with `closed` regions sealed.
        public static bool StillWinnable(StoreMap map, ICollection<int> closed, out string why)
        {
            why = null;
            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return true;
            int from = map.RegionAt(player.Position);
            if (from < 0) return true;
            if (closed.Contains(from)) { why = "the employee would be sealed in"; return false; }

            bool[] reach = map.ReachableRegions(from, closed);
            foreach (KeyValuePair<string, Vector3> target in RequiredTargets(map))
            {
                int region = map.RegionAt(target.Value);
                if (region < 0) continue;
                if (!reach[region] || closed.Contains(region))
                {
                    why = $"{target.Key} would be unreachable";
                    return false;
                }
            }
            return true;
        }

        public static bool StillWinnableByNavMesh(out string why)
        {
            why = null;
            PlayerPresence player = PlayerPresence.Current;
            if (player == null) return true;
            if (!NavMesh.SamplePosition(player.Position, out NavMeshHit from, 3f, NavMesh.AllAreas)) return true;

            var path = new NavMeshPath();
            foreach (KeyValuePair<string, Vector3> target in RequiredTargets(StoreMap.Current))
            {
                if (!NavMesh.SamplePosition(target.Value, out NavMeshHit to, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                {
                    why = $"no path to {target.Key}";
                    return false;
                }
            }
            return true;
        }

        // Everything the shift still requires the employee to reach, plus the time clock.
        public static IEnumerable<KeyValuePair<string, Vector3>> RequiredTargets(StoreMap map)
        {
            Landmark? clock = map.FindLandmark(LandmarkKind.TimeClock);
            if (clock.HasValue) yield return new KeyValuePair<string, Vector3>("the time clock", clock.Value.Position);

            foreach (Dirt d in Dirt.All)
                if (d != null) yield return new KeyValuePair<string, Vector3>("a spill", d.transform.position);

            foreach (Bay b in map.Bays)
                if (b.Unit != null && !b.Unit.IsFull) yield return new KeyValuePair<string, Vector3>("an empty bay", b.Position);

            Landmark? pallet = map.FindLandmark(LandmarkKind.StockCrateHome);
            if (pallet.HasValue && ShelfUnit.NotFullCount > 0)
                yield return new KeyValuePair<string, Vector3>("the stock pallet", pallet.Value.Position);

            foreach (Trashcan t in Trashcan.All)
                if (t != null && t.UsageCount > 0) yield return new KeyValuePair<string, Vector3>("a bin", t.transform.position);

            if (TrashBag.ActiveCount > 0)
            {
                Landmark? skip = map.FindLandmark(LandmarkKind.TrashSkip);
                if (skip.HasValue) yield return new KeyValuePair<string, Vector3>("the skip", skip.Value.Position);
            }

            if (CustomerNPC.WaitingCount > 0)
                foreach (Landmark l in map.Landmarks)
                    if (l.Kind == LandmarkKind.Checkout) yield return new KeyValuePair<string, Vector3>(l.Name, l.Position);
        }
    }
}
