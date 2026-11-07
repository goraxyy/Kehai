using System.Collections.Generic;
using Kehai.Store;
using UnityEngine;
using UnityEngine.AI;

namespace Kehai.Karen
{
    // The fairness contract (Karen.md §9), enforced in code. Rule 3, "every trick warns you",
    // was removed in 0.2.0: its timing checks are gone, and the audit keeps the pacing. Rule 9,
    // "hands-on tricks happen at her hands", came in 0.3.0.
    //
    // Some of the checks need the truth — whether the employee could still finish the
    // shift, whether they can see her path — and this is the one place outside the Director
    // that may consult it. It never feeds anything back into belief or planning; it only
    // says yes or no, and every no is written to the thought log as a CHECK.
    public static class FairnessGuard
    {
        // When a trick last sounded, for the log and the replays (no longer a rule).
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

        // Rule 4: no tactic above tier 1 while the employee is settling into the shift or
        // recovering from a chase. The Director's gate already keeps every rung inside this;
        // the check is for rung F with its shield off, where a trained policy may try.
        public static bool CheckPacing(Tactic tactic, KarenDirector director)
        {
            if (tactic.Tier < 2) return true;
            if (director.InRecovery)
                return Fail($"{tactic.Id}: tier {tactic.Tier} during the recovery window");
            if (director.CurrentPhase == KarenDirector.Phase.Settle)
                return Fail($"{tactic.Id}: tier {tactic.Tier} while the employee is settling in");
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

        // Rule 9: how far a hands-on trick may land from her body. Measured across the floor, so
        // a shelf's top slot or a bin's lid counts from where she stands.
        public const float HandsOnReach = 2f;

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // The audit for rule 9. The HandsOn step already refuses an effect out of reach; this
        // catches anything that would land one some other way. True when it's fair; a
        // violation, logged, when it isn't. No body (a test, the replay) has nothing to check.
        public static bool CheckHandsOn(string what, KarenBody body, Vector3 spot)
        {
            if (body == null) return true;
            float d = Flat(body.Position, spot);
            if (d <= HandsOnReach + 0.05f) return true;
            return Fail($"{what}: landed {d:0.0} m from her (rule 9, hands-on, allows {HandsOnReach:0} m)");
        }

        // Rule 2: the hint is withheld while the employee can see the path she's walking.
        public static bool PlayerSeesPath(KarenBody body, PlayerPresence player)
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

            // A tool she hid has to be carried back to its rack (0.3.0).
            foreach (ToolSnapPoint rack in ToolSnapPoint.TakenRacks)
                if (rack != null && rack.tool != null)
                {
                    yield return new KeyValuePair<string, Vector3>("the " + rack.ToolName, rack.tool.transform.position);
                    yield return new KeyValuePair<string, Vector3>("the " + rack.ToolName + "'s rack", rack.transform.position);
                }

            if (CustomerNPC.WaitingCount > 0)
                foreach (Landmark l in map.Landmarks)
                    if (l.Kind == LandmarkKind.Checkout) yield return new KeyValuePair<string, Vector3>(l.Name, l.Position);
        }
    }
}
